using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public sealed class WindomBaselineManifest
{
    public int schemaVersion = 1;
    public string baselineId, sourceRunId, unityVersion, approvedUtc, approvalReason;
    public string evidence = "RealAniObserved";
    public List<WindomBaselineEntry> scenarios = new List<WindomBaselineEntry>();
    public List<WindomVerificationHash> sourceHashes = new List<WindomVerificationHash>();
}

[Serializable]
public sealed class WindomBaselineEntry
{
    public string scenario, traceHash, scenarioContractHash;
}

[Serializable]
public sealed class WindomBaselineComparison
{
    public string scenario, state, reason, baselineTick, actualTick;
    public int firstMismatchIndex = -1;
}

[Serializable]
public sealed class WindomBaselineReport
{
    public int schemaVersion = 1;
    public string baselineId, state = "Running", firstFailure;
    public List<WindomBaselineComparison> comparisons = new List<WindomBaselineComparison>();
}

/// <summary>Versioned, explicit baseline promotion. Verification never modifies an approved baseline.</summary>
public static class WindomGoldenBaseline
{
    public const string ApprovedBaselineId = "6000.6-20260905-p1-02";
    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    public static string BaselineRoot => Path.Combine(ProjectRoot, "Verification", "Baselines");

    // Invoke only after the source run and the stated reason have been reviewed.
    // A new ID is mandatory: there is no overwrite/replace operation.
    public static void CreateFromReviewedRun(string runId, string newBaselineId, string approvalReason)
    {
        ValidateId(newBaselineId);
        if (string.IsNullOrWhiteSpace(approvalReason)) throw new ArgumentException("An approval reason is required.");
        if (WindomVerificationRunner.IsRunning) throw new InvalidOperationException("Wait for the active verification job.");
        var run = WindomVerificationRunner.GetVerificationStatus(runId);
        if (run.state != "Succeeded" || !run.suites.Any(s => s.name == "Golden" && s.scenarios == TestPlayGoldenScenarioCatalog.All.Count))
            throw new InvalidOperationException("Promotion requires a successful, complete Golden run.");
        // Prove that the scenario definition captured below is the one used by the reviewed run.
        string catalogPath = Path.Combine(ProjectRoot, "Assets", "Scripts", "TestPlay", "TestPlayGoldenTrace.cs");
        var catalogHash = run.hashes.FirstOrDefault(h => string.Equals(h.path, catalogPath, StringComparison.OrdinalIgnoreCase));
        if (catalogHash == null || !string.Equals(FileHash(catalogPath), catalogHash.sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Scenario catalog has changed since the reviewed run. Generate and review a new candidate.");
        string destination = Path.Combine(BaselineRoot, newBaselineId);
        if (Directory.Exists(destination)) throw new IOException("Baseline ID already exists; choose a new ID.");
        var manifest = new WindomBaselineManifest {
            baselineId = newBaselineId, sourceRunId = runId, unityVersion = run.unityVersion,
            approvedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture), approvalReason = approvalReason
        };
        foreach (var hash in run.hashes)
            manifest.sourceHashes.Add(new WindomVerificationHash {
                path = hash.path.StartsWith(ProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? hash.path.Substring(ProjectRoot.Length + 1).Replace('\\', '/') : hash.path,
                sha256 = hash.sha256
            });
        var traces = new Dictionary<string, byte[]>();
        foreach (var definition in TestPlayGoldenScenarioCatalog.All)
        {
            string source = Path.Combine(run.outputFolder, "golden", definition.id + ".unity-reference.jsonl");
            byte[] bytes = File.ReadAllBytes(source);
            string text = Encoding.UTF8.GetString(bytes);
            var check = CompareTexts(text, text, definition.id);
            if (check.state != "Match") throw new InvalidDataException(check.reason);
            traces.Add(definition.id, bytes);
            manifest.scenarios.Add(new WindomBaselineEntry {
                scenario = definition.id, traceHash = Hash(bytes), scenarioContractHash = ScenarioHash(definition)
            });
        }
        // Write an unreferenced staging directory and rename only after every file has been validated.
        Directory.CreateDirectory(BaselineRoot);
        string staging = Path.Combine(BaselineRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var trace in traces) File.WriteAllBytes(Path.Combine(staging, trace.Key + ".unity-reference.jsonl"), trace.Value);
            File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); } // only this operation's unique staging directory
        Debug.Log("[GoldenBaseline] Created " + destination + ". Default selection is unchanged.");
    }

    internal static async Task<int> CompareRunAsync(string runFolder, CancellationToken token)
    {
        var report = new WindomBaselineReport { baselineId = ApprovedBaselineId };
        try
        {
            string folder = Path.Combine(BaselineRoot, ApprovedBaselineId);
            var manifest = JsonUtility.FromJson<WindomBaselineManifest>(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            if (manifest == null || manifest.schemaVersion != 1 || manifest.baselineId != ApprovedBaselineId || manifest.evidence != "RealAniObserved" ||
                manifest.scenarios == null || manifest.scenarios.Count != TestPlayGoldenScenarioCatalog.All.Count ||
                manifest.scenarios.Select(s => s.scenario).Distinct(StringComparer.Ordinal).Count() != manifest.scenarios.Count)
                throw new InvalidDataException("Invalid or incomplete approved baseline manifest.");
            foreach (var definition in TestPlayGoldenScenarioCatalog.All)
            {
                token.ThrowIfCancellationRequested();
                var entry = manifest.scenarios.SingleOrDefault(s => s.scenario == definition.id);
                WindomBaselineComparison comparison;
                string baselineFile = Path.Combine(folder, definition.id + ".unity-reference.jsonl");
                if (entry == null || entry.scenarioContractHash != ScenarioHash(definition))
                    comparison = Rejected(definition.id, "Scenario definition/setup/input contract differs.");
                else if (!string.Equals(FileHash(baselineFile), entry.traceHash, StringComparison.OrdinalIgnoreCase))
                    comparison = Rejected(definition.id, "Approved baseline file hash differs from manifest.");
                else
                    comparison = CompareTexts(File.ReadAllText(baselineFile),
                        File.ReadAllText(Path.Combine(runFolder, "golden", definition.id + ".unity-reference.jsonl")), definition.id);
                report.comparisons.Add(comparison);
                await Task.Yield();
            }
            report.state = report.comparisons.All(c => c.state == "Match") ? "Succeeded" : "Failed";
            if (report.state == "Failed")
            {
                var first = report.comparisons.First(c => c.state != "Match");
                throw new InvalidDataException(first.scenario + " " + first.state + ": " + first.reason);
            }
            return report.comparisons.Count;
        }
        catch (OperationCanceledException) { report.state = "Cancelled"; throw; }
        catch (Exception ex) { report.state = "Failed"; report.firstFailure = ex.ToString(); throw; }
        finally { File.WriteAllText(Path.Combine(runFolder, "baseline-comparison.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false)); }
    }

    // Reject incompatible evidence before comparing behavior. Exact tick serialization is intentional.
    public static WindomBaselineComparison CompareTexts(string baseline, string actual, string expectedScenario)
    {
        try
        {
            var left = Parse(baseline, expectedScenario);
            var right = Parse(actual, expectedScenario);
            if (!JToken.DeepEquals(left[0]["session"], right[0]["session"]))
                return Rejected(expectedScenario, "Session schema/source/scenario/mech/data hash/tick rate/evidence differs.");
            if (left.Length != right.Length) return Rejected(expectedScenario, "Input/tick count differs.");
            for (int i = 1; i < left.Length; i++)
                if (!JToken.DeepEquals(left[i]["input"], right[i]["input"]))
                    return Rejected(expectedScenario, "Input differs at tick index " + (i - 1));
            if (!TestPlayGoldenTraceSession.TryParseJsonLines(baseline, out var reference, out string referenceError))
                return Rejected(expectedScenario, referenceError);
            if (!TestPlayGoldenTraceSession.TryParseJsonLines(actual, out var candidate, out string candidateError))
                return Rejected(expectedScenario, candidateError);
            int mismatch = TestPlayGoldenTraceSession.FindFirstMismatch(reference, candidate);
            return new WindomBaselineComparison {
                scenario = expectedScenario, state = mismatch < 0 ? "Match" : "Mismatch",
                firstMismatchIndex = mismatch, reason = mismatch < 0 ? "Exact tick equality (RealAniObserved)." : "Behavior differs at tick index " + mismatch,
                baselineTick = mismatch < 0 ? "" : reference.Ticks[mismatch], actualTick = mismatch < 0 ? "" : candidate.Ticks[mismatch]
            };
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException || ex is InvalidDataException || ex is ArgumentException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
        { return Rejected(expectedScenario, ex.Message); }
    }

    static JObject[] Parse(string text, string expectedScenario)
    {
        var lines = (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => JObject.Parse(line,
                new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error })).ToArray();
        if (lines.Length < 2 || !(lines[0]["session"] is JObject h) ||
            (int?)h["schemaVersion"] != 1 || (int?)h["tickRate"] != 60 || (string)h["source"] != "unity-core" ||
            (string)h["scenario"] != expectedScenario || (string)h["baseline"] != "RealAniObserved" ||
            string.IsNullOrEmpty((string)h["mechId"]) || !IsHash((string)h["aniHash"]) || !IsHash((string)h["sptHash"]))
            throw new InvalidDataException("Unsupported or incomplete trace contract.");
        for (int i = 1; i < lines.Length; i++)
            if ((int?)lines[i]["tick"] != i || !(lines[i]["input"] is JObject input) ||
                input["direction"]?.Type != JTokenType.Integer ||
                new[] { "rise", "boost", "shot", "melee", "guard", "lock" }.Any(k => input[k]?.Type != JTokenType.Boolean))
                throw new InvalidDataException("Invalid tick sequence or incomplete input at line " + (i + 1));
        return lines;
    }

    static WindomBaselineComparison Rejected(string scenario, string reason) =>
        new WindomBaselineComparison { scenario = scenario, state = "ContractMismatch", reason = reason };
    static bool IsHash(string s) => s != null && s.Length == 64 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    internal static string ScenarioHash(TestPlayGoldenScenarioDefinition d)
    {
        // Explicit fields: the runtime definition is not a Unity-serializable DTO.
        var contract = new JObject {
            ["id"] = d.id, ["setup"] = d.setup.ToString(), ["evidence"] = d.baseline.ToString(),
            ["target"] = new JArray(d.targetPosition.x, d.targetPosition.y, d.targetPosition.z),
            ["actions"] = new JArray(d.requiredActionIds), ["commands"] = new JArray(d.requiredCommands),
            ["segments"] = new JArray(d.inputSegments.Select(s => new JObject {
                ["ticks"] = s.ticks, ["direction"] = s.input.direction, ["rise"] = s.input.rise,
                ["shot"] = s.input.shot, ["melee"] = s.input.melee, ["guard"] = s.input.guard,
                ["lock"] = s.input.lockTarget, ["special1"] = s.input.special1,
                ["special2"] = s.input.special2, ["special3"] = s.input.special3
            }))
        };
        return TestPlayGoldenTraceSession.ComputeSha256(contract.ToString(Newtonsoft.Json.Formatting.None));
    }
    internal static string FileHash(string file) { using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
    static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
    static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 80 || !id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '.'))
            throw new ArgumentException("Baseline ID must contain ASCII letters, digits, dash or dot.");
        if (id == "." || id == ".." || id[0] == '.') throw new ArgumentException("Invalid baseline ID.");
    }
}
