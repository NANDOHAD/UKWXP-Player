using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Settings;

[Flags]
public enum WindomVerificationSelection
{
    Runtime = 1, Hod = 2, Golden = 4, SelectedGolden = 8, All = 7,
    BaselineComparison = 16, AniInventory = 32, Standalone = 64, Session = 128, Lifecycle = 256,
    Presentation = 512, Regression = All | BaselineComparison
}

[Serializable]
public sealed class WindomVerificationResult
{
    public int schemaVersion = 1;
    public string runId, state, unityVersion, startedUtc, completedUtc, currentSuite, firstFailure, outputFolder;
    public int selection;
    public int completedItems, totalItems;
    public string currentItem;
    public List<WindomVerificationSuiteResult> suites = new List<WindomVerificationSuiteResult>();
    public List<WindomVerificationLog> logs = new List<WindomVerificationLog>();
    public List<WindomVerificationHash> hashes = new List<WindomVerificationHash>();
}

[Serializable]
public sealed class WindomVerificationSuiteResult { public string name; public int assertions, scenarios; }
[Serializable]
public sealed class WindomVerificationLog { public string type, message; public bool expected; }
[Serializable]
public sealed class WindomVerificationHash { public string path, sha256; }

/// <summary>Editor-only job entry point. Call on the Editor main thread; never synchronously wait on a job.</summary>
[InitializeOnLoad]
public static class WindomVerificationRunner
{
    static WindomVerificationResult active;
    static CancellationTokenSource cancellation;
    static readonly string projectRoot = Directory.GetParent(Application.dataPath).FullName;
    static readonly string resultRoot = Path.Combine(projectRoot, "Logs", "VerificationRuns");
    static int mainThread = Thread.CurrentThread.ManagedThreadId;
    public static bool IsRunning => active != null;

    static WindomVerificationRunner()
    {
        AssemblyReloadEvents.beforeAssemblyReload += InterruptForReload;
        EditorApplication.quitting += InterruptForReload;
    }

    [MenuItem("Tools/WindomXP/Verification/Run All")]
    public static void RunAllFromMenu() { StartVerification(WindomVerificationSelection.Regression); }

    [MenuItem("Tools/WindomXP/Verification/Inventory Three Mechs")]
    public static void InventoryFromMenu() { StartVerification(WindomVerificationSelection.AniInventory); }

    [MenuItem("Tools/WindomXP/Verification/Cancel Active Run")]
    public static void CancelFromMenu() { if (active != null) CancelVerification(active.runId); }

    public static string StartVerification(WindomVerificationSelection selection, string mechFolder = null)
    {
        CheckMainThread();
        if ((int)selection <= 0 || ((int)selection & ~1023) != 0)
            throw new ArgumentOutOfRangeException(nameof(selection));
        if ((selection & WindomVerificationSelection.BaselineComparison) != 0 && (selection & WindomVerificationSelection.Golden) == 0)
            throw new ArgumentException("BaselineComparison requires Golden in the same run.", nameof(selection));
        if ((selection & WindomVerificationSelection.SelectedGolden) != 0 && string.IsNullOrWhiteSpace(mechFolder))
            throw new ArgumentException("SelectedGolden requires a mech folder.", nameof(mechFolder));
        string id = StartJob(selection, (job, token) => RunSuites(job, selection, mechFolder, token));
        Debug.Log("[Verification] Queued " + id + ". Result=" + Path.Combine(resultRoot, id, "result.json"));
        return id;
    }

    // Returns a detached snapshot; callers cannot mutate the running job.
    public static WindomVerificationResult GetVerificationStatus(string runId)
    {
        CheckMainThread();
        ValidateRunId(runId);
        if (active != null && active.runId == runId)
        {
            var snapshot = JsonUtility.FromJson<WindomVerificationResult>(JsonUtility.ToJson(active));
            if (IsTerminal(snapshot.state)) snapshot.state = "Finalizing";
            return snapshot;
        }
        string file = Path.Combine(resultRoot, runId, "result.json");
        string interrupted = Path.Combine(resultRoot, runId, "interrupted.json");
        if (File.Exists(interrupted)) file = interrupted;
        string failed = Path.Combine(resultRoot, runId, "persistence-failed.json");
        if (File.Exists(failed)) file = failed;
        if (!File.Exists(file)) throw new FileNotFoundException("Verification run not found.", file);
        return JsonUtility.FromJson<WindomVerificationResult>(File.ReadAllText(file));
    }

    public static bool CancelVerification(string runId)
    {
        CheckMainThread();
        ValidateRunId(runId);
        if (active == null || active.runId != runId || IsTerminal(active.state)) return false;
        active.state = "CancellationRequested";
        cancellation.Cancel();
        // Live status changes immediately. The next awaited checkpoint persists cancellation without racing another writer.
        return true;
    }

    static string StartJob(WindomVerificationSelection selection,
        Func<WindomVerificationResult, CancellationToken, Task> work)
    {
        if (active != null) throw new InvalidOperationException("A verification job is already active: " + active.runId);
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Verification requires an idle Edit Mode editor.");
        string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        var job = new WindomVerificationResult {
            runId = id, state = "Queued", selection = (int)selection, unityVersion = Application.unityVersion,
            startedUtc = UtcNow(), outputFolder = Path.Combine(resultRoot, id), firstFailure = ""
        };
        Directory.CreateDirectory(job.outputFolder);
        File.WriteAllText(Path.Combine(job.outputFolder, "result.json"), JsonUtility.ToJson(job, true));
        cancellation = new CancellationTokenSource();
        active = job;
        RunJob(job, work, cancellation);
        return id;
    }

    static async void RunJob(WindomVerificationResult job,
        Func<WindomVerificationResult, CancellationToken, Task> work, CancellationTokenSource source)
    {
        // Delay even synchronous suites until the StartVerification call has returned.
        await Task.Yield();
        bool reloadLocked = false;
        Application.LogCallback logger = (message, stack, type) => RecordLog(job, message, type, false);
        try
        {
            source.Token.ThrowIfCancellationRequested();
            EditorApplication.LockReloadAssemblies();
            reloadLocked = true;
            Application.logMessageReceived += logger;
            job.state = "Running";
            await SaveAsync(job);
            await work(job, source.Token);
            source.Token.ThrowIfCancellationRequested();
            job.state = string.IsNullOrEmpty(job.firstFailure) ? "Succeeded" : "Failed";
        }
        catch (OperationCanceledException) { job.state = string.IsNullOrEmpty(job.firstFailure) ? "Cancelled" : "Failed"; }
        catch (Exception ex) { Fail(job, ex.ToString()); job.state = "Failed"; }
        finally
        {
            Application.logMessageReceived -= logger;
            job.completedUtc = UtcNow();
            try { await SaveAsync(job); }
            catch (Exception ex)
            {
                Fail(job, "Result persistence failed: " + ex); job.state = "Failed";
                try { File.WriteAllText(Path.Combine(job.outputFolder, "persistence-failed.json"), JsonUtility.ToJson(job, true)); }
                catch (Exception markerError) { Debug.LogError("Failure marker could not be saved: " + markerError); }
                Debug.LogError(job.firstFailure);
            }
            finally
            {
                active = null;
                cancellation = null;
                source.Dispose();
                if (reloadLocked) EditorApplication.UnlockReloadAssemblies();
            }
            if (job.selection != 0)
            {
                string summary = "[Verification] " + job.state + " " + job.runId + ". Result=" + Path.Combine(job.outputFolder, "result.json");
                if (job.state == "Failed") Debug.LogError(summary + "\n" + job.firstFailure);
                else Debug.Log(summary);
            }
        }
    }

    static async Task RunSuites(WindomVerificationResult job, WindomVerificationSelection selection,
        string mechFolder, CancellationToken token)
    {
        job.currentSuite = "Initialization";
        var initialization = LocalizationSettings.InitializationOperation;
        double initializationDeadline = EditorApplication.timeSinceStartup + 60;
        while (!initialization.IsDone)
        {
            token.ThrowIfCancellationRequested();
            if (EditorApplication.timeSinceStartup >= initializationDeadline)
                throw new TimeoutException("Localization initialization did not finish within 60 seconds.");
            await Task.Yield();
        }
        if (initialization.OperationException != null) throw initialization.OperationException;
        // No locale/scene/Play changes: tests use their own temporary objects and files.
        string[] sources = Directory.GetFiles(Path.Combine(projectRoot, "Assets", "Scripts"), "*.cs", SearchOption.AllDirectories);
        var files = new List<string>(sources);
        files.AddRange(Directory.GetFiles(Path.Combine(projectRoot, "Assets", "Editor"), "*.cs", SearchOption.AllDirectories));
        files.Add(Path.Combine(projectRoot, "ProjectSettings", "ProjectVersion.txt"));
        files.Add(Path.Combine(projectRoot, "Packages", "manifest.json"));
        files.Add(Path.Combine(projectRoot, "Packages", "packages-lock.json"));
        string dataRoot = Path.Combine(projectRoot, "Windom_Data", "Robo");
        if (Directory.Exists(dataRoot))
            foreach (string file in Directory.GetFiles(dataRoot, "*", SearchOption.AllDirectories))
                if (IsDataFile(file)) files.Add(file);
        if (!string.IsNullOrEmpty(mechFolder)) { files.Add(Path.Combine(mechFolder, "Script.ani")); files.Add(Path.Combine(mechFolder, "Script.spt")); }
        job.hashes = await Task.Run(() => HashFiles(files, token), token);
        await SaveAsync(job);
        if ((selection & WindomVerificationSelection.Presentation) != 0)
            await RunSuite(job, "Presentation", token, async () => {
                var templateObject = new GameObject("VerificationPresentationTemplate");
                try
                {
                    var template = templateObject.AddComponent<TestPlayPresentationRuntime>();
                    TestPlayOriginalSoundSetup.ApplyMappings(template);
                    TestPlayOriginalTextureSetup.ApplyMappings(template, out _);
                    var report = await TestPlayPresentationVerification.RunAsync(
                        Path.Combine(projectRoot, "Windom_Data", "Robo", "ガンダムTR-1ヘイズル改", "Script.ani"),
                        Path.Combine(job.outputFolder, "presentation"), null, token, template);
                    return new WindomVerificationSuiteResult { name = "Presentation", assertions = report.assertions, scenarios = report.shots.Count };
                }
                finally { UnityEngine.Object.DestroyImmediate(templateObject); }
            });
        if ((selection & WindomVerificationSelection.Lifecycle) != 0)
            await RunSuite(job, "Lifecycle", token, async () => new WindomVerificationSuiteResult {
                name = "Lifecycle", assertions = (await TestPlaySessionLifecycleVerification.RunAsync(
                    Path.Combine(projectRoot, "Windom_Data", "Robo", "ガンダムTR-1ヘイズル改", "Script.ani"),
                    job.outputFolder, null, token)).assertions
            });
        if ((selection & WindomVerificationSelection.Session) != 0)
            await RunSuite(job, "Session", token, async () => new WindomVerificationSuiteResult {
                name = "Session", assertions = await TestPlaySessionVerification.RunForJobAsync(job.outputFolder, token)
            });
        if ((selection & WindomVerificationSelection.Standalone) != 0)
            await RunSuite(job, "Standalone", token, async () => new WindomVerificationSuiteResult {
                name = "Standalone", assertions = await TestPlayStandaloneVerification.RunForJobAsync(job.outputFolder, token)
            });
        if ((selection & WindomVerificationSelection.Runtime) != 0)
            await RunSuite(job, "Runtime", token, () => Task.FromResult(new WindomVerificationSuiteResult { name = "Runtime", assertions = TestPlayRuntimeVerification.RunAll() }));
        if ((selection & WindomVerificationSelection.Hod) != 0)
            await RunSuite(job, "Hod", token, async () => new WindomVerificationSuiteResult { name = "Hod", assertions = await HodHierarchyPriorityVerification.RunForJobAsync() });
        if ((selection & WindomVerificationSelection.Golden) != 0)
            await RunSuite(job, "Golden", token, async () => new WindomVerificationSuiteResult { name = "Golden", scenarios = await TestPlayGoldenTraceVerification.RunForJobAsync(Path.Combine(job.outputFolder, "golden"), token) });
        if ((selection & WindomVerificationSelection.BaselineComparison) != 0)
            await RunSuite(job, "BaselineComparison", token, async () => new WindomVerificationSuiteResult {
                name = "BaselineComparison", scenarios = await WindomGoldenBaseline.CompareRunAsync(job.outputFolder, token)
            });
        if ((selection & WindomVerificationSelection.SelectedGolden) != 0)
            await RunSuite(job, "SelectedGolden", token, async () => {
                await TestPlayGoldenTraceVerification.RunSelectedMechGt001Async(mechFolder, Path.Combine(job.outputFolder, "selected-golden"));
                return new WindomVerificationSuiteResult { name = "SelectedGolden", scenarios = 1 };
            });
        if ((selection & WindomVerificationSelection.AniInventory) != 0)
            await RunSuite(job, "AniInventory", token, async () => new WindomVerificationSuiteResult {
                name = "AniInventory", scenarios = await WindomAniInventory.RunAsync(
                    Path.Combine(job.outputFolder, "inventory"), token,
                    async (completed, total, item) => { job.completedItems = completed; job.totalItems = total; job.currentItem = item; await SaveAsync(job); })
            });
    }

    static async Task RunSuite(WindomVerificationResult job, string name, CancellationToken token,
        Func<Task<WindomVerificationSuiteResult>> run)
    {
        token.ThrowIfCancellationRequested();
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play Mode changed during verification.");
        job.currentSuite = name;
        await SaveAsync(job);
        job.suites.Add(await run());
        await SaveAsync(job);
        if (!string.IsNullOrEmpty(job.firstFailure)) throw new InvalidOperationException("Unexpected error during " + name);
        await Task.Yield(); // cancellation is cooperative at suite/scenario boundaries
        token.ThrowIfCancellationRequested();
    }

    static bool IsDataFile(string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        return ext == ".ani" || ext == ".an2" || ext == ".spt";
    }

    static List<WindomVerificationHash> HashFiles(List<string> files, CancellationToken token)
    {
        files.Sort(StringComparer.Ordinal);
        var result = new List<WindomVerificationHash>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var sha = SHA256.Create())
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                if (!seen.Add(file)) continue;
                using (var stream = File.OpenRead(file))
                    result.Add(new WindomVerificationHash { path = file, sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() });
            }
        return result;
    }

    internal static void RecordLog(WindomVerificationResult job, string message, LogType type, bool expected)
    {
        if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        job.logs.Add(new WindomVerificationLog { message = message, type = type.ToString(), expected = expected });
        if (!expected && type != LogType.Warning) Fail(job, type + ": " + message);
    }

    // Suppress only the exact, synchronous negative-path warning in this action.
    // Everything else is forwarded to Unity's normal logger and the job listener.
    public static void ExpectWarning(string expectedMessage, Action action)
    {
        var previous = Debug.unityLogger.logHandler;
        var handler = new ExpectedWarningHandler(previous, expectedMessage);
        Debug.unityLogger.logHandler = handler;
        try { action(); }
        finally { Debug.unityLogger.logHandler = previous; }
        if (handler.count != 1) throw new InvalidOperationException("Expected one warning, got " + handler.count + ": " + expectedMessage);
        if (active != null) RecordLog(active, expectedMessage, LogType.Warning, true);
    }

    sealed class ExpectedWarningHandler : ILogHandler
    {
        readonly ILogHandler previous;
        readonly string message;
        public int count;
        public ExpectedWarningHandler(ILogHandler previous, string message) { this.previous = previous; this.message = message; }
        public void LogException(Exception exception, UnityEngine.Object context) { previous.LogException(exception, context); }
        public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
        {
            if (type == LogType.Warning && string.Format(CultureInfo.InvariantCulture, format, args) == message) { count++; return; }
            previous.LogFormat(type, context, format, args);
        }
    }

    static bool IsTerminal(string state) => state == "Succeeded" || state == "Failed" || state == "Cancelled" || state == "Interrupted";
    static void Fail(WindomVerificationResult job, string error) { if (string.IsNullOrEmpty(job.firstFailure)) job.firstFailure = error; }
    static string UtcNow() { return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); }
    static void CheckMainThread() { if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Call verification APIs on the Editor main thread."); }
    static void ValidateRunId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 80) throw new ArgumentException("Invalid run ID.");
        foreach (char c in id) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && c != '-' && c != 'T' && c != 'Z') throw new ArgumentException("Invalid run ID.");
    }
    static Task SaveAsync(WindomVerificationResult job)
    {
        string path = Path.Combine(job.outputFolder, "result.json");
        return WindomVerificationStorage.WriteAtomicAsync(path, JsonUtility.ToJson(job, true));
    }
    static void InterruptForReload() { if (active != null) MarkInterrupted(active); }
    internal static void MarkInterrupted(WindomVerificationResult job)
    {
        job.state = "Interrupted";
        job.completedUtc = UtcNow();
        Fail(job, "Editor/domain stopped before completion; cleanup and suite completion are not guaranteed.");
        // Shutdown cannot await a locked result replacement. A separate immutable marker takes precedence on read.
        string marker = Path.Combine(job.outputFolder, "interrupted.json");
        if (!File.Exists(marker))
            using (var writer = new StreamWriter(new FileStream(marker, FileMode.CreateNew, FileAccess.Write)))
                writer.Write(JsonUtility.ToJson(job, true));
    }
    internal static string StartTestJob(Func<WindomVerificationResult, CancellationToken, Task> work)
    {
        CheckMainThread();
        return StartJob((WindomVerificationSelection)0, work);
    }
}
