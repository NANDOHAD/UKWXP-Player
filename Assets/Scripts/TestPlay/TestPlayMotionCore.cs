using UnityEngine;

public struct TestPlayMotionInput
{
    public Vector3 velocity;
    public Vector3 forcePerTick;
    public bool limitUpwardVelocity;
    public float upwardVelocityLimit;
    public bool gravityEnabled;
    public float gravityPerTick;
    public float terminalFallSpeed;
    public bool airborne;
    public float airborneHorizontalRetention;
    public float groundedHorizontalRetention;
    public float velocityMultiplier;
    public float aniUnitsToUnityScale;
}

public struct TestPlayMotionStep
{
    public Vector3 velocityBefore;
    public Vector3 velocityAfterRiseClamp;
    public Vector3 forcePerTick;
    public Vector3 velocityAfterForce;
    public Vector3 velocityAfterGravity;
    public Vector3 velocityAfterDamping;
    public Vector3 velocityAfterMultiplier;
    public Vector3 scriptedVelocity;
    public Vector3 scriptedVelocityBeforeRetention;
    public float scriptedMoveRetention;
    public Vector3 scriptedVelocityAfterRetention;
    public Vector3 requestedDisplacement;
    public bool riseClampApplied;
    public bool gravityApplied;
    public float horizontalRetention;
    public float velocityMultiplier;
    public float unitScale;
}

/// <summary>
/// Deterministic 60 Hz Force/GvEnable integration extracted from FUN_004cd840.
/// Force values are per-tick velocity deltas, not per-second accelerations.
/// </summary>
public static class TestPlayMotionCore
{
    public static TestPlayMotionStep Integrate(TestPlayMotionInput input)
    {
        TestPlayMotionStep result = new TestPlayMotionStep
        {
            velocityBefore = input.velocity,
            forcePerTick = input.forcePerTick,
            velocityMultiplier = input.velocityMultiplier,
            unitScale = Mathf.Max(0f, input.aniUnitsToUnityScale)
        };

        Vector3 velocity = input.velocity;
        float upwardLimit = Mathf.Max(0f, input.upwardVelocityLimit);
        if (input.limitUpwardVelocity && velocity.y > upwardLimit)
        {
            velocity.y = upwardLimit;
            result.riseClampApplied = true;
        }
        result.velocityAfterRiseClamp = velocity;

        velocity += input.forcePerTick;
        result.velocityAfterForce = velocity;

        if (input.gravityEnabled)
        {
            float terminal = Mathf.Max(0f, input.terminalFallSpeed);
            if (velocity.y > -terminal)
            {
                velocity.y = Mathf.Max(velocity.y - Mathf.Max(0f, input.gravityPerTick), -terminal);
                result.gravityApplied = true;
            }
        }
        result.velocityAfterGravity = velocity;

        float retention = input.airborne
            ? Mathf.Clamp01(input.airborneHorizontalRetention)
            : Mathf.Clamp01(input.groundedHorizontalRetention);
        velocity.x *= retention;
        velocity.z *= retention;
        result.horizontalRetention = retention;
        result.velocityAfterDamping = velocity;

        velocity *= input.velocityMultiplier;
        result.velocityAfterMultiplier = velocity;
        result.requestedDisplacement = velocity * result.unitScale;
        return result;
    }

    public static TestPlayMotionStep ComposeDisplacement(
        TestPlayMotionStep step,
        Vector3 scriptedVelocity,
        float aniUnitsToUnityScale)
    {
        return ComposeDisplacement(step, scriptedVelocity, 1f, aniUnitsToUnityScale);
    }

    public static TestPlayMotionStep ComposeDisplacement(
        TestPlayMotionStep step,
        Vector3 scriptedVelocity,
        float scriptedMoveRetention,
        float aniUnitsToUnityScale)
    {
        step.scriptedVelocity = scriptedVelocity;
        step.scriptedVelocityBeforeRetention = scriptedVelocity;
        step.scriptedMoveRetention = Mathf.Clamp01(scriptedMoveRetention);
        step.scriptedVelocityAfterRetention = scriptedVelocity * step.scriptedMoveRetention;
        step.unitScale = Mathf.Max(0f, aniUnitsToUnityScale);
        step.requestedDisplacement =
            (step.scriptedVelocityAfterRetention + step.velocityAfterMultiplier) * step.unitScale;
        return step;
    }
}
