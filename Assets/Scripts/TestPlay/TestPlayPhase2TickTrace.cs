using System.Globalization;
using System.Text;
using UnityEngine;

public static class TestPlayPhase2TickTrace
{
    public static string Serialize(
        int tick,
        TestPlayActionSelection action,
        TestPlayLocomotionState locomotionState,
        TestPlayMotionStep motion)
    {
        StringBuilder builder = new StringBuilder(512);
        builder.Append('{');
        AppendInt(builder, "tick", tick);
        builder.Append(',');
        AppendInt(builder, "requestedAction", action.requestedActionId);
        builder.Append(',');
        AppendInt(builder, "logicalAction", action.logicalActionId);
        builder.Append(',');
        AppendInt(builder, "poseAction", action.poseActionId);
        builder.Append(',');
        AppendInt(builder, "scriptAction", action.scriptActionId);
        builder.Append(",\"weaponMode\":\"").Append(action.weaponMode).Append('"');
        builder.Append(',');
        AppendInt(builder, "primaryChannel", action.primaryChannel);
        builder.Append(',');
        AppendInt(builder, "secondaryChannel", action.secondaryChannel);
        builder.Append(",\"locomotion\":\"").Append(locomotionState).Append('"');
        builder.Append(",\"velocityBefore\":");
        AppendVector(builder, motion.velocityBefore);
        builder.Append(",\"force\":");
        AppendVector(builder, motion.forcePerTick);
        builder.Append(",\"velocityAfter\":");
        AppendVector(builder, motion.velocityAfterMultiplier);
        builder.Append(",\"scriptedVelocity\":");
        AppendVector(builder, motion.scriptedVelocity);
        builder.Append(",\"scriptedVelocityBeforeRetention\":");
        AppendVector(builder, motion.scriptedVelocityBeforeRetention);
        builder.Append(",\"moveRetention\":").Append(Format(motion.scriptedMoveRetention));
        builder.Append(",\"scriptedVelocityAfterRetention\":");
        AppendVector(builder, motion.scriptedVelocityAfterRetention);
        builder.Append(",\"requestedDisplacement\":");
        AppendVector(builder, motion.requestedDisplacement);
        builder.Append(",\"riseClampApplied\":").Append(motion.riseClampApplied ? "true" : "false");
        builder.Append(",\"gravityApplied\":").Append(motion.gravityApplied ? "true" : "false");
        builder.Append(",\"retention\":").Append(Format(motion.horizontalRetention));
        builder.Append(",\"velocityMultiplier\":").Append(Format(motion.velocityMultiplier));
        builder.Append('}');
        return builder.ToString();
    }

    static void AppendInt(StringBuilder builder, string name, int value)
    {
        builder.Append('"').Append(name).Append("\":").Append(value);
    }

    static void AppendVector(StringBuilder builder, Vector3 value)
    {
        builder.Append('[')
            .Append(Format(value.x)).Append(',')
            .Append(Format(value.y)).Append(',')
            .Append(Format(value.z)).Append(']');
    }

    static string Format(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
