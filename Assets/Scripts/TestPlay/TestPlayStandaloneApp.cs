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
    // フォルダ名 → charaselect.sdt 1行目の表示名（なければフォルダ名をフォールバック）
    readonly Dictionary<string, string> mechDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Texture2D> selectionImages = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    int firstIndex, secondIndex;
    bool sameOpponent = true;
    // ドロップダウンの開閉状態
    bool firstDropdownOpen, secondDropdownOpen;
    Vector2 firstDropdownScroll, secondDropdownScroll;
    // フォルダパス欄の折りたたみ状態
    bool folderFoldout = false;
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
        if (host.presentationTemplate == null)
        {
            TestPlayMechSlot mappingSlot = playerSlot != null ? playerSlot : host.playerSlot;
            if (mappingSlot != null)
                host.presentationTemplate = mappingSlot.GetComponent<TestPlayPresentationRuntime>();
        }
        if (host.presentationTemplate == null)
        {
            GameObject canvas = hudCanvas != null ? hudCanvas : host.hudCanvas;
            if (canvas != null)
                host.presentationTemplate = canvas.GetComponentInChildren<TestPlayPresentationRuntime>(true);
        }
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
                var report = await TestPlaySessionLifecycleVerification.RunAsync(
                    cliFirst, output, gameCamera, default, host.presentationTemplate);
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
        GUILayout.BeginArea(new Rect(15, 15, 720, 560), GUI.skin.box);

        // ── タイトルと状態 ──────────────────────────────────
        GUILayout.Label("Ultimate Knight ウィンダムXP MODプレイヤー");
        GUILayout.Label("状態: " + DisplayStatus(host.Status));
        GUILayout.Space(4);

        // ── フォルダパス（折りたたみ） ────────────────────────
        bool editable = !busy;
        GUI.enabled = editable;
        folderFoldout = GUILayout.Toggle(folderFoldout, "▶ Roboフォルダパス", GUI.skin.button);
        if (folderFoldout)
        {
            GUILayout.BeginHorizontal();
            mechsRoot = GUILayout.TextField(mechsRoot);
            GUILayout.EndHorizontal();
        }
        bool refresh = GUILayout.Button("フォルダ内の機体を再読込み");
        GUILayout.Space(4);

        // ── 機体選択（2カラム） ────────────────────────────────
        if (mechNames.Count == 0)
        {
            GUILayout.Label("機体フォルダが見つかりません。ルートを確認して再読込してください。");
        }
        else
        {
            GUILayout.BeginHorizontal();

            // 自機カラム
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330));
            GUILayout.Label("【 自機 】");
            bool firstChanged;
            firstIndex = DrawMechDropdown(firstIndex, ref firstDropdownOpen, ref firstDropdownScroll,
                "firstDropdown", out firstChanged);
            if (firstChanged && firstDropdownOpen) secondDropdownOpen = false;
            // ドロップダウン展開中はプレビューを隠す（リストの下にselect.pngが埋もれないよう）
            if (!firstDropdownOpen) DrawMechPreview(SelectedName(firstIndex), 310, 160);
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // 相手機カラム
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330));
            GUILayout.Label("【 相手機 】");

            bool secondChanged;
            secondIndex = DrawMechDropdown(secondIndex, ref secondDropdownOpen, ref secondDropdownScroll,
                "secondDropdown", out secondChanged);
            if (secondChanged && secondDropdownOpen) firstDropdownOpen = false;
            // ドロップダウン展開中はプレビューを隠す（リストの下にselect.pngが埋もれないよう）
            if (!secondDropdownOpen) DrawMechPreview(SelectedName(secondIndex), 310, 160);

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        GUILayout.Space(4);

        // ── 操作ボタン行 ──────────────────────────────────────
        GUILayout.BeginHorizontal();
        bool start = GUILayout.Button("▶ 開始", GUILayout.Height(30));
        GUI.enabled = !quitting;
        bool quit = GUILayout.Button("✕ アプリを終了", GUILayout.Height(30));
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Label("操作: 矢印=移動  Z=上昇/ブースト  X=射撃  C=格闘  V=ガード  S=ロック  A/D/F=必殺技");
        if (!string.IsNullOrEmpty(message)) GUILayout.Label(message);
        GUILayout.EndArea();

        if (refresh)
        {
            RefreshMechList();
            return;
        }
        if (start)
        {
            firstDropdownOpen = false;
            secondDropdownOpen = false;
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
        foreach (Texture2D image in selectionImages.Values)
            if (image != null) Destroy(image);
        selectionImages.Clear();
        mechNames.Clear();
        mechDisplayNames.Clear();
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
            string folderName = Path.GetFileName(directory);
            mechNames.Add(folderName);
            // charaselect.sdt の1行目を表示名として読む（なければフォルダ名をフォールバック）
            mechDisplayNames[folderName] = ReadCharaSelectName(directory, folderName);
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

    /// <summary>機体フォルダ内の charaselect.sdt 1行目を表示名として返す。ファイルがなければ folderName を返す。</summary>
    static string ReadCharaSelectName(string directory, string folderName)
    {
        string sdtPath = FindFileIgnoreCase(directory, "charaselect.sdt");
        if (sdtPath == null) return folderName;
        try
        {
            // Shift-JIS（CP932）で読む
            var encoding = System.Text.Encoding.GetEncoding(932);
            using (var reader = new StreamReader(sdtPath, encoding))
            {
                string line = reader.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                    return line.Trim();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Standalone Player] charaselect.sdtの読込に失敗しました: " + sdtPath + " / " + e.Message);
        }
        return folderName;
    }

    /// <summary>ドロップダウン形式の機体選択。展開/折りたたみと選択を管理する。</summary>
    /// <param name="changed">このフレームにドロップダウン展開状態が変わったか</param>
    int DrawMechDropdown(int index, ref bool open, ref Vector2 scroll, string controlName, out bool changed)
    {
        changed = false;
        if (mechNames.Count == 0)
            return 0;

        // ドロップダウンボタン: charaselect.sdt由来の表示名を表示
        string displayName = SelectedDisplayName(index);
        if (GUILayout.Button(string.IsNullOrEmpty(displayName) ? "-- 選択してください --" : displayName))
        {
            open = !open;
            changed = true;
        }

        if (!open)
            return index;

        // 展開時: スクロール付きリスト表示（表示名を使用）
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(140));
        for (int i = 0; i < mechNames.Count; i++)
        {
            bool isSelected = (i == index);
            // 選択中は強調
            var origColor = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.4f, 0.8f, 1f, 1f);
            string label = GetDisplayName(mechNames[i]);
            if (GUILayout.Button(label))
            {
                index = i;
                open = false;
                changed = true;
            }
            GUI.backgroundColor = origColor;
        }
        GUILayout.EndScrollView();
        return index;
    }

    /// <summary>インデックスから表示名（charaselect.sdt 1行目 or フォルダ名）を返す。</summary>
    string SelectedDisplayName(int index)
    {
        string folderName = SelectedName(index);
        return GetDisplayName(folderName);
    }

    /// <summary>フォルダ名から表示名を引く。登録がなければフォルダ名をそのまま返す。</summary>
    string GetDisplayName(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return "";
        string display;
        return mechDisplayNames.TryGetValue(folderName, out display) ? display : folderName;
    }

    /// <summary>select.pngのプレビュー画像を指定サイズの枠内に描画する。</summary>
    void DrawMechPreview(string mechName, float maxWidth, float maxHeight)
    {
        Texture2D preview = GetSelectionImage(mechName);
        if (preview == null) return;
        float aspect = (float)preview.width / Mathf.Max(1f, preview.height);
        float w = Mathf.Min(maxWidth, maxHeight * aspect);
        float h = w / aspect;
        Rect rect = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
        GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
    }
    

    Texture2D GetSelectionImage(string mechName)
    {
        if (string.IsNullOrEmpty(mechName) || string.IsNullOrEmpty(mechsRoot))
            return null;
        if (selectionImages.TryGetValue(mechName, out Texture2D cached))
            return cached;

        string directory = Path.Combine(mechsRoot, mechName);
        string selectPath = FindFileIgnoreCase(directory, "select.png");
        if (selectPath == null)
        {
            selectionImages[mechName] = null;
            return null;
        }

        try
        {
            CypherTranscoder transcoder = new CypherTranscoder();
            if (!transcoder.findCypher(selectPath))
            {
                selectionImages[mechName] = null;
                return null;
            }
            byte[] imageBytes = transcoder.Transcode(selectPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(imageBytes, false))
            {
                Destroy(texture);
                selectionImages[mechName] = null;
                return null;
            }
            texture.name = mechName + "_select";
            selectionImages[mechName] = texture;
            return texture;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Standalone Player] select.pngの表示に失敗しました: " + selectPath + " / " + e.Message);
            selectionImages[mechName] = null;
            return null;
        }
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

    void OnDestroy()
    {
        if (font != null) Destroy(font);
        foreach (Texture2D image in selectionImages.Values)
            if (image != null) Destroy(image);
        selectionImages.Clear();
    }
}
