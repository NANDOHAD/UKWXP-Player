using System;
using System.Collections.Generic;

public static class TestPlayPhase1Verification
{
    public static int RunAll()
    {
        int assertions = 0;
        VerifyTypedCompilation(ref assertions);
        VerifyRuntimeReferenceResolution(ref assertions);
        VerifyCompilerDiagnostics(ref assertions);
        VerifyAnimationDataCompilation(ref assertions);
        VerifyPreviewInterpreterUsesTypedProgram(ref assertions);
        VerifyIndependentTrackScheduling(ref assertions);
        VerifyRepeatAndLoopScheduling(ref assertions);
        VerifyTraceSerialization(ref assertions);
        return assertions;
    }

    static void VerifyTypedCompilation(ref int assertions)
    {
        const string source =
            "@int[10]=1;" +
            "IF(@int[10],==,1);" +
            " Move(1,STOP,2);" +
            " IF(3,>,2); Voice(\"Pilot,Alpha\"); ELSE; Voice(No); ENDIF;" +
            "ELSE; Move(0,0,0); ENDIF;" +
            "UnknownProc(4);";

        TestPlayAniProgram program = TestPlayAniCompiler.Compile(source);
        Require(program.IsValid, "typed ANI source compiles without diagnostics", ref assertions);
        Require(program.instructions.Count == 3, "top-level typed instruction count", ref assertions);
        Require(program.instructions[0].kind == TestPlayAniInstructionKind.Assignment &&
                program.instructions[0].name == "@int[10]",
            "state assignment is represented as a typed instruction", ref assertions);

        TestPlayAniInstruction conditional = program.instructions[1];
        Require(conditional.kind == TestPlayAniInstructionKind.Conditional &&
                conditional.condition.left.kind == TestPlayAniOperandKind.IntReference &&
                conditional.condition.left.referenceIndex == 10,
            "IF keeps an unresolved state reference", ref assertions);
        Require(conditional.thenInstructions.Count == 2 && conditional.elseInstructions.Count == 1,
            "IF branches are represented structurally", ref assertions);
        Require(conditional.thenInstructions[0].arguments[1].kind == TestPlayAniOperandKind.Stop,
            "STOP remains a typed operand", ref assertions);
        Require(conditional.thenInstructions[1].thenInstructions[0].arguments[0].rawText == "\"Pilot,Alpha\"",
            "quoted comma remains one typed argument", ref assertions);
        Require(program.instructions[2].kind == TestPlayAniInstructionKind.Command &&
                program.instructions[2].name == "UnknownProc",
            "unknown commands remain in the program", ref assertions);
    }

    static void VerifyRuntimeReferenceResolution(ref int assertions)
    {
        const string source = "IF(@int[5],==,1);Snd(1);ELSE;Snd(2);ENDIF;";
        TestPlayStateTable state = new TestPlayStateTable();
        TestPlayScriptVM vm = new TestPlayScriptVM(state);
        List<int> soundIds = new List<int>();
        vm.commandHandler = (name, args, raw) =>
        {
            if (string.Equals(name, "Snd", StringComparison.OrdinalIgnoreCase))
                soundIds.Add(args[0].AsInt());
        };

        TestPlayAniProgram first = vm.GetCompiledProgram(source);
        TestPlayAniProgram second = vm.GetCompiledProgram(source);
        Require(ReferenceEquals(first, second), "compiled ANI programs are cached", ref assertions);

        state.SetInt(5, 1);
        vm.Execute(source);
        state.SetInt(5, 0);
        vm.Execute(source);
        Require(soundIds.Count == 2 && soundIds[0] == 1 && soundIds[1] == 2,
            "state references resolve at execution time", ref assertions);

        bool stop = false;
        int commands = 0;
        vm.commandHandler = (name, args, raw) =>
        {
            commands++;
            if (name == "GoScriptIndex")
                stop = true;
        };
        vm.shouldStopExecution = () => stop;
        vm.Execute("Snd(1);GoScriptIndex(2);Voice(AfterJump);");
        Require(commands == 2, "typed execution preserves flow interruption", ref assertions);
    }

    static void VerifyCompilerDiagnostics(ref int assertions)
    {
        TestPlayAniProgram missingEndIf = TestPlayAniCompiler.Compile("IF(1,==,1);Snd(1);");
        Require(!missingEndIf.IsValid && HasDiagnostic(missingEndIf, "MissingEndIf"),
            "missing ENDIF is diagnosed", ref assertions);

        TestPlayAniProgram unexpectedElse = TestPlayAniCompiler.Compile("ELSE;Snd(1);");
        Require(!unexpectedElse.IsValid && HasDiagnostic(unexpectedElse, "UnexpectedElse"),
            "unmatched ELSE is diagnosed", ref assertions);

        TestPlayAniProgram malformedCommand = TestPlayAniCompiler.Compile("Snd(1;");
        Require(!malformedCommand.IsValid && HasDiagnostic(malformedCommand, "MalformedCommand"),
            "unbalanced command parentheses are diagnosed", ref assertions);
    }

    static void VerifyAnimationDataCompilation(ref int assertions)
    {
        animation source = new animation
        {
            name = "Phase1Animation",
            squirrelInit = "@int[0]=1;",
            scripts = new List<script>
            {
                new script { unk = 3, time = 0.5f, squirrel = "Move(0,0,0.1);" },
                new script { unk = 999999999, time = 0f, squirrel = "" }
            }
        };

        TestPlayAniAnimationProgram program = TestPlayAniAnimationCompiler.Compile(source, 7);
        Require(program.actionId == 7 && program.name == "Phase1Animation",
            "animation identity reaches the typed program", ref assertions);
        Require(program.initialProgram.instructions.Count == 1,
            "animation initial script is compiled separately", ref assertions);
        Require(program.blocks.Count == 2 && program.blocks[0].durationTicks == 3 &&
                Math.Abs(program.blocks[0].poseAdvancePerTick - 0.5f) < 0.000001f,
            "ANI block timing is retained", ref assertions);
        Require(program.blocks[1].sentinel && program.blocks[1].program == null,
            "999999999 remains an explicit sentinel", ref assertions);

        TestPlayAniTrackDefinition track = program.CreateTrack(TestPlayAniTrackKind.Main, 0, false);
        Require(track.blocks.Count == 2 && track.blocks[0].program.instructions[0].name == "Move",
            "typed animation creates a scheduler track without reparsing", ref assertions);
    }

    static void VerifyPreviewInterpreterUsesTypedProgram(ref int assertions)
    {
        scriptInterpreter interpreter = new scriptInterpreter
        {
            LogLines = false,
            LogUnknownSymbols = false
        };
        List<int> soundIds = new List<int>();
        interpreter.registerFunction("Snd", values => soundIds.Add((int)values[0].num));
        interpreter.registerStaticVariable("ONE", interpreter.convertFloat(1f));

        interpreter.runScript("IF(ONE,!=,0);Snd(1);ELSE;Snd(2);ENDIF;", true);
        TestPlayAniProgram firstProgram = interpreter.LastCompiledProgram;
        interpreter.runScript("IF(ONE,!=,0);Snd(1);ELSE;Snd(2);ENDIF;", true);
        Require(interpreter.LastCompiledProgram != null &&
                interpreter.LastCompiledProgram.instructions[0].kind == TestPlayAniInstructionKind.Conditional,
            "editor preview interpreter uses the shared typed program", ref assertions);
        Require(ReferenceEquals(firstProgram, interpreter.LastCompiledProgram),
            "editor preview caches the shared typed program", ref assertions);
        Require(soundIds.Count == 2 && soundIds[0] == 1 && soundIds[1] == 1,
            "editor preview supports all six original IF comparisons", ref assertions);

        interpreter.runScript("IF(0,==,1);UnknownA(1);ELSE;UnknownB(2);ENDIF;", false);
        Require(interpreter.calledFunctions.ContainsKey("UnknownA") &&
                interpreter.calledFunctions.ContainsKey("UnknownB"),
            "symbol collection visits both typed IF branches", ref assertions);
    }

    static void VerifyIndependentTrackScheduling(ref int assertions)
    {
        TestPlayAniTrackScheduler scheduler = new TestPlayAniTrackScheduler();
        scheduler.Configure(CreateTrack(TestPlayAniTrackKind.Main, 0, false, 2, 1));
        scheduler.Configure(CreateTrack(TestPlayAniTrackKind.Secondary, 1, false, 3));
        scheduler.Configure(CreateTrack(TestPlayAniTrackKind.Sub, 2, false, 4));
        scheduler.StartTrack(TestPlayAniTrackKind.Main);
        scheduler.StartTrack(TestPlayAniTrackKind.Secondary);

        List<string> executionOrder = new List<string>();
        Action<TestPlayAniTrackState, TestPlayAniBlockDefinition, TestPlayAniTrackExecutionReason> execute =
            (state, block, reason) => executionOrder.Add(state.kind + ":" + state.channel + ":" + block.index + ":" + reason);

        scheduler.Tick(execute);
        Require(executionOrder.Count == 2 &&
                executionOrder[0].StartsWith("Main:0:0", StringComparison.Ordinal) &&
                executionOrder[1].StartsWith("Secondary:1:0", StringComparison.Ordinal),
            "main and secondary tracks execute independently in stable order", ref assertions);
        Require(scheduler.GetState(TestPlayAniTrackKind.Main).remainingTicks == 1 &&
                scheduler.GetState(TestPlayAniTrackKind.Secondary).remainingTicks == 2,
            "track durations decrement independently", ref assertions);
        Require(!scheduler.GetState(TestPlayAniTrackKind.Sub).active,
            "configured sub track stays inactive until started", ref assertions);

        scheduler.Tick(execute);
        Require(scheduler.GetState(TestPlayAniTrackKind.Main).blockIndex == 1 &&
                scheduler.GetState(TestPlayAniTrackKind.Main).pendingEntry,
            "main track advances without moving the secondary track", ref assertions);
        Require(scheduler.GetState(TestPlayAniTrackKind.Secondary).blockIndex == 0,
            "secondary track remains on its own block", ref assertions);

        scheduler.Tick(execute);
        Require(scheduler.GetState(TestPlayAniTrackKind.Main).finished &&
                scheduler.GetState(TestPlayAniTrackKind.Secondary).finished,
            "sentinel ends non-looping tracks", ref assertions);
        Require(executionOrder.Contains("Main:0:1:EnterBlock"),
            "advanced main block executes on entry", ref assertions);
    }

    static void VerifyRepeatAndLoopScheduling(ref int assertions)
    {
        TestPlayAniTrackScheduler repeatScheduler = new TestPlayAniTrackScheduler();
        repeatScheduler.Configure(CreateTrack(TestPlayAniTrackKind.Main, 0, false, 7));
        repeatScheduler.StartTrack(TestPlayAniTrackKind.Main);
        List<TestPlayAniTrackExecutionReason> reasons = new List<TestPlayAniTrackExecutionReason>();
        repeatScheduler.Tick((state, block, reason) =>
        {
            reasons.Add(reason);
            if (reason == TestPlayAniTrackExecutionReason.EnterBlock)
                repeatScheduler.SetRepeat(TestPlayAniTrackKind.Main, 2);
        });
        repeatScheduler.Tick((state, block, reason) => reasons.Add(reason));
        repeatScheduler.Tick((state, block, reason) => reasons.Add(reason));
        Require(reasons.Count == 1, "ExecScriptEveryTime interval waits for two ticks", ref assertions);
        repeatScheduler.Tick((state, block, reason) => reasons.Add(reason));
        Require(reasons.Count == 2 && reasons[1] == TestPlayAniTrackExecutionReason.Repeat,
            "ExecScriptEveryTime(2) repeats on the third following tick", ref assertions);

        TestPlayAniTrackScheduler loopScheduler = new TestPlayAniTrackScheduler();
        loopScheduler.Configure(CreateTrack(TestPlayAniTrackKind.Main, 0, true, 1));
        loopScheduler.StartTrack(TestPlayAniTrackKind.Main);
        int executions = 0;
        loopScheduler.Tick((state, block, reason) => executions++);
        Require(loopScheduler.GetState(TestPlayAniTrackKind.Main).active &&
                loopScheduler.GetState(TestPlayAniTrackKind.Main).pendingEntry,
            "looping track returns to its first block", ref assertions);
        loopScheduler.Tick((state, block, reason) => executions++);
        Require(executions == 2 && loopScheduler.GetState(TestPlayAniTrackKind.Main).executionCount == 2,
            "looped block executes again on the next tick", ref assertions);

        loopScheduler.Jump(TestPlayAniTrackKind.Main, 999);
        Require(HasEvent(loopScheduler.Events, TestPlayAniSchedulerEventType.Diagnostic),
            "unknown block jumps produce diagnostics", ref assertions);
    }

    static void VerifyTraceSerialization(ref int assertions)
    {
        TestPlayAniTrackScheduler scheduler = new TestPlayAniTrackScheduler();
        scheduler.Configure(CreateTrack(TestPlayAniTrackKind.Main, 0, false, 2));
        scheduler.StartTrack(TestPlayAniTrackKind.Main);
        scheduler.Tick(null);
        TestPlayAniTickTrace trace = scheduler.CaptureTrace();
        string first = trace.ToJsonLine();
        string second = trace.ToJsonLine();

        Require(first == second, "tick trace serialization is deterministic", ref assertions);
        Require(first.Contains("\"tick\":1") && first.Contains("\"kind\":\"Main\"") &&
                first.Contains("\"block\":0") && first.Contains("\"remaining\":1"),
            "tick trace contains stable track state", ref assertions);
        Require(first.Contains("BlockEntered") && first.Contains("BlockExecuted"),
            "tick trace records ordered scheduler events", ref assertions);
    }

    static TestPlayAniTrackDefinition CreateTrack(
        TestPlayAniTrackKind kind,
        int channel,
        bool loop,
        params int[] durations)
    {
        TestPlayAniTrackDefinition definition = new TestPlayAniTrackDefinition
        {
            kind = kind,
            channel = channel,
            loop = loop
        };

        for (int i = 0; i < durations.Length; i++)
        {
            definition.blocks.Add(new TestPlayAniBlockDefinition
            {
                index = i,
                durationTicks = durations[i],
                program = TestPlayAniCompiler.Compile("Snd(" + i + ");")
            });
        }

        definition.blocks.Add(new TestPlayAniBlockDefinition
        {
            index = 999999999,
            sentinel = true
        });
        return definition;
    }

    static bool HasDiagnostic(TestPlayAniProgram program, string code)
    {
        for (int i = 0; i < program.diagnostics.Count; i++)
        {
            if (program.diagnostics[i].code == code)
                return true;
        }
        return false;
    }

    static bool HasEvent(IReadOnlyList<TestPlayAniSchedulerEvent> events, TestPlayAniSchedulerEventType type)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].type == type)
                return true;
        }
        return false;
    }

    static void Require(bool condition, string message, ref int assertions)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("[TestPlayPhase1Verification] " + message);
    }
}
