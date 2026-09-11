using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Owns only assets created by this builder; editor/preview assets are never enrolled.</summary>
public sealed class MechAssetLease
{
    public GameObject Host { get; internal set; }
    /// <summary>False when Host is a scene mech slot that must survive ReleaseAsync.</summary>
    public bool OwnsHost { get; internal set; } = true;
    public RoboStructure Robo { get; internal set; }
    public SptRuntimeData Spt { get; internal set; }
    readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    Task releaseTask;
    public int RemainingOwnedAssetCount => owned.FindAll(asset => asset != null).Count;
    internal void Register(UnityEngine.Object asset)
    {
        if (asset != null && !owned.Contains(asset)) owned.Add(asset);
    }

    public Task ReleaseAsync() => releaseTask ?? (releaseTask = ReleaseCoreAsync());

    async Task ReleaseCoreAsync()
    {
        if (Robo != null)
        {
            Robo.RegisterOwnedAsset = null;
            // Parts may not yet have been parented when construction fails.
            foreach (var part in Robo.parts) Register(part);
            // Borrowed slots keep the GameObject; destroy only the runtime component.
            if (!OwnsHost) Register(Robo);
        }
        if (OwnsHost) Register(Host);
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            if (owned[i] == null) continue;
            if (owned[i] is GameObject go) go.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(owned[i]);
            else UnityEngine.Object.DestroyImmediate(owned[i]);
        }
        if (Application.isPlaying)
        {
            await Task.Yield();
            while (owned.Exists(asset => asset != null)) await Task.Yield();
        }
        owned.Clear();
        Robo = null;
        Spt = null;
        if (OwnsHost) Host = null;
    }
}

public static class UnityMechBuilder
{
    // Call on the Unity main thread. Synchronous Assimp import cannot be interrupted mid-file.
    public static async Task<MechAssetLease> BuildAsync(ani2 data, string sourcePath,
        CancellationToken cancellationToken = default, GameObject host = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (data == null || data.sourceFormat == AniContainerFormat.Hod ||
            data.animations == null || data.animations.Count == 0)
            throw new InvalidDataException("独立起動にはアニメーションを含むANI/AN2が必要です。");
        if (!MechLoader.TryApplyHierarchy(data, out var plan, out string error))
            throw new InvalidDataException(error);
        var lease = new MechAssetLease();
        var diagnostics = new List<string>();
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (message.StartsWith("[RoboStructure]") &&
                (type == LogType.Warning || type == LogType.Error || type == LogType.Exception))
                diagnostics.Add(message);
        };
        try
        {
            if (host != null)
            {
                if (host.GetComponent<RoboStructure>() != null || host.GetComponent<TestPlayController>() != null )
                    throw new InvalidOperationException("機体スロットが空ではありません: " + host.name);
                lease.OwnsHost = false;
                lease.Host = host;
            }
            else
            {
                lease.OwnsHost = true;
                lease.Host = new GameObject("StandaloneMech");
            }
            lease.Robo = lease.Host.AddComponent<RoboStructure>();
            var robo = lease.Robo;
            robo.RegisterOwnedAsset = lease.Register;
            robo.InitializeRuntime();
            robo.folder = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            robo.filename = Path.GetFileName(sourcePath);
            robo.ani = data;
            Application.logMessageReceived += capture;
            try { robo.buildStructure(data.structure); }
            finally { Application.logMessageReceived -= capture; }
            foreach (var part in robo.parts)
                if (part.transform.parent == null) part.transform.SetParent(lease.Host.transform, false);
            if (diagnostics.Count > 0)
                throw new InvalidDataException(string.Join("\n", diagnostics));
            for (int i = 1; i < robo.parts.Count; i++)
            {
                var filter = robo.parts[i].GetComponent<MeshFilter>();
                if (File.Exists(Path.Combine(robo.folder, data.structure.parts[i].name)) &&
                    (filter == null || filter.sharedMesh == null))
                    throw new InvalidDataException("モデルを構築できませんでした: " + data.structure.parts[i].name);
            }
            cancellationToken.ThrowIfCancellationRequested();
            string text = MechLoader.ReadSpt(robo);
            if (string.IsNullOrEmpty(text)) throw new InvalidDataException("Script.sptがありません: " + robo.folder);
            lease.Spt = MechLoader.BindSpt(text, robo.root.transform);
            cancellationToken.ThrowIfCancellationRequested();
            return lease;
        }
        catch
        {
            await lease.ReleaseAsync();
            throw;
        }
    }
}
