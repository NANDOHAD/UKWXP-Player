using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>Dedicated Player entry. External game data is read only and never bundled implicitly.</summary>
public sealed class TestPlayStandaloneApp : MonoBehaviour
{
    const string DefaultRelativeMechsRoot = "Windom_Data\\Robo";

    [Tooltip("プレイヤー機体用。シーン配置の TestPlayHUDCanvas。未設定時は実行時生成。")]
    public GameObject hudCanvas;
    [Tooltip("シーン配置のセッションhost。未設定時は実行時に生成する。")]
    public TestPlaySessionBootstrap sessionHost;
    [Tooltip("自機スロット。sessionHost未設定時のフォールバック用。")]
    public TestPlayMechSlot playerSlot;
    [Tooltip("相手機スロット。sessionHost未設定時のフォールバック用。")]
    public TestPlayMechSlot opponentSlot;

    string mechsRoot = "";
    readonly List<string> mechNames = new List<string>();
    int firstIndex, secondIndex;
    bool sameOpponent = true;
    Vector2 mechScroll;
    string message = "機体フォルダから自機と相手機を選んでください。";
    string cliFirst = "", cliSecond = "";
    TestPlaySessionBootstrap host;
    Camera gameCamera;
    bool busy, verifying, quitting;
    Font font;

    async void Start()
    {
        Application.runInBackground = true;
        var cam = new GameObject("StandaloneCamera", typeof(Camera), typeof(AudioListener));
        gameCamera = cam.GetComponent<Camera>();
        cam.transform.position = new Vector3(0, 3, -8);
        var light = new GameObject("StandaloneLight", typeof(Light)).GetComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.3f;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "StandaloneGround"; floor.transform.localScale = Vector3.one * 100;
        host = ResolveSessionHost();
        host.gameCamera = gameCamera;
        if (hudCanvas != null) host.hudCanvas = hudCanvas;
        if (playerSlot != null && host.playerSlot == null) host.playerSlot = playerSlot;
        if (opponentSlot != null && host.opponentSlot == null) host.opponentSlot = opponentSlot;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Meiryo", "Yu Gothic", "Arial" }, 16);
        mechsRoot = Argument("--mechs-folder") ?? DefaultMechsRoot();
        cliFirst = Argument("--mech") ?? "";
        cliSecond = Argument("--opponent") ?? cliFirst;
        string output = Argument("--verify-output");
        verifying = !string.IsNullOrEmpty(output);
        try
        {
            // The legacy Assimp wrapper defaults to the process working directory.
            // A standalone launch must resolve the packaged native DLL explicitly.
            if (!Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer &&
                !Assimp.Unmanaged.AssimpLibrary.Instance.LibraryLoaded)
                Assimp.Unmanaged.AssimpLibrary.Instance.LoadLibrary(
                    Path.Combine(Application.dataPath, "Plugins", "x86_64", "assimp64.dll"));
            await LocalizationSettings.InitializationOperation.Task;
            RefreshMechList();
            if (verifying)
            {
                Debug.Log("[Standalone Player] Lifecycle verification started");
                var report = await TestPlaySessionLifecycleVerification.RunAsync(cliFirst, output, gameCamera);
                Debug.Log("[Standalone Player] " + JsonUtility.ToJson(report));
                if (!Application.isEditor) Application.Quit(0);
            }
            else if (!string.IsNullOrEmpty(cliFirst))
                await Run(() => host.StartAsync(cliFirst, string.IsNullOrWhiteSpace(cliSecond) ? cliFirst : cliSecond));
        }
        catch (Exception e)
        {
            message = e.Message; Debug.LogException(e);
            if (verifying && !Application.isEditor) Application.Quit(1);
        }
    }

    public static string Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    async Task Run(Func<Task> operation)
    {
        busy = true;
        try { await operation(); message = ""; }
        catch (OperationCanceledException) { message = "読込を中止しました。"; }
        catch (Exception e) { message = e.Message; }
        finally { busy = false; }
    }

    async void OnGUI()
    {
        if (host == null || verifying) return;
        if (font != null) GUI.skin.font = font;

        // Finish GUI layout before awaiting; resumed continuations must not invoke GUILayout.
        if (IsBattleUiActive())
            await DrawBattleUi();
        else
            await DrawMainUi();
    }

    /// <summary>開始後（読込・実行・終了処理中）は機体選択を隠し、操作最小の小窓だけを出す。</summary>
    bool IsBattleUiActive()
    {
        string status = host.Status;
        return status == "Loading" || status == "Running" || status == "Stopping" || busy;
    }

    async Task DrawMainUi()
    {
        GUILayout.BeginArea(new Rect(15, 15, 680, 520), GUI.skin.box);
        GUILayout.Label("独立テストプレイ / " + DisplayStatus(host.Status));
        bool editable = !busy;
        GUI.enabled = editable;

        GUILayout.Label("機体ルートフォルダ（UI_SelectMechと同じ構成）");
        GUILayout.BeginHorizontal();
        mechsRoot = GUILayout.TextField(mechsRoot);
        bool refresh = GUILayout.Button("再読込", GUILayout.Width(80));
        GUILayout.EndHorizontal();

        if (mechNames.Count == 0)
            GUILayout.Label("機体フォルダが見つかりません。ルートを確認して再読込してください。");
        else
        {
            mechScroll = GUILayout.BeginScrollView(mechScroll, GUILayout.Height(220));
            GUILayout.Label("自機");
            firstIndex = GUILayout.SelectionGrid(firstIndex, mechNames.ToArray(), 1);
            GUILayout.Space(8);
            sameOpponent = GUILayout.Toggle(sameOpponent, "相手機は自機と同じ");
            if (!sameOpponent)
            {
                GUILayout.Label("相手機");
                secondIndex = GUILayout.SelectionGrid(secondIndex, mechNames.ToArray(), 1);
            }
            GUILayout.EndScrollView();
        }

        GUILayout.BeginHorizontal();
        bool start = GUILayout.Button("開始");
        GUI.enabled = !quitting;
        bool quit = GUILayout.Button("アプリを終了");
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label("矢印: 移動  Z: 上昇/ブースト  X: 射撃  C: 格闘  V: ガード  S: ロック  A/D/F: 必殺技");
        if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
        GUILayout.EndArea();

        if (refresh)
        {
            RefreshMechList();
            return;
        }
        if (start)
        {
            try
            {
                string firstPath = ResolveSelectedPath(firstIndex);
                string secondPath = sameOpponent ? firstPath : ResolveSelectedPath(secondIndex);
                await Run(() => host.StartAsync(firstPath, secondPath));
            }
            catch (Exception e) { message = e.Message; }
        }
        else if (quit) await QuitApp();
    }

    async Task DrawBattleUi()
    {
        GUILayout.BeginArea(new Rect(15, 15, 360, 160), GUI.skin.box);
        GUILayout.Label(DisplayStatus(host.Status));
        if (host.CombatEnded) GUILayout.Label("戦闘終了 — 再戦できます。");

        GUILayout.BeginHorizontal();
        GUI.enabled = host.Status == "Loading" || (!busy && host.Status == "Running");
        bool stop = GUILayout.Button(host.Status == "Loading" ? "読込中止" : "戦闘終了");
        GUI.enabled = !busy && host.Status == "Running";
        bool restart = GUILayout.Button("再戦");
        GUI.enabled = !quitting;
        bool quit = GUILayout.Button("アプリを終了");
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
        GUILayout.EndArea();

        if (restart) await Run(() => host.RestartAsync());
        else if (stop) await Run(() => host.StopAsync());
        else if (quit) await QuitApp();
    }

    async Task QuitApp()
    {
        quitting = true;
        await Run(() => host.StopAsync());
        Application.Quit();
    }

    void RefreshMechList()
    {
        string previousFirst = SelectedName(firstIndex);
        string previousSecond = SelectedName(secondIndex);
        mechNames.Clear();
        firstIndex = 0;
        secondIndex = 0;

        if (string.IsNullOrWhiteSpace(mechsRoot))
        {
            message = "機体ルートフォルダを指定してください。";
            return;
        }

        string root = Path.GetFullPath(mechsRoot.Trim());
        mechsRoot = root;
        if (!Directory.Exists(root))
        {
            message = "機体ルートフォルダがありません: " + root;
            return;
        }

        foreach (string directory in Directory.GetDirectories(root))
        {
            string scriptAni = FindFileIgnoreCase(directory, "Script.ani");
            if (scriptAni == null || !IsAn2Container(scriptAni))
                continue;
            mechNames.Add(Path.GetFileName(directory));
        }
        mechNames.Sort(StringComparer.OrdinalIgnoreCase);

        if (mechNames.Count == 0)
        {
            message = "AN2形式のScript.aniがある機体フォルダがみつかりません。";
            return;
        }

        firstIndex = IndexOfName(previousFirst);
        secondIndex = IndexOfName(previousSecond);
        message = mechNames.Count + "機の機体フォルダを読み込みました。";
    }

    string SelectedName(int index)
    {
        return index >= 0 && index < mechNames.Count ? mechNames[index] : "";
    }

    int IndexOfName(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        int index = mechNames.FindIndex(entry =>
            string.Equals(entry, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : 0;
    }

    string ResolveSelectedPath(int index)
    {
        if (index < 0 || index >= mechNames.Count)
            throw new InvalidOperationException("機体が選択されていません。");
        return ResolveContainerPath(Path.Combine(mechsRoot, mechNames[index]));
    }

    /// <summary>機体フォルダ内のScript.aniのうち、AN2規格のものだけを対象にする。</summary>
    public static string ResolveContainerPath(string mechFolder)
    {
        if (string.IsNullOrWhiteSpace(mechFolder) || !Directory.Exists(mechFolder))
            throw new DirectoryNotFoundException("機体フォルダがありません: " + mechFolder);

        string scriptAni = FindFileIgnoreCase(mechFolder, "Script.ani");
        if (scriptAni == null)
            throw new FileNotFoundException(
                "機体フォルダにScript.aniが見つかりません: " + mechFolder);
        if (!IsAn2Container(scriptAni))
            throw new InvalidDataException(
                "Script.aniがAN2形式ではありません（旧ANI等は対象外）: " + scriptAni);
        return scriptAni;
    }

    static bool IsAn2Container(string path)
    {
        AniContainerFormat format;
        string error;
        return UI_SelectMech.TryDetectContainerFormat(path, out format, out error)
            && format == AniContainerFormat.An2;
    }

    static string FindFileIgnoreCase(string directory, string fileName)
    {
        foreach (string path in Directory.GetFiles(directory))
        {
            if (string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    static string DefaultMechsRoot()
    {
        DirectoryInfo parent = Directory.GetParent(Application.dataPath);
        if (parent != null)
            return Path.Combine(parent.FullName, "Windom_Data", "Robo");
        return Path.GetFullPath(DefaultRelativeMechsRoot);
    }

    static string DisplayStatus(string status)
    {
        switch (status)
        {
            case "Loading": return "読込中";
            case "Running": return "実行中";
            case "Stopping": return "終了処理中";
            case "Stopped": return "終了";
            case "Cancelled": return "読込中止";
            case "Faulted": return "起動・実行失敗";
            default: return "開始待ち";
        }
    }

    TestPlaySessionBootstrap ResolveSessionHost()
    {
        if (sessionHost != null) return sessionHost;
        sessionHost = GetComponent<TestPlaySessionBootstrap>();
        if (sessionHost != null) return sessionHost;
        sessionHost = FindFirstObjectByType<TestPlaySessionBootstrap>();
        if (sessionHost != null) return sessionHost;
        var created = new GameObject("TwoMechSessionHost");
        sessionHost = created.AddComponent<TestPlaySessionBootstrap>();
        return sessionHost;
    }

    void OnDestroy() { if (font != null) Destroy(font); }
}
