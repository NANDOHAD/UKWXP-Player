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
    const string DefaultRelativeDataRoot = "Windom_Data";
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
    public string Message => message();
    public string DataRoot => dataRoot;
    public string MechsRoot => mechsRoot;
    public IReadOnlyList<string> MechNames => mechNames;
    public IReadOnlyList<TestPlayPilotData> Pilots => pilots;
    public string PilotsRoot { get; private set; }
    public string PilotMessage => pilotMessage();
    Func<string> pilotMessage = () => "";
    public TestPlayPilotData FirstPilot => PilotAt(firstPilotIndex);
    public TestPlayPilotData SecondPilot => PilotAt(secondPilotIndex);
    readonly List<TestPlayPilotData> pilots = new List<TestPlayPilotData>();
    int firstPilotIndex = -1, secondPilotIndex = -1;
    public int FirstIndex => firstIndex;
    public int SecondIndex => secondIndex;
    public TestPlaySessionBootstrap Host => host;
    public TestPlayCpuSettings OpponentCpuSettings { get; private set; } = new TestPlayCpuSettings();
    // Selection UI and programmatic callers share the same immutable session settings.
    public void ConfigureOpponentCpu(TestPlayCpuSettings settings)
    {
        if (IsBusy || (host != null && host.Status == "Running"))
            throw new InvalidOperationException("CPU設定は戦闘開始前に変更してください。");
        OpponentCpuSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        selectionView?.RefreshSelection();
    }
    public string Phase { get; private set; } = "";
    public bool CanCancel => IsBusy && cancellation != null && !cancellation.IsCancellationRequested && !quitting;
    string dataRoot = "";
    string mechsRoot = "";
    readonly List<string> mechNames = new List<string>();
    readonly Dictionary<string, string> mechDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> mechDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Texture2D> selectionImages = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    int firstIndex, secondIndex;
    Func<string> message = () => TestPlayLocalization.Text("機体フォルダから自機と相手機を選んでください。");
    TestPlaySessionBootstrap host;
    TestPlaySelectionView selectionView;
    Camera gameCamera;
    bool quitting;
    CancellationTokenSource cancellation;
    Task operation;
    public readonly List<TestPlayStageData> Maps = new List<TestPlayStageData>();
    public readonly List<string> BgmFiles = new List<string>();
    public int MapIndex { get; private set; } = -1;
    public int BgmIndex { get; private set; } = -1;
    public TestPlayStageData SelectedMap => MapIndex >= 0 && MapIndex < Maps.Count ? Maps[MapIndex] : null;
    public string SelectedBgm => BgmIndex >= 0 && BgmIndex < BgmFiles.Count ? BgmFiles[BgmIndex] : null;
    public string EnvironmentMessage => environmentMessage();
    Func<string> environmentMessage = () => "";
    public TestPlayBgmPlayer Music { get; private set; }
    public TestPlayStageRuntime Stage { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstance() { Instance = null; }

    void Awake()
    {
        if (!Application.isPlaying) return;
        if (Instance != null && Instance != this) { gameObject.SetActive(false); Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Music = gameObject.AddComponent<TestPlayBgmPlayer>();
    }

    async void Start()
    {
        if (Instance != this) return;
        Application.runInBackground = true;
        string explicitData = Argument("--data-folder");
        string explicitMechs = Argument("--mechs-folder");
        if (!string.IsNullOrEmpty(explicitData))
        {
            dataRoot = Path.GetFullPath(explicitData);
            mechsRoot = explicitMechs != null ? Path.GetFullPath(explicitMechs) : Path.Combine(dataRoot, "Robo");
        }
        else if (!string.IsNullOrEmpty(explicitMechs))
        {
            ConfigureRoots(explicitMechs);
        }
        else
        {
            dataRoot = DefaultDataRoot();
            mechsRoot = Path.Combine(dataRoot, "Robo");
        }
        await ExecuteAsync(async token => {
            SetPhase(TestPlayLocalization.Text("初期化中"), false);
            if (!Application.isEditor && Application.platform == RuntimePlatform.WindowsPlayer &&
                !Assimp.Unmanaged.AssimpLibrary.Instance.LibraryLoaded)
                Assimp.Unmanaged.AssimpLibrary.Instance.LoadLibrary(
                    Path.Combine(Application.dataPath, "Plugins", "x86_64", "assimp64.dll"));
            await LocalizationSettings.InitializationOperation.Task;
            token.ThrowIfCancellationRequested();
            RefreshMechList();
            RefreshPilots();
            RefreshEnvironment();
            string first = Argument("--mech");
            string output = Argument("--verify-output");
            string presentation = Argument("--verify-presentation-output");
            if (!string.IsNullOrEmpty(output) || !string.IsNullOrEmpty(presentation))
            {
                await LoadSceneAsync(BattleScene);
                BindBattle();
                SetPhase(TestPlayLocalization.Text("検証中"), false);
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
        string cpuOutput = Argument("--verify-cpu-output");
        if (!string.IsNullOrEmpty(cpuOutput))
        {
            try
            {
                await TestPlayCpuIntegrationVerification.RunAsync(cpuOutput);
                if (!Application.isEditor) Application.Quit(TestPlayCpuIntegrationVerification.Current.state == "Succeeded" ? 0 : 1);
            }
            catch (Exception e) { Debug.LogException(e); if (!Application.isEditor) Application.Quit(1); }
            return;
        }
        string integrationOutput = Argument("--verify-integration-output");
        if (!string.IsNullOrEmpty(integrationOutput))
        {
            try
            {
                await TestPlayIntegrationPresentationVerification.StartVerification(integrationOutput, Argument("--verify-graphics") == "true");
                if (!Application.isEditor) Application.Quit(TestPlayIntegrationPresentationVerification.Current.state == "Succeeded" ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (!Application.isEditor) Application.Quit(1);
            }
            return;
        }
        string movementOutput = Argument("--verify-movement-output");
        if (!string.IsNullOrEmpty(movementOutput) && IsReady && !IsBusy)
        {
            await TestPlayMovementPresentationVerification.StartVerification(movementOutput);
            if (!Application.isEditor) Application.Quit(TestPlayMovementPresentationVerification.Current.state == "Succeeded" ? 0 : 1);
        }
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
        ConfigureRoots(root);
        try { Music.Stop(); RefreshMechList(); RefreshPilots(); RefreshEnvironment(); }
        catch (Exception e) { message = () => TestPlayLocalization.Text("機体一覧を読み込めません: ") + e.Message; }
        selectionView?.Bind(this);
    }

    TestPlayPilotData PilotAt(int index) => index >= 0 && index < pilots.Count ? pilots[index] : null;

    void RefreshEnvironment()
    {
        string map = SelectedMap?.DirectoryPath, bgm = SelectedBgm;
        Maps.Clear(); BgmFiles.Clear(); MapIndex = BgmIndex = -1;
        var diagnostics = new List<string>();
        try
        {
            string root = !string.IsNullOrEmpty(dataRoot) ? dataRoot : DefaultDataRoot();
            string mapRoot = Path.Combine(root, "Map"), musicRoot = Path.Combine(root, "Sound", "MyBGM");
            if (Directory.Exists(mapRoot))
            {
                var folders = Directory.GetDirectories(mapRoot); Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
                foreach (string folder in folders)
                {
                    if (!File.Exists(Path.Combine(folder, "map.mpd"))) continue;
                    try { Maps.Add(TestPlayStageData.Read(folder)); }
                    catch (Exception e) { diagnostics.Add(Path.GetFileName(folder) + ": " + e.Message); }
                }
            }
            if (Directory.Exists(musicRoot)) foreach (string file in Directory.GetFiles(musicRoot))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".ogg" || extension == ".wav" || extension == ".mp3") BgmFiles.Add(file);
            }
            BgmFiles.Sort(StringComparer.OrdinalIgnoreCase);
            MapIndex = Maps.FindIndex(m => string.Equals(m.DirectoryPath, map, StringComparison.OrdinalIgnoreCase));
            if (MapIndex < 0 && Maps.Count > 0) MapIndex = 0;
            BgmIndex = BgmFiles.FindIndex(p => string.Equals(p, bgm, StringComparison.OrdinalIgnoreCase));
            environmentMessage = () => TestPlayLocalization.Text("{0}マップ ／ {1}曲", Maps.Count, BgmFiles.Count);
            if (diagnostics.Count > 0) { var previous = environmentMessage; environmentMessage = () => previous() + "\n" + string.Join("\n", diagnostics); }
        }
        catch (Exception e) { environmentMessage = () => TestPlayLocalization.Text("マップ・BGM一覧を読み込めません: ") + e.Message; }
    }
    public void SelectMap(int index)
    { if (IsBusy || index < 0 || index >= Maps.Count) return; MapIndex = index; selectionView?.RefreshSelection(); }
    public void SelectBgm(int index)
    { if (IsBusy || index < -1 || index >= BgmFiles.Count) return; Music.Stop(); BgmIndex = index; selectionView?.RefreshSelection(); }
    public async Task PreviewBgmAsync()
    {
        if (IsBusy) return;
        try { await Music.PlayAsync(SelectedBgm); }
        catch (Exception e) { environmentMessage = () => e.Message; selectionView?.RefreshSelection(); }
    }

    public void SelectPilot(bool opponent, int index)
    {
        if (IsBusy || index < -1 || index >= pilots.Count) return;
        if (opponent) secondPilotIndex = index; else firstPilotIndex = index;
        selectionView?.RefreshSelection();
    }

    public void CyclePilot(bool opponent, int direction)
    {
        int current = opponent ? secondPilotIndex : firstPilotIndex;
        int next = (current + 1 + direction + pilots.Count + 1) % (pilots.Count + 1) - 1;
        SelectPilot(opponent, next);
    }

    void RefreshPilots()
    {
        string first = FirstPilot?.DirectoryPath, second = SecondPilot?.DirectoryPath;
        pilots.Clear(); firstPilotIndex = secondPilotIndex = -1;
        var diagnostics = new List<string>();
        try
        {
            string root = !string.IsNullOrEmpty(dataRoot) ? dataRoot : DefaultDataRoot();
            PilotsRoot = Argument("--pilots-folder") ?? Path.Combine(root, "Pilot");
            pilots.AddRange(TestPlayPilotData.Scan(PilotsRoot, diagnostics));
            firstPilotIndex = pilots.FindIndex(p => string.Equals(p.DirectoryPath, first, StringComparison.OrdinalIgnoreCase));
            secondPilotIndex = pilots.FindIndex(p => string.Equals(p.DirectoryPath, second, StringComparison.OrdinalIgnoreCase));
            pilotMessage = () => pilots.Count == 0 ? TestPlayLocalization.Text("パイロットデータなし（パイロットなしで出撃できます）") : TestPlayLocalization.Text("{0}人のパイロットを読み込みました。", pilots.Count);
            if (diagnostics.Count > 0) { var previous = pilotMessage; pilotMessage = () => previous() + "\n" + string.Join("\n", diagnostics); }
        }
        catch (Exception e) { pilotMessage = () => TestPlayLocalization.Text("パイロット一覧を読み込めません: ") + e.Message; }
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
        Music.Stop();
        await LoadSceneAsync(BattleScene);
        token.ThrowIfCancellationRequested();
        BindBattle();
        ReleaseImages();
        if (SelectedMap != null)
        {
            SetPhase(TestPlayLocalization.Text("マップを読み込み中: ") + SelectedMap.Name, false);
            Stage = new GameObject("LoadedStage").AddComponent<TestPlayStageRuntime>();
            await Stage.LoadAsync(SelectedMap, gameCamera, token);
            Stage.PlacePlayers(playerSlot, opponentSlot);
            var environment = GameObject.Find("StandaloneGround");
            if (environment != null) environment.SetActive(false);
        }
        SetPhase(TestPlayLocalization.Text("機体データを読み込み中"), false);
        await host.StartWithCpuAsync(first, second, OpponentCpuSettings, token);
        token.ThrowIfCancellationRequested();
        if (host.Session != null)
        {
            float combatDistance = 0;
            foreach (var mech in host.Session.Mechs) combatDistance = Mathf.Max(combatDistance, mech.Controller.GetConfiguredLockDistance());
            FindFirstObjectByType<TestPlayStageAtmosphere>()?.SetCombatDistance(combatDistance);
        }
        FindFirstObjectByType<TestPlayPilotBattle>()?.Bind(host.Session, FirstPilot, SecondPilot);
        await Music.PlayAsync(SelectedBgm, token);
        UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
    }

    public Task RestartBattleAsync()
    {
        if (IsBusy || quitting || host == null || host.Status != "Running") return Task.CompletedTask;
        return ExecuteAsync(async token => {
            SetPhase(TestPlayLocalization.Text("再戦の準備中"), false);
            await host.RestartAsync(token);
            FindFirstObjectByType<TestPlayPilotBattle>()?.Bind(host.Session, FirstPilot, SecondPilot);
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
        SetPhase(TestPlayLocalization.Text("読込を中止しています"), false);
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
        selectionView?.RefreshSelection();
        cancellation = new CancellationTokenSource();
        loadingView?.Show(true);
        return operation = ExecuteCoreAsync(action, cancellation.Token);
    }

    async Task ExecuteCoreAsync(Func<CancellationToken, Task> action, CancellationToken token)
    {
        try { await action(token); message = () => ""; LastOperationSucceeded = true; }
        catch (Exception e)
        {
            message = () => e is OperationCanceledException ? TestPlayLocalization.Text("読込を中止しました。") : TestPlayLocalization.Text("読み込みに失敗しました: ") + e.Message;
            try
            {
                await StopBattleAsync();
                if (!quitting) { await LoadSceneAsync(SelectionScene); BindSelection(); IsReady = true; }
            }
            catch (Exception recovery) { var previous = message; message = () => previous() + TestPlayLocalization.Text("\n選択画面への復帰に失敗しました: ") + recovery.Message; Debug.LogException(recovery); }
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
        Music?.Stop();
        FindFirstObjectByType<TestPlayPilotBattle>()?.Unbind();
        SetPhase(TestPlayLocalization.Text("戦闘を終了しています"), false);
        if (host == null) host = FindFirstObjectByType<TestPlayBattleView>()?.sessionHost;
        if (host != null) await host.StopAsync();
        if (Stage != null) { Stage.Release(); Destroy(Stage.gameObject); Stage = null; }
        host = null; sessionHost = null; hudCanvas = null; playerSlot = null; opponentSlot = null; gameCamera = null;
    }

    async Task LoadSceneAsync(string path)
    {
        if (SceneManager.GetActiveScene().path == path) return;
        SetPhase(path == SelectionScene ? TestPlayLocalization.Text("機体選択画面を読み込み中") : TestPlayLocalization.Text("戦闘シーンを読み込み中"), true);
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
        // G2 presentation-only lighting; the scene owns this single light/component.
        var sun = GameObject.Find("StandaloneLight");
        if (sun == null || sun.GetComponent<Light>() == null)
            throw new InvalidOperationException("戦闘の主光源がありません。");
        var lighting = sun.GetComponent<TestPlayStageLighting>();
        if (lighting == null) lighting = sun.AddComponent<TestPlayStageLighting>();
        lighting.Apply(SelectedMap?.Name);
        var atmosphere = sun.GetComponent<TestPlayStageAtmosphere>();
        if (atmosphere == null) atmosphere = sun.AddComponent<TestPlayStageAtmosphere>();
        atmosphere.Apply(SelectedMap?.Name);
        if (gameCamera.GetComponent<TestPlayStagePostProcessing>() == null)
            gameCamera.gameObject.AddComponent<TestPlayStagePostProcessing>();
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
        mechDescriptions.Clear();
        firstIndex = 0;
        secondIndex = 0;

        if (string.IsNullOrWhiteSpace(mechsRoot))
        {
            message = () => TestPlayLocalization.Text("機体ルートフォルダを指定してください。");
            return;
        }

        string root = Path.GetFullPath(mechsRoot.Trim());
        mechsRoot = root;
        if (!Directory.Exists(root))
        {
            message = () => TestPlayLocalization.Text("機体ルートフォルダがありません: ") + root;
            return;
        }

        foreach (string directory in Directory.GetDirectories(root))
        {
            string scriptAni = FindFileIgnoreCase(directory, "Script.ani");
            if (scriptAni == null || !IsAn2Container(scriptAni))
                continue;
            string folderName = Path.GetFileName(directory);
            mechNames.Add(folderName);
            // 1行目は機体名、2～6行目は選択画面の説明。原本は読み取りのみ。
            mechDisplayNames[folderName] = ReadCharaSelect(directory, folderName, out string description);
            mechDescriptions[folderName] = description;
        }
        mechNames.Sort(StringComparer.OrdinalIgnoreCase);

        if (mechNames.Count == 0)
        {
            message = () => TestPlayLocalization.Text("AN2形式のScript.aniがある機体フォルダがみつかりません。");
            return;
        }

        firstIndex = IndexOfName(previousFirst);
        secondIndex = IndexOfName(previousSecond);
        message = () => TestPlayLocalization.Text("{0}機の機体フォルダを読み込みました。", mechNames.Count);
    }

    /// <summary>1行目の表示名と2～6行目の説明を読む。欠損時はフォルダ名と空の説明を返す。</summary>
    static string ReadCharaSelect(string directory, string folderName, out string description)
    {
        description = "";
        string sdtPath = FindFileIgnoreCase(directory, "charaselect.sdt");
        if (sdtPath == null) return folderName;
        try
        {
            // Use the shared decoder, including in a standalone Player without CP932 providers.
            string text = USEncoder.ToEncoding.ToUnicode(File.ReadAllBytes(sdtPath));
            using var reader = new StringReader(text);
            string line = reader.ReadLine();
            var lines = new List<string>(5);
            for (int i = 0; i < 5; i++)
            {
                string next = reader.ReadLine();
                if (next == null) break;
                lines.Add(next);
            }
            description = string.Join("\n", lines);
            if (!string.IsNullOrWhiteSpace(line)) return line.Trim();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Standalone Player] charaselect.sdtの読込に失敗しました: " + sdtPath + " / " + e.Message);
        }
        return folderName;
    }

    public string DescriptionAt(int index)
    {
        string folderName = SelectedName(index);
        return !string.IsNullOrEmpty(folderName) && mechDescriptions.TryGetValue(folderName, out string description)
            ? description : "";
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

    public static string DefaultDataRoot()
    {
        DirectoryInfo parent = Directory.GetParent(Application.dataPath);
        if (parent != null)
            return Path.Combine(parent.FullName, "Windom_Data");
        return Path.GetFullPath(DefaultRelativeDataRoot);
    }

    public static string DefaultMechsRoot()
    {
        return Path.Combine(DefaultDataRoot(), "Robo");
    }

    public static string ResolveDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return DefaultDataRoot();
        string full = Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(Path.Combine(full, "Robo")) || Directory.Exists(Path.Combine(full, "Texture")) ||
            string.Equals(Path.GetFileName(full), "Windom_Data", StringComparison.OrdinalIgnoreCase))
            return full;
        string parent = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(parent))
        {
            if (Directory.Exists(Path.Combine(parent, "Texture")) || Directory.Exists(Path.Combine(parent, "Sound")) ||
                string.Equals(Path.GetFileName(parent), "Windom_Data", StringComparison.OrdinalIgnoreCase))
                return parent;
        }
        return !string.IsNullOrEmpty(parent) ? parent : DefaultDataRoot();
    }

    void ConfigureRoots(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            dataRoot = DefaultDataRoot();
            mechsRoot = Path.Combine(dataRoot, "Robo");
            return;
        }
        string full = Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFileName(full), "Windom_Data", StringComparison.OrdinalIgnoreCase) ||
            (Directory.Exists(Path.Combine(full, "Robo")) && !string.Equals(Path.GetFileName(full), "Robo", StringComparison.OrdinalIgnoreCase)))
        {
            dataRoot = full;
            mechsRoot = Path.Combine(full, "Robo");
        }
        else
        {
            mechsRoot = full;
            dataRoot = ResolveDataRoot(full);
        }
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

    void OnApplicationPause(bool paused) { if (paused) TestPlayAudioSettings.Save(); }
    void OnApplicationQuit() { TestPlayAudioSettings.Save(); }
}
