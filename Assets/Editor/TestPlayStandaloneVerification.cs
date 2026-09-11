using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class TestPlayStandaloneVerification
{
    [MenuItem("Tools/WindomXP/Verification/Verify Standalone and Regression")]
    public static void Run() => WindomVerificationRunner.StartVerification(
        WindomVerificationSelection.Standalone | WindomVerificationSelection.Regression);

    static Mesh GetMesh(GameObject part)
    {
        var filter = part.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    internal static async Task<int> RunForJobAsync(string outputFolder, CancellationToken token)
    {
        int assertions = 0;
        Action<bool, string> check = (condition, message) => {
            if (!condition) throw new InvalidOperationException("[Standalone] " + message);
            assertions++;
        };
        if (UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale == null)
            check(UILocalization.Get("standalone.missing_locale_probe", "言語未選択 {0}", 7) == "言語未選択 7",
                "missing locale uses formatted fallback");
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "Windom_Data", "Robo", "ガンダムTR-1ヘイズル改", "Script.ani");
        GameObject host = new GameObject("StandaloneVerification");
        var bootstrap = host.AddComponent<TestPlayStandaloneBootstrap>();
        bootstrap.showHud = false;
        MechAssetLease other = null;
        var cameraObject = new GameObject("BorrowedCamera", typeof(Camera));
        var cameraAdapter = cameraObject.AddComponent<TestPlayCameraController>();
        var previousController = cameraObject.AddComponent<TestPlayController>();
        cameraAdapter.Bind(previousController);
        Camera previousCamera = cameraAdapter.controlledCamera;
        bootstrap.gameCamera = cameraObject.GetComponent<Camera>();
        try
        {
            await bootstrap.StartAsync(path, token);
            var controller = bootstrap.Controller;
            var robo = bootstrap.Assets.Robo;
            check(bootstrap.Status == "Running" && controller.playModeActive, "normal start");
            check(controller.sptSource == null && controller.target == null && controller.cameraController == cameraAdapter,
                "explicit context must not borrow scene UI, target or camera");
            check(robo.parts.Count == 74 && robo.ani.animations.Count == 200, "real mech structure");
            check(robo.parts.Count(p => GetMesh(p) != null) == 27, "real imported meshes");
            var transcoder = robo.transcoder;
            typeof(RoboStructure).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(robo, null);
            check(ReferenceEquals(transcoder, robo.transcoder), "Start preserves initialized decoder");
            check(controller.maximumHP == bootstrap.Assets.Spt.HP, "explicit SPT status");
            int previousTick = controller.tick;
            controller.StartTestPlay();
            check(controller.tick == previousTick, "double start must not reset state");
            bool rejected = false;
            try { controller.Initialize(new TestPlayContext()); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "running reinitialize rejected");
            other = await UnityMechBuilder.BuildAsync(await MechLoader.LoadAsync(path, null, token), path, token);
            check(!ReferenceEquals(robo.ani, other.Robo.ani) && !ReferenceEquals(bootstrap.Assets.Spt, other.Spt), "per-mech data ownership");
            foreach (var pair in bootstrap.Assets.Spt.WeaponPoints)
                if (pair.Value.BoneTr != null && other.Spt.WeaponPoints.TryGetValue(pair.Key, out var point))
                    check(pair.Value.BoneTr != point.BoneTr, "per-mech transform binding");
            controller.StopTestPlay();
            controller.BeginDeterministicTraceSession(TestPlayGoldenSetupKind.GroundedGun);
            typeof(TestPlayController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            check(controller.tick == 0, "manual clock excludes Update");
            Vector3 initial = robo.root.transform.position;
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 120; i++)
                lines.Add(controller.SimulateDeterministicTraceTick(new TestPlayGoldenInputFrame { direction = 1 }));
            check(controller.tick == 120, "manual input tick count");
            check(Vector3.Distance(initial, robo.root.transform.position) > 0.1f, "real ANI movement");
            check(robo.parts.All(p => float.IsFinite(p.transform.position.x) && float.IsFinite(p.transform.position.y) && float.IsFinite(p.transform.position.z)), "finite rendered poses");
            File.WriteAllLines(Path.Combine(outputFolder, "standalone-movement.txt"), lines);
            controller.EndDeterministicTraceSession();
            var ownedRoot = robo.root;
            var ownedMesh = robo.parts.Select(p => GetMesh(p)).First(m => m != null);
            await bootstrap.StopAsync();
            await bootstrap.StopAsync();
            check(bootstrap.Status == "Stopped" && ownedRoot == null && ownedMesh == null, "stop releases owned root and mesh");
            check(other.Robo.root != null, "releasing one mech preserves another");
            check(cameraAdapter != null && cameraAdapter.controller == previousController &&
                cameraAdapter.controlledCamera == previousCamera && !cameraAdapter.cameraModeActive,
                "borrowed camera references restored");
            // Borrow the in-memory editor data; an unsaved edit must not trigger a disk reload.
            var borrowedHost = new GameObject("BorrowedEditorContext");
            try
            {
                var preview = borrowedHost.AddComponent<MechaAnimator>();
                preview.run(robo.ani.animations[0], true);
                preview.UpperOverride = preview.runner;
                preview.ResetForStructureChange();
                check(!preview.play && preview.runner == null && preview.prevRunner == null &&
                    preview.UpperOverride == null, "new mech discards old preview poses");
                preview.run(other.Robo.ani.animations[0], true);
                check(preview.prevRunner == null, "new mech never blends from old frames");
                var firstRunner = preview.runner;
                preview.run(other.Robo.ani.animations[1], true);
                check(preview.prevRunner == firstRunner, "same mech keeps action transitions");
                preview.ResetForStructureChange();
                string oldName = other.Robo.ani.animations[0].name;
                other.Robo.ani.animations[0].name = "unsaved-context-test";
                other.Spt.HP = 1234;
                other.Spt.HasHP = true;
                var borrowed = borrowedHost.AddComponent<TestPlayController>();
                borrowed.Initialize(new TestPlayContext { Robo = other.Robo, Spt = other.Spt, ShowHud = false });
                borrowed.StartTestPlay();
                check(borrowed.maximumHP == 1234 && other.Robo.ani.animations[0].name == "unsaved-context-test", "unsaved ANI/SPT retained");
                borrowed.StopTestPlay();
                check(other.Robo.root != null && GetMesh(other.Robo.parts.First(p => GetMesh(p) != null)) != null,
                    "borrowed assets survive stop");
                other.Robo.ani.animations[0].name = oldName;
            }
            finally { UnityEngine.Object.DestroyImmediate(borrowedHost); }
            string missingFolder = Path.Combine(outputFolder, "missing-spt");
            Directory.CreateDirectory(missingFolder);
            int rootsBefore = UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length;
            bool missingRejected = false;
            try { await UnityMechBuilder.BuildAsync(await MechLoader.LoadAsync(path, null, token), Path.Combine(missingFolder, "Script.ani"), token); }
            catch (InvalidDataException e) { missingRejected = e.Message.Contains("Script.spt"); }
            check(missingRejected, "missing SPT rejected");
            check(UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length == rootsBefore, "failed build rolls back host");
            await other.ReleaseAsync();
            other = null;
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool wasCancelled = false;
            try { await MechLoader.LoadAsync(path, null, cancelled.Token); }
            catch (OperationCanceledException) { wasCancelled = true; }
            finally { cancelled.Dispose(); }
            check(wasCancelled, "load cancellation");
        }
        finally
        {
            await bootstrap.StopAsync();
            if (other != null) await other.ReleaseAsync();
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
        return assertions;
    }
}
