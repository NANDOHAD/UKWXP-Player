using System.Collections.Generic;
using UnityEngine;

public class TestPlayProjectile : MonoBehaviour
{
    public TestPlayController owner;
    public TestPlayTargetDummy target;
    public float speed = 25f;
    public float damage = 50f;
    public float lifeSeconds = 3f;
    public float hitRadius = 0.5f;
    public float homingTurnRate = 0f;
    public int downValue;
    public float horizontalImpactForce;
    public float verticalImpactForce;
    public int attackFlag;
    public TestPlayAttackCollisionKind collisionKind;
    public string sourceCommand = "Projectile";
    public TestPlayCombatValueSource valueSource = TestPlayCombatValueSource.UnityFallback;
    [Header("Original RunProc2 type 1 Core")]
    public bool useOriginalType1Core;
    public int weaponPointId = -1;
    public int remainingActiveTicks;
    public int trailPointCount = 1;
    public float distancePerTick;
    public float visualWidth;
    public int homingPercent;
    public int textureId = -1;
    public int originalTrailMode;
    public float maximumHomingTurnDegrees;
    [Min(1f)]
    public float originalTickRate = 60f;
    [Min(1)]
    public int maximumCatchUpTicks = 8;

    float age;
    float simulationAccumulator;
    bool originalType1TargetLinked;
    bool originalType1TargetHit;
    TestPlayType1TrailEffect originalType1TrailEffect;
    readonly List<Vector3> originalType1Trail = new List<Vector3>();
    readonly HashSet<int> sessionHitTargets = new HashSet<int>();

    public int OriginalType1TrailCount => originalType1Trail.Count;
    GameSession session;
    MechHandle guidance;
    bool sessionStepping;
    public int AttackId { get; private set; }
    public MechHandle OwnerHandle { get; private set; }
    public bool SessionExpired { get; private set; }

    internal void AttachSession(GameSession ownerSession, int attackId, MechHandle ownerHandle, MechHandle targetHandle)
    {
        if (session != null) throw new System.InvalidOperationException("弾は既にセッションへ登録されています。");
        session = ownerSession;
        AttackId = attackId;
        OwnerHandle = ownerHandle;
        guidance = targetHandle;
        target = null;
        originalTickRate = 60;
    }

    internal void SimulateSessionTick(GameSession ownerSession)
    {
        if (session != ownerSession || SessionExpired) return;
        sessionStepping = true;
        try { SessionExpired = SimulateOriginalTick(1f / 60f); }
        finally { sessionStepping = false; }
    }

    public void ConfigureOriginalType1(
        TestPlayType1ProjectileParameters parameters,
        bool targetLinked,
        float homingTurnDegrees)
    {
        useOriginalType1Core = true;
        weaponPointId = parameters.weaponPointId;
        remainingActiveTicks = parameters.activeTicks;
        trailPointCount = parameters.trailPointCount;
        distancePerTick = parameters.distancePerTick;
        visualWidth = parameters.visualWidth;
        homingPercent = parameters.homingPercent;
        textureId = parameters.textureId;
        originalTrailMode = parameters.trailMode;
        maximumHomingTurnDegrees = Mathf.Max(0f, homingTurnDegrees);
        originalType1TargetLinked = targetLinked;
        originalType1TargetHit = false;
        sessionHitTargets.Clear();
        originalType1Trail.Clear();
        originalType1Trail.Add(transform.position);
        originalType1TrailEffect = GetComponent<TestPlayType1TrailEffect>();
        originalType1TrailEffect?.SetTrail(originalType1Trail);
    }

    void Update()
    {
        if (session != null) return;
        float tickDeltaTime = 1f / Mathf.Max(1f, originalTickRate);
        simulationAccumulator += Mathf.Max(0f, Time.deltaTime);
        int catchUpTicks = 0;
        int catchUpLimit = Mathf.Max(1, maximumCatchUpTicks);
        while (simulationAccumulator + 0.0000001f >= tickDeltaTime && catchUpTicks < catchUpLimit)
        {
            simulationAccumulator -= tickDeltaTime;
            catchUpTicks++;
            if (SimulateOriginalTick(tickDeltaTime))
                return;
        }
    }

    public bool SimulateOriginalTick(float tickDeltaTime)
    {
        if (session != null && !sessionStepping)
            throw new System.InvalidOperationException("弾はセッション時計から更新してください。");
        if (useOriginalType1Core)
            return SimulateOriginalType1Tick();

        bool targetAlive = target != null && target.IsAlive;
        if (session != null && homingTurnRate > 0 && session.TryGet(guidance, out var snapshot))
        {
            Vector3 direction = snapshot.Position - transform.position;
            if (direction.sqrMagnitude > 0.000001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction.normalized, Vector3.up), homingTurnRate * tickDeltaTime);
        }
        TestPlayProjectileTickResult result = TestPlayCombatCore.TickProjectile(
            new TestPlayProjectileTickInput
            {
                position = transform.position,
                rotation = transform.rotation,
                age = age,
                lifeSeconds = lifeSeconds,
                speed = speed,
                homingTurnRate = homingTurnRate,
                tickDeltaTime = tickDeltaTime,
                targetAlive = targetAlive,
                targetPosition = targetAlive ? target.transform.position : Vector3.zero,
                combinedHitRadius = hitRadius + (targetAlive ? target.hitRadius : 0f)
            });

        age = result.age;
        transform.SetPositionAndRotation(result.position, result.rotation);
        if (result.expired)
        {
            Expire();
            return true;
        }

        if (result.hit && targetAlive)
        {
            ApplyHit(transform.position - transform.forward);
            Expire();
            return true;
        }

        return false;
    }

    bool SimulateOriginalType1Tick()
    {
        bool targetAlive = target != null && target.IsAlive;
        Vector3 targetPosition = targetAlive ? target.transform.position : Vector3.zero;
        if (session != null)
        {
            targetAlive = session.TryGet(guidance, out var snapshot);
            targetPosition = targetAlive ? snapshot.Position : Vector3.zero;
        }
        TestPlayType1ProjectileTickResult result = TestPlayCombatCore.TickOriginalType1Projectile(
            new TestPlayType1ProjectileTickInput
            {
                position = transform.position,
                rotation = transform.rotation,
                remainingActiveTicks = remainingActiveTicks,
                distancePerTick = distancePerTick,
                maximumHomingTurnDegrees = maximumHomingTurnDegrees,
                targetLinked = originalType1TargetLinked,
                targetAlive = targetAlive,
                targetPosition = targetPosition
            });

        remainingActiveTicks = result.remainingActiveTicks;
        originalType1TargetLinked = result.targetLinked;
        transform.SetPositionAndRotation(result.position, result.rotation);
        originalType1Trail.Add(result.position);
        while (originalType1Trail.Count > Mathf.Max(1, trailPointCount))
            originalType1Trail.RemoveAt(0);
        originalType1TrailEffect?.SetTrail(originalType1Trail);

        if (session != null)
        {
            foreach (var candidate in session.QueryAttackTargets(OwnerHandle, transform.position))
            {
                if (sessionHitTargets.Contains(candidate.Handle.MechId)) continue;
                for (int i = 1; i < originalType1Trail.Count; i++)
                {
                    if (!TestPlayCombatCore.IntersectsType1TrailSegmentSphere(originalType1Trail[i - 1],
                        originalType1Trail[i], candidate.Position, candidate.Radius)) continue;
                    var hit = CreateHit();
                    if (session.ResolveHit(AttackId, OwnerHandle, candidate.Handle, hit,
                        originalType1Trail[i - 1], false, 0, out hit))
                    {
                        sessionHitTargets.Add(candidate.Handle.MechId);
                        if (owner != null && owner.playModeActive && owner.currentHP > 0) owner.NotifyProjectileHit(hit);
                    }
                    break;
                }
            }
        }

        if (session == null && !originalType1TargetHit && targetAlive)
        {
            for (int i = 1; i < originalType1Trail.Count; i++)
            {
                if (!TestPlayCombatCore.IntersectsType1TrailSegmentSphere(
                        originalType1Trail[i - 1],
                        originalType1Trail[i],
                        target.transform.position,
                        target.hitRadius))
                    continue;

                originalType1TargetHit = true;
                ApplyHit(originalType1Trail[i - 1]);
                break;
            }
        }

        if (result.expired)
        {
            // 原作はこの後、描画幅を0.01/tickでfadeする。DX9表示だけの差なので
            // TestPlay Adapterはactive/collision終了時に破棄する。
            Expire();
            return true;
        }
        return false;
    }

    void Expire()
    {
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }

    void ApplyHit(Vector3 attackerPosition)
    {
        TestPlayCombatHitResult hit = CreateHit();
        hit = target.ResolveImpact(hit, attackerPosition, false, false, 0);
        owner?.NotifyProjectileHit(hit);
    }

    TestPlayCombatHitResult CreateHit()
    {
        TestPlayProjectilePayload payload = new TestPlayProjectilePayload
        {
            source = sourceCommand,
            damage = damage,
            down = downValue,
            horizontalImpactForce = horizontalImpactForce,
            verticalImpactForce = verticalImpactForce,
            attackFlag = attackFlag,
            collisionKind = collisionKind,
            valueSource = valueSource
        };
        return TestPlayCombatCore.CreateHitResult(payload, transform.forward);
    }
}
