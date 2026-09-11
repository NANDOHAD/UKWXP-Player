using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
public sealed class WindomAniInventoryReport
{
    public int schemaVersion = 1, completedFiles, totalFiles;
    public string state = "Running", currentFile, firstFailure = "";
    public string scope = "Static RunProc/RunProc2 declarations; no script execution or original-EXE parity";
    public List<WindomAniInventoryFile> files = new List<WindomAniInventoryFile>();
    public List<WindomAniInventoryRow> rows = new List<WindomAniInventoryRow>();
    public List<string> diagnostics = new List<string>();
}

[Serializable]
public sealed class WindomAniInventoryFile
{
    public string path, sha256, format;
    public int animations, scannedActions;
    public bool completed;
}

[Serializable]
public sealed class WindomAniInventoryRow
{
    public string file, aniHash, actionName, blockKind, branch, command, rawText;
    public int action, block, sourceOrdinal, duration, type = -1, subtype = -1, weaponPoint = -1;
    public float poseAdvance;
    public bool typeLiteral, subtypeLiteral, weaponPointLiteral, timedBlock, blockHasDiagnostics;
    public string[] arguments;
    public string p4, p5, p6, p7, p8, p9, p10, p11;
    public string occurrenceKey, patternKey;
}

/// <summary>Read-only public-loader inventory. Coordinates remain source positions, not runtime invocation counts.</summary>
public static class WindomAniInventory
{
    public static string[] DefaultFiles()
    {
        string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Windom_Data", "Robo");
        return new[] { "ELS_QT", "ガンダムTR-1ヘイズル改", "ザクIIS型" }
            .Select(name => Path.Combine(root, name, "Script.ani")).ToArray();
    }

    internal static async Task<int> RunAsync(string outputFolder, CancellationToken token,
        Func<int, int, string, Task> progress, string[] selectedFiles = null)
    {
        string[] files = (selectedFiles ?? DefaultFiles()).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var report = new WindomAniInventoryReport { totalFiles = files.Length };
        Directory.CreateDirectory(outputFolder);
        try
        {
            await SaveAsync(report, outputFolder);
            if (files.Length == 0) throw new ArgumentException("Inventory requires at least one file.");
            foreach (string path in files)
            {
                token.ThrowIfCancellationRequested();
                report.currentFile = path;
                if (progress != null) await progress(report.completedFiles, report.totalFiles, "Loading: " + path);
                if (!File.Exists(path)) throw new FileNotFoundException("ANI inventory input is missing.", path);
                string hash = await Task.Run(() => WindomGoldenBaseline.FileHash(path), token);
                var record = new WindomAniInventoryFile { path = path, sha256 = hash };
                report.files.Add(record);
                await SaveAsync(report, outputFolder);
                var data = new ani2();
                // The parser has no cancellation token. Await completion before observing cancellation/releasing data.
                if (!await data.load(path)) throw new InvalidDataException("ani2.load returned false: " + path);
                token.ThrowIfCancellationRequested();
                if (data.animations == null) throw new InvalidDataException("ANI has no animation collection: " + path);
                record.animations = data.animations.Count;
                record.format = data.sourceFormat.ToString();
                for (int action = 0; action < data.animations.Count; action++)
                {
                    token.ThrowIfCancellationRequested();
                    CollectAnimation(report, record, data.animations[action], action);
                    record.scannedActions = action + 1;
                    if ((action + 1) % 20 == 0)
                    {
                        if (progress != null) await progress(report.completedFiles, report.totalFiles, path + " action " + (action + 1) + "/" + data.animations.Count);
                        await SaveAsync(report, outputFolder);
                    }
                    await Task.Yield();
                }
                if (await Task.Run(() => WindomGoldenBaseline.FileHash(path), token) != hash)
                    throw new InvalidDataException("Input changed during inventory: " + path);
                record.completed = true;
                report.completedFiles++;
                await SaveAsync(report, outputFolder);
                if (progress != null) await progress(report.completedFiles, report.totalFiles, "Completed: " + path);
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
            report.state = "Succeeded";
            return report.completedFiles;
        }
        catch (OperationCanceledException) { report.state = "Cancelled"; throw; }
        catch (Exception ex) { report.state = "Failed"; report.firstFailure = ex.ToString(); throw; }
        finally { await SaveAsync(report, outputFolder); }
    }

    internal static void CollectAnimation(WindomAniInventoryReport report, WindomAniInventoryFile file, animation source, int action)
    {
        if (source == null) { report.diagnostics.Add(file.path + " action " + action + ": null animation"); return; }
        CollectBlock(report, file, source.name, action, -1, "Initial", 0, 0, source.squirrelInit);
        if (source.scripts == null) return;
        for (int block = 0; block < source.scripts.Count; block++)
        {
            var s = source.scripts[block];
            CollectBlock(report, file, source.name, action, block, s.unk == 999999999 ? "Sentinel" : "Timed", s.unk, s.time, s.squirrel);
        }
    }

    static void CollectBlock(WindomAniInventoryReport report, WindomAniInventoryFile file, string actionName,
        int action, int block, string kind, int duration, float poseAdvance, string text)
    {
        // Includes initial/sentinel text for inventory, explicitly labelled as not a timed execution block.
        var program = TestPlayAniCompiler.Compile(text);
        foreach (var diagnostic in program.diagnostics)
            report.diagnostics.Add(file.path + " action " + action + " " + kind + "/" + block +
                " statement " + diagnostic.sourceOrdinal + ": " + diagnostic.code + " " + diagnostic.rawText);
        int startRow = report.rows.Count;
        CollectInstructions(program.instructions, "root", report, file, actionName, action, block, kind, duration, poseAdvance);
        for (int i = startRow; i < report.rows.Count; i++) report.rows[i].blockHasDiagnostics = program.diagnostics.Count != 0;
    }

    static void CollectInstructions(List<TestPlayAniInstruction> instructions, string branch, WindomAniInventoryReport report,
        WindomAniInventoryFile file, string actionName, int action, int block, string kind, int duration, float poseAdvance)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.kind == TestPlayAniInstructionKind.Conditional)
            {
                string condition = branch + "/if" + instruction.sourceOrdinal;
                CollectInstructions(instruction.thenInstructions, condition + "/then", report, file, actionName, action, block, kind, duration, poseAdvance);
                CollectInstructions(instruction.elseInstructions, condition + "/else", report, file, actionName, action, block, kind, duration, poseAdvance);
            }
            bool extended = string.Equals(instruction.name, "RunProc2", StringComparison.OrdinalIgnoreCase);
            if (instruction.kind != TestPlayAniInstructionKind.Command || (!extended && !string.Equals(instruction.name, "RunProc", StringComparison.OrdinalIgnoreCase))) continue;
            string[] args = instruction.arguments.Select(a => a.rawText).ToArray();
            var row = new WindomAniInventoryRow {
                file = file.path, aniHash = file.sha256, actionName = actionName, action = action, block = block, blockKind = kind,
                branch = branch, sourceOrdinal = instruction.sourceOrdinal, command = extended ? "RunProc2" : "RunProc",
                rawText = instruction.rawText, arguments = args, timedBlock = kind == "Timed", duration = duration, poseAdvance = poseAdvance,
                p4 = Arg(args, 4), p5 = Arg(args, 5), p6 = Arg(args, 6), p7 = Arg(args, 7),
                p8 = Arg(args, 8), p9 = Arg(args, 9), p10 = Arg(args, 10), p11 = Arg(args, 11)
            };
            row.typeLiteral = LiteralInt(instruction, 1, out row.type);
            row.weaponPointLiteral = LiteralInt(instruction, 2, out row.weaponPoint);
            row.subtypeLiteral = extended && row.typeLiteral && row.type == 62 && LiteralInt(instruction, 3, out row.subtype);
            // Location key preserves distinct blocks/branches/actions. Pattern key groups normalized literal arguments across files.
            row.occurrenceKey = TestPlayGoldenTraceSession.ComputeSha256(string.Join("|", file.sha256, action.ToString(CultureInfo.InvariantCulture),
                kind, block.ToString(CultureInfo.InvariantCulture), branch, instruction.sourceOrdinal.ToString(CultureInfo.InvariantCulture)));
            string[] canonical = instruction.arguments.Select(a => a.kind == TestPlayAniOperandKind.Number
                ? "Number:" + a.number.ToString("R", CultureInfo.InvariantCulture) : a.kind + ":" + a.rawText).ToArray();
            row.patternKey = TestPlayGoldenTraceSession.ComputeSha256(row.command + "|" + string.Join("|", canonical.Select(s => s.Length + ":" + s)));
            report.rows.Add(row);
        }
    }

    static bool LiteralInt(TestPlayAniInstruction instruction, int index, out int number)
    {
        number = -1;
        if (index >= instruction.arguments.Count) return false;
        var a = instruction.arguments[index];
        if (a.kind != TestPlayAniOperandKind.Number || float.IsNaN(a.number) || float.IsInfinity(a.number) ||
            (double)a.number < int.MinValue || (double)a.number > int.MaxValue || Math.Truncate((double)a.number) != a.number) return false;
        number = (int)a.number;
        return true;
    }

    static string Arg(string[] args, int index) => index < args.Length ? args[index] : null;
    internal static async Task SaveAsync(WindomAniInventoryReport report, string folder)
    {
        await WindomVerificationStorage.WriteAtomicAsync(Path.Combine(folder, "inventory.json"), JsonUtility.ToJson(report, true));
        var csv = new StringBuilder("file,aniHash,action,actionName,blockKind,block,sourceOrdinal,branch,timedBlock,command,type,weaponPoint,subtype,p4,p5,p6,p7,p8,p9,p10,p11,occurrenceKey,patternKey,rawText\n");
        foreach (var r in report.rows)
            csv.AppendLine(string.Join(",", new[] { r.file, r.aniHash, r.action.ToString(CultureInfo.InvariantCulture), r.actionName, r.blockKind,
                r.block.ToString(CultureInfo.InvariantCulture), r.sourceOrdinal.ToString(CultureInfo.InvariantCulture), r.branch, r.timedBlock.ToString(), r.command,
                r.typeLiteral ? r.type.ToString(CultureInfo.InvariantCulture) : Arg(r.arguments, 1),
                r.weaponPointLiteral ? r.weaponPoint.ToString(CultureInfo.InvariantCulture) : Arg(r.arguments, 2),
                r.subtypeLiteral ? r.subtype.ToString(CultureInfo.InvariantCulture) : "", r.p4, r.p5, r.p6, r.p7, r.p8, r.p9, r.p10, r.p11,
                r.occurrenceKey, r.patternKey, r.rawText }.Select(Csv)));
        await WindomVerificationStorage.WriteAtomicAsync(Path.Combine(folder, "inventory.csv"), csv.ToString());
    }

    static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
