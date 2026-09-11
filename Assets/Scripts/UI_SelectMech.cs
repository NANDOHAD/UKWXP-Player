using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Threading.Tasks;

public enum LegacyAniLoadChoice
{
    KeepLegacy,
    ConvertToAn2
}

public sealed class LegacyAniAn2ConversionResult
{
    public bool success;
    public ani2 ani;
    public string destinationPath;
    public HodHierarchyRepairPlan hierarchyPlan;
    public string error;
}

public class UI_SelectMech : MonoBehaviour
{
    public Dropdown RoboDD;
    List<string> list = new List<string>();
    public GameObject MaskScreen;
    public Image selectImage;
    public Material selectMaterial;
    public bool enableTool = true;
    public UI_MsgBox msgBox;
    public RoboStructure robo;
    public RoboStructure prevRobo;
    public UI_ViewControl vc;
    public GameObject saveAni;
    public GameObject saveHod;
    public UI_Tabs editTabs;
    public GameObject modeSelect;
    public UI_EditParts editParts;
    public UI_EditAni editAni;
    public UI_SPT uiSPT;           // Script.spt の自動初期化に使用
    public string folder = "Windom_Data\\Robo";
    public GameObject prefPanel;
    public GameObject loadingUI; // ローディングUIのGameObject
    public Text lodingPerTxt;
    public GameObject maskLoad;
    
    // Start is called before the first frame update
    void Start()
    {
        robo.transcoder = new CypherTranscoder();
        RoboDD.ClearOptions();
        if(maskLoad != null && !maskLoad.activeSelf)
        {
            maskLoad.SetActive(true);
        }
        if (Directory.Exists(folder))
        {
            DirectoryInfo directory = new DirectoryInfo(folder);

            if (directory.GetDirectories().Length > 0)
            {
                List<string> options = new List<string>();
                foreach (DirectoryInfo di in directory.GetDirectories())
                {
                    options.Add(di.Name);
                    list.Add(di.Name);
                }
                RoboDD.AddOptions(options);
                selectedMech(0);

                selectImage.material = selectMaterial;
                enableTool = true;
            }
            else
            {
                msgBox.Show(UILocalization.Get(UILocalizationKeys.NoMechaData, "ディレクトリ内に機体データがみつかりません。"));
                enableTool = true;
            }
        }
        else
        {
            Directory.CreateDirectory(folder);
            msgBox.Show(UILocalization.Get(UILocalizationKeys.InitialDirectoryCreated, "初期ディレクトリ（Windom_Data\\Robo）を作成しました。"));
            enableTool = true;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void selectedMech(int value)
    {
        try
        {
            if (RoboDD == null)
            {
                return;
            }

            if (selectImage == null)
            {
                return;
            }

            if (robo == null || robo.transcoder == null)
            {
                return;
            }

            if (RoboDD.value < 0 || RoboDD.value >= list.Count)
            {
                return;
            }

            string filePath = Path.Combine(folder, list[RoboDD.value], "select.png");
            
            // ファイルの存在を確認
            if (File.Exists(filePath))
            {

                robo.transcoder.findCypher(filePath);

                Texture2D tex = Helper.LoadTextureEncrypted(filePath, ref robo.transcoder);
                if (tex == null)
                {
                    return;
                }


                Sprite st = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0));
                selectImage.sprite = st;
            }
            else
            {
            }
        }
        catch (Exception ex)
        {
        }
    }

    public async void loadFile(string name)
    {
        if (enableTool)
        {
            // ローディング表示を開始 (例: ローディングUIをアクティブにする)
            loadingUI.SetActive(true);
            try
            {
                await LoadDataAsync(name);
            }
            catch (Exception ex)
            {
                Debug.LogError($"機体データの読み込みに失敗しました: {ex.Message}");
                if (msgBox != null)
                    msgBox.Show(UILocalization.Get(UILocalizationKeys.MechaLoadFailed, "機体データの読み込みに失敗しました。\n{0}", ex.Message));
                return;
            }
            finally
            {
            // ローディング表示を終了 (例: ローディングUIを非アクティブにする)
            loadingUI.SetActive(false);
            MaskScreen.SetActive(false);
            }

        }
    }

    public static bool TryApplyAutomaticHierarchyRepairForLoad(
        ani2 ani,
        out HodHierarchyRepairPlan appliedHierarchyPlan,
        out string error)
    {
        return MechLoader.TryApplyHierarchy(ani, out appliedHierarchyPlan, out error);
    }

    public static bool TryDetectContainerFormat(
        string filename,
        out AniContainerFormat format,
        out string error)
    {
        format = AniContainerFormat.Unknown;
        error = "";
        try
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filename)))
            {
                if (reader.BaseStream.Length < 3)
                {
                    error = "ファイルが短すぎるためANI形式を判定できません。";
                    return false;
                }

                string signature = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(3));
                switch (signature)
                {
                    case "ANI":
                        format = AniContainerFormat.LegacyAni;
                        return true;
                    case "AN2":
                        format = AniContainerFormat.An2;
                        return true;
                    case "HOD":
                        format = AniContainerFormat.Hod;
                        return true;
                    default:
                        error = $"未対応のファイルシグネチャです: {signature}";
                        return false;
                }
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static string GetAvailableAn2ConversionPath(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
            throw new ArgumentException("変換元ANIファイルが指定されていません。", nameof(sourcePath));

        string directory = Path.GetDirectoryName(sourcePath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string candidate = Path.Combine(directory, baseName + ".an2");
        string sourceFullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(candidate) && !Directory.Exists(candidate)
            && !string.Equals(
                Path.GetFullPath(candidate),
                sourceFullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        int suffix = 1;
        while (true)
        {
            string suffixText = suffix == 1 ? ".converted" : ".converted" + suffix;
            candidate = Path.Combine(directory, baseName + suffixText + ".an2");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)
                && !string.Equals(
                    Path.GetFullPath(candidate),
                    sourceFullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
            suffix++;
        }
    }

    public static async Task<LegacyAniAn2ConversionResult> ConvertLegacyAniToAn2Async(
        ani2 source,
        string destinationPath,
        IProgress<int> progress = null)
    {
        LegacyAniAn2ConversionResult result = new LegacyAniAn2ConversionResult
        {
            destinationPath = destinationPath,
            error = ""
        };

        if (source == null || source.sourceFormat != AniContainerFormat.LegacyAni)
        {
            result.error = "旧ANIとして読み込まれたデータではありません。";
            return result;
        }
        if (string.IsNullOrEmpty(destinationPath))
        {
            result.error = "AN2の変換先が指定されていません。";
            return result;
        }

        string temporaryPath = null;
        bool ownsTemporary = false;
        try
        {
            string sourceFullPath = string.IsNullOrEmpty(source._filename)
                ? ""
                : Path.GetFullPath(source._filename);
            string destinationFullPath = Path.GetFullPath(destinationPath);
            if ((!string.IsNullOrEmpty(sourceFullPath)
                    && string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
                || File.Exists(destinationFullPath) || Directory.Exists(destinationFullPath))
            {
                result.error = "元の旧ANIまたは既存ファイルを上書きする変換先は使用できません。";
                return result;
            }

            ani2 export = source.CreateAn2ExportCopy(out HodHierarchyRepairPlan hierarchyPlan);
            result.hierarchyPlan = hierarchyPlan;

            temporaryPath = Path.Combine(Path.GetDirectoryName(destinationFullPath),
                ".windom-an2-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                using (var writer = new BinaryWriter(stream))
                    export.WriteAn2(writer);
            }

            ani2 converted = new ani2();
            bool loaded = await converted.load(temporaryPath, progress);
            if (!loaded || converted.sourceFormat != AniContainerFormat.An2
                || converted.structure == null || converted.animations == null)
            {
                result.error = "変換したAN2を再読み込みできませんでした。";
                return result;
            }
            export.ValidateAn2Export(converted);

            // Move without replacement is the final collision check, including
            // files created while the asynchronous reload was in progress.
            File.Move(temporaryPath, destinationFullPath);
            ownsTemporary = false;
            converted._filename = destinationPath;

            result.success = true;
            result.ani = converted;
            result.error = "";
            return result;
        }
        catch (Exception exception)
        {
            result.error = exception.Message;
            return result;
        }
        finally
        {
            TryDeleteGeneratedConversion(temporaryPath, ownsTemporary);
        }
    }

    static void TryDeleteGeneratedConversion(string path, bool generated)
    {
        if (!generated || string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UI_SelectMech] 失敗した変換ファイルを削除できませんでした: {exception.Message}");
        }
    }

    async Task<LegacyAniLoadChoice> AskLegacyAniConversionAsync(string destinationPath)
    {
        UI_InputBox dialog = editAni != null ? editAni.kakuninBox : null;
        if (dialog == null && editParts != null)
            dialog = editParts.kakuninBox;
        if (dialog == null)
            throw new InvalidOperationException("旧ANIの変換確認ダイアログが設定されていません。");

        TaskCompletionSource<LegacyAniLoadChoice> completion =
            new TaskCompletionSource<LegacyAniLoadChoice>();
        string message = UILocalization.Get(
            "ani.legacy_conversion.prompt",
            "読み込もうとしているファイルは旧ANI形式です。AN2へ変換して読み込みますか？\n\nOK: 元の旧ANIを変更せず「{0}」へAN2変換コピーを作成して読み込みます。原作で使用するのは先頭200枠までです。追加枠は本エディタ用として末尾に保持します。問題のあるコメント文字は実行を妨げない形に置き換えます。旧ANI固有の未解析末尾データはAN2コピーには含まれません。\nキャンセル: 旧ANIのまま読み込みます。",
            Path.GetFileName(destinationPath));

        bool restoreLoading = loadingUI != null && loadingUI.activeSelf;
        if (restoreLoading)
            loadingUI.SetActive(false);
        try
        {
            dialog.openNoTextBoxDialog(
                message,
                ignored => completion.TrySetResult(LegacyAniLoadChoice.ConvertToAn2),
                ignored => completion.TrySetResult(LegacyAniLoadChoice.KeepLegacy));
            return await completion.Task;
        }
        finally
        {
            if (restoreLoading && loadingUI != null)
                loadingUI.SetActive(true);
        }
    }

    private async Task LoadDataAsync(string name)
    {
        var progress = new Progress<int>(value =>
        {
            if (value == 100)
                lodingPerTxt.text = UILocalization.Get(UILocalizationKeys.LoadingComplete, "読み込み完了");
            else
                lodingPerTxt.text = UILocalization.Get(UILocalizationKeys.LoadingProgress, "読み込み中...{0}%", value);
        });

        string selectedFolder = Path.Combine(folder, list[RoboDD.value]);
        string sourcePath = Path.Combine(selectedFolder, name);
        LegacyAniLoadChoice legacyChoice = LegacyAniLoadChoice.KeepLegacy;
        string conversionPath = null;
        AniContainerFormat detectedFormat;
        string detectionError;
        if (TryDetectContainerFormat(sourcePath, out detectedFormat, out detectionError)
            && detectedFormat == AniContainerFormat.LegacyAni)
        {
            conversionPath = GetAvailableAn2ConversionPath(sourcePath);
            legacyChoice = await AskLegacyAniConversionAsync(conversionPath);
        }

        ani2 ani = await MechLoader.LoadAsync(sourcePath, progress);

        if (legacyChoice == LegacyAniLoadChoice.ConvertToAn2)
        {
            LegacyAniAn2ConversionResult conversion =
                await ConvertLegacyAniToAn2Async(ani, conversionPath, progress);
            if (!conversion.success)
            {
                if (loadingUI != null)
                    loadingUI.SetActive(false);
                msgBox?.Show(UILocalization.Get(
                    "ani.legacy_conversion.failed",
                    "旧ANIをAN2へ変換できなかったため、読み込みを中止しました。元の旧ANIは変更されていません。\n{0}",
                    conversion.error));
                return;
            }

            ani = conversion.ani;
            name = Path.GetFileName(conversion.destinationPath);
            Debug.Log($"[UI_SelectMech] 旧ANIをAN2へ変換しました: {conversion.destinationPath}");
        }

        HodHierarchyRepairPlan appliedHierarchyPlan;
        string hierarchyRepairError;
        if (!TryApplyAutomaticHierarchyRepairForLoad(
            ani, out appliedHierarchyPlan, out hierarchyRepairError))
        {
            Debug.LogWarning($"[UI_SelectMech] HOD階層の自動修復に失敗しました: {hierarchyRepairError}");
            msgBox?.Show(UILocalization.Get(
                "hod.ui.repair_apply_failed",
                "HOD階層を安全に修復できなかったため、読み込みを中止しました。\n{0}",
                hierarchyRepairError));
            return;
        }

        if (appliedHierarchyPlan != null
            && appliedHierarchyPlan.Kind != HodHierarchyRepairKind.None)
        {
            Debug.LogWarning(
                $"[UI_SelectMech] HOD階層をメモリ上で自動修復しました: {appliedHierarchyPlan.Summary}");
        }

        string structureEditingBlockReason = "";
        if (ani.sourceFormat == AniContainerFormat.LegacyAni)
        {
            string legacyHierarchyDetails;
            if (!HodHierarchyRepair.TryValidateWithoutRepair(
                ani, out legacyHierarchyDetails))
            {
                structureEditingBlockReason = UILocalization.Get(
                    "hod.ui.legacy_unrepaired_warning",
                    "旧ANIのHODパーツ階層は自動修復せず読み込みました。パーツの追加・削除時に、安全な正本を確定できる場合だけ正規化確認を表示します。\n{0}",
                    legacyHierarchyDetails);
            }
        }

        robo.folder = selectedFolder;
        robo.SetStructureEditingBlockReason(structureEditingBlockReason);
        if (editAni != null && editAni.ma != null)
            editAni.ma.ResetForStructureChange();
        robo.buildStructure(ani.structure);
        if (ani.sourceFormat == AniContainerFormat.LegacyAni
            || ani.sourceFormat == AniContainerFormat.An2)
        {
            robo.ani = ani;
            robo.filename = name;
            if (prevRobo != null)
            {
                prevRobo.buildStructureFromLoaded(robo, ani.structure);
                foreach (GameObject prt in prevRobo.parts)
                    prt.layer = 7;
            }

            saveAni.SetActive(true);
            saveHod.SetActive(false);
            editAni.populateAnimationList();
            if (editTabs != null)
                editTabs.setTabActive(0, true);
            if (modeSelect != null)
                modeSelect.SetActive(true);

            // .ani 読み込み完了後、Script.spt を自動で復号・パースして
            // BURNER エフェクトを初期化する
            if (uiSPT != null)
            {
                try
                {
                    uiSPT.loadSPTField();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[UI_SelectMech] Script.spt の自動読み込みに失敗しました: {ex.Message}");
                }
            }
        }
        else
        {
            robo.filename = ani._filename;
            saveAni.SetActive(false);
            saveHod.SetActive(true);
            if (editTabs != null)
                editTabs.setTabActive(0, false);
            if (modeSelect != null)
                modeSelect.SetActive(false);
        }

        editParts.PopulatePartsList();
        this.gameObject.SetActive(false);
        vc.Menu.SetActive(true);
        vc.EditMode(true);
        prefPanel.SetActive(false);

        if (legacyChoice == LegacyAniLoadChoice.ConvertToAn2)
        {
            if (loadingUI != null)
                loadingUI.SetActive(false);
            msgBox?.Show(UILocalization.Get(
                "ani.legacy_conversion.completed",
                "旧ANIをAN2へ変換し、変換後ファイルを読み込みました。\n保存先: {0}\n元の旧ANIは変更されていません。\n原作で使用する枠数: {1}（最大200）\n追加保持枠数: {2}（原作では使用しません）",
                conversionPath, ani.originalGameAnimationCount, ani.editorOnlyAnimationCount));
        }
    }


    public void setFolder(string value)
    {
        if (Directory.Exists(value))
        {
            folder = value;
            RoboDD.ClearOptions();
            list.Clear();
            DirectoryInfo directory = new DirectoryInfo(folder);

            if (directory.GetDirectories().Length > 0)
            {
                List<string> options = new List<string>();
                foreach (DirectoryInfo di in directory.GetDirectories())
                {
                    options.Add(di.Name);
                    list.Add(di.Name);
                }
                RoboDD.AddOptions(options);
                selectedMech(0);

                selectImage.material = selectMaterial;
            }
            else
            {
                msgBox.Show(UILocalization.Get(UILocalizationKeys.NoMechaDataFound, "ディレクトリ内に機体データがみつかりませんでした。"));
                enableTool = true;
            }
        }
        
    }

    public void ClearTextureCache()
    {
        Helper.TextureCache.Clear();
        //Debug.Log("Texture cache cleared.");
    }
}
