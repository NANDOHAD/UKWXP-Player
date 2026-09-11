using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class TestPlayOriginalObservationHeader
{
    public int schemaVersion = 1;
    public string source = "original-observation";
    public string scenario;
    public string mechId;
    public string exeHash;
    public string aniHash;
    public string sptHash;
    public int tickRate = 60;
    public int tickOrigin = 1;
    public string normalizationProfile;
    public string[] observedFields;
}

[Serializable]
sealed class TestPlayOriginalObservationHeaderEnvelope
{
    public TestPlayOriginalObservationHeader session;
}

public sealed class TestPlayOriginalTraceSession
{
    readonly TestPlayOriginalObservationHeader header;
    readonly List<string> ticks;

    TestPlayOriginalTraceSession(TestPlayOriginalObservationHeader value, List<string> records)
    {
        header = value;
        ticks = records;
    }

    public TestPlayOriginalObservationHeader Header => header;
    public IReadOnlyList<string> Ticks => ticks;
    public int TickCount => ticks.Count;

    public static bool TryParseJsonLines(
        string jsonLines,
        out TestPlayOriginalTraceSession session,
        out string error)
    {
        session = null;
        error = null;
        if (string.IsNullOrWhiteSpace(jsonLines))
        {
            error = "Original observation JSONL is empty.";
            return false;
        }

        string[] sourceLines = jsonLines.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        List<string> lines = new List<string>();
        for (int i = 0; i < sourceLines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(sourceLines[i]))
                lines.Add(sourceLines[i].Trim());
        }
        if (lines.Count < 2)
        {
            error = "Original observation JSONL requires a session header and at least one tick.";
            return false;
        }

        TestPlayOriginalObservationHeaderEnvelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<TestPlayOriginalObservationHeaderEnvelope>(lines[0]);
        }
        catch (Exception ex)
        {
            error = "Invalid original observation header: " + ex.Message;
            return false;
        }

        if (envelope == null || envelope.session == null)
        {
            error = "Original observation header must contain a session object.";
            return false;
        }
        if (envelope.session.schemaVersion != 1 && envelope.session.schemaVersion != 2)
        {
            error = "Unsupported original observation schemaVersion " + envelope.session.schemaVersion + ".";
            return false;
        }
        if (!string.Equals(envelope.session.source, "original-observation", StringComparison.Ordinal))
        {
            error = "Original observation source must be original-observation.";
            return false;
        }
        if (envelope.session.observedFields == null || envelope.session.observedFields.Length == 0)
        {
            error = "Original observation header must declare observedFields.";
            return false;
        }
        if (envelope.session.tickRate != 60 || string.IsNullOrWhiteSpace(envelope.session.scenario) ||
            string.IsNullOrWhiteSpace(envelope.session.mechId) ||
            string.IsNullOrWhiteSpace(envelope.session.exeHash) ||
            string.IsNullOrWhiteSpace(envelope.session.aniHash) ||
            string.IsNullOrWhiteSpace(envelope.session.sptHash) ||
            string.IsNullOrWhiteSpace(envelope.session.normalizationProfile))
        {
            error = "Original observation header is missing identity or normalization metadata.";
            return false;
        }

        List<string> tickLines = lines.GetRange(1, lines.Count - 1);
        if (!TestPlayTraceJson.TryReadInteger(tickLines[0], "tick", out long firstTick))
        {
            error = "Original observation first tick is missing.";
            return false;
        }
        if (firstTick != envelope.session.tickOrigin)
        {
            error = "Original observation first tick does not match tickOrigin.";
            return false;
        }
        for (int i = 0; i < tickLines.Count; i++)
        {
            long expectedTick = firstTick + i;
            if (!TestPlayTraceJson.TryReadInteger(tickLines[i], "tick", out long tick) || tick != expectedTick)
            {
                error = "Original observation tick records must be contiguous; line " +
                        (i + 2) + " did not contain tick " + expectedTick + ".";
                return false;
            }
        }

        session = new TestPlayOriginalTraceSession(envelope.session, tickLines);
        return true;
    }
}

public sealed class TestPlayOriginalTraceComparisonOptions
{
    public double rawFloatTolerance;
    public double transformTolerance = 0.00001d;
    public int contextRadius = 2;
}

public enum TestPlayOriginalTraceMismatchKind
{
    None,
    Header,
    MissingField,
    UnsupportedField,
    Value,
    TickCount
}

public sealed class TestPlayOriginalTraceMismatch
{
    public TestPlayOriginalTraceMismatchKind kind;
    public int tickIndex = -1;
    public string field;
    public string originalValue;
    public string unityValue;
    public string message;
}

public sealed class TestPlayOriginalTraceComparisonResult
{
    public int comparedTicks;
    public int comparedValues;
    public TestPlayOriginalTraceMismatch firstMismatch;

    public bool IsMatch => firstMismatch == null;
}

public static class TestPlayOriginalTraceComparer
{
    static readonly HashSet<string> IntegerFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "tick", "requestedAction", "logicalAction", "poseAction", "scriptAction",
        "primaryChannel", "secondaryChannel", "input.direction", "runtime.frame",
        "runtime.script", "runtime.scriptTick", "runtime.actionTick"
    };

    static readonly HashSet<string> BooleanFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "riseClampApplied", "gravityApplied", "input.rise", "input.boost", "input.shot",
        "input.melee", "input.guard", "input.lock", "runtime.poseHeld", "runtime.airborne",
        "runtime.grounded", "runtime.targetLocked"
    };

    static readonly HashSet<string> NumberFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "retention", "moveRetention", "velocityMultiplier", "resources.hp", "resources.movementEnergy",
        "resources.auxiliaryEnergy"
    };

    static readonly HashSet<string> VectorFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "velocityBefore", "force", "velocityAfter", "scriptedVelocity",
        "scriptedVelocityBeforeRetention", "scriptedVelocityAfterRetention",
        "requestedDisplacement", "runtime.rootPosition"
    };

    static readonly HashSet<string> QuaternionFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "runtime.rootRotation"
    };

    static readonly HashSet<string> StringFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "weaponMode", "locomotion"
    };

    public static TestPlayOriginalTraceComparisonResult Compare(
        TestPlayGoldenTraceSession unity,
        TestPlayOriginalTraceSession original,
        TestPlayOriginalTraceComparisonOptions options = null)
    {
        TestPlayOriginalTraceComparisonResult result = new TestPlayOriginalTraceComparisonResult();
        options = options ?? new TestPlayOriginalTraceComparisonOptions();
        if (unity == null || original == null)
            return Fail(result, TestPlayOriginalTraceMismatchKind.Header, -1, "session", "non-null", "null",
                "Both Unity and original observation sessions are required.");

        TestPlayGoldenSessionHeader unityHeader = unity.Header;
        TestPlayOriginalObservationHeader originalHeader = original.Header;
        if (!string.Equals(unityHeader.scenarioId, originalHeader.scenario, StringComparison.Ordinal))
            return HeaderMismatch(result, "scenario", originalHeader.scenario, unityHeader.scenarioId);
        if (!MechIdsMatch(unityHeader.mechId, originalHeader.mechId))
            return HeaderMismatch(result, "mechId", originalHeader.mechId, unityHeader.mechId);
        if (!HashEquals(unityHeader.aniHash, originalHeader.aniHash))
            return HeaderMismatch(result, "aniHash", originalHeader.aniHash, unityHeader.aniHash);
        if (!HashEquals(unityHeader.sptHash, originalHeader.sptHash))
            return HeaderMismatch(result, "sptHash", originalHeader.sptHash, unityHeader.sptHash);
        if (unityHeader.tickRate != originalHeader.tickRate)
            return HeaderMismatch(result, "tickRate", originalHeader.tickRate.ToString(), unityHeader.tickRate.ToString());

        int tickCount = Math.Min(unity.TickCount, original.TickCount);
        string[] fields = originalHeader.observedFields;
        for (int tick = 0; tick < tickCount; tick++)
        {
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                string field = fields[fieldIndex];
                TestPlayOriginalTraceMismatch mismatch = CompareField(
                    field,
                    original.Ticks[tick],
                    unity.Ticks[tick],
                    tick,
                    options);
                if (mismatch != null)
                {
                    result.firstMismatch = mismatch;
                    result.comparedTicks = tick;
                    return result;
                }
                result.comparedValues++;
            }
            result.comparedTicks++;
        }

        if (unity.TickCount != original.TickCount)
        {
            return Fail(result, TestPlayOriginalTraceMismatchKind.TickCount, tickCount, "tickCount",
                original.TickCount.ToString(CultureInfo.InvariantCulture),
                unity.TickCount.ToString(CultureInfo.InvariantCulture),
                "Trace lengths differ at the first unavailable tick.");
        }
        return result;
    }

    static bool MechIdsMatch(string unityMechId, string originalMechId)
    {
        if (string.Equals(unityMechId, originalMechId, StringComparison.Ordinal))
            return true;

        // The original trace records the canonical ROBO folder id while the
        // Unity real-mech trace records the display folder name.  The ANI/SPT
        // hashes are still checked immediately after this alias, so this does
        // not weaken the data-identity gate.
        return string.Equals(originalMechId, "KD-03", StringComparison.Ordinal) &&
               string.Equals(unityMechId, "ガンダムTR-1ヘイズル改", StringComparison.Ordinal);
    }

    public static string BuildFirstMismatchReport(
        TestPlayGoldenTraceSession unity,
        TestPlayOriginalTraceSession original,
        TestPlayOriginalTraceComparisonResult result,
        TestPlayOriginalTraceComparisonOptions options = null)
    {
        options = options ?? new TestPlayOriginalTraceComparisonOptions();
        StringBuilder builder = new StringBuilder();
        if (result == null)
            return "[TestPlayOriginalTrace] Comparison result is null.";
        if (result.IsMatch)
        {
            return "[TestPlayOriginalTrace] Match across " + result.comparedTicks + " ticks and " +
                   result.comparedValues + " observed values.";
        }

        TestPlayOriginalTraceMismatch mismatch = result.firstMismatch;
        builder.Append("[TestPlayOriginalTrace] First mismatch kind=").Append(mismatch.kind)
            .Append(" tick=").Append(mismatch.tickIndex)
            .Append(" field=").Append(mismatch.field)
            .Append(" original=").Append(mismatch.originalValue)
            .Append(" unity=").Append(mismatch.unityValue)
            .Append(" message=").Append(mismatch.message);

        if (mismatch.tickIndex >= 0 && unity != null && original != null)
        {
            int start = Math.Max(0, mismatch.tickIndex - Math.Max(0, options.contextRadius));
            int end = Math.Min(Math.Min(unity.TickCount, original.TickCount) - 1,
                mismatch.tickIndex + Math.Max(0, options.contextRadius));
            for (int i = start; i <= end; i++)
            {
                builder.Append("\n  tick ").Append(i).Append(" original: ").Append(original.Ticks[i]);
                builder.Append("\n  tick ").Append(i).Append(" unity: ").Append(unity.Ticks[i]);
            }
        }
        return builder.ToString();
    }

    static TestPlayOriginalTraceMismatch CompareField(
        string path,
        string originalJson,
        string unityJson,
        int tick,
        TestPlayOriginalTraceComparisonOptions options)
    {
        if (!IsSupported(path))
            return Mismatch(TestPlayOriginalTraceMismatchKind.UnsupportedField, tick, path, null, null,
                "observedFields contains an unsupported path.");

        string originalScope;
        string unityScope;
        string key;
        if (!TryResolveScope(originalJson, path, out originalScope, out key))
            return Mismatch(TestPlayOriginalTraceMismatchKind.MissingField, tick, path, "missing", null,
                "Original observation did not provide its declared field.");
        if (!TryResolveScope(unityJson, path, out unityScope, out key))
            return Mismatch(TestPlayOriginalTraceMismatchKind.MissingField, tick, path, null, "missing",
                "Unity trace does not contain the declared comparable field.");

        if (IntegerFields.Contains(path))
        {
            if (!TestPlayTraceJson.TryReadInteger(originalScope, key, out long originalValue))
                return InvalidValue(tick, path, "original integer");
            if (!TestPlayTraceJson.TryReadInteger(unityScope, key, out long unityValue))
                return InvalidValue(tick, path, "Unity integer");
            return originalValue == unityValue ? null : ValueMismatch(tick, path, originalValue, unityValue);
        }
        if (BooleanFields.Contains(path))
        {
            if (!TestPlayTraceJson.TryReadBoolean(originalScope, key, out bool originalValue))
                return InvalidValue(tick, path, "original boolean");
            if (!TestPlayTraceJson.TryReadBoolean(unityScope, key, out bool unityValue))
                return InvalidValue(tick, path, "Unity boolean");
            return originalValue == unityValue ? null : ValueMismatch(tick, path, originalValue, unityValue);
        }
        if (StringFields.Contains(path))
        {
            if (!TestPlayTraceJson.TryReadString(originalScope, key, out string originalValue))
                return InvalidValue(tick, path, "original string");
            if (!TestPlayTraceJson.TryReadString(unityScope, key, out string unityValue))
                return InvalidValue(tick, path, "Unity string");
            return string.Equals(originalValue, unityValue, StringComparison.Ordinal)
                ? null : ValueMismatch(tick, path, originalValue, unityValue);
        }
        if (NumberFields.Contains(path))
        {
            return CompareNumber(path, originalScope, unityScope, key, tick, options.rawFloatTolerance);
        }
        if (VectorFields.Contains(path) || QuaternionFields.Contains(path))
        {
            int count = QuaternionFields.Contains(path) ? 4 : 3;
            double tolerance = path.StartsWith("runtime.root", StringComparison.Ordinal)
                ? options.transformTolerance : options.rawFloatTolerance;
            if (!TestPlayTraceJson.TryReadNumberArray(originalScope, key, count, out double[] originalValue))
                return InvalidValue(tick, path, "original vector");
            if (!TestPlayTraceJson.TryReadNumberArray(unityScope, key, count, out double[] unityValue))
                return InvalidValue(tick, path, "Unity vector");
            for (int i = 0; i < count; i++)
            {
                if (Math.Abs(originalValue[i] - unityValue[i]) > tolerance)
                    return ValueMismatch(tick, path, Format(originalValue), Format(unityValue));
            }
            return null;
        }
        return Mismatch(TestPlayOriginalTraceMismatchKind.UnsupportedField, tick, path, null, null,
            "No comparison rule was registered.");
    }

    static TestPlayOriginalTraceMismatch CompareNumber(
        string path,
        string originalScope,
        string unityScope,
        string key,
        int tick,
        double tolerance)
    {
        if (!TestPlayTraceJson.TryReadNumber(originalScope, key, out double originalValue))
            return InvalidValue(tick, path, "original number");
        if (!TestPlayTraceJson.TryReadNumber(unityScope, key, out double unityValue))
            return InvalidValue(tick, path, "Unity number");
        return Math.Abs(originalValue - unityValue) <= tolerance
            ? null : ValueMismatch(tick, path, originalValue, unityValue);
    }

    static bool TryResolveScope(string json, string path, out string scope, out string key)
    {
        int separator = path.IndexOf('.');
        if (separator < 0)
        {
            scope = json;
            key = path;
            return TestPlayTraceJson.ContainsKey(scope, key);
        }
        string objectName = path.Substring(0, separator);
        key = path.Substring(separator + 1);
        return TestPlayTraceJson.TryReadObject(json, objectName, out scope) &&
               TestPlayTraceJson.ContainsKey(scope, key);
    }

    static bool IsSupported(string path)
    {
        return IntegerFields.Contains(path) || BooleanFields.Contains(path) ||
               NumberFields.Contains(path) || VectorFields.Contains(path) ||
               QuaternionFields.Contains(path) || StringFields.Contains(path);
    }

    static bool HashEquals(string left, string right)
    {
        return string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    }

    static TestPlayOriginalTraceComparisonResult HeaderMismatch(
        TestPlayOriginalTraceComparisonResult result,
        string field,
        string original,
        string unity)
    {
        return Fail(result, TestPlayOriginalTraceMismatchKind.Header, -1, field, original, unity,
            "Session headers do not identify the same replay input.");
    }

    static TestPlayOriginalTraceComparisonResult Fail(
        TestPlayOriginalTraceComparisonResult result,
        TestPlayOriginalTraceMismatchKind kind,
        int tick,
        string field,
        string original,
        string unity,
        string message)
    {
        result.firstMismatch = Mismatch(kind, tick, field, original, unity, message);
        return result;
    }

    static TestPlayOriginalTraceMismatch InvalidValue(int tick, string field, string side)
    {
        return Mismatch(TestPlayOriginalTraceMismatchKind.MissingField, tick, field, null, null,
            "Could not parse " + side + " value.");
    }

    static TestPlayOriginalTraceMismatch ValueMismatch(
        int tick,
        string field,
        object original,
        object unity)
    {
        return Mismatch(TestPlayOriginalTraceMismatchKind.Value, tick, field,
            Convert.ToString(original, CultureInfo.InvariantCulture),
            Convert.ToString(unity, CultureInfo.InvariantCulture),
            "Observed values differ.");
    }

    static TestPlayOriginalTraceMismatch Mismatch(
        TestPlayOriginalTraceMismatchKind kind,
        int tick,
        string field,
        string original,
        string unity,
        string message)
    {
        return new TestPlayOriginalTraceMismatch
        {
            kind = kind,
            tickIndex = tick,
            field = field,
            originalValue = original ?? "n/a",
            unityValue = unity ?? "n/a",
            message = message
        };
    }

    static string Format(double[] values)
    {
        StringBuilder builder = new StringBuilder("[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return builder.Append(']').ToString();
    }
}

static class TestPlayTraceJson
{
    public static bool ContainsKey(string json, string key)
    {
        return FindValueStart(json, key) >= 0;
    }

    public static bool TryReadObject(string json, string key, out string value)
    {
        value = null;
        int start = FindValueStart(json, key);
        if (start < 0 || start >= json.Length || json[start] != '{')
            return false;
        int end = FindMatching(json, start, '{', '}');
        if (end < 0)
            return false;
        value = json.Substring(start, end - start + 1);
        return true;
    }

    public static bool TryReadInteger(string json, string key, out long value)
    {
        value = 0;
        if (!TryReadToken(json, key, out string token))
            return false;
        return long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryReadNumber(string json, string key, out double value)
    {
        value = 0d;
        if (!TryReadToken(json, key, out string token))
            return false;
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static bool TryReadBoolean(string json, string key, out bool value)
    {
        value = false;
        if (!TryReadToken(json, key, out string token))
            return false;
        if (string.Equals(token, "true", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }
        return string.Equals(token, "false", StringComparison.Ordinal);
    }

    public static bool TryReadString(string json, string key, out string value)
    {
        value = null;
        int start = FindValueStart(json, key);
        if (start < 0 || start >= json.Length || json[start] != '"')
            return false;
        StringBuilder builder = new StringBuilder();
        bool escaped = false;
        for (int i = start + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (escaped)
            {
                builder.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else if (c == '"')
            {
                value = builder.ToString();
                return true;
            }
            else
            {
                builder.Append(c);
            }
        }
        return false;
    }

    public static bool TryReadNumberArray(string json, string key, int count, out double[] values)
    {
        values = null;
        int start = FindValueStart(json, key);
        if (start < 0 || start >= json.Length || json[start] != '[')
            return false;
        int end = FindMatching(json, start, '[', ']');
        if (end < 0)
            return false;
        string[] tokens = json.Substring(start + 1, end - start - 1).Split(',');
        if (tokens.Length != count)
            return false;
        values = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (!double.TryParse(tokens[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                return false;
        }
        return true;
    }

    static bool TryReadToken(string json, string key, out string token)
    {
        token = null;
        int start = FindValueStart(json, key);
        if (start < 0)
            return false;
        int end = start;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && !char.IsWhiteSpace(json[end]))
            end++;
        if (end == start)
            return false;
        token = json.Substring(start, end - start);
        return true;
    }

    static int FindValueStart(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            return -1;
        string marker = "\"" + key + "\"";
        int search = 0;
        while (search < json.Length)
        {
            int keyStart = json.IndexOf(marker, search, StringComparison.Ordinal);
            if (keyStart < 0)
                return -1;
            int colon = keyStart + marker.Length;
            while (colon < json.Length && char.IsWhiteSpace(json[colon]))
                colon++;
            if (colon < json.Length && json[colon] == ':')
            {
                colon++;
                while (colon < json.Length && char.IsWhiteSpace(json[colon]))
                    colon++;
                return colon;
            }
            search = keyStart + marker.Length;
        }
        return -1;
    }

    static int FindMatching(string json, int start, char open, char close)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    inString = false;
                continue;
            }
            if (c == '"')
                inString = true;
            else if (c == open)
                depth++;
            else if (c == close && --depth == 0)
                return i;
        }
        return -1;
    }
}
