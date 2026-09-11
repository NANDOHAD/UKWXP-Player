public enum TestPlayLocomotionState
{
    Other,
    Idle,
    Walk,
    JumpStart,
    Rise,
    AirMove,
    AirIdle,
    Landing,
    StepLanding,
    Step,
    Boost,
    Guard
}

public struct TestPlayLocomotionActions
{
    public int idle;
    public int move;
    public int jumpStart;
    public int rise;
    public int airMove;
    public int airIdle;
    public int landing;
    public int stepLanding;
    public int forwardStep;
    public int backStep;
    public int leftStep;
    public int rightStep;
    public int boost;
    public int guard;
}

/// <summary>
/// Side-effect-free locomotion state classification and transition guards.
/// Input sampling and scene mutation remain in TestPlayController's facade.
/// </summary>
public static class TestPlayLocomotionCore
{
    public static TestPlayLocomotionState Classify(int actionId, TestPlayLocomotionActions actions)
    {
        actionId = TestPlayActionCore.GetLogicalActionId(actionId);
        if (actionId == actions.idle) return TestPlayLocomotionState.Idle;
        if (actionId == actions.move) return TestPlayLocomotionState.Walk;
        if (actionId == actions.jumpStart) return TestPlayLocomotionState.JumpStart;
        if (actionId == actions.rise) return TestPlayLocomotionState.Rise;
        if (actionId == actions.airMove) return TestPlayLocomotionState.AirMove;
        if (actionId == actions.airIdle) return TestPlayLocomotionState.AirIdle;
        if (actionId == actions.landing) return TestPlayLocomotionState.Landing;
        if (actionId == actions.stepLanding) return TestPlayLocomotionState.StepLanding;
        if (IsStep(actionId, actions)) return TestPlayLocomotionState.Step;
        if (actionId == actions.boost) return TestPlayLocomotionState.Boost;
        if (actionId == actions.guard) return TestPlayLocomotionState.Guard;
        return TestPlayLocomotionState.Other;
    }

    public static int ResolveAirborneAction(bool hasDirectionInput, TestPlayLocomotionActions actions)
    {
        return hasDirectionInput ? actions.airMove : actions.airIdle;
    }

    public static int ResolveStepExit(bool airborne, bool grounded, TestPlayLocomotionActions actions)
    {
        return airborne && !grounded ? actions.airIdle : actions.stepLanding;
    }

    public static bool ShouldEndRise(
        bool riseSequenceActive,
        int currentActionId,
        bool riseHeld,
        bool hasEnergy,
        int actionTick,
        int minimumReleaseTicks,
        TestPlayLocomotionActions actions)
    {
        if (!riseSequenceActive || Classify(currentActionId, actions) != TestPlayLocomotionState.Rise)
            return false;
        if (actionTick < ClampMinimumTick(minimumReleaseTicks))
            return false;
        return !riseHeld || !hasEnergy;
    }

    public static bool ShouldEndBoost(
        bool boostMotionActive,
        bool hasEnergy,
        bool riseHeld,
        bool hasDirectionInput,
        int actionTick,
        int minimumReleaseTicks)
    {
        if (!boostMotionActive || !hasEnergy)
            return true;
        if (actionTick < ClampMinimumTick(minimumReleaseTicks))
            return false;
        return !riseHeld && !hasDirectionInput;
    }

    public static bool CanStartAirRise(int actionTick, int minimumIdleTicks)
    {
        return actionTick >= ClampMinimumTick(minimumIdleTicks);
    }

    public static bool IsStep(int actionId, TestPlayLocomotionActions actions)
    {
        actionId = TestPlayActionCore.GetLogicalActionId(actionId);
        return actionId == actions.forwardStep ||
               actionId == actions.backStep ||
               actionId == actions.leftStep ||
               actionId == actions.rightStep;
    }

    static int ClampMinimumTick(int value)
    {
        return value < 1 ? 1 : value;
    }
}
