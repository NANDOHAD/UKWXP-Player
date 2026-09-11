using System;
using System.Collections.Generic;
using UnityEngine;

public static class TestPlayPhase2Verification
{
    public static int RunAll()
    {
        int assertions = 0;
        VerifyActionSelection(ref assertions);
        VerifyMotionIntegration(ref assertions);
        VerifyLocomotionStateMachine(ref assertions);
        VerifyTrace(ref assertions);
        return assertions;
    }

    static void VerifyActionSelection(ref int assertions)
    {
        HashSet<int> usable = new HashSet<int> { 0, 1, 2, 50, 51, 130 };
        HashSet<int> scripted = new HashSet<int> { 0, 1, 51, 130 };
        Func<int, bool> hasUsable = usable.Contains;
        Func<int, bool> hasScript = scripted.Contains;

        TestPlayActionSelection gun = TestPlayActionCore.Resolve(
            0, TestPlayWeaponMode.Gun, hasUsable, hasScript);
        Require(gun.requestedActionId == 0 && gun.logicalActionId == 0 &&
                gun.poseActionId == 0 && gun.scriptActionId == 0,
            "gun action keeps one logical/pose/script identity", ref assertions);
        Require(gun.UsesDualChannels && gun.primaryChannel == 0 && gun.secondaryChannel == 1,
            "basic gun action schedules main then secondary ANI channels", ref assertions);

        TestPlayActionSelection swordFallback = TestPlayActionCore.Resolve(
            0, TestPlayWeaponMode.Sword, hasUsable, hasScript);
        Require(swordFallback.logicalActionId == 0 && swordFallback.poseActionId == 50,
            "sword locomotion selects the +50 pose", ref assertions);
        Require(swordFallback.scriptActionId == 0,
            "scriptless +50 pose falls back to the base ANI script", ref assertions);
        Require(swordFallback.UsesDualChannels && swordFallback.secondaryChannel == 1,
            "sword locomotion retains both original ANI channels", ref assertions);

        TestPlayActionSelection swordScript = TestPlayActionCore.Resolve(
            1, TestPlayWeaponMode.Sword, hasUsable, hasScript);
        Require(swordScript.poseActionId == 51 && swordScript.scriptActionId == 51,
            "+50 ANI script remains authoritative when present", ref assertions);

        usable.Remove(52);
        TestPlayActionSelection missingSwordPose = TestPlayActionCore.Resolve(
            2, TestPlayWeaponMode.Sword, hasUsable, hasScript);
        Require(missingSwordPose.poseActionId == 2 && missingSwordPose.logicalActionId == 2,
            "missing +50 entry safely uses the base pose", ref assertions);

        TestPlayActionSelection melee = TestPlayActionCore.Resolve(
            130, TestPlayWeaponMode.Sword, hasUsable, hasScript);
        Require(melee.poseActionId == 130 && melee.logicalActionId == 130,
            "melee action IDs are never offset by weapon-mode pose selection", ref assertions);
        Require(!melee.UsesDualChannels && melee.primaryChannel == 0 && melee.secondaryChannel == -1,
            "melee action uses the original main ANI channel only", ref assertions);
        Require(TestPlayActionCore.GetLogicalActionId(68) == 18 &&
                TestPlayActionCore.GetLogicalActionId(100) == 100,
            "logical action normalization is limited to the +50 basic range", ref assertions);
    }

    static void VerifyMotionIntegration(ref int assertions)
    {
        TestPlayMotionInput input = new TestPlayMotionInput
        {
            velocity = new Vector3(1f, 0.3f, -2f),
            forcePerTick = new Vector3(0.5f, 0.1f, 1f),
            limitUpwardVelocity = true,
            upwardVelocityLimit = 0.15f,
            gravityEnabled = true,
            gravityPerTick = 0.013f,
            terminalFallSpeed = 0.8f,
            airborne = true,
            airborneHorizontalRetention = 0.95f,
            groundedHorizontalRetention = 0.9f,
            velocityMultiplier = 2f,
            aniUnitsToUnityScale = 0.7f
        };

        TestPlayMotionStep step = TestPlayMotionCore.Integrate(input);
        Require(step.riseClampApplied && Approximately(step.velocityAfterRiseClamp, new Vector3(1f, 0.15f, -2f)),
            "rise speed is clamped before Force", ref assertions);
        Require(Approximately(step.velocityAfterForce, new Vector3(1.5f, 0.25f, -1f)),
            "Force is applied as one per-tick velocity delta", ref assertions);
        Require(step.gravityApplied && Approximately(step.velocityAfterGravity, new Vector3(1.5f, 0.237f, -1f)),
            "gravity follows Force in the original order", ref assertions);
        Require(Approximately(step.velocityAfterDamping, new Vector3(1.425f, 0.237f, -0.95f)),
            "air damping affects horizontal axes only", ref assertions);
        Require(Approximately(step.velocityAfterMultiplier, new Vector3(2.85f, 0.474f, -1.9f)),
            "vF_Multi is applied once after damping", ref assertions);

        step = TestPlayMotionCore.ComposeDisplacement(step, new Vector3(0.2f, 0f, 0.4f), 0.7f);
        Require(Approximately(step.requestedDisplacement, new Vector3(2.135f, 0.3318f, -1.05f)),
            "ANI Move and Force velocity compose before unit scaling", ref assertions);

        TestPlayMotionStep retained = TestPlayMotionCore.ComposeDisplacement(
            step, new Vector3(0.2f, 0f, 0.4f), 0.8f, 0.7f);
        Require(Approximately(retained.scriptedVelocityBeforeRetention, new Vector3(0.2f, 0f, 0.4f)) &&
                Mathf.Approximately(retained.scriptedMoveRetention, 0.8f) &&
                Approximately(retained.scriptedVelocityAfterRetention, new Vector3(0.16f, 0f, 0.32f)),
            "ANI Move keeps entry, action retention, and post-retention stages separately", ref assertions);
        Require(Approximately(retained.requestedDisplacement, new Vector3(2.107f, 0.3318f, -1.106f)),
            "post-retention Move is used for requested displacement", ref assertions);

        TestPlayMotionStep terminal = TestPlayMotionCore.Integrate(new TestPlayMotionInput
        {
            velocity = new Vector3(1f, -0.9f, 1f),
            gravityEnabled = true,
            gravityPerTick = 0.013f,
            terminalFallSpeed = 0.8f,
            airborne = false,
            airborneHorizontalRetention = 0.95f,
            groundedHorizontalRetention = 0.9f,
            velocityMultiplier = 1f
        });
        Require(!terminal.gravityApplied && Mathf.Approximately(terminal.velocityAfterMultiplier.y, -0.9f),
            "gravity does not modify velocity already below the original terminal threshold", ref assertions);
        Require(Mathf.Approximately(terminal.velocityAfterMultiplier.x, 0.9f) &&
                Mathf.Approximately(terminal.velocityAfterMultiplier.z, 0.9f),
            "grounded horizontal retention is 0.9", ref assertions);

        Vector3 releasedVelocity = Vector3.zero;
        for (int i = 0; i < 3; i++)
        {
            TestPlayMotionStep released = TestPlayMotionCore.Integrate(new TestPlayMotionInput
            {
                velocity = releasedVelocity,
                gravityEnabled = true,
                gravityPerTick = 0.013f,
                terminalFallSpeed = 0.8f,
                airborne = true,
                airborneHorizontalRetention = 0.95f,
                groundedHorizontalRetention = 0.9f,
                velocityMultiplier = 1f
            });
            releasedVelocity = released.velocityAfterMultiplier;
        }
        Require(Mathf.Approximately(releasedVelocity.y, -0.039f),
            "released rise deterministically transitions to gravity over successive ticks", ref assertions);
    }

    static void VerifyLocomotionStateMachine(ref int assertions)
    {
        TestPlayLocomotionActions actions = CreateActions();
        Require(TestPlayLocomotionCore.Classify(0, actions) == TestPlayLocomotionState.Idle,
            "idle state classification", ref assertions);
        Require(TestPlayLocomotionCore.Classify(51, actions) == TestPlayLocomotionState.Walk,
            "+50 walk pose classifies by logical action", ref assertions);
        Require(TestPlayLocomotionCore.Classify(3, actions) == TestPlayLocomotionState.JumpStart,
            "jump-start state classification", ref assertions);
        Require(TestPlayLocomotionCore.Classify(7, actions) == TestPlayLocomotionState.Rise,
            "rise state classification", ref assertions);
        Require(TestPlayLocomotionCore.Classify(4, actions) == TestPlayLocomotionState.AirMove &&
                TestPlayLocomotionCore.Classify(8, actions) == TestPlayLocomotionState.AirIdle,
            "air-move and air-idle remain distinct", ref assertions);
        Require(TestPlayLocomotionCore.Classify(5, actions) == TestPlayLocomotionState.Landing &&
                TestPlayLocomotionCore.Classify(6, actions) == TestPlayLocomotionState.StepLanding,
            "regular and step landing remain distinct", ref assertions);
        Require(TestPlayLocomotionCore.Classify(9, actions) == TestPlayLocomotionState.Step &&
                TestPlayLocomotionCore.Classify(12, actions) == TestPlayLocomotionState.Step,
            "all directional step actions share the step state", ref assertions);
        Require(TestPlayLocomotionCore.Classify(22, actions) == TestPlayLocomotionState.Boost &&
                TestPlayLocomotionCore.Classify(19, actions) == TestPlayLocomotionState.Guard,
            "boost and guard state classification", ref assertions);
        Require(TestPlayLocomotionCore.Classify(130, actions) == TestPlayLocomotionState.Other,
            "combat actions remain outside the locomotion machine", ref assertions);

        Require(TestPlayLocomotionCore.ResolveAirborneAction(false, actions) == 8 &&
                TestPlayLocomotionCore.ResolveAirborneAction(true, actions) == 4,
            "airborne direction selects air-idle or air-move deterministically", ref assertions);
        Require(TestPlayLocomotionCore.ResolveStepExit(true, false, actions) == 8 &&
                TestPlayLocomotionCore.ResolveStepExit(true, true, actions) == 6 &&
                TestPlayLocomotionCore.ResolveStepExit(false, false, actions) == 6,
            "step exit follows original airborne versus grounded branch", ref assertions);

        Require(!TestPlayLocomotionCore.ShouldEndRise(true, 7, false, true, 4, 5, actions),
            "rise release is gated before the minimum tick", ref assertions);
        Require(TestPlayLocomotionCore.ShouldEndRise(true, 7, false, true, 5, 5, actions),
            "rise release ends at the minimum tick", ref assertions);
        Require(!TestPlayLocomotionCore.ShouldEndRise(true, 7, true, true, 100, 5, actions),
            "held rise has no legacy twelve-tick cutoff", ref assertions);
        Require(TestPlayLocomotionCore.ShouldEndRise(true, 7, true, false, 5, 5, actions),
            "energy depletion ends rise", ref assertions);

        Require(!TestPlayLocomotionCore.ShouldEndBoost(true, true, false, false, 30, 31),
            "boost release is ignored through tick 30", ref assertions);
        Require(TestPlayLocomotionCore.ShouldEndBoost(true, true, false, false, 31, 31),
            "boost ends after the minimum release window", ref assertions);
        Require(!TestPlayLocomotionCore.ShouldEndBoost(true, true, false, true, 31, 31),
            "direction input sustains boost after jump release", ref assertions);
        Require(TestPlayLocomotionCore.ShouldEndBoost(true, false, true, true, 1, 31),
            "energy depletion ends boost immediately", ref assertions);
        Require(!TestPlayLocomotionCore.CanStartAirRise(10, 11) &&
                TestPlayLocomotionCore.CanStartAirRise(11, 11),
            "air re-rise opens only at the c38 greater-than-ten boundary", ref assertions);
    }

    static void VerifyTrace(ref int assertions)
    {
        TestPlayActionSelection action = new TestPlayActionSelection
        {
            requestedActionId = 1,
            logicalActionId = 1,
            poseActionId = 51,
            scriptActionId = 1,
            weaponMode = TestPlayWeaponMode.Sword,
            primaryChannel = 0,
            secondaryChannel = 1
        };
        TestPlayMotionStep motion = new TestPlayMotionStep
        {
            velocityBefore = new Vector3(0f, 0.1f, 0f),
            forcePerTick = new Vector3(0f, 0.02f, 0f),
            velocityAfterMultiplier = new Vector3(0f, 0.107f, 0f),
            requestedDisplacement = new Vector3(0f, 0.0749f, 0.2f),
            gravityApplied = true,
            horizontalRetention = 0.95f,
            velocityMultiplier = 1f
        };

        string first = TestPlayPhase2TickTrace.Serialize(17, action, TestPlayLocomotionState.Walk, motion);
        string second = TestPlayPhase2TickTrace.Serialize(17, action, TestPlayLocomotionState.Walk, motion);
        Require(first == second, "Phase 2 tick trace serialization is deterministic", ref assertions);
        Require(first.Contains("\"logicalAction\":1") &&
                first.Contains("\"poseAction\":51") &&
                first.Contains("\"scriptAction\":1"),
            "trace keeps logical, pose, and script action identities", ref assertions);
        Require(first.Contains("\"locomotion\":\"Walk\"") &&
                first.Contains("\"gravityApplied\":true") &&
                first.Contains("\"retention\":0.95"),
            "trace records locomotion and motion-core decisions", ref assertions);
    }

    static TestPlayLocomotionActions CreateActions()
    {
        return new TestPlayLocomotionActions
        {
            idle = 0,
            move = 1,
            jumpStart = 3,
            rise = 7,
            airMove = 4,
            airIdle = 8,
            landing = 5,
            stepLanding = 6,
            forwardStep = 11,
            backStep = 12,
            leftStep = 9,
            rightStep = 10,
            boost = 22,
            guard = 19
        };
    }

    static bool Approximately(Vector3 actual, Vector3 expected)
    {
        return (actual - expected).sqrMagnitude < 0.00000001f;
    }

    static void Require(bool condition, string message, ref int assertions)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("[TestPlayPhase2Verification] " + message);
    }
}
