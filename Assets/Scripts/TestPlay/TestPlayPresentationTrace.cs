using System.Collections.Generic;
using System.Globalization;
using System.Text;

public struct TestPlayPresentationSnapshot
{
    public IReadOnlyList<TestPlayPresentationEvent> events;
}

public static class TestPlayPhase4TickTrace
{
    public static string Serialize(
        string phase3Json,
        TestPlayPresentationSnapshot presentation)
    {
        string prefix = string.IsNullOrEmpty(phase3Json) ? "{}" : phase3Json;
        if (prefix[prefix.Length - 1] == '}')
            prefix = prefix.Substring(0, prefix.Length - 1);

        StringBuilder builder = new StringBuilder(prefix, prefix.Length + 768);
        builder.Append(",\"presentation\":{\"events\":[");
        int eventCount = presentation.events != null ? presentation.events.Count : 0;
        for (int i = 0; i < eventCount; i++)
        {
            if (i > 0) builder.Append(',');
            AppendEvent(builder, presentation.events[i]);
        }
        builder.Append("]}}");
        return builder.ToString();
    }

    static void AppendEvent(StringBuilder builder, TestPlayPresentationEvent value)
    {
        builder.Append('{')
            .Append("\"type\":\"").Append(value.type).Append("\",")
            .Append("\"tick\":").Append(value.tick).Append(',')
            .Append("\"action\":").Append(value.actionIndex).Append(',')
            .Append("\"script\":").Append(value.scriptIndex).Append(',')
            .Append("\"command\":\"").Append(Escape(value.command)).Append("\",")
            .Append("\"source\":\"").Append(Escape(value.source)).Append("\",")
            .Append("\"symbol\":\"").Append(Escape(value.symbol)).Append("\",")
            .Append("\"resource\":\"").Append(Escape(value.resourceName)).Append("\",")
            .Append("\"originalId\":").Append(value.originalId).Append(',')
            .Append("\"procType\":").Append(value.procType).Append(',')
            .Append("\"subtype\":").Append(value.subtype).Append(',')
            .Append("\"textureId\":").Append(value.textureId).Append(',')
            .Append("\"output\":").Append(Format(value.output)).Append(',')
            .Append("\"evidence\":\"").Append(value.evidence).Append("\",")
            .Append("\"adapter\":\"").Append(value.adapter).Append("\",")
            .Append("\"diagnostic\":\"").Append(Escape(value.diagnostic)).Append("\",")
            .Append("\"arguments\":[");

        int argumentCount = value.arguments != null ? value.arguments.Length : 0;
        for (int i = 0; i < argumentCount; i++)
        {
            if (i > 0) builder.Append(',');
            AppendArgument(builder, value.arguments[i]);
        }
        builder.Append("]}");
    }

    static void AppendArgument(StringBuilder builder, TestPlayScriptValue value)
    {
        builder.Append("{\"kind\":\"").Append(value.type).Append("\",\"value\":\"")
            .Append(Escape(value.ToString())).Append("\"}");
    }

    static string Escape(string value)
    {
        return (value ?? "")
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    static string Format(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
