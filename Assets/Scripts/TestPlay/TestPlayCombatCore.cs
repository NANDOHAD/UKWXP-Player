using System;
using UnityEngine;

public enum TestPlayCombatValueSource
{
    Unknown,
    OriginalScriptProfile,
    UnityFallback
}

public enum TestPlayCombatDecisionReason
{
    None,
    ComboQueued,
    SwordCancel,
    MeleeApproachFollowup,
    LocomotionCancel,
    AttackFinished
}

public enum TestPlayAttackCollisionKind
{
    Unspecified,
    OriginalType1,
    OriginalType11,
    OriginalType57
}

public enum TestPlayCombatHitDecision
{
    None,
    Ignored,
    Damaged,
    Guarded,
    Reflected,
    Invulnerable
}

public enum TestPlaySessionReactionPhase
{
    None,
    Hit,
    Knockback,
    Downed,
    Recovering,
    Defeated
}

public struct TestPlaySessionReactionInput
{
    public int accumulatedDown;
    public int incomingDown;
    public int reactionState;
    public float hpAfter;
    public bool airborne;
}

public struct TestPlaySessionReactionDecision
{
    public int accumulatedDown;
    public bool startsReaction;
    public bool knockdown;
    public bool defeated;
    public int actionId;
    public TestPlaySessionReactionPhase phase;
}

public struct TestPlayDefenseHitInput
{
    public TestPlayAttackCollisionKind collisionKind;
    public int attackFlag;
    public bool targetIsAttackOwner;
    public int shieldGuardValue;
    public float defenderForwardDotToAttacker;
    public int hitAcceptanceBlockTicks;
    public int reflectionProbabilityPercent;
    public int reflectionRoll;
    public bool sourceIsCharacter;
    public int meleeHitStopTicks;
}

public struct TestPlayDefenseHitResult
{
    public TestPlayCombatHitDecision decision;
    public int reactionState;
    public bool clearLinkedTarget;
    public bool forceFacingToAttacker;
    public int guardHitTimerTicks;
    public int defenderHitStopTicks;
    public int attackerHitStopTicks;
    public int attackerGuardReactionTicks;
    public bool applyAttackerGuardRecoil;
}

public struct TestPlayCombatSequenceInput
{
    public bool sequenceActive;
    public bool meleePressed;
    public bool meleeApproachActive;
    public bool comboInputPending;
    public bool currentActionIsMelee;
    public int currentActionId;
    public int meleeApproachActionId;
    public int swordCancelActionId;
    public bool swordCancelActionUsable;
    public int actionTick;
    public int meleeApproachMinimumTicks;
    public bool targetReached;
    public bool hasMovementEnergy;
    public int meleeApproachFollowupActionId;
    public bool meleeApproachFollowupUsable;
    public int heldLocomotionActionId;
    public bool heldActionIsLocomotionCancel;
}

public struct TestPlayCombatSequenceDecision
{
    public bool handled;
    public bool comboInputPending;
    public bool clearSequence;
    public bool clearMeleeApproach;
    public bool clearSwordCancel;
    public int transitionActionId;
    public TestPlayCombatDecisionReason reason;
}

public struct TestPlayCombatFinishDecision
{
    public bool clearSequence;
    public bool clearMeleeApproach;
    public bool clearComboInput;
    public bool clearSwordCancel;
    public int transitionActionId;
    public TestPlayCombatDecisionReason reason;
}

public struct TestPlayProjectilePayload
{
    public string source;
    public float damage;
    public int down;
    public float horizontalImpactForce;
    public float verticalImpactForce;
    public float speed;
    public bool homing;
    public int attackFlag;
    public TestPlayAttackCollisionKind collisionKind;
    public TestPlayCombatValueSource valueSource;
}

public struct TestPlayCombatHitResult
{
    public string source;
    public float damage;
    public int down;
    public Vector3 impactForce;
    public int attackFlag;
    public TestPlayAttackCollisionKind collisionKind;
    public TestPlayCombatHitDecision decision;
    public int reactionState;
    public int guardHitTimerTicks;
    public int hitStopTicks;
    public bool clearLinkedTarget;
    public bool forceFacingToAttacker;
    public int attackerGuardReactionTicks;
    public bool applyAttackerGuardRecoil;
    public TestPlayCombatValueSource valueSource;
}

public struct TestPlayProjectileTickInput
{
    public Vector3 position;
    public Quaternion rotation;
    public float age;
    public float lifeSeconds;
    public float speed;
    public float homingTurnRate;
    public float tickDeltaTime;
    public bool targetAlive;
    public Vector3 targetPosition;
    public float combinedHitRadius;
}

public struct TestPlayProjectileTickResult
{
    public Vector3 position;
    public Quaternion rotation;
    public float age;
    public bool expired;
    public bool hit;
}

/// <summary>
/// 原作RunProc2 type 1 / LZ_Beamへ渡される確定済み引数。
/// FUN_004e8310 -> FUN_004600c0。描画幅とtexture/trailModeは表示Adapter用に保持する。
/// </summary>
public struct TestPlayType1ProjectileParameters
{
    public int weaponPointId;
    public int energyCost;
    public int trailPointCount;
    public float distancePerTick;
    public float visualWidth;
    public int homingPercent;
    public int textureId;
    public int trailMode;
    public int activeTicks;
}

public struct TestPlayType1ProjectileTickInput
{
    public Vector3 position;
    public Quaternion rotation;
    public int remainingActiveTicks;
    public float distancePerTick;
    public float maximumHomingTurnDegrees;
    public bool targetLinked;
    public bool targetAlive;
    public Vector3 targetPosition;
}

public struct TestPlayType1ProjectileTickResult
{
    public Vector3 previousPosition;
    public Vector3 position;
    public Quaternion rotation;
    public int remainingActiveTicks;
    public bool targetLinked;
    public bool expired;
}

/// <summary>
/// 原作RunProc2 type 57が生成する1個の格闘判定。
/// FUN_004fa150 / FUN_00502e60でATTACK値を生成時に複製し、p8 tick存続する。
/// p2は攻防双方のhit-stop、p3は対象別命中履歴の存続tickとして使われる。
/// </summary>
public sealed class TestPlayMeleeAttackState
{
    public int SessionAttackId { get; internal set; }
    public int weaponPointId;
    public float length;
    public int remainingTicks;
    public int originalP2; // RunProc2第6引数: attacker/defender hit-stop tick
    public int originalP3; // RunProc2第7引数: per-target re-hit interval tick
    public string source;
    public TestPlayProjectilePayload payload;
    public Vector3 previousOrigin;
    public Vector3 previousTip;

    readonly System.Collections.Generic.Dictionary<int, int> hitTargetCooldownTicks =
        new System.Collections.Generic.Dictionary<int, int>();
    readonly System.Collections.Generic.List<int> hitTargetIdScratch =
        new System.Collections.Generic.List<int>();

    public int HitStopTicks => originalP2;
    public int PerTargetRehitTicks => originalP3;

    public bool HasHitTarget(int targetId)
    {
        return hitTargetCooldownTicks.ContainsKey(targetId);
    }

    public int GetTargetRehitCooldownTicks(int targetId)
    {
        return hitTargetCooldownTicks.TryGetValue(targetId, out int ticks) ? ticks : 0;
    }

    internal bool TryMarkTargetHit(int targetId)
    {
        if (hitTargetCooldownTicks.ContainsKey(targetId))
            return false;
        hitTargetCooldownTicks.Add(targetId, originalP3);
        return true;
    }

    internal void TickTargetRehitCooldowns()
    {
        if (hitTargetCooldownTicks.Count == 0)
            return;

        hitTargetIdScratch.Clear();
        foreach (System.Collections.Generic.KeyValuePair<int, int> pair in hitTargetCooldownTicks)
            hitTargetIdScratch.Add(pair.Key);

        for (int i = 0; i < hitTargetIdScratch.Count; i++)
        {
            int targetId = hitTargetIdScratch[i];
            int remainingTicks = hitTargetCooldownTicks[targetId] - 1;
            if (remainingTicks < 1)
                hitTargetCooldownTicks.Remove(targetId);
            else
                hitTargetCooldownTicks[targetId] = remainingTicks;
        }
    }
}

public struct TestPlayMeleeTickInput
{
    public Vector3 origin;
    public Vector3 forward;
    public bool targetAlive;
    public int targetId;
    public Vector3 targetPosition;
    public float targetRadius;
}

public struct TestPlayMeleeTickResult
{
    public bool hit;
    public bool expired;
    public Vector3 currentOrigin;
    public Vector3 currentTip;
}

public struct TestPlayShotActionDecision
{
    public int baseActionId;
    public int selectedActionId;
    public int scriptActionId;
    public bool usesDirectionalVariant;
    public bool usesDualChannels;
}

/// <summary>
/// Scene-independent combat decisions. Confirmed ANI values remain separate
/// from explicit Unity fallback values so uncertain weapon types are traceable.
/// </summary>
public static class TestPlayCombatCore
{
    public const int OriginalType1ActiveTicks = 300;
    public const float OriginalType1InitialAimConeDegrees = 20f;
    public const float OriginalType1HomingDistance = 100f;
    public const float OriginalType1BaseHomingTurnDegrees = 0.4f;
    public const int AttackCooldownSlotCount = 5;
    public const float OriginalShotForwardDotThreshold = 0.707f;
    public const float OriginalShotRearDotThreshold = -0.1f;
    public const float OriginalType1GuardDotThreshold = 0.1736f;
    public const float OriginalType11GuardDotThreshold = 0.766f;
    public const float OriginalType57GuardDotThreshold = 0.5f;
    public const int OriginalGuardHitTimerTicks = 20;
    public const int OriginalHitReactionDownThreshold = 200;
    public const int OriginalKnockdownThreshold = 401;
    public const int OriginalHitReactionTicks = 40;
    public const int OriginalKnockbackMinimumTicks = 11;
    public const int OriginalDownedTicks = 60;
    public const int OriginalGetUpTicks = 35;
    // Original action 16 (FUN_004e3420) emits its destruction smoke on c38 == 0,
    // then switches to the post-destruction action after c38 > 59.
    public const int OriginalDefeatPresentationTicks = 60;

    /// <summary>
    /// FUN_004b27a0のAttackFlag・ShildGuard・c40/c44/c50分岐をScene非依存で評価する。
    /// LaserReflectはScr_LaserReflectが機体+0xB68の1 byteへ書き、type 11はそれを
    /// signed charとして0..99 rollと厳密な&lt;で比較する。rollは呼出側の明示入力とする。
    /// </summary>
    public static TestPlayDefenseHitResult ResolveDefenseHit(TestPlayDefenseHitInput input)
    {
        TestPlayDefenseHitResult result = new TestPlayDefenseHitResult
        {
            decision = TestPlayCombatHitDecision.Damaged
        };

        if (input.targetIsAttackOwner && (input.attackFlag & 0x04) == 0)
        {
            result.decision = TestPlayCombatHitDecision.Ignored;
            return result;
        }

        if (input.hitAcceptanceBlockTicks > 0)
        {
            result.decision = TestPlayCombatHitDecision.Invulnerable;
            return result;
        }

        float guardThreshold = GetGuardDotThreshold(input.collisionKind);
        bool guardPierced = input.collisionKind == TestPlayAttackCollisionKind.OriginalType1 &&
                            (input.attackFlag & 0x10) != 0;
        bool guarded = input.shieldGuardValue != 0 && !guardPierced &&
                       guardThreshold <= 1f &&
                       input.defenderForwardDotToAttacker >= guardThreshold;
        if (guarded)
        {
            result.decision = TestPlayCombatHitDecision.Guarded;
            result.guardHitTimerTicks = OriginalGuardHitTimerTicks;
            if (input.collisionKind == TestPlayAttackCollisionKind.OriginalType57 &&
                input.shieldGuardValue == 1 && input.sourceIsCharacter)
            {
                result.attackerGuardReactionTicks = 2;
                result.applyAttackerGuardRecoil = true;
            }
            return result;
        }

        int laserReflectValue = NormalizeOriginalLaserReflectValue(
            input.reflectionProbabilityPercent);
        if (input.collisionKind == TestPlayAttackCollisionKind.OriginalType11 &&
            (input.attackFlag & 0x02) != 0 &&
            Mathf.Clamp(input.reflectionRoll, 0, 99) < laserReflectValue)
        {
            result.decision = TestPlayCombatHitDecision.Reflected;
            result.guardHitTimerTicks = OriginalGuardHitTimerTicks;
            return result;
        }

        result.reactionState = ResolveHitReactionState(input.attackFlag);
        result.clearLinkedTarget = (input.attackFlag & 0x20) != 0;
        result.forceFacingToAttacker = result.clearLinkedTarget;
        if (input.collisionKind == TestPlayAttackCollisionKind.OriginalType57)
        {
            int hitStopTicks = Mathf.Max(0, input.meleeHitStopTicks);
            result.defenderHitStopTicks = hitStopTicks;
            result.attackerHitStopTicks = hitStopTicks;
        }
        return result;
    }

    /// <summary>
    /// Scr_LaserReflectの1 byte格納とFUN_004b27a0のsigned char読取りを再現する。
    /// 0..100は百分率と同義だが、128..255は負値となり反射を成立させない。
    /// </summary>
    public static int NormalizeOriginalLaserReflectValue(int value)
    {
        return unchecked((sbyte)(byte)value);
    }

    public static int ResolveHitReactionState(int attackFlag)
    {
        if ((attackFlag & 0x08) != 0)
            return 3;
        if ((attackFlag & 0x01) != 0)
            return 2;
        return (attackFlag & 0x40) == 0 ? 1 : 0;
    }

    /// <summary>
    /// FUN_004ba1c0のpending reaction、蓄積down、HP、空中分岐をScene非依存で評価する。
    /// action 13/14/15の後段16/17と接触移動はUnity Adapterが担当する。
    /// </summary>
    public static TestPlaySessionReactionDecision ResolveSessionReaction(
        TestPlaySessionReactionInput input)
    {
        int accumulatedDown = Mathf.Max(0, input.accumulatedDown) +
                              Mathf.Max(0, input.incomingDown);
        bool defeated = input.hpAfter <= 0f;
        bool hasReaction = input.reactionState != 0;
        bool startsReaction = defeated ||
            (hasReaction && (accumulatedDown >= OriginalHitReactionDownThreshold ||
                             input.reactionState == 2 || input.reactionState == 3));
        bool knockdown = startsReaction &&
            (defeated || accumulatedDown >= OriginalKnockdownThreshold || input.reactionState == 3);

        return new TestPlaySessionReactionDecision
        {
            accumulatedDown = accumulatedDown,
            startsReaction = startsReaction,
            knockdown = knockdown,
            defeated = defeated,
            actionId = startsReaction ? (knockdown ? 15 : input.airborne ? 14 : 13) : -1,
            phase = defeated
                ? TestPlaySessionReactionPhase.Defeated
                : knockdown
                    ? TestPlaySessionReactionPhase.Knockback
                    : startsReaction
                        ? TestPlaySessionReactionPhase.Hit
                        : TestPlaySessionReactionPhase.None
        };
    }

    public static int TickAccumulatedDown(int value)
    {
        return Mathf.Max(0, value - 1);
    }

    public static int TickPositiveTimer(int value)
    {
        return value > 0 ? value - 1 : value;
    }

    public static TestPlayAttackCollisionKind ResolveRunProcCollisionKind(int procType)
    {
        switch (procType)
        {
            case 1: return TestPlayAttackCollisionKind.OriginalType1;
            case 11: return TestPlayAttackCollisionKind.OriginalType11;
            case 57: return TestPlayAttackCollisionKind.OriginalType57;
            default: return TestPlayAttackCollisionKind.Unspecified;
        }
    }

    static float GetGuardDotThreshold(TestPlayAttackCollisionKind collisionKind)
    {
        switch (collisionKind)
        {
            case TestPlayAttackCollisionKind.OriginalType1:
                return OriginalType1GuardDotThreshold;
            case TestPlayAttackCollisionKind.OriginalType11:
                return OriginalType11GuardDotThreshold;
            case TestPlayAttackCollisionKind.OriginalType57:
                return OriginalType57GuardDotThreshold;
            default:
                return float.PositiveInfinity;
        }
    }

    public static void ConfigureAttackProfile(
        TestPlayAttackProfile profile,
        int power,
        int down,
        float force,
        float forceY)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        profile.power = power;
        profile.down = down;
        profile.force = force;
        profile.forceY = forceY;
    }

    public static void TickCooldowns(int[] cooldownTicks)
    {
        if (cooldownTicks == null)
            return;

        int count = Mathf.Min(AttackCooldownSlotCount, cooldownTicks.Length);
        for (int i = 0; i < count; i++)
        {
            if (cooldownTicks[i] > 0)
                cooldownTicks[i]--;
        }
    }

    public static bool TrySetCooldown(int[] cooldownTicks, int slot, int ticks)
    {
        if (cooldownTicks == null || slot < 0 ||
            slot >= AttackCooldownSlotCount || slot >= cooldownTicks.Length)
            return false;

        cooldownTicks[slot] = Mathf.Max(0, ticks);
        return true;
    }

    public static int GetCooldown(int[] cooldownTicks, int slot)
    {
        return cooldownTicks != null && slot >= 0 &&
               slot < AttackCooldownSlotCount && slot < cooldownTicks.Length
            ? cooldownTicks[slot]
            : 0;
    }

    public static int ResolveShotInputAction(
        bool currentActionIsBoost,
        int actionTick,
        bool boostShotUsable,
        bool swordEquipped,
        bool switchToGunUsable,
        int shotAction,
        int boostShotAction,
        int switchToGunAction)
    {
        if (currentActionIsBoost)
            return actionTick > 5 && boostShotUsable ? boostShotAction : -1;
        if (swordEquipped)
            return switchToGunUsable ? switchToGunAction : shotAction;
        return shotAction;
    }

    public static TestPlayShotActionDecision ResolveTargetRelativeShotAction(
        int baseActionId,
        Vector3 facing,
        Vector3 targetOffset,
        Func<int, bool> hasUsableAction)
    {
        TestPlayShotActionDecision decision = new TestPlayShotActionDecision
        {
            baseActionId = baseActionId,
            selectedActionId = baseActionId,
            scriptActionId = baseActionId,
            usesDirectionalVariant = false,
            usesDualChannels = false
        };

        int leftActionId;
        int rightActionId;
        if (baseActionId == 100)
        {
            leftActionId = 102;
            rightActionId = 101;
        }
        else if (baseActionId == 106)
        {
            leftActionId = 108;
            rightActionId = 107;
        }
        else
        {
            return decision;
        }

        Vector3 flatFacing = Vector3.ProjectOnPlane(facing, Vector3.up);
        Vector3 flatTarget = Vector3.ProjectOnPlane(targetOffset, Vector3.up);
        if (flatFacing.sqrMagnitude <= Mathf.Epsilon || flatTarget.sqrMagnitude <= Mathf.Epsilon)
            return decision;

        flatFacing.Normalize();
        flatTarget.Normalize();
        float dot = Vector3.Dot(flatFacing, flatTarget);
        if (dot >= OriginalShotForwardDotThreshold)
            return decision;

        float side = Vector3.Cross(flatFacing, flatTarget).y;
        int sideActionId = side <= 0f ? leftActionId : rightActionId;
        int selectedActionId = sideActionId;
        int scriptActionId = baseActionId;

        if (dot < OriginalShotRearDotThreshold || !IsUsable(hasUsableAction, sideActionId))
        {
            if (!IsUsable(hasUsableAction, 103))
                return decision;
            selectedActionId = 103;
            scriptActionId = 103;
        }

        if (!IsUsable(hasUsableAction, selectedActionId))
            return decision;

        decision.selectedActionId = selectedActionId;
        decision.scriptActionId = scriptActionId;
        decision.usesDirectionalVariant = true;
        decision.usesDualChannels = true;
        return decision;
    }

    static bool IsUsable(Func<int, bool> hasUsableAction, int actionId)
    {
        return hasUsableAction == null || hasUsableAction(actionId);
    }

    public static int ResolveMeleeInputAction(
        bool swordEquipped,
        bool switchToSwordUsable,
        int direction,
        int switchToSwordAction,
        int forwardMeleeAction,
        bool forwardUsable,
        int neutralMeleeAction,
        bool neutralUsable,
        int leftMeleeAction,
        bool leftUsable,
        int rightMeleeAction,
        bool rightUsable,
        int backMeleeAction,
        bool backUsable)
    {
        if (!swordEquipped && switchToSwordUsable)
            return switchToSwordAction;
        if ((direction == 7 || direction == 8 || direction == 9) && forwardUsable)
            return forwardMeleeAction;
        if (direction == 4 && leftUsable)
            return leftMeleeAction;
        if (direction == 6 && rightUsable)
            return rightMeleeAction;
        if (direction == 2 && backUsable)
            return backMeleeAction;
        return neutralUsable ? neutralMeleeAction : forwardMeleeAction;
    }

    public static bool CanAcceptNormalAttackInput(
        bool currentAnimationMissing,
        int currentLogicalActionId,
        int requestedActionId,
        bool boostMotionActive,
        bool boostShotWindowOpen,
        int boostActionId,
        int boostShotActionId,
        int idleActionId,
        int moveActionId,
        int riseActionId,
        int airMoveActionId,
        int airIdleActionId,
        bool currentActionIsStep)
    {
        if (currentAnimationMissing)
            return true;
        if (currentLogicalActionId == boostActionId)
            return boostMotionActive &&
                   (requestedActionId != boostShotActionId || boostShotWindowOpen);
        return currentLogicalActionId == idleActionId ||
               currentLogicalActionId == moveActionId ||
               currentLogicalActionId == riseActionId ||
               currentLogicalActionId == airMoveActionId ||
               currentLogicalActionId == airIdleActionId ||
               currentActionIsStep;
    }

    public static TestPlayCombatSequenceDecision EvaluateSequence(TestPlayCombatSequenceInput input)
    {
        TestPlayCombatSequenceDecision result = new TestPlayCombatSequenceDecision
        {
            transitionActionId = -1,
            comboInputPending = input.comboInputPending
        };
        if (!input.sequenceActive)
            return result;

        result.handled = true;
        if (input.meleePressed && !input.meleeApproachActive && input.currentActionIsMelee)
        {
            result.comboInputPending = true;
            result.reason = TestPlayCombatDecisionReason.ComboQueued;
        }

        if (result.comboInputPending && input.currentActionIsMelee &&
            input.swordCancelActionId > 0 && input.swordCancelActionUsable)
        {
            result.transitionActionId = input.swordCancelActionId;
            result.comboInputPending = false;
            result.clearMeleeApproach = true;
            result.clearSwordCancel = true;
            result.reason = TestPlayCombatDecisionReason.SwordCancel;
            return result;
        }

        if (input.meleeApproachActive && input.currentActionId == input.meleeApproachActionId)
        {
            int minimumTicks = Mathf.Max(1, input.meleeApproachMinimumTicks);
            if (input.actionTick >= minimumTicks &&
                (input.targetReached || !input.hasMovementEnergy) &&
                input.meleeApproachFollowupUsable)
            {
                result.transitionActionId = input.meleeApproachFollowupActionId;
                result.comboInputPending = false;
                result.clearMeleeApproach = true;
                result.clearSwordCancel = true;
                result.reason = TestPlayCombatDecisionReason.MeleeApproachFollowup;
            }
            return result;
        }

        if (input.currentActionIsMelee && input.actionTick > 15 && input.heldActionIsLocomotionCancel)
        {
            result.transitionActionId = input.heldLocomotionActionId;
            result.comboInputPending = false;
            result.clearSequence = true;
            result.clearMeleeApproach = true;
            result.clearSwordCancel = true;
            result.reason = TestPlayCombatDecisionReason.LocomotionCancel;
        }
        return result;
    }

    public static TestPlayCombatFinishDecision ResolveFinish(
        bool meleeApproachActive,
        int currentActionId,
        int meleeApproachActionId,
        int meleeApproachFollowupActionId,
        bool meleeApproachFollowupUsable,
        bool airborne,
        int airIdleActionId,
        int groundedRecoveryActionId)
    {
        if (meleeApproachActive && currentActionId == meleeApproachActionId &&
            meleeApproachFollowupUsable)
        {
            return new TestPlayCombatFinishDecision
            {
                clearMeleeApproach = true,
                clearComboInput = true,
                clearSwordCancel = true,
                transitionActionId = meleeApproachFollowupActionId,
                reason = TestPlayCombatDecisionReason.MeleeApproachFollowup
            };
        }

        return new TestPlayCombatFinishDecision
        {
            clearSequence = true,
            clearMeleeApproach = true,
            clearComboInput = true,
            clearSwordCancel = true,
            transitionActionId = airborne ? airIdleActionId : groundedRecoveryActionId,
            reason = TestPlayCombatDecisionReason.AttackFinished
        };
    }

    public static TestPlayProjectilePayload CreateProjectilePayload(
        TestPlayAttackProfile profile,
        float fallbackDamage,
        float speed,
        bool homing,
        string source,
        int attackFlag = 0,
        TestPlayAttackCollisionKind collisionKind = TestPlayAttackCollisionKind.Unspecified)
    {
        bool usesScriptProfile = profile != null && profile.power > 0;
        return new TestPlayProjectilePayload
        {
            source = source ?? "",
            damage = usesScriptProfile ? profile.power : Mathf.RoundToInt(Mathf.Max(0f, fallbackDamage)),
            down = profile != null ? profile.down : 0,
            horizontalImpactForce = profile != null ? profile.force : 0f,
            verticalImpactForce = profile != null ? profile.forceY : 0f,
            speed = Mathf.Max(0f, speed),
            homing = homing,
            attackFlag = attackFlag,
            collisionKind = collisionKind,
            valueSource = usesScriptProfile
                ? TestPlayCombatValueSource.OriginalScriptProfile
                : TestPlayCombatValueSource.UnityFallback
        };
    }

    public static TestPlayCombatHitResult CreateHitResult(
        TestPlayAttackProfile profile,
        float fallbackDamage,
        Vector3 horizontalDirection,
        string source)
    {
        TestPlayProjectilePayload payload = CreateProjectilePayload(
            profile, fallbackDamage, 0f, false, source);
        horizontalDirection.y = 0f;
        horizontalDirection = horizontalDirection.sqrMagnitude > 0.000001f
            ? horizontalDirection.normalized
            : Vector3.forward;
        return new TestPlayCombatHitResult
        {
            source = payload.source,
            damage = payload.damage,
            down = payload.down,
            impactForce = horizontalDirection * payload.horizontalImpactForce +
                          Vector3.up * payload.verticalImpactForce,
            attackFlag = payload.attackFlag,
            collisionKind = payload.collisionKind,
            valueSource = payload.valueSource
        };
    }

    public static TestPlayCombatHitResult CreateHitResult(
        TestPlayProjectilePayload payload,
        Vector3 horizontalDirection)
    {
        horizontalDirection.y = 0f;
        horizontalDirection = horizontalDirection.sqrMagnitude > 0.000001f
            ? horizontalDirection.normalized
            : Vector3.forward;
        return new TestPlayCombatHitResult
        {
            source = payload.source,
            damage = payload.damage,
            down = payload.down,
            impactForce = horizontalDirection * payload.horizontalImpactForce +
                          Vector3.up * payload.verticalImpactForce,
            attackFlag = payload.attackFlag,
            collisionKind = payload.collisionKind,
            valueSource = payload.valueSource
        };
    }

    public static TestPlayMeleeAttackState CreateMeleeAttackState(
        TestPlayAttackProfile profile,
        float fallbackDamage,
        int weaponPointId,
        float length,
        int lifetimeTicks,
        int originalP2,
        int originalP3,
        Vector3 origin,
        Vector3 forward,
        string source,
        int attackFlag = 0)
    {
        Vector3 direction = forward.sqrMagnitude > 0.000001f
            ? forward.normalized
            : Vector3.forward;
        float clampedLength = Mathf.Max(0f, length);
        return new TestPlayMeleeAttackState
        {
            weaponPointId = weaponPointId,
            length = clampedLength,
            remainingTicks = Mathf.Max(0, lifetimeTicks),
            originalP2 = originalP2,
            originalP3 = originalP3,
            source = source ?? "",
            payload = CreateProjectilePayload(
                profile,
                fallbackDamage,
                0f,
                false,
                source,
                attackFlag,
                TestPlayAttackCollisionKind.OriginalType57),
            previousOrigin = origin,
            previousTip = origin + direction * clampedLength
        };
    }

    /// <summary>
    /// 原作BB_SwordBeamAtkの現在線分と前tick線分から作る掃引四辺形を、
    /// Unity TestPlayの球形targetへ照合する。命中targetはp3 tickだけ履歴に残り、
    /// 履歴削除後は同じ生成物から再命中できる。
    /// </summary>
    public static TestPlayMeleeTickResult TickMeleeAttack(
        TestPlayMeleeAttackState state,
        TestPlayMeleeTickInput input)
    {
        if (state == null)
            return new TestPlayMeleeTickResult { expired = true };

        Vector3 direction = input.forward.sqrMagnitude > 0.000001f
            ? input.forward.normalized
            : Vector3.forward;
        Vector3 currentOrigin = input.origin;
        Vector3 currentTip = currentOrigin + direction * Mathf.Max(0f, state.length);
        bool active = state.remainingTicks > 0;
        if (active)
            state.TickTargetRehitCooldowns();
        bool hit = active && input.targetAlive && !state.HasHitTarget(input.targetId) &&
                   IntersectsSweptSegmentSphere(
                       state.previousOrigin,
                       state.previousTip,
                       currentOrigin,
                       currentTip,
                       input.targetPosition,
                       input.targetRadius);
        if (hit)
            state.TryMarkTargetHit(input.targetId);

        state.previousOrigin = currentOrigin;
        state.previousTip = currentTip;
        if (active)
            state.remainingTicks--;

        return new TestPlayMeleeTickResult
        {
            hit = hit,
            expired = state.remainingTicks <= 0,
            currentOrigin = currentOrigin,
            currentTip = currentTip
        };
    }

    // One geometry/lifetime update for all session targets. Legacy single-target entry stays intact.
    public static TestPlayMeleeTickResult TickMeleeAttackTargets(
        TestPlayMeleeAttackState state, Vector3 origin, Vector3 forward,
        System.Collections.Generic.IReadOnlyList<TestPlayTargetSnapshot> targets,
        System.Collections.Generic.List<TestPlayTargetSnapshot> hits)
    {
        hits.Clear();
        if (state == null) return new TestPlayMeleeTickResult { expired = true };
        Vector3 direction = forward.sqrMagnitude > 0.000001f ? forward.normalized : Vector3.forward;
        Vector3 tip = origin + direction * Mathf.Max(0f, state.length);
        if (state.remainingTicks > 0)
        {
            state.TickTargetRehitCooldowns();
            foreach (var target in targets)
                if (target.Alive && !state.HasHitTarget(target.Handle.MechId) &&
                    IntersectsSweptSegmentSphere(state.previousOrigin, state.previousTip, origin, tip,
                        target.Position, target.Radius) && state.TryMarkTargetHit(target.Handle.MechId))
                    hits.Add(target);
            state.remainingTicks--;
        }
        state.previousOrigin = origin;
        state.previousTip = tip;
        return new TestPlayMeleeTickResult { hit = hits.Count > 0, expired = state.remainingTicks <= 0,
            currentOrigin = origin, currentTip = tip };
    }

    public static bool IntersectsSweptSegmentSphere(
        Vector3 previousOrigin,
        Vector3 previousTip,
        Vector3 currentOrigin,
        Vector3 currentTip,
        Vector3 sphereCenter,
        float sphereRadius)
    {
        float radiusSquared = Mathf.Max(0f, sphereRadius);
        radiusSquared *= radiusSquared;

        float distanceSquared = Mathf.Min(
            PointSegmentDistanceSquared(sphereCenter, previousOrigin, previousTip),
            PointSegmentDistanceSquared(sphereCenter, currentOrigin, currentTip));
        distanceSquared = Mathf.Min(distanceSquared,
            PointSegmentDistanceSquared(sphereCenter, previousOrigin, currentOrigin));
        distanceSquared = Mathf.Min(distanceSquared,
            PointSegmentDistanceSquared(sphereCenter, previousTip, currentTip));
        distanceSquared = Mathf.Min(distanceSquared,
            PointTriangleDistanceSquared(sphereCenter, previousOrigin, previousTip, currentTip));
        distanceSquared = Mathf.Min(distanceSquared,
            PointTriangleDistanceSquared(sphereCenter, previousOrigin, currentTip, currentOrigin));
        return distanceSquared <= radiusSquared;
    }

    static float PointSegmentDistanceSquared(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float denominator = segment.sqrMagnitude;
        if (denominator <= 0.000001f)
            return (point - start).sqrMagnitude;
        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / denominator);
        return (point - (start + segment * t)).sqrMagnitude;
    }

    // Real-Time Collision Detection (Christer Ericson)の最近点領域判定。
    static float PointTriangleDistanceSquared(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        if (Vector3.Cross(ab, ac).sqrMagnitude <= 0.000001f)
        {
            return Mathf.Min(
                PointSegmentDistanceSquared(point, a, b),
                Mathf.Min(
                    PointSegmentDistanceSquared(point, b, c),
                    PointSegmentDistanceSquared(point, c, a)));
        }

        Vector3 ap = point - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return ap.sqrMagnitude;

        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return bp.sqrMagnitude;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return (point - (a + v * ab)).sqrMagnitude;
        }

        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return cp.sqrMagnitude;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return (point - (a + w * ac)).sqrMagnitude;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            Vector3 edge = c - b;
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return (point - (b + w * edge)).sqrMagnitude;
        }

        float denominatorFace = 1f / (va + vb + vc);
        float faceV = vb * denominatorFace;
        float faceW = vc * denominatorFace;
        Vector3 closest = a + ab * faceV + ac * faceW;
        return (point - closest).sqrMagnitude;
    }

    public static TestPlayType1ProjectileParameters CreateOriginalType1Parameters(
        int weaponPointId,
        int energyCost,
        int trailPointCount,
        int distancePerTickTimes100,
        int visualWidthTimes100,
        int homingPercent,
        int textureId,
        int trailMode)
    {
        return new TestPlayType1ProjectileParameters
        {
            weaponPointId = weaponPointId,
            energyCost = Mathf.Max(0, energyCost),
            trailPointCount = Mathf.Max(1, trailPointCount),
            distancePerTick = Mathf.Max(0, distancePerTickTimes100) / 100f,
            visualWidth = Mathf.Max(0, visualWidthTimes100) / 100f,
            homingPercent = homingPercent,
            textureId = textureId,
            trailMode = trailMode,
            activeTicks = OriginalType1ActiveTicks
        };
    }

    /// <summary>
    /// FUN_004e8310: 100以内のtargetにだけ距離反比例の基礎旋回値を与え、
    /// p4を百分率補正として加える。戻り値は1 original tick当たりの最大角度。
    /// </summary>
    public static float ResolveOriginalType1HomingTurnDegrees(float targetDistance, int homingPercent)
    {
        if (targetDistance >= OriginalType1HomingDistance)
            return 0f;
        float distanceFactor = 1f - Mathf.Clamp01(targetDistance / OriginalType1HomingDistance);
        float percentFactor = 1f + homingPercent / 100f;
        return Mathf.Max(0f, OriginalType1BaseHomingTurnDegrees * distanceFactor * percentFactor);
    }

    /// <summary>FUN_004e8310 / FUN_0055fb50の20度以内target初期照準。</summary>
    public static Quaternion ResolveOriginalType1InitialRotation(
        Quaternion muzzleRotation,
        Vector3 muzzlePosition,
        bool targetAlive,
        Vector3 targetPosition)
    {
        if (!targetAlive)
            return muzzleRotation;
        Vector3 direction = targetPosition - muzzlePosition;
        if (direction.sqrMagnitude <= 0.000001f ||
            Vector3.Angle(muzzleRotation * Vector3.forward, direction) >= OriginalType1InitialAimConeDegrees)
            return muzzleRotation;
        Vector3 up = muzzleRotation * Vector3.up;
        return Quaternion.LookRotation(direction.normalized, up);
    }

    /// <summary>
    /// FUN_00460200の順序どおり、ローカルZ+へ移動してからtarget方向へ最大角度だけ旋回する。
    /// </summary>
    public static TestPlayType1ProjectileTickResult TickOriginalType1Projectile(
        TestPlayType1ProjectileTickInput input)
    {
        TestPlayType1ProjectileTickResult result = new TestPlayType1ProjectileTickResult
        {
            previousPosition = input.position,
            position = input.position,
            rotation = input.rotation,
            remainingActiveTicks = input.remainingActiveTicks,
            targetLinked = input.targetLinked
        };
        if (result.remainingActiveTicks <= 0)
        {
            result.expired = true;
            return result;
        }

        result.position += result.rotation * Vector3.forward * Mathf.Max(0f, input.distancePerTick);
        if (result.targetLinked && input.targetAlive)
        {
            Vector3 targetDirection = input.targetPosition - result.position;
            Vector3 forward = result.rotation * Vector3.forward;
            if (targetDirection.sqrMagnitude > 0.000001f)
            {
                if (Vector3.Dot(forward, targetDirection) < 0f)
                {
                    result.targetLinked = false;
                }
                else if (input.maximumHomingTurnDegrees > 0f)
                {
                    Quaternion desired = Quaternion.LookRotation(
                        targetDirection.normalized,
                        result.rotation * Vector3.up);
                    result.rotation = Quaternion.RotateTowards(
                        result.rotation,
                        desired,
                        input.maximumHomingTurnDegrees);
                }
            }
        }
        else if (!input.targetAlive)
        {
            result.targetLinked = false;
        }

        result.remainingActiveTicks--;
        result.expired = result.remainingActiveTicks <= 0;
        return result;
    }

    /// <summary>
    /// 原作はtarget側の複合形状をtrail線分へ照合する。Unity TestPlayでは単一球形targetを
    /// 明示的Adapterとして使うため、この関数は線分対球だけを担当する。
    /// </summary>
    public static bool IntersectsType1TrailSegmentSphere(
        Vector3 segmentStart,
        Vector3 segmentEnd,
        Vector3 sphereCenter,
        float sphereRadius)
    {
        float radius = Mathf.Max(0f, sphereRadius);
        return PointSegmentDistanceSquared(sphereCenter, segmentStart, segmentEnd) <= radius * radius;
    }

    public static TestPlayProjectileTickResult TickProjectile(TestPlayProjectileTickInput input)
    {
        float dt = Mathf.Max(0f, input.tickDeltaTime);
        TestPlayProjectileTickResult result = new TestPlayProjectileTickResult
        {
            position = input.position,
            rotation = input.rotation,
            age = input.age + dt
        };
        if (result.age >= Mathf.Max(0f, input.lifeSeconds))
        {
            result.expired = true;
            return result;
        }

        if (input.targetAlive && input.homingTurnRate > 0f)
        {
            Vector3 targetDirection = input.targetPosition - result.position;
            if (targetDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion desired = Quaternion.LookRotation(targetDirection.normalized, Vector3.up);
                result.rotation = Quaternion.RotateTowards(
                    result.rotation, desired, input.homingTurnRate * dt);
            }
        }

        result.position += result.rotation * Vector3.forward * Mathf.Max(0f, input.speed) * dt;
        if (input.targetAlive)
        {
            float radius = Mathf.Max(0f, input.combinedHitRadius);
            result.hit = (input.targetPosition - result.position).sqrMagnitude <= radius * radius;
        }
        return result;
    }
}
