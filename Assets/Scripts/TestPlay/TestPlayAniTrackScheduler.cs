using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public enum TestPlayAniTrackKind
{
    Main,
    Secondary,
    Sub
}

public enum TestPlayAniTrackExecutionReason
{
    EnterBlock,
    Repeat
}

public enum TestPlayAniSchedulerEventType
{
    TrackStarted,
    BlockEntered,
    BlockExecuted,
    BlockRepeated,
    BlockJumped,
    TrackLooped,
    TrackFinished,
    TrackStopped,
    Diagnostic
}

public sealed class TestPlayAniBlockDefinition
{
    public int index;
    public int durationTicks = 1;
    public float poseAdvancePerTick;
    public bool sentinel;
    public TestPlayAniProgram program;
}

public sealed class TestPlayAniTrackDefinition
{
    public TestPlayAniTrackKind kind;
    public int channel;
    public bool loop;
    public readonly List<TestPlayAniBlockDefinition> blocks = new List<TestPlayAniBlockDefinition>();
}

public sealed class TestPlayAniTrackState
{
    public TestPlayAniTrackKind kind;
    public int channel;
    public int blockIndex = -1;
    public int remainingTicks;
    public int repeatInterval = -1;
    public int repeatCounter;
    public bool active;
    public bool finished;
    public bool pendingEntry;
    public int executionCount;

    public TestPlayAniTrackState Copy()
    {
        return (TestPlayAniTrackState)MemberwiseClone();
    }
}

public sealed class TestPlayAniSchedulerEvent
{
    public TestPlayAniSchedulerEventType type;
    public int tick;
    public TestPlayAniTrackKind track;
    public int channel;
    public int blockIndex;
    public string message = "";
}

public sealed class TestPlayAniTrackScheduler
{
    readonly Dictionary<TestPlayAniTrackKind, TestPlayAniTrackDefinition> definitions =
        new Dictionary<TestPlayAniTrackKind, TestPlayAniTrackDefinition>();
    readonly Dictionary<TestPlayAniTrackKind, TestPlayAniTrackState> states =
        new Dictionary<TestPlayAniTrackKind, TestPlayAniTrackState>();
    readonly List<TestPlayAniSchedulerEvent> events = new List<TestPlayAniSchedulerEvent>();

    public int TickNumber { get; private set; }
    public IReadOnlyList<TestPlayAniSchedulerEvent> Events => events;

    public void Configure(TestPlayAniTrackDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        definitions[definition.kind] = definition;
        states[definition.kind] = new TestPlayAniTrackState
        {
            kind = definition.kind,
            channel = definition.channel
        };
    }

    public void StartTrack(TestPlayAniTrackKind kind, int blockIndex = -1)
    {
        TestPlayAniTrackDefinition definition = GetDefinition(kind);
        TestPlayAniTrackState state = GetMutableState(kind);
        int firstIndex = blockIndex >= 0 ? blockIndex : FindFirstExecutableBlock(definition);
        state.channel = definition.channel;
        state.blockIndex = firstIndex;
        state.remainingTicks = 0;
        state.repeatInterval = -1;
        state.repeatCounter = 0;
        state.active = firstIndex >= 0;
        state.finished = firstIndex < 0;
        state.pendingEntry = state.active;
        state.executionCount = 0;
        AddEvent(TestPlayAniSchedulerEventType.TrackStarted, state, "");
    }

    public void Stop(TestPlayAniTrackKind kind)
    {
        TestPlayAniTrackState state = GetMutableState(kind);
        state.active = false;
        state.finished = true;
        state.pendingEntry = false;
        AddEvent(TestPlayAniSchedulerEventType.TrackStopped, state, "");
    }

    public void Jump(TestPlayAniTrackKind kind, int blockIndex)
    {
        TestPlayAniTrackState state = GetMutableState(kind);
        if (FindBlock(GetDefinition(kind), blockIndex) == null)
        {
            AddEvent(TestPlayAniSchedulerEventType.Diagnostic, state, "Unknown block " + blockIndex);
            return;
        }

        state.blockIndex = blockIndex;
        state.remainingTicks = 0;
        state.repeatInterval = -1;
        state.repeatCounter = 0;
        state.active = true;
        state.finished = false;
        state.pendingEntry = true;
        AddEvent(TestPlayAniSchedulerEventType.BlockJumped, state, "");
    }

    public void SetRepeat(TestPlayAniTrackKind kind, int interval)
    {
        TestPlayAniTrackState state = GetMutableState(kind);
        state.repeatInterval = Math.Max(0, interval);
        state.repeatCounter = state.repeatInterval;
    }

    public void ClearRepeat(TestPlayAniTrackKind kind)
    {
        TestPlayAniTrackState state = GetMutableState(kind);
        state.repeatInterval = -1;
        state.repeatCounter = 0;
    }

    public TestPlayAniTrackState GetState(TestPlayAniTrackKind kind)
    {
        return GetMutableState(kind).Copy();
    }

    public void Tick(Action<TestPlayAniTrackState, TestPlayAniBlockDefinition, TestPlayAniTrackExecutionReason> execute)
    {
        TickNumber++;
        events.Clear();
        TickTrack(TestPlayAniTrackKind.Main, execute);
        TickTrack(TestPlayAniTrackKind.Secondary, execute);
        TickTrack(TestPlayAniTrackKind.Sub, execute);
    }

    public TestPlayAniTickTrace CaptureTrace()
    {
        TestPlayAniTickTrace trace = new TestPlayAniTickTrace { tick = TickNumber };
        AddSnapshot(trace, TestPlayAniTrackKind.Main);
        AddSnapshot(trace, TestPlayAniTrackKind.Secondary);
        AddSnapshot(trace, TestPlayAniTrackKind.Sub);
        for (int i = 0; i < events.Count; i++)
            trace.events.Add(CopyEvent(events[i]));
        return trace;
    }

    void TickTrack(
        TestPlayAniTrackKind kind,
        Action<TestPlayAniTrackState, TestPlayAniBlockDefinition, TestPlayAniTrackExecutionReason> execute)
    {
        if (!states.TryGetValue(kind, out TestPlayAniTrackState state) || !state.active)
            return;

        TestPlayAniTrackDefinition definition = definitions[kind];
        TestPlayAniBlockDefinition block = FindBlock(definition, state.blockIndex);
        if (block == null)
        {
            AddEvent(TestPlayAniSchedulerEventType.Diagnostic, state, "Unknown block " + state.blockIndex);
            Finish(state);
            return;
        }

        if (block.sentinel)
        {
            HandleSentinel(definition, state);
            if (!state.active)
                return;
            block = FindBlock(definition, state.blockIndex);
        }

        if (state.pendingEntry)
        {
            state.pendingEntry = false;
            state.remainingTicks = Math.Max(1, block.durationTicks);
            AddEvent(TestPlayAniSchedulerEventType.BlockEntered, state, "");
            ExecuteBlock(state, block, TestPlayAniTrackExecutionReason.EnterBlock, execute);
        }
        else if (state.repeatInterval >= 0)
        {
            if (state.repeatCounter == 0)
            {
                ExecuteBlock(state, block, TestPlayAniTrackExecutionReason.Repeat, execute);
                state.repeatCounter = state.repeatInterval;
            }
            else
            {
                state.repeatCounter--;
            }
        }

        state.remainingTicks--;
        if (state.remainingTicks <= 0)
            Advance(definition, state);
    }

    void ExecuteBlock(
        TestPlayAniTrackState state,
        TestPlayAniBlockDefinition block,
        TestPlayAniTrackExecutionReason reason,
        Action<TestPlayAniTrackState, TestPlayAniBlockDefinition, TestPlayAniTrackExecutionReason> execute)
    {
        state.executionCount++;
        AddEvent(
            reason == TestPlayAniTrackExecutionReason.EnterBlock
                ? TestPlayAniSchedulerEventType.BlockExecuted
                : TestPlayAniSchedulerEventType.BlockRepeated,
            state,
            "");
        execute?.Invoke(state.Copy(), block, reason);
    }

    void Advance(TestPlayAniTrackDefinition definition, TestPlayAniTrackState state)
    {
        int position = FindBlockPosition(definition, state.blockIndex);
        if (position < 0 || position + 1 >= definition.blocks.Count)
        {
            if (definition.loop)
                Loop(definition, state);
            else
                Finish(state);
            return;
        }

        TestPlayAniBlockDefinition next = definition.blocks[position + 1];
        if (next.sentinel)
        {
            if (definition.loop)
                Loop(definition, state);
            else
            {
                state.blockIndex = next.index;
                Finish(state);
            }
            return;
        }

        state.blockIndex = next.index;
        state.remainingTicks = 0;
        state.repeatInterval = -1;
        state.repeatCounter = 0;
        state.pendingEntry = true;
    }

    void HandleSentinel(TestPlayAniTrackDefinition definition, TestPlayAniTrackState state)
    {
        if (definition.loop)
            Loop(definition, state);
        else
            Finish(state);
    }

    void Loop(TestPlayAniTrackDefinition definition, TestPlayAniTrackState state)
    {
        int first = FindFirstExecutableBlock(definition);
        if (first < 0)
        {
            Finish(state);
            return;
        }

        state.blockIndex = first;
        state.remainingTicks = 0;
        state.repeatInterval = -1;
        state.repeatCounter = 0;
        state.pendingEntry = true;
        AddEvent(TestPlayAniSchedulerEventType.TrackLooped, state, "");
    }

    void Finish(TestPlayAniTrackState state)
    {
        state.active = false;
        state.finished = true;
        state.pendingEntry = false;
        state.remainingTicks = 0;
        AddEvent(TestPlayAniSchedulerEventType.TrackFinished, state, "");
    }

    void AddSnapshot(TestPlayAniTickTrace trace, TestPlayAniTrackKind kind)
    {
        if (states.TryGetValue(kind, out TestPlayAniTrackState state))
            trace.tracks.Add(state.Copy());
    }

    void AddEvent(TestPlayAniSchedulerEventType type, TestPlayAniTrackState state, string message)
    {
        events.Add(new TestPlayAniSchedulerEvent
        {
            type = type,
            tick = TickNumber,
            track = state.kind,
            channel = state.channel,
            blockIndex = state.blockIndex,
            message = message ?? ""
        });
    }

    TestPlayAniTrackDefinition GetDefinition(TestPlayAniTrackKind kind)
    {
        if (!definitions.TryGetValue(kind, out TestPlayAniTrackDefinition definition))
            throw new InvalidOperationException("Track is not configured: " + kind);
        return definition;
    }

    TestPlayAniTrackState GetMutableState(TestPlayAniTrackKind kind)
    {
        if (!states.TryGetValue(kind, out TestPlayAniTrackState state))
            throw new InvalidOperationException("Track is not configured: " + kind);
        return state;
    }

    static int FindFirstExecutableBlock(TestPlayAniTrackDefinition definition)
    {
        for (int i = 0; i < definition.blocks.Count; i++)
        {
            if (!definition.blocks[i].sentinel)
                return definition.blocks[i].index;
        }
        return -1;
    }

    static TestPlayAniBlockDefinition FindBlock(TestPlayAniTrackDefinition definition, int index)
    {
        int position = FindBlockPosition(definition, index);
        return position >= 0 ? definition.blocks[position] : null;
    }

    static int FindBlockPosition(TestPlayAniTrackDefinition definition, int index)
    {
        for (int i = 0; i < definition.blocks.Count; i++)
        {
            if (definition.blocks[i].index == index)
                return i;
        }
        return -1;
    }

    static TestPlayAniSchedulerEvent CopyEvent(TestPlayAniSchedulerEvent source)
    {
        return new TestPlayAniSchedulerEvent
        {
            type = source.type,
            tick = source.tick,
            track = source.track,
            channel = source.channel,
            blockIndex = source.blockIndex,
            message = source.message
        };
    }
}

public sealed class TestPlayAniTickTrace
{
    public int tick;
    public readonly List<TestPlayAniTrackState> tracks = new List<TestPlayAniTrackState>();
    public readonly List<TestPlayAniSchedulerEvent> events = new List<TestPlayAniSchedulerEvent>();

    public string ToJsonLine()
    {
        StringBuilder builder = new StringBuilder(256);
        builder.Append("{\"tick\":").Append(tick.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"tracks\":[");
        for (int i = 0; i < tracks.Count; i++)
        {
            if (i > 0) builder.Append(',');
            TestPlayAniTrackState track = tracks[i];
            builder.Append("{\"kind\":\"").Append(track.kind).Append("\"");
            builder.Append(",\"channel\":").Append(track.channel.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"block\":").Append(track.blockIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"remaining\":").Append(track.remainingTicks.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"repeat\":").Append(track.repeatCounter.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"active\":").Append(track.active ? "true" : "false");
            builder.Append(",\"finished\":").Append(track.finished ? "true" : "false");
            builder.Append('}');
        }
        builder.Append("],\"events\":[");
        for (int i = 0; i < events.Count; i++)
        {
            if (i > 0) builder.Append(',');
            TestPlayAniSchedulerEvent item = events[i];
            builder.Append("{\"type\":\"").Append(item.type).Append("\"");
            builder.Append(",\"track\":\"").Append(item.track).Append("\"");
            builder.Append(",\"channel\":").Append(item.channel.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"block\":").Append(item.blockIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"message\":\"").Append(Escape(item.message)).Append("\"}");
        }
        builder.Append("]}");
        return builder.ToString();
    }

    static string Escape(string value)
    {
        return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
