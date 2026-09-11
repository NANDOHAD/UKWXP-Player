using System;
using System.Globalization;
using UnityEngine;

public enum TestPlayScriptValueType
{
    Empty,
    Number,
    Symbol,
    Stop
}

public struct TestPlayScriptValue
{
    public TestPlayScriptValueType type;
    public float number;
    public string symbol;

    public static TestPlayScriptValue Empty()
    {
        return new TestPlayScriptValue { type = TestPlayScriptValueType.Empty, number = 0f, symbol = "" };
    }

    public static TestPlayScriptValue Number(float value)
    {
        return new TestPlayScriptValue { type = TestPlayScriptValueType.Number, number = value, symbol = "" };
    }

    public static TestPlayScriptValue Symbol(string value)
    {
        return new TestPlayScriptValue { type = TestPlayScriptValueType.Symbol, number = 0f, symbol = value ?? "" };
    }

    public static TestPlayScriptValue Stop()
    {
        return new TestPlayScriptValue { type = TestPlayScriptValueType.Stop, number = 0f, symbol = "STOP" };
    }

    public int AsInt(int fallback = 0)
    {
        if (type == TestPlayScriptValueType.Number)
            return Mathf.RoundToInt(number);

        int parsed;
        if (!string.IsNullOrWhiteSpace(symbol) && int.TryParse(symbol, out parsed))
            return parsed;

        return fallback;
    }

    public float AsFloat(float fallback = 0f)
    {
        if (type == TestPlayScriptValueType.Number)
            return number;

        float parsed;
        if (!string.IsNullOrWhiteSpace(symbol) && float.TryParse(NormalizeFloatLiteral(symbol), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            return parsed;

        return fallback;
    }

    public bool AsBool(bool fallback = false)
    {
        if (type == TestPlayScriptValueType.Number)
            return Mathf.Abs(number) > 0.00001f;

        bool parsed;
        if (!string.IsNullOrWhiteSpace(symbol) && bool.TryParse(symbol, out parsed))
            return parsed;

        return fallback;
    }

    public override string ToString()
    {
        if (type == TestPlayScriptValueType.Number)
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return symbol ?? "";
    }

    public static bool TryParse(string token, TestPlayStateTable state, out TestPlayScriptValue value)
    {
        value = Empty();
        string t = (token ?? "").Trim();
        if (t.Length == 0)
            return true;

        if (string.Equals(t, "STOP", StringComparison.OrdinalIgnoreCase))
        {
            value = Stop();
            return true;
        }

        float refValue;
        if (state != null && state.TryReadReference(t, out refValue))
        {
            value = Number(refValue);
            return true;
        }

        if ((t.StartsWith("\"", StringComparison.Ordinal) && t.EndsWith("\"", StringComparison.Ordinal)) ||
            (t.StartsWith("'", StringComparison.Ordinal) && t.EndsWith("'", StringComparison.Ordinal)))
        {
            value = Symbol(t.Substring(1, t.Length - 2));
            return true;
        }

        float parsed;
        if (float.TryParse(NormalizeFloatLiteral(t), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            value = Number(parsed);
            return true;
        }

        value = Symbol(t);
        return true;
    }

    static string NormalizeFloatLiteral(string value)
    {
        string t = (value ?? "").Trim();
        if (t.EndsWith("f", StringComparison.OrdinalIgnoreCase))
            return t.Substring(0, t.Length - 1);
        return t;
    }
}
