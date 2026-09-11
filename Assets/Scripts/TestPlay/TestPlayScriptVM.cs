using System;
using System.Collections.Generic;

public delegate void TestPlayCommandHandler(string name, List<TestPlayScriptValue> args, string rawLine);
public delegate void TestPlayAssignmentHandler(string name, string op, List<TestPlayScriptValue> values, string rawLine);

public class TestPlayScriptVM
{
    public TestPlayStateTable state;
    public TestPlayCommandHandler commandHandler;
    public TestPlayAssignmentHandler assignmentHandler;
    public Action<string> unhandledLineHandler;
    public Func<bool> shouldStopExecution;

    readonly Dictionary<string, TestPlayAniProgram> compiledScripts =
        new Dictionary<string, TestPlayAniProgram>();

    public TestPlayScriptVM(TestPlayStateTable state)
    {
        this.state = state;
    }

    public void Execute(string scriptText)
    {
        if (string.IsNullOrEmpty(scriptText))
            return;

        TestPlayAniProgram program = GetCompiledProgram(scriptText);
        TestPlayAniExecutor.Execute(
            program,
            state,
            commandHandler,
            assignmentHandler,
            unhandledLineHandler,
            shouldStopExecution);
    }

    public void Compile(string scriptText)
    {
        if (string.IsNullOrEmpty(scriptText) || compiledScripts.ContainsKey(scriptText))
            return;

        compiledScripts.Add(scriptText, TestPlayAniCompiler.Compile(scriptText));
    }

    public TestPlayAniProgram GetCompiledProgram(string scriptText)
    {
        if (string.IsNullOrEmpty(scriptText))
            return TestPlayAniCompiler.Compile("");

        Compile(scriptText);
        return compiledScripts[scriptText];
    }

    public void ClearCompiledScripts()
    {
        compiledScripts.Clear();
    }
}
