using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class TestPlayPhase6Verification
{
    const string ExpectedOriginalExeSha256 =
        "ec5a09973cd00c1bab7ad7fe284293c06a415c65378410e31e4534327ccc20c1";

    public static int RunAll()
    {
        int assertions = 0;
        VerifyObservationContract(ref assertions);
        VerifyMoveStageComparison(ref assertions);
        VerifyPartialComparison(ref assertions);
        VerifyMismatchDiagnostics(ref assertions);
        VerifyTransformTolerance(ref assertions);
        VerifyOriginalExecutableIdentity(ref assertions);
        VerifyDataMatchedReferencePath(ref assertions);
        return assertions;
    }

    static void VerifyObservationContract(ref int assertions)
    {
        string jsonl = BuildObservation(
            new[] { "tick", "input.direction", "logicalAction" },
            Tick(0, 0, 0, "[0,0,0]"),
            Tick(1, 8, 1, "[0,0,0.1]"));
        Require(TestPlayOriginalTraceSession.TryParseJsonLines(jsonl, out TestPlayOriginalTraceSession session,
                out string error) && string.IsNullOrEmpty(error),
            "valid original-observation JSONL parses", ref assertions);
        Require(session.Header.source == "original-observation" && session.Header.schemaVersion == 1,
            "observation contract keeps an explicit source and schema", ref assertions);
        Require(session.TickCount == 2 && session.Ticks[1].Contains("\"direction\":8"),
            "observation session retains ordered raw tick records", ref assertions);

        string withoutCoverage = BuildObservation(new string[0], Tick(0, 0, 0, "[0,0,0]"));
        Require(!TestPlayOriginalTraceSession.TryParseJsonLines(withoutCoverage, out _, out string coverageError) &&
                coverageError.Contains("observedFields"),
            "observation without an explicit field coverage is rejected", ref assertions);

        string nonContiguous = BuildObservation(
            new[] { "tick" },
            Tick(0, 0, 0, "[0,0,0]"),
            Tick(2, 0, 0, "[0,0,0]"));
        Require(!TestPlayOriginalTraceSession.TryParseJsonLines(nonContiguous, out _, out string tickError) &&
                tickError.Contains("contiguous"),
            "observation ticks must be contiguous from the declared capture origin", ref assertions);

        TestPlayGoldenTraceSession unity = BuildUnitySession(
            Tick(1, 0, 0, "[0,0,0]"), Tick(2, 8, 1, "[0,0,0.1]"));
        Require(TestPlayGoldenTraceSession.TryParseJsonLines(
                unity.SerializeJsonLines(), out TestPlayGoldenTraceSession reparsed, out string unityError) &&
                string.IsNullOrEmpty(unityError),
            "Unity reference JSONL parser accepts the existing tick-one origin", ref assertions);
        Require(reparsed.TickCount == 2 && reparsed.Header.scenarioId == "GT-001",
            "Unity reference parser preserves header and tick records", ref assertions);

        string canonicalMechId = jsonl.Replace(
            "\"mechId\":\"ガンダムTR-1ヘイズル改\"",
            "\"mechId\":\"KD-03\"");
        Require(TestPlayOriginalTraceSession.TryParseJsonLines(
                canonicalMechId, out TestPlayOriginalTraceSession canonicalOriginal, out _) &&
                TestPlayOriginalTraceComparer.Compare(
                    BuildUnitySession(Tick(0, 0, 0, "[0,0,0]"), Tick(1, 8, 1, "[0,0,0.1]")),
                    canonicalOriginal).IsMatch,
            "canonical ROBO id and Unity display mech name remain comparable", ref assertions);
    }

    static void VerifyPartialComparison(ref int assertions)
    {
        TestPlayGoldenTraceSession unity = BuildUnitySession(
            Tick(0, 0, 0, "[0,0,0]"),
            Tick(1, 8, 1, "[0,0,0.1]"));
        string jsonl = BuildObservation(
            new[] { "tick", "input.direction", "logicalAction", "scriptedVelocity", "runtime.grounded" },
            Tick(0, 0, 0, "[0,0,0]"),
            Tick(1, 8, 1, "[0,0,0.1]"));
        Require(TestPlayOriginalTraceSession.TryParseJsonLines(jsonl,
            out TestPlayOriginalTraceSession original, out _),
            "partial observation fixture parses", ref assertions);
        TestPlayOriginalTraceComparisonResult result =
            TestPlayOriginalTraceComparer.Compare(unity, original);
        Require(result.IsMatch, "only explicitly observed fields participate in comparison", ref assertions);
        Require(result.comparedTicks == 2 && result.comparedValues == 10,
            "comparison reports tick and value coverage", ref assertions);
        Require(TestPlayOriginalTraceComparer.BuildFirstMismatchReport(unity, original, result)
                .Contains("Match across 2 ticks"),
            "matching comparison has a concise coverage report", ref assertions);
    }

    static void VerifyMoveStageComparison(ref int assertions)
    {
        string[] fields =
        {
            "tick", "scriptedVelocityBeforeRetention", "moveRetention",
            "scriptedVelocityAfterRetention"
        };
        string[] ticks =
        {
            Tick(0, 8, 1, "[0,0,0.08]", "", "[0,0,0]", 1f, "[0,0,0.08]"),
            Tick(1, 0, 0, "[0,0,0.08]", "", "[0,0,0]", 0.8f, "[0,0,0.064]")
        };
        TestPlayGoldenTraceSession unity = BuildUnitySession(ticks);
        string jsonl = BuildObservation(2, fields, ticks);
        Require(TestPlayOriginalTraceSession.TryParseJsonLines(jsonl,
                out TestPlayOriginalTraceSession original, out string parseError) &&
                string.IsNullOrEmpty(parseError) && original.Header.schemaVersion == 2,
            "schema v2 Move-stage observation parses", ref assertions);
        TestPlayOriginalTraceComparisonResult result =
            TestPlayOriginalTraceComparer.Compare(unity, original);
        Require(result.IsMatch && result.comparedTicks == 2 && result.comparedValues == 8,
            "entry Move, action retention, and post-retention Move compare at the same tick stage",
            ref assertions);
    }

    static void VerifyMismatchDiagnostics(ref int assertions)
    {
        TestPlayGoldenTraceSession unity = BuildUnitySession(
            Tick(0, 0, 0, "[0,0,0]"),
            Tick(1, 8, 1, "[0,0,0.1]"),
            Tick(2, 0, 0, "[0,0,0]"));
        string changed = BuildObservation(
            new[] { "tick", "logicalAction" },
            Tick(0, 0, 0, "[0,0,0]"),
            Tick(1, 8, 9, "[0,0,0.1]"),
            Tick(2, 0, 0, "[0,0,0]"));
        TestPlayOriginalTraceSession.TryParseJsonLines(changed, out TestPlayOriginalTraceSession original, out _);
        TestPlayOriginalTraceComparisonResult mismatch = TestPlayOriginalTraceComparer.Compare(unity, original);
        Require(!mismatch.IsMatch && mismatch.firstMismatch.kind == TestPlayOriginalTraceMismatchKind.Value,
            "different observed value is classified as a value mismatch", ref assertions);
        Require(mismatch.firstMismatch.tickIndex == 1 && mismatch.firstMismatch.field == "logicalAction" &&
                mismatch.firstMismatch.originalValue == "9" && mismatch.firstMismatch.unityValue == "1",
            "first mismatch identifies tick field and both values", ref assertions);
        string report = TestPlayOriginalTraceComparer.BuildFirstMismatchReport(unity, original, mismatch,
            new TestPlayOriginalTraceComparisonOptions { contextRadius = 1 });
        Require(report.Contains("First mismatch") && report.Contains("tick 0 original") &&
                report.Contains("tick 2 unity"),
            "mismatch report includes bounded surrounding context", ref assertions);

        string missingTick = Tick(0, 0, 0, "[0,0,0]").Replace(
            ",\"resources\":{\"hp\":1000,\"movementEnergy\":1000,\"auxiliaryEnergy\":500}", "");
        string missing = BuildObservation(
            new[] { "tick", "resources.movementEnergy" },
            missingTick);
        TestPlayOriginalTraceSession.TryParseJsonLines(missing, out TestPlayOriginalTraceSession missingSession, out _);
        TestPlayOriginalTraceComparisonResult missingResult = TestPlayOriginalTraceComparer.Compare(
            BuildUnitySession(Tick(0, 0, 0, "[0,0,0]")), missingSession);
        Require(missingResult.firstMismatch.kind == TestPlayOriginalTraceMismatchKind.MissingField,
            "declared but absent original field is not mistaken for a zero value", ref assertions);

        string unsupported = BuildObservation(
            new[] { "tick", "unknown.value" },
            Tick(0, 0, 0, "[0,0,0]", "\"unknown\":{\"value\":1},"));
        TestPlayOriginalTraceSession.TryParseJsonLines(unsupported,
            out TestPlayOriginalTraceSession unsupportedSession, out _);
        TestPlayOriginalTraceComparisonResult unsupportedResult = TestPlayOriginalTraceComparer.Compare(
            BuildUnitySession(Tick(0, 0, 0, "[0,0,0]")), unsupportedSession);
        Require(unsupportedResult.firstMismatch.kind == TestPlayOriginalTraceMismatchKind.UnsupportedField,
            "unknown comparison paths are explicit diagnostics", ref assertions);

        string shortObservation = BuildObservation(new[] { "tick" }, Tick(0, 0, 0, "[0,0,0]"));
        TestPlayOriginalTraceSession.TryParseJsonLines(shortObservation,
            out TestPlayOriginalTraceSession shortSession, out _);
        TestPlayOriginalTraceComparisonResult lengthResult = TestPlayOriginalTraceComparer.Compare(unity, shortSession);
        Require(lengthResult.firstMismatch.kind == TestPlayOriginalTraceMismatchKind.TickCount &&
                lengthResult.firstMismatch.tickIndex == 1,
            "length divergence reports the first unavailable tick", ref assertions);
    }

    static void VerifyTransformTolerance(ref int assertions)
    {
        string unityTick = Tick(0, 0, 0, "[0,0,0]", "", "[1,2,3]");
        string closeTick = Tick(0, 0, 0, "[0,0,0]", "", "[1.000001,2,3]");
        string farTick = Tick(0, 0, 0, "[0,0,0]", "", "[1.001,2,3]");
        TestPlayGoldenTraceSession unity = BuildUnitySession(unityTick);
        TestPlayOriginalTraceSession.TryParseJsonLines(
            BuildObservation(new[] { "tick", "runtime.rootPosition" }, closeTick),
            out TestPlayOriginalTraceSession close, out _);
        TestPlayOriginalTraceSession.TryParseJsonLines(
            BuildObservation(new[] { "tick", "runtime.rootPosition" }, farTick),
            out TestPlayOriginalTraceSession far, out _);
        Require(TestPlayOriginalTraceComparer.Compare(unity, close).IsMatch,
            "coordinate conversion values use the explicit transform tolerance", ref assertions);
        Require(!TestPlayOriginalTraceComparer.Compare(unity, far).IsMatch,
            "transform differences beyond tolerance remain visible", ref assertions);

        string rawForceChanged = unityTick.Replace("\"force\":[0,0,0]", "\"force\":[0,0,0.000001]");
        TestPlayOriginalTraceSession.TryParseJsonLines(
            BuildObservation(new[] { "tick", "force" }, rawForceChanged),
            out TestPlayOriginalTraceSession raw, out _);
        Require(!TestPlayOriginalTraceComparer.Compare(unity, raw).IsMatch,
            "raw original Force values remain exact by default", ref assertions);
    }

    static void VerifyOriginalExecutableIdentity(ref int assertions)
    {
        string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
        string exePath = Path.Combine(root, "Logs", "WindomXP", "WindomXP_orig.exe");
        Require(File.Exists(exePath), "original executable capture target exists", ref assertions);
        string hash = ComputeFileSha256(exePath);
        Require(string.Equals(hash, ExpectedOriginalExeSha256, StringComparison.OrdinalIgnoreCase),
            "capture target hash matches the decompiled x86 snapshot", ref assertions);
        TestPlayGoldenScenarioDefinition gt001 = TestPlayGoldenScenarioCatalog.Find("GT-001");
        TestPlayGoldenInputFrame[] frames = gt001.ExpandInputFrames();
        Require(frames.Length == 22 && frames[2].direction == 8 && frames[14].direction == 0,
            "GT-001 capture contract fixes press and release tick boundaries", ref assertions);
    }

    static void VerifyDataMatchedReferencePath(ref int assertions)
    {
        string aniHash = new string('a', 64);
        string sptHash = new string('b', 64);
        string folder = TestPlayGoldenTraceVerification.GetHashSpecificOutputFolder(aniHash, sptHash);
        Require(Path.GetFileName(folder) == "aaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbb",
            "data-matched reference folder uses a Windows-safe abbreviated key", ref assertions);

        string resolved = TestPlayOriginalTraceComparison.ResolveUnityReferencePath(
            new TestPlayOriginalObservationHeader
            {
                scenario = "GT-001",
                aniHash = aniHash,
                sptHash = sptHash
            });
        Require(resolved.EndsWith(Path.Combine("aaaaaaaaaaaaaaaa_bbbbbbbbbbbbbbbb",
                "GT-001.unity-reference.jsonl"), StringComparison.OrdinalIgnoreCase),
            "comparison resolves the reference by original ANI/SPT hashes", ref assertions);
    }

    static TestPlayGoldenTraceSession BuildUnitySession(params string[] ticks)
    {
        TestPlayGoldenTraceSession session = new TestPlayGoldenTraceSession(new TestPlayGoldenSessionHeader
        {
            schemaVersion = 1,
            source = "unity-core",
            scenarioId = "GT-001",
            mechId = "ガンダムTR-1ヘイズル改",
            aniHash = "ani-hash",
            sptHash = "spt-hash",
            tickRate = 60,
            baseline = TestPlayGoldenBaselineKind.RealAniObserved
        });
        for (int i = 0; i < ticks.Length; i++)
            session.AddTick(ticks[i]);
        return session;
    }

    static string BuildObservation(string[] fields, params string[] ticks)
    {
        return BuildObservation(1, fields, ticks);
    }

    static string BuildObservation(int schemaVersion, string[] fields, params string[] ticks)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("{\"session\":{\"schemaVersion\":").Append(schemaVersion)
            .Append(",\"source\":\"original-observation\",")
            .Append("\"scenario\":\"GT-001\",\"mechId\":\"ガンダムTR-1ヘイズル改\",")
            .Append("\"exeHash\":\"").Append(ExpectedOriginalExeSha256).Append("\",")
            .Append("\"aniHash\":\"ani-hash\",\"sptHash\":\"spt-hash\",\"tickRate\":60,")
            .Append("\"tickOrigin\":0,\"normalizationProfile\":\"test-identity-v1\",")
            .Append("\"observedFields\":[");
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append('"').Append(fields[i]).Append('"');
        }
        builder.Append("]}}");
        for (int i = 0; i < ticks.Length; i++)
            builder.Append('\n').Append(ticks[i]);
        return builder.ToString();
    }

    static string Tick(
        int tick,
        int direction,
        int logicalAction,
        string scriptedVelocity,
        string prefix = "",
        string rootPosition = "[0,0,0]",
        float moveRetention = 1f,
        string scriptedVelocityAfterRetention = null)
    {
        string afterRetention = scriptedVelocityAfterRetention ?? scriptedVelocity;
        return "{" + prefix +
               "\"tick\":" + tick +
               ",\"requestedAction\":" + logicalAction +
               ",\"logicalAction\":" + logicalAction +
               ",\"poseAction\":" + logicalAction +
               ",\"scriptAction\":" + logicalAction +
               ",\"weaponMode\":\"Gun\",\"primaryChannel\":0,\"secondaryChannel\":1" +
               ",\"locomotion\":\"GroundedIdle\",\"velocityBefore\":[0,0,0]" +
               ",\"force\":[0,0,0],\"velocityAfter\":[0,0,0]" +
               ",\"scriptedVelocity\":" + scriptedVelocity +
               ",\"scriptedVelocityBeforeRetention\":" + scriptedVelocity +
               ",\"moveRetention\":" + moveRetention.ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ",\"scriptedVelocityAfterRetention\":" + afterRetention +
               ",\"requestedDisplacement\":" + scriptedVelocity +
               ",\"riseClampApplied\":false,\"gravityApplied\":false" +
               ",\"retention\":0.9,\"velocityMultiplier\":1" +
               ",\"input\":{\"direction\":" + direction +
               ",\"rise\":false,\"boost\":false,\"shot\":false,\"melee\":false," +
               "\"guard\":false,\"lock\":false}" +
               ",\"resources\":{\"hp\":1000,\"movementEnergy\":1000,\"auxiliaryEnergy\":500}" +
               ",\"runtime\":{\"frame\":0,\"script\":0,\"scriptTick\":0,\"actionTick\":" + tick +
               ",\"poseHeld\":false,\"airborne\":false,\"grounded\":true," +
               "\"targetLocked\":false,\"rootPosition\":" + rootPosition +
               ",\"rootRotation\":[0,0,0,1]}}";
    }

    static string ComputeFileSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2"));
            return builder.ToString();
        }
    }

    static void Require(bool condition, string message, ref int assertions)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("[TestPlayPhase6Verification] " + message);
    }
}
