using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Play-only scene replacement: Unity restores the original editing scenes on exit.</summary>
[InitializeOnLoad]
public static class TestPlayStandaloneLauncher
{
    const string PendingPath = "WindomXP.Standalone.PendingPath";
    const string PendingSecondPath = "WindomXP.Standalone.PendingSecondPath";
    static TestPlayStandaloneLauncher()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/WindomXP/Test Play/Start Standalone (ANI or AN2)")]
    public static void ChooseAndStart()
    {
        string path = EditorUtility.OpenFilePanel("独立テストプレイ用ANI/AN2を選択",
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Windom_Data", "Robo"), "");
        if (!string.IsNullOrEmpty(path)) Start(path);
    }

    public static void Start(string path)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            WindomVerificationRunner.IsRunning)
            throw new InvalidOperationException("検証・Play・コンパイルの終了後に起動してください。");
        if (!File.Exists(path)) throw new FileNotFoundException("機体ファイルがありません。", path);
        SessionState.SetString(PendingPath, Path.GetFullPath(path));
        SessionState.EraseString(PendingSecondPath);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/WindomXP/Test Play/Start Two-Mech Session (same ANI or AN2)")]
    public static void ChooseAndStartTwoMechs()
    {
        string path = EditorUtility.OpenFilePanel("2機体テスト用ANI/AN2を選択（同じ機体を2体生成）",
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Windom_Data", "Robo"), "");
        if (!string.IsNullOrEmpty(path)) StartTwoMechs(path, path);
    }

    public static void StartTwoMechs(string firstPath, string secondPath)
    {
        if (!File.Exists(secondPath)) throw new FileNotFoundException("2体目の機体ファイルがありません。", secondPath);
        Start(firstPath);
        SessionState.SetString(PendingSecondPath, Path.GetFullPath(secondPath));
    }

    static async void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        string path = SessionState.GetString(PendingPath, "");
        string secondPath = SessionState.GetString(PendingSecondPath, "");
        SessionState.EraseString(PendingPath);
        SessionState.EraseString(PendingSecondPath);
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var oldScenes = new System.Collections.Generic.List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) oldScenes.Add(SceneManager.GetSceneAt(i));
            Scene scene = SceneManager.CreateScene("StandaloneTestPlay");
            SceneManager.SetActiveScene(scene);
            foreach (var oldScene in oldScenes)
            {
                var unload = SceneManager.UnloadSceneAsync(oldScene);
                while (unload != null && !unload.isDone) await System.Threading.Tasks.Task.Yield();
            }
            var cameraObject = new GameObject("StandaloneCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.position = new Vector3(0, 3, -8);
            var lightObject = new GameObject("StandaloneLight", typeof(Light));
            var light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            lightObject.transform.rotation = Quaternion.Euler(45, -30, 0);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "StandaloneGround";
            floor.transform.localScale = Vector3.one * 100;
            if (string.IsNullOrEmpty(secondPath))
            {
                var host = new GameObject("StandaloneTestPlayHost").AddComponent<TestPlayStandaloneBootstrap>();
                host.gameCamera = cameraObject.GetComponent<Camera>();
                host.sourcePath = path;
                await host.StartAsync(path);
            }
            else
            {
                var host = new GameObject("TwoMechSessionHost").AddComponent<TestPlaySessionBootstrap>();
                host.gameCamera = cameraObject.GetComponent<Camera>();
                await host.StartAsync(path, secondPath);
            }
            Debug.Log("[Standalone] Running: " + path + ". Play終了で編集シーンに戻ります。");
        }
        catch (Exception e) { Debug.LogException(e); }
    }
}
