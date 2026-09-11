using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class TestPlayPhase3Verification
{
    const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int RunAll()
    {
        int assertions = 0;
        VerifyAttackProfileAndCooldowns(ref assertions);
        VerifyAttackInputSelection(ref assertions);
        VerifyCombatSequence(ref assertions);
        VerifyPayloadAndHitResolution(ref assertions);
        VerifyProjectileTick(ref assertions);
        VerifyCombatTrace(ref assertions);
        VerifyControllerAdapter(ref assertions);
        return assertions;
    }

    static void VerifyAttackProfileAndCooldowns(ref int assertions)
    {
        TestPlayAttackProfile profile = new TestPlayAttackProfile();
        TestPlayCombatCore.ConfigureAttackProfile(profile, 80, 150, 0.4f, 2f);
        Require(profile.power == 80 && profile.down == 150,
            "ATTACK integer profile values", ref assertions);
        Require(Mathf.Approximately(profile.force, 0.4f) && Mathf.Approximately(profile.forceY, 2f),
            "ATTACK horizontal and vertical force values", ref assertions);

        int[] cooldowns = new int[TestPlayCombatCore.AttackCooldownSlotCount];
        Require(TestPlayCombatCore.TrySetCooldown(cooldowns, 0, 3) && cooldowns[0] == 3,
            "cooldown slot zero stores X delay", ref assertions);
        Require(TestPlayCombatCore.TrySetCooldown(cooldowns, 4, -5) && cooldowns[4] == 0,
            "cooldown values clamp at zero", ref assertions);
        Require(!TestPlayCombatCore.TrySetCooldown(cooldowns, -1, 3) &&
                !TestPlayCombatCore.TrySetCooldown(cooldowns, 5, 3),
            "only the original five cooldown slots are accepted", ref assertions);
        TestPlayCombatCore.TickCooldowns(cooldowns);
        Require(cooldowns[0] == 2 && TestPlayCombatCore.GetCooldown(cooldowns, 0) == 2,
            "cooldowns decrement once per combat tick", ref assertions);
        Require(TestPlayCombatCore.GetCooldown(cooldowns, 5) == 0,
            "out-of-range cooldown reads are safe", ref assertions);
    }

    static void VerifyAttackInputSelection(ref int assertions)
    {
        Require(TestPlayCombatCore.ResolveShotInputAction(
                true, 5, true, false, false, 100, 106, 68) == -1,
            "boost shot is closed through tick five", ref assertions);
        Require(TestPlayCombatCore.ResolveShotInputAction(
                true, 6, true, false, false, 100, 106, 68) == 106,
            "boost shot opens after tick five", ref assertions);
        Require(TestPlayCombatCore.ResolveShotInputAction(
                false, 0, false, true, true, 100, 106, 68) == 68,
            "sword form switches to gun before shooting", ref assertions);
        Require(TestPlayCombatCore.ResolveShotInputAction(
                false, 0, false, true, false, 100, 106, 68) == 100,
            "missing switch action falls back to direct shot", ref assertions);
        Require(TestPlayCombatCore.ResolveShotInputAction(
                false, 0, false, false, false, 100, 106, 68) == 100,
            "gun form selects normal shot", ref assertions);
        Require(TestPlayCombatCore.CanAcceptNormalAttackInput(
                false, 0, 100, false, false, 22, 106, 0, 1, 7, 4, 8, false),
            "idle accepts a normal attack", ref assertions);
        Require(!TestPlayCombatCore.CanAcceptNormalAttackInput(
                false, 22, 106, false, false, 22, 106, 0, 1, 7, 4, 8, false),
            "inactive boost rejects boost shot", ref assertions);
        Require(TestPlayCombatCore.CanAcceptNormalAttackInput(
                false, 22, 106, true, true, 22, 106, 0, 1, 7, 4, 8, false),
            "active boost accepts its dedicated shot", ref assertions);

        Require(ResolveMelee(false, true, 0) == 18,
            "gun form switches to sword before melee", ref assertions);
        Require(ResolveMelee(true, true, 8) == 130,
            "forward directions select approach melee", ref assertions);
        Require(ResolveMelee(true, true, 4) == 141 &&
                ResolveMelee(true, true, 6) == 146 &&
                ResolveMelee(true, true, 2) == 151,
            "left right and back select direction-specific melee", ref assertions);
        Require(ResolveMelee(true, true, 0) == 131,
            "neutral input selects neutral melee", ref assertions);
        Require(ResolveMelee(true, false, 4) == 131,
            "missing direction action falls back to usable neutral melee", ref assertions);
    }

    static int ResolveMelee(bool sword, bool directionalUsable, int direction)
    {
        return TestPlayCombatCore.ResolveMeleeInputAction(
            sword, true, direction, 18,
            130, directionalUsable,
            131, true,
            141, directionalUsable,
            146, directionalUsable,
            151, directionalUsable);
    }

    static void VerifyCombatSequence(ref int assertions)
    {
        TestPlayCombatSequenceInput input = BaseSequenceInput();
        input.meleePressed = true;
        TestPlayCombatSequenceDecision queued = TestPlayCombatCore.EvaluateSequence(input);
        Require(queued.handled && queued.comboInputPending &&
                queued.reason == TestPlayCombatDecisionReason.ComboQueued,
            "new C edge queues melee combo input", ref assertions);

        input.comboInputPending = true;
        input.meleePressed = false;
        input.swordCancelActionId = 132;
        input.swordCancelActionUsable = true;
        TestPlayCombatSequenceDecision swordCancel = TestPlayCombatCore.EvaluateSequence(input);
        Require(swordCancel.transitionActionId == 132 && !swordCancel.comboInputPending,
            "pending combo enters usable SwordCancel action", ref assertions);
        Require(swordCancel.clearMeleeApproach && swordCancel.clearSwordCancel &&
                swordCancel.reason == TestPlayCombatDecisionReason.SwordCancel,
            "SwordCancel clears transient combo state", ref assertions);

        input = BaseSequenceInput();
        input.currentActionId = 130;
        input.meleeApproachActionId = 130;
        input.meleeApproachActive = true;
        input.actionTick = 5;
        input.targetReached = true;
        input.meleeApproachMinimumTicks = 6;
        input.meleeApproachFollowupActionId = 136;
        input.meleeApproachFollowupUsable = true;
        Require(TestPlayCombatCore.EvaluateSequence(input).transitionActionId == -1,
            "approach followup is closed before tick six", ref assertions);
        input.actionTick = 6;
        TestPlayCombatSequenceDecision followup = TestPlayCombatCore.EvaluateSequence(input);
        Require(followup.transitionActionId == 136 &&
                followup.reason == TestPlayCombatDecisionReason.MeleeApproachFollowup,
            "approach followup opens at tick six when target is reached", ref assertions);
        input.targetReached = false;
        input.hasMovementEnergy = false;
        Require(TestPlayCombatCore.EvaluateSequence(input).transitionActionId == 136,
            "energy depletion also ends approach at the boundary", ref assertions);

        input = BaseSequenceInput();
        input.actionTick = 15;
        input.heldLocomotionActionId = 22;
        input.heldActionIsLocomotionCancel = true;
        Require(TestPlayCombatCore.EvaluateSequence(input).transitionActionId == -1,
            "locomotion cancel is closed through tick fifteen", ref assertions);
        input.actionTick = 16;
        TestPlayCombatSequenceDecision movementCancel = TestPlayCombatCore.EvaluateSequence(input);
        Require(movementCancel.transitionActionId == 22 && movementCancel.clearSequence &&
                movementCancel.reason == TestPlayCombatDecisionReason.LocomotionCancel,
            "boost or step cancels melee after tick fifteen", ref assertions);

        TestPlayCombatFinishDecision approachFinish = TestPlayCombatCore.ResolveFinish(
            true, 130, 130, 136, true, false, 8, 6);
        Require(approachFinish.transitionActionId == 136 && !approachFinish.clearSequence,
            "approach animation completion enters followup without ending sequence", ref assertions);
        TestPlayCombatFinishDecision groundFinish = TestPlayCombatCore.ResolveFinish(
            false, 100, 130, 136, true, false, 8, 6);
        Require(groundFinish.transitionActionId == 6 && groundFinish.clearSequence,
            "ground attack completion enters recovery six", ref assertions);
        TestPlayCombatFinishDecision airFinish = TestPlayCombatCore.ResolveFinish(
            false, 100, 130, 136, true, true, 8, 6);
        Require(airFinish.transitionActionId == 8 && airFinish.clearSequence,
            "air attack completion enters air idle eight", ref assertions);
    }

    static TestPlayCombatSequenceInput BaseSequenceInput()
    {
        return new TestPlayCombatSequenceInput
        {
            sequenceActive = true,
            currentActionIsMelee = true,
            currentActionId = 131,
            swordCancelActionId = -1,
            hasMovementEnergy = true,
            heldLocomotionActionId = -1
        };
    }

    static void VerifyPayloadAndHitResolution(ref int assertions)
    {
        TestPlayAttackProfile profile = new TestPlayAttackProfile
        {
            power = 37,
            down = 44,
            force = 2f,
            forceY = 3f
        };
        TestPlayProjectilePayload scripted = TestPlayCombatCore.CreateProjectilePayload(
            profile, 100f, 20f, true, "WeaponAttack:3");
        Require(Mathf.Approximately(scripted.damage, 37f) && scripted.down == 44,
            "script profile overrides Unity fallback damage", ref assertions);
        Require(Mathf.Approximately(scripted.horizontalImpactForce, 2f) &&
                Mathf.Approximately(scripted.verticalImpactForce, 3f),
            "projectile snapshots both impact forces", ref assertions);
        Require(scripted.homing && Mathf.Approximately(scripted.speed, 20f) &&
                scripted.valueSource == TestPlayCombatValueSource.OriginalScriptProfile,
            "projectile retains movement and confirmed-value source", ref assertions);

        profile.Reset();
        TestPlayProjectilePayload fallback = TestPlayCombatCore.CreateProjectilePayload(
            profile, 80f, 15f, false, "RunProc2:57");
        Require(Mathf.Approximately(fallback.damage, 80f) &&
                fallback.valueSource == TestPlayCombatValueSource.UnityFallback,
            "missing ATTACK power uses an explicitly marked Unity fallback", ref assertions);

        TestPlayCombatCore.ConfigureAttackProfile(profile, 37, 44, 2f, 3f);
        TestPlayCombatHitResult hit = TestPlayCombatCore.CreateHitResult(
            profile, 80f, new Vector3(0f, 5f, 10f), "RunProc2:57");
        Require(Mathf.Approximately(hit.damage, 37f) && hit.down == 44,
            "hit result uses active profile damage and down", ref assertions);
        Require(Approximately(hit.impactForce, new Vector3(0f, 3f, 2f)),
            "hit result normalizes horizontal direction and adds vertical force", ref assertions);
        Require(hit.source == "RunProc2:57" &&
                hit.valueSource == TestPlayCombatValueSource.OriginalScriptProfile,
            "hit result retains source and evidence category", ref assertions);
    }

    static void VerifyProjectileTick(ref int assertions)
    {
        TestPlayProjectileTickInput input = new TestPlayProjectileTickInput
        {
            position = Vector3.zero,
            rotation = Quaternion.identity,
            age = 0f,
            lifeSeconds = 3f,
            speed = 6f,
            tickDeltaTime = 1f / 60f,
            targetAlive = true,
            targetPosition = new Vector3(0f, 0f, 0.25f),
            combinedHitRadius = 0.2f
        };
        TestPlayProjectileTickResult first = TestPlayCombatCore.TickProjectile(input);
        Require(Mathf.Approximately(first.age, 1f / 60f) &&
                Approximately(first.position, new Vector3(0f, 0f, 0.1f)),
            "projectile advances exactly one 60 Hz tick", ref assertions);
        Require(first.hit && !first.expired,
            "projectile hit uses post-move position and combined radius", ref assertions);

        input.age = 2.99f;
        input.tickDeltaTime = 0.02f;
        TestPlayProjectileTickResult expired = TestPlayCombatCore.TickProjectile(input);
        Require(expired.expired && Approximately(expired.position, Vector3.zero),
            "expired projectile does not move or hit", ref assertions);

        input.age = 0f;
        input.tickDeltaTime = 1f / 60f;
        input.targetPosition = Vector3.right * 10f;
        input.combinedHitRadius = 0.01f;
        input.homingTurnRate = 180f;
        TestPlayProjectileTickResult homing = TestPlayCombatCore.TickProjectile(input);
        Require(Mathf.Abs(Quaternion.Angle(Quaternion.identity, homing.rotation) - 3f) < 0.001f,
            "homing rotation is limited by degrees per 60 Hz tick", ref assertions);
    }

    static void VerifyCombatTrace(ref int assertions)
    {
        string phase2 = "{\"tick\":12,\"logicalAction\":131}";
        TestPlayCombatTraceEvent[] events =
        {
            new TestPlayCombatTraceEvent
            {
                type = TestPlayCombatTraceEventType.Hit,
                tick = 12,
                actionId = 131,
                source = "RunProc2:57",
                damage = 37f,
                down = 44,
                force = 2f,
                forceY = 3f,
                valueSource = TestPlayCombatValueSource.OriginalScriptProfile
            }
        };
        TestPlayCombatSnapshot snapshot = new TestPlayCombatSnapshot
        {
            power = 37,
            down = 44,
            force = 2f,
            forceY = 3f,
            attackFlag = 1,
            swordCancelActionId = 132,
            cooldownTicks = new[] { 2, 0, 0, 0, 0 },
            sequenceActive = true,
            comboInputPending = true,
            events = events
        };
        string first = TestPlayPhase3TickTrace.Serialize(phase2, snapshot);
        string second = TestPlayPhase3TickTrace.Serialize(phase2, snapshot);
        Require(first == second, "Phase 3 trace serialization is deterministic", ref assertions);
        Require(first.Contains("\"tick\":12") && first.Contains("\"combat\":{"),
            "combat trace extends the Phase 2 tick record", ref assertions);
        Require(first.Contains("\"cooldowns\":[2,0,0,0,0]") &&
                first.Contains("\"swordCancel\":132"),
            "trace includes cooldown and combo-window state", ref assertions);
        Require(first.Contains("\"valueSource\":\"OriginalScriptProfile\"") &&
                first.Contains("\"source\":\"RunProc2:57\""),
            "trace preserves hit source and evidence category", ref assertions);
    }

    static void VerifyControllerAdapter(ref int assertions)
    {
        GameObject go = new GameObject("TestPlayPhase3Verification_Controller");
        try
        {
            TestPlayController controller = go.AddComponent<TestPlayController>();
            controller.logUnhandledCommands = false;
            MethodInfo beginTrace = typeof(TestPlayController).GetMethod("BeginCombatTraceTick", InstancePrivate);
            MethodInfo handle = typeof(TestPlayController).GetMethod("HandleCommand", InstancePrivate);
            Require(beginTrace != null && handle != null,
                "controller combat adapter helpers are available", ref assertions);

            beginTrace.Invoke(controller, null);
            handle.Invoke(controller, new object[]
            {
                "ATTACK",
                Values(37f, 44f, 2f, 3f),
                "ATTACK(37,44,2,3);"
            });
            handle.Invoke(controller, new object[]
            {
                "AttackDelay",
                Values(0f, 3f),
                "AttackDelay(0,3);"
            });
            string trace = controller.CapturePhase3TickTrace();
            Require(trace.Contains("\"power\":37") && trace.Contains("\"down\":44"),
                "controller exposes Core attack profile in Phase 3 trace", ref assertions);
            Require(trace.Contains("ProfileChanged") && trace.Contains("CooldownSet") &&
                    trace.Contains("\"cooldowns\":[3,0,0,0,0]"),
                "controller records profile and cooldown events in the active tick", ref assertions);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static List<TestPlayScriptValue> Values(params float[] values)
    {
        List<TestPlayScriptValue> result = new List<TestPlayScriptValue>();
        for (int i = 0; i < values.Length; i++)
            result.Add(TestPlayScriptValue.Number(values[i]));
        return result;
    }

    static bool Approximately(Vector3 actual, Vector3 expected)
    {
        return (actual - expected).sqrMagnitude < 0.00000001f;
    }

    static void Require(bool condition, string message, ref int assertions)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("[TestPlayPhase3Verification] " + message);
    }
}
