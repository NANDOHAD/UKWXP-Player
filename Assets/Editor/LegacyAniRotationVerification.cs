using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Run through the HOD verification job; never overwrites sample data.</summary>
public static class LegacyAniRotationVerification
{
    static int assertions;

    // Shared transform math remains covered without requiring legacy ANI mech fixtures.
    internal static int RunSyntheticForJob()
    {
        assertions = 0;
        TestTrsDecomposition();
        return assertions;
    }

    internal static async Task<int> RunForJobAsync()
    {
        assertions = 0;
        TestTrsDecomposition();
        string root = Directory.GetParent(Application.dataPath).FullName;
        string folder = Path.Combine(Path.GetTempPath(), "WindomXP-Rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await TestSample(Path.Combine(root, "Windom_Data", "Robo", "Aerial Rebuild", "Script.ani"), folder, true);
            await TestSample(Path.Combine(root, "Windom_Data", "Robo", "ELS_QT", "Script.ani"), folder, false);
            Debug.Log($"[LegacyAniRotationVerification] {assertions} assertions passed.");
            return assertions;
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    static void TestTrsDecomposition()
    {
        Quaternion[] rotations = {
            Quaternion.identity,
            new Quaternion(1, 0, 0, 0), new Quaternion(0, 1, 0, 0), new Quaternion(0, 0, 1, 0),
            Quaternion.AngleAxis(180f, new Vector3(1, 2, 3).normalized),
            Quaternion.Euler(179.999f, 23f, -48f), Quaternion.Euler(73f, 18f, 35f)
        };
        Vector3[] scales = {
            Vector3.one, Vector3.one * 0.000001f, Vector3.one * 0.25f,
            new Vector3(0.000001f, 2f, 0.3f), new Vector3(-2, 3, 4),
            new Vector3(2, -3, 4), new Vector3(-2, -3, 4),
            new Vector3(0, 2, 3), new Vector3(2, 0, 3), new Vector3(2, 3, 0),
            new Vector3(0, 0, 3), new Vector3(0, 2, 0), new Vector3(2, 0, 0), Vector3.zero
        };
        foreach (Quaternion rotation in rotations)
        foreach (Vector3 scale in scales)
        {
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(1, -2, 3), rotation, scale);
            Quaternion actual = Utils.GetRotation(matrix);
            RequireUnit(actual, "synthetic rotation");
            RequireMatrix(matrix, Matrix4x4.TRS(Utils.GetPosition(matrix), actual, Utils.GetScale(matrix)),
                "synthetic TRS " + scale);
            if (scale.x > 0 && scale.y > 0 && scale.z > 0)
                Require(Mathf.Abs(Quaternion.Dot(rotation, actual)) > 0.999999f, "known rotation survives scaling");
        }
    }

    static async Task TestSample(string path, string folder, bool aerial)
    {
        string before = Hash(path);
        var source = new ani2();
        Require(await source.load(path), "sample public load " + path);
        var rawStructure = new hod1(path);
        using (var stream = File.OpenRead(path))
        {
            var reader = new BinaryReader(stream);
            reader.ReadBytes(259);
            Require(rawStructure.loadFromBinary(ref reader), "raw structure read");
        }
        int target = source.structure.parts.FindIndex(p => p.name == "GB3_rB.x");
        if (aerial)
        {
            Require(target >= 0, "GB3_rB.x exists");
            var part = source.structure.parts[target];
            RequireUnit(part.rotation, "GB3_rB.x structure");
            RequireMatrix(rawStructure.parts[target].transform,
                Matrix4x4.TRS(part.position, part.rotation, part.scale), "GB3_rB.x original matrix");
            Require(Mathf.Abs(part.rotation.w) < 0.001f, "GB3_rB.x is a half turn, not identity");
            Require(part.scale.x > 0 && part.scale.x < 0.000002f, "GB3_rB.x tiny scale is retained");
            Debug.Log("[LegacyAniRotationVerification] GB3_rB.x rotation=" + part.rotation.ToString("F8")
                + " Euler=" + part.rotation.eulerAngles.ToString("F4") + " scale=" + part.scale.ToString("F9"));
        }

        // Every frame is checked, including previously zero ELS_QT quaternions.
        foreach (var part in source.structure.parts) RequireUnit(part.rotation, "structure rotation");
        foreach (var anim in source.animations)
        for (int f = 0; f < anim.frames.Count; f++)
        for (int p = 0; p < anim.frames[f].parts.Count; p++)
        {
            var part = anim.frames[f].parts[p];
            RequireUnit(part.rotation, "frame rotation");
            Require(part.rotation.Equals(part.unk1) && part.rotation.Equals(part.unk2)
                && part.rotation.Equals(part.unk3), "legacy constraints follow rotation");
            RequireUnit(anim.interpolatePart(f, p, 0.5f).rotation, "editing preview interpolation");
        }

        string saved = Path.Combine(folder, aerial ? "aerial.ani" : "els.ani");
        source.save(saved);
        Require(Hash(saved) == before, "unchanged legacy save is byte exact");
        var reloaded = new ani2();
        Require(await reloaded.load(saved), "legacy public reload");
        RequireSamePoses(source, reloaded);

        string exported = Path.ChangeExtension(saved, ".an2");
        source.saveAsAn2(exported);
        var an2 = new ani2();
        Require(await an2.load(exported), "AN2 public reload");
        RequireSamePoses(source, an2);
        Require(HodHierarchyRepair.TryValidateWithoutRepair(an2, out string error), "AN2 hierarchy: " + error);

        if (aerial)
        {
            // Edit the original tiny-scale part, then ensure the matrix path
            // (rather than cached bytes) also reloads the intended rotation.
            var part = source.structure.parts[target];
            part.rotation = Quaternion.Euler(73f, 18f, 35f);
            source.structure.parts[target] = part;
            source.save(saved);
            var edited = new ani2();
            Require(await edited.load(saved), "edited tiny-scale legacy reload");
            RequireMatrix(Matrix4x4.TRS(part.position, part.rotation, part.scale),
                Matrix4x4.TRS(edited.structure.parts[target].position, edited.structure.parts[target].rotation,
                    edited.structure.parts[target].scale), "edited tiny-scale pose persists");

            string hodPath = Path.Combine(folder, "part.hod");
            using (var stream = File.Create(hodPath))
            {
                var writer = new BinaryWriter(stream);
                rawStructure.saveToBinary(ref writer);
            }
            var standalone = new ani2();
            Require(await standalone.load(hodPath), "standalone old HOD public load");
            RequireMatrix(rawStructure.parts[target].transform,
                Matrix4x4.TRS(standalone.structure.parts[target].position, standalone.structure.parts[target].rotation,
                    standalone.structure.parts[target].scale), "standalone HOD rotation");
        }
        Require(Hash(path) == before, "source sample hash unchanged");
    }

    static void RequireSamePoses(ani2 expected, ani2 actual)
    {
        Require(expected.structure.parts.Count == actual.structure.parts.Count, "part count preserved");
        Require(expected.animations.Count == actual.animations.Count, "animation count preserved");
        for (int p = 0; p < expected.structure.parts.Count; p++)
        {
            var a = expected.structure.parts[p];
            var b = actual.structure.parts[p];
            Require(a.rotation.Equals(b.rotation) && a.position.Equals(b.position) && a.scale.Equals(b.scale),
                "structure pose preserved");
        }
        for (int a = 0; a < expected.animations.Count; a++)
        {
            Require(expected.animations[a].frames.Count == actual.animations[a].frames.Count, "frame count preserved");
            for (int f = 0; f < expected.animations[a].frames.Count; f++)
            {
                var left = expected.animations[a].frames[f].parts;
                var right = actual.animations[a].frames[f].parts;
                Require(left.Count == right.Count, "frame part count preserved");
                for (int p = 0; p < left.Count; p++)
                    Require(left[p].rotation.Equals(right[p].rotation) && left[p].position.Equals(right[p].position)
                        && left[p].scale.Equals(right[p].scale), "frame pose preserved");
            }
        }
    }

    static void RequireMatrix(Matrix4x4 expected, Matrix4x4 actual, string label)
    {
        // Relative per-column tolerance also detects lost rotation at 1e-6 scale.
        for (int c = 0; c < 4; c++)
        {
            Vector4 a = expected.GetColumn(c), b = actual.GetColumn(c);
            Require((a - b).magnitude <= Mathf.Max(a.magnitude * 0.00002f, 1e-12f), label + " column " + c);
        }
    }

    static void RequireUnit(Quaternion value, string label)
    {
        float norm = Quaternion.Dot(value, value);
        Require(!float.IsNaN(norm) && Mathf.Abs(norm - 1f) < 0.00001f, label + " is finite and unit length");
    }

    static void Require(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("Legacy rotation: " + label);
        assertions++;
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream));
    }
}
