using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class TestPlayPhase5Verification
{
    public static int RunAll()
    {
        int assertions = 0;
        VerifyScenarioCatalog(ref assertions);
        VerifyPhase5Trace(ref assertions);
        VerifySessionDeterminism(ref assertions);
        VerifyDeterministicControllerPath(ref assertions);
        VerifyRealMechManifest(ref assertions);
        return assertions;
    }

    static void VerifyScenarioCatalog(ref int assertions)
    {
        IReadOnlyList<TestPlayGoldenScenarioDefinition> definitions = TestPlayGoldenScenarioCatalog.All;
        Require(definitions.Count == 10, "GT-001 through GT-010 are registered", ref assertions);
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < definitions.Count; i++)
        {
            TestPlayGoldenScenarioDefinition definition = definitions[i];
            Require(ids.Add(definition.id), definition.id + " is unique", ref assertions);
            Require(definition.baseline == TestPlayGoldenBaselineKind.RealAniObserved,
                definition.id + " is labelled as a Unity run over real ANI, not original observation", ref assertions);
            Require(definition.requiredActionIds != null && definition.requiredActionIds.Length > 0,
                definition.id + " declares action anchors", ref assertions);
            Require(definition.requiredCommands != null && definition.requiredCommands.Length > 0,
                definition.id + " declares ANI command anchors", ref assertions);
            Require(definition.ExpandInputFrames().Length > 0,
                definition.id + " expands to an explicit tick input stream", ref assertions);
        }
        Require(TestPlayGoldenScenarioCatalog.Find("gt-001") == definitions[0],
            "scenario lookup is case insensitive", ref assertions);
        Require(TestPlayGoldenScenarioCatalog.Find("GT-011") == null,
            "unknown scenario lookup is safe", ref assertions);

        TestPlayGoldenInputFrame[] move = definitions[0].ExpandInputFrames();
        Require(move.Length == 22 && move[2].direction == 8 && move[14].direction == 0,
            "GT-001 has fixed forward and release boundaries", ref assertions);
        TestPlayGoldenInputFrame[] shortJump = definitions[1].ExpandInputFrames();
        Require(shortJump.Length == 93 && shortJump[2].rise && !shortJump[3].rise,
            "GT-002 represents a one-tick Z press", ref assertions);
        TestPlayGoldenInputFrame[] boost = definitions[5].ExpandInputFrames();
        Require(boost[0].rise && !boost[1].rise && boost[3].rise,
            "GT-006 preserves the Z double-tap edges", ref assertions);
        TestPlayGoldenInputFrame[] lockShot = definitions[9].ExpandInputFrames();
        Require(lockShot[0].lockTarget && lockShot[3].shot && lockShot[3].direction == 4,
            "GT-010 fixes lock then lateral shot input", ref assertions);
    }

    static void VerifyPhase5Trace(ref int assertions)
    {
        TestPlayGoldenTickSnapshot snapshot = new TestPlayGoldenTickSnapshot
        {
            direction = 8,
            rise = true,
            boost = false,
            shot = true,
            movementEnergy = 975f,
            auxiliaryEnergy = 500f,
            hp = 1000f,
            frameIndex = 3,
            scriptIndex = 2,
            scriptTick = 4,
            actionTick = 5,
            poseHeldAtEnd = true,
            airborne = true,
            targetLocked = true,
            rootPosition = new Vector3(1.25f, 2f, -3f),
            rootRotation = new Quaternion(0f, 0.5f, 0f, 0.8660254f)
        };
        string phase4 = "{\"tick\":5,\"presentation\":{\"events\":[]}}";
        string first = TestPlayPhase5TickTrace.Serialize(phase4, snapshot);
        string second = TestPlayPhase5TickTrace.Serialize(phase4, snapshot);
        Require(first == second, "Phase 5 tick serialization is deterministic", ref assertions);
        Require(first.Contains("\"input\":{\"direction\":8") &&
                first.Contains("\"rise\":true") && first.Contains("\"shot\":true"),
            "Phase 5 trace adds explicit input state", ref assertions);
        Require(first.Contains("\"movementEnergy\":975") &&
                first.Contains("\"auxiliaryEnergy\":500"),
            "Phase 5 trace adds resource state", ref assertions);
        Require(first.Contains("\"scriptTick\":4") && first.Contains("\"actionTick\":5") &&
                first.Contains("\"poseHeld\":true"),
            "Phase 5 trace adds runtime boundaries", ref assertions);
        Require(first.Contains("\"rootPosition\":[1.25,2,-3]") &&
                first.Contains("\"rootRotation\":[0,0.5,0,0.8660254]"),
            "Phase 5 trace retains round-trip root transform values", ref assertions);
    }

    static void VerifySessionDeterminism(ref int assertions)
    {
        TestPlayGoldenSessionHeader header = new TestPlayGoldenSessionHeader
        {
            schemaVersion = 1,
            source = "unity-core",
            scenarioId = "GT-001",
            mechId = "Mech\"A",
            aniHash = "ani",
            sptHash = "spt",
            tickRate = 60,
            baseline = TestPlayGoldenBaselineKind.RealAniObserved
        };
        TestPlayGoldenTraceSession first = new TestPlayGoldenTraceSession(header);
        TestPlayGoldenTraceSession second = new TestPlayGoldenTraceSession(header);
        first.AddTick("{\"tick\":1}");
        first.AddTick("{\"tick\":2}");
        second.AddTick("{\"tick\":1}");
        second.AddTick("{\"tick\":2}");
        Require(first.TickCount == 2 && first.Ticks.Count == 2,
            "session retains ordered tick records", ref assertions);
        Require(TestPlayGoldenTraceSession.FindFirstMismatch(first, second) == -1,
            "equal sessions have no mismatch", ref assertions);
        Require(first.SerializeJsonLines() == second.SerializeJsonLines(),
            "equal sessions serialize byte-for-byte", ref assertions);
        Require(first.SerializeJsonLines().StartsWith("{\"session\":{\"schemaVersion\":1"),
            "session header is the first JSONL record", ref assertions);
        Require(first.SerializeJsonLines().Contains("Mech\\\"A"),
            "session header escapes identifiers", ref assertions);
        string hash = first.ComputeTraceHash();
        Require(hash.Length == 64 && hash == second.ComputeTraceHash(),
            "equal sessions produce the same SHA-256", ref assertions);

        second.AddTick("{\"tick\":3}");
        Require(TestPlayGoldenTraceSession.FindFirstMismatch(first, second) == 2,
            "length mismatch reports its first tick boundary", ref assertions);
        TestPlayGoldenTraceSession changed = new TestPlayGoldenTraceSession(header);
        changed.AddTick("{\"tick\":1}");
        changed.AddTick("{\"tick\":9}");
        Require(TestPlayGoldenTraceSession.FindFirstMismatch(first, changed) == 1,
            "content mismatch reports its first tick", ref assertions);
        Require(TestPlayGoldenTraceSession.FindFirstMismatch(null, first) == -2,
            "null comparison has a distinct diagnostic result", ref assertions);
    }

    static void VerifyDeterministicControllerPath(ref int assertions)
    {
        string first = RunSyntheticInputTrace(out int firstAction);
        string second = RunSyntheticInputTrace(out int secondAction);
        Require(firstAction == 0 && secondAction == 0,
            "forward press and release returns the synthetic runtime to idle", ref assertions);
        Require(first == second,
            "hardware-independent controller replay is byte-for-byte deterministic", ref assertions);
        Require(first.Contains("\"logicalAction\":1") && first.Contains("\"direction\":8"),
            "deterministic replay reaches movement action with explicit input", ref assertions);
        Require(first.Contains("\"grounded\":true") &&
                !first.Contains("\"velocityBefore\":[0,-"),
            "grounded deterministic replay preserves logical contact without collider drift", ref assertions);
        Require(first.Contains("\"movementEnergy\":") && first.Contains("\"presentation\":{") &&
                first.Contains("\"combat\":{") && first.Contains("\"runtime\":{") ,
            "Phase 5 replay combines prior Core traces", ref assertions);
    }

    static string RunSyntheticInputTrace(out int finalAction)
    {
        GameObject host = new GameObject("TestPlayPhase5Synthetic");
        GameObject root = new GameObject("TestPlayPhase5Root");
        try
        {
            ani2 data = new ani2 { animations = new List<animation>() };
            for (int i = 0; i < 200; i++)
            {
                data.animations.Add(new animation
                {
                    name = "Action" + i,
                    frames = new List<hod2v1>
                    {
                        new hod2v1("Frame" + i) { parts = new List<hod2v1_Part>() }
                    },
                    scripts = new List<script>()
                });
            }
            data.animations[0].scripts.Add(new script { unk = 1, time = 1f / 60f, squirrel = "" });
            data.animations[1].scripts.Add(new script
            {
                unk = 2,
                time = 1f / 60f,
                squirrel = "Move(0,0,0.1);AnimeLoop=1;"
            });

            RoboStructure robo = host.AddComponent<RoboStructure>();
            robo.root = root;
            robo.parts = new List<GameObject> { root };
            robo.ani = data;
            TestPlayController controller = host.AddComponent<TestPlayController>();
            controller.robo = robo;
            controller.useColliderGrounding = false;
            controller.logMotionAssignments = false;
            controller.BeginDeterministicTraceSession(TestPlayGoldenSetupKind.GroundedGun);

            StringBuilder trace = new StringBuilder();
            trace.Append(controller.SimulateDeterministicTraceTick(
                new TestPlayGoldenInputFrame { direction = 8 }));
            trace.Append('\n').Append(controller.SimulateDeterministicTraceTick(
                new TestPlayGoldenInputFrame { direction = 8 }));
            trace.Append('\n').Append(controller.SimulateDeterministicTraceTick(
                new TestPlayGoldenInputFrame()));
            finalAction = controller.CurrentActionSelection.logicalActionId;
            controller.EndDeterministicTraceSession();
            return trace.ToString();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    static void VerifyRealMechManifest(ref int assertions)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folder = Path.Combine(projectRoot, "Windom_Data", "Robo", "ガンダムTR-1ヘイズル改");
        string aniPath = Path.Combine(folder, "Script.ani");
        string sptPath = Path.Combine(folder, "Script.spt");
        Require(Directory.Exists(folder), "real-mech folder exists", ref assertions);
        Require(File.Exists(aniPath) && new FileInfo(aniPath).Length > 0,
            "real Script.ani exists and is non-empty", ref assertions);
        Require(File.Exists(sptPath) && new FileInfo(sptPath).Length > 0,
            "real Script.spt exists and is non-empty", ref assertions);
        byte[] signature = new byte[3];
        using (FileStream stream = File.OpenRead(aniPath))
            Require(stream.Read(signature, 0, signature.Length) == 3,
                "real ANI signature is readable", ref assertions);
        string containerSignature = Encoding.ASCII.GetString(signature);
        Require(containerSignature == "ANI" || containerSignature == "AN2",
            "real-mech animation uses a supported ANI/AN2 container", ref assertions);
        string firstHash = ComputeFileSha256(aniPath);
        string secondHash = ComputeFileSha256(aniPath);
        Require(firstHash.Length == 64 && firstHash == secondHash,
            "real ANI identity hash is deterministic", ref assertions);
        Require(ComputeFileSha256(sptPath).Length == 64,
            "real SPT identity hash is available for the session header", ref assertions);
    }

    static string ComputeFileSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    static void Require(bool condition, string message, ref int assertions)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("[TestPlayPhase5Verification] " + message);
    }
}
