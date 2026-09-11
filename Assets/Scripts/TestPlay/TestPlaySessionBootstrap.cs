using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Reusable host. Each rematch owns fresh session IDs, inputs and assets.</summary>
public sealed class TestPlaySessionBootstrap : MonoBehaviour
{
    public GameSession Session { get; private set; }
    public Camera gameCamera;
    [Tooltip("プレイヤー機体用。シーン配置の TestPlayHUDCanvas。未設定時は実行時生成。")]
    public GameObject hudCanvas;
    [Tooltip("自機スロット。未設定時はセッション都度 SessionMech を生成する。")]
    public TestPlayMechSlot playerSlot;
    [Tooltip("相手機スロット。未設定時はセッション都度 SessionMech を生成する。")]
    public TestPlayMechSlot opponentSlot;
    public string Status { get; private set; } = "Created";
    public string Failure { get; private set; }
    public bool CombatEnded => Session != null && Session.IsCombatEnded;
    TestPlayBufferedInputProvider keyboard;
    TestPlayCameraController cameraAdapter;
    TestPlayController previousController;
    Camera previousCamera;
    bool ownsCameraAdapter, destroyed, restarting;
    CancellationTokenSource loading;
    Task startTask, stopTask;
    string firstSource, secondSource;

    public Task StartAsync(string firstPath, string secondPath, CancellationToken token = default)
    {
        if (destroyed || restarting || Status == "Loading" || Status == "Running" || Status == "Stopping" ||
            (startTask != null && !startTask.IsCompleted) || (stopTask != null && !stopTask.IsCompleted))
            throw new InvalidOperationException("起動・停止処理の完了後に開始してください。");
        return BeginStart(firstPath, secondPath, token);
    }

    Task BeginStart(string firstPath, string secondPath, CancellationToken token)
    {
        firstSource = firstPath; secondSource = secondPath;
        Status = "Loading"; Failure = null; stopTask = null; Session = null;
        loading = CancellationTokenSource.CreateLinkedTokenSource(token);
        return startTask = StartCoreAsync(firstPath, secondPath, loading.Token);
    }

    async Task StartCoreAsync(string firstPath, string secondPath, CancellationToken token)
    {
        try
        {
            keyboard = new TestPlayBufferedInputProvider(1);
            if (gameCamera != null)
            {
                var adapter = gameCamera.GetComponent<TestPlayCameraController>();
                if (adapter != null && adapter.cameraModeActive)
                    throw new InvalidOperationException("カメラは使用中です。");
                ownsCameraAdapter = adapter == null;
                cameraAdapter = adapter != null ? adapter : gameCamera.gameObject.AddComponent<TestPlayCameraController>();
                previousController = cameraAdapter.controller;
                previousCamera = cameraAdapter.controlledCamera;
                cameraAdapter.controlledCamera = gameCamera;
            }
            Session = await GameSession.CreateAsync(new[] {
                CreateSpawn(firstPath, playerSlot, new Vector3(-3, 0, 0), Quaternion.identity,
                    keyboard, cameraAdapter, showHud: true, hudCanvas),
                CreateSpawn(secondPath, opponentSlot, new Vector3(3, 0, 12), Quaternion.Euler(0, 180, 0),
                    new TestPlayIdleInputProvider(), null, showHud: false, null)
            }, TestPlaySessionClock.Automatic, token);
            token.ThrowIfCancellationRequested();
            Session.Start();
            Status = "Running";
        }
        catch (Exception e)
        {
            Failure = e.Message;
            try { if (Session != null) await Session.StopAsync(); }
            finally { await RestoreCameraAsync(); keyboard = null; Status = e is OperationCanceledException ? "Cancelled" : "Faulted"; }
            throw;
        }
        finally { loading.Dispose(); loading = null; }
    }

    public async Task RestartAsync(CancellationToken token = default)
    {
        if (destroyed || restarting || Status == "Created" || Status == "Loading" || Status == "Stopping")
            throw new InvalidOperationException("再戦できる状態ではありません。");
        restarting = true;
        try
        {
            await StopAsync();
            token.ThrowIfCancellationRequested();
            if (destroyed) throw new OperationCanceledException();
            await BeginStart(firstSource, secondSource, token);
        }
        finally { restarting = false; }
    }

    async void Update()
    {
        if (Status != "Running") return;
        try
        {
            AdvanceInput(TestPlayKeyboardInput.Sample(cameraAdapter), Time.deltaTime);
        }
        catch (Exception e)
        {
            Status = "Faulted"; Failure = e.Message;
            Debug.LogException(e);
            try { await StopAsync(); } catch (Exception cleanup) { Debug.LogException(cleanup); }
        }
    }

    // Explicit frame input for automation/replay hosts; disable this component's Update when used.
    public int AdvanceInput(TestPlaySessionInput input, double seconds)
    {
        if (Status != "Running") throw new InvalidOperationException("セッションは実行中ではありません。");
        keyboard.Submit(CombatEnded ? default : input);
        return Session.Advance(seconds);
    }

    public Task StopAsync() => stopTask ?? (stopTask = StopCoreAsync());
    async Task StopCoreAsync()
    {
        bool failed = Status == "Faulted";
        Status = "Stopping";
        loading?.Cancel();
        if (startTask != null) try { await startTask; } catch { }
        bool cancelled = Status == "Cancelled";
        failed |= Status == "Faulted";
        try
        {
            if (Session != null) await Session.StopAsync();
            await RestoreCameraAsync();
            keyboard = null;
            Status = failed ? "Faulted" : cancelled ? "Cancelled" : "Stopped";
        }
        catch (Exception e) { Status = "Faulted"; Failure = e.Message; throw; }
    }

    async Task RestoreCameraAsync()
    {
        if (cameraAdapter == null) return;
        var adapter = cameraAdapter;
        cameraAdapter = null;
        adapter.ExitTestPlayCamera();
        if (ownsCameraAdapter)
        {
            if (Application.isPlaying)
            {
                Destroy(adapter);
                while (adapter != null) await Task.Yield();
            }
            else DestroyImmediate(adapter);
        }
        else
        {
            adapter.controlledCamera = previousCamera;
            adapter.Bind(previousController);
        }
    }

    async void OnDestroy()
    {
        destroyed = true;
        try { await StopAsync(); } catch (Exception e) { Debug.LogException(e); }
    }

    static TestPlayMechSpawn CreateSpawn(string path, TestPlayMechSlot slot,
        Vector3 fallbackPosition, Quaternion fallbackRotation,
        ITestPlayInputProvider input, TestPlayCameraController camera,
        bool showHud, GameObject hudCanvas)
    {
        bool useSlot = slot != null;
        return new TestPlayMechSpawn
        {
            SourcePath = path,
            Host = useSlot ? slot.gameObject : null,
            Position = useSlot ? slot.SpawnPosition : fallbackPosition,
            Rotation = useSlot ? slot.SpawnRotation : fallbackRotation,
            Input = input,
            Camera = camera,
            ShowHud = showHud,
            HudCanvas = hudCanvas
        };
    }
}
