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
        return assertions;
    }
}

/// <summary>Real loaded controllers; deterministic collision fixtures plus real ANI Play checks.</summary>
public static class TestPlaySessionCombatVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    static object Field(object obj, string name) => obj.GetType().GetField(name, Private).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    static int AttackId(GameSession s, MechRuntime owner) => (int)Call(s, "AllocateAttackId", owner.Handle);
    static bool Hit(GameSession s, int id, MechRuntime owner, MechRuntime defender, int flag, float damage,
        bool melee, int stop, out TestPlayCombatHitResult resolved)
    {
        var hit = TestPlayCombatCore.CreateHitResult(new TestPlayProjectilePayload { damage = damage, attackFlag = flag,
            down = 250, collisionKind = melee ? TestPlayAttackCollisionKind.OriginalType57 : TestPlayAttackCollisionKind.OriginalType1,
            horizontalImpactForce = 0.2f, source = "combat-fixture" }, Vector3.forward);
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
                Set(b.Controller, "shieldGuard", 0);
                b.Controller.TryAcquireTargetLock();
                Hit(s, id, a, b, 0x20, 5, true, 4, out var impact);
                check(b.Controller.SessionReactionState == 1 && b.Controller.CurrentActionSelection.logicalActionId == 13 &&
                    !b.Controller.targetLockActive && Vector3.Dot(b.Controller.robo.root.transform.forward, Vector3.back) > 0.99f &&
                    b.Controller.LastSessionDownValue == 250 && b.Controller.LastSessionImpactForce == impact.impactForce,
                    "real defender reaction pose, force snapshot and clear/facing feedback");
                check((int)Field(b.Controller, "hitStopTicks") == 4, "new defender hit-stop not pre-decremented");
                int frame = (int)Field(b.Controller, "frameIndex");
                s.Step();
                check((int)Field(b.Controller, "hitStopTicks") == 3 && (int)Field(b.Controller, "frameIndex") == frame,
                    "defender hit-stop holds reaction pose");
                for (int i = 0; i < 120; i++) s.Step();
                check(!((bool)Field(b.Controller, "sessionReactionActive")) && b.Controller.SessionReactionState == 0,
                    "reaction finishes and releases input gate");

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
                Hit(s, AttackId(s, a), a, c, 0x10, 10000, false, 0, out _);
                s.Step(); s.Step();
                check(ended == 1 && s.IsCombatEnded && defeated == 2, "combat end notification exactly once");
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
            b.Controller.TryAcquireTargetLock();
            Hit(ordered, id, a, b, 0, 5, false, 0, out _);
            check(b.Controller.CurrentActionSelection.logicalActionId == 14, "airborne reaction uses available action 14");
            check(b.Controller.targetLockActive, "ordinary nonlethal reaction preserves lock without clear-link flag");
            for (int i = 0; i < 120; i++) ordered.Step();
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
