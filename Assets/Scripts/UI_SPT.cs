using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class UI_SPT : MonoBehaviour
{
    public InputField SPTField;
    public RoboStructure robo;

    [Header("BURNER初期化")]
    [Tooltip("バーナーエフェクトのPrefab（nullの場合はデフォルトのParticleSystemを生成）")]
    public ParticleSystem burnerEffectPrefab;

    [Tooltip("SPT読み込み後に自動でSptParserを実行する対象のMechaAnimator")]
    public MechaAnimator mechaAnimator;

    // 最後にパースした SptRuntimeData（外部から参照可能）
    public SptRuntimeData LastSptData { get; private set; }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void loadSPTField()
    {
        TryLoadSptRuntimeData();
    }

    /// <summary>
    /// 現在の機体フォルダからScript.sptを読み、編集UIとランタイムへ同じ内容を反映する。
    /// テストモード開始時にも使用し、機体ロード順に依存した未読状態を防ぐ。
    /// </summary>
    public bool TryLoadSptRuntimeData()
    {
        string sptText = MechLoader.ReadSpt(robo);
        if (sptText == null)
        {
            ClearSptRuntimeData();
            return false;
        }
        if (SPTField != null)
            SPTField.text = sptText;

        // SPT読み込み後、自動でSptParserを実行してBURNERエフェクトを初期化
        ApplySptToRuntime(sptText);
        return LastSptData != null;
    }

    /// <summary>
    /// SPTテキストをパースし、MechaAnimatorのAniScriptRuntimeへSptRuntimeDataを渡す。
    /// UIから手動で呼ぶことも可能。
    /// </summary>
    public void ApplySptToRuntime(string sptText)
    {
        if (string.IsNullOrEmpty(sptText))
        {
            ClearSptRuntimeData();
            return;
        }

        var data = MechLoader.BindSpt(sptText, robo != null && robo.root != null ? robo.root.transform : null, burnerEffectPrefab);
        LastSptData = data;

        // 4. AniScriptRuntime に渡す
        //    mechaAnimator が Inspector で未設定の場合はシーン内から自動検索する
        AniScriptRuntime runtime = FindAniScriptRuntime();

        if (runtime != null)
        {
            runtime.sptData = data;
        }
        else
        {
            Debug.LogWarning("[UI_SPT] AniScriptRuntime が見つかりませんでした。" +
                             " MechaAnimator と同じ GameObject に AniScriptRuntime を追加してください。");
        }
    }

    void ClearSptRuntimeData()
    {
        if (SPTField != null)
            SPTField.text = "";

        LastSptData = null;
        AniScriptRuntime runtime = FindAniScriptRuntime();
        if (runtime != null)
            runtime.sptData = null;
    }

    AniScriptRuntime FindAniScriptRuntime()
    {
        AniScriptRuntime runtime = null;
        if (mechaAnimator != null)
            runtime = mechaAnimator.GetComponent<AniScriptRuntime>();

        if (runtime == null)
            // フォールバック: シーン内の AniScriptRuntime を自動検索
            runtime = FindObjectOfType<AniScriptRuntime>();

        return runtime;
    }

    public void saveSPT()
    {
        List<byte> list = new List<byte>();
        list.AddRange(USEncoder.ToEncoding.ToSJIS(SPTField.text));
        for (int j = 0; j < list.Count; j++)
        {
            if (list[j] == 0x0A && (j == 0 || list[j - 1] != 0x0D))
            {
                list.Insert(j, 0x0D);
                j++;
            }
        }
        byte[] tFile = robo.transcoder.Transcode(list.ToArray());
        File.WriteAllBytes(Path.Combine(robo.folder, "Script.spt"), tFile);
    }
}
