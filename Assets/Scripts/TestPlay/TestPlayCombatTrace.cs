using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public enum TestPlayCombatTraceEventType
{
    ProfileChanged,
    CooldownSet,
    AttackStarted,
    ComboQueued,
    ActionTransition,
    ProjectileSpawned,
    Hit,
    AttackFinished
}

public struct TestPlayCombatTraceEvent
{
    public TestPlayCombatTraceEventType type;
    public int tick;
    public int actionId;
    public int targetActionId;
    public int slot;
    public int cooldownTicks;
    public string source;
    public float damage;
    public int down;
    public float force;
    public float forceY;
    public int attackFlag;
    public TestPlayCombatHitDecision hitDecision;
    public int reactionState;
    public int guardHitTimerTicks;
    public int hitStopTicks;
    public TestPlayCombatValueSource valueSource;
    public TestPlayCombatDecisionReason reason;
}

public struct TestPlayCombatSnapshot
{
    public int power;
    public int down;
    public float force;
    public float forceY;
    public int attackFlag;
    public int swordCancelActionId;
    public int[] cooldownTicks;
    public bool sequenceActive;
    public bool meleeApproachActive;
    public bool comboInputPending;
    public IReadOnlyList<TestPlayCombatTraceEvent> events;
}

public static class TestPlayPhase3TickTrace
{
    public static string Serialize(
        string phase2Json,
        TestPlayCombatSnapshot combat)
    {
        string prefix = string.IsNullOrEmpty(phase2Json) ? "{}" : phase2Json;
        if (prefix[prefix.Length - 1] == '}')
            prefix = prefix.Substring(0, prefix.Length - 1);

        StringBuilder builder = new StringBuilder(prefix, prefix.Length + 512);
        builder.Append(",\"combat\":{");
        builder.Append("\"profile\":{")
            .Append("\"power\":").Append(combat.power).Append(',')
            .Append("\"down\":").Append(combat.down).Append(',')
            .Append("\"force\":").Append(Format(combat.force)).Append(',')
            .Append("\"forceY\":").Append(Format(combat.forceY)).Append(',')
            .Append("\"attackFlag\":").Append(combat.attackFlag).Append(',')
            .Append("\"swordCancel\":").Append(combat.swordCancelActionId).Append("},");
        builder.Append("\"cooldowns\":[");
        int cooldownCount = combat.cooldownTicks != null ? combat.cooldownTicks.Length : 0;
        for (int i = 0; i < cooldownCount; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(combat.cooldownTicks[i]);
        }
        builder.Append("],\"sequenceActive\":").Append(combat.sequenceActive ? "true" : "false")
            .Append(",\"meleeApproachActive\":").Append(combat.meleeApproachActive ? "true" : "false")
            .Append(",\"comboPending\":").Append(combat.comboInputPending ? "true" : "false")
            .Append(",\"events\":[");

        int eventCount = combat.events != null ? combat.events.Count : 0;
        for (int i = 0; i < eventCount; i++)
        {
            if (i > 0) builder.Append(',');
            AppendEvent(builder, combat.events[i]);
        }
        builder.Append("]}}");
        return builder.ToString();
    }

    static void AppendEvent(StringBuilder builder, TestPlayCombatTraceEvent value)
    {
        builder.Append('{')
            .Append("\"type\":\"").Append(value.type).Append("\",")
            .Append("\"tick\":").Append(value.tick).Append(',')
            .Append("\"action\":").Append(value.actionId).Append(',')
            .Append("\"targetAction\":").Append(value.targetActionId).Append(',')
            .Append("\"slot\":").Append(value.slot).Append(',')
            .Append("\"cooldown\":").Append(value.cooldownTicks).Append(',')
            .Append("\"source\":\"").Append(Escape(value.source)).Append("\",")
            .Append("\"damage\":").Append(Format(value.damage)).Append(',')
            .Append("\"down\":").Append(value.down).Append(',')
            .Append("\"force\":").Append(Format(value.force)).Append(',')
            .Append("\"forceY\":").Append(Format(value.forceY)).Append(',');
        if (value.type == TestPlayCombatTraceEventType.Hit)
        {
            builder.Append("\"attackFlag\":").Append(value.attackFlag).Append(',')
                .Append("\"decision\":\"").Append(value.hitDecision).Append("\",")
                .Append("\"reaction\":").Append(value.reactionState).Append(',')
                .Append("\"guardTimer\":").Append(value.guardHitTimerTicks).Append(',')
                .Append("\"hitStop\":").Append(value.hitStopTicks).Append(',');
        }
        builder.Append("\"valueSource\":\"").Append(value.valueSource).Append("\",")
            .Append("\"reason\":\"").Append(value.reason).Append("\"}");
    }

    static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static string Format(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
