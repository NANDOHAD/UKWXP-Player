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
        public List<string> rounds = new List<string>();
    }

    public static async Task<Report> RunAsync(string path, string output, Camera camera = null,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(output);
        var report = new Report { unityVersion = Application.unityVersion, player = !Application.isEditor };
        Action<bool, string> check = (ok, message) => {
            if (!ok) throw new InvalidOperationException("[Lifecycle] " + message);
            report.assertions++;
        };
        int roots = Count<RoboStructure>(), controllers = Count<TestPlayController>(),
            projectiles = Count<TestPlayProjectile>(), sounds = Count<AudioSource>(), particles = Count<ParticleSystem>(), canvases = Count<Canvas>();
        var hostObject = new GameObject("LifecycleVerificationHost");
        var host = hostObject.AddComponent<TestPlaySessionBootstrap>();
        host.enabled = false; host.gameCamera = camera;
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
            while (!start.IsCompleted && Count<RoboStructure>() == roots) await Task.Yield();
            check(!start.IsCompleted && Count<RoboStructure>() > roots, "cancel occurs after assets exist");
            var stop = host.StopAsync();
            check(ReferenceEquals(stop, host.StopAsync()), "double stop shares completion");
            await stop;
            check(start.IsCanceled && host.Status == "Cancelled", "loading cancellation is distinct from failure"); clean();
            await host.StartAsync(path, path, token);
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
                    if (round == 2 && tick % 45 == 0) frame.pressed.melee = true;
                    if (round == 0 || round == 3)
                    {
                        frame.held.direction = tick < 50 ? 8 : 0;
                        frame.pressed.rise = tick == 60;
                        frame.held.rise = tick >= 100 && tick < 160;
                    }
                    host.AdvanceInput(frame, GameSession.TickSeconds);
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
                if (round == 1) check(hitCount == 1 && b.Controller.currentHP == b.Controller.maximumHP - 100, "real ANI shot hits once");
                if (round == 2) check(defeats == 1 && host.CombatEnded && b.Controller.currentHP == 0, "real ANI melee reaches combat end");
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
                File.WriteAllText(Path.Combine(output, "lifecycle-result.json"), JsonUtility.ToJson(report, true));
            }
        }
        return report;
    }

    static int Count<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
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
