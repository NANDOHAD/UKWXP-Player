using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Shared read-only entry. Legacy conversion remains an editor-host decision.</summary>
public static class MechLoader
{
    public static async Task<ani2> LoadAsync(string path, IProgress<int> progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var data = new ani2();
        if (!await data.load(path, progress) || data.structure == null || data.animations == null)
            throw new InvalidDataException($"'{path}' を読み込めませんでした。");
        cancellationToken.ThrowIfCancellationRequested();
        return data;
    }

    public static bool TryApplyHierarchy(ani2 data, out HodHierarchyRepairPlan plan, out string error)
    {
        if (data != null && data.sourceFormat == AniContainerFormat.LegacyAni)
        {
            plan = null;
            error = "";
            return true;
        }
        return HodHierarchyRepair.TryApplyChildCountFirst(data, out plan, out error);
    }

    public static string ReadSpt(RoboStructure robo)
    {
        if (robo == null || string.IsNullOrEmpty(robo.folder) || robo.transcoder == null)
            return null;
        string path = Path.Combine(robo.folder, "Script.spt");
        if (!File.Exists(path)) return null;
        byte[] source = File.ReadAllBytes(path);
        byte[] decoded = robo.transcoder.Transcode((byte[])source.Clone());
        // Original FUN_00573340 XORs while offset < length-4. At a multiple
        // of four this leaves the last full word plain (real Helesta SPT).
        // Existing editor saves encrypt every full word: preserve that format
        // too. Select the original variant only with its literal closing token;
        // never repair/invent a command or change the shared binary transcoder.
        int end = source.Length;
        if (end >= 4 && end % 4 == 0 && source[end-4] == (byte)')' &&
            source[end-3] == (byte)';' && source[end-2] == 13 && source[end-1] == 10)
            Array.Copy(source, end-4, decoded, end-4, 4);
        return USEncoder.ToEncoding.ToUnicode(decoded);
    }

    public static SptRuntimeData BindSpt(string text, Transform root, ParticleSystem prefab = null)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var data = SptParser.Parse(text);
        if (root != null)
            SptParser.BindTransforms(root, data);
        SptParser.BuildBurnerEffects(data, prefab);
        return data;
    }
}
