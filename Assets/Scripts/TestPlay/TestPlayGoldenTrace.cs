using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public enum TestPlayGoldenBaselineKind
{
    OriginalConfirmedBoundary,
    RealAniObserved,
    UnityReference
}

public enum TestPlayGoldenSetupKind
{
    GroundedGun,
    AirborneGun,
    GroundedSword
}

public struct TestPlayGoldenInputFrame
{
    public int direction;
    public bool rise;
    public bool shot;
    public bool melee;
    public bool guard;
    public bool lockTarget;
    public bool special1;
    public bool special2;
    public bool special3;
}

public struct TestPlayGoldenInputSegment
{
    public int ticks;
    public TestPlayGoldenInputFrame input;
}

public sealed class TestPlayGoldenScenarioDefinition
{
    public string id;
    public string title;
    public TestPlayGoldenBaselineKind baseline;
    public TestPlayGoldenSetupKind setup;
    public Vector3 targetPosition;
    public int[] requiredActionIds;
    public string[] requiredCommands;
    public TestPlayGoldenInputSegment[] inputSegments;

    public TestPlayGoldenInputFrame[] ExpandInputFrames()
    {
        List<TestPlayGoldenInputFrame> result = new List<TestPlayGoldenInputFrame>();
        if (inputSegments == null)
            return result.ToArray();

        for (int i = 0; i < inputSegments.Length; i++)
        {
            TestPlayGoldenInputSegment segment = inputSegments[i];
            for (int tick = 0; tick < Mathf.Max(0, segment.ticks); tick++)
                result.Add(segment.input);
        }
        return result.ToArray();
    }
}

public static class TestPlayGoldenScenarioCatalog
{
    static readonly TestPlayGoldenInputFrame Idle = new TestPlayGoldenInputFrame();

    static readonly TestPlayGoldenScenarioDefinition[] Definitions =
    {
        Scenario("GT-001", "待機から前進して解放", TestPlayGoldenSetupKind.GroundedGun,
            new[] { 0, 1 }, new[] { "Move" },
            Segment(2, Idle), Segment(12, Input(direction: 8)), Segment(8, Idle)),
        Scenario("GT-002", "Z短押し", TestPlayGoldenSetupKind.GroundedGun,
            new[] { 3, 7, 8 }, new[] { "Force" },
            Segment(2, Idle), Segment(1, Input(rise: true)), Segment(90, Idle)),
        Scenario("GT-003", "Z長押し", TestPlayGoldenSetupKind.GroundedGun,
            new[] { 3, 7, 8 }, new[] { "Force" },
            // The representative real ANI spends about 185 ticks completing
            // action 7 (5 ticks at 0.1 frame, then 180 ticks at 0.01 frame).
            // Keep Z held beyond that boundary so the trace observes the
            // non-loop final-pose hold before validating release to 8.
            Segment(2, Idle), Segment(220, Input(rise: true)), Segment(35, Idle)),
        Scenario("GT-004", "空中方向入力から解放", TestPlayGoldenSetupKind.AirborneGun,
            new[] { 4, 8 }, new[] { "Force" },
            Segment(15, Input(direction: 8)), Segment(35, Idle)),
        Scenario("GT-005", "方向二度押しステップ", TestPlayGoldenSetupKind.GroundedGun,
            new[] { 11, 6 }, new[] { "Move" },
            Segment(1, Input(direction: 8)), Segment(2, Idle),
            Segment(45, Input(direction: 8)), Segment(35, Idle)),
        Scenario("GT-006", "Z二度押しブースト", TestPlayGoldenSetupKind.AirborneGun,
            new[] { 22, 8 }, new[] { "Move", "RunProc" },
            Segment(1, Input(rise: true)), Segment(2, Idle),
            Segment(50, Input(rise: true)), Segment(40, Idle)),
        Scenario("GT-007", "X射撃", TestPlayGoldenSetupKind.GroundedGun,
            new[] { 100, 6 }, new[] { "ATTACK", "AttackDelay" },
            Segment(2, Idle), Segment(1, Input(shot: true)), Segment(150, Idle)),
        Scenario("GT-008", "C持替えから方向格闘", TestPlayGoldenSetupKind.GroundedGun,
            new[] { 18, 130 }, new[] { "ChangeWeapon" },
            Segment(1, Input(melee: true)), Segment(60, Idle),
            Segment(1, Input(direction: 8, melee: true)), Segment(100, Input(direction: 8)),
            Segment(60, Idle)),
        Scenario("GT-009", "格闘連携", TestPlayGoldenSetupKind.GroundedSword,
            new[] { 131, 132, 6 }, new[] { "SwordCancel", "RunProc2" },
            Segment(1, Input(melee: true)), Segment(10, Idle),
            Segment(1, Input(melee: true)), Segment(20, Idle),
            Segment(1, Input(melee: true)), Segment(200, Idle)),
        ScenarioWithTarget("GT-010", "ロックと射撃旋回", TestPlayGoldenSetupKind.GroundedGun,
            Vector3.back * 2f,
            new[] { 103, 6 }, new[] { "ShotTurnAng" },
            Segment(1, Input(lockTarget: true)), Segment(2, Idle),
            Segment(1, Input(direction: 4, shot: true)), Segment(70, Input(direction: 4)),
            Segment(60, Idle))
    };

    public static IReadOnlyList<TestPlayGoldenScenarioDefinition> All => Definitions;

    public static TestPlayGoldenScenarioDefinition Find(string id)
    {
        for (int i = 0; i < Definitions.Length; i++)
        {
            if (string.Equals(Definitions[i].id, id, StringComparison.OrdinalIgnoreCase))
                return Definitions[i];
        }
        return null;
    }

    static TestPlayGoldenScenarioDefinition Scenario(
        string id,
        string title,
        TestPlayGoldenSetupKind setup,
        int[] requiredActionIds,
        string[] requiredCommands,
        params TestPlayGoldenInputSegment[] segments)
    {
        return new TestPlayGoldenScenarioDefinition
        {
            id = id,
            title = title,
            baseline = TestPlayGoldenBaselineKind.RealAniObserved,
            setup = setup,
            targetPosition = Vector3.forward * 2f,
            requiredActionIds = requiredActionIds,
            requiredCommands = requiredCommands,
            inputSegments = segments
        };
    }

    static TestPlayGoldenScenarioDefinition ScenarioWithTarget(
        string id,
        string title,
        TestPlayGoldenSetupKind setup,
        Vector3 targetPosition,
        int[] requiredActionIds,
        string[] requiredCommands,
        params TestPlayGoldenInputSegment[] segments)
    {
        TestPlayGoldenScenarioDefinition definition = Scenario(
            id, title, setup, requiredActionIds, requiredCommands, segments);
        definition.targetPosition = targetPosition;
        return definition;
    }

    static TestPlayGoldenInputSegment Segment(int ticks, TestPlayGoldenInputFrame input)
    {
        return new TestPlayGoldenInputSegment { ticks = ticks, input = input };
    }

    static TestPlayGoldenInputFrame Input(
        int direction = 0,
        bool rise = false,
        bool shot = false,
        bool melee = false,
        bool guard = false,
        bool lockTarget = false,
        bool special1 = false,
        bool special2 = false,
        bool special3 = false)
    {
        return new TestPlayGoldenInputFrame
        {
            direction = direction,
            rise = rise,
            shot = shot,
            melee = melee,
            guard = guard,
            lockTarget = lockTarget,
            special1 = special1,
            special2 = special2,
            special3 = special3
        };
    }
}

public struct TestPlayGoldenSessionHeader
{
    public int schemaVersion;
    public string source;
    public string scenarioId;
    public string mechId;
    public string aniHash;
    public string sptHash;
    public int tickRate;
    public TestPlayGoldenBaselineKind baseline;
}

public struct TestPlayGoldenTickSnapshot
{
    public int direction;
    public bool rise;
    public bool boost;
    public bool shot;
    public bool melee;
    public bool guard;
    public bool lockInput;
    public float hp;
    public float movementEnergy;
    public float auxiliaryEnergy;
    public int frameIndex;
    public int scriptIndex;
    public int scriptTick;
    public int actionTick;
    public bool poseHeldAtEnd;
    public bool airborne;
    public bool grounded;
    public bool targetLocked;
    public Vector3 rootPosition;
    public Quaternion rootRotation;
}

public static class TestPlayPhase5TickTrace
{
    public static string Serialize(string phase4Json, TestPlayGoldenTickSnapshot snapshot)
    {
        string prefix = string.IsNullOrEmpty(phase4Json) ? "{}" : phase4Json;
        if (prefix[prefix.Length - 1] == '}')
            prefix = prefix.Substring(0, prefix.Length - 1);

        StringBuilder builder = new StringBuilder(prefix, prefix.Length + 512);
        builder.Append(",\"input\":{")
            .Append("\"direction\":").Append(snapshot.direction).Append(',')
            .Append("\"rise\":").Append(Bool(snapshot.rise)).Append(',')
            .Append("\"boost\":").Append(Bool(snapshot.boost)).Append(',')
            .Append("\"shot\":").Append(Bool(snapshot.shot)).Append(',')
            .Append("\"melee\":").Append(Bool(snapshot.melee)).Append(',')
            .Append("\"guard\":").Append(Bool(snapshot.guard)).Append(',')
            .Append("\"lock\":").Append(Bool(snapshot.lockInput)).Append("},")
            .Append("\"resources\":{")
            .Append("\"hp\":").Append(Format(snapshot.hp)).Append(',')
            .Append("\"movementEnergy\":").Append(Format(snapshot.movementEnergy)).Append(',')
            .Append("\"auxiliaryEnergy\":").Append(Format(snapshot.auxiliaryEnergy)).Append("},")
            .Append("\"runtime\":{")
            .Append("\"frame\":").Append(snapshot.frameIndex).Append(',')
            .Append("\"script\":").Append(snapshot.scriptIndex).Append(',')
            .Append("\"scriptTick\":").Append(snapshot.scriptTick).Append(',')
            .Append("\"actionTick\":").Append(snapshot.actionTick).Append(',')
            .Append("\"poseHeld\":").Append(Bool(snapshot.poseHeldAtEnd)).Append(',')
            .Append("\"airborne\":").Append(Bool(snapshot.airborne)).Append(',')
            .Append("\"grounded\":").Append(Bool(snapshot.grounded)).Append(',')
            .Append("\"targetLocked\":").Append(Bool(snapshot.targetLocked)).Append(',')
            .Append("\"rootPosition\":");
        AppendVector(builder, snapshot.rootPosition);
        builder.Append(",\"rootRotation\":");
        AppendQuaternion(builder, snapshot.rootRotation);
        builder.Append("}}");
        return builder.ToString();
    }

    static void AppendVector(StringBuilder builder, Vector3 value)
    {
        builder.Append('[').Append(Format(value.x)).Append(',')
            .Append(Format(value.y)).Append(',').Append(Format(value.z)).Append(']');
    }

    static void AppendQuaternion(StringBuilder builder, Quaternion value)
    {
        builder.Append('[').Append(Format(value.x)).Append(',')
            .Append(Format(value.y)).Append(',').Append(Format(value.z)).Append(',')
            .Append(Format(value.w)).Append(']');
    }

    static string Bool(bool value) => value ? "true" : "false";
    static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}

public sealed class TestPlayGoldenTraceSession
{
    readonly TestPlayGoldenSessionHeader header;
    readonly List<string> ticks = new List<string>();

    public TestPlayGoldenTraceSession(TestPlayGoldenSessionHeader value)
    {
        header = value;
    }

    public int TickCount => ticks.Count;
    public IReadOnlyList<string> Ticks => ticks;
    public TestPlayGoldenSessionHeader Header => header;

    public void AddTick(string json)
    {
        ticks.Add(string.IsNullOrEmpty(json) ? "{}" : json);
    }

    public static bool TryParseJsonLines(
        string jsonLines,
        out TestPlayGoldenTraceSession session,
        out string error)
    {
        session = null;
        error = null;
        if (string.IsNullOrWhiteSpace(jsonLines))
        {
            error = "Unity golden trace JSONL is empty.";
            return false;
        }

        string[] sourceLines = jsonLines.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        List<string> lines = new List<string>();
        for (int i = 0; i < sourceLines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(sourceLines[i]))
                lines.Add(sourceLines[i].Trim());
        }
        if (lines.Count < 2 || !TestPlayTraceJson.TryReadObject(lines[0], "session", out string headerJson))
        {
            error = "Unity golden trace requires a session header and at least one tick.";
            return false;
        }

        if (!TestPlayTraceJson.TryReadInteger(headerJson, "schemaVersion", out long schemaVersion) ||
            !TestPlayTraceJson.TryReadString(headerJson, "source", out string source) ||
            !TestPlayTraceJson.TryReadString(headerJson, "scenario", out string scenario) ||
            !TestPlayTraceJson.TryReadString(headerJson, "mechId", out string mechId) ||
            !TestPlayTraceJson.TryReadString(headerJson, "aniHash", out string aniHash) ||
            !TestPlayTraceJson.TryReadString(headerJson, "sptHash", out string sptHash) ||
            !TestPlayTraceJson.TryReadInteger(headerJson, "tickRate", out long tickRate) ||
            !TestPlayTraceJson.TryReadString(headerJson, "baseline", out string baselineText) ||
            !Enum.TryParse(baselineText, out TestPlayGoldenBaselineKind baseline))
        {
            error = "Unity golden trace session header is incomplete or invalid.";
            return false;
        }

        TestPlayGoldenTraceSession parsed = new TestPlayGoldenTraceSession(new TestPlayGoldenSessionHeader
        {
            schemaVersion = (int)schemaVersion,
            source = source,
            scenarioId = scenario,
            mechId = mechId,
            aniHash = aniHash,
            sptHash = sptHash,
            tickRate = (int)tickRate,
            baseline = baseline
        });
        if (!TestPlayTraceJson.TryReadInteger(lines[1], "tick", out long firstTick))
        {
            error = "Unity golden trace first tick is missing.";
            return false;
        }
        for (int i = 1; i < lines.Count; i++)
        {
            long expectedTick = firstTick + i - 1;
            if (!TestPlayTraceJson.TryReadInteger(lines[i], "tick", out long tick) || tick != expectedTick)
            {
                error = "Unity golden trace ticks must be contiguous; line " +
                        (i + 1) + " is invalid.";
                return false;
            }
            parsed.AddTick(lines[i]);
        }
        session = parsed;
        return true;
    }

    public string SerializeJsonLines()
    {
        StringBuilder builder = new StringBuilder(SerializeHeader(header));
        for (int i = 0; i < ticks.Count; i++)
            builder.Append('\n').Append(ticks[i]);
        return builder.ToString();
    }

    public string ComputeTraceHash()
    {
        return ComputeSha256(SerializeJsonLines());
    }

    public static int FindFirstMismatch(TestPlayGoldenTraceSession left, TestPlayGoldenTraceSession right)
    {
        if (left == null || right == null)
            return -2;
        int count = Math.Min(left.ticks.Count, right.ticks.Count);
        for (int i = 0; i < count; i++)
        {
            if (!string.Equals(left.ticks[i], right.ticks[i], StringComparison.Ordinal))
                return i;
        }
        return left.ticks.Count == right.ticks.Count ? -1 : count;
    }

    public static string ComputeSha256(string value)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    static string SerializeHeader(TestPlayGoldenSessionHeader value)
    {
        return new StringBuilder(384)
            .Append("{\"session\":{")
            .Append("\"schemaVersion\":").Append(value.schemaVersion).Append(',')
            .Append("\"source\":\"").Append(Escape(value.source)).Append("\",")
            .Append("\"scenario\":\"").Append(Escape(value.scenarioId)).Append("\",")
            .Append("\"mechId\":\"").Append(Escape(value.mechId)).Append("\",")
            .Append("\"aniHash\":\"").Append(Escape(value.aniHash)).Append("\",")
            .Append("\"sptHash\":\"").Append(Escape(value.sptHash)).Append("\",")
            .Append("\"tickRate\":").Append(value.tickRate).Append(',')
            .Append("\"baseline\":\"").Append(value.baseline).Append("\"}}")
            .ToString();
    }

    static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }
}
