using System;
using System.Collections.Generic;
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
        await VerifyMeshlessModelsAsync(outputFolder, token, check);
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

    static async Task VerifyMeshlessModelsAsync(string outputFolder, CancellationToken token, Action<bool, string> check)
    {
        // Isolated AN2/X/SPT fixtures: never change the real mech or its source models.
        foreach (string precision in new[] { "0032", "0064" })
        foreach (string encryption in new[] { "plain", "encrypted", "plain-first-mixed", "encrypted-first-mixed" })
        foreach (bool emptyMesh in new[] { false, true })
        {
            bool encrypted = encryption != "plain";
            string label = precision + "-" + encryption + (emptyMesh ? "-empty-mesh" : "-frame");
            string folder = Path.Combine(outputFolder, "meshless-models", label);
            Directory.CreateDirectory(folder);
            var coder = new CypherTranscoder { cypher = encrypted ? 0x0B7E7759u : 0u };
            string header = "xof 0303txt " + precision + "\n";
            string pivot = header + (emptyMesh ? "Mesh Empty { 0; 0; }" : "Frame Pivot { Frame Nested { } }");
            string visible = header + "Frame Container { Frame Child { Mesh Visible {\n" +
                "3;\n0.0;0.0;0.0;,\n1.0;0.0;0.0;,\n0.0;1.0;0.0;;\n1;\n3;0,1,2;;\n" +
                "MeshMaterialList { 1; 1; 0;; Material Surface { 1;1;1;1;; 1; 0;0;0;; 0;0;0;; " +
                "TextureFilename { \"fixture.png\"; } } } } } }";
            byte[] pivotBytes = System.Text.Encoding.UTF8.GetBytes(pivot);
            byte[] visibleBytes = System.Text.Encoding.UTF8.GetBytes(visible);
            File.WriteAllBytes(Path.Combine(folder, "pivot.x"), encryption == "plain-first-mixed" ? pivotBytes : coder.Transcode(pivotBytes));
            File.WriteAllBytes(Path.Combine(folder, "visible.x"), encryption == "encrypted-first-mixed" ? visibleBytes : coder.Transcode(visibleBytes));
            var textureFixture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                textureFixture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
                textureFixture.Apply();
                File.WriteAllBytes(Path.Combine(folder, "fixture.png"), coder.Transcode(textureFixture.EncodeToPNG()));
            }
            finally { UnityEngine.Object.DestroyImmediate(textureFixture); }
            File.WriteAllBytes(Path.Combine(folder, "Script.spt"), coder.Transcode(USEncoder.ToEncoding.ToSJIS("WEAPONPOINT(0, pivot.x, UP)\r\n")));
            var structure = new hod2v0("meshless") { parts = new List<hod2v0_Part> {
                new hod2v0_Part { name = "root", childCount = 1, scale = Vector3.one, rotation = Quaternion.identity },
                new hod2v0_Part { name = "pivot.x", treeDepth = 1, childCount = 1,
                    position = new Vector3(2, 3, 4), rotation = Quaternion.Euler(0, 30, 0), scale = new Vector3(2, 1, 1) },
                new hod2v0_Part { name = "visible.x", treeDepth = 2, position = Vector3.up,
                    rotation = Quaternion.identity, scale = Vector3.one }
            } };
            var frame = new hod2v1("pose") { parts = structure.parts.Select(p => new hod2v1_Part {
                name = p.name, treeDepth = p.treeDepth, childCount = p.childCount,
                position = p.position, rotation = p.rotation, scale = p.scale
            }).ToList() };
            var source = new ani2 { structure = structure, animations = Enumerable.Range(0, 200).Select(i => new animation {
                name = "pose" + i, frames = new List<hod2v1> { frame }, scripts = new List<script>()
            }).ToList() };
            string path = Path.Combine(folder, "Script.an2");
            source.save(path);
            var loaded = await MechLoader.LoadAsync(path, null, token);
            check(loaded.sourceFormat == AniContainerFormat.An2 && loaded.structure.parts.Count == 3 &&
                loaded.animations[0].frames[0].parts[1].position == structure.parts[1].position, label + " AN2 save/reload");
            MechAssetLease lease = null;
            MechAssetLease secondLease = null;
            int rootsBefore = UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length;
            var diagnostics = new List<string>();
            Application.LogCallback capture = (message, stack, type) => {
                if (message.StartsWith("[RoboStructure]") && type != LogType.Log) diagnostics.Add(message);
            };
            Application.logMessageReceived += capture;
            try
            {
                lease = await UnityMechBuilder.BuildAsync(loaded, path, token);
                var robo = lease.Robo;
                check(diagnostics.Count == 0, label + " no import warnings");
                check(robo.transcoder.cypher == coder.cypher, label + " folder key retained for SPT and textures");
                check(robo.parts.Count == 3 && robo.parts[2].transform.parent == robo.parts[1].transform,
                    label + " transform-only parent and child retained");
                check(GetMesh(robo.parts[1]) == null && robo.parts[1].GetComponent<MeshRenderer>() == null,
                    label + " empty part has no rendering assets");
                check(GetMesh(robo.parts[2]) != null && GetMesh(robo.parts[2]).vertexCount == 3,
                    label + " child mesh still imported from nested X frames");
                check(robo.parts[1].transform.localPosition == structure.parts[1].position &&
                    Quaternion.Angle(robo.parts[1].transform.localRotation, structure.parts[1].rotation) < 0.01f &&
                    robo.parts[1].transform.localScale == structure.parts[1].scale, label + " HOD local TRS retained");
                check(lease.Spt.WeaponPoints[0].BoneTr == robo.parts[1].transform, label + " SPT binds meshless part");
                var mesh = GetMesh(robo.parts[2]);
                var root = robo.root;
                var texture = robo.parts[2].GetComponent<MeshRenderer>().sharedMaterial.mainTexture as Texture2D;
                check(texture != null && texture.width == 2 && texture.GetPixel(0, 0) == Color.red,
                    label + " texture decoded with folder key");
                secondLease = await UnityMechBuilder.BuildAsync(loaded, path, token);
                var secondTexture = secondLease.Robo.parts[2].GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
                check(secondTexture != null && texture != secondTexture, label + " independent texture ownership");
                await lease.ReleaseAsync();
                check(root == null && mesh == null && lease.RemainingOwnedAssetCount == 0, label + " release all assets");
                check(texture == null && secondTexture != null, label + " release preserves other mech texture");
                lease = null;
                await secondLease.ReleaseAsync();
                secondLease = null;
                // An unreadable model must not be mistaken for a successfully parsed empty model.
                File.WriteAllBytes(Path.Combine(folder, "pivot.x"), new byte[0]);
                bool rejected = false;
                try { lease = await UnityMechBuilder.BuildAsync(loaded, path, token); }
                catch (InvalidDataException e) { rejected = e.Message.Contains("pivot.x"); }
                check(rejected, label + " invalid model still rejected");
                check(UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length == rootsBefore,
                    label + " failed build rolls back");
            }
            finally
            {
                Application.logMessageReceived -= capture;
                if (lease != null) await lease.ReleaseAsync();
                if (secondLease != null) await secondLease.ReleaseAsync();
            }
        }
    }
}
