using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Explicit single-scene build; does not replace the editor Build Settings scenes.</summary>
public static class TestPlayStandaloneBuild
{
    public const string ScenePath = "Assets/Scenes/StandaloneTestPlay.unity";
    public static string Status { get; private set; } = "Idle";
    public static string Failure { get; private set; }

    [MenuItem("Tools/WindomXP/Test Play/Build Standalone Windows Player")]
    public static void ChooseBuild()
    {
        string folder = EditorUtility.SaveFolderPanel("独立Playerの新規出力先", "", "StandalonePlayer");
        if (!string.IsNullOrEmpty(folder)) StartBuild(folder);
    }

    public static void StartBuild(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
            WindomVerificationRunner.IsRunning || Status == "Building")
            throw new InvalidOperationException("Play・検証・コンパイルの終了後にビルドしてください。");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("未保存のシーンを保持するにはTools/BuildStandalone.ps1の作業コピーを使用してください。");
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0)
            throw new IOException("空の出力フォルダを指定してください。");
        Directory.CreateDirectory(output);
        Status = "Building"; Failure = null;
        Build(output);
    }

    static void Build(string output)
    {
        try
        {
            if (!File.Exists(ScenePath))
            {
                var original = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    SceneManager.SetActiveScene(scene);
                    new GameObject("StandaloneApp").AddComponent<TestPlayStandaloneApp>();
                    if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("専用シーンの保存に失敗しました。");
                }
                finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(original); }
            }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = Path.Combine(output, "WindomStandalone.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            Status = report.summary.result == BuildResult.Succeeded ? "Succeeded" : "Failed";
            File.WriteAllText(Path.Combine(output, "build-result.txt"),
                Status + "\nUnity=" + Application.unityVersion + "\nErrors=" + report.summary.totalErrors +
                "\nWarnings=" + report.summary.totalWarnings + "\nBytes=" + report.summary.totalSize);
            if (Status == "Failed") Failure = "Playerビルドに失敗しました。Consoleを確認してください。";
        }
        catch (Exception e)
        {
            Status = "Failed"; Failure = e.ToString();
            File.WriteAllText(Path.Combine(output, "build-failure.txt"), Failure);
            Debug.LogException(e);
        }
    }

    public static void BuildBatch()
    {
        try
        {
            StartBuild(TestPlayStandaloneApp.Argument("--standalone-output") ?? throw new ArgumentException("--standalone-output is required"));
            EditorApplication.Exit(Status == "Succeeded" ? 0 : 1);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
