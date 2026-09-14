using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Localization.Settings;

/// <summary>Game flow and read-only mech catalogue. Scene views own their Canvas elements.
/// Scene/loading presentation is a Unity alternative; the existing 60 Hz session owns combat.</summary>
public sealed class TestPlayStandaloneApp : MonoBehaviour
{
    public const string SelectionScene = "Assets/Scenes/MechSelection.unity";
    public const string BattleScene = "Assets/Scenes/StandaloneTestPlay.unity";
    const string DefaultRelativeMechsRoot = "Windom_Data\\Robo";
    // Retained public facade for existing Inspector/automation callers. Rebound on scene entry.
    public GameObject hudCanvas;
    public TestPlaySessionBootstrap sessionHost;
    public TestPlayMechSlot playerSlot;
    public TestPlayMechSlot opponentSlot;
    public TestPlayLoadingView loadingView;
    public static TestPlayStandaloneApp Instance { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsReady { get; private set; }
    public bool LastOperationSucceeded { get; private set; }
    public string Message => message;
    public string MechsRoot => mechsRoot;
    public IReadOnlyList<string> MechNames => mechNames;
    public int FirstIndex => firstIndex;
    public int SecondIndex => secondIndex;
    public TestPlaySessionBootstrap Host => host;
    public string Phase { get; private set; } = "初期化中";
    public bool CanCancel => IsBusy && cancellation != null && !cancellation.IsCancellationRequested && !quitting;
    string mechsRoot = "";
    readonly List<string> mechNames = new List<string>();
    readonly Dictionary<string, string> mechDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Texture2D> selectionImages = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    int firstIndex, secondIndex;
    string message = "機体フォルダから自機と相手機を選んでください。";
    TestPlaySessionBootstrap host;
    TestPlaySelectionView selectionView;
    Camera gameCamera;
    bool quitting;
    CancellationTokenSource cancellation;
    Task operation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstance() { Instance = null; }

    void Awake()
    {
        if (!Application.isPlaying) return;
        if (Instance != null && Instance != this) { gameObject.SetActive(false); Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    async void Start()
    {
        if (Instance != this) return;
        Application.runInBackground = true;
        mechsRoot = Argument("--mechs-folder") ?? DefaultMechsRoot();
        await ExecuteAsync(async token => {
            SetPhase("初期化中", false);
            if (!Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer &&
                !Assimp.Unmanaged.AssimpLibrary.Instance.LibraryLoaded)
                Assimp.Unmanaged.AssimpLibrary.Instance.LoadLibrary(
                    Path.Combine(Application.dataPath, "Plugins", "x86_64", "assimp64.dll"));
            await LocalizationSettings.InitializationOperation.Task;
            token.ThrowIfCancellationRequested();
            RefreshMechList();
            string first = Argument("--mech");
            string output = Argument("--verify-output");
            string presentation = Argument("--verify-presentation-output");
            if (!string.IsNullOrEmpty(output) || !string.IsNullOrEmpty(presentation))
            {
                await LoadSceneAsync(BattleScene);
                BindBattle();
                SetPhase("検証中", false);
                if (!string.IsNullOrEmpty(presentation))
                {
                    var report = await TestPlayPresentationVerification.RunAsync(first, presentation, gameCamera, token, host.presentationTemplate);
                    Debug.Log("[Standalone Player] Presentation " + JsonUtility.ToJson(report));
                    await LoadSceneAsync(SelectionScene); BindSelection();
                    loadingView.Show(false);
                    await CaptureSelectionVerification(presentation, "selection-ui.png");
                    ReleaseImages();
                    selectionImages[SelectedName(firstIndex)] = null;
                    selectionImages[SelectedName(secondIndex)] = null;
                    selectionView.RefreshSelection();
                    await CaptureSelectionVerification(presentation, "selection-missing-ui.png");
                }
                else
                {
                    var report = await TestPlaySessionLifecycleVerification.RunAsync(first, output, gameCamera, token, host.presentationTemplate);
                    Debug.Log("[Standalone Player] " + JsonUtility.ToJson(report));
                }
                if (!Application.isEditor) Application.Quit(0);
            }
            else if (!string.IsNullOrEmpty(first))
                await EnterBattleAsync(first, Argument("--opponent") ?? first, token);
            else
            {
                await LoadSceneAsync(SelectionScene);
                BindSelection();
            }
            IsReady = true;
        });
        if (!LastOperationSucceeded && (!string.IsNullOrEmpty(Argument("--verify-output")) || !string.IsNullOrEmpty(Argument("--verify-presentation-output"))) && !Application.isEditor)
            Application.Quit(1);
        string uiOutput = Argument("--verify-ui-output");
        if (!string.IsNullOrEmpty(uiOutput) && IsReady && !IsBusy)
        {
            await TestPlayUiFlowVerification.StartVerification(uiOutput);
            if (!Application.isEditor) Application.Quit(TestPlayUiFlowVerification.Current.state == "Succeeded" ? 0 : 1);
        }
    }

    public void SelectMech(bool opponent, int index)
    {
        if (IsBusy || index < 0 || index >= mechNames.Count) return;
        if (opponent) secondIndex = index; else firstIndex = index;
        selectionView?.RefreshSelection();
    }

    public void RefreshCatalogue(string root)
    {
        if (IsBusy) return;
        mechsRoot = root;
        try { RefreshMechList(); }
        catch (Exception e) { message = "機体一覧を読み込めません: " + e.Message; }
        selectionView?.Bind(this);
    }

    public string DisplayNameAt(int index) => GetDisplayName(SelectedName(index));
    public Texture2D PreviewAt(int index) => GetSelectionImage(SelectedName(index));

    public Task StartBattleAsync()
    {
        if (IsBusy || quitting) return Task.CompletedTask;
        return ExecuteAsync(async token => {
            // Validate both independent selections before leaving the selection scene.
            string first = ResolveSelectedPath(firstIndex);
            string second = ResolveSelectedPath(secondIndex);
            await EnterBattleAsync(first, second, token);
        });
    }

    async Task EnterBattleAsync(string first, string second, CancellationToken token)
    {
        await LoadSceneAsync(BattleScene);
        token.ThrowIfCancellationRequested();
        BindBattle();
        ReleaseImages();
        SetPhase("機体データを読み込み中", false);
        await host.StartAsync(first, second, token);
        token.ThrowIfCancellationRequested();
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }

    public Task RestartBattleAsync()
    {
        if (IsBusy || quitting || host == null || host.Status != "Running") return Task.CompletedTask;
        return ExecuteAsync(async token => {
            SetPhase("再戦の準備中", false);
            await host.RestartAsync(token);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        });
    }

    public Task ReturnToSelectionAsync()
    {
        if (IsBusy || quitting) return Task.CompletedTask;
        return ExecuteAsync(async token => { await StopBattleAsync(); await LoadSceneAsync(SelectionScene); BindSelection(); });
    }

    public void CancelLoading()
    {
        if (!CanCancel) return;
        cancellation.Cancel();
        SetPhase("読込を中止しています", false);
    }

    public async Task QuitAsync()
    {
        if (quitting) return;
        quitting = true;
        cancellation?.Cancel();
        if (operation != null) await operation;
        await StopBattleAsync();
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return Task.CompletedTask;
        IsBusy = true; LastOperationSucceeded = false;
        cancellation = new CancellationTokenSource();
        loadingView?.Show(true);
        return operation = ExecuteCoreAsync(action, cancellation.Token);
    }

    async Task ExecuteCoreAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try { await action(token); message = ""; LastOperationSucceeded = true; }
        catch (Exception e)
        {
            message = e is OperationCanceledException ? "読込を中止しました。" : "読み込みに失敗しました: " + e.Message;
            try
            {
                await StopBattleAsync();
                if (!quitting) { await LoadSceneAsync(SelectionScene); BindSelection(); IsReady = true; }
            }
            catch (Exception recovery) { message += "\n選択画面への復帰に失敗しました: " + recovery.Message; Debug.LogException(recovery); }
        }
        finally
        {
            cancellation.Dispose(); cancellation = null; IsBusy = false;
            loadingView?.Show(false);
            selectionView?.RefreshSelection();
        }
    }

    async Task StopBattleAsync()
    {
        SetPhase("戦闘を終了しています", false);
        if (host == null) host = FindFirstObjectByType<TestPlayBattleView>()?.sessionHost;
        if (host != null) await host.StopAsync();
        host = null; sessionHost = null; hudCanvas = null; playerSlot = null; opponentSlot = null; gameCamera = null;
    }

    async Task LoadSceneAsync(string path)
    {
        if (SceneManager.GetActiveScene().path == path) return;
        SetPhase(path == SelectionScene ? "機体選択画面を読み込み中" : "戦闘シーンを読み込み中", true);
        // Unity scene loads cannot be cancelled midway. Finish activation, then honour cancellation
        // before loading mechs; cleanup and return to selection use the same serialized flow.
        var load = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
        if (load == null) throw new IOException("シーンを読み込めません: " + path);
        while (!load.isDone) { loadingView?.SetProgress(load.progress / 0.9f); await Task.Yield(); }
    }

    void BindSelection()
    {
        selectionView = FindFirstObjectByType<TestPlaySelectionView>();
        if (selectionView == null) throw new InvalidOperationException("機体選択Canvasがありません。");
        selectionView.Bind(this);
    }

    void BindBattle()
    {
        selectionView = null;
        var view = FindFirstObjectByType<TestPlayBattleView>();
        if (view == null || view.sessionHost == null || view.gameCamera == null)
            throw new InvalidOperationException("戦闘シーンの参照が未設定です。");
        host = sessionHost = view.sessionHost;
        gameCamera = view.gameCamera; host.gameCamera = gameCamera;
        hudCanvas = host.hudCanvas; playerSlot = host.playerSlot; opponentSlot = host.opponentSlot;
        view.Bind(this);
    }

    void SetPhase(string value, bool progress)
    {
        Phase = value;
        loadingView?.SetPhase(value, progress);
    }

    async Task CaptureSelectionVerification(string directory, string filename)
    {
        float ready = Time.realtimeSinceStartup + 0.2f;
        while (Time.realtimeSinceStartup < ready) await Task.Yield();
        string path = Path.Combine(directory, filename);
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) await Task.Yield();
        if (!File.Exists(path)) throw new IOException("機体選択画面の保存がタイムアウトしました。");
    }

    public static string Argument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
        return null;
    }
    void RefreshMechList()
    {
        string previousFirst = SelectedName(firstIndex);
        string previousSecond = SelectedName(secondIndex);
        foreach (Texture2D image in selectionImages.Values)
            ReleaseSelectionImage(image);
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
            // Use the shared decoder, including in a standalone Player without CP932 providers.
            string text = USEncoder.ToEncoding.ToUnicode(File.ReadAllBytes(sdtPath));
            string line = text.Split(new[] { '\r', '\n' }, 2)[0];
            if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Standalone Player] charaselect.sdtの読込に失敗しました: " + sdtPath + " / " + e.Message);
        }
        return folderName;
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

    Texture2D GetSelectionImage(string mechName)
    {
        if (string.IsNullOrEmpty(mechName) || string.IsNullOrEmpty(mechsRoot))
            return null;
        if (selectionImages.TryGetValue(mechName, out Texture2D cached))
            return cached;

        string directory = Path.Combine(mechsRoot, mechName);
        string selectPath = null;
        Texture2D texture = null;
        try
        {
            selectPath = Directory.Exists(directory) ? FindFileIgnoreCase(directory, "select.png") : null;
            if (selectPath == null) return null;
            CypherTranscoder transcoder = new CypherTranscoder();
            // Local extension registration preserves the shared decoder and its scan rules.
            transcoder.registerFileType(Path.GetExtension(selectPath), 0x474e5089);
            if (!transcoder.findCypher(selectPath)) return null;
            byte[] imageBytes = transcoder.Transcode(selectPath);
            // Avoid passing an absent/truncated signature to the native image decoder.
            if (imageBytes.Length < 8 || imageBytes[4] != 13 || imageBytes[5] != 10
                || imageBytes[6] != 26 || imageBytes[7] != 10) return null;
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(imageBytes, false)) return null;
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
        finally
        {
            if (!selectionImages.ContainsKey(mechName)) selectionImages[mechName] = null;
            if (selectionImages[mechName] != texture) ReleaseSelectionImage(texture);
        }
    }

    static void ReleaseSelectionImage(Texture2D texture)
    {
        if (texture == null) return;
        if (Application.isPlaying) Destroy(texture);
        else DestroyImmediate(texture);
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


    void ReleaseImages()
    {
        foreach (Texture2D image in selectionImages.Values) ReleaseSelectionImage(image);
        selectionImages.Clear();
    }

    void OnDestroy()
    {
        if (Instance == this) { Instance = null; cancellation?.Cancel(); }
        ReleaseImages();
    }
}
