using System;
using System.Collections.Generic;
using System.Globalization;

public enum TestPlayAniInstructionKind
{
    Command,
    Assignment,
    Conditional
}

public enum TestPlayAniOperandKind
{
    Empty,
    Number,
    Symbol,
    Stop,
    IntReference,
    FloatReference
}

public sealed class TestPlayAniOperand
{
    public TestPlayAniOperandKind kind;
    public string rawText = "";
    public float number;
    public int referenceIndex = -1;

    public TestPlayScriptValue Resolve(TestPlayStateTable state)
    {
        switch (kind)
        {
            case TestPlayAniOperandKind.Number:
                return TestPlayScriptValue.Number(number);
            case TestPlayAniOperandKind.Stop:
                return TestPlayScriptValue.Stop();
            case TestPlayAniOperandKind.IntReference:
                return TestPlayScriptValue.Number(state != null ? state.GetInt(referenceIndex) : 0f);
            case TestPlayAniOperandKind.FloatReference:
                return TestPlayScriptValue.Number(state != null ? state.GetFloat(referenceIndex) : 0f);
            case TestPlayAniOperandKind.Symbol:
                return TestPlayScriptValue.Symbol(Unquote(rawText));
            default:
                return TestPlayScriptValue.Empty();
        }
    }

    static string Unquote(string value)
    {
        string text = value ?? "";
        if (text.Length >= 2 &&
            ((text[0] == '"' && text[text.Length - 1] == '"') ||
             (text[0] == '\'' && text[text.Length - 1] == '\'')))
            return text.Substring(1, text.Length - 2);
        return text;
    }
}

public sealed class TestPlayAniCondition
{
    public TestPlayAniOperand left;
    public string comparisonOperator = "";
    public TestPlayAniOperand right;
}

public sealed class TestPlayAniInstruction
{
    public TestPlayAniInstructionKind kind;
    public string name = "";
    public string assignmentOperator = "";
    public readonly List<TestPlayAniOperand> arguments = new List<TestPlayAniOperand>();
    public TestPlayAniCondition condition;
    public readonly List<TestPlayAniInstruction> thenInstructions = new List<TestPlayAniInstruction>();
    public readonly List<TestPlayAniInstruction> elseInstructions = new List<TestPlayAniInstruction>();
    public string rawText = "";
    public int sourceOrdinal;
}

public sealed class TestPlayAniDiagnostic
{
    public string code = "";
    public string message = "";
    public string rawText = "";
    public int sourceOrdinal;
}

public sealed class TestPlayAniProgram
{
    public string sourceText = "";
    public readonly List<TestPlayAniInstruction> instructions = new List<TestPlayAniInstruction>();
    public readonly List<TestPlayAniDiagnostic> diagnostics = new List<TestPlayAniDiagnostic>();

    public bool IsValid => diagnostics.Count == 0;
}

public static class TestPlayAniCompiler
{
    enum Terminator
    {
        EndOfProgram,
        Else,
        EndIf
    }

    public static TestPlayAniProgram Compile(string scriptText)
    {
        TestPlayAniProgram program = new TestPlayAniProgram { sourceText = scriptText ?? "" };
        List<string> statements = new List<string>();
        SplitStatements(program.sourceText, statements);
        int index = 0;
        Terminator terminator;
        ParseBlock(program, statements, ref index, program.instructions, false, out terminator);
        return program;
    }

    static void ParseBlock(
        TestPlayAniProgram program,
        List<string> statements,
        ref int index,
        List<TestPlayAniInstruction> output,
        bool stopAtTerminator,
        out Terminator terminator)
    {
        terminator = Terminator.EndOfProgram;
        while (index < statements.Count)
        {
            int ordinal = index;
            string statement = CleanLine(statements[index]);
            if (string.Equals(statement, "ELSE", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                if (stopAtTerminator)
                {
                    terminator = Terminator.Else;
                    return;
                }

                AddDiagnostic(program, "UnexpectedElse", "ELSE has no matching IF.", statement, ordinal);
                continue;
            }

            if (string.Equals(statement, "ENDIF", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                if (stopAtTerminator)
                {
                    terminator = Terminator.EndIf;
                    return;
                }

                AddDiagnostic(program, "UnexpectedEndIf", "ENDIF has no matching IF.", statement, ordinal);
                continue;
            }

            if (StartsWithCommand(statement, "IF"))
            {
                TestPlayAniInstruction conditional = new TestPlayAniInstruction
                {
                    kind = TestPlayAniInstructionKind.Conditional,
                    name = "IF",
                    rawText = statement,
                    sourceOrdinal = ordinal,
                    condition = ParseCondition(program, statement, ordinal)
                };
                index++;

                Terminator branchTerminator;
                ParseBlock(program, statements, ref index, conditional.thenInstructions, true, out branchTerminator);
                if (branchTerminator == Terminator.Else)
                {
                    ParseBlock(program, statements, ref index, conditional.elseInstructions, true, out branchTerminator);
                }

                if (branchTerminator != Terminator.EndIf)
                {
                    AddDiagnostic(program, "MissingEndIf", "IF has no matching ENDIF.", statement, ordinal);
                }

                output.Add(conditional);
                continue;
            }

            index++;
            if (string.IsNullOrEmpty(statement))
                continue;

            output.Add(ParseExecutable(program, statement, ordinal));
        }
    }

    static TestPlayAniInstruction ParseExecutable(TestPlayAniProgram program, string line, int ordinal)
    {
        string op;
        int opIndex;
        if (TryFindAssignment(line, out op, out opIndex))
        {
            string left = line.Substring(0, opIndex).Trim();
            string right = line.Substring(opIndex + op.Length).Trim();
            if (right.StartsWith("(", StringComparison.Ordinal) && right.EndsWith(")", StringComparison.Ordinal))
                right = right.Substring(1, right.Length - 2);

            TestPlayAniInstruction assignment = new TestPlayAniInstruction
            {
                kind = TestPlayAniInstructionKind.Assignment,
                name = left,
                assignmentOperator = op,
                rawText = line,
                sourceOrdinal = ordinal
            };
            ParseOperands(right, assignment.arguments);
            return assignment;
        }

        int paren = line.IndexOf('(');
        int close = line.LastIndexOf(')');
        TestPlayAniInstruction command = new TestPlayAniInstruction
        {
            kind = TestPlayAniInstructionKind.Command,
            name = paren > 0 ? line.Substring(0, paren).Trim() : line,
            rawText = line,
            sourceOrdinal = ordinal
        };

        if (paren > 0 && close > paren)
        {
            ParseOperands(line.Substring(paren + 1, close - paren - 1), command.arguments);
        }
        else if (paren >= 0 || close >= 0)
        {
            AddDiagnostic(program, "MalformedCommand", "Command parentheses are not balanced.", line, ordinal);
        }

        return command;
    }

    static TestPlayAniCondition ParseCondition(TestPlayAniProgram program, string line, int ordinal)
    {
        int paren = line.IndexOf('(');
        int close = line.LastIndexOf(')');
        if (paren < 0 || close <= paren)
        {
            AddDiagnostic(program, "MalformedIf", "IF parentheses are not balanced.", line, ordinal);
            return null;
        }

        List<string> tokens = SplitArgs(line.Substring(paren + 1, close - paren - 1));
        if (tokens.Count < 3)
        {
            AddDiagnostic(program, "MalformedIf", "IF requires left, operator, and right arguments.", line, ordinal);
            return null;
        }

        return new TestPlayAniCondition
        {
            left = ParseOperand(tokens[0]),
            comparisonOperator = tokens[1].Trim(),
            right = ParseOperand(tokens[2])
        };
    }

    static void ParseOperands(string text, List<TestPlayAniOperand> output)
    {
        List<string> tokens = SplitArgs(text);
        for (int i = 0; i < tokens.Count; i++)
            output.Add(ParseOperand(tokens[i]));
    }

    static TestPlayAniOperand ParseOperand(string token)
    {
        string text = (token ?? "").Trim();
        TestPlayAniOperand operand = new TestPlayAniOperand { rawText = text };
        if (text.Length == 0)
        {
            operand.kind = TestPlayAniOperandKind.Empty;
            return operand;
        }

        if (string.Equals(text, "STOP", StringComparison.OrdinalIgnoreCase))
        {
            operand.kind = TestPlayAniOperandKind.Stop;
            return operand;
        }

        int referenceIndex;
        if (TryParseReference(text, "@int", out referenceIndex))
        {
            operand.kind = TestPlayAniOperandKind.IntReference;
            operand.referenceIndex = referenceIndex;
            return operand;
        }

        if (TryParseReference(text, "@float", out referenceIndex))
        {
            operand.kind = TestPlayAniOperandKind.FloatReference;
            operand.referenceIndex = referenceIndex;
            return operand;
        }

        float number;
        if (float.TryParse(NormalizeFloatLiteral(text), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            operand.kind = TestPlayAniOperandKind.Number;
            operand.number = number;
            return operand;
        }

        operand.kind = TestPlayAniOperandKind.Symbol;
        return operand;
    }

    static bool TryParseReference(string token, string prefix, out int index)
    {
        index = -1;
        if (!token.StartsWith(prefix + "[", StringComparison.Ordinal) || !token.EndsWith("]", StringComparison.Ordinal))
            return false;

        string raw = token.Substring(prefix.Length + 1, token.Length - prefix.Length - 2);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) &&
               index >= 0 && index < TestPlayStateTable.ScriptVariableCount;
    }

    static string NormalizeFloatLiteral(string value)
    {
        string text = (value ?? "").Trim();
        return text.EndsWith("f", StringComparison.OrdinalIgnoreCase)
            ? text.Substring(0, text.Length - 1)
            : text;
    }

    static List<string> SplitArgs(string argsText)
    {
        List<string> args = new List<string>();
        if (string.IsNullOrWhiteSpace(argsText))
            return args;

        int start = 0;
        bool inQuote = false;
        char quote = '\0';
        for (int i = 0; i < argsText.Length; i++)
        {
            char c = argsText[i];
            if ((c == '"' || c == '\'') && (i == 0 || argsText[i - 1] != '\\'))
            {
                if (!inQuote)
                {
                    inQuote = true;
                    quote = c;
                }
                else if (quote == c)
                {
                    inQuote = false;
                }
            }
            else if (c == ',' && !inQuote)
            {
                args.Add(argsText.Substring(start, i - start).Trim());
                start = i + 1;
            }
        }

        args.Add(argsText.Substring(start).Trim());
        return args;
    }

    static void SplitStatements(string scriptText, List<string> output)
    {
        if (string.IsNullOrEmpty(scriptText))
            return;

        int start = 0;
        bool inDoubleQuote = false;
        bool inSingleQuote = false;
        bool inComment = false;
        int parenthesisDepth = 0;

        for (int i = 0; i < scriptText.Length; i++)
        {
            char c = scriptText[i];
            if (inComment)
            {
                if (c == '\r' || c == '\n')
                {
                    inComment = false;
                    start = i + 1;
                }
                continue;
            }

            if (c == '"' && !inSingleQuote && (i == 0 || scriptText[i - 1] != '\\'))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (c == '\'' && !inDoubleQuote && (i == 0 || scriptText[i - 1] != '\\'))
            {
                if (!inSingleQuote && parenthesisDepth == 0)
                {
                    AddStatement(scriptText, start, i, output);
                    inComment = true;
                    start = i + 1;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (inDoubleQuote || inSingleQuote)
                continue;

            if (c == '(')
                parenthesisDepth++;
            else if (c == ')' && parenthesisDepth > 0)
                parenthesisDepth--;

            if (c == ';' || c == '\r' || c == '\n')
            {
                AddStatement(scriptText, start, i, output);
                start = i + 1;
            }
        }

        if (!inComment)
            AddStatement(scriptText, start, scriptText.Length, output);
    }

    static void AddStatement(string text, int start, int end, List<string> output)
    {
        if (end <= start)
            return;

        string statement = text.Substring(start, end - start).Trim();
        if (!string.IsNullOrEmpty(statement))
            output.Add(statement);
    }

    static string CleanLine(string rawLine)
    {
        string line = (rawLine ?? "").Trim();
        if (line.EndsWith(";", StringComparison.Ordinal))
            line = line.Substring(0, line.Length - 1).Trim();
        return line;
    }

    static bool StartsWithCommand(string line, string command)
    {
        if (!line.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return false;

        int index = command.Length;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;
        return index < line.Length && line[index] == '(';
    }

    static bool TryFindAssignment(string line, out string op, out int index)
    {
        int firstParen = line.IndexOf('(');
        string[] operators = { "+=", "-=", "*=", "/=", "=" };
        for (int i = 0; i < operators.Length; i++)
        {
            index = line.IndexOf(operators[i], StringComparison.Ordinal);
            if (index < 0)
                continue;
            if (firstParen >= 0 && index > firstParen)
                continue;
            if (operators[i] == "=" && IsComparisonEquals(line, index))
                continue;
            op = operators[i];
            return true;
        }

        op = "";
        index = -1;
        return false;
    }

    static bool IsComparisonEquals(string line, int index)
    {
        if (index > 0 && (line[index - 1] == '=' || line[index - 1] == '!' || line[index - 1] == '<' || line[index - 1] == '>'))
            return true;
        return index + 1 < line.Length && line[index + 1] == '=';
    }

    static void AddDiagnostic(TestPlayAniProgram program, string code, string message, string rawText, int ordinal)
    {
        program.diagnostics.Add(new TestPlayAniDiagnostic
        {
            code = code,
            message = message,
            rawText = rawText ?? "",
            sourceOrdinal = ordinal
        });
    }
}

public static class TestPlayAniExecutor
{
    public static void Execute(
        TestPlayAniProgram program,
        TestPlayStateTable state,
        TestPlayCommandHandler commandHandler,
        TestPlayAssignmentHandler assignmentHandler,
        Action<string> diagnosticHandler,
        Func<bool> shouldStopExecution)
    {
        if (program == null)
            return;

        for (int i = 0; i < program.diagnostics.Count; i++)
        {
            TestPlayAniDiagnostic diagnostic = program.diagnostics[i];
            diagnosticHandler?.Invoke(diagnostic.code + ": " + diagnostic.rawText);
        }

        ExecuteList(program.instructions, state, commandHandler, assignmentHandler, diagnosticHandler, shouldStopExecution);
    }

    static bool ExecuteList(
        List<TestPlayAniInstruction> instructions,
        TestPlayStateTable state,
        TestPlayCommandHandler commandHandler,
        TestPlayAssignmentHandler assignmentHandler,
        Action<string> diagnosticHandler,
        Func<bool> shouldStopExecution)
    {
        for (int i = 0; i < instructions.Count; i++)
        {
            TestPlayAniInstruction instruction = instructions[i];
            if (instruction.kind == TestPlayAniInstructionKind.Conditional)
            {
                bool result = EvaluateCondition(instruction.condition, state, diagnosticHandler, instruction.rawText);
                List<TestPlayAniInstruction> branch = result ? instruction.thenInstructions : instruction.elseInstructions;
                if (!ExecuteList(branch, state, commandHandler, assignmentHandler, diagnosticHandler, shouldStopExecution))
                    return false;
            }
            else
            {
                List<TestPlayScriptValue> values = Resolve(instruction.arguments, state);
                if (instruction.kind == TestPlayAniInstructionKind.Assignment)
                {
                    float numberValue = values.Count > 0 ? values[0].AsFloat() : 0f;
                    if (state == null || !state.TryApplyReferenceAssignment(instruction.name, instruction.assignmentOperator, numberValue))
                    {
                        assignmentHandler?.Invoke(
                            instruction.name,
                            instruction.assignmentOperator,
                            values,
                            instruction.rawText);
                    }
                }
                else
                {
                    commandHandler?.Invoke(instruction.name, values, instruction.rawText);
                }
            }

            if (shouldStopExecution != null && shouldStopExecution())
                return false;
        }

        return true;
    }

    static bool EvaluateCondition(
        TestPlayAniCondition condition,
        TestPlayStateTable state,
        Action<string> diagnosticHandler,
        string rawText)
    {
        if (condition == null)
            return false;

        float left = condition.left.Resolve(state).AsFloat();
        float right = condition.right.Resolve(state).AsFloat();
        switch ((condition.comparisonOperator ?? "").Trim())
        {
            case "==": return Approximately(left, right);
            case "!=": return !Approximately(left, right);
            case ">=": return left >= right;
            case "<=": return left <= right;
            case ">": return left > right;
            case "<": return left < right;
            default:
                diagnosticHandler?.Invoke("IF operator: " + condition.comparisonOperator + " raw=" + rawText);
                return false;
        }
    }

    static List<TestPlayScriptValue> Resolve(List<TestPlayAniOperand> operands, TestPlayStateTable state)
    {
        List<TestPlayScriptValue> values = new List<TestPlayScriptValue>(operands.Count);
        for (int i = 0; i < operands.Count; i++)
            values.Add(operands[i].Resolve(state));
        return values;
    }

    static bool Approximately(float left, float right)
    {
        float difference = Math.Abs(left - right);
        float scale = Math.Max(1f, Math.Max(Math.Abs(left), Math.Abs(right)));
        return difference <= 0.000001f * scale;
    }
}
