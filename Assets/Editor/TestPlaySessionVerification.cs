using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class TestPlaySessionVerification
{
    [MenuItem("Tools/WindomXP/Verification/Verify Session and Regression")]
    public static void Run() => WindomVerificationRunner.StartVerification(
        WindomVerificationSelection.Session | WindomVerificationSelection.Standalone | WindomVerificationSelection.Regression);

    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static void SetPrivate(TestPlayController c, string name, object value) =>
        typeof(TestPlayController).GetField(name, PrivateInstance).SetValue(c, value);
    static object GetPrivate(TestPlayController c, string name) =>
        typeof(TestPlayController).GetField(name, PrivateInstance).GetValue(c);
    static void InvokeUpdate(UnityEngine.Object c) => c.GetType().GetMethod("Update", PrivateInstance).Invoke(c, null);
    static int CountSessionCombatEffectRoots() =>
        UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Count(t => t.name == "TestPlaySessionHitFallback" || t.name == "TestPlayEffect_CombatDefeat");
    static bool Rejects(Action action)
    {
        try { action(); return false; } catch (InvalidOperationException) { return true; }
    }

    static TestPlayMechSpawn Spawn(string path, ITestPlayInputProvider input, int index) =>
        new TestPlayMechSpawn { SourcePath = path, Input = input, Position = new Vector3(index * 8, 0, 12 * index),
            ShowHud = false, UseColliderGrounding = false };

    internal static async Task<int> RunForJobAsync(string outputFolder, CancellationToken token)
    {
        int assertions = 0;
        Action<bool, string> check = (condition, message) => {
            if (!condition) throw new InvalidOperationException("[Session] " + message);
            assertions++;
        };
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "Windom_Data", "Robo", "ガンダムTR-1ヘイズル改", "Script.ani");
        var recorded = new[] { new List<TestPlaySessionInput>(), new List<TestPlaySessionInput>() };
        var trace = new List<string>();
        var first = new TestPlayBufferedInputProvider(1);
        var second = new TestPlayBufferedInputProvider(2);
        GameSession session = null;
        MechHandle oldHandle = default;
        int rootsBefore = UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length;
        int combatEffectsBefore = CountSessionCombatEffectRoots();
        try
        {
            session = await GameSession.CreateAsync(new[] { Spawn(path, first, 0), Spawn(path, second, 1) },
                TestPlaySessionClock.Automatic, token);
            check(session.Status == "Ready" && session.Tick == 0 && session.Mechs.All(m => !m.Controller.playModeActive), "no ticking while ready");
            check(Rejects(() => session.Advance(1)), "ready rejects tick");
            session.Start();
            var a = session.Mechs[0]; var b = session.Mechs[1];
            int aBurnerStarts = 0, bBurnerStarts = 0;
            a.Controller.RuntimeEventRaised += e => { if (e.type == TestPlayRuntimeEventType.BurnerOutput && e.floatValue > 0) aBurnerStarts++; };
            b.Controller.RuntimeEventRaised += e => { if (e.type == TestPlayRuntimeEventType.BurnerOutput && e.floatValue > 0) bBurnerStarts++; };
            oldHandle = a.Handle;
            check(a.Handle.MechId == 1 && b.Handle.MechId == 2 && a.Handle.SessionId == b.Handle.SessionId, "stable spawn IDs");
            check(a.Controller.target == null && b.Controller.target == null, "no dummy state owner");
            check(!ReferenceEquals(a.Assets.Robo.ani, b.Assets.Robo.ani) && !ReferenceEquals(a.Assets.Spt, b.Assets.Spt) &&
                !ReferenceEquals(a.Controller.state, b.Controller.state) && !ReferenceEquals(GetPrivate(a.Controller, "vm"), GetPrivate(b.Controller, "vm")), "ANI/SPT/state/VM isolated");
            foreach (var pair in a.Assets.Spt.BurnerSets)
                if (pair.Value.BoneTr != null)
                    check(pair.Value.BoneTr != b.Assets.Spt.BurnerSets[pair.Key].BoneTr &&
                        (pair.Value.Scale <= 0 || (pair.Value.Ps != null && b.Assets.Spt.BurnerSets[pair.Key].Ps != null &&
                        pair.Value.Ps != b.Assets.Spt.BurnerSets[pair.Key].Ps)), "burner bindings isolated");
            check(Rejects(session.Start) && Rejects(session.Step), "double start and mixed clocks rejected");
            check(Rejects(() => a.Controller.SimulateDeterministicTraceTick(default)), "legacy manual tick rejected");
            InvokeUpdate(a.Controller); InvokeUpdate(b.Controller);
            check(session.Tick == 0 && a.Controller.tick == 0 && b.Controller.tick == 0, "controller Update cannot double tick");
            check(session.Query(a.Handle, 0).Single().Handle.MechId == 2 && session.Query(b.Handle, 0).Single().Handle.MechId == 1, "owner exclusion");
            session.TickCompleted += (tick, inputs) => {
                for (int i = 0; i < inputs.Length; i++) recorded[i].Add(inputs[i]);
                trace.Add(a.Controller.CapturePhase5TickTrace() + "|" + b.Controller.CapturePhase5TickTrace());
            };
            // 1 second in render half-ticks, including a key pressed and released before tick 1.
            first.Submit(new TestPlaySessionInput { pressed = new TestPlayGoldenInputFrame { rise = true } });
            check(session.Advance(1.0 / 240) == 0, "short press frame has no tick");
            first.Submit(default);
            check(session.Advance(1.0 / 240) == 0, "release before tick preserves latch");
            session.Advance(1.0 / 120);
            check(a.Controller.state.GetInt(199) == 1 && b.Controller.state.GetInt(199) == 0, "short Z press only reaches mech 1");
            for (int i = 0; i < 118; i++) session.Advance(1.0 / 120);
            check(session.Tick == 60 && a.Controller.tick == 60 && b.Controller.tick == 60, "one second equals 60 ticks for both mechs");
            check(a.Controller.currentEnergy != b.Controller.currentEnergy && a.Controller.currentAnimationIndex != b.Controller.currentAnimationIndex, "energy and animation isolated");
            var outputA = (Dictionary<int, float>)GetPrivate(a.Controller, "burnerRequestedOutputs");
            var outputB = (Dictionary<int, float>)GetPrivate(b.Controller, "burnerRequestedOutputs");
            check(!ReferenceEquals(outputA, outputB) && aBurnerStarts > 0 && bBurnerStarts == 0,
                "real ANI burner output isolated: " + aBurnerStarts + "/" + bBurnerStarts);
            // Catch-up consumes edge once, held/basis persist, excess time is retained.
            first.Submit(new TestPlaySessionInput { held = new TestPlayGoldenInputFrame { direction = 6 },
                pressed = new TestPlayGoldenInputFrame { lockTarget = true }, hasMovementBasis = true, movementForward = Vector3.right });
            second.Submit(new TestPlaySessionInput { held = new TestPlayGoldenInputFrame { direction = 4 } });
            check(session.Advance(1) == 8 && session.Tick == 68, "catch-up capped at eight");
            check(recorded[0][60].pressed.lockTarget && !recorded[0][61].pressed.lockTarget &&
                recorded[0][61].held.direction == 6 && recorded[0][61].movementForward == Vector3.right, "edge once, held and basis retained");
            while (session.Advance(0) > 0) { }
            check(session.Tick == 120 && a.Controller.tick == b.Controller.tick, "catch-up retains remainder");
            check(a.Controller.SessionLockHandle.MechId == 2 && !b.Controller.targetLockActive, "lock is per mech");
            File.WriteAllLines(Path.Combine(outputFolder, "session-v1-auto.txt"), trace);
            for (int i = 0; i < 2; i++) File.WriteAllLines(Path.Combine(outputFolder, "session-v1-input-" + (i + 1) + ".jsonl"), recorded[i].Select(f => JsonUtility.ToJson(f)));
            await session.StopAsync();
            check(!session.TryGet(oldHandle, out _) && session.Status == "Stopped", "stopped target handles invalid");
            await session.StopAsync();
            check(a.Assets.Robo == null && b.Assets.Robo == null, "both roots released");
        }
        finally { if (session != null) await session.StopAsync(); }

        // Replay twice in newly loaded sessions, using captured basis and separate input streams.
        for (int run = 0; run < 2; run++)
        {
            token.ThrowIfCancellationRequested();
            session = await GameSession.CreateAsync(new[] {
                Spawn(path, new TestPlayReplayInputProvider(1, recorded[0]), 0),
                Spawn(path, new TestPlayReplayInputProvider(2, recorded[1]), 1)
            }, TestPlaySessionClock.Manual, token);
            try
            {
                session.Start();
                check(session.SessionId != oldHandle.SessionId && !session.TryGet(oldHandle, out _), "old-session handle rejected");
                check(Rejects(() => session.Advance(0.1)), "manual mode rejects render clock");
                var replay = new List<string>();
                for (int i = 0; i < trace.Count; i++)
                {
                    session.Step();
                    string line = session.Mechs[0].Controller.CapturePhase5TickTrace() + "|" + session.Mechs[1].Controller.CapturePhase5TickTrace();
                    replay.Add(line);
                    check(line == trace[i], "replay " + run + " differs at tick " + (i + 1));
                }
                File.WriteAllLines(Path.Combine(outputFolder, "session-v1-replay-" + run + ".txt"), replay);
                check(Rejects(session.Step) && session.Status == "Faulted" && session.Tick == trace.Count, "missing replay fails before tick and stops runtime");
            }
            finally { await session.StopAsync(); }
        }

        // Timer/snapshot/projectile boundaries use the same real controllers with idle input.
        session = await GameSession.CreateAsync(new[] { Spawn(path, new TestPlayIdleInputProvider(), 0),
            Spawn(path, new TestPlayIdleInputProvider(), 1) }, TestPlaySessionClock.Manual, token);
        try
        {
            session.Start();
            var a = session.Mechs[0]; var b = session.Mechs[1];
            a.Controller.currentHP -= 17;
            a.Controller.state.SetInt(157, 5); b.Controller.state.SetInt(157, 9);
            a.Controller.state.SetInt(158, 7); b.Controller.state.SetInt(158, 11);
            SetPrivate(a.Controller, "hitStopTicks", 1); SetPrivate(b.Controller, "hitStopTicks", 3);
            int aActionTick = (int)GetPrivate(a.Controller, "actionTick");
            int bActionTick = (int)GetPrivate(b.Controller, "actionTick");
            session.Step();
            check(a.Controller.state.GetInt(157) == 4 && b.Controller.state.GetInt(157) == 8 &&
                a.Controller.state.GetInt(158) == 6 && b.Controller.state.GetInt(158) == 10, "defense timers once per mech");
            check((int)GetPrivate(a.Controller, "hitStopTicks") == 0 && (int)GetPrivate(b.Controller, "hitStopTicks") == 2 &&
                (int)GetPrivate(a.Controller, "actionTick") == aActionTick && (int)GetPrivate(b.Controller, "actionTick") == bActionTick, "hit-stop uses pre-decrement state");
            check(a.Controller.currentHP == a.Controller.maximumHP - 17 && b.Controller.currentHP == b.Controller.maximumHP, "HP isolated");
            var snapshotBefore = session.Query(a.Handle, 0).Single();
            b.Controller.robo.root.transform.position += Vector3.right;
            check(session.Query(a.Handle, 0).Single().Position == snapshotBefore.Position, "geometry is frozen within tick");
            session.Step();
            check(session.Query(a.Handle, 0).Single().Position != snapshotBefore.Position, "geometry refreshes on next tick");
            // Spawn through the real controller ANI command path during a tick.
            TestPlayProjectile spawned = null;
            Vector3 spawnPosition = default;
            a.Controller.RuntimeEventRaised += e => {
                if (e.type == TestPlayRuntimeEventType.WeaponSpawned)
                {
                    spawned = UnityEngine.Object.FindObjectsByType<TestPlayProjectile>(FindObjectsSortMode.None)
                        .FirstOrDefault(p => p.owner == a.Controller);
                    if (spawned != null) spawnPosition = spawned.transform.position;
                }
            };
            a.Controller.ChangeAnimation(a.Controller.shotAction);
            for (int i = 0; i < 100 && spawned == null; i++) session.Step();
            check(spawned != null && spawned.AttackId > 0 && spawned.OwnerHandle.MechId == 1, "projectile enrolled with stable ID");
            check(spawned.transform.position == spawnPosition, "new projectile holds pose on spawn tick");
            InvokeUpdate(spawned);
            check(spawned.transform.position == spawnPosition && Rejects(() => spawned.SimulateOriginalTick(1f / 60)), "projectile auto/manual exclusion");
            session.Step();
            check(spawned != null && spawned.transform.position != spawnPosition, "projectile moves on following tick");
            b.Controller.currentHP = 0;
            check(!session.TryGet(b.Handle, out _) && session.Query(a.Handle, 0).Count == 0, "dead target invalid immediately");
        }
        finally { await session.StopAsync(); }
        check(UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length == rootsBefore,
            "session verification leaves no mech roots");
        check(UnityEngine.Object.FindObjectsByType<TestPlayProjectile>(FindObjectsSortMode.None).All(p => p.AttackId == 0), "session projectiles released");
        assertions += await TestPlaySessionCombatVerification.RunForJobAsync(outputFolder, token);
        assertions += await TestPlaySessionCombatVerification.VerifyMeleeMotionAsync(path, outputFolder, token);
        assertions += await TestPlaySessionCombatVerification.VerifyHitFeedbackAsync(path, outputFolder, token);
        assertions += await TestPlaySessionCombatVerification.VerifyShotImpactAsync(path, outputFolder, token);
        assertions += await TestPlaySessionCombatVerification.VerifyShotColliderAsync(path, outputFolder, token);
        assertions += await TestPlaySessionCombatVerification.VerifyLockedShotAsync(path, outputFolder, token);
        check(CountSessionCombatEffectRoots() == combatEffectsBefore,
            "session verification leaves no combat effect roots");
        return assertions;
    }
}

/// <summary>Real loaded controllers; deterministic collision fixtures plus real ANI Play checks.</summary>
public static class TestPlaySessionCombatVerification
{
    public static async Task<int> VerifyLockedShotAsync(string path, string folder, CancellationToken token)
    {
        int count = 0;
        Action<bool, string> check = (ok, message) => {
            if (!ok) throw new InvalidOperationException("[LockedShot] " + message);
            count++;
        };
        int rootsBefore = UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length;
        int projectilesBefore = UnityEngine.Object.FindObjectsByType<TestPlayProjectile>(FindObjectsSortMode.None).Length;
        var trace = new List<string> { "case,tick,action,muzzleAngle,launchError,muzzleBulletError,targetMovement" };
        for (int index = 0; index < 6; index++)
        {
            token.ThrowIfCancellationRequested();
            var input = new TestPlayBufferedInputProvider(1);
            float yaw = new[] { 0f, 90f, -90f, 180f, 90f, -90f }[index];
            Vector3 destination = new Vector3(0, index == 4 ? 20 : index == 5 ? -20 : 0, 30);
            var session = await GameSession.CreateAsync(new[] {
                new TestPlayMechSpawn { SourcePath = path, Input = input, Rotation = Quaternion.Euler(0, yaw, 0), UseColliderGrounding = false },
                new TestPlayMechSpawn { SourcePath = path, Input = new TestPlayIdleInputProvider(), Position = destination, UseColliderGrounding = false }
            }, TestPlaySessionClock.Manual, token);
            try
            {
                session.Start();
                var c = session.Mechs[0].Controller;
                var opponent = session.Mechs[1].Controller;
                input.Submit(new TestPlaySessionInput { pressed = new TestPlayGoldenInputFrame { lockTarget = true } });
                session.Step();
                check(c.targetLockActive, "S acquires opponent for case " + index);
                Vector3 lockPosition = c.GetLockedTargetTransform().position;
                TestPlayProjectile shot = null;
                Vector3 launchPosition = default, launchForward = default;
                c.RuntimeEventRaised += e => {
                    if (e.type != TestPlayRuntimeEventType.WeaponSpawned || e.command != "RunProc2:1") return;
                    shot = UnityEngine.Object.FindObjectsByType<TestPlayProjectile>(FindObjectsSortMode.None)
                        .First(p => p.owner == c);
                    launchPosition = shot.transform.position;
                    launchForward = shot.transform.forward;
                    Vector3 targetPosition = c.GetLockedTargetTransform().position;
                    Vector3 desired = targetPosition - launchPosition;
                    var muzzle = c.RuntimeSptData.WeaponPoints[0];
                    float cone = Vector3.Angle(muzzle.WorldForward, desired);
                    float error = Vector3.Angle(launchForward, desired);
                    float muzzleError = Vector3.Angle(launchForward, muzzle.WorldForward);
                    // Original joint aiming is constrained and is not muzzle IK.
                    // Its type1 20-degree cone is a coarse facing acceptance bound;
                    // do not redirect the bullet to erase the remaining ANI offset.
                    check(error < TestPlayCombatCore.OriginalType1InitialAimConeDegrees,
                        "constrained ANI pose faces within original type1 cone, case=" + index + " error=" + error);
                    if (c.currentAnimationIndex != 103)
                    {
                        Transform arm = c.RuntimeSptData.AttackArms[0].BoneTr;
                        Vector3 local = arm.InverseTransformVector(targetPosition - arm.position);
                        local.z = 0f;
                        check(Vector3.Angle(local, Vector3.down) < 0.05f,
                            "real SPT arm aims its local -Y at projected target without off-axis twist");
                    }
                    check(muzzleError < 0.05f, "bullet leaves straight along the visible muzzle axis");
                    check(Vector3.Distance(launchPosition, muzzle.BoneTr.position) < 0.0001f, "real WEAPONPOINT origin retained");
                    check(Vector3.Distance(lockPosition, targetPosition) > 1f, "target is sampled at launch, not lock acquisition");
                    trace.Add(FormattableString.Invariant($"{index},{session.Tick},{c.currentAnimationIndex},{cone},{error},{muzzleError},{Vector3.Distance(lockPosition, targetPosition)}"));
                };
                for (int tick = 0; tick < 100 && shot == null; tick++)
                {
                    // Move the real target before each tick snapshot, including after X press.
                    opponent.robo.root.transform.position = destination + Vector3.right * (2f + tick * 0.1f);
                    input.Submit(tick == 0 ? new TestPlaySessionInput {
                        pressed = new TestPlayGoldenInputFrame { shot = true }
                    } : default);
                    session.Step();
                    if (Application.isPlaying) await Task.Yield();
                }
                check(shot != null, "real AN2 X action emits type1 for case " + index);
                check(Vector3.Distance(shot.transform.position, launchPosition) < 0.0001f,
                    "new shot does not move until next session tick");
                var visibleMuzzle = c.RuntimeSptData.WeaponPoints[shot.weaponPointId];
                check(Vector3.Angle(launchForward, visibleMuzzle.WorldForward) < 0.05f &&
                      Vector3.Distance(launchPosition, visibleMuzzle.BoneTr.position) < 0.0001f,
                    "end-of-tick rendered muzzle still matches emission after pose and root motion");
                if (Application.isPlaying)
                    CaptureLockedShotPose(c, folder, index);
                // Move target after launch; movement still uses the launch direction first.
                opponent.robo.root.transform.position += Vector3.left * 10f;
                session.Step();
                check(Vector3.Angle(shot.transform.position - launchPosition, launchForward) < 0.05f,
                    "first projectile step uses snapshotted launch direction before existing homing");
            }
            finally { await session.StopAsync(); }
        }
        check(UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length == rootsBefore &&
              UnityEngine.Object.FindObjectsByType<TestPlayProjectile>(FindObjectsSortMode.None).Length == projectilesBefore,
            "locked shot fixtures release all mechs and projectiles");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "locked-shot.csv"), trace);
        File.WriteAllText(Path.Combine(folder, "locked-shot-result.txt"), "Succeeded assertions=" + count + " play=" + Application.isPlaying);
        return count;
    }

    static void CaptureLockedShotPose(TestPlayController controller, string folder, int index)
    {
        var go = new GameObject("LockedShotCapture");
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        var rt = new RenderTexture(1280, 960, 24);
        var texture = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            Vector3 origin = controller.robo.root.transform.position;
            Vector3 aim = controller.RuntimeSptData.WeaponPoints[0].WorldForward;
            go.transform.position = origin - aim * 5f + Vector3.Cross(Vector3.up, aim).normalized * 8f + Vector3.up * 4f;
            go.transform.LookAt(origin + aim * 2f + Vector3.up);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
            camera.fieldOfView = 45f;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
            texture.Apply();
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "muzzle-pose-" + index + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    public static async Task<int> VerifyShotColliderAsync(string path, string folder, CancellationToken token)
    {
        int count = 0;
        var log = new List<string>();
        Action<bool,string> check = (ok,message) => {
            log.Add((ok ? "PASS " : "FAIL ") + message);
            if (!ok) throw new InvalidOperationException("[ShotCollider] " + message);
            count++;
        };
        var floor = new GameObject("ShotColliderFloor");
        floor.transform.position = new Vector3(0,-0.5f,0);
        floor.AddComponent<BoxCollider>().size = new Vector3(100,1,100);
        GameSession session = null;
        try
        {
            var input = new TestPlayBufferedInputProvider(1);
            session = await GameSession.CreateAsync(new[] {
                new TestPlayMechSpawn { SourcePath=path, Input=input, UseColliderGrounding=true },
                new TestPlayMechSpawn { SourcePath=path, Input=new TestPlayIdleInputProvider(),
                    Position=Vector3.forward*4, Rotation=Quaternion.Euler(0,180,0), UseColliderGrounding=true }
            },TestPlaySessionClock.Manual,token);
            session.Start();
            var a=session.Mechs[0]; var b=session.Mechs[1];
            var cc=b.Controller.robo.root.GetComponent<CharacterController>();
            Vector3 expectedCenter=cc.transform.TransformPoint(cc.center);
            session.Step();
            session.TryGet(b.Handle,out var snapshot);
            check(cc!=null && cc.enabled,"real character capsule exists");
            check(Vector3.Distance((snapshot.ShotCapsuleStart+snapshot.ShotCapsuleEnd)*0.5f,
                expectedCenter)<0.001f && Mathf.Abs(snapshot.ShotCapsuleRadius-cc.radius)<0.001f,
                "tick-start shot snapshot matches the collider center and radius");
            Vector3 from=new Vector3(0,1.6f,2), to=new Vector3(0,1.6f,6);
            check(!TestPlayCombatCore.IntersectsType1TrailSegmentSphere(from,to,snapshot.Position,snapshot.Radius),
                "reproduces old root sphere missing a torso-height crossing");
            check(TestPlayCombatCore.IntersectsTrailSegmentCapsule(from,to,snapshot.ShotCapsuleStart,snapshot.ShotCapsuleEnd,snapshot.ShotCapsuleRadius),
                "capsule accepts the torso-height crossing");
            check(!TestPlayCombatCore.IntersectsTrailSegmentCapsule(from+Vector3.right,to+Vector3.right,
                snapshot.ShotCapsuleStart,snapshot.ShotCapsuleEnd,snapshot.ShotCapsuleRadius),"outside side edge misses");
            check(!TestPlayCombatCore.IntersectsTrailSegmentCapsule(from+Vector3.up,to+Vector3.up,
                snapshot.ShotCapsuleStart,snapshot.ShotCapsuleEnd,snapshot.ShotCapsuleRadius),"above capsule cap misses");
            check(TestPlayCombatCore.IntersectsTrailSegmentCapsule(snapshot.ShotCapsuleStart,snapshot.ShotCapsuleStart,
                snapshot.ShotCapsuleStart,snapshot.ShotCapsuleEnd,snapshot.ShotCapsuleRadius),"zero length segment inside hits");
            check(TestPlayCombatCore.IntersectsTrailSegmentCapsule(new Vector3(0,5,4),new Vector3(0,-1,4),
                snapshot.ShotCapsuleStart,snapshot.ShotCapsuleEnd,snapshot.ShotCapsuleRadius),"parallel high-speed segment intersects capsule caps");
            float hp=b.Controller.currentHP;
            var projectile=Projectile(session,a,from);
            projectile.ConfigureOriginalType1(new TestPlayType1ProjectileParameters { activeTicks=12,trailPointCount=10,distancePerTick=10 },false,0);
            session.Step();
            check(b.Controller.currentHP==hp-25,"real high-speed type1 crossing applies damage at torso height");
            session.Step(); check(b.Controller.currentHP==hp-25,"trail overlap does not repeat damage");
            for(int i=0;i<14;i++)session.Step();
            hp=b.Controller.currentHP;
            input.Submit(new TestPlaySessionInput { pressed=new TestPlayGoldenInputFrame { lockTarget=true } });session.Step();
            check(a.Controller.targetLockActive,"S locks real opponent");
            input.Submit(new TestPlaySessionInput { pressed=new TestPlayGoldenInputFrame { shot=true } });
            int shots=0;
            a.Controller.RuntimeEventRaised += e=> { if(e.type==TestPlayRuntimeEventType.WeaponSpawned && e.command=="RunProc2:1")shots++; };
            for(int i=0;i<70 && b.Controller.currentHP==hp;i++)
            {
                token.ThrowIfCancellationRequested();session.Step(); input.Submit(default);
                if(Application.isPlaying)await Task.Yield();
            }
            check(shots==1,"actual X animation emits one shot");
            check(b.Controller.currentHP<hp,"actual S-X muzzle shot hits real opponent collider");
        }
        finally
        {
            if(session!=null)await session.StopAsync();
            UnityEngine.Object.DestroyImmediate(floor);
            Directory.CreateDirectory(folder);File.WriteAllLines(Path.Combine(folder,"shot-collider.txt"),log);
        }
        File.AppendAllText(Path.Combine(folder,"shot-collider.txt"),"\nSucceeded assertions="+count+" play="+Application.isPlaying);
        return count;
    }

    public static async Task<int> VerifyShotImpactAsync(string path, string folder, CancellationToken token)
    {
        int count = 0;
        var log = new List<string>();
        Action<bool, string> check = (ok, message) => {
            log.Add((ok ? "PASS " : "FAIL ") + message);
            if (!ok) throw new InvalidOperationException("[ShotImpact] " + message);
            count++;
        };
        int rootsBefore = UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length;
        var go = new GameObject("ShotImpactVerification");
        go.transform.position = Vector3.down * 0.5f;
        go.AddComponent<BoxCollider>().size = new Vector3(100,1,100);
        var template = go.AddComponent<TestPlayPresentationRuntime>();
        TestPlayOriginalSoundSetup.ApplyMappings(template);
        TestPlayOriginalTextureSetup.ApplyMappings(template, out var missing);
        GameSession session = null;
        try
        {
            check(missing.Count == 0, "original texture mappings available");
            session = await GameSession.CreateAsync(new[] {
                new TestPlayMechSpawn { SourcePath = path, Position = Vector3.zero, Input = new TestPlayIdleInputProvider(),
                    UseColliderGrounding = true, PresentationTemplate = template },
                new TestPlayMechSpawn { SourcePath = path, Position = Vector3.forward * 4, Rotation = Quaternion.Euler(0,180,0),
                    Input = new TestPlayIdleInputProvider(), UseColliderGrounding = true, PresentationTemplate = template }
            }, TestPlaySessionClock.Manual, token);
            session.Start(); session.Step();
            var a = session.Mechs[0]; var b = session.Mechs[1];
            var audio = b.Controller.presentationRuntime;
            check(TestPlayPresentationRuntime.FindBinding(audio.sounds, "7")?.clip?.name == "BeamHit",
                "defender has original BeamHit sound mapping");
            int sounds = audio.SoundPlaybackCount, effects = b.Controller.SessionHitEffectSpawnCount;
            int id = AttackId(session, a);
            Set(b.Controller, "shieldGuard", 1);
            Impact(session, id, a, b, 0, 1, 0, 0, 0, false, 0, out var guard);
            check(guard.decision == TestPlayCombatHitDecision.Guarded, "shot fixture actually guards");
            b.Controller.state.SetInt(158, 2);
            Impact(session, id, a, b, 0x10, 1, 0, 0, 0, false, 0, out var invulnerable);
            check(invulnerable.decision == TestPlayCombatHitDecision.Invulnerable, "shot fixture actually blocks damage");
            check(audio.SoundPlaybackCount == sounds && b.Controller.SessionHitEffectSpawnCount == effects,
                "guard and invulnerability emit neither damage sound nor flash");
            Set(b.Controller, "shieldGuard", 0); b.Controller.state.SetInt(158, 0);
            float hp = b.Controller.currentHP;
            var projectile = Projectile(session, a, Vector3.forward * 2 + Vector3.up * 1.6f);
            for (int i = 0; i < 3 && b.Controller.currentHP == hp; i++) session.Step();
            check(b.Controller.currentHP == hp - 25, "moving type1 projectile causes real collision damage");
            check(audio.SoundPlaybackCount == sounds + 1 && audio.LastSoundPlaybackKey == "7",
                "one projectile hit requests BeamHit exactly once on the defender");
            check(b.Controller.SessionHitEffectSpawnCount == effects + 1, "one projectile hit creates one composite impact");
            var layers = b.Controller.GetComponentsInChildren<TestPlayOriginalEffect>();
            check(layers.Length == 18 && layers.Any(l => l.sourceTexture.name.EndsWith("beamHit2")) &&
                layers.Any(l => l.sourceTexture.name.EndsWith("beamHit3")), "impact contains original flash, ring and 16 spark streaks");
            check(layers.All(l => l.GetComponent<Renderer>().enabled && l.CurrentTint.a > 0.9f), "all impact layers start visible");
            if (Application.isPlaying)
            {
                await Task.Yield();
                check(audio.soundSource.isPlaying, "AudioSource starts the real impact clip in Play");
                CaptureShotImpact(b.Controller, layers, folder);
            }
            var initialPositions = layers.Select(l => l.transform.position).ToArray();
            for (int i = 0; i < 12; i++) session.Step();
            check(audio.SoundPlaybackCount == sounds + 1 && b.Controller.currentHP == hp - 25,
                "same projectile trail cannot repeat the damage or sound");
            check(layers.All(l => l != null && l.CurrentTint.a < 1f && l.CurrentTint.a > 0f), "flash and sparks fade on the session clock");
            check(layers.Where(l => l.billboardAxis.sqrMagnitude > 0f).All(l =>
                Vector3.Distance(l.transform.position, initialPositions[Array.IndexOf(layers,l)]) > 0.6f), "sparks travel visibly away from the impact");
            if (Application.isPlaying) CaptureShotImpact(b.Controller, layers, folder, "shot-impact-spread.png");
            for (int i = 0; i < 18; i++) session.Step();
            if (Application.isPlaying) await Task.Yield();
            check(layers.All(l => l == null), "impact layers are removed after 30 ticks");
            Impact(session, AttackId(session,a), a,b,0x40,1,0,0,0,false,0,out _);
            check(audio.SoundPlaybackCount == sounds + 2 && b.Controller.SessionHitEffectSpawnCount == effects + 2,
                "a separate accepted shot receives its own sound and effect");
            var reverseAudio = a.Controller.presentationRuntime;
            int reverseSounds = reverseAudio.SoundPlaybackCount;
            Impact(session, AttackId(session,b), b,a,0x40,1,0,0,0,false,0,out _);
            check(reverseAudio.SoundPlaybackCount == reverseSounds + 1 && reverseAudio.LastSoundPlaybackKey == "7",
                "player and opponent share the same shot impact presentation");
        }
        finally
        {
            if (session != null) await session.StopAsync();
            UnityEngine.Object.DestroyImmediate(go);
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "shot-impact.txt"), log);
        }
        check(UnityEngine.Object.FindObjectsByType<RoboStructure>(FindObjectsSortMode.None).Length == rootsBefore,
            "shot impact fixture releases its mechs and child effects");
        File.AppendAllText(Path.Combine(folder, "shot-impact.txt"), "\nSucceeded assertions=" + count + " play=" + Application.isPlaying);
        return count;
    }

    static void CaptureShotImpact(TestPlayController controller, TestPlayOriginalEffect[] layers, string folder, string fileName = "shot-impact.png")
    {
        var go = new GameObject("ShotImpactCapture");
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        var rt = new RenderTexture(1280, 960, 24);
        var texture = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        var rotations = layers.Select(l => l.transform.rotation).ToArray();
        try
        {
            Vector3 origin = controller.robo.root.transform.position;
            go.transform.position = origin + new Vector3(3, 2.3f, -5);
            go.transform.LookAt(origin + Vector3.up * 1.1f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
            camera.fieldOfView = 35; camera.targetTexture = rt;
            foreach (var layer in layers) layer.FaceCamera(camera);
            camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0,0,1280,960),0,0); texture.Apply();
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,fileName), texture.EncodeToPNG());
        }
        finally
        {
            for (int i=0;i<layers.Length;i++) if(layers[i]!=null) layers[i].transform.rotation=rotations[i];
            RenderTexture.active=previous; camera.targetTexture=null; rt.Release();
            UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(go);
        }
    }

    public static async Task<int> VerifyHitFeedbackAsync(string path, string folder, CancellationToken token)
    {
        int count = 0;
        var log = new List<string>();
        Action<bool, string> check = (ok, message) => {
            log.Add((ok ? "PASS " : "FAIL ") + message);
            if (!ok) throw new InvalidOperationException("[HitFeedback] " + message);
            count++;
        };
        var go = new GameObject("HitFeedbackVerification");
        var template = go.AddComponent<TestPlayPresentationRuntime>();
        TestPlayOriginalSoundSetup.ApplyMappings(template);
        var host = go.AddComponent<TestPlaySessionBootstrap>();
        host.enabled = false;
        host.presentationTemplate = template;
        try
        {
            await host.StartAsync(path, path, token);
            var s = host.Session;
            var a = s.Mechs[0]; var b = s.Mechs[1];
            foreach (var mech in s.Mechs) mech.Controller.useColliderGrounding = false;
            a.Assets.Robo.root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            b.Assets.Robo.root.transform.SetPositionAndRotation(Vector3.forward * 2, Quaternion.Euler(0, 180, 0));
            host.AdvanceInput(default, GameSession.TickSeconds);
            var audio = b.Controller.presentationRuntime;
            var clip = TestPlayPresentationRuntime.FindBinding(audio.sounds, "11")?.clip;
            check(clip != null && clip.name == "BeamHit2", "opponent inherits the real hit clip mapping");
            check(audio.soundSource != a.Controller.presentationRuntime.soundSource,
                "both mechs own separate audio sources");
            int sounds = audio.SoundPlaybackCount;
            int id = AttackId(s, a);
            Set(b.Controller, "shieldGuard", 1);
            Impact(s, id, a, b, 0, 1, 0, 0, 0, true, 0, out _);
            b.Controller.state.SetInt(158, 2);
            Impact(s, id, a, b, 0x10, 1, 0, 0, 0, true, 0, out _);
            check(audio.SoundPlaybackCount == sounds, "guard and invulnerability do not play damage SE");
            b.Controller.state.SetInt(158, 0);
            Set(b.Controller, "shieldGuard", 0);
            Impact(s, id, a, b, 0, 1, 250, 0, 0, true, 4, out _);
            check(audio.SoundPlaybackCount == sounds + 1 && audio.LastSoundPlaybackKey == "11",
                "one accepted melee hit produces one playback request on defender");
            int poseId = b.Controller.CurrentActionSelection.poseActionId;
            var first = b.Assets.Robo.ani.animations[poseId].frames[0];
            Action assertFirstPose = () => {
                float error = 0;
                for (int i = 1; i < b.Assets.Robo.parts.Count; i++)
                {
                    var part = b.Assets.Robo.parts[i].transform;
                    error += Vector3.Distance(part.localPosition, first.parts[i].position);
                    error += Quaternion.Angle(part.localRotation, first.parts[i].rotation);
                }
                check(error < 0.1f, "rendered reaction pose is held, error=" + error);
            };
            assertFirstPose(); // Catches old idle pose even when action/frame diagnostics are correct.
            if (Application.isPlaying)
            {
                await Task.Yield();
                check(audio.soundSource.isPlaying, "real AudioSource starts the hit clip in Play");
            }
            for (int i = 0; i < 4; i++) { host.AdvanceInput(default, GameSession.TickSeconds); assertFirstPose(); }
            for (int i = 0; i < 39; i++)
            {
                token.ThrowIfCancellationRequested();
                host.AdvanceInput(default, GameSession.TickSeconds);
                check(b.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.Hit &&
                    b.Controller.CurrentActionSelection.logicalActionId == 13, "reaction remains active tick=" + (i + 1));
                if (Application.isPlaying) await Task.Yield();
            }
            host.AdvanceInput(default, GameSession.TickSeconds);
            check(b.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.None,
                "reaction releases only after 40 unfrozen ticks");
            // Repeated hits refresh the pose immediately, rather than restarting an idle blend.
            Impact(s, id, a, b, 1, 1, 0, 0, 0, true, 4, out _);
            assertFirstPose();
            Impact(s, id, a, b, 1, 1, 0, 0, 0, true, 4, out _);
            assertFirstPose();
            check(audio.SoundPlaybackCount == sounds + 3, "repeated accepted impacts each play one hit SE");
            log.Add("Succeeded assertions=" + count + " play=" + Application.isPlaying);
        }
        finally
        {
            await host.StopAsync();
            if (Application.isPlaying) { UnityEngine.Object.Destroy(go); await Task.Yield(); }
            else UnityEngine.Object.DestroyImmediate(go);
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "hit-feedback.txt"), log);
        }
        return count;
    }

    internal static async Task<int> VerifyMeleeMotionAsync(string path, string folder, CancellationToken token)
    {
        int count = 0;
        Action<bool, string> check = (ok, message) => {
            if (!ok) throw new InvalidOperationException("[MeleeMotion] " + message);
            count++;
        };
        var input = new TestPlayBufferedInputProvider(1);
        var s = await GameSession.CreateAsync(new[] {
            new TestPlayMechSpawn { SourcePath = path, Input = input, UseColliderGrounding = false },
            new TestPlayMechSpawn { SourcePath = path, Input = new TestPlayIdleInputProvider(),
                Position = new Vector3(10, 0, 10), UseColliderGrounding = false }
        }, TestPlaySessionClock.Manual, token);
        try
        {
            s.Start();
            var c = s.Mechs[0].Controller;
            Transform root = c.robo.root.transform;
            Call(c, "StartNormalAttackAction", 131);
            Set(c, "moveCommand", Vector3.forward * 0.5f);
            Set(c, "forceCommand", Vector3.zero);
            Set(c, "gvEnable", false);
            for (int i = 0; i < 30; i++) Call(c, "ApplyRootMotion");
            float expectedDistance = 0.5f * c.aniUnitsToUnityScale * 9f * (1f - Mathf.Pow(0.9f, 30));
            check(Mathf.Abs(root.position.z - expectedDistance) < 0.0001f,
                "unlocked missed melee decays geometrically instead of travelling 10.5 units");
            check(root.rotation == Quaternion.identity, "unlocked melee does not home on nearest opponent");
            c.TryAcquireTargetLock();
            Set(c, "meleeTargetTurnDegrees", 10f);
            Quaternion before = root.rotation;
            Call(c, "ApplyMeleeTargetSteering", root);
            check(Mathf.Abs(Quaternion.Angle(before, root.rotation) - 10f) < 0.001f,
                "locked melee respects the ANI angular budget");
            for (int i = 0; i < 20; i++) Call(c, "ApplyMeleeTargetSteering", root);
            Vector3 desired = s.Mechs[1].Controller.robo.root.transform.position - root.position;
            desired.y = 0;
            check(Vector3.Angle(root.forward, desired) < 0.01f, "locked root faces target without overshoot");

            Set(c, "swordCancelAction", 132);
            Set(c, "hitStopTicks", 3);
            Vector3 stopped = root.position;
            int actionTick = (int)Field(c, "actionTick");
            input.Submit(new TestPlaySessionInput { pressed = new TestPlayGoldenInputFrame { melee = true } });
            s.Step(); input.Submit(default); s.Step(); s.Step();
            check(root.position == stopped && (int)Field(c, "actionTick") == actionTick &&
                c.currentAnimationIndex == 131, "hit-stop freezes root and action on all three ticks");
            check((bool)Field(c, "meleeComboInputPending"), "C edge survives release during hit-stop");
            s.Step();
            check(c.currentAnimationIndex == 132, "buffered C enters SwordCancel after hit-stop");
            check(Mathf.Approximately((float)Field(c, "scriptedMoveRetention"), 0.5f),
                "missed combo uses original 0.5 retention");
            Set(c, "meleeSequenceHit", true);
            Set(c, "swordCancelAction", 133);
            Set(c, "meleeComboInputPending", true);
            Call(c, "TryUpdateNormalAttackSequence");
            check(c.currentAnimationIndex == 133 && Mathf.Approximately((float)Field(c, "scriptedMoveRetention"), 0.9f),
                "connected combo retains original 0.9 movement");
            Set(c, "swordCancelAction", 134);
            Call(c, "ExecuteScript", "ATTACK(20,50,0.5,0);");
            Call(c, "ResetOriginalBlockState");
            check((int)Field(c, "swordCancelAction") == -1, "cancel window cannot leak into next ANI block");
            var profile = (TestPlayAttackProfile)Field(c, "attackProfile");
            check(profile.power == 20 && profile.down == 50 && Mathf.Approximately(profile.force, 0.5f),
                "later melee blocks preserve ATTACK instead of falling back to damage 80 and down 0");
            c.ChangeAnimation(130);
            check(Mathf.Approximately((float)Field(c, "scriptedMoveRetention"), 1f),
                "approach retains original 1.0 movement");
            var defender = s.Mechs[1];
            Impact(s, AttackId(s, s.Mechs[0]), s.Mechs[0], defender, 0, 20, 50, 0.5f, 0, true, 5, out _);
            Impact(s, AttackId(s, s.Mechs[0]), s.Mechs[0], defender, 0, 20, 50, 0.5f, 0, true, 5, out _);
            check(Mathf.Abs(((Vector3)Field(defender.Controller, "velocity")).magnitude - 0.5f) < 0.0001f,
                "overlapping melee impacts replace knockback instead of accumulating during hit-stop");
            File.WriteAllText(Path.Combine(folder, "melee-motion.txt"),
                "assertions=" + count + " missedMelee30TickDistance=" + expectedDistance);
        }
        finally { await s.StopAsync(); }
        return count;
    }

    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    static object Field(object obj, string name) => obj.GetType().GetField(name, Private).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    static void SetProperty(object obj, string name, object value) => obj.GetType().GetProperty(name, Private).SetValue(obj, value);
    static int AttackId(GameSession s, MechRuntime owner) => (int)Call(s, "AllocateAttackId", owner.Handle);
    static bool Hit(GameSession s, int id, MechRuntime owner, MechRuntime defender, int flag, float damage,
        bool melee, int stop, out TestPlayCombatHitResult resolved)
    {
        return Impact(s, id, owner, defender, flag, damage, 250, 0.2f, 0f, melee, stop, out resolved);
    }
    static bool Impact(GameSession s, int id, MechRuntime owner, MechRuntime defender, int flag, float damage,
        int down, float force, float forceY, bool melee, int stop, out TestPlayCombatHitResult resolved)
    {
        var hit = TestPlayCombatCore.CreateHitResult(new TestPlayProjectilePayload { damage = damage, attackFlag = flag,
            down = down, collisionKind = melee ? TestPlayAttackCollisionKind.OriginalType57 : TestPlayAttackCollisionKind.OriginalType1,
            horizontalImpactForce = force, verticalImpactForce = forceY, source = "combat-fixture" }, Vector3.forward);
        object[] args = { id, owner.Handle, defender.Handle, hit, owner.Controller.robo.root.transform.position, melee, stop, null };
        bool accepted = (bool)Call(s, "ResolveHit", args);
        resolved = (TestPlayCombatHitResult)args[7];
        return accepted;
    }
    static TestPlayProjectile Projectile(GameSession s, MechRuntime owner, Vector3 position, float damage = 25)
    {
        var go = new GameObject("SessionCombatFixtureProjectile");
        go.transform.position = position;
        var p = go.AddComponent<TestPlayProjectile>();
        p.owner = owner.Controller;
        p.damage = damage;
        p.attackFlag = 0x40; // test geometry independently from reaction movement
        p.collisionKind = TestPlayAttackCollisionKind.OriginalType1;
        p.ConfigureOriginalType1(new TestPlayType1ProjectileParameters { activeTicks = 12, trailPointCount = 10,
            distancePerTick = 1 }, false, 0);
        Call(s, "RegisterProjectile", p, owner.Handle, default(MechHandle));
        return p;
    }

    internal static async Task<int> RunForJobAsync(string folder, CancellationToken token)
    {
        int assertions = 0;
        Action<bool, string> check = (ok, message) => {
            if (!ok) throw new InvalidOperationException("[SessionCombat] " + message);
            assertions++;
        };
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "Windom_Data", "Robo", "ガンダムTR-1ヘイズル改", "Script.ani");
        List<string> expected = null;
        for (int run = 0; run < 2; run++)
        {
            token.ThrowIfCancellationRequested();
            var s = await GameSession.CreateAsync(Enumerable.Range(0, 3).Select(i => new TestPlayMechSpawn {
                SourcePath = path, Position = Vector3.forward * i * 2, Input = new TestPlayIdleInputProvider(),
                UseColliderGrounding = false }).ToArray(), TestPlaySessionClock.Manual, token);
            var trace = new List<string>();
            var hits = new List<TestPlaySessionHit>();
            int defeated = 0, ended = 0;
            try
            {
                s.Start();
                s.HitResolved += h => { hits.Add(h); h.sessionId = ""; trace.Add(JsonUtility.ToJson(h)); };
                s.MechDefeated += _ => defeated++;
                s.CombatEnded += _ => ended++;
                var a = s.Mechs[0]; var b = s.Mechs[1]; var c = s.Mechs[2];
                float hp = b.Controller.currentHP;
                var p = Projectile(s, a, Vector3.zero);
                for (int i = 0; i < 8; i++) s.Step();
                check(a.Controller.currentHP == hp && b.Controller.currentHP == hp - 25 && c.Controller.currentHP == hp - 25,
                    "type1 owner excluded and each target damaged once across persistent trail");
                check(hits.Count == 2 && hits[0].targetId == 2 && hits[1].targetId == 3 &&
                    hits.All(h => h.attackId == p.AttackId), "type1 stable per-target hit records");
                for (int i = 0; i < 6; i++) s.Step();
                check(p == null && s.ActiveProjectileCount == 0, "type1 lifetime unchanged by target count");

                // Shared sweep and interval boundary, three candidates with deterministic positions.
                var snapshots = s.Query(a.Handle, 0);
                foreach (int interval in new[] { 0, 1, 2, 3 })
                {
                    var atk = TestPlayCombatCore.CreateMeleeAttackState(null, 1, 0, 6, 4, 0, interval,
                        Vector3.left * 2, Vector3.forward, "sweep");
                    var output = new List<TestPlayTargetSnapshot>();
                    for (int t = 0; t < 5; t++)
                    {
                        var r = TestPlayCombatCore.TickMeleeAttackTargets(atk, Vector3.zero, Vector3.forward, snapshots, output);
                        bool eligible = t < 4 && (t == 0 || interval <= 1 || t % interval == 0);
                        check(output.Count == (eligible ? 2 : 0), "type57 target interval " + interval + " tick " + t);
                        check(atk.remainingTicks == Mathf.Max(0, 3 - t) && r.expired == (t >= 3), "type57 lifetime once per tick");
                    }
                }
                // Endpoints miss but the swept surface crosses both spheres.
                var sweep = TestPlayCombatCore.CreateMeleeAttackState(null, 1, 0, 6, 1, 0, 0,
                    Vector3.left * 2, Vector3.forward, "surface");
                var swept = new List<TestPlayTargetSnapshot>();
                TestPlayCombatCore.TickMeleeAttackTargets(sweep, Vector3.right * 2, Vector3.forward, snapshots, swept);
                check(swept.Count == 2, "type57 previous-to-current swept face covers both targets");

                int id = AttackId(s, a);
                check(!Hit(s, id, a, a, 0x04, 100, false, 0, out _), "session owner excluded even for self-hit flag");
                b.Controller.robo.root.transform.rotation = Quaternion.Euler(0, 180, 0);
                s.Step();
                Set(b.Controller, "shieldGuard", 1);
                float before = b.Controller.currentHP;
                int effectsBeforeDefense = b.Controller.SessionHitEffectSpawnCount;
                Hit(s, id, a, b, 0, 50, true, 4, out var guard);
                check(guard.decision == TestPlayCombatHitDecision.Guarded && b.Controller.currentHP == before &&
                    b.Controller.state.GetInt(157) == 20 && guard.applyAttackerGuardRecoil && guard.attackerGuardReactionTicks == 2,
                    "guard preserves HP and produces timer/recoil feedback");
                b.Controller.state.SetInt(158, 2);
                Hit(s, id, a, b, 0x10, 50, false, 0, out var blocked);
                check(blocked.decision == TestPlayCombatHitDecision.Invulnerable && b.Controller.currentHP == before, "hit block precedes guard pierce");
                b.Controller.state.SetInt(158, 0);
                Hit(s, id, a, b, 0x10 | 0x40, 7, false, 0, out var pierce);
                check(pierce.decision == TestPlayCombatHitDecision.Damaged && b.Controller.currentHP == before - 7 &&
                    !((bool)Field(b.Controller, "sessionReactionActive")), "guard pierce and no-reaction flag");
                check(b.Controller.SessionHitEffectSpawnCount == effectsBeforeDefense + 1,
                    "only accepted damage creates a hit effect");
                Set(b.Controller, "shieldGuard", 0);
                b.Controller.TryAcquireTargetLock();
                SetProperty(b.Controller, "SessionAccumulatedDown", 0);
                Vector3 impactStart = b.Controller.robo.root.transform.position;
                Hit(s, id, a, b, 0x20, 5, true, 4, out var impact);
                check(b.Controller.SessionReactionState == 1 && b.Controller.CurrentActionSelection.logicalActionId == 13 &&
                    !b.Controller.targetLockActive && Vector3.Dot(b.Controller.robo.root.transform.forward, Vector3.back) > 0.99f &&
                    b.Controller.LastSessionDownValue == 250 && b.Controller.LastSessionImpactForce == impact.impactForce,
                    "real defender reaction pose, force snapshot and clear/facing feedback");
                check(b.Controller.LastSessionCollisionKind == TestPlayAttackCollisionKind.OriginalType57 &&
                    b.Controller.SessionAccumulatedDown == 250 &&
                    b.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.Hit,
                    "melee hit records collision kind, accumulated down and active hit phase");
                check(b.Controller.SessionHitEffectSpawnCount == effectsBeforeDefense + 2,
                    "shooting and melee damage share the hit presentation path");
                check((int)Field(b.Controller, "hitStopTicks") == 4, "new defender hit-stop not pre-decremented");
                int frame = (int)Field(b.Controller, "frameIndex");
                s.Step();
                check((int)Field(b.Controller, "hitStopTicks") == 3 && (int)Field(b.Controller, "frameIndex") == frame,
                    "defender hit-stop holds reaction pose");
                for (int i = 0; i < 120; i++) s.Step();
                check(!((bool)Field(b.Controller, "sessionReactionActive")) && b.Controller.SessionReactionState == 0,
                    "reaction finishes and releases input gate");
                check(Vector3.Distance(impactStart, b.Controller.robo.root.transform.position) > 0.01f,
                    "ATTACK force moves the defender during the reaction and decays through session motion");

                // Melee controller integration: a generated attack resolves both targets in one tick.
                var point = a.Assets.Spt.WeaponPoints.Values.First(w => w.BoneTr != null);
                int pointId = a.Assets.Spt.WeaponPoints.First(kv => kv.Value == point).Key;
                b.Controller.robo.root.transform.position = point.BoneTr.position + point.WorldForward * 2;
                c.Controller.robo.root.transform.position = point.BoneTr.position + point.WorldForward * 4;
                Set(a.Controller, "hitStopTicks", 50); Set(b.Controller, "hitStopTicks", 50); Set(c.Controller, "hitStopTicks", 50);
                Set(b.Controller, "shieldGuard", 0); Set(c.Controller, "shieldGuard", 0);
                var attack = TestPlayCombatCore.CreateMeleeAttackState(null, 3, pointId, 6, 2, 4, 2,
                    point.BoneTr.position, point.WorldForward, "integration", 0x40);
                typeof(TestPlayMeleeAttackState).GetProperty("SessionAttackId").SetValue(attack, AttackId(s, a));
                ((List<TestPlayMeleeAttackState>)Field(a.Controller, "activeMeleeAttacks")).Add(attack);
                int countBefore = hits.Count;
                s.Step();
                check(hits.Count == countBefore + 2 && hits[countBefore].targetId == 2 && hits[countBefore + 1].targetId == 3 &&
                    attack.remainingTicks == 1 && (int)Field(b.Controller, "hitStopTicks") >= 4,
                    "controller type57 resolves stable candidates and lifetime once");
                s.Step();
                check(hits.Count == countBefore + 2 && attack.remainingTicks == 0, "controller type57 interval suppresses repeat");

                // HP0 invalidates targets immediately; attacks already in flight survive.
                b.Controller.robo.root.transform.position = Vector3.forward * 2;
                c.Controller.robo.root.transform.position = Vector3.forward * 4;
                s.Step();
                var survivor = Projectile(s, b, Vector3.zero, 9);
                int kill = AttackId(s, a);
                Hit(s, kill, a, b, 0x10, 10000, false, 0, out _);
                check(b.Controller.SessionDefeated && b.Controller.currentHP == 0 &&
                    b.Controller.CurrentActionSelection.logicalActionId == 15 && !s.TryGet(b.Handle, out _) && defeated == 1,
                    "HP0 chooses available down pose and invalidates target immediately");
                check(!Hit(s, kill, a, b, 0, 10, false, 0, out _), "second same-tick hit cannot damage defeated target");
                int attacksBefore = s.ActiveProjectileCount;
                Call(b.Controller, "HandleCommand", "RunProc2", new List<TestPlayScriptValue>(), "dead fixture");
                check(s.ActiveProjectileCount == attacksBefore, "dead controller cannot spawn new attacks");
                float cBefore = c.Controller.currentHP;
                for (int i = 0; i < 5; i++) s.Step();
                check(c.Controller.currentHP == cBefore - 9 && survivor != null, "flying projectile still hits after owner defeat");
                UnityEngine.Object.DestroyImmediate(b.Controller);
                s.Step();
                check(s.Status == "Running" && survivor != null, "destroyed owner does not fault session or remove flying attack");
                Call(c.Controller, "SetAirborneFlag", false);
                Set(c.Controller, "groundedFlag", true);
                Set(c.Controller, "velocity", Vector3.zero);
                Set(c.Controller, "hitStopTicks", 0);
                Set(c.Controller, "sessionAnimationHitStopped", false);
                Hit(s, AttackId(s, a), a, c, 0x10, 10000, false, 0, out _);
                s.Step(); s.Step();
                check(ended == 1 && s.IsCombatEnded && defeated == 2, "combat end notification exactly once");
                for (int i = 0; i < TestPlayCombatCore.OriginalKnockbackMinimumTicks - 2; i++) s.Step();
                check(c.Controller.SessionDefeated &&
                    c.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.Defeated &&
                    c.Controller.CurrentActionSelection.logicalActionId == 16 &&
                    (bool)Field(c.Controller, "sessionReactionActive") &&
                    c.Controller.SessionDefeatEffectSpawnCount == 1 && c.Controller.SessionModelVisible,
                    "defeated mech settles into the down pose and remains input-gated" +
                    $" (defeated={c.Controller.SessionDefeated}, phase={c.Controller.SessionReactionPhase}, " +
                    $"action={c.Controller.CurrentActionSelection.logicalActionId}, " +
                    $"active={(bool)Field(c.Controller, "sessionReactionActive")}, " +
                    $"airborne={(bool)Field(c.Controller, "airborneFlag")}, " +
                    $"grounded={(bool)Field(c.Controller, "groundedFlag")}, " +
                    $"velocity={(Vector3)Field(c.Controller, "velocity")})");
                for (int i = 0; i < TestPlayCombatCore.OriginalDefeatPresentationTicks - 1; i++) s.Step();
                check(c.Controller.SessionModelVisible,
                    "defeated model remains visible while the 60-tick effect is active");
                s.Step();
                Renderer[] defeatedRenderers = c.Controller.robo.root.GetComponentsInChildren<Renderer>(true);
                check(!c.Controller.SessionModelVisible && defeatedRenderers.Length > 0 &&
                    defeatedRenderers.All(r => !r.enabled),
                    "defeat effect completion hides the model without removing the session root");
                check(hits.All(h => h.ownerId != h.targetId && h.hpAfter >= 0 && h.damage == h.hpBefore - h.hpAfter),
                    "combat records contain actual applied HP difference");
                File.WriteAllLines(Path.Combine(folder, "session-combat-v1-" + run + ".jsonl"), trace);
                if (expected == null) expected = trace;
                else check(expected.SequenceEqual(trace), "two fresh combat sessions produce identical ordered trace");
            }
            finally { await s.StopAsync(); }
            check(s.ActiveProjectileCount == 0 && s.Mechs.All(m => m.Assets.Robo == null), "combat session releases roots and projectiles");
        }
        // Missing poses and mutually lethal same-tick melee use separate, deliberately large fixture spheres.
        var ordered = await GameSession.CreateAsync(Enumerable.Range(0, 2).Select(i => new TestPlayMechSpawn {
            SourcePath = path, Input = new TestPlayIdleInputProvider(), TargetRadius = 10,
            Position = Vector3.forward * i, UseColliderGrounding = false }).ToArray(), TestPlaySessionClock.Manual, token);
        try
        {
            ordered.Start();
            var a = ordered.Mechs[0]; var b = ordered.Mechs[1];
            int id = AttackId(ordered, a);
            var originalPose = b.Assets.Robo.ani.animations[13];
            try
            {
                b.Assets.Robo.ani.animations[13] = null;
                Hit(ordered, id, a, b, 0, 5, false, 0, out _);
                check(b.Controller.currentHP == b.Controller.maximumHP - 5 &&
                    !((bool)Field(b.Controller, "sessionReactionActive")), "missing reaction pose applies HP without inventing an action");
            }
            finally { b.Assets.Robo.ani.animations[13] = originalPose; }
            Set(b.Controller, "airborneFlag", true);
            SetProperty(b.Controller, "SessionAccumulatedDown", 0);
            b.Controller.TryAcquireTargetLock();
            Hit(ordered, id, a, b, 0, 5, false, 0, out _);
            check(b.Controller.CurrentActionSelection.logicalActionId == 14, "airborne reaction uses available action 14");
            check(b.Controller.targetLockActive, "ordinary nonlethal reaction preserves lock without clear-link flag");
            for (int i = 0; i < 120; i++) ordered.Step();

            Call(b.Controller, "SetAirborneFlag", false);
            Set(b.Controller, "groundedFlag", true);
            Set(b.Controller, "velocity", Vector3.zero);
            b.Controller.robo.root.transform.position = Vector3.forward;
            SetProperty(b.Controller, "SessionAccumulatedDown", 400);
            Impact(ordered, AttackId(ordered, a), a, b, 1, 5, 1, 0.2f, 0f, true, 0, out _);
            check(b.Controller.CurrentActionSelection.logicalActionId == 15 &&
                b.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.Knockback,
                "melee down threshold enters the blow-away action and knockback phase");
            for (int i = 0; i < TestPlayCombatCore.OriginalKnockbackMinimumTicks; i++) ordered.Step();
            check(b.Controller.CurrentActionSelection.logicalActionId == 16 &&
                b.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.Downed,
                "knockback settles into the real action-16 down pose");
            for (int i = 0; i < TestPlayCombatCore.OriginalDownedTicks; i++) ordered.Step();
            check(b.Controller.CurrentActionSelection.logicalActionId == 17 &&
                b.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.Recovering,
                "downed phase enters the real action-17 get-up pose");
            for (int i = 0; i < TestPlayCombatCore.OriginalGetUpTicks; i++) ordered.Step();
            check(b.Controller.SessionReactionPhase == TestPlaySessionReactionPhase.None &&
                b.Controller.SessionAccumulatedDown == 0 && !((bool)Field(b.Controller, "sessionReactionActive")),
                "get-up completes, clears accumulated down and restores input");
            foreach (var mech in ordered.Mechs)
            {
                mech.Controller.robo.root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Call(mech.Controller, "SetAirborneFlag", false);
                mech.Controller.ChangeAnimation(mech.Controller.idleAction);
                var point = mech.Assets.Spt.WeaponPoints.First(kv => kv.Value.BoneTr != null);
                var attack = TestPlayCombatCore.CreateMeleeAttackState(null, 10000, point.Key, 6, 2, 0, 1,
                    point.Value.BoneTr.position, point.Value.WorldForward, "same-tick-lethal");
                typeof(TestPlayMeleeAttackState).GetProperty("SessionAttackId").SetValue(attack, AttackId(ordered, mech));
                ((List<TestPlayMeleeAttackState>)Field(mech.Controller, "activeMeleeAttacks")).Add(attack);
                Set(mech.Controller, "hitStopTicks", 20);
                Set(mech.Controller, "shieldGuard", 0);
            }
            var records = new List<TestPlaySessionHit>();
            ordered.HitResolved += h => records.Add(h);
            ordered.Step();
            File.WriteAllLines(Path.Combine(folder, "session-combat-order.jsonl"), records.Select(h => JsonUtility.ToJson(h)));
            File.WriteAllText(Path.Combine(folder, "session-combat-order-state.txt"),
                "hp=" + a.Controller.currentHP + "/" + b.Controller.currentHP + " ticks=" +
                a.Controller.tick + "/" + b.Controller.tick + "/" + ordered.Tick + " positions=" +
                a.Controller.robo.root.transform.position + "/" + b.Controller.robo.root.transform.position);
            check(a.Controller.currentHP == a.Controller.maximumHP && b.Controller.currentHP == 0 && records.Count == 1 &&
                records[0].ownerId == 1 && records[0].targetId == 2 && b.Controller.tick == ordered.Tick,
                "lower MechId lethal melee cancels later owner's attack in the same tick");
            var flying = Projectile(ordered, a, Vector3.zero);
            UnityEngine.Object.DestroyImmediate(a.Controller.robo.root);
            ordered.Step();
            check(!ordered.TryGet(a.Handle, out _) && ordered.Status == "Running" && flying != null,
                "removed live root invalidates geometry without dereferencing destroyed target/owner");
        }
        finally { await ordered.StopAsync(); }
        return assertions;
    }
}
