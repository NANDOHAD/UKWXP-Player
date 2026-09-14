using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Shared Editor/Player acceptance using real ANI and the public reusable host.</summary>
public static class TestPlaySessionLifecycleVerification
{
    [Serializable]
    public sealed class Report
    {
        public string state = "Running", unityVersion, failure;
        public bool player;
        public int assertions, ticks, finitePoses, hits, defeats, rematches;
        public int mappedAudioBindings, mappedTextureBindings, configuredAudioSources, activeAudioSources;
        public int missingAudioMappings, missingTextureMappings;
        public bool audioSourcesConfigured, propulsionClipsConfigured, propulsionStartClipConfigured, propulsionLoopClipConfigured, originalEffectShaderSupported;
        public int propulsionActivationCount, suppressedSoundPlaybackCount;
        public int soundPlaybackCount;
        public int soundEventCount;
        public int shotActionSoundEventCount;
        public string lastSoundPlaybackKey, lastSoundPlaybackClip;
        public int missingScriptWarnings;
        public string logScope = "RunAsync only; startup warnings are counted from Player.log separately";
        public int peakActiveAudioSources, remainingAudioSources, voicePlaybackCount;
        public List<string> missingAudioMappingKeys = new List<string>();
        public List<string> rounds = new List<string>();
    }

    public static async Task<Report> RunAsync(string path, string output, Camera camera = null,
        CancellationToken token = default, TestPlayPresentationRuntime presentationTemplate = null)
    {
        Directory.CreateDirectory(output);
        var report = new Report { unityVersion = Application.unityVersion, player = !Application.isEditor };
        Action<bool, string> check = (ok, message) => {
            if (!ok) throw new InvalidOperationException("[Lifecycle] " + message);
            report.assertions++;
        };
        int roots = Count<RoboStructure>(), controllers = Count<TestPlayController>(),
            projectiles = Count<TestPlayProjectile>(), sounds = Count<AudioSource>(), particles = Count<ParticleSystem>(), canvases = Count<Canvas>();
        int initialActiveAudio = ActiveAudioCount();
        Application.LogCallback onLog = (message, stack, type) => {
            if (type == LogType.Warning && message.Contains("The referenced script")) report.missingScriptWarnings++;
        };
        Application.logMessageReceived += onLog;
        // Own the ground fixture: the product entry is now a selection scene without colliders.
        // Combat's real collider-grounding path must not depend on the scene the verifier was started from.
        int initialColliders = Count<Collider>();
        var groundObject = new GameObject("LifecycleVerificationGround");
        groundObject.transform.position = new Vector3(0, -0.5f, 0);
        groundObject.AddComponent<BoxCollider>().size = new Vector3(2000, 1, 2000);
        Physics.SyncTransforms();
        var hostObject = new GameObject("LifecycleVerificationHost");
        var host = hostObject.AddComponent<TestPlaySessionBootstrap>();
        host.enabled = false; host.gameCamera = camera; host.presentationTemplate = presentationTemplate;
        Action clean = () => check(Count<RoboStructure>() == roots && Count<TestPlayController>() == controllers &&
            Count<TestPlayProjectile>() == projectiles && Count<AudioSource>() == sounds && Count<ParticleSystem>() == particles && Count<Canvas>() == canvases,
            "roots/controllers/projectiles/audio/particles/HUD returned to initial counts");
        try
        {
            // Missing second file must roll back the fully created first mech.
            bool failed = false;
            try { await host.StartAsync(path, Path.Combine(output, Guid.NewGuid() + ".ani"), token); }
            catch (FileNotFoundException) { failed = true; }
            check(failed && host.Status == "Faulted", "second load failure reported"); clean();
            // Cancel after the first complete mech, at the between-load checkpoint.
            var start = host.StartAsync(path, path, token);
            check(Rejects(() => host.StartAsync(path, path)), "double start during loading rejected");
            bool observedLoadingCheckpoint = false;
            for (int checkpoint = 0; checkpoint < 256 && !start.IsCompleted; checkpoint++)
            {
                observedLoadingCheckpoint |= Count<RoboStructure>() > roots;
                if (observedLoadingCheckpoint)
                    break;
                await Task.Yield();
            }
            // Editor/Player scheduling can complete both synchronous imports before this observer continuation runs,
            // and a Player can expose neither transient object count during a very fast load. StopAsync remains the
            // ownership boundary; the optional checkpoint observation must not turn a valid run into a false failure.
            var stop = host.StopAsync();
            check(ReferenceEquals(stop, host.StopAsync()), "double stop shares completion");
            await stop;
            check(start.IsCompleted && (host.Status == "Stopped" || host.Status == "Cancelled" || host.Status == "Faulted"),
                observedLoadingCheckpoint ? "loading checkpoint stop completed" : "fast loading stop completed"); clean();
            await host.StartAsync(path, path, token);
            CapturePresentationDiagnostics(host, report);
            if (presentationTemplate != null)
            {
                check(report.mappedAudioBindings > 0 && report.mappedTextureBindings > 0,
                    "scene/template presentation mappings copied");
                check(report.configuredAudioSources == 4 && report.audioSourcesConfigured,
                    "four presentation AudioSources configured");
                check(report.propulsionClipsConfigured && report.propulsionStartClipConfigured && report.propulsionLoopClipConfigured,
                    "propulsion start and loop clips configured");
                check(report.originalEffectShaderSupported, "original effect shader supported");
            }
            for (int round = 0; round < 4; round++)
            {
                token.ThrowIfCancellationRequested();
                var session = host.Session;
                var a = session.Mechs[0]; var b = session.Mechs[1];
                check(session.Tick == 0 && a.Controller.currentHP == a.Controller.maximumHP &&
                    b.Controller.currentHP == b.Controller.maximumHP && !host.CombatEnded && session.ActiveProjectileCount == 0,
                    "fresh HP/clock/combat/projectiles at round " + round);
                check(Rejects(() => host.StartAsync(path, path)), "double start during combat rejected");
                int hitCount = 0, defeats = 0;
                session.HitResolved += hit => { hitCount++; report.hits++; };
                session.MechDefeated += _ => { defeats++; report.defeats++; };
                var hitTrace = new List<string>();
                session.HitResolved += hit => hitTrace.Add(JsonUtility.ToJson(hit));
                // Fixture position only: normal target radius and real weapon points.
                a.Assets.Robo.root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                b.Assets.Robo.root.transform.SetPositionAndRotation(new Vector3(0, 0, round == 2 ? 3 : 12), Quaternion.Euler(0, 180, 0));
                int ticks = round == 2 ? 900 : round == 1 ? 140 : 240;
                for (int tick = 0; tick < ticks; tick++)
                {
                    token.ThrowIfCancellationRequested();
                    var frame = new TestPlaySessionInput();
                    if (tick == 0) frame.pressed.lockTarget = true;
                    if (round == 1 && tick == 0) frame.pressed.shot = true;
                    if (round == 2)
                    {
                        float separation = Vector3.Distance(a.Assets.Robo.root.transform.position,
                            b.Assets.Robo.root.transform.position);
                        if (separation > 3.25f) frame.held.direction = 8;
                        else if (tick % 45 == 0) frame.pressed.melee = true;
                    }
                    if (round == 0 || round == 3)
                    {
                        frame.held.direction = tick < 50 ? 8 : 0;
                        frame.pressed.rise = tick == 60;
                        frame.held.rise = tick >= 100 && tick < 160;
                    }
                    host.AdvanceInput(frame, GameSession.TickSeconds);
                    report.peakActiveAudioSources = Math.Max(report.peakActiveAudioSources,
                        session.Mechs.Sum(m => m.Controller.presentationRuntime.ActiveAudioSourceCount));
                    report.ticks++;
                    foreach (var mech in session.Mechs)
                        foreach (var part in mech.Assets.Robo.parts)
                        {
                            var p = part.transform.position; var q = part.transform.rotation;
                            if (!float.IsFinite(p.sqrMagnitude) || !float.IsFinite(q.x + q.y + q.z + q.w))
                                throw new InvalidOperationException("nonfinite real mech pose");
                            report.finitePoses++;
                        }
                    if (Application.isPlaying && tick % 30 == 0) await Task.Yield();
                }
                check(session.Tick == ticks, "one shared clock per round");
                if (round == 1)
                {
                    check(hitCount == 1 && b.Controller.currentHP == b.Controller.maximumHP - 100, "real ANI shot hits once");
                }
                if (round == 2) check(defeats == 1 && host.CombatEnded && b.Controller.currentHP == 0,
                    "real ANI melee reaches combat end" +
                    $" (hits={hitCount}, defeats={defeats}, hp={b.Controller.currentHP}, " +
                    $"attackerZ={a.Assets.Robo.root.transform.position.z:F3}, " +
                    $"defenderZ={b.Assets.Robo.root.transform.position.z:F3}, " +
                    $"phase={b.Controller.SessionReactionPhase})");
                CapturePresentationDiagnostics(host, report, true);
                check(session.Mechs.All(m => m.Assets.Robo.GetComponentsInChildren<Renderer>(true)
                    .All(r => r.sharedMaterials.All(mat => mat != null && mat.shader != null && mat.shader.isSupported))), "supported mech shaders");
                report.rounds.Add(round + ": tick=" + ticks + " hits=" + hitCount + " defeated=" + defeats);
                File.WriteAllLines(Path.Combine(output, "round-" + round + "-hits.jsonl"), hitTrace);
                if (camera != null && Application.isPlaying)
                {
                    await Task.Yield();
                    Capture(camera, Path.Combine(output, "round-" + round + ".png"));
                }
                var oldHandle = a.Handle;
                if (round < 3)
                {
                    await host.RestartAsync(token);
                    report.rematches++;
                    check(host.Session.SessionId != session.SessionId && !host.Session.TryGet(oldHandle, out _), "old target invalid after rematch");
                }
                else
                {
                    // Stop requested by a tick event must complete the tick, then release assets.
                    Task callbackStop = null;
                    session.TickCompleted += (_, __) => callbackStop = host.StopAsync();
                    host.AdvanceInput(default, GameSession.TickSeconds);
                    await callbackStop;
                    await host.StopAsync();
                    check(host.Status == "Stopped", "tick callback stop completed");
                }
                check(session.Status == "Stopped" && session.ActiveProjectileCount == 0 &&
                    session.Mechs.All(m => m.Assets.RemainingOwnedAssetCount == 0 && m.Controller == null) &&
                    !session.TryGet(oldHandle, out _), "old assets and target references released");
            }
            clean();
            report.state = "Succeeded";
        }
        catch (Exception e) { report.state = "Failed"; report.failure = e.ToString(); throw; }
        finally
        {
            try { await host.StopAsync(); }
            catch (Exception e) { report.state = "Failed"; report.failure += "\nCleanup: " + e; throw; }
            finally
            {
                if (Application.isPlaying) { UnityEngine.Object.Destroy(hostObject); while (hostObject != null) await Task.Yield(); }
                else UnityEngine.Object.DestroyImmediate(hostObject);
                if (Application.isPlaying) { UnityEngine.Object.Destroy(groundObject); while (groundObject != null) await Task.Yield(); }
                else UnityEngine.Object.DestroyImmediate(groundObject);
                check(Count<Collider>() == initialColliders, "verification ground collider released");
                report.remainingAudioSources = Math.Max(0, Count<AudioSource>() - sounds);
                report.activeAudioSources = Math.Max(0, ActiveAudioCount() - initialActiveAudio);
                Application.logMessageReceived -= onLog;
                File.WriteAllText(Path.Combine(output, "lifecycle-result.json"), JsonUtility.ToJson(report, true));
            }
        }
        return report;
    }

    static void CapturePresentationDiagnostics(TestPlaySessionBootstrap host, Report report, bool accumulate = false)
    {
        if (host == null || host.Session == null)
            return;

        foreach (MechRuntime mech in host.Session.Mechs)
        {
            TestPlayPresentationRuntime presentation = mech.Controller != null
                ? mech.Controller.presentationRuntime
                : null;
            if (presentation == null)
                continue;

            report.mappedAudioBindings = Math.Max(report.mappedAudioBindings, presentation.MappedAudioBindingCount);
            report.mappedTextureBindings = Math.Max(report.mappedTextureBindings, presentation.MappedTextureBindingCount);
            foreach (string key in presentation.MissingAudioMappingKeys)
                if (!report.missingAudioMappingKeys.Contains(key)) report.missingAudioMappingKeys.Add(key);
            report.missingAudioMappings = report.missingAudioMappingKeys.Count;
            report.missingTextureMappings = Math.Max(report.missingTextureMappings, presentation.MissingTextureMappingCount);
            report.configuredAudioSources = Math.Max(report.configuredAudioSources, presentation.ConfiguredAudioSourceCount);
            report.audioSourcesConfigured |= presentation.AudioSourcesConfigured;
            report.propulsionClipsConfigured |= presentation.PropulsionAudioClipsConfigured;
            report.propulsionStartClipConfigured |= presentation.propulsionStartClip != null;
            report.propulsionLoopClipConfigured |= presentation.propulsionLoopClip != null;
            if (accumulate)
            {
                report.propulsionActivationCount += presentation.PropulsionActivationCount;
                report.suppressedSoundPlaybackCount += presentation.SuppressedSoundPlaybackCount;
                report.soundPlaybackCount += presentation.SoundPlaybackCount;
                report.soundEventCount += presentation.SoundEventCount;
                report.shotActionSoundEventCount += presentation.ShotActionSoundEventCount;
                report.voicePlaybackCount += presentation.VoicePlaybackCount;
            }
            if (presentation.SoundPlaybackCount > 0)
            {
                report.lastSoundPlaybackKey = presentation.LastSoundPlaybackKey;
                report.lastSoundPlaybackClip = presentation.LastSoundPlaybackClip;
            }
            report.originalEffectShaderSupported |= presentation.OriginalEffectShaderConfigured;
        }
    }

    static int Count<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    static int ActiveAudioCount() => UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(s => s.isPlaying);
    static bool Rejects(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
    static void Capture(Camera camera, string path)
    {
        var previous = camera.targetTexture; var active = RenderTexture.active;
        var target = new RenderTexture(960, 540, 24); var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previous; RenderTexture.active = active;
            target.Release(); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target);
        }
    }
}
