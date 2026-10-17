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
    public int originalModelSlot = -1;
    public float maximumHomingTurnDegrees;
    [Min(1f)]
    public float originalTickRate = 60f;
    [Min(1)]
    public int maximumCatchUpTicks = 8;

    float age;
    float simulationAccumulator;
    bool originalType1TargetLinked;
    bool originalType1TargetHit;
    bool originalType1Fading;
    float originalType1FadeWidth;
    TestPlayType1TrailEffect originalType1TrailEffect;
    readonly List<Vector3> originalType1Trail = new List<Vector3>();
    readonly HashSet<int> sessionHitTargets = new HashSet<int>();
    readonly Dictionary<int, int> type11NextHitTick = new Dictionary<int, int>();
    int cannonTick;
    Transform swordVisual;
    Quaternion swordRotation;
    Vector3 swordVictimOffset;
    Quaternion swordVictimRotation;
    MechHandle swordVictim;
    bool swordRequiresOwner;
    int swordRestTicks;
    public int SwordThrowStage { get; private set; }

    public void ConfigureOriginalType31(Transform visual, Quaternion modelRotation, Vector3? modelPosition = null)
    {
        swordVisual = visual;
        swordRotation = modelRotation;
        swordRequiresOwner = owner != null;
        SwordThrowStage = 0;
        swordRestTicks = 0;
        // FUN_0047ed50 initially copies the second WP matrix, including position.
        if (swordVisual != null) swordVisual.SetPositionAndRotation(modelPosition ?? transform.position, swordRotation);
    }

    public int OriginalType1TrailCount => originalType1Trail.Count;
    public bool OriginalType1Fading => originalType1Fading;
    public float OriginalType1FadeWidth => originalType1FadeWidth;
    GameSession session;
    MechHandle guidance;
    bool sessionStepping;
    public int AttackId { get; private set; }
    public MechHandle OwnerHandle { get; private set; }
    public bool SessionExpired { get; private set; }
    internal GameSession ModelSession => session;
    internal bool CpuThreatActive => !SessionExpired && !originalType1Fading &&
        supportAttack == null && beamAttack == null && (remainingActiveTicks > 0 || !useOriginalType1Core);
    internal MechHandle ModelGuidance { get => guidance; set => guidance = value; }
    internal bool EndWhenOwnerLost;
    TestPlaySupportAttack supportAttack;
    internal void SetSupportAttack(TestPlaySupportAttack value) => supportAttack = value;
    TestPlayOrdnanceAttack ordnanceAttack;
    internal void SetOrdnanceAttack(TestPlayOrdnanceAttack value) => ordnanceAttack=value;
    TestPlayBeamAttack beamAttack;
    internal void SetBeamAttack(TestPlayBeamAttack value) => beamAttack = value;
    internal void TransferBeamOwner(TestPlayController controller, MechHandle handle, MechHandle targetHandle)
    { owner = controller; OwnerHandle = handle; guidance = targetHandle; }
    TestPlayModelAttack modelAttack;
    internal void SetModelAttack(TestPlayModelAttack value) => modelAttack = value;

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
        originalType1Fading = false;
        originalType1FadeWidth = visualWidth;
        sessionHitTargets.Clear();
        type11NextHitTick.Clear();
        cannonTick = 0;
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
        if (EndWhenOwnerLost && (owner == null || owner.currentHP <= 0 ||
            (session != null && !session.TryGet(OwnerHandle, out _)))) { Expire(); return true; }
        if (supportAttack != null)
        {
            bool finished = supportAttack.Step();
            if (finished) Expire();
            return finished;
        }
        if (ordnanceAttack != null)
        {
            bool finished=ordnanceAttack.Step();
            if(finished)Expire();
            return finished;
        }
        if (beamAttack != null)
        {
            bool finished = beamAttack.Step();
            if (finished) Expire();
            return finished;
        }
        if (modelAttack != null)
        {
            bool finished = modelAttack.Step();
            if (finished) Expire();
            return finished;
        }
        if (useOriginalType1Core && collisionKind == TestPlayAttackCollisionKind.OriginalType31)
            return SimulateOriginalSwordThrowTick();
        if (useOriginalType1Core && TestPlayCombatCore.IsOriginalCannon(collisionKind))
            return SimulateOriginalCannonTick();
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

    bool SimulateOriginalSwordThrowTick()
    {
        // FUN_0047edc0 differs from the shared flying-projectile owner contract:
        // losing the owner immediately selects the terminal effect state.
        if (swordRequiresOwner && (owner == null || owner.currentHP <= 0 ||
            (session != null && !session.TryGet(OwnerHandle, out _)))) SwordThrowStage = 999;
        if (SwordThrowStage == 999)
        {
            if (owner != null) owner.SpawnSwordThrowEffect(transform.position,true);
            Expire(); return true;
        }
        if (SwordThrowStage == 3 || SwordThrowStage == 4)
        {
            if (SwordThrowStage == 4)
            {
                if (session != null)
                {
                    if (!session.TryGet(swordVictim,out var victim)) { Expire(); return true; }
                    transform.SetPositionAndRotation(victim.Position+victim.Rotation*swordVictimOffset,
                        victim.Rotation*swordVictimRotation);
                }
                else if (target == null || !target.IsAlive) { Expire(); return true; }
                else transform.SetPositionAndRotation(target.transform.TransformPoint(swordVictimOffset),
                    target.transform.rotation*swordVictimRotation);
            }
            if (++swordRestTicks > 600) SwordThrowStage=999;
            return false;
        }
        Vector3 start=transform.position;
        transform.position += transform.forward*distancePerTick;
        bool alive = target != null && target.IsAlive;
        Vector3 aim = alive ? target.transform.position : Vector3.zero;
        if (session != null)
        {
            alive=session.TryGet(guidance,out var guided);
            if(alive)aim=(guided.ShotCapsuleStart+guided.ShotCapsuleEnd)*.5f;
        }
        if(!alive)originalType1TargetLinked=false;
        if(originalType1TargetLinked && maximumHomingTurnDegrees>0)
        {
            Vector3 delta=aim-transform.position;
            float dot=delta.sqrMagnitude>.000001f?Vector3.Dot(transform.forward,delta.normalized):1;
            if(dot>-1 && dot<1)transform.rotation=Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(delta.normalized,transform.up),maximumHomingTurnDegrees);
            if(Vector3.Dot(transform.forward,delta)<0)originalType1TargetLinked=false;
        }
        swordRotation *= Quaternion.AngleAxis(20,Vector3.forward);
        if(swordVisual!=null)swordVisual.SetPositionAndRotation(transform.position,swordRotation);
        if(--remainingActiveTicks<=0) { Expire(); return true; }
        owner?.SpawnSwordThrowFlightEffect(transform.position);
        var walls=FindCannonWorldContacts(start,transform.position);
        if(walls.Count>0)transform.position=walls[0].point;
        originalType1Trail.Add(transform.position);
        while(originalType1Trail.Count>3)originalType1Trail.RemoveAt(0);
        if(session!=null)
        {
            foreach(var candidate in session.QueryAttackTargets(OwnerHandle,transform.position))
            {
                bool contact=false;
                for(int i=1;i<originalType1Trail.Count;i++)
                    contact |= TestPlayCombatCore.IntersectsTrailSegmentCapsule(originalType1Trail[i-1],originalType1Trail[i],
                        candidate.ShotCapsuleStart,candidate.ShotCapsuleEnd,candidate.ShotCapsuleRadius);
                if(!contact)continue;
                var hit=CreateHit();
                if(!session.ResolveHit(AttackId,OwnerHandle,candidate.Handle,hit,start,false,0,out hit))continue;
                if(owner!=null)owner.NotifyProjectileHit(hit);
                if(hit.decision==TestPlayCombatHitDecision.Guarded)
                {
                    SwordThrowStage=999; owner?.SpawnSwordThrowEffect(transform.position,false,true);return false;
                }
                if(hit.decision==TestPlayCombatHitDecision.Damaged)
                {
                    SwordThrowStage=4; swordRestTicks=0; swordVictim=candidate.Handle;
                    swordVictimOffset=Quaternion.Inverse(candidate.Rotation)*(transform.position-candidate.Position);
                    swordVictimRotation=Quaternion.Inverse(candidate.Rotation)*transform.rotation;
                    owner?.SpawnSwordThrowEffect(transform.position,false);return false;
                }
            }
        }
        else if(alive && TestPlayCombatCore.IntersectsType1TrailSegmentSphere(start,transform.position,target.transform.position,target.hitRadius))
        {
            var hit=target.ResolveImpact(CreateHit(),start,false,false,0);owner?.NotifyProjectileHit(hit);
            if(hit.decision==TestPlayCombatHitDecision.Guarded) { SwordThrowStage=999; return false; }
            if(hit.decision==TestPlayCombatHitDecision.Damaged)
            {
                SwordThrowStage=4;swordRestTicks=0;
                swordVictimOffset=target.transform.InverseTransformPoint(transform.position);
                swordVictimRotation=Quaternion.Inverse(target.transform.rotation)*transform.rotation;
                return false;
            }
        }
        if(walls.Count>0) { SwordThrowStage=3;swordRestTicks=0;owner?.SpawnSwordThrowEffect(transform.position,false,false,true); }
        return false;
    }

    bool SimulateOriginalCannonTick()
    {
        // FUN_004684f0: move first, then optional type18 guidance; after life
        // runs out, stop and drain one trail point per tick (not Bullet width fade).
        bool penetrating = collisionKind == TestPlayAttackCollisionKind.OriginalType26;
        cannonTick++;
        Vector3 start = transform.position;
        transform.position += transform.forward * distancePerTick;
        if (collisionKind == TestPlayAttackCollisionKind.OriginalType18 && originalType1TargetLinked)
        {
            bool alive = target != null && target.IsAlive;
            Vector3 aim = alive ? target.transform.position : Vector3.zero;
            if (session != null)
            {
                alive = session.TryGet(guidance, out var snapshot) && snapshot.Alive;
                if (alive) aim = (snapshot.ShotCapsuleStart + snapshot.ShotCapsuleEnd) * .5f;
            }
            if (!alive) originalType1TargetLinked = false;
            else if (maximumHomingTurnDegrees > 0)
            {
                Vector3 delta = aim - transform.position;
                float dot = delta.sqrMagnitude > .000001f ? Vector3.Dot(transform.forward, delta.normalized) : 1f;
                // FUN_0055ff80 does not rotate parallel/antiparallel vectors.
                if (dot > -1f && dot < 1f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(delta.normalized, transform.up), maximumHomingTurnDegrees);
                if (Vector3.Dot(transform.forward, delta) < 0) originalType1TargetLinked = false;
            }
        }
        if (remainingActiveTicks > 0 && remainingActiveTicks % 2 == 0)
            owner?.SpawnCannonPuff(transform.position, false, penetrating);
        remainingActiveTicks = Mathf.Max(0, remainingActiveTicks-1);
        if (remainingActiveTicks == 0)
        {
            distancePerTick = 0;
            if (originalType1Trail.Count > 0) originalType1Trail.RemoveAt(0);
            originalType1TrailEffect?.SetTrail(originalType1Trail);
            if (originalType1Trail.Count == 0) { Expire(); return true; }
            return false;
        }

        // Unity terrain adapter: swept center ray against non-mech, non-trigger
        // world colliders. Canon stops at the nearest wall; Canon2 keeps its endpoint
        // and emits its +38 visual for each crossed collider, without consuming life.
        var worldHits = FindCannonWorldContacts(start,transform.position);
        if (!penetrating && worldHits.Count > 0) transform.position = worldHits[0].point;
        originalType1Trail.Add(transform.position);
        while (originalType1Trail.Count > Mathf.Max(1,trailPointCount)) originalType1Trail.RemoveAt(0);
        originalType1TrailEffect?.SetTrail(originalType1Trail);
        if (session != null)
        {
            foreach (var candidate in session.QueryAttackTargets(OwnerHandle,transform.position))
            {
                if (type11NextHitTick.TryGetValue(candidate.Handle.MechId,out int next) && session.Tick < next) continue;
                for (int i=1; i<originalType1Trail.Count; i++)
                {
                    if (!TestPlayCombatCore.IntersectsTrailSegmentCapsule(originalType1Trail[i-1],originalType1Trail[i],
                        candidate.ShotCapsuleStart,candidate.ShotCapsuleEnd,candidate.ShotCapsuleRadius)) continue;
                    var hit=CreateHit();
                    if (session.ResolveHit(AttackId,OwnerHandle,candidate.Handle,hit,originalType1Trail[i-1],false,0,out hit))
                    {
                        if (hit.decision != TestPlayCombatHitDecision.Ignored && hit.decision != TestPlayCombatHitDecision.Invulnerable)
                            type11NextHitTick[candidate.Handle.MechId]=session.Tick+10;
                        if (owner != null && owner.playModeActive && owner.currentHP>0) owner.NotifyProjectileHit(hit);
                        if (hit.decision == TestPlayCombatHitDecision.Damaged || hit.decision == TestPlayCombatHitDecision.Guarded)
                        {
                            Vector3 center=(candidate.ShotCapsuleStart+candidate.ShotCapsuleEnd)*.5f;
                            FinishCannonContact(center,false);
                            if (!penetrating) return false;
                        }
                        // Canon also has the no-op +3c callback: reflection keeps owner/direction.
                    }
                    break;
                }
            }
        }
        else if (target != null && target.IsAlive)
        {
            int id = target.GetEntityId().GetHashCode();
            for(int i=1;i<originalType1Trail.Count;i++)
            {
                if (type11NextHitTick.TryGetValue(id,out int next) && cannonTick < next) break;
                if (!TestPlayCombatCore.IntersectsType1TrailSegmentSphere(originalType1Trail[i-1],originalType1Trail[i],target.transform.position,target.hitRadius)) continue;
                var hit=target.ResolveImpact(CreateHit(),originalType1Trail[i-1],false,false,0);
                if (hit.decision != TestPlayCombatHitDecision.Ignored && hit.decision != TestPlayCombatHitDecision.Invulnerable)
                    type11NextHitTick[id] = cannonTick+10;
                owner?.NotifyProjectileHit(hit);
                if(hit.decision==TestPlayCombatHitDecision.Damaged || hit.decision==TestPlayCombatHitDecision.Guarded)
                { FinishCannonContact(target.transform.position,false); if (!penetrating) return false; }
                break;
            }
        }
        foreach (var worldHit in worldHits)
        {
            FinishCannonContact(worldHit.point,true);
            if (!penetrating) break;
        }
        return false;
    }

    void FinishCannonContact(Vector3 position,bool terrain)
    {
        bool penetrating = collisionKind == TestPlayAttackCollisionKind.OriginalType26;
        // LZ_Canon2 +30/+34/+38 callbacks never reset life; +3c is inherited no-op.
        if (!penetrating) remainingActiveTicks=0;
        owner?.SpawnCannonImpact(position,transform.forward,terrain,penetrating);
    }

    internal static List<RaycastHit> FindCannonWorldContacts(Vector3 start,Vector3 end)
    {
        var contacts = new List<RaycastHit>();
        Vector3 delta=end-start;
        float distance=delta.magnitude;
        if(distance<=.000001f) return contacts;
        foreach(var hit in Physics.RaycastAll(start,delta/distance,distance,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            var c=hit.collider;
            if(c is CharacterController || c.GetComponentInParent<TestPlayProjectile>()!=null ||
                c.GetComponentInParent<RoboStructure>()!=null || c.GetComponentInParent<TestPlayController>()!=null ||
                c.GetComponentInParent<TestPlayTargetDummy>()!=null) continue;
            contacts.Add(hit);
        }
        contacts.Sort((a,b)=> {
            int order = a.distance.CompareTo(b.distance);
            return order != 0 ? order : a.collider.GetEntityId().GetHashCode().CompareTo(b.collider.GetEntityId().GetHashCode());
        });
        return contacts;
    }

    bool SimulateOriginalType1Tick()
    {
        bool type11Core = collisionKind == TestPlayAttackCollisionKind.OriginalType11;
        if (originalType1Fading && !type11Core)
            return SimulateOriginalType1FadeTick();

        bool targetAlive = target != null && target.IsAlive;
        Vector3 targetPosition = targetAlive ? target.transform.position : Vector3.zero;
        if (session != null)
        {
            targetAlive = session.TryGet(guidance, out var snapshot);
            targetPosition = targetAlive ? snapshot.Position : Vector3.zero;
            if (targetAlive && (collisionKind == TestPlayAttackCollisionKind.OriginalType6 ||
                                collisionKind == TestPlayAttackCollisionKind.OriginalType11))
                targetPosition = (snapshot.ShotCapsuleStart + snapshot.ShotCapsuleEnd) * 0.5f;
        }
        TestPlayType1ProjectileTickResult result = TestPlayCombatCore.TickOriginalType1Projectile(
            new TestPlayType1ProjectileTickInput
            {
                position = transform.position,
                rotation = transform.rotation,
                remainingActiveTicks = type11Core && remainingActiveTicks <= 0 ? 1 : remainingActiveTicks,
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

        if (result.expired)
        {
            if (collisionKind != TestPlayAttackCollisionKind.OriginalType1 &&
                collisionKind != TestPlayAttackCollisionKind.OriginalType11)
            {
                Expire();
                return true;
            }

            // FUN_00460200 moves first, decrements the lifetime, disables the
            // active/collision branch, and starts its 0.01-per-tick width fade
            // on this same update.
            originalType1Fading = true;
            return SimulateOriginalType1FadeTick();
        }

        if (session != null)
        {
            foreach (var candidate in session.QueryAttackTargets(OwnerHandle, transform.position))
            {
                bool type11 = collisionKind == TestPlayAttackCollisionKind.OriginalType11;
                if (type11 ? type11NextHitTick.TryGetValue(candidate.Handle.MechId, out int nextHit) && session.Tick < nextHit
                           : sessionHitTargets.Contains(candidate.Handle.MechId)) continue;
                for (int i = 1; i < originalType1Trail.Count; i++)
                {
                    if (!TestPlayCombatCore.IntersectsTrailSegmentCapsule(originalType1Trail[i - 1],
                        originalType1Trail[i], candidate.ShotCapsuleStart, candidate.ShotCapsuleEnd,
                        candidate.ShotCapsuleRadius)) continue;
                    var hit = CreateHit();
                    if (session.ResolveHit(AttackId, OwnerHandle, candidate.Handle, hit,
                        originalType1Trail[i - 1], false, 0, out hit))
                    {
                        if (type11)
                        {
                            if (hit.decision != TestPlayCombatHitDecision.Ignored &&
                                hit.decision != TestPlayCombatHitDecision.Invulnerable)
                                type11NextHitTick[candidate.Handle.MechId] = session.Tick + 10;
                        }
                        else sessionHitTargets.Add(candidate.Handle.MechId);
                        if (owner != null && owner.playModeActive && owner.currentHP > 0) owner.NotifyProjectileHit(hit);
                        if (type11 && (hit.decision == TestPlayCombatHitDecision.Damaged ||
                                       hit.decision == TestPlayCombatHitDecision.Guarded))
                        {
                            // LZ_Bullet hit/guard sets life to zero; next update begins fade.
                            remainingActiveTicks = 0;
                            return false;
                        }
                        // LZ_Bullet vtable 0x5b75bc + 0x3c is FUN_0048a080 (no-op).
                        // A successful LaserReflect check suppresses damage but does NOT
                        // redirect this concrete class or transfer its owner. Preserve trail,
                        // guidance and lifetime; the registered target interval is 10 ticks.
                        if (type11 && hit.decision == TestPlayCombatHitDecision.Reflected) break;
                        if (collisionKind == TestPlayAttackCollisionKind.OriginalType6 &&
                            hit.decision != TestPlayCombatHitDecision.Ignored &&
                            hit.decision != TestPlayCombatHitDecision.Invulnerable)
                        {
                            // LZ_Bullet::hit (FUN_00462490) clears its active life.
                            remainingActiveTicks = 0;
                            Expire();
                            return true;
                        }
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
                if (collisionKind == TestPlayAttackCollisionKind.OriginalType11)
                {
                    remainingActiveTicks = 0;
                    return false;
                }
                if (collisionKind == TestPlayAttackCollisionKind.OriginalType6)
                {
                    remainingActiveTicks = 0;
                    Expire();
                    return true;
                }
                break;
            }
        }

        return false;
    }

    bool SimulateOriginalType1FadeTick()
    {
        bool alive = TestPlayCombatCore.AdvanceOriginalType1Fade(ref originalType1FadeWidth);
        originalType1TrailEffect?.SetWidth(originalType1FadeWidth);
        if (alive)
            return false;
        Expire();
        return true;
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

    internal TestPlayCombatHitResult CreateHit()
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
