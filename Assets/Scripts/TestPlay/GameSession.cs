using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public enum TestPlaySessionClock { Automatic, Manual }

public readonly struct MechHandle
{
    public readonly Guid SessionId;
    public readonly int MechId;
    public MechHandle(Guid sessionId, int mechId) { SessionId = sessionId; MechId = mechId; }
}

public sealed class TestPlayMechSpawn
{
    public string SourcePath;
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.identity;
    public float TargetRadius = 1f;
    public ITestPlayInputProvider Input;
    public TestPlayCameraController Camera;
    public bool ShowHud;
    public GameObject HudCanvas;
    public bool UseColliderGrounding = true;
    /// <summary>Optional scene mech slot. When set, UnityMechBuilder assembles into it without owning the GameObject.</summary>
    public GameObject Host;
}

/// <summary>Sphere/pose at tick start. HP remains owned by the controller.</summary>
public readonly struct TestPlayTargetSnapshot
{
    public readonly MechHandle Handle;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly float Radius;
    public readonly bool Alive;
    public readonly int StateId;
    public TestPlayTargetSnapshot(MechRuntime mech)
    {
        Handle = mech.Handle;
        Position = mech.Controller.robo.root.transform.position;
        Rotation = mech.Controller.robo.root.transform.rotation;
        Radius = mech.TargetRadius;
        Alive = mech.IsAlive;
        StateId = mech.Controller.state.GetInt(156);
    }
}

public interface ITestPlayTargetProvider
{
    bool TryGet(MechHandle handle, out TestPlayTargetSnapshot snapshot);
    IReadOnlyList<TestPlayTargetSnapshot> Query(MechHandle owner, float maximumDistance);
}

/// <summary>Stable identity plus references to one controller's authoritative state.</summary>
public sealed class MechRuntime
{
    public MechHandle Handle { get; internal set; }
    public TestPlayController Controller { get; internal set; }
    public MechAssetLease Assets { get; internal set; }
    public float TargetRadius { get; internal set; }
    public bool IsAlive => Controller != null && Controller.robo != null && Controller.robo.root != null &&
        Controller.currentHP > 0 && !Controller.SessionDefeated && Controller.playModeActive;
    internal ITestPlayInputProvider Input;
    internal Transform SnapshotTransform;
}

/// <summary>
/// session-v1 Unity contract: one 60 Hz clock, ordered mech updates, next-tick projectiles.
/// Sphere collision and reaction presentation are Unity adapters, not original EXE parity.
/// </summary>
public sealed class GameSession : ITestPlayTargetProvider
{
    public const double TickSeconds = 1.0 / 60.0;
    public Guid SessionId { get; } = Guid.NewGuid();
    public string Status { get; private set; } = "Created";
    public string Failure { get; private set; }
    public int Tick { get; private set; }
    public TestPlaySessionClock Clock { get; }
    public int MaximumCatchUpTicks { get; set; } = 8;
    public IReadOnlyList<MechRuntime> Mechs => mechs.AsReadOnly();
    public int ActiveProjectileCount => projectiles.Count;
    public event Action<int, TestPlaySessionInput[]> TickCompleted;
    public event Action<TestPlaySessionHit> HitResolved;
    public event Action<MechHandle> MechDefeated;
    public event Action<int> CombatEnded;
    public bool IsCombatEnded { get; private set; }
    readonly List<MechRuntime> mechs = new List<MechRuntime>();
    readonly List<TestPlayProjectile> projectiles = new List<TestPlayProjectile>();
    readonly Dictionary<int, TestPlayTargetSnapshot> snapshots = new Dictionary<int, TestPlayTargetSnapshot>();
    double accumulator;
    int nextAttackId;
    bool stepping;
    bool stopRequested;
    Task stopTask;

    GameSession(TestPlaySessionClock clock) { Clock = clock; }

    public static async Task<GameSession> CreateAsync(IReadOnlyList<TestPlayMechSpawn> spawns,
        TestPlaySessionClock clock, CancellationToken token = default)
    {
        if (spawns == null || spawns.Count == 0) throw new ArgumentException("機体を指定してください。");
        // Copy mutable requests before awaiting. Never expose a half-ready session.
        var requests = new List<TestPlayMechSpawn>();
        foreach (var s in spawns)
        {
            if (s == null || s.Input == null || !float.IsFinite(s.TargetRadius) || s.TargetRadius <= 0)
                throw new ArgumentException("入力providerと正の対象半径が必要です。");
            requests.Add(new TestPlayMechSpawn { SourcePath = s.SourcePath, Position = s.Position,
                Rotation = s.Rotation, TargetRadius = s.TargetRadius, Input = s.Input,
                Camera = s.Camera, ShowHud = s.ShowHud, HudCanvas = s.HudCanvas,
                UseColliderGrounding = s.UseColliderGrounding, Host = s.Host });
        }
        var session = new GameSession(clock) { Status = "Loading" };
        try
        {
            foreach (var s in requests)
            {
                if (!System.IO.File.Exists(s.SourcePath))
                    throw new System.IO.FileNotFoundException("機体ファイルがありません。", s.SourcePath);
                var data = await MechLoader.LoadAsync(s.SourcePath, null, token);
                var assets = await UnityMechBuilder.BuildAsync(data, s.SourcePath, token, s.Host);
                var mech = new MechRuntime { Handle = new MechHandle(session.SessionId, session.mechs.Count + 1),
                    Assets = assets, Input = s.Input, TargetRadius = s.TargetRadius };
                session.mechs.Add(mech);
                if (assets.OwnsHost)
                    assets.Host.name = "SessionMech_" + mech.Handle.MechId;
                assets.Robo.root.transform.SetPositionAndRotation(s.Position, s.Rotation);
                var proxy = new GameObject("TargetSnapshot_" + mech.Handle.MechId);
                assets.Register(proxy);
                mech.SnapshotTransform = proxy.transform;
                mech.Controller = assets.Host.AddComponent<TestPlayController>();
                // Borrowed slots keep the GameObject; controller must be enrolled for release.
                assets.Register(mech.Controller);
                mech.Controller.useColliderGrounding = s.UseColliderGrounding;
                mech.Controller.Initialize(new TestPlayContext { Robo = assets.Robo, Spt = assets.Spt,
                    Camera = s.Camera, ShowHud = s.ShowHud, HudCanvas = s.HudCanvas });
                mech.Controller.AttachSession(session, mech.Handle);
                // Allow cancellation between complete mech loads, including synchronous imports.
                await Task.Yield();
                token.ThrowIfCancellationRequested();
            }
            session.Status = "Ready";
            return session;
        }
        catch (Exception e)
        {
            session.Failure = e.Message;
            await session.StopAsync();
            session.Status = e is OperationCanceledException ? "Cancelled" : "Faulted";
            throw;
        }
    }

    public void Start()
    {
        if (Status != "Ready") throw new InvalidOperationException("セッションは開始できません: " + Status);
        try
        {
            foreach (var mech in mechs)
            {
                try { mech.Controller.StartTestPlay(); }
                finally { mech.Assets.Register(mech.Controller.GetComponent<TestPlayHudRuntime>()?.OwnedCanvas); }
            }
            CaptureTargets();
            Status = "Running";
        }
        catch (Exception e) { Fault(e); throw; }
    }

    public int Advance(double deltaSeconds)
    {
        RequireRunning();
        if (Clock != TestPlaySessionClock.Automatic) throw new InvalidOperationException("manual時計へ自動更新できません。");
        if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        accumulator += deltaSeconds;
        int count = 0;
        while (!stopRequested && accumulator + 1e-9 >= TickSeconds && count < Math.Max(1, MaximumCatchUpTicks))
        {
            StepProviders();
            accumulator -= TickSeconds;
            count++;
        }
        return count;
    }

    public void Step()
    {
        RequireRunning();
        if (Clock != TestPlaySessionClock.Manual) throw new InvalidOperationException("自動時計へ手動tickを注入できません。");
        StepProviders();
    }

    void RequireRunning()
    {
        if (Status != "Running" || stepping || stopRequested) throw new InvalidOperationException("セッションを進められません: " + Status);
    }

    void StepProviders()
    {
        stepping = true;
        try
        {
            var inputs = new TestPlaySessionInput[mechs.Count];
            for (int i = 0; i < mechs.Count; i++) inputs[i] = mechs[i].Input.Read(mechs[i].Handle.MechId, Tick + 1);
            CaptureTargets();
            int existingProjectiles = projectiles.Count;
            Tick++;
            foreach (var mech in mechs) if (mech.Controller != null) mech.Controller.PrepareSessionTimers();
            for (int i = 0; i < mechs.Count; i++)
                if (mechs[i].Controller != null && mechs[i].Controller.playModeActive &&
                    mechs[i].Controller.robo != null && mechs[i].Controller.robo.root != null)
                    mechs[i].Controller.SimulateSessionTick(inputs[i]);
            for (int i = 0; i < existingProjectiles; i++)
                if (projectiles[i] != null) projectiles[i].SimulateSessionTick(this);
            projectiles.RemoveAll(p => p == null || p.SessionExpired);
            foreach (var mech in mechs) if (mech.Controller != null) mech.Controller.ValidateSessionLock();
            if (!IsCombatEnded && mechs.Count > 1 && mechs.FindAll(m => m.IsAlive).Count <= 1)
            {
                IsCombatEnded = true;
                CombatEnded?.Invoke(Tick);
            }
            TickCompleted?.Invoke(Tick, inputs);
        }
        catch (Exception e) { Fault(e); throw; }
        finally { stepping = false; }
    }

    void Fault(Exception e)
    {
        Failure = e.Message;
        Status = "Faulted";
        // Immediately stop simulation and presentation. Asset release is awaited by StopAsync.
        foreach (var mech in mechs) if (mech.Controller != null) mech.Controller.StopTestPlay();
    }

    void CaptureTargets()
    {
        snapshots.Clear();
        foreach (var mech in mechs)
        {
            if (mech.Controller == null || mech.Controller.robo == null || mech.Controller.robo.root == null) continue;
            var snapshot = new TestPlayTargetSnapshot(mech);
            snapshots.Add(mech.Handle.MechId, snapshot);
            if (mech.SnapshotTransform != null)
                mech.SnapshotTransform.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
        }
    }

    public bool TryGet(MechHandle handle, out TestPlayTargetSnapshot snapshot)
    {
        snapshot = default;
        return Status == "Running" && handle.SessionId == SessionId &&
            snapshots.TryGetValue(handle.MechId, out snapshot) && snapshot.Alive &&
            mechs[handle.MechId - 1].IsAlive;
    }

    public IReadOnlyList<TestPlayTargetSnapshot> Query(MechHandle owner, float maximumDistance)
    {
        var result = new List<TestPlayTargetSnapshot>();
        if (!TryGet(owner, out var source)) return result;
        foreach (var candidate in snapshots.Values)
            if (candidate.Handle.MechId != owner.MechId && TryGet(candidate.Handle, out _) &&
                (maximumDistance <= 0 || Vector3.Distance(source.Position, candidate.Position) <= maximumDistance))
                result.Add(candidate);
        result.Sort((a, b) => {
            int distance = (a.Position - source.Position).sqrMagnitude.CompareTo((b.Position - source.Position).sqrMagnitude);
            return distance != 0 ? distance : a.Handle.MechId.CompareTo(b.Handle.MechId);
        });
        return result;
    }

    internal Transform GetSnapshotTransform(MechHandle handle) => TryGet(handle, out _)
        ? mechs[handle.MechId - 1].SnapshotTransform : null;

    // Flying attacks survive their owner. Collision queries do not require a living owner.
    internal IReadOnlyList<TestPlayTargetSnapshot> QueryAttackTargets(MechHandle owner, Vector3 origin)
    {
        var result = new List<TestPlayTargetSnapshot>();
        if (Status != "Running" || owner.SessionId != SessionId || owner.MechId < 1 || owner.MechId > mechs.Count)
            return result;
        foreach (var candidate in snapshots.Values)
            if (candidate.Handle.MechId != owner.MechId && TryGet(candidate.Handle, out _)) result.Add(candidate);
        result.Sort((a, b) => {
            int distance = (a.Position - origin).sqrMagnitude.CompareTo((b.Position - origin).sqrMagnitude);
            return distance != 0 ? distance : a.Handle.MechId.CompareTo(b.Handle.MechId);
        });
        return result;
    }

    internal int AllocateAttackId(MechHandle owner)
    {
        if (!TryGet(owner, out _)) throw new InvalidOperationException("有効な攻撃ownerが必要です。");
        return ++nextAttackId;
    }

    internal bool ResolveHit(int attackId, MechHandle owner, MechHandle target,
        TestPlayCombatHitResult hit, Vector3 attackerPosition, bool melee, int hitStopTicks,
        out TestPlayCombatHitResult resolved)
    {
        resolved = hit;
        if (attackId <= 0 || attackId > nextAttackId || owner.SessionId != SessionId ||
            owner.MechId < 1 || owner.MechId > mechs.Count || owner.MechId == target.MechId ||
            !TryGet(target, out var snapshot)) return false;
        var defender = mechs[target.MechId - 1].Controller;
        float before = defender.currentHP;
        resolved = defender.ResolveSessionImpact(hit, attackerPosition, snapshot, melee, hitStopTicks);
        HitResolved?.Invoke(new TestPlaySessionHit { sessionId = SessionId.ToString(), tick = Tick,
            attackId = attackId, ownerId = owner.MechId, targetId = target.MechId,
            decision = resolved.decision, requestedDamage = hit.damage, damage = before - defender.currentHP,
            hpBefore = before, hpAfter = defender.currentHP, reaction = resolved.reactionState });
        if (before > 0 && defender.currentHP <= 0) MechDefeated?.Invoke(target);
        return true;
    }

    internal void RegisterProjectile(TestPlayProjectile projectile, MechHandle owner, MechHandle guidance)
    {
        if (Status != "Running" || owner.SessionId != SessionId)
            throw new InvalidOperationException("停止中のセッションには弾を追加できません。");
        projectile.AttachSession(this, AllocateAttackId(owner), owner, guidance);
        projectiles.Add(projectile);
    }

    public Task StopAsync()
    {
        stopRequested = true;
        return stopTask ?? (stopTask = StopCoreAsync());
    }

    async Task StopCoreAsync()
    {
        // Event consumers may request shutdown during a tick; finish that tick before cleanup.
        while (stepping) await Task.Yield();
        bool failed = Status == "Faulted";
        Status = "Stopping";
        accumulator = 0;
        TickCompleted = null;
        HitResolved = null;
        MechDefeated = null;
        CombatEnded = null;
        foreach (var mech in mechs) if (mech.Controller != null) mech.Controller.StopTestPlay();
        snapshots.Clear();
        // Include projectiles created outside a controller fixture; wait for deferred Destroy.
        var pending = new List<GameObject>();
        foreach (var p in projectiles)
        {
            if (p == null) continue;
            pending.Add(p.gameObject);
            if (Application.isPlaying) UnityEngine.Object.Destroy(p.gameObject);
            else UnityEngine.Object.DestroyImmediate(p.gameObject);
        }
        projectiles.Clear();
        var failures = new List<Exception>();
        foreach (var mech in mechs)
        {
            mech.Input = null;
            try { await mech.Assets.ReleaseAsync(); }
            catch (Exception e) { failures.Add(e); }
        }
        while (pending.Exists(p => p != null)) await Task.Yield();
        if (failures.Count > 0)
        {
            Status = "Faulted";
            Failure = new AggregateException(failures).ToString();
            throw new AggregateException(failures);
        }
        Status = failed ? "Faulted" : "Stopped";
    }
}

[Serializable]
public struct TestPlaySessionHit
{
    public string sessionId;
    public int tick, attackId, ownerId, targetId, reaction;
    public TestPlayCombatHitDecision decision;
    public float requestedDamage, damage, hpBefore, hpAfter;
}
