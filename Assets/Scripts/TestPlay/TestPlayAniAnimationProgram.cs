using System;
using System.Collections.Generic;

public sealed class TestPlayAniAnimationBlock
{
    public int index;
    public int durationTicks;
    public float poseAdvancePerTick;
    public bool sentinel;
    public TestPlayAniProgram program;
}

public sealed class TestPlayAniAnimationProgram
{
    public int actionId = -1;
    public string name = "";
    public TestPlayAniProgram initialProgram;
    public readonly List<TestPlayAniAnimationBlock> blocks = new List<TestPlayAniAnimationBlock>();

    public TestPlayAniTrackDefinition CreateTrack(TestPlayAniTrackKind kind, int channel, bool loop)
    {
        TestPlayAniTrackDefinition definition = new TestPlayAniTrackDefinition
        {
            kind = kind,
            channel = channel,
            loop = loop
        };

        for (int i = 0; i < blocks.Count; i++)
        {
            TestPlayAniAnimationBlock source = blocks[i];
            definition.blocks.Add(new TestPlayAniBlockDefinition
            {
                index = source.index,
                durationTicks = source.durationTicks,
                poseAdvancePerTick = source.poseAdvancePerTick,
                sentinel = source.sentinel,
                program = source.program
            });
        }

        return definition;
    }
}

public static class TestPlayAniAnimationCompiler
{
    public static TestPlayAniAnimationProgram Compile(animation source, int actionId = -1)
    {
        TestPlayAniAnimationProgram result = new TestPlayAniAnimationProgram
        {
            actionId = actionId,
            name = source != null ? source.name ?? "" : "",
            initialProgram = TestPlayAniCompiler.Compile(source != null ? source.squirrelInit : "")
        };

        if (source == null || source.scripts == null)
            return result;

        for (int i = 0; i < source.scripts.Count; i++)
        {
            script sourceBlock = source.scripts[i];
            bool sentinel = sourceBlock.unk == 999999999;
            result.blocks.Add(new TestPlayAniAnimationBlock
            {
                index = i,
                durationTicks = sentinel ? 0 : Math.Max(1, sourceBlock.unk),
                poseAdvancePerTick = sourceBlock.time,
                sentinel = sentinel,
                program = sentinel ? null : TestPlayAniCompiler.Compile(sourceBlock.squirrel)
            });
        }

        return result;
    }
}
