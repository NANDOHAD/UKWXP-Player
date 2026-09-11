using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>P1-05a one-mech host. The legacy clock is retained until P1-05b session-v1.</summary>
public sealed class TestPlayStandaloneBootstrap : MonoBehaviour
{
    [Tooltip("機体ANI/AN2の絶対パス。旧ANIは変換せず読み込みます。")]
    public string sourcePath;
    public bool startOnPlay;
    public Camera gameCamera;
    public bool showHud = true;
    [Tooltip("プレイヤー機体用。シーン配置の TestPlayHUDCanvas。未設定時は実行時生成。")]
    public GameObject hudCanvas;
    public string Status { get; private set; } = "Created";
    public string Failure { get; private set; }
    public TestPlayController Controller { get; private set; }
    public MechAssetLease Assets { get; private set; }
    CancellationTokenSource loading;
    Task startTask;
    Task stopTask;
    TestPlayCameraController borrowedCamera;
    TestPlayController previousCameraController;
    Camera previousControlledCamera;

    async void Start()
    {
        if (!startOnPlay) return;
        try { await StartAsync(sourcePath); }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogException(e); }
    }

    public Task StartAsync(string path, CancellationToken cancellationToken = default)
    {
        if (Status != "Created") throw new InvalidOperationException("新しい独立起動hostを使用してください: " + Status);
        Status = "Loading";
        loading = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return startTask = StartCoreAsync(path, loading.Token);
    }

    async Task StartCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var data = await MechLoader.LoadAsync(path, null, cancellationToken);
            Assets = await UnityMechBuilder.BuildAsync(data, path, cancellationToken);
            Assets.Host.transform.SetParent(transform, false);
            TestPlayCameraController cameraAdapter = null;
            if (gameCamera != null)
            {
                cameraAdapter = gameCamera.GetComponent<TestPlayCameraController>();
                if (cameraAdapter == null)
                {
                    cameraAdapter = gameCamera.gameObject.AddComponent<TestPlayCameraController>();
                    Assets.Register(cameraAdapter);
                }
                else
                {
                    if (cameraAdapter.cameraModeActive)
                        throw new InvalidOperationException("カメラは別のテストプレイで使用中です。");
                    borrowedCamera = cameraAdapter;
                    previousCameraController = cameraAdapter.controller;
                    previousControlledCamera = cameraAdapter.controlledCamera;
                }
                cameraAdapter.controlledCamera = gameCamera;
            }
            Controller = Assets.Host.AddComponent<TestPlayController>();
            Controller.Initialize(new TestPlayContext { Robo = Assets.Robo, Spt = Assets.Spt,
                Camera = cameraAdapter, ShowHud = showHud, HudCanvas = showHud ? hudCanvas : null });
            cancellationToken.ThrowIfCancellationRequested();
            Controller.StartTestPlay();
            Status = "Running";
        }
        catch (Exception e)
        {
            Failure = e.Message;
            if (Controller != null) Controller.StopTestPlay();
            RestoreBorrowedCamera();
            if (Assets != null) await Assets.ReleaseAsync();
            Status = e is OperationCanceledException ? "Cancelled" : "Faulted";
            throw;
        }
        finally { loading.Dispose(); loading = null; }
    }

    public Task StopAsync() => stopTask ?? (stopTask = StopCoreAsync());

    async Task StopCoreAsync()
    {
        loading?.Cancel();
        if (startTask != null)
        {
            try { await startTask; }
            catch { /* Start preserves the failure; cleanup still has to finish. */ }
        }
        bool failed = Status == "Faulted" || Status == "Cancelled";
        if (!failed) Status = "Stopping";
        if (Controller != null) Controller.StopTestPlay();
        RestoreBorrowedCamera();
        if (Assets != null) await Assets.ReleaseAsync();
        if (!failed) Status = "Stopped";
    }

    void RestoreBorrowedCamera()
    {
        if (borrowedCamera == null) return;
        borrowedCamera.controlledCamera = previousControlledCamera;
        borrowedCamera.Bind(previousCameraController);
        borrowedCamera = null;
    }

    async void OnDestroy()
    {
        try { await StopAsync(); }
        catch (Exception e) { Debug.LogException(e); }
    }
}
