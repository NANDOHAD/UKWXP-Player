using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class TestPlayOriginalTraceComparison
{
    [MenuItem("Tools/WindomXP/Test Play/Compare Original Observation Trace")]
    public static void CompareFromMenu()
    {
        string selectedPath = EditorUtility.OpenFilePanel(
            "原作EXE観測traceを選択",
            Path.Combine(ProjectRoot, "Logs", "TestPlayOriginal"),
            "jsonl");
        if (string.IsNullOrEmpty(selectedPath))
            return;

        try
        {
            string originalText = File.ReadAllText(selectedPath, Encoding.UTF8);
            if (!TestPlayOriginalTraceSession.TryParseJsonLines(
                    originalText, out TestPlayOriginalTraceSession original, out string originalError))
                throw new InvalidDataException(originalError);

            ValidateOriginalExecutable(original.Header.exeHash);
            string unityPath = ResolveUnityReferencePath(original.Header);
            if (!File.Exists(unityPath))
                throw new FileNotFoundException(
                    "Unity reference trace with matching ANI/SPT hashes is missing. " +
                    "Run Selected-Mech GT-001 Reference Trace first.", unityPath);
            if (!TestPlayGoldenTraceSession.TryParseJsonLines(
                    File.ReadAllText(unityPath, Encoding.UTF8),
                    out TestPlayGoldenTraceSession unity,
                    out string unityError))
                throw new InvalidDataException(unityError);

            // Original debugger CSV values are decimal renderings of float32
            // memory, while Unity's round-trip JSON prints the stored float32
            // bit pattern.  Keep the comparer strict by default, but use the
            // explicit float32 serialization tolerance for this real-trace
            // menu comparison.
            TestPlayOriginalTraceComparisonOptions options =
                new TestPlayOriginalTraceComparisonOptions { rawFloatTolerance = 0.0000001d };
            TestPlayOriginalTraceComparisonResult result =
                TestPlayOriginalTraceComparer.Compare(unity, original, options);
            string report = TestPlayOriginalTraceComparer.BuildFirstMismatchReport(
                unity, original, result, options);
            string outputFolder = Path.Combine(ProjectRoot, "Logs", "TestPlayOriginalCompare");
            Directory.CreateDirectory(outputFolder);
            string outputPath = Path.Combine(outputFolder,
                original.Header.scenario + ".first-mismatch.txt");
            File.WriteAllText(outputPath, report, new UTF8Encoding(false));

            if (result.IsMatch)
                Debug.Log(report + " Output=" + outputPath);
            else
                Debug.LogWarning(report + "\nOutput=" + outputPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("[TestPlayOriginalTrace] Comparison failed: " + ex);
        }
    }

    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    public static string ResolveUnityReferencePath(TestPlayOriginalObservationHeader header)
    {
        string hashSpecific = Path.Combine(
            TestPlayGoldenTraceVerification.GetHashSpecificOutputFolder(header.aniHash, header.sptHash),
            header.scenario + ".unity-reference.jsonl");
        if (File.Exists(hashSpecific))
            return hashSpecific;

        string legacy = Path.Combine(ProjectRoot, "Logs", "TestPlayGolden",
            header.scenario + ".unity-reference.jsonl");
        if (!File.Exists(legacy))
            return hashSpecific;
        if (!TestPlayGoldenTraceSession.TryParseJsonLines(
                File.ReadAllText(legacy, Encoding.UTF8), out TestPlayGoldenTraceSession session, out _))
            return hashSpecific;
        return string.Equals(session.Header.aniHash, header.aniHash, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(session.Header.sptHash, header.sptHash, StringComparison.OrdinalIgnoreCase)
            ? legacy
            : hashSpecific;
    }

    static void ValidateOriginalExecutable(string expectedHash)
    {
        string path = Path.Combine(ProjectRoot, "Logs", "WindomXP", "WindomXP_orig.exe");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "Original executable is missing from the approved Logs/WindomXP environment.", path);
        string actualHash = ComputeFileSha256(path);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Original executable hash does not match the observation header. expected=" +
                expectedHash + " actual=" + actualHash);
        }
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
}
