using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Behavioral probes for immutable baseline comparison and cooperative inventory jobs.</summary>
public static class WindomP103Verification
{
    public static bool IsRunning { get; private set; }
    public static string LastResult { get; private set; } = "Not run";
    public static string OutputFolder { get; private set; }
    static int assertions;

    [MenuItem("Tools/WindomXP/Verification/Verify Baseline and Inventory")]
    public static async void Run()
    {
        if (IsRunning || WindomVerificationRunner.IsRunning) throw new InvalidOperationException("A verification is active.");
        IsRunning = true;
        assertions = 0;
        LastResult = "Running";
        try
        {
            string trace = File.ReadAllText(Path.Combine(WindomGoldenBaseline.BaselineRoot, WindomGoldenBaseline.ApprovedBaselineId, "GT-001.unity-reference.jsonl"));
            Require(WindomGoldenBaseline.CompareTexts(trace, trace, "GT-001").state == "Match", "identical trace matches");
            string[] lines = trace.Split('\n');
            var tick = JObject.Parse(lines[1]);
            tick["logicalAction"] = 12345;
            lines[1] = tick.ToString(Newtonsoft.Json.Formatting.None);
            string wrong = string.Join("\n", lines);
            Require(TestPlayGoldenTraceSession.TryParseJsonLines(wrong, out var wrongA, out _) &&
                TestPlayGoldenTraceSession.TryParseJsonLines(wrong, out var wrongB, out _) &&
                TestPlayGoldenTraceSession.FindFirstMismatch(wrongA, wrongB) == -1, "same wrong behavior is deterministic");
            var difference = WindomGoldenBaseline.CompareTexts(trace, wrong, "GT-001");
            Require(difference.state == "Mismatch" && difference.firstMismatchIndex == 0 && difference.actualTick.Contains("12345"), "approved reference catches deterministic regression and saves first mismatch");
            foreach (var change in new[] {
                new { key = "schemaVersion", value = (JToken)2 }, new { key = "tickRate", value = (JToken)30 },
                new { key = "scenario", value = (JToken)"GT-002" }, new { key = "aniHash", value = (JToken)new string('a', 64) },
                new { key = "sptHash", value = (JToken)new string('b', 64) }, new { key = "source", value = (JToken)"original-exe" },
                new { key = "mechId", value = (JToken)"different" }, new { key = "baseline", value = (JToken)"UnityReference" }
            })
            {
                lines = trace.Split('\n');
                var header = JObject.Parse(lines[0]);
                header["session"][change.key] = change.value;
                lines[0] = header.ToString(Newtonsoft.Json.Formatting.None);
                Require(WindomGoldenBaseline.CompareTexts(trace, string.Join("\n", lines), "GT-001").state == "ContractMismatch", "reject changed " + change.key);
            }
            lines = trace.Split('\n');
            tick = JObject.Parse(lines[1]); tick["input"]["shot"] = true;
            lines[1] = tick.ToString(Newtonsoft.Json.Formatting.None);
            Require(WindomGoldenBaseline.CompareTexts(trace, string.Join("\n", lines), "GT-001").state == "ContractMismatch", "input change is not behavior parity");
            Require(WindomGoldenBaseline.CompareTexts(trace, trace.Substring(0, trace.LastIndexOf('\n')), "GT-001").state == "ContractMismatch", "truncation rejected");
            Require(WindomGoldenBaseline.CompareTexts(trace, "{broken}", "GT-001").state == "ContractMismatch", "malformed trace rejected");
            var definition = TestPlayGoldenScenarioCatalog.Find("GT-001");
            var changedSetup = new TestPlayGoldenScenarioDefinition {
                id = definition.id, baseline = definition.baseline, setup = TestPlayGoldenSetupKind.AirborneGun,
                requiredActionIds = definition.requiredActionIds, requiredCommands = definition.requiredCommands,
                inputSegments = definition.inputSegments, targetPosition = definition.targetPosition
            };
            Require(WindomGoldenBaseline.ScenarioHash(definition) != WindomGoldenBaseline.ScenarioHash(changedSetup), "setup hash is not an empty DTO hash");
            bool rejected = false;
            try { WindomVerificationRunner.StartVerification(WindomVerificationSelection.BaselineComparison); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "baseline comparison requires generated Golden traces");
            rejected = false;
            try { WindomGoldenBaseline.CreateFromReviewedRun("unused", "../escape", "probe"); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "promotion ID cannot escape baseline root");

            var report = new WindomAniInventoryReport();
            var file = new WindomAniInventoryFile { path = "fixture.ani", sha256 = new string('c', 64) };
            var animation = new animation {
                name = "quoted,\"name\"", squirrelInit = "RunProc2(0,62,7,11,3,0,0,0,0,0,0,0)",
                scripts = new System.Collections.Generic.List<script> {
                    new script { unk = 1, time = 1, squirrel = "IF(@int[1],==,0)\nRunProc2(0,62,7,11,3,0,0,0,0,0,0,0)\nELSE\nRunProc2(0,62,@int[2],11,@float[3])\nENDIF" },
                    new script { unk = 999999999, squirrel = "RunProc2(0,62,7,11,3,0,0,0,0,0,0,0)" }
                }
            };
            WindomAniInventory.CollectAnimation(report, file, animation, 42);
            Require(report.rows.Count == 4 && report.diagnostics.Count == 0, "both conditional branches plus initial and sentinel are inventoried");
            Require(report.rows.Select(r => r.occurrenceKey).Distinct().Count() == 4, "distinct source locations never collapse");
            Require(report.rows.Select(r => r.patternKey).Distinct().Count() == 2, "identical patterns group across locations");
            var dynamicRow = report.rows.Single(r => !r.weaponPointLiteral);
            Require(dynamicRow.weaponPoint == -1 && dynamicRow.p4 == "@float[3]" && dynamicRow.p11 == null, "dynamic/missing arguments remain unresolved");
            Require(report.rows.Count(r => r.timedBlock) == 2, "initial/sentinel rows are not timed execution evidence");

            string id = WindomVerificationRunner.StartTestJob(async (job, token) => {
                OutputFolder = job.outputFolder;
                await WindomAniInventory.SaveAsync(report, job.outputFolder);
                Require(File.ReadAllText(Path.Combine(job.outputFolder, "inventory.csv")).Contains("quoted,\"\"name\"\""), "CSV quotes round-trip");
                string lockFile = Path.Combine(job.outputFolder, "locked-result.json");
                File.WriteAllText(lockFile, "before");
                Task write;
                using (var reader = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    write = WindomVerificationStorage.WriteAtomicAsync(lockFile, "after");
                    await Task.Delay(100);
                    Require(!write.IsCompleted && File.ReadAllText(lockFile) == "before", "locked result retains old contents while yielding");
                }
                await write;
                Require(File.ReadAllText(lockFile) == "after", "replacement resumes after reader releases file");
                await WindomAniInventory.RunAsync(Path.Combine(job.outputFolder, "cancel-probe"), token,
                    (completed, total, item) => { if (completed == 1) WindomVerificationRunner.CancelVerification(job.runId); return Task.CompletedTask; });
            });
            var result = await Wait(id);
            var partial = JsonUtility.FromJson<WindomAniInventoryReport>(File.ReadAllText(Path.Combine(result.outputFolder, "cancel-probe", "inventory.json")));
            Require(result.state == "Cancelled" && partial.state == "Cancelled" && partial.completedFiles == 1 && partial.files[0].completed, "cancel after first real file preserves completed partial results");
            Require(partial.rows.Count > 0, "partial rows persisted");
            id = WindomVerificationRunner.StartTestJob(async (job, token) => {
                await WindomAniInventory.RunAsync(Path.Combine(job.outputFolder, "missing-probe"), token, null,
                    new[] { Path.Combine(job.outputFolder, "missing.ani") });
            });
            result = await Wait(id);
            var failed = JsonUtility.FromJson<WindomAniInventoryReport>(File.ReadAllText(Path.Combine(result.outputFolder, "missing-probe", "inventory.json")));
            Require(result.state == "Failed" && failed.state == "Failed" && failed.completedFiles == 0 && !string.IsNullOrEmpty(failed.firstFailure), "missing input is a structured failure");
            id = WindomVerificationRunner.StartTestJob(async (job, token) => {
                int completed = await WindomAniInventory.RunAsync(Path.Combine(job.outputFolder, "retry"), token, null);
                Require(completed == 3, "three-file retry after cancellation/failure succeeds");
            });
            result = await Wait(id);
            Require(result.state == "Succeeded", "retry has a successful terminal job: " + result.firstFailure);
            // Copy generated candidates only. The approved baseline remains untouched throughout failure probes.
            id = WindomVerificationRunner.StartTestJob(async (job, token) => {
                string folder = Path.Combine(job.outputFolder, "golden");
                Directory.CreateDirectory(folder);
                foreach (string source in Directory.GetFiles(Path.Combine(WindomGoldenBaseline.BaselineRoot, WindomGoldenBaseline.ApprovedBaselineId), "*.jsonl"))
                    File.Copy(source, Path.Combine(folder, Path.GetFileName(source)));
                File.WriteAllText(Path.Combine(folder, "GT-001.unity-reference.jsonl"), wrong);
                await WindomGoldenBaseline.CompareRunAsync(job.outputFolder, token);
            });
            result = await Wait(id);
            var mismatchReport = JsonUtility.FromJson<WindomBaselineReport>(File.ReadAllText(Path.Combine(result.outputFolder, "baseline-comparison.json")));
            Require(result.state == "Failed" && mismatchReport.comparisons.Count == 10 && mismatchReport.comparisons[0].state == "Mismatch", "actual comparison job fails and persists mismatch without baseline mutation");
            LastResult = assertions + " assertions passed.";
            File.WriteAllText(Path.Combine(OutputFolder, "p1-03-result.txt"), LastResult);
            Debug.Log("[P1-03Verification] " + LastResult + " Output=" + OutputFolder);
        }
        catch (Exception ex) { LastResult = "Failed: " + ex; Debug.LogError(LastResult); }
        finally { IsRunning = false; }
    }

    static async Task<WindomVerificationResult> Wait(string id)
    {
        double deadline = EditorApplication.timeSinceStartup + 180;
        while (WindomVerificationRunner.IsRunning)
        {
            if (EditorApplication.timeSinceStartup > deadline) { WindomVerificationRunner.CancelVerification(id); throw new TimeoutException("P1-03 probe timed out; cancellation requested."); }
            await Task.Yield();
        }
        return WindomVerificationRunner.GetVerificationStatus(id);
    }
    static void Require(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException(message); }
}
