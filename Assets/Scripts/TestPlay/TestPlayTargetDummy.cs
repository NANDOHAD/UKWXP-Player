using UnityEngine;

public class TestPlayTargetDummy : MonoBehaviour
{
    public float hp = 1000f;
    public float hitRadius = 1.5f;
    public int stateId = 1;
    public bool logHits = true;
    [Tooltip("原作ShildGuard値。実ANIでは0/1/2を使用します。")]
    [Range(0, 255)]
    public int shildGuard;
    [Tooltip("原作@int[157] / c44。ガード・反射時は20になり、60 Hz tickごとに減算します。")]
    [Min(0)]
    public int guardHitTimerTicks;
    [Tooltip("原作@int[158] / c40。非ゼロ中は被弾を受け付けません。")]
    [Min(0)]
    public int hitAcceptanceBlockTicks;
    [Tooltip("原作c50。type 57の第6引数から設定されるヒットストップです。")]
    [Min(0)]
    public int hitStopTicks;
    public Vector3 lastImpactForce;
    public int lastDownValue;
    public int lastAttackFlag;
    public TestPlayCombatHitDecision lastHitDecision;
    public bool linkedTargetClearedByAttack;
    public bool facingForcedByAttack;

    public bool IsAlive => hp > 0f;

    public void ApplyDamage(float damage, Vector3 hitPoint, string source)
    {
        if (damage <= 0f)
            return;

        hp = Mathf.Max(0f, hp - damage);
        stateId = hp > 0f ? 3 : 6;

        if (logHits)
            Debug.Log($"[TestPlayTargetDummy] Hit {damage} from {source}. HP={hp}");
    }

    public void ApplyImpact(float damage, Vector3 impactForce, int downValue, string source)
    {
        lastImpactForce = impactForce;
        lastDownValue = downValue;
        ApplyDamage(damage, transform.position, source);
    }

    public TestPlayCombatHitResult ResolveImpact(
        TestPlayCombatHitResult hit,
        Vector3 attackerPosition,
        bool targetIsAttackOwner,
        bool sourceIsCharacter,
        int meleeHitStopTicks,
        int reflectionProbabilityPercent = 0,
        int reflectionRoll = 0)
    {
        Vector3 toAttacker = Vector3.ProjectOnPlane(attackerPosition - transform.position, Vector3.up);
        Vector3 defenderForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        float forwardDot = toAttacker.sqrMagnitude > 0.000001f &&
                           defenderForward.sqrMagnitude > 0.000001f
            ? Vector3.Dot(defenderForward.normalized, toAttacker.normalized)
            : -1f;
        TestPlayDefenseHitResult defense = TestPlayCombatCore.ResolveDefenseHit(
            new TestPlayDefenseHitInput
            {
                collisionKind = hit.collisionKind,
                attackFlag = hit.attackFlag,
                targetIsAttackOwner = targetIsAttackOwner,
                shieldGuardValue = shildGuard,
                defenderForwardDotToAttacker = forwardDot,
                hitAcceptanceBlockTicks = hitAcceptanceBlockTicks,
                reflectionProbabilityPercent = reflectionProbabilityPercent,
                reflectionRoll = reflectionRoll,
                sourceIsCharacter = sourceIsCharacter,
                meleeHitStopTicks = meleeHitStopTicks
            });

        lastAttackFlag = hit.attackFlag;
        lastHitDecision = defense.decision;
        hit.decision = defense.decision;
        hit.reactionState = defense.reactionState;
        hit.guardHitTimerTicks = defense.guardHitTimerTicks;
        hit.hitStopTicks = defense.defenderHitStopTicks;
        hit.clearLinkedTarget = defense.clearLinkedTarget;
        hit.forceFacingToAttacker = defense.forceFacingToAttacker;
        hit.attackerGuardReactionTicks = defense.attackerGuardReactionTicks;
        hit.applyAttackerGuardRecoil = defense.applyAttackerGuardRecoil;

        if (defense.guardHitTimerTicks > 0)
            guardHitTimerTicks = Mathf.Max(guardHitTimerTicks, defense.guardHitTimerTicks);
        if (defense.defenderHitStopTicks > 0)
            hitStopTicks = Mathf.Max(hitStopTicks, defense.defenderHitStopTicks);
        if (defense.decision != TestPlayCombatHitDecision.Damaged)
            return hit;

        lastImpactForce = hit.impactForce;
        lastDownValue = hit.down;
        linkedTargetClearedByAttack = defense.clearLinkedTarget;
        facingForcedByAttack = defense.forceFacingToAttacker;
        int stateBeforeHit = stateId;
        ApplyDamage(hit.damage, transform.position, hit.source);
        if (hp > 0f)
            stateId = defense.reactionState != 0 ? defense.reactionState : stateBeforeHit;
        return hit;
    }

    public void SimulateOriginalCombatTimerTick()
    {
        guardHitTimerTicks = TestPlayCombatCore.TickPositiveTimer(guardHitTimerTicks);
        hitAcceptanceBlockTicks = TestPlayCombatCore.TickPositiveTimer(hitAcceptanceBlockTicks);
        hitStopTicks = TestPlayCombatCore.TickPositiveTimer(hitStopTicks);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, hitRadius);
    }
}
