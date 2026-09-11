using System;
using System.Collections.Generic;
using UnityEngine;
// SptParser / SptRuntimeData / BurnerSetInfo は SptParser.cs で定義

/// <summary>
/// .ani 内スクリプト（Scr_命令相当）を Unity 側へ橋渡しする実行コンテキスト。
/// .ani フォーマットは変更せず、命令→状態変更/イベントとして再現する。
/// </summary>
public class AniScriptRuntime : MonoBehaviour
{
    [Header("Debug")]
    public bool logUnhandled = true;

    // “オリジナル側の命令が触るであろう値”を、まずは状態として保持する（移植の中心）
    [Header("State (best-effort names)")]
    public Vector3 move;              // Scr_Move の入力値候補（方向/速度等）
    public Vector3 force;             // Scr_Force の入力値候補
    public int attackPow;             // Scr_AttackPow
    public float attackForce;         // Scr_AttackForce
    public float attackForceY;        // Scr_AttackForceY
    public int attackDownF;           // Scr_AttackDownF
    public bool shieldGuard;          // Scr_ShildGuard
    public int laserReflectValue;     // Scr_LaserReflect: orig +0xB68 signed-char read
    public float shotTurnAng;         // Scr_ShotTurnAng
    public float turnMoveAng;         // Scr_TurnMoveAng
    public float addExGaugeValue;     // Scr_AddExGauge (orig: float, stored at +0xC8)
    public int addEnergyValue;        // Scr_AddEnergy (orig: int/short, stored at +0xD0)
    public bool gvEnable;             // Scr_GvEnable (orig: byte, stored at +0xE8)
    public int camEffectId;           // Scr_CamEffect (orig: byte, stored at +0xE9)
    public int goScriptIndex;         // Scr_GoScriptIndex
    public int goPoseIndex;           // Scr_GoPoseIndex
    public int changeAnime;           // Scr_ChangeAnime（アニメID/インデックス想定）

    [Header("Lock Targets (from Scr_LockArmTarget/Scr_LockBodyTarget)")]
    public bool[] lockArmEnable = new bool[2];
    public Vector2[] lockArmAngles = new Vector2[2];
    public bool[] lockBodyEnable = new bool[2];
    public Vector2[] lockBodyAngles = new Vector2[2];

    // 汎用の変数ストア（オリジナル側の “変数テーブル” の代替受け皿）
    public readonly Dictionary<string, scriptVar> vars = new Dictionary<string, scriptVar>(StringComparer.Ordinal);
    public readonly Dictionary<int, int> extParams = new Dictionary<int, int>(); // Scr_SetExtParam 相当（推定）

    [Header("BURNER / SPT")]
    [Tooltip("SptParser.Parse() + BindTransforms() + BuildBurnerEffects() を済ませた SptRuntimeData をセットする")]
    public SptRuntimeData sptData;

    // 現フレームで BURNER 命令から要求されたバーナーID セット
    readonly HashSet<int> _burnerRequestedIds = new HashSet<int>();

    // イベント（キャラ制御側が購読して挙動再現に使う）
    public event Action<int> OnChangeAnime;
    public event Action<int> OnGoScriptIndex;
    public event Action<int> OnGoPoseIndex;
    public event Action<string, scriptVar[]> OnSnd;
    public event Action<string, scriptVar[]> OnVoice;
    public event Action<string, scriptVar[]> OnCamEffect;
    public event Action<string, scriptVar[]> OnRunProc;
    public event Action<string, scriptVar[]> OnRunSubScript;
    public event Action<string, scriptVar[]> OnCatchChara;
    public event Action<int, int> OnSetExtParam;

    static int AsInt(scriptVar v)
    {
        if (v.type == scriptVarType.NUM) return Mathf.RoundToInt(v.num);
        if (v.type == scriptVarType.STR && int.TryParse(v.str, out var i)) return i;
        return 0;
    }
    static float AsFloat(scriptVar v)
    {
        if (v.type == scriptVarType.NUM) return v.num;
        if (v.type == scriptVarType.STR && float.TryParse(v.str, out var f)) return f;
        return 0f;
    }
    static bool AsBool(scriptVar v)
    {
        if (v.type == scriptVarType.NUM) return Mathf.Abs(v.num) > 0.0001f;
        if (v.type == scriptVarType.STR && bool.TryParse(v.str, out var b)) return b;
        return false;
    }

    // スクリプト命令名は実ファイル側の表記ゆれがあり得るのでエイリアスを広めに張る
    static IEnumerable<string> Aliases(string canonical)
    {
        yield return canonical;
        if (canonical.StartsWith("Scr_", StringComparison.Ordinal))
            yield return canonical.Substring(4);
        else
            yield return "Scr_" + canonical;
    }

    public void Register(scriptInterpreter interpreter)
    {
        // 変数の set/get を汎用で受ける
        interpreter.registerVariable("VAR", v => { }, () => new scriptVar { type = scriptVarType.EMPTY });
        // ↑ “VAR” 自体は使わないが、registerVariable の仕組みを温存

        // 任意変数 setter/getter を追加（未知変数も受けるため、直接辞書に入れるAPIを使う）
        // scriptInterpreter は固定名しか登録できないので、ここは「想定される名前」を後で増やす。

        // 命令の登録（オリジナルの Scr_ 群を受け皿にする）
        RegisterCmd(interpreter, "Move", Cmd_Move);
        RegisterCmd(interpreter, "Force", Cmd_Force);
        RegisterCmd(interpreter, "AttackPow", Cmd_AttackPow);
        RegisterCmd(interpreter, "AttackForce", Cmd_AttackForce);
        RegisterCmd(interpreter, "AttackForceY", Cmd_AttackForceY);
        RegisterCmd(interpreter, "AttackDownF", Cmd_AttackDownF);
        RegisterCmd(interpreter, "LockArmTarget", Cmd_LockArmTarget);
        RegisterCmd(interpreter, "LockBodyTarget", Cmd_LockBodyTarget);
        RegisterCmd(interpreter, "ShildGuard", Cmd_ShildGuard);
        RegisterCmd(interpreter, "ShotTurnAng", Cmd_ShotTurnAng);
        RegisterCmd(interpreter, "TurnMoveAng", Cmd_TurnMoveAng);
        RegisterCmd(interpreter, "Sub_LRKey", Cmd_Sub_LRKey);
        RegisterCmd(interpreter, "AddExGauge", Cmd_AddExGauge);
        RegisterCmd(interpreter, "AddEnergy", Cmd_AddEnergy);
        RegisterCmd(interpreter, "Snd", Cmd_Snd);
        RegisterCmd(interpreter, "Voice", Cmd_Voice);
        RegisterCmd(interpreter, "RunProc", Cmd_RunProc);
        RegisterCmd(interpreter, "SetExtParam", Cmd_SetExtParam);
        RegisterCmd(interpreter, "vF_Multi", Cmd_vF_Multi);
        RegisterCmd(interpreter, "GvEnable", Cmd_GvEnable);
        RegisterCmd(interpreter, "CamEffect", Cmd_CamEffect);
        RegisterCmd(interpreter, "GoScriptIndex", Cmd_GoScriptIndex);
        RegisterCmd(interpreter, "GoPoseIndex", Cmd_GoPoseIndex);
        RegisterCmd(interpreter, "BunerOut", Cmd_BunerOut);
        RegisterCmd(interpreter, "ChangeAnime", Cmd_ChangeAnime);
        RegisterCmd(interpreter, "Rnd", Cmd_Rnd);
        RegisterCmd(interpreter, "LocalRnd", Cmd_LocalRnd);
        RegisterCmd(interpreter, "RunSubScript", Cmd_RunSubScript);
        RegisterCmd(interpreter, "CatchChara", Cmd_CatchChara);
        RegisterCmd(interpreter, "ExecScriptEveryTime", Cmd_ExecScriptEveryTime);

        // 新規発見・名称不一致のエイリアスとスタブ
        RegisterCmd(interpreter, "BURNER", Cmd_BURNER);
        RegisterCmd(interpreter, "WeaponAttack", Cmd_WeaponAttack);
        RegisterCmd(interpreter, "WeaponAttack2", Cmd_WeaponAttack2);
        RegisterCmd(interpreter, "RunProc2", Cmd_RunProc2);
        RegisterCmd(interpreter, "ATTACK", Cmd_ATTACK);
        RegisterCmd(interpreter, "AttackFlag", Cmd_AttackFlag);
        RegisterCmd(interpreter, "LaserReflect", Cmd_LaserReflect);
        RegisterCmd(interpreter, "SwordCancel", Cmd_SwordCancel);
        RegisterCmd(interpreter, "BoostDashMode", Cmd_BoostDashMode);
        RegisterCmd(interpreter, "AttackDelay", Cmd_AttackDelay);
        RegisterCmd(interpreter, "AnimeLoop", Cmd_AnimeLoop);
        RegisterCmd(interpreter, "SwordEnable", Cmd_SwordEnable);
        RegisterCmd(interpreter, "ChangeWeapon", Cmd_ChangeWeapon);
        RegisterCmd(interpreter, "MoveLock", Cmd_MoveLock);

        // 名称不一致のLock系エイリアス（とりあえず同じハンドラへ流す）
        RegisterCmd(interpreter, "LockBodyDownTarget", Cmd_LockBodyTarget);
        RegisterCmd(interpreter, "LockBodyUpTarget", Cmd_LockBodyTarget);
        RegisterCmd(interpreter, "LockArm1Target", Cmd_LockArmTarget);
        RegisterCmd(interpreter, "LockArm2Target", Cmd_LockArmTarget);

        // IF/ELSE/ENDIF は scriptInterpreter 側の簡易実装もあるが、括弧無しでも来るので登録だけしておく
        RegisterCmd(interpreter, "ELSE", _ => { /* handled by interpretIFStatement */ });
        RegisterCmd(interpreter, "ENDIF", _ => { /* handled by interpretIFStatement */ });
    }

    void RegisterCmd(scriptInterpreter interpreter, string canonical, scriptFunc func)
    {
        foreach (var name in Aliases(canonical))
        {
            if (!interpreter.registeredFunc.ContainsKey(name))
                interpreter.registerFunction(name, func);
        }
    }

    // --- 命令ハンドラ（意味は WindomXP_orig.exe.c を起点に“推定→検証→確定”していく） ---
    // ここでは「受け皿」と「状態の置き場所」を作り、後で精密化する。

    void Cmd_Move(scriptVar[] v)
    {
        // orig: FUN_004b6420 が3つのScrFLOATを取り、内部の3成分(+0xad0/+0xad4/+0xad8)を更新する挙動
        // ここでは Unity 側は Vector3 に集約して保持する
        if (v.Length >= 3) move = new Vector3(AsFloat(v[0]), AsFloat(v[1]), AsFloat(v[2]));
        else if (v.Length >= 1) move = new Vector3(AsFloat(v[0]), move.y, move.z);
    }

    void Cmd_Force(scriptVar[] v)
    {
        // orig: FUN_004b65c0 が3つのScrFLOATを取り、内部の3成分(+0xae8/+0xaec/+0xaf0)を更新する挙動
        if (v.Length >= 3) force = new Vector3(AsFloat(v[0]), AsFloat(v[1]), AsFloat(v[2]));
        else if (v.Length >= 1) force = new Vector3(AsFloat(v[0]), force.y, force.z);
    }

    void Cmd_AttackPow(scriptVar[] v)      { if (v.Length >= 1) attackPow = AsInt(v[0]); }
    void Cmd_AttackForce(scriptVar[] v)    { if (v.Length >= 1) attackForce = AsFloat(v[0]); }
    void Cmd_AttackForceY(scriptVar[] v)   { if (v.Length >= 1) attackForceY = AsFloat(v[0]); }
    void Cmd_AttackDownF(scriptVar[] v)    { if (v.Length >= 1) attackDownF = AsInt(v[0]); }
    void Cmd_ShildGuard(scriptVar[] v)     { shieldGuard = (v.Length >= 1) ? AsBool(v[0]) : true; }
    void Cmd_ShotTurnAng(scriptVar[] v)    { if (v.Length >= 1) shotTurnAng = AsFloat(v[0]); }
    void Cmd_TurnMoveAng(scriptVar[] v)    { if (v.Length >= 1) turnMoveAng = AsFloat(v[0]); }
    void Cmd_AddExGauge(scriptVar[] v)     { if (v.Length >= 1) addExGaugeValue = AsFloat(v[0]); }
    void Cmd_AddEnergy(scriptVar[] v)      { if (v.Length >= 1) addEnergyValue = AsInt(v[0]); }
    void Cmd_GvEnable(scriptVar[] v)       { gvEnable = (v.Length >= 1) ? AsBool(v[0]) : true; }

    void Cmd_GoScriptIndex(scriptVar[] v)
    {
        if (v.Length >= 1) goScriptIndex = AsInt(v[0]);
        OnGoScriptIndex?.Invoke(goScriptIndex);
    }

    void Cmd_GoPoseIndex(scriptVar[] v)
    {
        if (v.Length >= 1) goPoseIndex = AsInt(v[0]);
        OnGoPoseIndex?.Invoke(goPoseIndex);
    }

    void Cmd_ChangeAnime(scriptVar[] v)
    {
        if (v.Length >= 1) changeAnime = AsInt(v[0]);
        OnChangeAnime?.Invoke(changeAnime);
    }

    void Cmd_Snd(scriptVar[] v)        { OnSnd?.Invoke("Snd", v); }
    void Cmd_Voice(scriptVar[] v)      { OnVoice?.Invoke("Voice", v); }
    void Cmd_CamEffect(scriptVar[] v)
    {
        if (v.Length >= 1) camEffectId = AsInt(v[0]);
        OnCamEffect?.Invoke("CamEffect", v);
    }
    void Cmd_RunProc(scriptVar[] v)    { OnRunProc?.Invoke("RunProc", v); }
    void Cmd_RunSubScript(scriptVar[] v) { OnRunSubScript?.Invoke("RunSubScript", v); }
    void Cmd_CatchChara(scriptVar[] v) { OnCatchChara?.Invoke("CatchChara", v); }

    void Cmd_LockArmTarget(scriptVar[] v)
    {
        // WindomXP_orig.exe.c より:
        // - enable が byte(0/1?)、float が2つ書き込まれている形跡
        // - スロット(0/1)が存在する（配列インデックス）形跡
        // 推定: LockArmTarget(slot, enable, ang1, ang2)
        var slot = (v.Length >= 1) ? Mathf.Clamp(AsInt(v[0]), 0, lockArmEnable.Length - 1) : 0;
        if (v.Length >= 2) lockArmEnable[slot] = AsBool(v[1]);
        if (v.Length >= 4) lockArmAngles[slot] = new Vector2(AsFloat(v[2]), AsFloat(v[3]));
    }

    void Cmd_LockBodyTarget(scriptVar[] v)
    {
        // 推定: LockBodyTarget(slot, enable, ang1, ang2)
        var slot = (v.Length >= 1) ? Mathf.Clamp(AsInt(v[0]), 0, lockBodyEnable.Length - 1) : 0;
        if (v.Length >= 2) lockBodyEnable[slot] = AsBool(v[1]);
        if (v.Length >= 4) lockBodyAngles[slot] = new Vector2(AsFloat(v[2]), AsFloat(v[3]));
    }
    void Cmd_Sub_LRKey(scriptVar[] v) { /* TODO: input gating */ }
    void Cmd_SetExtParam(scriptVar[] v)
    {
        // orig: FUN_004b66e0 が ScrINT を取り、(byte index) で配列へ格納する形跡
        // 推定: SetExtParam(index, value)
        if (v.Length < 2)
            return;
        var index = AsInt(v[0]);
        var value = AsInt(v[1]);
        extParams[index] = value;
        OnSetExtParam?.Invoke(index, value);
    }
    void Cmd_vF_Multi(scriptVar[] v) { /* TODO: multiplier */ }
    void Cmd_BunerOut(scriptVar[] v) { /* TODO: booster/afterburner */ }

    void Cmd_Rnd(scriptVar[] v) { /* TODO: random (global) */ }
    void Cmd_LocalRnd(scriptVar[] v) { /* TODO: random (local) */ }
    void Cmd_ExecScriptEveryTime(scriptVar[] v) { /* TODO: execute every frame/tick */ }

    // =====================================================================
    // BURNER 命令（ループ型エフェクト）
    // =====================================================================
    // 使い方（MechaAnimator 側）:
    //   1. スクリプトブロック発火前に BurnerFrameReset() を呼ぶ
    //   2. runScript() を実行 → Cmd_BURNER が要求IDを蓄積する
    //   3. BurnerFrameApply() を呼んで要求された PS を Play、要求外を Stop する

    /// <summary>フレーム開始時に呼ぶ。前フレームのリクエストをクリアする。</summary>
    public void BurnerFrameReset()
    {
        _burnerRequestedIds.Clear();
    }

    /// <summary>
    /// フレーム終了時（runScript() 後）に呼ぶ。
    /// 要求されたバーナーを Play、要求されなかったバーナーを Stop する。
    /// </summary>
    public void BurnerFrameApply()
    {
        if (sptData == null) return;
        foreach (var kv in sptData.BurnerSets)
        {
            var info = kv.Value;
            if (info.Ps == null) continue;

            bool requested = _burnerRequestedIds.Contains(info.Id);
            if (requested)
            {
                if (!info.Ps.isPlaying) info.Ps.Play(withChildren: true);
            }
            else
            {
                if (!info.Ps.isStopped) info.Ps.Stop(withChildren: true,
                    stopBehavior: ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    void Cmd_BURNER(scriptVar[] v)
    {
        // BURNER(id) : このフレームで点火要求があったことをマーク
        if (v.Length >= 1)
            _burnerRequestedIds.Add(AsInt(v[0]));
    }

    void Cmd_WeaponAttack(scriptVar[] v) { /* TODO: 射撃武器の発射命令 */ }
    void Cmd_WeaponAttack2(scriptVar[] v) { /* TODO: サブ武器の発射命令 */ }
    void Cmd_RunProc2(scriptVar[] v) { /* TODO: 詳細パラメータ付きの弾・エフェクト生成 */ }
    
    void Cmd_ATTACK(scriptVar[] v)
    {
        // 例: ATTACK(威力, 硬直, 持続, タイプ)
        // TODO: 攻撃判定の一括設定
    }

    void Cmd_AttackFlag(scriptVar[] v)
    {
        var flag = v.Length >= 1 ? AsInt(v[0]) : 0;
        // TODO: 攻撃判定のON/OFF切り替え
    }

    void Cmd_LaserReflect(scriptVar[] v)
    {
        laserReflectValue = TestPlayCombatCore.NormalizeOriginalLaserReflectValue(
            v.Length >= 1 ? AsInt(v[0]) : 0);
    }
    void Cmd_SwordCancel(scriptVar[] v) { /* TODO: ヒット時派生先アクションIDの登録 */ }
    void Cmd_BoostDashMode(scriptVar[] v) { /* TODO: ブーストダッシュ状態への移行 */ }
    void Cmd_AttackDelay(scriptVar[] v) { /* TODO: 攻撃発生のディレイ処理 */ }
    void Cmd_AnimeLoop(scriptVar[] v) { /* TODO: アニメーションのループ制御 */ }
    void Cmd_SwordEnable(scriptVar[] v) { /* TODO: 剣モデルの表示・非表示 */ }
    void Cmd_ChangeWeapon(scriptVar[] v) { /* TODO: 武器の持ち替え */ }
    void Cmd_MoveLock(scriptVar[] v) { /* TODO: 移動入力のロック */ }
}

