using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class TestPlayRuntimeVerification
{
    const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    [MenuItem("Tools/WindomXP/Test Play/Run Runtime Verification")]
    public static void RunFromMenu()
    {
        WindomVerificationRunner.StartVerification(WindomVerificationSelection.Runtime);
    }

    public static int RunAll()
    {
        int assertions = 0;
        VerifyParserAndState(ref assertions);
        VerifyOriginalAnimationInitialScriptBoundary(ref assertions);
        VerifySptStatusInitialization(ref assertions);
        VerifyNestedIf(ref assertions);
        VerifyFlowInterruption(ref assertions);
        VerifyRepeatInterval(ref assertions);
        VerifyOriginalAttackAndBurnerArguments(ref assertions);
        VerifyOriginalBurnerDrawPath(ref assertions);
        VerifyOriginalDefenseSemantics(ref assertions);
        VerifyOriginalNormalAttackFlow(ref assertions);
        VerifyOriginalBasicAniChannelsAndJump(ref assertions);
        VerifyOriginalSoundIds(ref assertions);
        VerifyOriginalTextureTables(ref assertions);
        VerifyBurnerDirectionAndVisual(ref assertions);
        VerifyOriginalSimulationClockAndLock(ref assertions);
        assertions += VerifyProjectileLockGating();
        VerifyCameraControllerLifecycle(ref assertions);
        VerifyMovementReferenceModes(ref assertions);
        VerifyOriginalDirectionMovement(ref assertions);
        VerifyOriginalJumpAndBoost(ref assertions);
        assertions += TestPlayPhase1Verification.RunAll();
        assertions += TestPlayPhase2Verification.RunAll();
        assertions += TestPlayPhase3Verification.RunAll();
        assertions += TestPlayPhase4Verification.RunAll();
        assertions += TestPlayPhase5Verification.RunAll();
        assertions += TestPlayPhase6Verification.RunAll();
        return assertions;
    }

    public static int VerifyProjectileLockGating()
    {
        int assertions = 0;
        GameObject host = new GameObject("ProjectileLockVerification");
        GameObject root = new GameObject("ProjectileLockRoot");
        GameObject muzzle = new GameObject("ProjectileLockMuzzle");
        GameObject dummy = new GameObject("ProjectileLockTarget");
        TestPlayController controller = host.AddComponent<TestPlayController>();
        try
        {
            RoboStructure robo = host.AddComponent<RoboStructure>();
            robo.root = root;
            controller.robo = robo;
            controller.sptSource = host.AddComponent<UI_SPT>();
            TestPlayTargetDummy target = dummy.AddComponent<TestPlayTargetDummy>();
            target.logHits = false;
            controller.target = target;
            muzzle.transform.SetPositionAndRotation(new Vector3(3f, 2f, 1f), Quaternion.Euler(-12f, 35f, 7f));
            SptRuntimeData data = new SptRuntimeData();
            data.WeaponPoints[0] = new WeaponPointInfo { BoneTr = muzzle.transform };
            SetField(controller.sptSource, "<LastSptData>k__BackingField", data);
            dummy.transform.position = muzzle.transform.TransformPoint(new Vector3(2f, 0f, 20f));
            MethodInfo spawn = typeof(TestPlayController).GetMethod("SpawnRunProc", BindingFlags.Instance | BindingFlags.NonPublic);
            List<GameObject> transients = (List<GameObject>)GetField(controller, "spawnedTransientObjects");
            TestPlayProjectile FireType1()
            {
                spawn.Invoke(controller, new object[] { Values(0f, 1f, 0f, 0f, 30f, 100f, 10f, 100f, -1f, 0f, 0f, 0f), true });
                return transients[transients.Count - 1].GetComponent<TestPlayProjectile>();
            }

            controller.ClearTargetLock();
            TestPlayProjectile free = FireType1();
            Require(Vector3.Distance(free.transform.position, muzzle.transform.position) < 0.00001f &&
                    Vector3.Angle(free.transform.forward, muzzle.transform.forward) < 0.01f,
                "unlocked type 1 uses the rotated WEAPONPOINT position and forward even with a living target inside the aim cone", ref assertions);
            Require(free.target == target && free.maximumHomingTurnDegrees == 0f,
                "unlocked type 1 retains collision target without acquiring guidance", ref assertions);
            Require(controller.TryAcquireTargetLock(), "projectile probe acquires a valid lock", ref assertions);
            for (int tick = 0; tick < 5; tick++) free.SimulateOriginalTick(1f / 60f);
            Require(Vector3.Distance(free.transform.position, muzzle.transform.position + muzzle.transform.forward * 5f) < 0.0001f &&
                    Vector3.Angle(free.transform.forward, muzzle.transform.forward) < 0.01f,
                "a projectile fired unlocked remains straight after the owner acquires a lock", ref assertions);
            TestPlayProjectile locked = FireType1();
            Require(Vector3.Angle(locked.transform.forward, dummy.transform.position - muzzle.transform.position) < 0.01f &&
                    locked.maximumHomingTurnDegrees > 0f,
                "locked type 1 preserves initial aim and distance-dependent homing", ref assertions);
            dummy.transform.position += muzzle.transform.right * 4f;
            Quaternion beforeTurn = locked.transform.rotation;
            locked.SimulateOriginalTick(1f / 60f);
            Require(Quaternion.Angle(beforeTurn, locked.transform.rotation) > 0.01f,
                "locked type 1 still turns toward a moving target", ref assertions);

            controller.ClearTargetLock();
            TestPlayProjectile released = FireType1();
            Require(Vector3.Angle(released.transform.forward, muzzle.transform.forward) < 0.01f && released.maximumHomingTurnDegrees == 0f,
                "a shot after lock release returns to muzzle-forward flight", ref assertions);
            controller.lockedTarget = target;
            controller.targetLockActive = true;
            controller.fallbackLockDistance = 1f;
            TestPlayProjectile invalid = FireType1();
            Require(Vector3.Angle(invalid.transform.forward, muzzle.transform.forward) < 0.01f && invalid.maximumHomingTurnDegrees == 0f,
                "a stale out-of-range lock cannot aim or guide a new shot", ref assertions);

            controller.ClearTargetLock();
            dummy.transform.position = muzzle.transform.position + muzzle.transform.forward * 2f;
            float hpBefore = target.hp;
            TestPlayProjectile directHit = FireType1();
            directHit.SimulateOriginalTick(1f / 60f);
            directHit.SimulateOriginalTick(1f / 60f);
            Require(target.hp < hpBefore, "unlocked straight shots still damage a target on the muzzle axis", ref assertions);

            MethodInfo fallbackSpawn = typeof(TestPlayController).GetMethod("SpawnProjectile", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(float), typeof(float), typeof(bool) }, null);
            fallbackSpawn.Invoke(controller, new object[] { "LockProbe", 1f, 20f, true });
            TestPlayProjectile fallback = transients[transients.Count - 1].GetComponent<TestPlayProjectile>();
            Require(fallback.target == target && fallback.homingTurnRate == 0f,
                "fallback projectiles also require a lock for guidance while retaining collisions", ref assertions);
            controller.fallbackLockDistance = 100f;
            controller.TryAcquireTargetLock();
            fallbackSpawn.Invoke(controller, new object[] { "LockProbe", 1f, 20f, true });
            Require(transients[transients.Count - 1].GetComponent<TestPlayProjectile>().homingTurnRate > 0f,
                "fallback homing remains available with a valid lock", ref assertions);
        }
        finally
        {
            foreach (GameObject transient in (List<GameObject>)GetField(controller, "spawnedTransientObjects"))
                if (transient != null) UnityEngine.Object.DestroyImmediate(transient);
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(muzzle);
            UnityEngine.Object.DestroyImmediate(dummy);
        }
        return assertions;
    }

    static void VerifyParserAndState(ref int assertions)
    {
        TestPlayStateTable state = new TestPlayStateTable();
        state.ResetDefaults();
        TestPlayScriptVM vm = new TestPlayScriptVM(state);
        List<string> commands = new List<string>();
        int fallbackAssignments = 0;
        vm.commandHandler = (name, args, raw) => commands.Add(name);
        vm.assignmentHandler = (name, op, values, raw) => fallbackAssignments++;

        vm.Execute("@int[0]=2; @int[0]+=3; Move(1,STOP,2); BURNER(4,0.5); ' ignored\r\n@float[1]=1.25;");

        Require(state.GetInt(0) == 5, "compound @int assignment", ref assertions);
        Require(Mathf.Approximately(state.GetFloat(1), 1.25f), "invariant @float literal", ref assertions);
        Require(commands.Count == 2 && commands[0] == "Move" && commands[1] == "BURNER", "semicolon command splitting", ref assertions);

        vm.Execute("@int[200]=99;");
        Require(state.GetInt(200) == 0, "script variables are limited to 0..199", ref assertions);
        Require(fallbackAssignments == 1, "out-of-range reference is diagnosed as unsupported", ref assertions);
    }

    static void VerifyOriginalAnimationInitialScriptBoundary(ref int assertions)
    {
        GameObject controllerObject = new GameObject("TestPlayVerification_InitialScriptController");
        GameObject rootObject = new GameObject("TestPlayVerification_InitialScriptRoot");
        try
        {
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            robo.root = rootObject;
            robo.ani = new ani2 { animations = new List<animation>() };
            for (int i = 0; i < 100; i++)
            {
                robo.ani.animations.Add(new animation
                {
                    name = "InitialBoundaryAction" + i,
                    frames = new List<hod2v1>(),
                    scripts = new List<script>()
                });
            }

            animation baseAction = robo.ani.animations[0];
            baseAction.squirrelInit = "@int[42]+=100;";
            baseAction.scripts.Add(new script { unk = 5, time = 0f, squirrel = "@int[42]+=1;" });

            animation swordPose = robo.ani.animations[50];
            swordPose.squirrelInit = "@int[42]+=1000;";
            swordPose.frames.Add(new hod2v1("InitialBoundarySwordPose") { parts = new List<hod2v1_Part>() });

            animation transitionAction = robo.ani.animations[1];
            transitionAction.squirrelInit = "@int[42]+=10000;";
            transitionAction.scripts.Add(new script { unk = 5, time = 0f, squirrel = "@int[42]+=10;" });

            // An init-only +50 entry is neither an executable script source nor
            // a usable pose in the original record layout.
            robo.ani.animations[51].squirrelInit = "@int[42]+=100000;";

            controller.robo = robo;
            controller.state.ResetDefaults();

            MethodInfo awake = typeof(TestPlayController).GetMethod("Awake", InstancePrivate);
            MethodInfo setWeapon = typeof(TestPlayController).GetMethod("SetHeldWeapon", InstancePrivate);
            MethodInfo resolveSelection = typeof(TestPlayController).GetMethod("ResolveActionSelection", InstancePrivate);
            MethodInfo tickAnimation = typeof(TestPlayController).GetMethod("TickAnimation", InstancePrivate);
            MethodInfo restartAnimation = typeof(TestPlayController).GetMethod(
                "ChangeAnimation",
                InstancePrivate,
                null,
                new[] { typeof(int), typeof(bool) },
                null);
            Require(awake != null && setWeapon != null && resolveSelection != null &&
                    tickAnimation != null && restartAnimation != null,
                "initial-script boundary helpers are available", ref assertions);

            awake.Invoke(controller, null);
            setWeapon.Invoke(controller, new object[] { "SWORD" });

            TestPlayActionSelection fallback = (TestPlayActionSelection)resolveSelection.Invoke(
                controller,
                new object[] { 0 });
            Require(fallback.poseActionId == 50 && fallback.scriptActionId == 0,
                "+50 pose keeps the base timed blocks even when both AN2 init texts are non-empty",
                ref assertions);

            controller.state.SetInt(42, 0);
            controller.ChangeAnimation(0);
            tickAnimation.Invoke(controller, null);
            Require(controller.state.GetInt(42) == 2,
                "first action entry executes the timed block on both original channels without executing script-index -1 text",
                ref assertions);

            restartAnimation.Invoke(controller, new object[] { 0, true });
            tickAnimation.Invoke(controller, null);
            Require(controller.state.GetInt(42) == 4,
                "same-action re-entry executes both timed channels without script-index -1 text",
                ref assertions);

            TestPlayActionSelection initOnlyVariant = (TestPlayActionSelection)resolveSelection.Invoke(
                controller,
                new object[] { 1 });
            Require(initOnlyVariant.poseActionId == 1 && initOnlyVariant.scriptActionId == 1,
                "an init-only +50 entry is not selected as an executable action",
                ref assertions);

            controller.ChangeAnimation(1);
            tickAnimation.Invoke(controller, null);
            Require(controller.state.GetInt(42) == 24,
                "action transition executes only the destination timed block on both original channels",
                ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }

    static void VerifyOriginalDefenseSemantics(ref int assertions)
    {
        Require(TestPlayCombatCore.ResolveHitReactionState(0) == 1 &&
                TestPlayCombatCore.ResolveHitReactionState(1) == 2 &&
                TestPlayCombatCore.ResolveHitReactionState(8) == 3 &&
                TestPlayCombatCore.ResolveHitReactionState(9) == 3 &&
                TestPlayCombatCore.ResolveHitReactionState(0x40) == 0,
            "AttackFlag 1/8 reaction priority and bit 0x40 normal-reaction suppression",
            ref assertions);

        TestPlayDefenseHitInput input = new TestPlayDefenseHitInput
        {
            collisionKind = TestPlayAttackCollisionKind.OriginalType1,
            defenderForwardDotToAttacker = 1f
        };
        input.targetIsAttackOwner = true;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Ignored,
            "AttackFlag bit 0x04 is required for owner/self collision", ref assertions);
        input.attackFlag = 0x04;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Damaged,
            "AttackFlag bit 0x04 permits owner/self collision", ref assertions);

        input = new TestPlayDefenseHitInput
        {
            collisionKind = TestPlayAttackCollisionKind.OriginalType1,
            shieldGuardValue = 1,
            defenderForwardDotToAttacker = TestPlayCombatCore.OriginalType1GuardDotThreshold
        };
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Guarded,
            "type 1 guards at the original 0.1736 forward-dot boundary", ref assertions);
        input.defenderForwardDotToAttacker = TestPlayCombatCore.OriginalType1GuardDotThreshold - 0.0001f;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Damaged,
            "type 1 does not guard below the original forward-dot boundary", ref assertions);
        input.defenderForwardDotToAttacker = 1f;
        input.attackFlag = 0x10;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Damaged,
            "AttackFlag bit 0x10 pierces ShildGuard for collision type 1", ref assertions);

        input = new TestPlayDefenseHitInput
        {
            collisionKind = TestPlayAttackCollisionKind.OriginalType11,
            shieldGuardValue = 2,
            defenderForwardDotToAttacker = TestPlayCombatCore.OriginalType11GuardDotThreshold
        };
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Guarded,
            "type 11 accepts ShildGuard value 2 at the original 0.766 boundary", ref assertions);
        input.defenderForwardDotToAttacker = TestPlayCombatCore.OriginalType11GuardDotThreshold - 0.0001f;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Damaged,
            "type 11 does not guard below the original forward-dot boundary", ref assertions);

        input = new TestPlayDefenseHitInput
        {
            collisionKind = TestPlayAttackCollisionKind.OriginalType57,
            shieldGuardValue = 1,
            defenderForwardDotToAttacker = TestPlayCombatCore.OriginalType57GuardDotThreshold,
            sourceIsCharacter = true,
            meleeHitStopTicks = 5
        };
        TestPlayDefenseHitResult swordGuard = TestPlayCombatCore.ResolveDefenseHit(input);
        Require(swordGuard.decision == TestPlayCombatHitDecision.Guarded &&
                swordGuard.guardHitTimerTicks == 20 &&
                swordGuard.attackerGuardReactionTicks == 2 &&
                swordGuard.applyAttackerGuardRecoil &&
                swordGuard.defenderHitStopTicks == 0,
            "type 57 ShildGuard value 1 applies the confirmed guard feedback without hit-stop",
            ref assertions);
        input.shieldGuardValue = 2;
        TestPlayDefenseHitResult swordGuard2 = TestPlayCombatCore.ResolveDefenseHit(input);
        Require(swordGuard2.decision == TestPlayCombatHitDecision.Guarded &&
                swordGuard2.guardHitTimerTicks == 20 &&
                swordGuard2.attackerGuardReactionTicks == 0 &&
                !swordGuard2.applyAttackerGuardRecoil,
            "type 57 ShildGuard value 2 guards without the value-1 feedback branch",
            ref assertions);

        input = new TestPlayDefenseHitInput
        {
            collisionKind = TestPlayAttackCollisionKind.OriginalType11,
            attackFlag = 0x02,
            reflectionProbabilityPercent = 50,
            reflectionRoll = 49
        };
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Reflected,
            "AttackFlag bit 0x02 reflects when the explicit original probability roll succeeds",
            ref assertions);
        input.reflectionRoll = 50;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Damaged,
            "AttackFlag bit 0x02 uses a strict roll-less-than-probability boundary",
            ref assertions);
        input.reflectionProbabilityPercent = 128;
        input.reflectionRoll = 0;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Damaged,
            "LaserReflect preserves the original signed-char read for byte values 128..255",
            ref assertions);
        input.reflectionProbabilityPercent = 276;
        input.reflectionRoll = 19;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Reflected &&
                TestPlayCombatCore.NormalizeOriginalLaserReflectValue(276) == 20,
            "LaserReflect stores the low byte before the type 11 probability comparison",
            ref assertions);
        input.reflectionProbabilityPercent = 50;
        input.reflectionRoll = 0;
        input.shieldGuardValue = 1;
        input.defenderForwardDotToAttacker = 1f;
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Guarded,
            "type 11 evaluates directional guard before its reflection branch", ref assertions);

        input = new TestPlayDefenseHitInput
        {
            collisionKind = TestPlayAttackCollisionKind.OriginalType57,
            attackFlag = 0x20,
            hitAcceptanceBlockTicks = 1,
            meleeHitStopTicks = 5
        };
        Require(TestPlayCombatCore.ResolveDefenseHit(input).decision == TestPlayCombatHitDecision.Invulnerable,
            "nonzero @int[158] blocks hit acceptance", ref assertions);
        input.hitAcceptanceBlockTicks = 0;
        TestPlayDefenseHitResult swordHit = TestPlayCombatCore.ResolveDefenseHit(input);
        Require(swordHit.decision == TestPlayCombatHitDecision.Damaged &&
                swordHit.clearLinkedTarget && swordHit.forceFacingToAttacker &&
                swordHit.defenderHitStopTicks == 5 && swordHit.attackerHitStopTicks == 5,
            "AttackFlag bit 0x20 and type 57 p2 hit-stop are preserved in the damage decision",
            ref assertions);

        Require(TestPlayCombatCore.TickPositiveTimer(2) == 1 &&
                TestPlayCombatCore.TickPositiveTimer(1) == 0 &&
                TestPlayCombatCore.TickPositiveTimer(0) == 0,
            "@int[157], @int[158], and c50 timers decrement with a zero boundary",
            ref assertions);
        Require(TestPlayCombatCore.ResolveRunProcCollisionKind(1) == TestPlayAttackCollisionKind.OriginalType1 &&
                TestPlayCombatCore.ResolveRunProcCollisionKind(11) == TestPlayAttackCollisionKind.OriginalType11 &&
                TestPlayCombatCore.ResolveRunProcCollisionKind(57) == TestPlayAttackCollisionKind.OriginalType57 &&
                TestPlayCombatCore.ResolveRunProcCollisionKind(62) == TestPlayAttackCollisionKind.Unspecified,
            "only confirmed RunProc collision classes 1/11/57 are classified", ref assertions);

        GameObject targetObject = new GameObject("TestPlayVerification_DefenseTarget");
        try
        {
            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();
            target.logHits = false;
            target.hp = 100f;
            target.shildGuard = 1;
            targetObject.transform.position = Vector3.forward * 2f;
            targetObject.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            TestPlayCombatHitResult hit = new TestPlayCombatHitResult
            {
                source = "RunProc2:57",
                damage = 25f,
                attackFlag = 0,
                collisionKind = TestPlayAttackCollisionKind.OriginalType57
            };
            hit = target.ResolveImpact(hit, Vector3.zero, false, true, 5);
            Require(hit.decision == TestPlayCombatHitDecision.Guarded &&
                    Mathf.Approximately(target.hp, 100f) && target.guardHitTimerTicks == 20,
                "TargetDummy adapter blocks type 57 damage and stores @int[157] on front guard",
                ref assertions);

            target.shildGuard = 0;
            target.hitAcceptanceBlockTicks = 1;
            hit = target.ResolveImpact(hit, Vector3.zero, false, true, 5);
            Require(hit.decision == TestPlayCombatHitDecision.Invulnerable &&
                    Mathf.Approximately(target.hp, 100f),
                "TargetDummy adapter blocks damage while @int[158] is nonzero", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
        }
    }

    static void VerifyOriginalBurnerDrawPath(ref int assertions)
    {
        TestPlayOriginalBurnerDrawParameters normal =
            TestPlayPresentationCore.CreateOriginalNormalBurnerDrawParameters(true, 2f, 0.25f);
        Require(normal.ownerKind == TestPlayOriginalBurnerOwnerKind.NormalRobot &&
                normal.requested && Mathf.Approximately(normal.aniOutput, 0.25f) &&
                Mathf.Approximately(normal.drawValue, 2f),
            "normal-robot BURNER draw value uses the configured SPT value without ANI output multiplication",
            ref assertions);
        Require(Mathf.Approximately(normal.primarySizeArgument, 1f) &&
                Mathf.Approximately(normal.primaryLengthArgument, 2f) &&
                Mathf.Approximately(normal.secondarySizeArgument, 2f / 3f) &&
                Mathf.Approximately(normal.secondaryLocalZArgument, 0.25f),
            "normal BURNER derives the original 1/2, 1, 1/3, and 1/8 draw arguments",
            ref assertions);

        TestPlayOriginalBurnerDrawParameters zeroOutput =
            TestPlayPresentationCore.CreateOriginalNormalBurnerDrawParameters(true, 2f, 0f);
        Require(zeroOutput.requested && Mathf.Approximately(zeroOutput.drawValue, 2f),
            "ANI output zero does not clear the original BURNER request byte or SPT draw value",
            ref assertions);

        TestPlayOriginalBurnerDrawParameters ship =
            TestPlayPresentationCore.CreateOriginalShipBurnerDrawParameters(2f, 0.3f);
        TestPlayOriginalBurnerDrawParameters reverseShip =
            TestPlayPresentationCore.CreateOriginalShipBurnerDrawParameters(2f, -0.3f);
        Require(ship.ownerKind == TestPlayOriginalBurnerOwnerKind.Ship &&
                Mathf.Approximately(ship.drawValue, 6f) &&
                Mathf.Approximately(reverseShip.drawValue, 0f),
            "CShip-only BURNER path applies configured value times max speed times ten",
            ref assertions);

        TestPlayOriginalBurnerPrimaryUpdateResult primary = default;
        for (int i = 0; i < TestPlayPresentationCore.OriginalBurnerPrimaryExpiryUpdate - 1; i++)
            primary = TestPlayPresentationCore.AdvanceOriginalBurnerPrimary(false, 1f, primary.updateCount);
        Require(!primary.expiredAfterUpdate && primary.updateCount == 10 &&
                Mathf.Approximately(primary.matrixAdjustmentArgument, -1f / 15f),
            "BB_Burner remains active through update ten and forwards the confirmed matrix adjustment argument",
            ref assertions);
        primary = TestPlayPresentationCore.AdvanceOriginalBurnerPrimary(false, 1f, primary.updateCount);
        Require(primary.expiredAfterUpdate && primary.updateCount == 11,
            "BB_Burner expires when its update counter reaches eleven",
            ref assertions);
        TestPlayOriginalBurnerPrimaryUpdateResult detachedPrimary =
            TestPlayPresentationCore.AdvanceOriginalBurnerPrimary(true, 1f, 3);
        Require(detachedPrimary.expiredAfterUpdate && detachedPrimary.updateCount == 3 &&
                Mathf.Approximately(detachedPrimary.matrixAdjustmentArgument, 0f),
            "BB_Burner expires without another matrix update when its owner is marked for deletion",
            ref assertions);

        TestPlayOriginalBurnerBallUpdateResult ball = default;
        ball.remainingCounter = TestPlayPresentationCore.OriginalBurnerBallInitialCounter;
        for (int i = 0; i < 4; i++)
            ball = TestPlayPresentationCore.AdvanceOriginalBurnerBall(false, ball.remainingCounter);
        Require(!ball.expiredAfterUpdate && ball.remainingCounter == 0 &&
                Mathf.Approximately(ball.matrixBasisMultiplier, 0.95f),
            "BB_BurnerBall applies the confirmed 0.95 matrix multiplier and remains active at counter zero",
            ref assertions);
        ball = TestPlayPresentationCore.AdvanceOriginalBurnerBall(false, ball.remainingCounter);
        Require(ball.expiredAfterUpdate && ball.remainingCounter == -1,
            "BB_BurnerBall expires on its fifth update after the counter becomes negative",
            ref assertions);
        TestPlayOriginalBurnerBallUpdateResult detachedBall =
            TestPlayPresentationCore.AdvanceOriginalBurnerBall(true, 4);
        Require(detachedBall.expiredAfterUpdate && detachedBall.remainingCounter == 4 &&
                Mathf.Approximately(detachedBall.matrixBasisMultiplier, 1f),
            "BB_BurnerBall expires without matrix scaling when its owner is marked for deletion",
            ref assertions);

        List<TestPlayScriptValue> variant0Arguments =
            Values(0f, 62f, 0f, 3f, 350f, 350f, 0f, 0f, 0f, 0f, 0f, 0f);
        Require(TestPlayPresentationCore.TryCreateOriginalBurnerBurstParameters(
                    true,
                    variant0Arguments,
                    out TestPlayBurnerBurstParameters burst) &&
                burst.weaponPointId == 0 && burst.variant == 0 &&
                Mathf.Approximately(burst.primarySize, 3.5f) &&
                Mathf.Approximately(burst.primaryLength, 3.5f) &&
                Mathf.Approximately(burst.secondarySize, 1.75f) &&
                Mathf.Approximately(burst.secondaryLocalZ, 0.4375f) &&
                burst.primaryTextureId == 8 && burst.secondaryTextureId == 9,
            "type 62 subtype 3 variant zero resolves the confirmed dimensions and texture pair",
            ref assertions);
        Require(TestPlayPresentationCore.TryCreateOriginalBurnerBurstParameters(
                    true,
                    Values(0f, 62f, 28f, 3f, 300f, 300f, 1f, 7f, 8f, 9f, 10f, 11f),
                    out TestPlayBurnerBurstParameters alternateBurst) &&
                alternateBurst.weaponPointId == 28 && alternateBurst.variant == 1 &&
                alternateBurst.primaryTextureId == 43 && alternateBurst.secondaryTextureId == 44 &&
                alternateBurst.unusedP7 == 7 && alternateBurst.unusedP11 == 11,
            "type 62 subtype 3 variant one resolves texture pair 43/44 and preserves unread p7-p11",
            ref assertions);
        Require(!TestPlayPresentationCore.TryCreateOriginalBurnerBurstParameters(
                    true,
                    Values(0f, 62f, 0f, 3f, 350f, 350f, 2f, 0f, 0f, 0f, 0f, 0f),
                    out _),
            "type 62 subtype 3 rejects an unobserved variant instead of guessing a texture pair",
            ref assertions);

        TestPlayBurnerBurstState burstState =
            TestPlayPresentationCore.CreateOriginalBurnerBurstState();
        for (int i = 0; i < 5; i++)
            TestPlayPresentationCore.AdvanceOriginalBurnerBurst(false, burst, ref burstState);
        Require(burstState.primaryAlive && !burstState.secondaryAlive &&
                burstState.primaryUpdateCount == 5 && burstState.secondaryRemainingCounter == -1 &&
                Mathf.Approximately(burstState.primaryExpansionPerSide, 3.5f / 3f) &&
                Mathf.Approximately(burstState.secondaryBasisScale, Mathf.Pow(0.95f, 5f)),
            "subtype 3 keeps BB_Burner after BB_BurnerBall independently expires on update five",
            ref assertions);
        bool burstAlive = true;
        for (int i = 5; i < 11; i++)
            burstAlive = TestPlayPresentationCore.AdvanceOriginalBurnerBurst(false, burst, ref burstState);
        Require(!burstAlive && !burstState.primaryAlive && burstState.primaryUpdateCount == 11 &&
                Mathf.Approximately(burstState.primaryExpansionPerSide, 3.5f * 11f / 15f),
            "subtype 3 removes its remaining BB_Burner on update eleven",
            ref assertions);
    }

    static void VerifySptStatusInitialization(ref int assertions)
    {
        SptRuntimeData data = SptParser.Parse(
            "Name=StatusTest;\n" +
            "NameEng=StatusTestEN;\n" +
            "HP=5200;\n" +
            "Generator=3600;\n" +
            "Energy=1700;\n" +
            "Score=800;\n" +
            "RestBody=2;\n" +
            "LockDist=125;\n" +
            "SubLockDist(0,90);\n" +
            "SubLockDist=3,45;\n" +
            "WEAPONPOINT(1,Weapon_point2.x,UP);\n" +
            "WEAPONPOINT(49,Foot.x,DOWN);\n" +
            "WEAPONPOINT(50,Invalid.x,UP);\n" +
            "ATTACKARMSET(0,ArmAim.x);\n" +
            "ATTACKARMSET(2,InvalidArm.x);\n" +
            "GUNFILENAME(0,Gun.x);\n" +
            "GUNFILENAME(9,Sword_dammy.x);\n" +
            "GUNFILENAME(20,InvalidGun.x);\n" +
            "SWORDFILENAME(0,Sword.x);\n" +
            "SWORDFILENAME(9,Gun_dammy.x);\n");

        Require(data.HasHP && data.HP == 5200, "SPT HP is parsed with presence", ref assertions);
        Require(data.HasGenerator && data.Generator == 3600, "SPT Generator is parsed", ref assertions);
        Require(data.HasEnergy && data.Energy == 1700, "SPT Energy is parsed separately", ref assertions);
        Require(data.HasScore && data.Score == 800 && data.HasRestBody && data.RestBody == 2,
            "SPT Score and RestBody are parsed", ref assertions);
        Require(data.HasLockDist && Mathf.Approximately(data.LockDist, 125f), "SPT LockDist is parsed", ref assertions);
        Require(data.SubLockDistances.Count == 2 && data.SubLockDistances[0] == 90 && data.SubLockDistances[3] == 45,
            "SPT SubLockDist command and compatibility forms are parsed", ref assertions);
        Require(data.WeaponPoints.Count == 2 && data.WeaponPoints[1].FrameName == "Weapon_point2" &&
                data.WeaponPoints[1].Direction == SptDirection.UP &&
                data.WeaponPoints[49].Direction == SptDirection.DOWN,
            "SPT WEAPONPOINT accepts original indices 0..49 and normalizes .x names", ref assertions);
        Require(data.AttackArms.Count == 1 && data.AttackArms[0].FrameName == "ArmAim" &&
                data.GunModels.Count == 2 && data.GunModels[0].FrameName == "Gun" &&
                data.GunModels[9].FrameName == "Sword_dammy" &&
                data.SwordModels.Count == 2 && data.SwordModels[0].FrameName == "Sword" &&
                data.SwordModels[9].FrameName == "Gun_dammy",
            "SPT attack-arm and weapon-model definitions enforce original ranges and normalize .x names",
            ref assertions);

        GameObject sptObject = new GameObject("TestPlayVerification_SPT");
        GameObject controllerObject = new GameObject("TestPlayVerification_SPTController");
        try
        {
            GameObject weaponPointObject = new GameObject("Weapon_point2.x");
            weaponPointObject.transform.SetParent(sptObject.transform, false);
            GameObject attackArmObject = new GameObject("ArmAim.x");
            attackArmObject.transform.SetParent(sptObject.transform, false);
            GameObject gunObject = new GameObject("Gun.x");
            gunObject.transform.SetParent(sptObject.transform, false);
            MeshRenderer gunRenderer = gunObject.AddComponent<MeshRenderer>();
            GameObject gunChildObject = new GameObject("GunChild");
            gunChildObject.transform.SetParent(gunObject.transform, false);
            MeshRenderer gunChildRenderer = gunChildObject.AddComponent<MeshRenderer>();
            GameObject swordDummyObject = new GameObject("Sword_dammy.x");
            swordDummyObject.transform.SetParent(sptObject.transform, false);
            MeshRenderer swordDummyRenderer = swordDummyObject.AddComponent<MeshRenderer>();
            GameObject swordObject = new GameObject("Sword.x");
            swordObject.transform.SetParent(sptObject.transform, false);
            MeshRenderer swordRenderer = swordObject.AddComponent<MeshRenderer>();
            GameObject gunDummyObject = new GameObject("Gun_dammy.x");
            gunDummyObject.transform.SetParent(sptObject.transform, false);
            MeshRenderer gunDummyRenderer = gunDummyObject.AddComponent<MeshRenderer>();
            GameObject inspectorArmObject = new GameObject("InspectorArm");
            inspectorArmObject.transform.SetParent(sptObject.transform, false);
            SptParser.BindTransforms(sptObject.transform, data);
            Require(data.WeaponPoints[1].BoneTr == weaponPointObject.transform &&
                    data.WeaponPoints[1].WorldForward == Vector3.forward,
                "SPT WEAPONPOINT binds the real frame name and UP direction", ref assertions);
            Require(data.AttackArms[0].BoneTr == attackArmObject.transform &&
                    data.GunModels[0].BoneTr == gunObject.transform &&
                    data.GunModels[9].BoneTr == swordDummyObject.transform &&
                    data.SwordModels[0].BoneTr == swordObject.transform &&
                    data.SwordModels[9].BoneTr == gunDummyObject.transform,
                "SPT attack-arm and weapon-model definitions bind real frame transforms", ref assertions);

            UI_SPT spt = sptObject.AddComponent<UI_SPT>();
            SetField(spt, "<LastSptData>k__BackingField", data);
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            controller.sptSource = spt;
            controller.state.ResetDefaults();

            MethodInfo initialize = typeof(TestPlayController).GetMethod("InitializeOriginalSptStatus", InstancePrivate);
            MethodInfo energyTick = typeof(TestPlayController).GetMethod("UpdateOriginalMovementEnergy", InstancePrivate);
            MethodInfo resolveArm = typeof(TestPlayController).GetMethod("ResolveArmAimRoot", InstancePrivate);
            MethodInfo spawnRunProc = typeof(TestPlayController).GetMethod("SpawnRunProc", InstancePrivate);
            MethodInfo handleCommand = typeof(TestPlayController).GetMethod("HandleCommand", InstancePrivate);
            initialize.Invoke(controller, null);

            Require(Mathf.Approximately(controller.maximumHP, 5200f) &&
                    Mathf.Approximately(controller.currentHP, 5200f),
                "test player HP is initialized from SPT HP", ref assertions);
            Require(Mathf.Approximately(controller.maximumEnergy, 3600f) &&
                    Mathf.Approximately(controller.currentEnergy, 3600f),
                "movement gauge is initialized from SPT Generator", ref assertions);
            Require(Mathf.Approximately(controller.maximumAuxiliaryEnergy, 1700f) &&
                    Mathf.Approximately(controller.currentAuxiliaryEnergy, 1700f),
                "auxiliary gauge is initialized independently from SPT Energy", ref assertions);
            Require(ReferenceEquals(resolveArm.Invoke(controller, new object[] { 0, null }), attackArmObject.transform) &&
                    ReferenceEquals(resolveArm.Invoke(controller, new object[] { 0, inspectorArmObject.transform }), inspectorArmObject.transform),
                "LockArm uses the Inspector reference first and SPT ATTACKARMSET only as fallback", ref assertions);

            spawnRunProc.Invoke(controller, new object[] { Values(0f, 51f), false });
            Require(gunRenderer.enabled && gunChildRenderer.enabled && swordDummyRenderer.enabled &&
                    !swordRenderer.enabled && !gunDummyRenderer.enabled,
                "original proc type 51 shows Gun and Sword_dammy for gun mode", ref assertions);
            Require(gunObject.activeSelf && gunChildObject.activeSelf && swordDummyObject.activeSelf &&
                    swordObject.activeSelf && gunDummyObject.activeSelf,
                "weapon visibility changes preserve the bound HOD hierarchy and runtime components",
                ref assertions);
            spawnRunProc.Invoke(controller, new object[] { Values(0f, 52f), false });
            Require(!gunRenderer.enabled && !gunChildRenderer.enabled && !swordDummyRenderer.enabled &&
                    swordRenderer.enabled && gunDummyRenderer.enabled,
                "original proc type 52 shows Sword and Gun_dammy for sword mode", ref assertions);
            handleCommand.Invoke(controller, new object[]
            {
                "ChangeWeapon",
                new List<TestPlayScriptValue> { TestPlayScriptValue.Symbol("GUN") },
                "ChangeWeapon(GUN);"
            });
            Require(controller.state.GetInt(152) == 0 && gunRenderer.enabled && gunChildRenderer.enabled &&
                    swordDummyRenderer.enabled && !swordRenderer.enabled && !gunDummyRenderer.enabled,
                "ChangeWeapon(GUN) uses the same four-model visibility as proc type 51", ref assertions);
            handleCommand.Invoke(controller, new object[]
            {
                "ChangeWeapon",
                new List<TestPlayScriptValue> { TestPlayScriptValue.Symbol("SWORD") },
                "ChangeWeapon(SWORD);"
            });
            Require(controller.state.GetInt(152) == 1 && !gunRenderer.enabled && !gunChildRenderer.enabled &&
                    !swordDummyRenderer.enabled && swordRenderer.enabled && gunDummyRenderer.enabled,
                "ChangeWeapon(SWORD) uses the same four-model visibility as proc type 52", ref assertions);
            Require(controller.state.GetInt(100) == 3600 && controller.state.GetInt(101) == 3600 &&
                    controller.state.GetInt(102) == 1700 && controller.state.GetInt(103) == 1700,
                "original integer resource slots 100..103 mirror SPT runtime values", ref assertions);
            Require(controller.configuredScore == 800 && controller.configuredRestBody == 2,
                "SPT Score and RestBody reach test-play runtime state", ref assertions);
            Require(TestPlayHudRuntime.ResolveMechaName(controller) == "StatusTest",
                "test-play HUD resolves the machine name from SPT Name", ref assertions);
            Require(Mathf.Approximately(TestPlayHudRuntime.CalculateFillAmount(900f, 3600f), 0.25f),
                "test-play HUD gauge fill uses the current-to-maximum ratio", ref assertions);

            controller.SetAirborneFlag(true);
            controller.state.SetInt(100, 3500);
            controller.state.SetInt(103, 1600);
            energyTick.Invoke(controller, null);
            Require(Mathf.Approximately(controller.currentEnergy, 3500f),
                "original @int[100] changes update the movement gauge", ref assertions);
            Require(Mathf.Approximately(controller.currentAuxiliaryEnergy, 1600f),
                "original @int[103] changes update the auxiliary gauge", ref assertions);

            controller.ApplySelfDamage(200f);
            Require(Mathf.Approximately(controller.currentHP, 5000f), "self damage uses SPT HP capacity", ref assertions);
            controller.RestoreSelfHP(500f);
            Require(Mathf.Approximately(controller.currentHP, 5200f), "self HP recovery clamps to SPT maximum", ref assertions);
            Require(controller.TryGetSubLockDistance(3, out float subLockDistance) &&
                    Mathf.Approximately(subLockDistance, 45f),
                "parsed SubLockDist is exposed to future sub-target actions", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(sptObject);
        }
    }

    static void VerifyNestedIf(ref int assertions)
    {
        TestPlayStateTable state = new TestPlayStateTable();
        TestPlayScriptVM vm = new TestPlayScriptVM(state);
        vm.Execute(
            "@int[10]=1;" +
            "IF(@int[10],==,1);" +
            " IF(3,>,2); @int[11]=7; ELSE; @int[11]=8; ENDIF;" +
            "ELSE; @int[11]=9; ENDIF;" +
            "IF(2,!=,3); @int[12]=4; ENDIF;");

        Require(state.GetInt(11) == 7, "nested IF/ELSE/ENDIF", ref assertions);
        Require(state.GetInt(12) == 4, "six original IF comparators", ref assertions);
    }

    static void VerifyFlowInterruption(ref int assertions)
    {
        TestPlayStateTable state = new TestPlayStateTable();
        TestPlayScriptVM vm = new TestPlayScriptVM(state);
        List<string> commands = new List<string>();
        bool stop = false;
        vm.commandHandler = (name, args, raw) =>
        {
            commands.Add(name);
            if (name == "GoScriptIndex")
                stop = true;
        };
        vm.shouldStopExecution = () => stop;
        vm.Execute("Snd(1);GoScriptIndex(3);Voice(AfterJump);");

        Require(commands.Count == 2, "flow command stops the remaining instruction scan", ref assertions);
        Require(commands[1] == "GoScriptIndex", "flow stops at the jump command", ref assertions);
    }

    static void VerifyRepeatInterval(ref int assertions)
    {
        GameObject go = new GameObject("TestPlayVerification_Controller");
        try
        {
            TestPlayController controller = go.AddComponent<TestPlayController>();
            SetField(controller, "executeScriptRepeatedly", true);
            SetField(controller, "scriptRepeatInterval", 2);
            SetField(controller, "scriptRepeatCounter", 2);
            MethodInfo consume = typeof(TestPlayController).GetMethod("ConsumeScriptRepeatTick", InstancePrivate);

            Require(!(bool)consume.Invoke(controller, null), "repeat interval tick 1", ref assertions);
            Require(!(bool)consume.Invoke(controller, null), "repeat interval tick 2", ref assertions);
            Require((bool)consume.Invoke(controller, null), "ExecScriptEveryTime(2) repeats on tick 3", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static void VerifyOriginalAttackAndBurnerArguments(ref int assertions)
    {
        GameObject go = new GameObject("TestPlayVerification_Commands");
        try
        {
            TestPlayController controller = go.AddComponent<TestPlayController>();
            controller.logUnhandledCommands = false;
            MethodInfo handle = typeof(TestPlayController).GetMethod("HandleCommand", InstancePrivate);
            SetField(controller, "moveCommand", new Vector3(1f, 2f, 3f));
            handle.Invoke(controller, new object[]
            {
                "Move",
                new List<TestPlayScriptValue>
                {
                    TestPlayScriptValue.Number(0f),
                    TestPlayScriptValue.Stop(),
                    TestPlayScriptValue.Number(4f)
                },
                "Move(0,STOP,4);"
            });
            Vector3 move = (Vector3)GetField(controller, "moveCommand");
            Require(move == new Vector3(1f, 0f, 4f), "Move numeric zero preserves and STOP clears an axis", ref assertions);
            MethodInfo resetBlock = typeof(TestPlayController).GetMethod("ResetOriginalBlockState", InstancePrivate);
            resetBlock.Invoke(controller, null);
            Require((Vector3)GetField(controller, "moveCommand") == new Vector3(1f, 0f, 4f),
                "block entry preserves ANI Move while resetting transient Force state", ref assertions);

            handle.Invoke(controller, new object[]
            {
                "ATTACK",
                Values(80f, 150f, 0.4f, 2f),
                "ATTACK(80,150,0.4,2);"
            });

            Require(controller.attackProfile.power == 80, "ATTACK power", ref assertions);
            Require(controller.attackProfile.down == 150, "ATTACK down", ref assertions);
            Require(Mathf.Approximately(controller.attackProfile.force, 0.4f), "ATTACK force", ref assertions);
            Require(Mathf.Approximately(controller.attackProfile.forceY, 2f), "ATTACK forceY", ref assertions);

            handle.Invoke(controller, new object[]
            {
                "BURNER",
                Values(3f, 0.75f),
                "BURNER(3,0.75);"
            });
            Dictionary<int, float> outputs = (Dictionary<int, float>)GetField(controller, "burnerRequestedOutputs");
            Require(outputs.ContainsKey(3) && Mathf.Approximately(outputs[3], 0.75f), "BURNER output value", ref assertions);

            handle.Invoke(controller, new object[]
            {
                "BURNER",
                Values(20f, 1f),
                "BURNER(20,1);"
            });
            Require(!outputs.ContainsKey(20), "BURNER id range 0..19", ref assertions);

            Require(TestPlayPresentationCore.IsOriginalWindProc(false, 53) &&
                    TestPlayPresentationCore.IsOriginalWindProc(false, 54) &&
                    !TestPlayPresentationCore.IsOriginalWindProc(true, 53) &&
                    TestPlayPresentationCore.GetOriginalWindVisualCount(false, 53) == 7 &&
                    TestPlayPresentationCore.GetOriginalWindVisualCount(false, 54) == 1,
                "RunProc type 53/54 are the confirmed seven-line and one-ring presentation-only handlers",
                ref assertions);
            TestPlayPresentationEvent windProc = TestPlayPresentationCore.CreateProc(
                false,
                Values(0f, 53f, 2f),
                TestPlayPresentationAdapterKind.PrimitiveFallback);
            Require(windProc.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    windProc.procType == 53 && windProc.arguments.Length == 3,
                "RunProc type 53 keeps source arguments while classifying its executable-confirmed visual meaning",
                ref assertions);

            List<TestPlayScriptValue> thunderArguments = Values(
                0f, 60f, 4f, 3f, 20f, 25f, 21f, 100f, 10f, 91f, 92f, 93f);
            TestPlayThunderEffectParameters thunderParameters;
            bool parsedThunderParameters = TestPlayPresentationCore.TryCreateOriginalThunderEffectParameters(
                true,
                thunderArguments,
                out thunderParameters);
            Require(TestPlayPresentationCore.IsOriginalThunderEffectProc(true, 60) &&
                    !TestPlayPresentationCore.IsOriginalThunderEffectProc(false, 60) &&
                    parsedThunderParameters &&
                    thunderParameters.weaponPointId == 4 &&
                    Mathf.Approximately(thunderParameters.width, 0.03f) &&
                    thunderParameters.length == 20 &&
                    Mathf.Approximately(thunderParameters.forwardSpeedPerTick, 0.25f) &&
                    thunderParameters.textureId == 21 &&
                    Mathf.Approximately(thunderParameters.scatterRadius, 1f) &&
                    thunderParameters.activeTicks == 10 &&
                    thunderParameters.unusedP9 == 91 &&
                    thunderParameters.unusedP10 == 92 &&
                    thunderParameters.unusedP11 == 93,
                "RunProc2 type 60 maps the executable-confirmed WEAPONPOINT, scaled fields, texture, lifetime, and unused tail",
                ref assertions);
            float thunderWidth = thunderParameters.width;
            int thunderRemaining = thunderParameters.activeTicks;
            float thunderTravel = 0f;
            int thunderTicks = 0;
            bool thunderAlive = true;
            while (thunderAlive && thunderTicks < 30)
            {
                thunderAlive = TestPlayPresentationCore.AdvanceOriginalThunderEffect(
                    thunderParameters.width,
                    thunderParameters.forwardSpeedPerTick,
                    ref thunderWidth,
                    ref thunderRemaining,
                    ref thunderTravel);
                thunderTicks++;
            }
            Require(!thunderAlive &&
                    thunderTicks == thunderParameters.activeTicks + TestPlayPresentationCore.OriginalThunderFadeTicks - 1 &&
                    thunderRemaining == 0 &&
                    Mathf.Approximately(thunderTravel, thunderTicks * thunderParameters.forwardSpeedPerTick),
                "RunProc2 type 60 applies movement before p8 countdown and the original ten-step p3 fade",
                ref assertions);
            TestPlayPresentationEvent thunderProc = TestPlayPresentationCore.CreateProc(
                true,
                thunderArguments,
                TestPlayPresentationAdapterKind.OriginalTextureQuad);
            Require(thunderProc.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    thunderProc.procType == 60 && thunderProc.arguments.Length == 12,
                "RunProc2 type 60 keeps all source arguments while classifying confirmed LZ_ThunderEffect semantics",
                ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static void VerifyOriginalNormalAttackFlow(ref int assertions)
    {
        GameObject controllerObject = new GameObject("TestPlayVerification_NormalAttackController");
        GameObject rootObject = new GameObject("TestPlayVerification_NormalAttackRoot");
        GameObject posePartObject = new GameObject("TestPlayVerification_NormalAttackPosePart");
        GameObject targetObject = new GameObject("TestPlayVerification_NormalAttackTarget");
        GameObject weaponPointObject = new GameObject("Weapon_point2.x");
        GameObject spawnedProjectile = null;
        GameObject spawnedType1Projectile = null;
        try
        {
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();
            target.logHits = false;
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            posePartObject.transform.SetParent(rootObject.transform, false);
            weaponPointObject.transform.SetParent(rootObject.transform, false);
            targetObject.transform.position = Vector3.forward * 2f;
            robo.root = rootObject;
            robo.parts.Add(rootObject);
            robo.parts.Add(posePartObject);
            robo.ani = new ani2 { animations = new List<animation>() };
            for (int i = 0; i < 200; i++)
            {
                robo.ani.animations.Add(new animation
                {
                    name = "VerificationAction" + i,
                    frames = new List<hod2v1>(),
                    scripts = new List<script> { new script { unk = 1, time = 0f, squirrel = "" } }
                });
            }

            // Real switch blocks gate ChangeWeapon behind the standing/lower-body
            // state.  Keep that condition in the probe so a zero/default-state
            // regression cannot hide behind a direct SetHeldWeapon call.
            robo.ani.animations[controller.switchToSwordAction].scripts = new List<script>
            {
                new script
                {
                    unk = 1,
                    time = 0f,
                    squirrel = "IF(@int[151],==,1);\nChangeWeapon(SWORD);\nENDIF;"
                }
            };
            robo.ani.animations[controller.switchToGunAction].scripts = new List<script>
            {
                new script
                {
                    unk = 1,
                    time = 0f,
                    squirrel = "IF(@int[151],==,1);\nChangeWeapon(GUN);\nENDIF;"
                }
            };
            robo.ani.animations[controller.neutralMeleeAction].scripts = new List<script>
            {
                new script { unk = 1, time = 0f, squirrel = "" },
                new script
                {
                    unk = 10,
                    time = 0.05f,
                    squirrel = "IF(@int[151],==,0);\nSwordCancel=132;\nENDIF;"
                }
            };
            robo.ani.animations[133].scripts = new List<script>();
            robo.ani.animations[133].frames = new List<hod2v1>
            {
                new hod2v1("ComboEnd0") { parts = new List<hod2v1_Part>() },
                new hod2v1("ComboEnd1") { parts = new List<hod2v1_Part>() },
                new hod2v1("ComboEnd2") { parts = new List<hod2v1_Part>() }
            };
            int swordRecoveryAction = controller.stepLandingAction + 50;
            hod2v1_Part rootPose = new hod2v1_Part
            {
                rotation = Quaternion.identity,
                scale = Vector3.one,
                position = Vector3.zero
            };
            hod2v1_Part standingPose = new hod2v1_Part
            {
                rotation = Quaternion.identity,
                scale = Vector3.one,
                position = new Vector3(0f, 1f, 0f)
            };
            hod2v1_Part recoveryPose = new hod2v1_Part
            {
                rotation = Quaternion.identity,
                scale = Vector3.one,
                position = new Vector3(0f, -1f, 0f)
            };
            robo.ani.animations[controller.idleAction].frames = new List<hod2v1>
            {
                new hod2v1("Standing") { parts = new List<hod2v1_Part> { rootPose, standingPose } }
            };
            robo.ani.animations[controller.stepLandingAction].scripts = new List<script>
            {
                new script { unk = 2, time = 0.5f, squirrel = "" }
            };
            robo.ani.animations[controller.stepLandingAction].frames = new List<hod2v1>
            {
                new hod2v1("Recovery") { parts = new List<hod2v1_Part> { rootPose, recoveryPose } }
            };
            robo.ani.animations[swordRecoveryAction].scripts = new List<script>();
            robo.ani.animations[swordRecoveryAction].frames = new List<hod2v1>
            {
                new hod2v1("Recovery0") { parts = new List<hod2v1_Part>() },
                new hod2v1("Recovery1") { parts = new List<hod2v1_Part>() }
            };
            robo.ani.animations[controller.idleAction + 50].scripts = new List<script>();
            robo.ani.animations[controller.idleAction + 50].frames = new List<hod2v1>
            {
                new hod2v1("SwordIdle") { parts = new List<hod2v1_Part>() }
            };
            robo.ani.animations[controller.moveAction].scripts = new List<script>
            {
                new script { unk = 4, time = 0.5f, squirrel = "AnimeLoop=1;" }
            };
            robo.ani.animations[controller.moveAction + 50].scripts = new List<script>();
            robo.ani.animations[controller.moveAction + 50].frames = new List<hod2v1>
            {
                new hod2v1("SwordWalk0") { parts = new List<hod2v1_Part>() },
                new hod2v1("SwordWalk1") { parts = new List<hod2v1_Part>() },
                new hod2v1("SwordWalk2") { parts = new List<hod2v1_Part>() }
            };

            controller.robo = robo;
            controller.target = target;
            TestPlayPresentationRuntime presentation = controllerObject.AddComponent<TestPlayPresentationRuntime>();
            presentation.controller = controller;
            presentation.logMissingAudio = false;
            presentation.originalEffectShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/TestPlayOriginalEffect.shader");
            Texture2D swordTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Generated/TestPlay/OriginalTextures/11_sabel.png");
            Texture2D swordLineTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Generated/TestPlay/OriginalTextures/12_sabel_line.png");
            Texture2D thunderTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Generated/TestPlay/OriginalTextures/06_laser2.bmp");
            Texture2D windRingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Generated/TestPlay/OriginalTextures/19_WindRing.png");
            Texture2D hinokoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Generated/TestPlay/OriginalTextures/39_hinoko.png");
            Texture2D burnerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Generated/TestPlay/OriginalTextures/07_burner.png");
            Texture2D burner2Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Generated/TestPlay/OriginalTextures/08_burner2.png");
            presentation.originalTextures.Add(new TestPlayTextureBinding
            {
                loadSequence = 11,
                scriptTextureId = 12,
                originalFileName = "sabel.png",
                texture = swordTexture
            });
            presentation.originalTextures.Add(new TestPlayTextureBinding
            {
                loadSequence = 12,
                scriptTextureId = 13,
                originalFileName = "sabel_line.png",
                texture = swordLineTexture
            });
            presentation.originalTextures.Add(new TestPlayTextureBinding
            {
                loadSequence = 6,
                scriptTextureId = 7,
                originalFileName = "laser2.bmp",
                texture = thunderTexture
            });
            presentation.originalTextures.Add(new TestPlayTextureBinding
            {
                loadSequence = 19,
                scriptTextureId = TestPlayPresentationCore.OriginalWindRingTextureId,
                originalFileName = TestPlayPresentationCore.OriginalWindRingTextureFileName,
                texture = windRingTexture
            });
            presentation.originalTextures.Add(new TestPlayTextureBinding
            {
                loadSequence = 39,
                scriptTextureId = 40,
                originalFileName = "hinoko.png",
                texture = hinokoTexture
            });
            presentation.originalTextures.Add(new TestPlayTextureBinding
            {
                loadSequence = 7,
                scriptTextureId = 8,
                originalFileName = "burner.png",
                texture = burnerTexture
            });
            presentation.originalTextures.Add(new TestPlayTextureBinding
            {
                loadSequence = 8,
                scriptTextureId = 9,
                originalFileName = "burner2.png",
                texture = burner2Texture
            });
            GameObject magicShieldPrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            magicShieldPrefab.name = "TestPlayVerification_MagicShieldModelSlot1";
            magicShieldPrefab.transform.SetParent(controllerObject.transform, false);
            magicShieldPrefab.SetActive(false);
            presentation.effects.Add(new TestPlayEffectBinding
            {
                key = "RunProc2:62:8:ModelSlot1",
                prefab = magicShieldPrefab
            });
            controller.presentationRuntime = presentation;
            UI_SPT spt = controllerObject.AddComponent<UI_SPT>();
            SptRuntimeData meleeSptData = SptParser.Parse(
                "WEAPONPOINT(0,Weapon_point2.x,UP);\nWEAPONPOINT(1,Weapon_point2.x,UP);");
            SptParser.BindTransforms(rootObject.transform, meleeSptData);
            SetField(spt, "<LastSptData>k__BackingField", meleeSptData);
            controller.sptSource = spt;
            controller.maximumEnergy = 100f;
            controller.currentEnergy = 100f;
            controller.state.ResetDefaults();
            controller.state.SetInt(100, 100);
            controller.state.SetFloat(100, 100f);

            MethodInfo setWeapon = typeof(TestPlayController).GetMethod("SetHeldWeapon", InstancePrivate);
            MethodInfo resolveShot = typeof(TestPlayController).GetMethod("ResolveShotInputAction", InstancePrivate);
            MethodInfo resolveShotSelection = typeof(TestPlayController).GetMethod("ResolveShotActionSelection", InstancePrivate);
            MethodInfo resolveMelee = typeof(TestPlayController).GetMethod("ResolveMeleeInputAction", InstancePrivate);
            MethodInfo resolveWeaponAction = typeof(TestPlayController).GetMethod("ResolveActionForWeaponMode", InstancePrivate);
            MethodInfo handle = typeof(TestPlayController).GetMethod("HandleCommand", InstancePrivate);
            MethodInfo handleAssignment = typeof(TestPlayController).GetMethod("HandleAssignment", InstancePrivate);
            MethodInfo resetBlock = typeof(TestPlayController).GetMethod("ResetOriginalBlockState", InstancePrivate);
            MethodInfo spawnRunProc = typeof(TestPlayController).GetMethod("SpawnRunProc", InstancePrivate);
            MethodInfo tickMeleeAttacks = typeof(TestPlayController).GetMethod("TickActiveMeleeAttacks", InstancePrivate);
            MethodInfo tickSwordBeams = typeof(TestPlayController).GetMethod("TickActiveSwordBeams", InstancePrivate);
            MethodInfo tickThunderEffects = typeof(TestPlayController).GetMethod("TickActiveThunderEffects", InstancePrivate);
            MethodInfo tickWindRingSpecialEffects = typeof(TestPlayController).GetMethod("TickActiveWindRingSpecialEffects", InstancePrivate);
            MethodInfo tickBurnerBurstEffects = typeof(TestPlayController).GetMethod("TickActiveBurnerBurstEffects", InstancePrivate);
            MethodInfo tickHinokoEffects = typeof(TestPlayController).GetMethod("TickActiveHinokoEffects", InstancePrivate);
            MethodInfo tickMagicShieldEffects = typeof(TestPlayController).GetMethod("TickActiveMagicShieldEffects", InstancePrivate);
            MethodInfo updateCombatTimers = typeof(TestPlayController).GetMethod("UpdateOriginalCombatTimers", InstancePrivate);
            MethodInfo updateCooldowns = typeof(TestPlayController).GetMethod("UpdateOriginalAttackCooldowns", InstancePrivate);
            MethodInfo updateAttack = typeof(TestPlayController).GetMethod("TryUpdateNormalAttackSequence", InstancePrivate);
            MethodInfo updateInput = typeof(TestPlayController).GetMethod("UpdateInputState", InstancePrivate);
            MethodInfo awake = typeof(TestPlayController).GetMethod("Awake", InstancePrivate);
            MethodInfo tickAnimation = typeof(TestPlayController).GetMethod("TickAnimation", InstancePrivate);
            MethodInfo startGroundRecovery = typeof(TestPlayController).GetMethod("StartGroundRecoverySequence", InstancePrivate);
            MethodInfo startNormalAttack = typeof(TestPlayController).GetMethod("StartNormalAttackAction", InstancePrivate);
            MethodInfo finishNormalAttack = typeof(TestPlayController).GetMethod("FinishNormalAttackSequence", InstancePrivate);
            MethodInfo completeGroundRecovery = typeof(TestPlayController).GetMethod("CompleteGroundRecoverySequence", InstancePrivate);
            MethodInfo applyPose = typeof(TestPlayController).GetMethod("ApplyPose", InstancePrivate);
            MethodInfo applyRootMotion = typeof(TestPlayController).GetMethod("ApplyRootMotion", InstancePrivate);
            MethodInfo getOneShot = typeof(TestPlayController).GetMethod("GetOneShotActionFromInput", InstancePrivate);
            MethodInfo applyShotSteering = typeof(TestPlayController).GetMethod("ApplyOriginalShotSteering", InstancePrivate);
            MethodInfo energyTick = typeof(TestPlayController).GetMethod("UpdateOriginalMovementEnergy", InstancePrivate);
            MethodInfo spawnProjectile = typeof(TestPlayController).GetMethod(
                "SpawnProjectile",
                InstancePrivate,
                null,
                new[] { typeof(string), typeof(float), typeof(float), typeof(bool) },
                null);
            Require(setWeapon != null && resolveShot != null && resolveShotSelection != null &&
                    resolveMelee != null && resolveWeaponAction != null &&
                    handle != null && handleAssignment != null && spawnRunProc != null && tickMeleeAttacks != null &&
                    tickSwordBeams != null && tickThunderEffects != null && tickHinokoEffects != null &&
                    tickWindRingSpecialEffects != null &&
                    tickBurnerBurstEffects != null &&
                    tickMagicShieldEffects != null &&
                    updateCombatTimers != null &&
                    updateCooldowns != null && updateAttack != null &&
                    updateInput != null && awake != null && tickAnimation != null && startGroundRecovery != null &&
                    startNormalAttack != null &&
                    finishNormalAttack != null && completeGroundRecovery != null && applyPose != null &&
                    applyRootMotion != null && getOneShot != null && applyShotSteering != null &&
                    energyTick != null && spawnProjectile != null,
                "original normal-attack runtime helpers are available", ref assertions);

            Require(presentation.originalEffectShader != null && swordTexture != null && swordLineTexture != null &&
                    thunderTexture != null && windRingTexture != null && hinokoTexture != null &&
                    burnerTexture != null && burner2Texture != null,
                "original sword-beam, thunder, WindRing, hinoko, and burner-pair textures are available", ref assertions);
            awake.Invoke(controller, null);
            controller.SetAirborneFlag(false);
            Require(controller.state.GetInt(150) == 0,
                "original @int[150] is zero while grounded", ref assertions);
            controller.SetAirborneFlag(true);
            Require(controller.state.GetInt(150) == 1,
                "original @int[150] is one while airborne", ref assertions);
            controller.SetAirborneFlag(false);
            setWeapon.Invoke(controller, new object[] { "GUN" });
            Require(controller.state.GetInt(151) == 1,
                "normal test-play state starts on ANI execution channel 1", ref assertions);
            Require((int)resolveShot.Invoke(controller, null) == controller.shotAction,
                "gun-mode X selects action 100", ref assertions);

            TestPlayShotActionDecision forwardShot = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                100, Vector3.forward, Vector3.forward, action => true);
            TestPlayShotActionDecision leftShot = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                100, Vector3.forward, Vector3.left, action => true);
            TestPlayShotActionDecision rightShot = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                100, Vector3.forward, Vector3.right, action => true);
            TestPlayShotActionDecision rearShot = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                100, Vector3.forward, Vector3.back, action => true);
            TestPlayShotActionDecision unavailableVariant = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                100, Vector3.forward, Vector3.right, action => action == 100);
            TestPlayShotActionDecision missingSideUsesRear = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                100, Vector3.forward, Vector3.right, action => action == 100 || action == 103);
            TestPlayShotActionDecision boostLeftShot = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                106, Vector3.forward, Vector3.left, action => true);
            TestPlayShotActionDecision boostRightShot = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                106, Vector3.forward, Vector3.right, action => true);
            TestPlayShotActionDecision boostRearShot = TestPlayCombatCore.ResolveTargetRelativeShotAction(
                106, Vector3.forward, Vector3.back, action => true);
            Require(forwardShot.selectedActionId == 100 && !forwardShot.usesDirectionalVariant,
                "target-relative shot keeps base action 100 inside the forward cone", ref assertions);
            Require(leftShot.selectedActionId == 102 && leftShot.scriptActionId == 100 &&
                    leftShot.usesDirectionalVariant && leftShot.usesDualChannels,
                "left target selects pose 102 with base script 100 on dual channels", ref assertions);
            Require(rightShot.selectedActionId == 101 && rightShot.scriptActionId == 100 &&
                    rightShot.usesDirectionalVariant && rightShot.usesDualChannels,
                "right target selects pose 101 with base script 100 on dual channels", ref assertions);
            Require(rearShot.selectedActionId == 103 && rearShot.scriptActionId == 103 &&
                    rearShot.usesDirectionalVariant && rearShot.usesDualChannels,
                "rear target selects self-scripted action 103 on dual channels", ref assertions);
            Require(unavailableVariant.selectedActionId == 100 &&
                    !unavailableVariant.usesDirectionalVariant,
                "missing side and rear variants fall back to the base shot", ref assertions);
            Require(missingSideUsesRear.selectedActionId == 103 &&
                    missingSideUsesRear.scriptActionId == 103,
                "missing side variant uses rear action 103 when available", ref assertions);
            Require(boostLeftShot.selectedActionId == 108 && boostLeftShot.scriptActionId == 106 &&
                    boostRightShot.selectedActionId == 107 && boostRightShot.scriptActionId == 106 &&
                    boostRearShot.selectedActionId == 103 && boostRearShot.scriptActionId == 103,
                "boost shot 106 resolves left 108, right 107, and rear 103", ref assertions);

            controller.lockedTarget = target;
            controller.targetLockActive = true;
            targetObject.transform.position = Vector3.left * 2f;
            Require((int)resolveShot.Invoke(controller, null) == 102,
                "locked left target selects grounded shot action 102", ref assertions);
            targetObject.transform.position = Vector3.right * 2f;
            Require((int)resolveShot.Invoke(controller, null) == 101,
                "locked right target selects grounded shot action 101", ref assertions);
            targetObject.transform.position = Vector3.back * 2f;
            Require((int)resolveShot.Invoke(controller, null) == 103,
                "locked rear target selects grounded shot action 103", ref assertions);
            TestPlayActionSelection rearSelection = (TestPlayActionSelection)resolveShotSelection.Invoke(
                controller, new object[] { 103 });
            Require(rearSelection.requestedActionId == controller.shotAction &&
                    rearSelection.logicalActionId == 103 && rearSelection.poseActionId == 103 &&
                    rearSelection.scriptActionId == 103 && rearSelection.primaryChannel == 0 &&
                    rearSelection.secondaryChannel == 1,
                "rear shot keeps base request identity while action 103 supplies pose and script", ref assertions);
            targetObject.transform.position = Vector3.forward * 2f;
            Require((int)resolveMelee.Invoke(controller, new object[] { 8 }) == controller.switchToSwordAction,
                "gun-mode C first selects weapon-switch action 18", ref assertions);

            controller.ChangeAnimation(controller.switchToSwordAction);
            tickAnimation.Invoke(controller, null);
            Require(controller.state.GetInt(152) == 1 && controller.heldWeapon == "SWORD" &&
                    controller.currentAnimationIndex == controller.idleAction + 50,
                "action 18 conditional ChangeWeapon switches to sword before returning to the sword idle table", ref assertions);
            controller.ChangeAnimation(controller.moveAction);
            tickAnimation.Invoke(controller, null);
            tickAnimation.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.moveAction + 50 &&
                    ReferenceEquals(GetField(controller, "currentScriptAnimation"), robo.ani.animations[controller.moveAction]) &&
                    controller.frameIndex == 1,
                "sword walk uses action 51 poses with action 1 script timing", ref assertions);
            controller.ChangeAnimation(controller.switchToGunAction);
            tickAnimation.Invoke(controller, null);
            Require(controller.state.GetInt(152) == 0 && controller.heldWeapon == "GUN" &&
                    controller.currentAnimationIndex == controller.idleAction,
                "action 68 conditional ChangeWeapon switches back to gun", ref assertions);

            SetField(controller, "sampledShotKeyHeld", true);
            updateInput.Invoke(controller, null);
            Require((int)getOneShot.Invoke(controller, null) == controller.shotAction,
                "the initial X press edge starts a shot", ref assertions);
            updateInput.Invoke(controller, null);
            Require(controller.state.GetInt(192) == 1 && (int)getOneShot.Invoke(controller, null) == -1,
                "holding X keeps its script state but does not auto-repeat the attack", ref assertions);
            SetField(controller, "sampledShotKeyHeld", false);
            updateInput.Invoke(controller, null);

            controller.blendActionTransitions = false;
            controller.ChangeAnimation(controller.idleAction);
            controller.SetAirborneFlag(false);
            controller.state.SetInt(190, 0);
            startNormalAttack.Invoke(controller, new object[] { controller.shotAction });
            finishNormalAttack.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.stepLandingAction &&
                    controller.CurrentActionSelection.logicalActionId == controller.stepLandingAction &&
                    (bool)GetField(controller, "presentStandingPoseDuringGroundedShotRecovery"),
                "grounded action-100 completion keeps logical action 6 while presenting the standing pose",
                ref assertions);
            applyPose.Invoke(controller, null);
            Require(posePartObject.transform.localPosition == standingPose.position &&
                    posePartObject.transform.localPosition != recoveryPose.position,
                "grounded action-100 recovery applies the standing HOD instead of the action-6 landing HOD",
                ref assertions);
            int suppressedLandingSounds = presentation.SuppressedSoundPlaybackCount;
            handle.Invoke(controller, new object[] { "Snd", Values(2f), "Snd(2);" });
            Require(presentation.SuppressedSoundPlaybackCount == suppressedLandingSounds + 1,
                "grounded stationary action-100 recovery suppresses action-6 Snd(2) playback",
                ref assertions);
            completeGroundRecovery.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.idleAction &&
                    !(bool)GetField(controller, "presentStandingPoseDuringGroundedShotRecovery"),
                "grounded shot recovery clears its standing-pose presentation adapter on idle entry",
                ref assertions);

            controller.ChangeAnimation(controller.moveAction);
            controller.state.SetInt(190, 8);
            startNormalAttack.Invoke(controller, new object[] { controller.shotAction });
            finishNormalAttack.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.stepLandingAction &&
                    !(bool)GetField(controller, "presentStandingPoseDuringGroundedShotRecovery"),
                "a grounded action-100 shot started from movement keeps the normal action-6 recovery pose",
                ref assertions);
            suppressedLandingSounds = presentation.SuppressedSoundPlaybackCount;
            handle.Invoke(controller, new object[] { "Snd", Values(2f), "Snd(2);" });
            Require(presentation.SuppressedSoundPlaybackCount == suppressedLandingSounds,
                "ordinary action-6 recovery keeps Snd(2) playback enabled",
                ref assertions);
            completeGroundRecovery.Invoke(controller, null);
            controller.state.SetInt(190, 0);
            controller.blendActionTransitions = true;

            controller.currentAnimationIndex = controller.boostAction;
            SetField(controller, "boostMotionActive", true);
            SetField(controller, "actionTick", 5);
            Require((int)resolveShot.Invoke(controller, null) == -1,
                "boost X is ignored through the original first five ticks", ref assertions);
            SetField(controller, "actionTick", 6);
            Require((int)resolveShot.Invoke(controller, null) == controller.boostShotAction,
                "boost X after tick 5 selects original action 106", ref assertions);

            controller.currentAnimationIndex = controller.idleAction;
            SetField(controller, "boostMotionActive", false);
            setWeapon.Invoke(controller, new object[] { "SWORD" });
            Require((int)resolveShot.Invoke(controller, null) == controller.switchToGunAction,
                "sword-mode X first selects weapon-switch action 68", ref assertions);
            controller.currentAnimationIndex = controller.boostAction + 50;
            SetField(controller, "boostMotionActive", true);
            Require((int)resolveShot.Invoke(controller, null) == controller.boostShotAction,
                "boost action 106 takes precedence over sword-mode weapon switching", ref assertions);
            controller.currentAnimationIndex = controller.idleAction + 50;
            SetField(controller, "boostMotionActive", false);
            Require((int)resolveMelee.Invoke(controller, new object[] { 8 }) == controller.meleeAction,
                "forward C selects melee approach action 130", ref assertions);
            Require((int)resolveMelee.Invoke(controller, new object[] { 0 }) == controller.neutralMeleeAction &&
                    (int)resolveMelee.Invoke(controller, new object[] { 4 }) == controller.leftMeleeAction &&
                    (int)resolveMelee.Invoke(controller, new object[] { 6 }) == controller.rightMeleeAction &&
                    (int)resolveMelee.Invoke(controller, new object[] { 2 }) == controller.backMeleeAction,
                "neutral/left/right/back C select actions 131/141/146/151", ref assertions);
            Require((int)resolveWeaponAction.Invoke(controller, new object[] { controller.moveAction }) == controller.moveAction + 50,
                "sword mode resolves basic action IDs through the original +50 table", ref assertions);

            rootObject.transform.rotation = Quaternion.identity;
            controller.currentAnimationIndex = 103;
            controller.state.SetInt(190, 4);
            SetField(controller, "attackSequenceActive", true);
            SetField(controller, "shotTurnAng", 20f);
            applyShotSteering.Invoke(controller, new object[] { rootObject.transform });
            Require(Mathf.Abs(Vector3.Angle(Vector3.forward, rootObject.transform.forward) - 20f) < 0.01f &&
                    rootObject.transform.forward.x < 0f,
                "ShotTurnAng steers target-relative action 103 left by the scripted angle", ref assertions);
            rootObject.transform.rotation = Quaternion.identity;

            handle.Invoke(controller, new object[] { "AttackDelay", Values(0f, 3f), "AttackDelay(0,3);" });
            Require(controller.GetAttackCooldownTicks(0) == 3, "AttackDelay stores the X cooldown slot", ref assertions);
            updateCooldowns.Invoke(controller, null);
            Require(controller.GetAttackCooldownTicks(0) == 2, "attack cooldowns decrement once per original tick", ref assertions);

            target.hp = 1000f;
            handle.Invoke(controller, new object[] { "ATTACK", Values(37f, 44f, 2f, 3f), "ATTACK(37,44,2,3);" });
            handleAssignment.Invoke(controller, new object[] { "AttackFlag", "=", Values(9f), "AttackFlag=9;" });
            Require(Mathf.Approximately(target.hp, 1000f),
                "ATTACK configures a profile without applying an immediate hit", ref assertions);

            List<TestPlayPresentationEvent> windPresentationEvents = new List<TestPlayPresentationEvent>();
            controller.PresentationEventRaised += presentationEvent => windPresentationEvents.Add(presentationEvent);
            List<GameObject> windTransients =
                (List<GameObject>)GetField(controller, "spawnedTransientObjects");
            int windStart = windTransients.Count;
            spawnRunProc.Invoke(controller, new object[] { Values(0f, 53f, 2f), false });
            int windLineEnd = windTransients.Count;
            bool windLinesValid = windLineEnd - windStart == TestPlayPresentationCore.OriginalWindLineCount;
            for (int i = windStart; i < windLineEnd; i++)
            {
                GameObject windObject = windTransients[i];
                TestPlayWindEffect effect = windObject != null ? windObject.GetComponent<TestPlayWindEffect>() : null;
                windLinesValid &= effect != null && effect.Kind == TestPlayWindEffectKind.WindLine &&
                    Mathf.Approximately(effect.DisplayWidth, TestPlayPresentationCore.OriginalWindLineWidth) &&
                    Mathf.Approximately(effect.DisplayLength, TestPlayPresentationCore.OriginalWindLineLength) &&
                    windObject.GetComponent<TestPlayProjectile>() == null;
            }
            Require(windLinesValid,
                "RunProc type 53 creates seven non-combat BB_WindLine adapters with confirmed 0.07 x 3.0 dimensions and ignores p3",
                ref assertions);

            spawnRunProc.Invoke(controller, new object[] { Values(1f, 54f, 0f), false });
            int windRingEnd = windTransients.Count;
            GameObject ringObject = windRingEnd == windLineEnd + 1 ? windTransients[windLineEnd] : null;
            TestPlayWindEffect ringEffect = ringObject != null ? ringObject.GetComponent<TestPlayWindEffect>() : null;
            Require(ringEffect != null && ringEffect.Kind == TestPlayWindEffectKind.WindRing &&
                    Mathf.Approximately(ringEffect.DisplayRadius, TestPlayPresentationCore.OriginalWindRingRadius) &&
                    ringObject.GetComponent<TestPlayProjectile>() == null,
                "RunProc type 54 creates one non-combat BB_WindRing2 adapter",
                ref assertions);

            int proc53Events = 0;
            int proc54Events = 0;
            int visual53Events = 0;
            int visual54Events = 0;
            for (int i = 0; i < windPresentationEvents.Count; i++)
            {
                TestPlayPresentationEvent presentationEvent = windPresentationEvents[i];
                if (presentationEvent.type == TestPlayPresentationEventType.Proc &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed)
                {
                    if (presentationEvent.procType == 53) proc53Events++;
                    if (presentationEvent.procType == 54) proc54Events++;
                }
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc:53")
                    visual53Events++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc:54")
                    visual54Events++;
            }
            Require(proc53Events == 1 && proc54Events == 1 &&
                    visual53Events == TestPlayPresentationCore.OriginalWindLineCount && visual54Events == 1,
                "RunProc type 53/54 trace records confirmed Proc semantics and every Unity visual adapter",
                ref assertions);
            for (int i = windStart; i < windRingEnd; i++)
            {
                if (windTransients[i] != null)
                    UnityEngine.Object.DestroyImmediate(windTransients[i]);
            }

            int swordBeamStart = windTransients.Count;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(1f, 55f, 1f, 200f, 12f, 13f, 0f, 0f, 0f, 0f, 0f, 35f), true
            });
            GameObject swordBeamObject = windTransients.Count == swordBeamStart + 1
                ? windTransients[swordBeamStart]
                : null;
            TestPlaySwordBeamEffect swordBeam = swordBeamObject != null
                ? swordBeamObject.GetComponent<TestPlaySwordBeamEffect>()
                : null;
            MethodInfo updateSwordMotionBlur = typeof(TestPlaySwordBeamEffect).GetMethod(
                "UpdateMotionBlur",
                InstancePrivate);
            Require(swordBeam != null && swordBeam.Anchor == weaponPointObject.transform &&
                    swordBeam.PrimaryTextureId == 12 && swordBeam.LineTextureId == 13 &&
                    swordBeam.HasPrimaryLayer && swordBeam.HasLineLayer &&
                    Mathf.Approximately(swordBeam.CurrentLength, 0f) &&
                    Mathf.Approximately(swordBeam.TargetLength, 2f) &&
                    swordBeamObject.GetComponent<TestPlayProjectile>() == null &&
                    Mathf.Approximately(target.hp, 1000f),
                "RunProc2 type 55 creates the two-layer non-combat sword beam on WEAPONPOINT p2",
                ref assertions);
            Transform primaryPlane0 = swordBeam != null ? swordBeam.GetPrimaryPlane(0) : null;
            Transform primaryPlane1 = swordBeam != null ? swordBeam.GetPrimaryPlane(1) : null;
            Require(updateSwordMotionBlur != null && primaryPlane0 != null && primaryPlane1 != null &&
                    swordBeam.PrimaryPlaneCount == 2 && swordBeam.LinePlaneCount == 2 &&
                    Vector3.Dot(primaryPlane0.TransformDirection(Vector3.up).normalized,
                        swordBeam.transform.forward) > 0.999f &&
                    Vector3.Dot(primaryPlane1.TransformDirection(Vector3.up).normalized,
                        swordBeam.transform.forward) > 0.999f &&
                    Mathf.Abs(Vector3.Dot(primaryPlane0.forward, primaryPlane1.forward)) < 0.001f &&
                    !swordBeam.IsLineBlurVisible,
                "both vertical saber textures follow local Z+ while crossed, and Beam_Line starts hidden",
                ref assertions);
            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(1f, 2f, 3f),
                Quaternion.Euler(0f, 30f, 0f));
            tickSwordBeams.Invoke(controller, null);
            Require(swordBeam != null && Mathf.Approximately(swordBeam.CurrentLength, 0.2f) &&
                    swordBeam.transform.parent == weaponPointObject.transform &&
                    swordBeam.transform.localPosition == Vector3.zero &&
                    swordBeam.transform.localRotation == Quaternion.identity,
                "RunProc2 type 55 grows by 0.2 per tick while following its WEAPONPOINT anchor",
                ref assertions);
            updateSwordMotionBlur.Invoke(swordBeam, null);
            Transform linePlane0 = swordBeam.GetLinePlane(0);
            Transform linePlane1 = swordBeam.GetLinePlane(1);
            Require(swordBeam.IsLineBlurVisible && linePlane0 != null && linePlane1 != null &&
                    linePlane0.GetComponent<Renderer>().enabled && linePlane1.GetComponent<Renderer>().enabled,
                "Beam_Line appears as a motion blur only after the saber root or tip moves",
                ref assertions);
            updateSwordMotionBlur.Invoke(swordBeam, null);
            Require(!swordBeam.IsLineBlurVisible &&
                    !linePlane0.GetComponent<Renderer>().enabled && !linePlane1.GetComponent<Renderer>().enabled,
                "Beam_Line hides again on the next stationary presentation frame",
                ref assertions);

            int proc55Events = 0;
            int primary55Events = 0;
            int line55Events = 0;
            for (int i = 0; i < windPresentationEvents.Count; i++)
            {
                TestPlayPresentationEvent presentationEvent = windPresentationEvents[i];
                if (presentationEvent.type == TestPlayPresentationEventType.Proc &&
                    presentationEvent.procType == 55 &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed)
                    proc55Events++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc2:55:Primary" &&
                    presentationEvent.textureId == 12)
                    primary55Events++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc2:55:Line" &&
                    presentationEvent.textureId == 13)
                    line55Events++;
            }
            Require(proc55Events == 1 && primary55Events == 1 && line55Events == 1,
                "RunProc2 type 55 trace records confirmed Proc semantics and both texture-layer adapters",
                ref assertions);

            meleeSptData.WeaponPoints[1].Direction = SptDirection.DOWN;
            int managedSwordStart = windTransients.Count;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(1f, 55f, 1f, 200f, 12f, 13f, 0f, 0f, 0f, 0f, 1f, 35f), true
            });
            GameObject firstManagedSword = windTransients.Count == managedSwordStart + 1
                ? windTransients[managedSwordStart]
                : null;
            Require(firstManagedSword != null &&
                    Vector3.Dot(firstManagedSword.transform.forward, weaponPointObject.transform.forward) > 0.999f,
                "RunProc2 type 55 keeps the real WEAPONPOINT bone local Z+ for DOWN presentation",
                ref assertions);
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(1f, 55f, 1f, 200f, 12f, 13f, 0f, 0f, 0f, 0f, 1f, 35f), true
            });
            Require(firstManagedSword == null && windTransients.Count == managedSwordStart + 1,
                "RunProc2 type 55 p10 replaces only the previously managed sword beam",
                ref assertions);
            meleeSptData.WeaponPoints[1].Direction = SptDirection.UP;

            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(2f, 3f, 4f),
                Quaternion.Euler(0f, 45f, 0f));
            int thunderStart = windTransients.Count;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(0f, 60f, 1f, 1f, 4f, 0f, 7f, 10f, 10f, 0f, 0f, 0f), true
            });
            GameObject thunderObject = windTransients.Count == thunderStart + 1
                ? windTransients[thunderStart]
                : null;
            TestPlayThunderEffect thunderEffect = thunderObject != null
                ? thunderObject.GetComponent<TestPlayThunderEffect>()
                : null;
            Require(thunderEffect != null && thunderEffect.HasTextureLayer &&
                    thunderEffect.WeaponPointId == 1 && thunderEffect.TextureId == 7 &&
                    thunderEffect.OriginalLength == 4 &&
                    Mathf.Approximately(thunderEffect.InitialWidth, 0.01f) &&
                    Mathf.Approximately(thunderEffect.ScatterRadius, 0.1f) &&
                    thunderEffect.SpawnPosition == new Vector3(2f, 3f, 4f) &&
                    thunderObject.transform.parent == null &&
                    thunderObject.GetComponent<TestPlayProjectile>() == null &&
                    Mathf.Approximately(target.hp, 1000f),
                "RunProc2 type 60 creates a snapshot-positioned non-combat LZ_ThunderEffect with the real texture",
                ref assertions);
            weaponPointObject.transform.position = new Vector3(20f, 30f, 40f);
            tickThunderEffects.Invoke(controller, null);
            Require(thunderEffect != null && thunderEffect.AppliedTicks == 1 &&
                    Vector3.Distance(thunderEffect.transform.position, weaponPointObject.transform.position) > 1f &&
                    ((System.Collections.ICollection)GetField(controller, "activeThunderEffects")).Count == 1,
                "RunProc2 type 60 copies the WEAPONPOINT matrix at spawn and advances independently",
                ref assertions);

            int proc60Events = 0;
            int visual60Events = 0;
            for (int i = 0; i < windPresentationEvents.Count; i++)
            {
                TestPlayPresentationEvent presentationEvent = windPresentationEvents[i];
                if (presentationEvent.type == TestPlayPresentationEventType.Proc &&
                    presentationEvent.procType == 60 &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed)
                    proc60Events++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc2:60" &&
                    presentationEvent.textureId == 7 &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad)
                    visual60Events++;
            }
            Require(proc60Events == 1 && visual60Events == 1,
                "RunProc2 type 60 trace records confirmed Proc semantics and its texture Adapter",
                ref assertions);

            for (int i = 1; i < 19; i++)
                tickThunderEffects.Invoke(controller, null);
            Require(thunderObject == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeThunderEffects")).Count == 0 &&
                    windTransients.Count == thunderStart,
                "RunProc2 type 60 p8=10 expires after the active phase and ten-step scalar fade",
                ref assertions);

            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(2f, 3f, 4f),
                Quaternion.Euler(15f, 30f, 45f));
            int windRingSpecialStart = windTransients.Count;
            List<TestPlayScriptValue> windRingSpecialArguments =
                Values(0f, 62f, 1f, 2f, 200f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            Require(TestPlayPresentationCore.TryCreateOriginalWindRingSpecialParameters(
                        true,
                        windRingSpecialArguments,
                        out TestPlayWindRingSpecialParameters windRingSpecialParameters) &&
                    windRingSpecialParameters.weaponPointId == 1 &&
                    windRingSpecialParameters.unusedP4 == 200,
                "subtype 2 parameter extraction keeps p2 as WEAPONPOINT and records the unread p4 value",
                ref assertions);
            TestPlayWindRingSpecialState windRingSpecialCoreState =
                TestPlayPresentationCore.CreateOriginalWindRingSpecialState();
            bool windRingSpecialCoreAlive = true;
            for (int i = 1; i <= 11; i++)
                windRingSpecialCoreAlive =
                    TestPlayPresentationCore.AdvanceOriginalWindRingSpecial(ref windRingSpecialCoreState);
            Require(!windRingSpecialCoreAlive && windRingSpecialCoreState.elapsedTicks == 11 &&
                    Mathf.Approximately(windRingSpecialCoreState.size, 4.3f) &&
                    windRingSpecialCoreState.alphaByte == 15,
                "subtype 2 Core grows on update eleven before rejecting candidate alpha -9",
                ref assertions);
            spawnRunProc.Invoke(controller, new object[]
            {
                windRingSpecialArguments, true
            });
            GameObject windRingSpecialObject = windTransients.Count == windRingSpecialStart + 1
                ? windTransients[windRingSpecialStart]
                : null;
            TestPlayOriginalEffect windRingSpecialEffect = windRingSpecialObject != null
                ? windRingSpecialObject.GetComponent<TestPlayOriginalEffect>()
                : null;
            Require(windRingSpecialEffect != null &&
                    windRingSpecialEffect.sourceTexture == windRingTexture &&
                    !windRingSpecialEffect.billboard &&
                    windRingSpecialEffect.displaySize == Vector2.one &&
                    windRingSpecialObject.transform.position == new Vector3(2f, 3f, 4f) &&
                    windRingSpecialObject.transform.rotation == Quaternion.identity &&
                    windRingSpecialObject.GetComponent<TestPlayProjectile>() == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeWindRingSpecialEffects")).Count == 1 &&
                    Mathf.Approximately(target.hp, 1000f),
                "RunProc2 type 62 subtype 2 creates a translation-snapshot non-combat BB_WindRing quad",
                ref assertions);

            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(8f, 9f, 10f),
                Quaternion.Euler(60f, 90f, 120f));
            tickWindRingSpecialEffects.Invoke(controller, null);
            Require(windRingSpecialObject != null &&
                    windRingSpecialObject.transform.position == new Vector3(2f, 3f, 4f) &&
                    windRingSpecialObject.transform.rotation == Quaternion.identity &&
                    Mathf.Approximately(windRingSpecialEffect.displaySize.x, 1.3f) &&
                    Mathf.Approximately(windRingSpecialEffect.displaySize.y, 1.3f) &&
                    Mathf.Approximately(windRingSpecialEffect.CurrentTint.a, 231f / 255f),
                "subtype 2 copies only WEAPONPOINT translation and grows before applying -24 alpha on update one",
                ref assertions);

            spawnRunProc.Invoke(controller, new object[]
            {
                Values(0f, 62f, 1f, 2f, 999f, 88f, 77f, 66f, 55f, 44f, 33f, 22f), true
            });
            GameObject secondWindRingSpecialObject = windTransients.Count == windRingSpecialStart + 2
                ? windTransients[windRingSpecialStart + 1]
                : null;
            TestPlayOriginalEffect secondWindRingSpecialEffect = secondWindRingSpecialObject != null
                ? secondWindRingSpecialObject.GetComponent<TestPlayOriginalEffect>()
                : null;
            Require(secondWindRingSpecialEffect != null &&
                    secondWindRingSpecialEffect.displaySize == Vector2.one &&
                    secondWindRingSpecialObject.transform.position == new Vector3(8f, 9f, 10f) &&
                    ((System.Collections.ICollection)GetField(controller, "activeWindRingSpecialEffects")).Count == 2,
                "subtype 2 instances are independent and p4-p11 do not replace its fixed BB_WindRing parameters",
                ref assertions);

            int procWindRingSpecialEvents = 0;
            int visualWindRingSpecialEvents = 0;
            for (int i = 0; i < windPresentationEvents.Count; i++)
            {
                TestPlayPresentationEvent presentationEvent = windPresentationEvents[i];
                if (presentationEvent.type == TestPlayPresentationEventType.Proc &&
                    presentationEvent.procType == 62 && presentationEvent.subtype == 2 &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad)
                    procWindRingSpecialEvents++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc2:62:2" &&
                    presentationEvent.textureId == TestPlayPresentationCore.OriginalWindRingTextureId &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad &&
                    presentationEvent.diagnostic ==
                        "OriginalBBWindRingParametersAndLifecycleWithUnityQuadAdapter")
                    visualWindRingSpecialEvents++;
            }
            Require(procWindRingSpecialEvents == 2 && visualWindRingSpecialEvents == 2,
                "subtype 2 trace records confirmed BB_WindRing semantics and the named-texture Unity quad Adapter",
                ref assertions);

            for (int i = 2; i <= 10; i++)
                tickWindRingSpecialEffects.Invoke(controller, null);
            Require(windRingSpecialObject != null && secondWindRingSpecialObject != null &&
                    Mathf.Approximately(windRingSpecialEffect.displaySize.x, 4f) &&
                    Mathf.Approximately(windRingSpecialEffect.displaySize.y, 4f) &&
                    Mathf.Approximately(windRingSpecialEffect.CurrentTint.a, 15f / 255f) &&
                    Mathf.Approximately(secondWindRingSpecialEffect.displaySize.x, 3.7f) &&
                    Mathf.Approximately(secondWindRingSpecialEffect.displaySize.y, 3.7f) &&
                    Mathf.Approximately(secondWindRingSpecialEffect.CurrentTint.a, 39f / 255f),
                "subtype 2 remains alive through update ten with exact size and alpha state",
                ref assertions);
            tickWindRingSpecialEffects.Invoke(controller, null);
            Require(windRingSpecialObject == null && secondWindRingSpecialObject != null &&
                    Mathf.Approximately(secondWindRingSpecialEffect.displaySize.x, 4f) &&
                    Mathf.Approximately(secondWindRingSpecialEffect.displaySize.y, 4f) &&
                    Mathf.Approximately(secondWindRingSpecialEffect.CurrentTint.a, 15f / 255f) &&
                    ((System.Collections.ICollection)GetField(controller, "activeWindRingSpecialEffects")).Count == 1,
                "the first subtype 2 instance expires on update eleven without affecting the second instance",
                ref assertions);
            tickWindRingSpecialEffects.Invoke(controller, null);
            Require(secondWindRingSpecialObject == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeWindRingSpecialEffects")).Count == 0 &&
                    windTransients.Count == windRingSpecialStart,
                "the second subtype 2 instance independently expires and cleans up on update eleven",
                ref assertions);

            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(2f, 3f, 4f),
                Quaternion.identity);
            int burnerBurstStart = windTransients.Count;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(0f, 62f, 1f, 3f, 350f, 350f, 0f, 0f, 0f, 0f, 0f, 0f), true
            });
            GameObject burnerPrimaryObject = windTransients.Count == burnerBurstStart + 2
                ? windTransients[burnerBurstStart]
                : null;
            GameObject burnerBallObject = windTransients.Count == burnerBurstStart + 2
                ? windTransients[burnerBurstStart + 1]
                : null;
            TestPlayOriginalEffect burnerPrimaryEffect = burnerPrimaryObject != null
                ? burnerPrimaryObject.GetComponent<TestPlayOriginalEffect>()
                : null;
            TestPlayOriginalEffect burnerBallEffect = burnerBallObject != null
                ? burnerBallObject.GetComponent<TestPlayOriginalEffect>()
                : null;
            Require(burnerPrimaryEffect != null && burnerBallEffect != null &&
                    burnerPrimaryEffect.sourceTexture == burnerTexture &&
                    burnerBallEffect.sourceTexture == burner2Texture &&
                    burnerPrimaryEffect.displaySize == new Vector2(3.5f, 3.5f) &&
                    burnerBallEffect.displaySize == Vector2.one * 1.75f &&
                    Mathf.Approximately(burnerPrimaryEffect.CurrentTint.a, 128f / 255f) &&
                    burnerPrimaryObject.GetComponent<TestPlayProjectile>() == null &&
                    burnerBallObject.GetComponent<TestPlayProjectile>() == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeBurnerBurstEffects")).Count == 1 &&
                    Mathf.Approximately(target.hp, 1000f),
                "RunProc2 type 62 subtype 3 creates the non-combat BB_Burner and BB_BurnerBall texture pair",
                ref assertions);

            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(8f, 9f, 10f),
                Quaternion.Euler(0f, 90f, 0f));
            tickBurnerBurstEffects.Invoke(controller, null);
            Require(burnerPrimaryObject != null && burnerBallObject != null &&
                    burnerPrimaryObject.transform.position == weaponPointObject.transform.position &&
                    Vector3.Distance(
                        burnerBallObject.transform.position,
                        weaponPointObject.transform.TransformPoint(Vector3.forward * 0.4375f)) < 0.0001f &&
                    Mathf.Approximately(burnerPrimaryEffect.displaySize.x, 3.5f + 7f / 15f) &&
                    Mathf.Approximately(burnerBallEffect.displaySize.x, 1.75f * 0.95f),
                "subtype 3 follows the WEAPONPOINT matrix and advances both confirmed update formulas",
                ref assertions);

            int procBurnerBurstEvents = 0;
            int visualBurnerBurstEvents = 0;
            for (int i = 0; i < windPresentationEvents.Count; i++)
            {
                TestPlayPresentationEvent presentationEvent = windPresentationEvents[i];
                if (presentationEvent.type == TestPlayPresentationEventType.Proc &&
                    presentationEvent.procType == 62 && presentationEvent.subtype == 3 &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad)
                    procBurnerBurstEvents++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    (presentationEvent.source == "RunProc2:62:3:Primary" ||
                     presentationEvent.source == "RunProc2:62:3:Ball") &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad)
                    visualBurnerBurstEvents++;
            }
            Require(procBurnerBurstEvents == 1 && visualBurnerBurstEvents == 2,
                "subtype 3 trace records one confirmed Proc and two independently adapted original effects",
                ref assertions);

            for (int i = 1; i < 5; i++)
                tickBurnerBurstEffects.Invoke(controller, null);
            Require(burnerPrimaryObject != null && burnerBallObject == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeBurnerBurstEffects")).Count == 1 &&
                    windTransients.Count == burnerBurstStart + 1,
                "BB_BurnerBall is removed on update five while BB_Burner remains active",
                ref assertions);
            for (int i = 5; i < 11; i++)
                tickBurnerBurstEffects.Invoke(controller, null);
            Require(burnerPrimaryObject == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeBurnerBurstEffects")).Count == 0 &&
                    windTransients.Count == burnerBurstStart,
                "BB_Burner is independently removed on update eleven and both subtype 3 layers are cleaned up",
                ref assertions);

            SetField(controller, "velocity", Vector3.zero);
            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(2f, 3f, 4f),
                Quaternion.identity);
            int hinokoStart = windTransients.Count;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(0f, 62f, 1f, 6f, 5f, 300f, 15f, 15f, 15f, 0f, 0f, 0f), true
            });
            GameObject hinokoObject = windTransients.Count == hinokoStart + 1
                ? windTransients[hinokoStart]
                : null;
            TestPlayOriginalEffect hinokoEffect = hinokoObject != null
                ? hinokoObject.GetComponent<TestPlayOriginalEffect>()
                : null;
            Vector3 hinokoSpawnPosition = hinokoObject != null
                ? hinokoObject.transform.position
                : Vector3.zero;
            Require(hinokoEffect != null && hinokoEffect.sourceTexture == hinokoTexture &&
                    !hinokoEffect.billboard &&
                    hinokoEffect.displaySize == new Vector2(0.05f, 0.025f) &&
                    Vector3.Distance(hinokoSpawnPosition, weaponPointObject.transform.position) <=
                        Mathf.Sqrt(3f) * 0.15f + 0.0001f &&
                    hinokoObject.GetComponent<TestPlayProjectile>() == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeHinokoEffects")).Count == 1 &&
                    Mathf.Approximately(target.hp, 1000f),
                "RunProc2 type 62 subtype 6 creates a scattered non-combat BB_Hinoko quad from real p4/p6-p8",
                ref assertions);

            tickHinokoEffects.Invoke(controller, null);
            Require(hinokoObject != null &&
                    Mathf.Abs(Vector3.Distance(hinokoObject.transform.position, hinokoSpawnPosition) - 0.03f) < 0.0001f &&
                    Mathf.Approximately(hinokoEffect.CurrentTint.a, 1f),
                "subtype 6 advances by the p5-derived copied-matrix step and keeps alpha 255 on its first update",
                ref assertions);

            int procHinokoEvents = 0;
            int visualHinokoEvents = 0;
            for (int i = 0; i < windPresentationEvents.Count; i++)
            {
                TestPlayPresentationEvent presentationEvent = windPresentationEvents[i];
                if (presentationEvent.type == TestPlayPresentationEventType.Proc &&
                    presentationEvent.procType == 62 && presentationEvent.subtype == 6 &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad)
                    procHinokoEvents++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc2:62:6" &&
                    presentationEvent.textureId == TestPlayPresentationCore.OriginalHinokoTextureId &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad &&
                    presentationEvent.diagnostic ==
                        "OriginalParametersAndLifecycleWithDeterministicUnitySharedRngAdapter")
                    visualHinokoEvents++;
            }
            Require(procHinokoEvents == 1 && visualHinokoEvents == 1,
                "subtype 6 trace separates confirmed BB_Hinoko semantics from its deterministic Unity RNG Adapter",
                ref assertions);

            for (int i = 1; i < 30; i++)
                tickHinokoEffects.Invoke(controller, null);
            Require(hinokoObject != null && Mathf.Approximately(hinokoEffect.CurrentTint.a, 1f),
                "subtype 6 retains alpha 255 through update 30", ref assertions);
            tickHinokoEffects.Invoke(controller, null);
            Require(hinokoObject != null &&
                    Mathf.Approximately(hinokoEffect.CurrentTint.a, 251f / 255f),
                "subtype 6 starts the original four-alpha fade on update 31", ref assertions);
            for (int tickIndex = 32; tickIndex <= 93; tickIndex++)
                tickHinokoEffects.Invoke(controller, null);
            Require(hinokoObject != null &&
                    Mathf.Approximately(hinokoEffect.CurrentTint.a, 3f / 255f) &&
                    ((System.Collections.ICollection)GetField(controller, "activeHinokoEffects")).Count == 1,
                "subtype 6 remains alive at alpha three through update 93", ref assertions);
            tickHinokoEffects.Invoke(controller, null);
            Require(hinokoObject == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeHinokoEffects")).Count == 0 &&
                    windTransients.Count == hinokoStart,
                "subtype 6 expires and cleans up on original update 94", ref assertions);

            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(3f, 4f, 5f),
                Quaternion.Euler(0f, 60f, 0f));
            int magicShieldStart = windTransients.Count;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(0f, 62f, 1f, 8f, 1f, 0f, 10f, 5f, 2f, 0f, 2f, 0f), true
            });
            GameObject magicShieldObject = windTransients.Count == magicShieldStart + 1
                ? windTransients[magicShieldStart]
                : null;
            Require(magicShieldObject != null &&
                    magicShieldObject.name.Contains("ModelSlot1_WEAPONPOINT1") &&
                    magicShieldObject.GetComponent<TestPlayProjectile>() == null &&
                    magicShieldObject.transform.position == weaponPointObject.transform.position &&
                    magicShieldObject.transform.rotation == weaponPointObject.transform.rotation &&
                    magicShieldObject.transform.localScale == Vector3.one * 0.1f &&
                    ((System.Collections.ICollection)GetField(controller, "activeMagicShieldEffects")).Count == 1 &&
                    Mathf.Approximately(target.hp, 1000f),
                "RunProc2 type 62 subtype 8 creates a non-combat mapped LZ_MagicShieldEffect state at WEAPONPOINT p2",
                ref assertions);

            weaponPointObject.transform.SetPositionAndRotation(
                new Vector3(6f, 7f, 8f),
                Quaternion.Euler(0f, 90f, 0f));
            tickMagicShieldEffects.Invoke(controller, null);
            Require(magicShieldObject != null &&
                    magicShieldObject.transform.position == weaponPointObject.transform.position &&
                    magicShieldObject.transform.rotation == weaponPointObject.transform.rotation &&
                    magicShieldObject.transform.localScale == Vector3.one * 0.2f,
                "subtype 8 follows its WEAPONPOINT matrix when p6 is nonzero and grows by 0.1 per tick",
                ref assertions);

            int proc62Events = 0;
            int visual62Events = 0;
            for (int i = 0; i < windPresentationEvents.Count; i++)
            {
                TestPlayPresentationEvent presentationEvent = windPresentationEvents[i];
                if (presentationEvent.type == TestPlayPresentationEventType.Proc &&
                    presentationEvent.procType == 62 && presentationEvent.subtype == 8 &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.MappedPrefab)
                    proc62Events++;
                if (presentationEvent.type == TestPlayPresentationEventType.Visual &&
                    presentationEvent.source == "RunProc2:62:8" &&
                    presentationEvent.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                    presentationEvent.adapter == TestPlayPresentationAdapterKind.MappedPrefab &&
                    presentationEvent.diagnostic == "OriginalLifecycleWithMappedModelSlotPrefab")
                    visual62Events++;
            }
            Require(proc62Events == 1 && visual62Events == 1,
                "subtype 8 trace separates the confirmed lifecycle from its configured Unity model-slot Prefab",
                ref assertions);

            for (int i = 1; i < 18; i++)
                tickMagicShieldEffects.Invoke(controller, null);
            Require(magicShieldObject == null &&
                    ((System.Collections.ICollection)GetField(controller, "activeMagicShieldEffects")).Count == 0 &&
                    windTransients.Count == magicShieldStart,
                "real-data subtype 8 p5=0 follows the confirmed grow, one active update, fade, and removal boundary",
                ref assertions);

            weaponPointObject.transform.position = new Vector3(1f, 2f, 3f);
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(0f, 62f, 1f, 8f, 1f, 50f, 0f, 0f, 0f, 0f, 0f, 0f), true
            });
            GameObject snapshotMagicShield = windTransients.Count == magicShieldStart + 1
                ? windTransients[magicShieldStart]
                : null;
            weaponPointObject.transform.position = new Vector3(9f, 8f, 7f);
            tickMagicShieldEffects.Invoke(controller, null);
            Require(snapshotMagicShield != null &&
                    snapshotMagicShield.transform.position == new Vector3(1f, 2f, 3f),
                "subtype 8 keeps its spawn matrix snapshot when p6 is zero",
                ref assertions);
            for (int i = 1; i < 18; i++)
                tickMagicShieldEffects.Invoke(controller, null);
            Require(snapshotMagicShield == null && windTransients.Count == magicShieldStart,
                "p6 zero still completes subtype 8 cleanup without a persistent invisible state",
                ref assertions);

            int missingThunderTransientCount = windTransients.Count;
            WindomVerificationRunner.ExpectWarning(
                "[TestPlay] Unhandled RunProc2:60 WEAPONPOINT 49 is not bound; original LZ_ThunderEffect is not created.",
                () => spawnRunProc.Invoke(controller, new object[]
                {
                    Values(0f, 60f, 49f, 1f, 4f, 0f, 7f, 10f, 10f, 0f, 0f, 0f), true
                }));
            Require(windTransients.Count == missingThunderTransientCount &&
                    ((System.Collections.ICollection)GetField(controller, "activeThunderEffects")).Count == 0,
                "RunProc2 type 60 creates nothing when its WEAPONPOINT is unavailable",
                ref assertions);

            weaponPointObject.transform.localPosition = Vector3.zero;
            weaponPointObject.transform.localRotation = Quaternion.identity;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(1f, 57f, 1f, 200f, 0f, 5f, 5f, 0f, 0f, 0f, 0f, 9f), true
            });
            handleAssignment.Invoke(controller, new object[] { "AttackFlag", "=", Values(0f), "AttackFlag=0;" });
            Require(Mathf.Approximately(target.hp, 1000f),
                "RunProc2 type 57 creates a persistent WEAPONPOINT judgment without an immediate root-distance hit",
                ref assertions);
            handle.Invoke(controller, new object[] { "ATTACK", Values(99f, 88f, 7f, 6f), "ATTACK(99,88,7,6);" });
            tickMeleeAttacks.Invoke(controller, null);
            Require(Mathf.Approximately(target.hp, 963f) && target.lastDownValue == 44 &&
                    target.lastImpactForce == new Vector3(0f, 3f, 2f),
                "RunProc2 type 57 sweeps from WEAPONPOINT and uses its spawn-time ATTACK snapshot",
                ref assertions);
            Require(target.lastAttackFlag == 9 &&
                    target.lastHitDecision == TestPlayCombatHitDecision.Damaged &&
                    target.stateId == 3,
                "RunProc2 type 57 snapshots AttackFlag and resolves its hit reaction " +
                "(flag=" + target.lastAttackFlag + ", decision=" + target.lastHitDecision +
                ", state=" + target.stateId + ")", ref assertions);
            Require(target.hitStopTicks == 5 && (int)GetField(controller, "hitStopTicks") == 5,
                "RunProc2 type 57 applies p2 hit-stop to attacker and defender", ref assertions);
            tickMeleeAttacks.Invoke(controller, null);
            Require(Mathf.Approximately(target.hp, 963f),
                "type 57 p3 keeps the target in its hit history before the re-hit interval", ref assertions);
            for (int i = 0; i < 3; i++)
                tickMeleeAttacks.Invoke(controller, null);
            Require(Mathf.Approximately(target.hp, 963f),
                "type 57 p3 blocks the same target for the complete cooldown window", ref assertions);
            tickMeleeAttacks.Invoke(controller, null);
            Require(Mathf.Approximately(target.hp, 926f),
                "type 57 can hit the same target again when its p3 hit record expires", ref assertions);
            for (int i = 0; i < 3; i++)
                tickMeleeAttacks.Invoke(controller, null);
            Require(((List<TestPlayMeleeAttackState>)GetField(controller, "activeMeleeAttacks")).Count == 0,
                "RunProc2 type 57 expires after its p8 tick lifetime", ref assertions);

            handle.Invoke(controller, new object[]
            {
                "ATTACK", Values(37f, 44f, 2f, 3f), "ATTACK(37,44,2,3);"
            });
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(1f, 57f, 1f, 200f, 0f, 2f, 0f, 0f, 0f, 0f, 0f, 3f), true
            });
            tickMeleeAttacks.Invoke(controller, null);
            tickMeleeAttacks.Invoke(controller, null);
            Require(Mathf.Approximately(target.hp, 852f),
                "type 57 p3=0 removes its hit record before the next tick and permits consecutive hits",
                ref assertions);

            Require(TestPlayCombatCore.IntersectsSweptSegmentSphere(
                        Vector3.zero, Vector3.forward * 2f,
                        Vector3.zero, Vector3.right * 2f,
                        new Vector3(0.8f, 0f, 0.8f), 0.1f) &&
                    !TestPlayCombatCore.IntersectsSweptSegmentSphere(
                        Vector3.zero, Vector3.forward * 2f,
                        Vector3.zero, Vector3.right * 2f,
                        new Vector3(-2f, 0f, -2f), 0.1f),
                "type 57 Core tests the swept segment surface rather than root distance", ref assertions);

            handle.Invoke(controller, new object[] { "ATTACK", Values(37f, 44f, 2f, 3f), "ATTACK(37,44,2,3);" });
            handleAssignment.Invoke(controller, new object[] { "AttackFlag", "=", Values(2f), "AttackFlag=2;" });

            controller.maximumAuxiliaryEnergy = 500f;
            controller.currentAuxiliaryEnergy = 500f;
            controller.state.SetInt(103, 500);
            controller.state.SetFloat(103, 500f);
            float movementEnergyBeforeType1 = controller.currentEnergy;
            spawnRunProc.Invoke(controller, new object[]
            {
                Values(0f, 1f, 0f, 200f, 30f, 100f, 10f, 0f, 7f, 0f, 0f, 0f), true
            });
            List<GameObject> transients = (List<GameObject>)GetField(controller, "spawnedTransientObjects");
            spawnedType1Projectile = transients[transients.Count - 1];
            TestPlayProjectile type1Projectile = spawnedType1Projectile.GetComponent<TestPlayProjectile>();
            TestPlayType1TrailEffect type1TrailEffect =
                spawnedType1Projectile.GetComponent<TestPlayType1TrailEffect>();
            Require(type1Projectile != null && type1Projectile.useOriginalType1Core &&
                    type1Projectile.weaponPointId == 0 &&
                    type1Projectile.remainingActiveTicks == TestPlayCombatCore.OriginalType1ActiveTicks &&
                    type1Projectile.trailPointCount == 30 &&
                    Mathf.Approximately(type1Projectile.distancePerTick, 1f) &&
                    Mathf.Approximately(type1Projectile.visualWidth, 0.1f) &&
                    type1Projectile.textureId == 7 &&
                    spawnedType1Projectile.transform.position == weaponPointObject.transform.position &&
                    Mathf.Approximately(controller.currentAuxiliaryEnergy, 300f) &&
                    Mathf.Approximately(controller.currentEnergy, movementEnergyBeforeType1),
                "RunProc2 type 1 maps p0..p6, snapshots WEAPONPOINT, charges p0 from the Script.spt Energy gauge without consuming Generator, and starts a fixed 300-tick LZ_Beam",
                ref assertions);
            Require(type1TrailEffect != null &&
                    type1TrailEffect.TrailRenderer != null &&
                    type1TrailEffect.TrailRenderer.useWorldSpace &&
                    type1TrailEffect.TrailRenderer.alignment == LineAlignment.View &&
                    type1TrailEffect.PositionCount == 1 &&
                    Mathf.Approximately(type1TrailEffect.displayWidth, 0.1f) &&
                    spawnedType1Projectile.GetComponent<TestPlayOriginalEffect>() == null,
                "type 1 presentation starts as a one-point world-space ribbon rather than a pre-extended billboard quad",
                ref assertions);
            float type1HpBefore = target.hp;
            Require(type1Projectile.OriginalType1TrailCount == 1 &&
                    !type1Projectile.SimulateOriginalTick(1f / 60f) &&
                    type1Projectile.remainingActiveTicks == 299 &&
                    type1Projectile.OriginalType1TrailCount == 2 &&
                    type1TrailEffect.PositionCount == 2 &&
                    type1TrailEffect.TrailRenderer.GetPosition(0) == Vector3.zero &&
                    type1TrailEffect.TrailRenderer.GetPosition(1) == Vector3.forward &&
                    spawnedType1Projectile.transform.position == Vector3.forward &&
                    Mathf.Approximately(target.hp, type1HpBefore - 37f) &&
                    target.lastDownValue == 44 && target.lastAttackFlag == 2,
                "type 1 moves p2/100 on the first 60 Hz tick and applies its spawn-time ATTACK/AttackFlag snapshot through the trail segment",
                ref assertions);
            float hpAfterType1Hit = target.hp;
            Require(!type1Projectile.SimulateOriginalTick(1f / 60f) &&
                    Mathf.Approximately(target.hp, hpAfterType1Hit),
                "one type 1 LZ_Beam hits the same target only once and continues after the hit",
                ref assertions);

            TestPlayType1ProjectileTickResult type1Lifetime = default;
            TestPlayType1ProjectileTickInput type1LifetimeInput = new TestPlayType1ProjectileTickInput
            {
                position = Vector3.zero,
                rotation = Quaternion.identity,
                remainingActiveTicks = TestPlayCombatCore.OriginalType1ActiveTicks,
                distancePerTick = 1f
            };
            for (int i = 0; i < TestPlayCombatCore.OriginalType1ActiveTicks; i++)
            {
                type1Lifetime = TestPlayCombatCore.TickOriginalType1Projectile(type1LifetimeInput);
                type1LifetimeInput.position = type1Lifetime.position;
                type1LifetimeInput.rotation = type1Lifetime.rotation;
                type1LifetimeInput.remainingActiveTicks = type1Lifetime.remainingActiveTicks;
            }
            Require(type1Lifetime.expired && type1Lifetime.remainingActiveTicks == 0 &&
                    type1Lifetime.position == Vector3.forward * 300f,
                "type 1 Core remains active for exactly 300 movement ticks",
                ref assertions);

            handle.Invoke(controller, new object[] { "ATTACK", Values(37f, 44f, 2f, 3f), "ATTACK(37,44,2,3);" });
            handleAssignment.Invoke(controller, new object[] { "AttackFlag", "=", Values(2f), "AttackFlag=2;" });
            spawnProjectile.Invoke(controller, new object[] { "VerificationShot", 37f, 20f, false });
            handleAssignment.Invoke(controller, new object[] { "AttackFlag", "=", Values(0f), "AttackFlag=0;" });
            spawnedProjectile = transients[transients.Count - 1];
            TestPlayProjectile projectile = spawnedProjectile.GetComponent<TestPlayProjectile>();
            Require(projectile != null && projectile.downValue == 44 &&
                    Mathf.Approximately(projectile.horizontalImpactForce, 2f) &&
                    Mathf.Approximately(projectile.verticalImpactForce, 3f) &&
                    projectile.attackFlag == 2,
                "projectiles snapshot the active ATTACK and AttackFlag profile", ref assertions);

            handleAssignment.Invoke(controller, new object[] { "ShildGuard", "=", Values(2f), "ShildGuard=2;" });
            Require((int)GetField(controller, "shieldGuard") == 2,
                "ShildGuard retains real ANI value 2 instead of collapsing it to bool", ref assertions);
            handleAssignment.Invoke(controller, new object[] { "LaserReflect", "=", Values(20f), "LaserReflect=20;" });
            Require(controller.CurrentLaserReflectValue == 20,
                "LaserReflect assignment stores the confirmed defender-side +0xB68 value", ref assertions);
            handleAssignment.Invoke(controller, new object[] { "LaserReflect", "=", Values(276f), "LaserReflect=276;" });
            Require(controller.CurrentLaserReflectValue == 20,
                "LaserReflect assignment preserves the original low-byte storage", ref assertions);
            resetBlock.Invoke(controller, null);
            Require(controller.CurrentLaserReflectValue == 0,
                "LaserReflect resets at the original ANI block boundary", ref assertions);
            controller.state.SetInt(157, 2);
            controller.state.SetInt(158, 2);
            target.guardHitTimerTicks = 2;
            target.hitAcceptanceBlockTicks = 2;
            bool animationWasHitStopped = (bool)updateCombatTimers.Invoke(controller, null);
            Require(animationWasHitStopped && controller.state.GetInt(157) == 1 &&
                    controller.state.GetInt(158) == 1 && target.guardHitTimerTicks == 1 &&
                    target.hitAcceptanceBlockTicks == 1 &&
                    (int)GetField(controller, "hitStopTicks") == 4,
                "controller and target defense timers decrement once per original 60 Hz tick",
                ref assertions);

            controller.ChangeAnimation(controller.neutralMeleeAction);
            SetField(controller, "attackSequenceActive", true);
            SetField(controller, "meleeKeyPressedThisTick", true);
            updateAttack.Invoke(controller, null);
            Require(controller.state.GetInt(151) == 0 &&
                    controller.currentAnimationIndex == controller.neutralMeleeAction &&
                    (bool)GetField(controller, "meleeComboInputPending"),
                "melee action 131 uses ANI channel 0 and keeps an early C edge pending", ref assertions);
            SetField(controller, "meleeKeyPressedThisTick", false);
            tickAnimation.Invoke(controller, null);
            tickAnimation.Invoke(controller, null);
            updateAttack.Invoke(controller, null);
            Require(controller.currentAnimationIndex == 132 && controller.state.GetInt(151) == 0,
                "pending C follows the real channel-0 SwordCancel=132 script window", ref assertions);

            controller.ChangeAnimation(133);
            SetField(controller, "attackSequenceActive", true);
            SetField(controller, "meleeApproachActive", false);
            controller.SetAirborneFlag(false);
            tickAnimation.Invoke(controller, null);
            tickAnimation.Invoke(controller, null);
            Require(controller.currentAnimationIndex == 133,
                "a scriptless combo finisher remains active through its HOD frames", ref assertions);
            tickAnimation.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.stepLandingAction + 50,
                "a scriptless combo finisher returns through action 6 and its sword variant", ref assertions);
            tickAnimation.Invoke(controller, null);
            tickAnimation.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.idleAction + 50,
                "no-input combo completion reaches the sword standing pose after recovery", ref assertions);

            robo.ani.animations[swordRecoveryAction].scripts = new List<script>
            {
                new script
                {
                    unk = 1,
                    time = 0f,
                    squirrel = "ChangeAnime(" + swordRecoveryAction + ");"
                }
            };
            startGroundRecovery.Invoke(controller, new object[] { controller.stepLandingAction });
            tickAnimation.Invoke(controller, null);
            Require(controller.currentAnimationIndex == swordRecoveryAction,
                "a self-redirecting recovery script reproduces the stuck landing state", ref assertions);
            tickAnimation.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.idleAction + 50 &&
                    !(bool)GetField(controller, "landingSequenceActive"),
                "the independent recovery clock guarantees the sword standing state", ref assertions);

            int[] comboStageActions = { 131, 132, 133 };
            int[][] comboStageDurations =
            {
                new[] { 5, 10, 3, 4, 3, 10, 15, 20, 0 },
                new[] { 1, 10, 3, 4, 3, 10, 15, 15, 0 },
                new[] { 1, 14, 0, 7, 10, 10, 30, 0 }
            };
            robo.ani.animations[swordRecoveryAction].scripts = new List<script>();
            for (int stage = 0; stage < comboStageActions.Length; stage++)
            {
                List<script> stageScripts = new List<script>();
                int attackDurationTicks = 0;
                for (int block = 0; block < comboStageDurations[stage].Length; block++)
                {
                    int blockTicks = comboStageDurations[stage][block];
                    stageScripts.Add(new script { unk = blockTicks, time = 0f, squirrel = "" });
                    attackDurationTicks += Mathf.Max(1, blockTicks);
                }

                robo.ani.animations[comboStageActions[stage]].scripts = stageScripts;
                controller.ChangeAnimation(comboStageActions[stage]);
                SetField(controller, "attackSequenceActive", true);
                SetField(controller, "meleeApproachActive", false);
                controller.SetAirborneFlag(false);
                for (int attackTick = 0; attackTick < attackDurationTicks; attackTick++)
                    tickAnimation.Invoke(controller, null);

                Require(controller.currentAnimationIndex == swordRecoveryAction,
                    "combo stage " + (stage + 1) + " enters sword recovery", ref assertions);
                SetField(controller, "bodyUpAimRequested", true);
                int recoveryTicks = robo.ani.animations[swordRecoveryAction].frames.Count;
                for (int recoveryTick = 0; recoveryTick < recoveryTicks; recoveryTick++)
                    tickAnimation.Invoke(controller, null);

                Require(controller.currentAnimationIndex == controller.idleAction + 50 &&
                        controller.frameIndex == 0 &&
                        !(bool)GetField(controller, "landingSequenceActive") &&
                        !(bool)GetField(controller, "bodyUpAimRequested"),
                    "combo stage " + (stage + 1) + " clears recovery state and applies sword standing", ref assertions);
                applyRootMotion.Invoke(controller, null);
                Require(controller.groundedFlag && !controller.airborneFlag && controller.state.GetInt(150) == 0,
                    "combo stage " + (stage + 1) + " stays grounded on the recovery-to-standing physics tick", ref assertions);
            }

            controller.currentAnimationIndex = controller.meleeAction;
            SetField(controller, "attackSequenceActive", true);
            SetField(controller, "meleeApproachActive", true);
            controller.currentEnergy = 100f;
            controller.state.SetInt(100, 100);
            controller.state.SetFloat(100, 100f);
            SetField(controller, "lastSyncedMovementEnergyInt", 100);
            SetField(controller, "lastSyncedMovementEnergyFloat", 100f);
            energyTick.Invoke(controller, null);
            Require(Mathf.Approximately(controller.currentEnergy, 95f),
                "melee approach action 130 consumes five movement-energy units per tick", ref assertions);
        }
        finally
        {
            if (spawnedType1Projectile != null)
                UnityEngine.Object.DestroyImmediate(spawnedType1Projectile);
            if (spawnedProjectile != null)
                UnityEngine.Object.DestroyImmediate(spawnedProjectile);
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }

    static void VerifyOriginalBasicAniChannelsAndJump(ref int assertions)
    {
        GameObject controllerObject = new GameObject("TestPlayVerification_BasicAniChannelsController");
        GameObject rootObject = new GameObject("TestPlayVerification_BasicAniChannelsRoot");
        try
        {
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            robo.root = rootObject;
            robo.ani = new ani2 { animations = new List<animation>() };
            for (int i = 0; i < 100; i++)
            {
                robo.ani.animations.Add(new animation
                {
                    name = "BasicChannelAction" + i,
                    frames = new List<hod2v1>(),
                    scripts = new List<script>()
                });
            }

            robo.ani.animations[controller.idleAction].scripts = new List<script>
            {
                new script { unk = 1, time = 0f, squirrel = "" }
            };
            robo.ani.animations[controller.riseStartAction].scripts = new List<script>
            {
                new script
                {
                    unk = 1,
                    time = 0f,
                    squirrel =
                        "IF(@int[151],==,0); Force=(0,STOP,0); GvEnable=0; ENDIF;" +
                        "IF(@int[151],==,1); @int[178]=1; ENDIF;"
                }
            };
            robo.ani.animations[controller.riseAction].scripts = new List<script>
            {
                new script
                {
                    unk = 5,
                    time = 0f,
                    squirrel =
                        "IF(@int[151],==,0); Force=(0,0.04,0); GvEnable=0; ENDIF;" +
                        "IF(@int[151],==,1); @int[179]=1; ENDIF;"
                }
            };
            robo.ani.animations[controller.riseAction].frames = new List<hod2v1>
            {
                new hod2v1("GunRise0") { parts = new List<hod2v1_Part>() },
                new hod2v1("GunRise1") { parts = new List<hod2v1_Part>() },
                new hod2v1("GunRise2") { parts = new List<hod2v1_Part>() }
            };

            int swordIdleAction = controller.idleAction + 50;
            int swordRiseStartAction = controller.riseStartAction + 50;
            int swordRiseAction = controller.riseAction + 50;
            robo.ani.animations[swordIdleAction].frames = new List<hod2v1>
            {
                new hod2v1("SwordIdle") { parts = new List<hod2v1_Part>() }
            };
            robo.ani.animations[swordRiseStartAction].frames = new List<hod2v1>
            {
                new hod2v1("SwordJumpStart") { parts = new List<hod2v1_Part>() }
            };
            robo.ani.animations[swordRiseAction].frames = new List<hod2v1>
            {
                new hod2v1("SwordRise0") { parts = new List<hod2v1_Part>() },
                new hod2v1("SwordRise1") { parts = new List<hod2v1_Part>() },
                new hod2v1("SwordRise2") { parts = new List<hod2v1_Part>() }
            };

            controller.robo = robo;
            controller.useColliderGrounding = false;
            controller.maximumEnergy = 1000f;
            controller.currentEnergy = 1000f;
            controller.state.ResetDefaults();

            MethodInfo awake = typeof(TestPlayController).GetMethod("Awake", InstancePrivate);
            MethodInfo setWeapon = typeof(TestPlayController).GetMethod("SetHeldWeapon", InstancePrivate);
            MethodInfo updateAction = typeof(TestPlayController).GetMethod("UpdateActionFromInput", InstancePrivate);
            MethodInfo tickAnimation = typeof(TestPlayController).GetMethod("TickAnimation", InstancePrivate);
            MethodInfo applyRootMotion = typeof(TestPlayController).GetMethod("ApplyRootMotion", InstancePrivate);
            Require(awake != null && setWeapon != null && updateAction != null &&
                    tickAnimation != null && applyRootMotion != null,
                "basic dual-channel jump runtime helpers are available", ref assertions);

            awake.Invoke(controller, null);
            setWeapon.Invoke(controller, new object[] { "GUN" });
            controller.SetAirborneFlag(false);
            controller.ChangeAnimation(controller.idleAction);
            SetField(controller, "riseKeyHeld", true);
            updateAction.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.riseStartAction,
                "grounded Z input starts gun jump action 3", ref assertions);
            tickAnimation.Invoke(controller, null);
            tickAnimation.Invoke(controller, null);
            applyRootMotion.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.riseAction &&
                    controller.state.GetInt(178) == 1 && controller.state.GetInt(179) == 1 &&
                    controller.state.GetInt(151) == 1 && rootObject.transform.position.y > 0f,
                "basic jump ANI runs main-channel lift and secondary-channel work", ref assertions);
            for (int heldTick = 0; heldTick < controller.riseMaximumTicks + 10; heldTick++)
            {
                updateAction.Invoke(controller, null);
                tickAnimation.Invoke(controller, null);
            }
            Require(controller.currentAnimationIndex == controller.riseAction &&
                    (bool)GetField(controller, "animationPoseHeldAtEnd") &&
                    controller.frameIndex == robo.ani.animations[controller.riseAction].frames.Count - 1 &&
                    (int)GetField(controller, "actionTick") > controller.riseMaximumTicks,
                "held gun jump plays action 7 once and keeps its final pose without a 12-tick cutoff", ref assertions);
            SetField(controller, "riseKeyHeld", false);
            updateAction.Invoke(controller, null);
            Require(controller.currentAnimationIndex == controller.airIdleAction,
                "releasing Z after the minimum rise window leaves the held pose for air-stop action 8", ref assertions);

            rootObject.transform.position = Vector3.zero;
            SetField(controller, "velocity", Vector3.zero);
            SetField(controller, "forceCommand", Vector3.zero);
            SetField(controller, "riseSequenceActive", false);
            SetField(controller, "previousAirborneFlag", false);
            SetField(controller, "riseKeyHeld", false);
            controller.SetAirborneFlag(false);
            controller.state.SetInt(178, 0);
            controller.state.SetInt(179, 0);
            setWeapon.Invoke(controller, new object[] { "SWORD" });
            controller.ChangeAnimation(controller.idleAction);
            SetField(controller, "riseKeyHeld", true);
            updateAction.Invoke(controller, null);
            Require(controller.currentAnimationIndex == swordRiseStartAction,
                "grounded Z input starts sword jump pose action 53", ref assertions);
            tickAnimation.Invoke(controller, null);
            tickAnimation.Invoke(controller, null);
            applyRootMotion.Invoke(controller, null);
            Require(controller.currentAnimationIndex == swordRiseAction &&
                    ReferenceEquals(GetField(controller, "currentScriptAnimation"), robo.ani.animations[controller.riseAction]) &&
                    controller.state.GetInt(178) == 1 && controller.state.GetInt(179) == 1 &&
                    rootObject.transform.position.y > 0f,
                "sword jump poses 53/57 reuse dual-channel scripts 3/7 and rise", ref assertions);
            for (int heldTick = 0; heldTick < controller.riseMaximumTicks + 10; heldTick++)
            {
                updateAction.Invoke(controller, null);
                tickAnimation.Invoke(controller, null);
            }
            Require(controller.currentAnimationIndex == swordRiseAction &&
                    (bool)GetField(controller, "animationPoseHeldAtEnd") && controller.frameIndex == 2,
                "held sword jump plays pose action 57 once and keeps its final frame", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }

    static void VerifyOriginalSoundIds(ref int assertions)
    {
        RequireSound(0, "shot.wav", ref assertions);
        RequireSound(20, "magic02.wav", ref assertions);
        RequireSound(97, "PI2.wav", ref assertions);
        RequireSound(98, "PI.wav", ref assertions);
        RequireSound(99, "cursor27.wav", ref assertions);

        Require(!TestPlayOriginalSoundSetup.TryGetFileName(21, out _), "unconfirmed Snd IDs stay unmapped", ref assertions);
        Require(TestPlayOriginalSoundSetup.TryGetPropulsionAdapterFileName(out string propulsionFileName) &&
                string.Equals(propulsionFileName, "burner.wav", StringComparison.OrdinalIgnoreCase),
            "the compatibility propulsion adapter filename remains burner.wav",
            ref assertions);
        Require(TestPlayOriginalSoundSetup.TryGetPropulsionAdapterFileNames(
                    out string propulsionStartFileName,
                    out string propulsionLoopFileName) &&
                string.Equals(propulsionStartFileName, "burner.wav", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(propulsionLoopFileName, "burner_f15.wav", StringComparison.OrdinalIgnoreCase),
            "BURNER output starts burner.wav once and loops burner_f15.wav through the Unity adapter",
            ref assertions);
    }

    static void RequireSound(int id, string expectedFileName, ref int assertions)
    {
        bool found = TestPlayOriginalSoundSetup.TryGetFileName(id, out string fileName);
        Require(found && string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase), "original Snd(" + id + ") mapping", ref assertions);
    }

    static void VerifyOriginalTextureTables(ref int assertions)
    {
        RequireLoadTexture(0, "Beam.bmp", ref assertions);
        RequireLoadTexture(10, "GSmoke.png", ref assertions);
        RequireLoadTexture(13, "beam2.png", ref assertions);
        RequireLoadTexture(43, "burner4.png", ref assertions);
        RequireScriptTexture(6, "GSmoke.png", ref assertions);
        RequireScriptTexture(12, "sabel.png", ref assertions);
        RequireScriptTexture(14, "beam2.png", ref assertions);
        RequireScriptTexture(39, "blueLight.bmp", ref assertions);
        Require(!TestPlayOriginalTextureSetup.TryGetScriptTextureFileName(11, out _), "guide texture id 11 stays unmapped", ref assertions);
    }

    static void RequireLoadTexture(int loadSequence, string expectedFileName, ref int assertions)
    {
        bool found = TestPlayOriginalTextureSetup.TryGetLoadSequenceFileName(loadSequence, out string fileName);
        Require(found && string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase),
            "original texture load sequence " + loadSequence, ref assertions);
    }

    static void RequireScriptTexture(int textureId, string expectedFileName, ref int assertions)
    {
        bool found = TestPlayOriginalTextureSetup.TryGetScriptTextureFileName(textureId, out string fileName);
        Require(found && string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase),
            "guide script texture id " + textureId, ref assertions);
    }

    static void VerifyBurnerDirectionAndVisual(ref int assertions)
    {
        MethodInfo testPlayRotation = typeof(TestPlayController).GetMethod("BurnerDirectionToRotation", StaticPrivate);
        MethodInfo sptRotation = typeof(SptParser).GetMethod("DirectionToRotation", StaticPrivate);
        Require(testPlayRotation != null && sptRotation != null,
            "SPT and TestPlay burner direction adapters are available", ref assertions);

        Quaternion testPlayUp = (Quaternion)testPlayRotation.Invoke(null, new object[] { SptDirection.UP });
        Quaternion testPlayDown = (Quaternion)testPlayRotation.Invoke(null, new object[] { SptDirection.DOWN });
        Quaternion sptUp = (Quaternion)sptRotation.Invoke(null, new object[] { SptDirection.UP });
        Quaternion sptDown = (Quaternion)sptRotation.Invoke(null, new object[] { SptDirection.DOWN });
        Require(
            Vector3.Dot(testPlayUp * Vector3.forward, Vector3.forward) > 0.999f &&
            Vector3.Dot(testPlayDown * Vector3.forward, Vector3.forward) > 0.999f,
            "TestPlay BURNERSET UP and DOWN preserve the HOD output local Z+ basis", ref assertions);
        Require(
            Vector3.Dot(sptUp * Vector3.forward, testPlayUp * Vector3.forward) > 0.999f &&
            Vector3.Dot(sptDown * Vector3.forward, testPlayDown * Vector3.forward) > 0.999f,
            "SPT preview and TestPlay use the same burner direction convention", ref assertions);

        SptRuntimeData fourthTokenData = SptParser.Parse(
            "BURNERSET(0,Output01.x,1.0,UP);\n" +
            "BURNERSET(1,Output02.x,1.0,LEGACY_CUSTOM_TOKEN);");
        Require(
            fourthTokenData.BurnerSets.Count == 2 &&
            fourthTokenData.BurnerSets[0].FourthToken == "UP" &&
            fourthTokenData.BurnerSets[1].FourthToken == "LEGACY_CUSTOM_TOKEN",
            "BURNERSET preserves the required fourth token, including values unknown to the Unity adapter",
            ref assertions);
        Require(
            fourthTokenData.BurnerSets[1].Direction == SptDirection.UP &&
            Vector3.Dot(
                (Quaternion)sptRotation.Invoke(null, new object[] { fourthTokenData.BurnerSets[1].Direction }) * Vector3.forward,
                Vector3.forward) > 0.999f,
            "unknown BURNERSET fourth tokens use the original-compatible no-rotation fallback",
            ref assertions);

        Texture2D burnerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/Generated/TestPlay/OriginalTextures/07_burner.png");
        Shader effectShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TestPlayOriginalEffect.shader");
        Require(burnerTexture != null && effectShader != null,
            "decrypted original burner texture and additive shader are available", ref assertions);

        GameObject burnerObject = new GameObject("TestPlayVerificationBurnerVisual");
        try
        {
            TestPlayBurnerCone burner = burnerObject.AddComponent<TestPlayBurnerCone>();
            burner.ConfigureVisual(burnerTexture, effectShader);
            burner.SetTarget(true, 2f, 0.2f, Color.white, 0f);

            Mesh mesh = burnerObject.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer renderer = burnerObject.GetComponent<MeshRenderer>();
            Require(burner.UsesOriginalTexture && mesh != null && mesh.vertexCount == 8 && mesh.uv.Length == 8,
                "burner uses the original texture on crossed plume planes", ref assertions);
            Require(
                Mathf.Approximately(mesh.bounds.min.z, 0f) && Mathf.Approximately(mesh.bounds.max.z, 1f) &&
                Vector3.Distance(burnerObject.transform.localScale, new Vector3(0.4f, 0.4f, 2f)) < 0.0001f,
                "original burner plume is rooted at its SPT point and scales along local Z", ref assertions);
            Require(
                renderer.sharedMaterial != null && renderer.sharedMaterial.GetTexture("_MainTex") == burnerTexture,
                "burner material carries the decrypted original texture", ref assertions);
            Require(
                CountRenderedBurnerPixels(burnerObject) > 0,
                "burner shader renders visible pixels in the active render pipeline", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(burnerObject);
        }

        GameObject particleRoot = new GameObject("TestPlayVerificationBurnerParticleRoot");
        try
        {
            SptRuntimeData data = new SptRuntimeData();
            BurnerSetInfo info = new BurnerSetInfo
            {
                Id = 0,
                FrameName = particleRoot.name,
                Scale = 1f,
                Direction = SptDirection.UP,
                BoneTr = particleRoot.transform
            };
            data.BurnerSets.Add(info.Id, info);
            SptParser.BuildBurnerEffects(data);
            ParticleSystemRenderer particleRenderer = info.Ps != null
                ? info.Ps.GetComponent<ParticleSystemRenderer>()
                : null;
            Shader particleShader = particleRenderer != null && particleRenderer.sharedMaterial != null
                ? particleRenderer.sharedMaterial.shader
                : null;
            Require(
                particleShader != null && particleShader.isSupported &&
                !string.Equals(particleShader.name, "Hidden/InternalErrorShader", StringComparison.Ordinal),
                "fallback burner particle uses a supported non-error shader", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(particleRoot);
        }
    }

    static int CountRenderedBurnerPixels(GameObject burnerObject)
    {
        const int DiagnosticLayer = 31;
        int oldLayer = burnerObject.layer;
        GameObject cameraObject = new GameObject("TestPlayVerificationBurnerCamera");
        RenderTexture renderTexture = null;
        Texture2D readback = null;
        RenderTexture oldActive = RenderTexture.active;
        try
        {
            burnerObject.layer = DiagnosticLayer;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << DiagnosticLayer;
            camera.orthographic = true;
            camera.orthographicSize = 1.2f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10f;
            camera.transform.position = new Vector3(0f, 3f, 1f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            renderTexture = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTexture;
            camera.Render();
            camera.targetTexture = null;

            RenderTexture.active = renderTexture;
            readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            readback.Apply();

            int visiblePixels = 0;
            Color32[] pixels = readback.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.r > 4 || pixel.g > 4 || pixel.b > 4)
                    visiblePixels++;
            }
            return visiblePixels;
        }
        finally
        {
            burnerObject.layer = oldLayer;
            RenderTexture.active = oldActive;
            if (readback != null)
                UnityEngine.Object.DestroyImmediate(readback);
            if (renderTexture != null)
                UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    static void VerifyOriginalSimulationClockAndLock(ref int assertions)
    {
        GameObject controllerObject = new GameObject("TestPlayVerificationClockLockController");
        GameObject rootObject = new GameObject("TestPlayVerificationClockLockRoot");
        GameObject targetObject = new GameObject("TestPlayVerificationClockLockTarget");
        GameObject aimObject = new GameObject("TestPlayVerificationClockLockAim");
        try
        {
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            UI_SPT spt = controllerObject.AddComponent<UI_SPT>();
            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();
            robo.root = rootObject;
            controller.robo = robo;
            controller.target = target;
            controller.sptSource = spt;
            aimObject.transform.SetParent(rootObject.transform, false);
            targetObject.transform.position = new Vector3(4f, 1f, 0f);

            Require(Mathf.Abs(controller.GetOriginalTickDeltaTime() - 1f / 60f) < 0.000001f,
                "test play owns an original 60 Hz tick independent of Unity FixedUpdate", ref assertions);
            Require(controller.SecondsToOriginalTicks(0.3f) == 18,
                "0.3-second input windows are converted to 18 original ticks", ref assertions);
            MethodInfo tickWindow = typeof(TestPlayController).GetMethod("IsTickWithinWindow", StaticPrivate);
            Require(tickWindow != null &&
                    (bool)tickWindow.Invoke(null, new object[] { 10, 28, 18 }) &&
                    !(bool)tickWindow.Invoke(null, new object[] { 10, 29, 18 }),
                "double-tap windows use simulation ticks rather than render time", ref assertions);

            MethodInfo updateInput = typeof(TestPlayController).GetMethod("UpdateInputState", InstancePrivate);
            MethodInfo consumeInput = typeof(TestPlayController).GetMethod("ConsumeLatchedInput", InstancePrivate);
            SetField(controller, "latchedShotKeyPress", true);
            updateInput.Invoke(controller, null);
            Require(controller.state.GetInt(192) == 1, "a short render-frame shot press is latched for one simulation tick", ref assertions);
            consumeInput.Invoke(controller, null);
            updateInput.Invoke(controller, null);
            Require(controller.state.GetInt(192) == 0, "a consumed short press is not repeated on a catch-up tick", ref assertions);

            SetField(spt, "<LastSptData>k__BackingField", new SptRuntimeData { LockDist = 5f });
            Require(Mathf.Approximately(controller.GetConfiguredLockDistance(), 5f),
                "target lock uses Script.spt LockDist", ref assertions);

            MethodInfo updateLock = typeof(TestPlayController).GetMethod("UpdateTargetLock", InstancePrivate);
            MethodInfo updateTargetState = typeof(TestPlayController).GetMethod("UpdateTargetState", InstancePrivate);
            controller.state.SetInt(195, 1);
            updateLock.Invoke(controller, null);
            updateTargetState.Invoke(controller, null);
            Require(controller.targetLockActive && controller.lockedTarget == target,
                "lock input acquires the configured candidate target", ref assertions);
            Require(Mathf.Abs(controller.state.GetFloat(99) - Mathf.Sqrt(17f)) < 0.0001f &&
                    controller.state.GetInt(154) == target.stateId,
                "locked target distance and state feed the original state slots", ref assertions);

            Quaternion rootRotationBeforeAim = rootObject.transform.rotation;
            MethodInfo handle = typeof(TestPlayController).GetMethod("HandleCommand", InstancePrivate);
            MethodInfo applyAim = typeof(TestPlayController).GetMethod("ApplyQueuedAimCommands", InstancePrivate);
            MethodInfo resetBlock = typeof(TestPlayController).GetMethod("ResetOriginalBlockState", InstancePrivate);
            handle.Invoke(controller, new object[] { "LockBodyUpTarget", Values(-1f, -1f), "LockBodyUpTarget(-1,-1);" });
            applyAim.Invoke(controller, null);
            Require(rootObject.transform.rotation == rootRotationBeforeAim,
                "unmapped body aim no longer rotates the locomotion root", ref assertions);

            resetBlock.Invoke(controller, null);
            controller.bodyUpAimRoot = aimObject.transform;
            handle.Invoke(controller, new object[] { "LockBodyUpTarget", Values(-1f, -1f), "LockBodyUpTarget(-1,-1);" });
            applyAim.Invoke(controller, null);
            Require(aimObject.transform.rotation != Quaternion.identity && rootObject.transform.rotation == rootRotationBeforeAim,
                "mapped body aim rotates only its explicit visual transform", ref assertions);

            controller.state.SetInt(195, 0);
            updateLock.Invoke(controller, null);
            targetObject.transform.position = new Vector3(6f, 0f, 0f);
            updateLock.Invoke(controller, null);
            Require(!controller.targetLockActive && controller.GetLockedTargetTransform() == null,
                "a target outside LockDist is released", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(aimObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }

    static void VerifyCameraControllerLifecycle(ref int assertions)
    {
        GameObject cameraObject = new GameObject("TestPlayVerificationCamera");
        GameObject controllerObject = new GameObject("TestPlayVerificationController");
        GameObject rootObject = new GameObject("TestPlayVerificationRoot");
        GameObject targetObject = new GameObject("TestPlayVerificationTarget");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            FreeCam freeCamera = cameraObject.AddComponent<FreeCam>();
            TestPlayCameraController testCamera = cameraObject.AddComponent<TestPlayCameraController>();
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();

            Vector3 originalPosition = new Vector3(2f, 3f, 4f);
            Quaternion originalRotation = Quaternion.Euler(10f, 20f, 0f);
            cameraObject.transform.SetPositionAndRotation(originalPosition, originalRotation);
            camera.fieldOfView = 40f;
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            targetObject.transform.position = new Vector3(8f, 0f, 8f);
            robo.root = rootObject;
            controller.robo = robo;
            controller.target = target;
            Require(controller.TryAcquireTargetLock(), "camera test acquires an explicit target lock", ref assertions);
            testCamera.controlledCamera = camera;
            testCamera.editorCamera = freeCamera;

            testCamera.EnterTestPlayCamera(controller);
            Require(testCamera.cameraModeActive && !freeCamera.enabled, "test camera replaces editor FreeCam", ref assertions);
            Require(testCamera.usingLockTarget, "test camera uses available lock target", ref assertions);
            Require(Mathf.Approximately(camera.fieldOfView, 60f), "test camera applies the original 60-degree FOV", ref assertions);
            Require(cameraObject.transform.position != originalPosition, "test camera enters follow pose", ref assertions);

            MethodInfo updateCamera = typeof(TestPlayCameraController).GetMethod("UpdateFollowCamera", InstancePrivate);
            Require(updateCamera != null, "locked camera direction verification is available", ref assertions);
            targetObject.transform.position = Vector3.forward * 10f;
            controller.state.SetInt(190, 4);
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.left);
            updateCamera.Invoke(testCamera, new object[] { rootObject.transform, 0f });
            Require(cameraObject.transform.position.x > rootObject.transform.position.x && cameraObject.transform.forward.x < 0f,
                "left movement turns locked camera left", ref assertions);

            controller.state.SetInt(190, 6);
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.right);
            updateCamera.Invoke(testCamera, new object[] { rootObject.transform, 0f });
            Require(cameraObject.transform.position.x < rootObject.transform.position.x && cameraObject.transform.forward.x > 0f,
                "right movement turns locked camera right", ref assertions);

            controller.state.SetInt(190, 8);
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            updateCamera.Invoke(testCamera, new object[] { rootObject.transform, 0f });
            Vector3 forwardCameraPosition = cameraObject.transform.position;

            controller.state.SetInt(190, 2);
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.back);
            updateCamera.Invoke(testCamera, new object[] { rootObject.transform, 0f });
            Require(cameraObject.transform.position.z < forwardCameraPosition.z,
                "backward movement pulls locked camera away from target", ref assertions);
            Require(cameraObject.transform.position.y > forwardCameraPosition.y,
                "backward movement raises locked camera framing", ref assertions);
            Require(cameraObject.transform.position.z < rootObject.transform.position.z && cameraObject.transform.forward.z > 0f,
                "backward movement keeps lock target in front of camera", ref assertions);

            controller.state.SetInt(190, 1);
            rootObject.transform.rotation = Quaternion.LookRotation((Vector3.back + Vector3.left).normalized);
            updateCamera.Invoke(testCamera, new object[] { rootObject.transform, 0f });
            Require(cameraObject.transform.position.z <= rootObject.transform.position.z + 0.001f && cameraObject.transform.forward.z > 0f,
                "backward diagonal movement does not cross in front of player", ref assertions);

            testCamera.ExitTestPlayCamera();
            Require(!testCamera.cameraModeActive && freeCamera.enabled, "test camera restores editor control", ref assertions);
            Require(cameraObject.transform.position == originalPosition && cameraObject.transform.rotation == originalRotation &&
                    Mathf.Approximately(camera.fieldOfView, 40f), "test camera restores transform and FOV", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    static void VerifyMovementReferenceModes(ref int assertions)
    {
        GameObject controllerObject = new GameObject("TestPlayVerificationMovementReferenceController");
        GameObject rootObject = new GameObject("TestPlayVerificationMovementReferenceRoot");
        GameObject targetObject = new GameObject("TestPlayVerificationMovementReferenceTarget");
        GameObject cameraObject = new GameObject("TestPlayVerificationMovementReferenceCamera");
        TestPlayCameraController testCamera = null;
        try
        {
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();
            Camera camera = cameraObject.AddComponent<Camera>();
            testCamera = cameraObject.AddComponent<TestPlayCameraController>();
            float defaultInputTurnDegrees = controller.inputTurnDegreesPerTick;
            robo.root = rootObject;
            controller.robo = robo;
            controller.target = target;
            controller.cameraController = testCamera;
            testCamera.controlledCamera = camera;
            targetObject.transform.position = Vector3.forward * 10f;
            Require(controller.TryAcquireTargetLock(), "movement test acquires an explicit target lock", ref assertions);
            controller.currentAnimationIndex = controller.moveAction;
            controller.inputTurnDegreesPerTick = 0f;
            SetField(controller, "moveAnimationActive", true);
            testCamera.EnterTestPlayCamera(controller);

            MethodInfo buildMove = typeof(TestPlayController).GetMethod("TryGetOriginalStyleInputWorldMove", InstancePrivate);
            MethodInfo resetHeading = typeof(TestPlayController).GetMethod("ResetInputMoveHeading", InstancePrivate);
            Require(buildMove != null && resetHeading != null, "movement reference switching is available", ref assertions);
            Require(Mathf.Approximately(defaultInputTurnDegrees, 12f) &&
                    Mathf.Approximately(testCamera.lateralMovementHeadingWeight, 0.5f) &&
                    Mathf.Approximately(testCamera.maxLockedOrbitAngle, 60f),
                "original steering and reduced lock-camera orbit are the defaults", ref assertions);

            controller.state.SetInt(190, 8);
            SetField(testCamera, "movementReferenceForward", Vector3.right);
            SetField(testCamera, "hasMovementReference", true);
            object[] targetArgs = { rootObject.transform, new Vector3(0f, 0f, 0.2f), Vector3.zero };
            bool targetMove = (bool)buildMove.Invoke(controller, targetArgs);
            Require(targetMove && Vector3.Dot(((Vector3)targetArgs[2]).normalized, Vector3.forward) > 0.999f,
                "target-relative movement has priority while lock mode is enabled", ref assertions);

            controller.useTargetRelativeMovement = false;
            controller.useCameraRelativeMovement = true;
            testCamera.useLockTargetWhenAvailable = false;
            resetHeading.Invoke(controller, null);
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.right);
            object[] cameraArgs = { rootObject.transform, new Vector3(0f, 0f, 0.2f), Vector3.zero };
            bool cameraMove = (bool)buildMove.Invoke(controller, cameraArgs);
            Require(cameraMove && Vector3.Dot(((Vector3)cameraArgs[2]).normalized, Vector3.right) > 0.999f,
                "camera-relative mode maps forward input to the live camera heading", ref assertions);

            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.back);
            object[] updatedCameraArgs = { rootObject.transform, new Vector3(0f, 0f, 0.2f), Vector3.zero };
            bool updatedCameraMove = (bool)buildMove.Invoke(controller, updatedCameraArgs);
            Require(updatedCameraMove && Vector3.Dot(((Vector3)updatedCameraArgs[2]).normalized, Vector3.right) > 0.999f,
                "held input keeps the camera basis captured at input start", ref assertions);

            controller.useCameraRelativeMovement = false;
            resetHeading.Invoke(controller, null);
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            object[] legacyArgs = { rootObject.transform, new Vector3(0f, 0f, 0.2f), Vector3.zero };
            bool legacyMove = (bool)buildMove.Invoke(controller, legacyArgs);
            Require(legacyMove && Vector3.Dot(((Vector3)legacyArgs[2]).normalized, Vector3.left) > 0.999f,
                "disabling both reference switches preserves the input-start mech heading", ref assertions);
        }
        finally
        {
            if (testCamera != null)
                testCamera.ExitTestPlayCamera();
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }


    static void VerifyOriginalDirectionMovement(ref int assertions)
    {
        MethodInfo encode = typeof(TestPlayController).GetMethod("EncodeDirectionInput", StaticPrivate);
        MethodInfo directionVector = typeof(TestPlayController).GetMethod("GetDirectionVector", StaticPrivate);
        Require(encode != null && directionVector != null, "original direction helpers are available", ref assertions);

        Require(
            (int)encode.Invoke(null, new object[] { 0, 1 }) == 8 &&
            (int)encode.Invoke(null, new object[] { 0, -1 }) == 2 &&
            (int)encode.Invoke(null, new object[] { -1, 0 }) == 4 &&
            (int)encode.Invoke(null, new object[] { 1, 0 }) == 6,
            "cardinal input uses original keypad direction codes", ref assertions);
        Require(
            (int)encode.Invoke(null, new object[] { -1, 1 }) == 7 &&
            (int)encode.Invoke(null, new object[] { 1, 1 }) == 9 &&
            (int)encode.Invoke(null, new object[] { -1, -1 }) == 1 &&
            (int)encode.Invoke(null, new object[] { 1, -1 }) == 3,
            "combined arrows use original diagonal direction codes", ref assertions);

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        Vector3 left = (Vector3)directionVector.Invoke(null, new object[] { 4, forward, right });
        Vector3 back = (Vector3)directionVector.Invoke(null, new object[] { 2, forward, right });
        Vector3 diagonal = (Vector3)directionVector.Invoke(null, new object[] { 9, forward, right });
        Require(left == Vector3.left && back == Vector3.back, "left and back map away from forward Move", ref assertions);
        Require(Mathf.Approximately(diagonal.magnitude, 1f) && diagonal.x > 0f && diagonal.z > 0f,
            "diagonal movement is normalized", ref assertions);

        GameObject controllerObject = new GameObject("TestPlayVerificationDirectionController");
        GameObject rootObject = new GameObject("TestPlayVerificationDirectionRoot");
        GameObject targetObject = new GameObject("TestPlayVerificationDirectionTarget");
        try
        {
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();
            robo.root = rootObject;
            controller.robo = robo;
            controller.target = target;
            controller.currentAnimationIndex = controller.moveAction;
            controller.inputTurnDegreesPerTick = 0f;
            controller.inputMoveMagnitude = 0.08f;
            targetObject.transform.position = Vector3.forward * 10f;
            Require(controller.TryAcquireTargetLock(), "direction test acquires an explicit target lock", ref assertions);
            SetField(controller, "moveAnimationActive", true);

            MethodInfo buildMove = typeof(TestPlayController).GetMethod("TryGetOriginalStyleInputWorldMove", InstancePrivate);
            MethodInfo resetHeading = typeof(TestPlayController).GetMethod("ResetInputMoveHeading", InstancePrivate);
            Require(buildMove != null && resetHeading != null, "directional movement runtime is available", ref assertions);

            controller.state.SetInt(190, 4);
            object[] leftArgs = { rootObject.transform, new Vector3(0f, 0f, 0.2f), Vector3.zero };
            bool movedLeft = (bool)buildMove.Invoke(controller, leftArgs);
            Vector3 leftMove = (Vector3)leftArgs[2];
            Require(movedLeft && Vector3.Distance(leftMove, Vector3.left * 0.2f) < 0.0001f,
                "script forward magnitude is redirected left", ref assertions);
            Require(Vector3.Dot(rootObject.transform.forward, Vector3.left) > 0.999f,
                "movement heading turns the mech", ref assertions);

            resetHeading.Invoke(controller, null);
            controller.state.SetInt(190, 2);
            object[] backArgs = { rootObject.transform, new Vector3(0f, 0f, 0.15f), Vector3.zero };
            bool movedBack = (bool)buildMove.Invoke(controller, backArgs);
            Vector3 backMove = (Vector3)backArgs[2];
            Require(movedBack && Vector3.Distance(backMove, Vector3.back * 0.15f) < 0.0001f,
                "script forward magnitude is redirected backward", ref assertions);

            // Non-lock camera movement basis follows the mech's live facing. The
            // controller must latch the basis for one held direction; otherwise a
            // DOWN input flips its desired heading every tick after the mech turns.
            controller.ClearTargetLock();
            controller.useTargetRelativeMovement = false;
            controller.useCameraRelativeMovement = true;
            TestPlayCameraController nonLockCamera = controllerObject.AddComponent<TestPlayCameraController>();
            nonLockCamera.controller = controller;
            nonLockCamera.controlledCamera = controllerObject.AddComponent<Camera>();
            controller.cameraController = nonLockCamera;
            nonLockCamera.EnterTestPlayCamera(controller);
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            resetHeading.Invoke(controller, null);
            controller.state.SetInt(190, 2);
            object[] firstHeldBackArgs = { rootObject.transform, new Vector3(0f, 0f, 0.15f), Vector3.zero };
            object[] secondHeldBackArgs = { rootObject.transform, new Vector3(0f, 0f, 0.15f), Vector3.zero };
            bool firstHeldBack = (bool)buildMove.Invoke(controller, firstHeldBackArgs);
            bool secondHeldBack = (bool)buildMove.Invoke(controller, secondHeldBackArgs);
            Require(
                firstHeldBack && secondHeldBack &&
                Vector3.Dot(((Vector3)firstHeldBackArgs[2]).normalized, Vector3.back) > 0.999f &&
                Vector3.Dot(((Vector3)secondHeldBackArgs[2]).normalized, Vector3.back) > 0.999f,
                "held non-lock DOWN keeps its initial camera basis and remains backward", ref assertions);

            nonLockCamera.ExitTestPlayCamera();
            controller.useCameraRelativeMovement = false;
            controller.useTargetRelativeMovement = true;
            controller.TryAcquireTargetLock();
            resetHeading.Invoke(controller, null);
            controller.state.SetInt(190, 6);
            object[] fallbackArgs = { rootObject.transform, Vector3.zero, Vector3.zero };
            bool movedRight = (bool)buildMove.Invoke(controller, fallbackArgs);
            Vector3 rightMove = (Vector3)fallbackArgs[2];
            Require(movedRight && Vector3.Distance(rightMove, Vector3.right * controller.inputMoveMagnitude) < 0.0001f,
                "missing script Move uses input fallback magnitude in the requested direction", ref assertions);

            controller.useColliderGrounding = false;
            controller.inputTurnDegreesPerTick = 0f;
            SetField(controller, "moveCommand", new Vector3(0f, 0f, 0.08f));
            MethodInfo applyRootMotion = typeof(TestPlayController).GetMethod("ApplyRootMotion", InstancePrivate);
            Require(applyRootMotion != null, "root motion direction verification is available", ref assertions);

            Vector3 upDelta = RunRootMotionDirection(controller, rootObject.transform, applyRootMotion, resetHeading, 8);
            Vector3 downDelta = RunRootMotionDirection(controller, rootObject.transform, applyRootMotion, resetHeading, 2);
            Vector3 leftDelta = RunRootMotionDirection(controller, rootObject.transform, applyRootMotion, resetHeading, 4);
            Vector3 rightDelta = RunRootMotionDirection(controller, rootObject.transform, applyRootMotion, resetHeading, 6);
            Vector3 diagonalDelta = RunRootMotionDirection(controller, rootObject.transform, applyRootMotion, resetHeading, 9);
            Require(
                Vector3.Dot(upDelta.normalized, Vector3.forward) > 0.999f &&
                Vector3.Dot(downDelta.normalized, Vector3.back) > 0.999f &&
                Vector3.Dot(leftDelta.normalized, Vector3.left) > 0.999f &&
                Vector3.Dot(rightDelta.normalized, Vector3.right) > 0.999f &&
                diagonalDelta.x > 0f && diagonalDelta.z > 0f &&
                Mathf.Abs(diagonalDelta.magnitude - upDelta.magnitude) < 0.0001f,
                "ApplyRootMotion moves in all requested directions without diagonal speed gain", ref assertions);

            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            resetHeading.Invoke(controller, null);
            controller.state.SetInt(190, 4);
            controller.inputTurnDegreesPerTick = 12f;
            for (int i = 0; i < 8; i++)
                applyRootMotion.Invoke(controller, null);
            Require(Vector3.Dot(rootObject.transform.forward, Vector3.left) > 0.999f && rootObject.transform.position.x < 0f,
                "original direction steering reaches left over eight 12-degree ticks", ref assertions);

            MethodInfo prepareStep = typeof(TestPlayController).GetMethod("PrepareStepFromDirection", InstancePrivate);
            MethodInfo finishStep = typeof(TestPlayController).GetMethod("ShouldFinishOriginalStyleStep", InstancePrivate);
            MethodInfo resolveStepTicks = typeof(TestPlayController).GetMethod("ResolveStepScriptTicks", InstancePrivate);
            MethodInfo forceAirborneByAction = typeof(TestPlayController).GetMethod("ShouldForceAirborneByAction", InstancePrivate);
            MethodInfo stepExitAction = typeof(TestPlayController).GetMethod("GetOriginalStepExitAction", InstancePrivate);
            MethodInfo heldActionFromInput = typeof(TestPlayController).GetMethod("GetHeldActionFromInput", InstancePrivate);
            MethodInfo updateStepRecoveryGate = typeof(TestPlayController).GetMethod("UpdateStepRecoveryInputGate", InstancePrivate);
            Require(
                prepareStep != null && finishStep != null && resolveStepTicks != null &&
                forceAirborneByAction != null && stepExitAction != null && heldActionFromInput != null &&
                updateStepRecoveryGate != null,
                "original step runtime helpers are available", ref assertions);

            rootObject.transform.position = Vector3.zero;
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            int leftInputFromForward = (int)prepareStep.Invoke(controller, new object[] { 4 });
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
            int leftInputFromRight = (int)prepareStep.Invoke(controller, new object[] { 4 });
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            int leftInputFromBack = (int)prepareStep.Invoke(controller, new object[] { 4 });
            rootObject.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            int leftInputFromLeft = (int)prepareStep.Invoke(controller, new object[] { 4 });
            Require(
                leftInputFromForward == controller.leftStepAction &&
                leftInputFromRight == controller.backStepAction &&
                leftInputFromBack == controller.rightStepAction &&
                leftInputFromLeft == controller.forwardStepAction,
                "step animation 9..12 follows input direction relative to the pre-step body facing", ref assertions);

            controller.stepTurnDegreesPerTick = 3f;
            controller.preserveStepReferenceFacing = true;
            controller.useOriginalStepFacing = true;
            Vector3 stepForward = RunStepRootMotionDirection(controller, rootObject.transform, applyRootMotion, prepareStep, 8);
            Vector3 stepBack = RunStepRootMotionDirection(controller, rootObject.transform, applyRootMotion, prepareStep, 2);
            Vector3 stepLeft = RunStepRootMotionDirection(controller, rootObject.transform, applyRootMotion, prepareStep, 4);
            Vector3 stepRight = RunStepRootMotionDirection(controller, rootObject.transform, applyRootMotion, prepareStep, 6);
            Require(
                Vector3.Dot(stepForward.normalized, Vector3.forward) > 0.999f &&
                Vector3.Dot(stepBack.normalized, Vector3.back) > 0.999f &&
                stepLeft.x < 0f && stepLeft.z > 0f &&
                stepRight.x > 0f && stepRight.z > 0f,
                "step root motion follows all four input directions with the original forward bias", ref assertions);
            Require(
                Mathf.Abs(stepLeft.normalized.z - (0.3f / Mathf.Sqrt(1.09f))) < 0.0001f &&
                Mathf.Abs(stepRight.magnitude - stepForward.magnitude) < 0.0001f,
                "side-step bias is 0.3 before normalization without speed gain", ref assertions);

            Vector3 stepDiagonal = RunStepRootMotionDirection(controller, rootObject.transform, applyRootMotion, prepareStep, 9);
            Vector3 expectedDiagonal = new Vector3(1f, 0f, 1.3f).normalized;
            Require(Vector3.Dot(stepDiagonal.normalized, expectedDiagonal) > 0.999f,
                "diagonal step keeps the original raw direction plus forward bias", ref assertions);
            Require(Mathf.Abs(Vector3.Angle(Vector3.forward, rootObject.transform.forward) - 3f) < 0.01f &&
                    Vector3.Dot(rootObject.transform.forward, Vector3.right) > 0f,
                "original diagonal step turns the chosen forward-step ANI toward world movement by at most three degrees per tick", ref assertions);

            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.LookRotation(Vector3.forward, Vector3.up));
            controller.state.SetInt(190, 4);
            int leftStep = (int)prepareStep.Invoke(controller, new object[] { 4 });
            controller.currentAnimationIndex = leftStep;
            SetField(controller, "stepSequenceActive", true);
            SetField(controller, "actionTick", 1);
            SetField(controller, "moveCommand", new Vector3(0f, 0f, 0.51f));
            applyRootMotion.Invoke(controller, null);
            Require(leftStep == controller.leftStepAction &&
                    Vector3.Angle(Vector3.forward, rootObject.transform.forward) < 0.001f &&
                    rootObject.transform.position.x < 0f,
                "left input selects left-step ANI and preserves the body facing while moving left", ref assertions);
            SetField(controller, "stepSequenceActive", false);

            controller.useOriginalStepFacing = false;
            controller.preserveStepReferenceFacing = true;
            RunStepRootMotionDirection(controller, rootObject.transform, applyRootMotion, prepareStep, 9);
            Require(Vector3.Angle(Vector3.forward, rootObject.transform.forward) < 0.001f,
                "legacy reference-facing approximation remains available behind its compatibility switch", ref assertions);
            controller.useOriginalStepFacing = true;

            controller.state.SetInt(190, 8);
            int durationAction = (int)prepareStep.Invoke(controller, new object[] { 8 });
            controller.currentAnimationIndex = durationAction;
            SetField(controller, "stepSequenceActive", true);
            SetField(controller, "actionTick", 15);
            controller.state.SetInt(190, 0);
            bool finishesAt15 = (bool)finishStep.Invoke(controller, null);
            SetField(controller, "actionTick", 16);
            bool finishesAt16 = (bool)finishStep.Invoke(controller, null);
            Require(!finishesAt15 && finishesAt16,
                "step release is ignored through tick 15 and accepted from tick 16", ref assertions);

            controller.state.SetInt(190, 8);
            SetField(controller, "actionTick", 60);
            bool finishesAt60 = (bool)finishStep.Invoke(controller, null);
            SetField(controller, "actionTick", 61);
            bool finishesAt61 = (bool)finishStep.Invoke(controller, null);
            Require(!finishesAt60 && finishesAt61,
                "held step has the original 61-tick hard limit", ref assertions);

            controller.maximumEnergy = 100f;
            controller.currentEnergy = 0f;
            controller.state.SetFloat(100, 0f);
            SetField(controller, "actionTick", 1);
            Require((bool)finishStep.Invoke(controller, null),
                "depleted movement energy terminates a step without waiting for tick 16", ref assertions);

            controller.currentAnimationIndex = durationAction;
            SetField(controller, "stepSequenceActive", true);
            controller.SetAirborneFlag(false);
            controller.groundedFlag = true;
            Require(!(bool)forceAirborneByAction.Invoke(controller, null),
                "a grounded step does not force the character into the airborne state", ref assertions);
            Require((int)stepExitAction.Invoke(controller, null) == controller.stepLandingAction,
                "a grounded step exits through dedicated step-landing action 6 instead of air movement", ref assertions);

            controller.SetAirborneFlag(true);
            controller.groundedFlag = false;
            Require((int)stepExitAction.Invoke(controller, null) == controller.airIdleAction,
                "an airborne step exits through original air-stop action 8", ref assertions);

            controller.currentAnimationIndex = controller.airIdleAction;
            SetField(controller, "stepSequenceActive", false);
            SetField(controller, "stepRecoveryActive", true);
            SetField(controller, "stepRecoveryDirection", 8);
            controller.state.SetInt(190, 8);
            Require((int)heldActionFromInput.Invoke(controller, null) == controller.airIdleAction,
                "holding the step direction keeps action 8 and cannot enter upward-Force action 4", ref assertions);
            controller.state.SetInt(190, 0);
            updateStepRecoveryGate.Invoke(controller, null);
            controller.state.SetInt(190, 8);
            Require((int)heldActionFromInput.Invoke(controller, null) == controller.airMoveAction,
                "releasing the direction clears step recovery so a later new input can use air movement", ref assertions);

            SetField(controller, "stepRecoveryActive", true);
            SetField(controller, "stepRecoveryDirection", 8);
            controller.state.SetInt(190, 6);
            updateStepRecoveryGate.Invoke(controller, null);
            Require((int)heldActionFromInput.Invoke(controller, null) == controller.airMoveAction,
                "changing direction also clears step recovery without inheriting the old held direction", ref assertions);

            controller.maxStepScriptTicksPerBlock = 5;
            int resolvedTicks = (int)resolveStepTicks.Invoke(controller, new object[] { new script { unk = 120 } });
            Require(resolvedTicks == 120,
                "step ANI block length is no longer truncated to five ticks", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }

    static void VerifyOriginalJumpAndBoost(ref int assertions)
    {
        GameObject controllerObject = new GameObject("TestPlayVerificationJumpBoostController");
        GameObject rootObject = new GameObject("TestPlayVerificationJumpBoostRoot");
        GameObject targetObject = new GameObject("TestPlayVerificationJumpBoostTarget");
        try
        {
            TestPlayController controller = controllerObject.AddComponent<TestPlayController>();
            RoboStructure robo = controllerObject.AddComponent<RoboStructure>();
            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();
            robo.root = rootObject;
            controller.robo = robo;
            controller.target = target;
            controller.useColliderGrounding = false;
            controller.requireLockInput = false;
            controller.maximumEnergy = 1000f;
            controller.currentEnergy = 1000f;
            controller.state.SetFloat(100, 1000f);
            targetObject.transform.position = Vector3.forward * 10f;

            MethodInfo normalizeActions = typeof(TestPlayController).GetMethod("NormalizeActionIds", InstancePrivate);
            MethodInfo airborneAction = typeof(TestPlayController).GetMethod("GetAirborneLocomotionAction", InstancePrivate);
            MethodInfo boostMove = typeof(TestPlayController).GetMethod("TryGetOriginalStyleBoostWorldMove", InstancePrivate);
            MethodInfo resetBoost = typeof(TestPlayController).GetMethod("ResetBoostRuntimeState", InstancePrivate);
            MethodInfo endBoost = typeof(TestPlayController).GetMethod("ShouldEndOriginalStyleBoost", InstancePrivate);
            MethodInfo endRise = typeof(TestPlayController).GetMethod("ShouldEndOriginalStyleRise", InstancePrivate);
            MethodInfo canAirRise = typeof(TestPlayController).GetMethod("CanStartOriginalAirRise", InstancePrivate);
            MethodInfo startBoost = typeof(TestPlayController).GetMethod("StartBoostAction", InstancePrivate);
            MethodInfo forceAirborne = typeof(TestPlayController).GetMethod("ShouldForceAirborneByAction", InstancePrivate);
            MethodInfo riseSteering = typeof(TestPlayController).GetMethod("ApplyOriginalRiseSteering", InstancePrivate);
            MethodInfo energyTick = typeof(TestPlayController).GetMethod("UpdateOriginalMovementEnergy", InstancePrivate);
            MethodInfo integrateForce = typeof(TestPlayController).GetMethod("IntegrateOriginalForceVelocity", InstancePrivate);
            MethodInfo scriptedMoveRetention = typeof(TestPlayController).GetMethod("GetScriptedMoveRetention", InstancePrivate);
            MethodInfo airIdleBrake = typeof(TestPlayController).GetMethod("ApplyOriginalAirIdleVerticalBrake", InstancePrivate);
            MethodInfo captureDrivenVelocity = typeof(TestPlayController).GetMethod("CaptureDrivenHorizontalVelocity", InstancePrivate);
            MethodInfo applyDrivenInertia = typeof(TestPlayController).GetMethod("ApplyPendingDrivenHorizontalInertia", InstancePrivate);
            MethodInfo applyRootMotion = typeof(TestPlayController).GetMethod("ApplyRootMotion", InstancePrivate);
            Require(
                normalizeActions != null && airborneAction != null && boostMove != null && resetBoost != null &&
                endBoost != null && endRise != null && canAirRise != null && startBoost != null &&
                forceAirborne != null && riseSteering != null && energyTick != null &&
                integrateForce != null && scriptedMoveRetention != null && airIdleBrake != null &&
                captureDrivenVelocity != null && applyDrivenInertia != null && applyRootMotion != null,
                "original jump/boost runtime helpers are available", ref assertions);

            controller.landingAction = controller.riseStartAction;
            normalizeActions.Invoke(controller, null);
            controller.stepLandingAction = controller.landingAction;
            normalizeActions.Invoke(controller, null);
            Require(controller.landingAction == 5 && controller.stepLandingAction == 6 && controller.airIdleAction == 8,
                "legacy landing IDs normalize to original regular landing 5 and step landing 6", ref assertions);

            controller.state.SetInt(190, 0);
            Require((int)airborneAction.Invoke(controller, null) == controller.airIdleAction,
                "neutral airborne state uses original air-stop action 8", ref assertions);
            controller.state.SetInt(190, 8);
            Require((int)airborneAction.Invoke(controller, null) == controller.airMoveAction,
                "directional airborne state uses original air-move action 4", ref assertions);
            Require(Mathf.Abs((float)scriptedMoveRetention.Invoke(
                        controller, new object[] { controller.airIdleAction }) - 0.99f) < 0.0001f,
                "air-stop action 8 retains inherited ANI Move by the original 0.99 factor", ref assertions);

            controller.currentAnimationIndex = controller.airIdleAction;
            controller.currentEnergy = 1000f;
            SetField(controller, "actionTick", controller.airIdleVerticalBrakeTicks - 1);
            SetField(controller, "velocity", Vector3.up * 0.049f);
            airIdleBrake.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y - 0.061f) < 0.0001f,
                "air-stop adds the original 0.012 vertical brake through c38 tick 30", ref assertions);

            SetField(controller, "actionTick", controller.airIdleVerticalBrakeTicks);
            SetField(controller, "velocity", Vector3.up * 0.049f);
            airIdleBrake.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y - 0.049f) < 0.0001f,
                "air-stop vertical brake stops at the original c38 < 31 boundary", ref assertions);

            SetField(controller, "actionTick", controller.airIdleVerticalBrakeTicks - 1);
            SetField(controller, "velocity", Vector3.up * controller.airIdleVerticalBrakeVelocityThreshold);
            airIdleBrake.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y -
                             controller.airIdleVerticalBrakeVelocityThreshold) < 0.0001f,
                "air-stop vertical brake requires Y velocity strictly below 0.05", ref assertions);

            controller.currentEnergy = 0f;
            SetField(controller, "velocity", Vector3.zero);
            airIdleBrake.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y) < 0.0001f,
                "air-stop vertical brake requires positive movement energy", ref assertions);

            controller.currentEnergy = 1000f;
            SetField(controller, "velocity", Vector3.zero);
            SetField(controller, "forceCommand", Vector3.zero);
            SetField(controller, "gvEnable", true);
            controller.SetAirborneFlag(true);
            airIdleBrake.Invoke(controller, null);
            integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y + 0.001f) < 0.0001f,
                "air-stop applies +0.012 before the common -0.013 gravity tick", ref assertions);

            controller.currentAnimationIndex = controller.boostAction;
            SetField(controller, "boostMotionActive", true);
            SetField(controller, "actionTick", 1);
            controller.state.SetInt(190, 4);
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            resetBoost.Invoke(controller, null);
            object[] initialBoostArgs = { rootObject.transform, new Vector3(0f, 0f, 0.25f), Vector3.zero };
            Require((bool)boostMove.Invoke(controller, initialBoostArgs), "boost root motion is handled", ref assertions);
            Vector3 initialBoostMove = (Vector3)initialBoostArgs[2];
            Require(Mathf.Abs(Vector3.Angle(Vector3.forward, initialBoostMove) - 15f) < 0.01f &&
                    Mathf.Abs(initialBoostMove.magnitude - 0.25f) < 0.0001f,
                "boost starts with original 15-degree steering and preserves ANI speed", ref assertions);

            SetField(controller, "actionTick", 10);
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            resetBoost.Invoke(controller, null);
            object[] sustainedBoostArgs = { rootObject.transform, new Vector3(0f, 0f, 0.25f), Vector3.zero };
            boostMove.Invoke(controller, sustainedBoostArgs);
            Require(Mathf.Abs(Vector3.Angle(Vector3.forward, (Vector3)sustainedBoostArgs[2]) - 2.5f) < 0.01f,
                "sustained 90-degree boost turn uses original 2.5-degree formula", ref assertions);

            SetField(controller, "riseKeyHeld", false);
            controller.state.SetInt(190, 0);
            SetField(controller, "actionTick", 30);
            Require(!(bool)endBoost.Invoke(controller, null), "boost ignores full release through tick 30", ref assertions);
            SetField(controller, "actionTick", 31);
            Require((bool)endBoost.Invoke(controller, null), "boost ends after tick 30 when all movement input is released", ref assertions);
            controller.state.SetInt(190, 8);
            Require(!(bool)endBoost.Invoke(controller, null), "direction input sustains boost after jump-key release", ref assertions);
            Require((bool)forceAirborne.Invoke(controller, null), "active boost disables gravity even after jump-key release", ref assertions);

            controller.currentAnimationIndex = controller.riseAction;
            SetField(controller, "riseSequenceActive", true);
            SetField(controller, "riseKeyHeld", true);
            controller.state.SetInt(191, 0);
            controller.currentEnergy = 1000f;
            SetField(controller, "actionTick", controller.riseMaximumTicks + 10);
            Require(!(bool)endRise.Invoke(controller, null),
                "held rise remains active beyond the legacy 12-tick limit", ref assertions);
            controller.currentEnergy = 0f;
            Require((bool)endRise.Invoke(controller, null),
                "held rise ends when movement energy is depleted", ref assertions);
            controller.currentEnergy = 1000f;
            SetField(controller, "actionTick", controller.riseMinimumReleaseTicks - 1);
            SetField(controller, "riseKeyHeld", false);
            Require(!(bool)endRise.Invoke(controller, null),
                "rise release is ignored before the original minimum callback window", ref assertions);
            SetField(controller, "actionTick", controller.riseMinimumReleaseTicks);
            Require((bool)endRise.Invoke(controller, null),
                "rise release is accepted after the original minimum callback window", ref assertions);

            controller.currentAnimationIndex = controller.airIdleAction;
            SetField(controller, "actionTick", controller.airRiseMinimumIdleTicks - 1);
            Require(!(bool)canAirRise.Invoke(controller, null),
                "air-stop cannot immediately restart rise while Z remains held", ref assertions);
            SetField(controller, "actionTick", controller.airRiseMinimumIdleTicks);
            Require((bool)canAirRise.Invoke(controller, null),
                "air-stop accepts re-rise only after the original c38 > 10 window", ref assertions);

            controller.maximumEnergy = 1000f;
            controller.currentEnergy = 1000f;
            controller.state.SetInt(100, 1000);
            SetField(controller, "riseKeyHeld", true);
            controller.state.SetInt(191, 1);
            controller.currentAnimationIndex = controller.airIdleAction;
            startBoost.Invoke(controller, null);
            Require(Mathf.Approximately(controller.currentEnergy, 800f),
                "airborne boost entry charges Generator/5 once before per-tick drain", ref assertions);

            controller.currentAnimationIndex = controller.riseAction;
            SetField(controller, "riseSequenceActive", true);
            SetField(controller, "riseKeyHeld", true);
            controller.state.SetInt(190, 4);
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            riseSteering.Invoke(controller, new object[] { rootObject.transform });
            Require(Mathf.Abs(Vector3.Angle(Vector3.forward, rootObject.transform.forward) - 14f) < 0.01f,
                "held rise steers by the original 14-degree maximum", ref assertions);

            controller.currentAnimationIndex = controller.boostAction;
            captureDrivenVelocity.Invoke(controller, new object[] { Vector3.forward * 0.25f });
            controller.currentAnimationIndex = controller.riseAction;
            SetField(controller, "riseSequenceActive", true);
            SetField(controller, "gvEnable", false);
            controller.SetAirborneFlag(true);
            SetField(controller, "velocity", Vector3.zero);
            SetField(controller, "forceCommand", new Vector3(0f, 0.04f, 0f));
            applyDrivenInertia.Invoke(controller, null);
            rootObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            applyRootMotion.Invoke(controller, null);
            Vector3 inertialRisePosition = rootObject.transform.position;
            float expectedRiseDistance = 0.04f * controller.aniUnitsToUnityScale;
            float expectedInertiaDistance = 0.25f * controller.airborneHorizontalForceRetention * controller.aniUnitsToUnityScale;
            Require(Mathf.Abs(inertialRisePosition.y - expectedRiseDistance) < 0.0001f &&
                    Mathf.Abs(inertialRisePosition.z - expectedInertiaDistance) < 0.0001f,
                "rise applies one ANI Force/tick integration and one Unity-unit conversion", ref assertions);

            SetField(controller, "velocity", Vector3.up * 7f);
            SetField(controller, "forceCommand", new Vector3(0f, 0.02f, 0f));
            integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y - 0.17f) < 0.0001f,
                "rise clamps to original 0.15 vertical speed before adding ANI Force", ref assertions);

            controller.currentAnimationIndex = controller.airMoveAction;
            SetField(controller, "riseSequenceActive", false);
            controller.SetAirborneFlag(true);
            controller.currentEnergy = 1000f;
            SetField(controller, "gvEnable", true);
            SetField(controller, "velocity", Vector3.up * 0.17f);
            SetField(controller, "forceCommand", new Vector3(0f, 0.02f, 0f));
            for (int i = 0; i < 120; i++)
                integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y + 0.8f) < 0.0001f &&
                    Mathf.Abs(controller.LastMotionStep.forcePerTick.y) < 0.0001f,
                "directional air move cannot apply its positive ANI Force as energy-free lift", ref assertions);

            controller.currentEnergy = 0f;
            SetField(controller, "velocity", Vector3.up * 0.17f);
            for (int i = 0; i < 120; i++)
                integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y + 0.8f) < 0.0001f &&
                    Mathf.Abs(controller.LastMotionStep.forcePerTick.y) < 0.0001f,
                "held directional air move descends even when the movement gauge is empty", ref assertions);

            SetField(controller, "gvEnable", false);
            SetField(controller, "velocity", Vector3.zero);
            SetField(controller, "forceCommand", new Vector3(0f, -0.02f, 0f));
            integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y + 0.02f) < 0.0001f,
                "directional air move preserves non-positive ANI Force", ref assertions);

            controller.currentAnimationIndex = controller.airIdleAction;
            controller.state.SetInt(190, 0);
            controller.currentEnergy = 1000f;
            controller.useColliderGrounding = true;
            SetField(controller, "gvEnable", true);
            SetField(controller, "forceCommand", Vector3.zero);
            SetField(controller, "moveCommand", Vector3.zero);
            controller.verticalFallSpeed = 0f;
            SetField(controller, "actionTick", 1);
            rootObject.transform.SetPositionAndRotation(Vector3.up * 10f, Quaternion.identity);
            float releasedRisePeak = rootObject.transform.position.y;
            for (int i = 0; i < 50; i++)
            {
                applyRootMotion.Invoke(controller, null);
                releasedRisePeak = Mathf.Max(releasedRisePeak, rootObject.transform.position.y);
                SetField(controller, "actionTick", (int)GetField(controller, "actionTick") + 1);
            }
            Require(rootObject.transform.position.y < releasedRisePeak - 0.01f,
                "released directional air move descends after the original 31-tick air-stop brake window", ref assertions);

            controller.useColliderGrounding = false;
            controller.currentAnimationIndex = controller.airIdleAction;
            controller.SetAirborneFlag(true);
            SetField(controller, "velocity", Vector3.zero);
            SetField(controller, "forceCommand", Vector3.zero);
            SetField(controller, "gvEnable", true);
            integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y + 0.013f) < 0.0001f,
                "GvEnable applies the original 0.013 downward velocity per tick", ref assertions);
            for (int i = 0; i < 100; i++)
                integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).y + 0.8f) < 0.0001f,
                "original gravity stops at the -0.8 terminal tick velocity", ref assertions);

            SetField(controller, "velocity", Vector3.right);
            SetField(controller, "gvEnable", false);
            SetField(controller, "vFMulti", 0.5f);
            integrateForce.Invoke(controller, null);
            Require(Mathf.Abs(((Vector3)GetField(controller, "velocity")).x - 0.475f) < 0.0001f,
                "airborne damping is followed by one vF_Multi application", ref assertions);
            SetField(controller, "vFMulti", 1f);

            controller.currentAnimationIndex = controller.riseAction;
            SetField(controller, "riseSequenceActive", true);
            controller.useColliderGrounding = false;
            controller.currentEnergy = 100f;
            controller.maximumEnergy = 100f;
            controller.state.SetFloat(100, 100f);
            energyTick.Invoke(controller, null);
            Require(Mathf.Approximately(controller.currentEnergy, 95f),
                "rise consumes five original energy units per tick", ref assertions);
            controller.currentAnimationIndex = controller.boostAction;
            SetField(controller, "boostMotionActive", true);
            energyTick.Invoke(controller, null);
            Require(Mathf.Approximately(controller.currentEnergy, 90f),
                "boost consumes five original energy units per tick", ref assertions);
            controller.currentAnimationIndex = controller.forwardStepAction;
            SetField(controller, "boostMotionActive", false);
            SetField(controller, "stepSequenceActive", true);
            energyTick.Invoke(controller, null);
            Require(Mathf.Approximately(controller.currentEnergy, 86f),
                "grounded step consumes four original energy units instead of recovering energy", ref assertions);
            SetField(controller, "stepSequenceActive", false);
            controller.currentEnergy = 0f;
            controller.state.SetFloat(100, 0f);
            Require((bool)endBoost.Invoke(controller, null), "empty energy ends boost", ref assertions);

            controller.currentAnimationIndex = controller.idleAction;
            controller.currentEnergy = 50f;
            controller.state.SetFloat(100, 50f);
            SetField(controller, "riseSequenceActive", false);
            SetField(controller, "boostMotionActive", false);
            controller.SetAirborneFlag(false);
            energyTick.Invoke(controller, null);
            Require(Mathf.Approximately(controller.currentEnergy, 74f),
                "grounded recovery uses the original initialized value 24 per tick", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
            UnityEngine.Object.DestroyImmediate(controllerObject);
        }
    }

    static Vector3 RunRootMotionDirection(
        TestPlayController controller,
        Transform root,
        MethodInfo applyRootMotion,
        MethodInfo resetHeading,
        int direction)
    {
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        resetHeading.Invoke(controller, null);
        controller.state.SetInt(190, direction);
        applyRootMotion.Invoke(controller, null);
        return root.position;
    }

    static Vector3 RunStepRootMotionDirection(
        TestPlayController controller,
        Transform root,
        MethodInfo applyRootMotion,
        MethodInfo prepareStep,
        int direction)
    {
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        controller.state.SetInt(190, direction);
        int action = (int)prepareStep.Invoke(controller, new object[] { direction });
        controller.currentAnimationIndex = action;
        SetField(controller, "stepSequenceActive", true);
        SetField(controller, "actionTick", 1);
        SetField(controller, "moveCommand", new Vector3(0f, 0f, 0.51f));
        applyRootMotion.Invoke(controller, null);
        SetField(controller, "stepSequenceActive", false);
        return root.position;
    }

    static List<TestPlayScriptValue> Values(params float[] values)
    {
        List<TestPlayScriptValue> result = new List<TestPlayScriptValue>();
        for (int i = 0; i < values.Length; i++)
            result.Add(TestPlayScriptValue.Number(values[i]));
        return result;
    }

    static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, InstancePrivate);
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, name);
        field.SetValue(target, value);
    }

    static object GetField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, InstancePrivate);
        if (field == null)
            throw new MissingFieldException(target.GetType().Name, name);
        return field.GetValue(target);
    }

    static void Require(bool condition, string label, ref int assertions)
    {
        if (!condition)
            throw new InvalidOperationException("[TestPlayVerification] Failed: " + label);
        assertions++;
    }
}
