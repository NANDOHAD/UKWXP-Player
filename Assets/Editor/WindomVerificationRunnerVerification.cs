using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises the job protocol; synthetic failures never emit Console errors.</summary>
public static class WindomVerificationRunnerVerification
{
    public static bool IsRunning { get; private set; }
    public static string LastResult { get; private set; } = "Not run";
    static int assertions;

    [MenuItem("Tools/WindomXP/Verification/Verify Job Protocol")]
    public static async void Run()
    {
        if (IsRunning || WindomVerificationRunner.IsRunning) throw new InvalidOperationException("Verification is already running.");
        IsRunning = true;
        LastResult = "Running";
        assertions = 0;
        try
        {
            var previous = Debug.unityLogger.logHandler;
            WindomVerificationRunner.ExpectWarning("expected-job-probe", () => Debug.LogWarning("expected-job-probe"));
            Require(ReferenceEquals(previous, Debug.unityLogger.logHandler), "logger restored after expected warning");
            bool rejected = false;
            try { WindomVerificationRunner.ExpectWarning("missing-probe", () => { }); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "missing warning fails");
            rejected = false;
            try { WindomVerificationRunner.ExpectWarning("duplicate-probe", () => { Debug.LogWarning("duplicate-probe"); Debug.LogWarning("duplicate-probe"); }); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "duplicate warning fails");
            try { WindomVerificationRunner.ExpectWarning("throwing-probe", () => { throw new InvalidOperationException("probe"); }); }
            catch (InvalidOperationException) { }
            Require(ReferenceEquals(previous, Debug.unityLogger.logHandler), "logger restored when action throws");

            string id = WindomVerificationRunner.StartTestJob((job, token) => {
                WindomVerificationRunner.ExpectWarning("expected-recorded-probe", () => Debug.LogWarning("expected-recorded-probe"));
                return Task.CompletedTask;
            });
            Require(WindomVerificationRunner.GetVerificationStatus(id).state == "Queued", "start returns before work");
            var snapshot = WindomVerificationRunner.GetVerificationStatus(id);
            snapshot.state = "tampered";
            Require(WindomVerificationRunner.GetVerificationStatus(id).state == "Queued", "result snapshots are detached");
            rejected = false;
            try { WindomVerificationRunner.StartTestJob((job, token) => Task.CompletedTask); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "concurrent start rejected");
            var result = await Wait(id);
            Require(result.state == "Succeeded" && result.logs.Count == 1 && result.logs[0].expected, "expected warning recorded and passed");
            Require(!string.IsNullOrEmpty(result.completedUtc), "completion persisted");

            id = WindomVerificationRunner.StartTestJob((job, token) => { throw new InvalidOperationException("intentional-assertion-failure"); });
            result = await Wait(id);
            Require(result.state == "Failed" && result.firstFailure.Contains("intentional-assertion-failure"), "exception becomes failed result");
            id = WindomVerificationRunner.StartTestJob((job, token) => {
                WindomVerificationRunner.RecordLog(job, "unexpected-error-probe", LogType.Error, false);
                WindomVerificationRunner.RecordLog(job, "second-error", LogType.Exception, false);
                return Task.CompletedTask;
            });
            result = await Wait(id);
            Require(result.state == "Failed" && result.firstFailure.Contains("unexpected-error-probe") && result.logs.Count == 2, "first unexpected error retained");

            bool workRan = false;
            id = WindomVerificationRunner.StartTestJob((job, token) => { workRan = true; return Task.CompletedTask; });
            Require(WindomVerificationRunner.CancelVerification(id), "queued cancellation accepted");
            result = await Wait(id);
            Require(result.state == "Cancelled" && !workRan, "queued cancellation skips work");

            bool cleanup = false;
            id = WindomVerificationRunner.StartTestJob(async (job, token) => {
                try { while (true) { token.ThrowIfCancellationRequested(); await Task.Yield(); } }
                finally { cleanup = true; }
            });
            await Task.Yield();
            Require(WindomVerificationRunner.CancelVerification(id), "running cancellation accepted");
            result = await Wait(id);
            Require(result.state == "Cancelled" && cleanup, "cancellation finishes cleanup");
            Require(!WindomVerificationRunner.CancelVerification(id), "terminal result cannot be cancelled");

            id = WindomVerificationRunner.StartTestJob((job, token) => Task.CompletedTask);
            result = await Wait(id);
            Require(result.state == "Succeeded", "retry after failure/cancellation works");
            // Test the interruption persistence handler on a detached completed test record.
            WindomVerificationRunner.MarkInterrupted(result);
            Require(WindomVerificationRunner.GetVerificationStatus(id).state == "Interrupted", "interruption survives disk reload");
            rejected = false;
            try { WindomVerificationRunner.GetVerificationStatus("../outside"); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "unsafe run ID rejected");
            System.IO.FileStream heldReader = null;
            try
            {
                id = WindomVerificationRunner.StartTestJob((job, token) => {
                    heldReader = new System.IO.FileStream(System.IO.Path.Combine(job.outputFolder, "result.json"),
                        System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
                    return Task.CompletedTask;
                });
                double deadline = EditorApplication.timeSinceStartup + 5;
                while (heldReader == null && EditorApplication.timeSinceStartup < deadline) await Task.Yield();
                Require(heldReader != null && WindomVerificationRunner.GetVerificationStatus(id).state == "Finalizing" &&
                    !WindomVerificationRunner.CancelVerification(id), "pending final persistence is not reported as success or cancellable");
            }
            finally { heldReader?.Dispose(); }
            result = await Wait(id);
            Require(result.state == "Succeeded", "final persistence resumes after reader unlock");
            LastResult = assertions + " assertions passed.";
            Debug.Log("[VerificationProtocol] " + LastResult);
        }
        catch (Exception ex) { LastResult = "Failed: " + ex; Debug.LogError(LastResult); }
        finally { IsRunning = false; }
    }

    static async Task<WindomVerificationResult> Wait(string id)
    {
        double deadline = EditorApplication.timeSinceStartup + 30;
        while (WindomVerificationRunner.IsRunning)
        {
            if (EditorApplication.timeSinceStartup > deadline)
            {
                WindomVerificationRunner.CancelVerification(id);
                throw new TimeoutException("Job protocol probe did not complete.");
            }
            await Task.Yield();
        }
        return WindomVerificationRunner.GetVerificationStatus(id);
    }

    static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
