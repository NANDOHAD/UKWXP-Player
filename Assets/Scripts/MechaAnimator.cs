using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class MA_Runner
{
    public animation[] anim;
    public int scriptIndex = 0;
    public int scriptTime = 0;
    public int frameIndex = 0;
    public float frameTime = 0;
    public bool loop;
    public bool animeEnd = false;
    public float blendWeight;
    public int PrevScriptIndex { get; private set; } = -1;

    public MA_Runner(animation ani, bool _loop = false)
    {
        anim = new animation[] { ani };
        blendWeight = 0;
        loop = _loop;
    }

    public MA_Runner(animation[] animations, float _blendWeight, bool _loop = false)
    {
        int frameLength = animations[0].frames.Count;
        for (int i = 1; i < animations.Length; i++)
        {
            if (animations[i].frames.Count != frameLength)
            {
                frameLength = -1;
                break;
            }
        }

        if (frameLength != -1)
        {
            anim = animations;
            blendWeight = _blendWeight;
        }
        else
        {
            anim = new animation[] { animations[0] };
            blendWeight = 0;
        }

        loop = _loop;
    }

    public void Update()
    {
        if (!animeEnd)
        {
            PrevScriptIndex = scriptIndex;
            scriptTime++;

            if (anim == null || anim.Length == 0 || anim[0] == null || anim[0].scripts == null || anim[0].scripts.Count == 0)
            {
                animeEnd = true;
                return;
            }

            if (scriptIndex < 0) scriptIndex = 0;
            if (scriptIndex >= anim[0].scripts.Count) scriptIndex = anim[0].scripts.Count - 1;

            if (scriptTime >= anim[0].scripts[scriptIndex].unk)
            {

                scriptIndex++;
                scriptTime = 0;
                if (scriptIndex >= anim[0].scripts.Count)
                {
                    if (loop)
                    {
                        scriptIndex = 0;
                        frameTime = 0;
                        frameIndex = 0;
                    }
                    else
                        animeEnd = true;

                }
            }

            if (!animeEnd)
            {
                frameTime += anim[0].scripts[scriptIndex].time;
                if (frameTime >= 1)
                {
                    frameIndex++;
                    frameTime = frameTime - 1f;
                }
            }
        }
    }

    public hod2v1_Part getMT(int partID)
    {
        if (anim.Length > 1)
        {
            hod2v1_Part a = anim[0].interpolatePart(frameIndex, partID, frameTime);
            hod2v1_Part b = anim[1].interpolatePart(frameIndex, partID, frameTime);
            return MechaAnimator.InterpolateTransform(a, b, blendWeight);
        }

        return anim[0].interpolatePart(frameIndex, partID, frameTime);
    }
}

public class MechaAnimator : MonoBehaviour
{
    [Header("Play Data")]
    public bool play = false;
    public MA_Runner runner;
    public MA_Runner prevRunner;
    public float transition = 0;
    public float transitionSpeed = 0.1f;
    public MA_Runner UpperOverride;
    public RoboStructure structure;

    [Header("ANI Script")]
    public bool logAniScripts = false;
    public bool executeAniScripts = false;
    public bool logScriptLines = false;
    public bool logUnknownScriptSymbols = false;
    public bool dumpSymbolsToFile = false;
    public string dumpFileName = "ani_script_symbols_report.txt";
    scriptInterpreter interpreter;
    AniScriptRuntime runtime;
    int lastFiredScriptIndex = -999;
    bool initFired = false;
    // Start is called before the first frame update
    void Start()
    {
        interpreter = new scriptInterpreter();
        runtime = GetComponent<AniScriptRuntime>();
        if (runtime != null)
        {
            runtime.Register(interpreter);
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (play && runner != null && !runner.animeEnd)
        {

            // Fixed Timestep 0.02秒をANI 1 tickとして扱い、ゲーム互換の50Hzで進める。
            runner.Update();

            FireAniScriptsIfNeeded();

            //interpolate between current frame and next frame
            if (runner.frameIndex < runner.anim[0].frames.Count - 1)
            {
                for (int i = 1; i < structure.parts.Count; i++)
                {
                    GameObject go = structure.parts[i];
                    if (go != null)
                    {
                        hod2v1_Part mt = new hod2v1_Part();
                        if (prevRunner != null && transition < 1)
                        {
                            mt = InterpolateTransform(prevRunner.getMT(i), runner.getMT(i), transition);
                        }else{
                            mt = runner.getMT(i);
                        }
                        go.transform.localPosition = mt.position;
                        go.transform.localRotation = mt.rotation;
                        go.transform.localScale = mt.scale;
                        if (mt.scale.x + mt.scale.y + mt.scale.z < 0.05){
                            //Debug.Log("Bug: " + go.name);
                        }

                    }
                }
                if (prevRunner != null && transition < 1)
                    transition += transitionSpeed;
            }
            //else
            //	isUpdated = false;

        }
    }

    void FireAniScriptsIfNeeded()
    {
        if (runner == null || runner.anim == null || runner.anim.Length == 0 || runner.anim[0] == null)
            return;

        var a = runner.anim[0];
        if (!initFired)
        {
            initFired = true;
            if (!string.IsNullOrEmpty(a.squirrelInit))
                HandleScriptText(a.name, -1, a.squirrelInit);
        }

        if (a.scripts == null || a.scripts.Count == 0)
            return;

        // 初回、またはscriptIndexが進んだタイミングで発火
        if (lastFiredScriptIndex != runner.scriptIndex)
        {
            lastFiredScriptIndex = runner.scriptIndex;
            var idx = Mathf.Clamp(runner.scriptIndex, 0, a.scripts.Count - 1);
            var text = a.scripts[idx].squirrel;

            // BURNER ループ型: スクリプトブロック切り替わりのたびに要求セットをリセット
            if (executeAniScripts)
                runtime?.BurnerFrameReset();

            if (!string.IsNullOrEmpty(text))
                HandleScriptText(a.name, idx, text);

            // スクリプト実行後、要求されたバーナーのみ Play / 他を Stop
            if (executeAniScripts)
                runtime?.BurnerFrameApply();
        }
    }

    void HandleScriptText(string animName, int scriptIdx, string scriptText)
    {
        if (logAniScripts)
        {
            var header = scriptIdx < 0 ? "INIT" : $"IDX={scriptIdx}";
            Debug.Log($"[ANI_SCRIPT] {animName} {header}\n{scriptText}");
        }

        if (!dumpSymbolsToFile && !executeAniScripts)
            return;

        if (interpreter == null)
            interpreter = new scriptInterpreter();

        if (dumpSymbolsToFile)
        {
            try
            {
                interpreter.LogLines = false;
                interpreter.LogUnknownSymbols = logUnknownScriptSymbols;
                interpreter.runScript(scriptText, invokeCallbacks: false);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ANI_SCRIPT] Scan failed: {ex.Message}");
            }

            if (interpreter.HasNewSymbols)
            {
                try
                {
                    var path = Path.Combine(Application.persistentDataPath, dumpFileName);
                    File.WriteAllText(path, interpreter.BuildReport());
                    if (logAniScripts)
                        Debug.Log($"[ANI_SCRIPT] Symbols report updated: {path}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ANI_SCRIPT] Failed to write symbols report: {ex.Message}");
                }
            }
        }

        if (!executeAniScripts)
            return;

        try
        {
            interpreter.LogLines = logScriptLines;
            interpreter.LogUnknownSymbols = logUnknownScriptSymbols;
            interpreter.runScript(scriptText, invokeCallbacks: true);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ANI_SCRIPT] Execute failed: {ex.Message}");
        }
    }

    /// <summary>Discard poses from the previous mech before replacing its part table.</summary>
    public void ResetForStructureChange()
    {
        play = false;
        runner = null;
        prevRunner = null;
        UpperOverride = null;
        transition = 0;
        initFired = false;
        lastFiredScriptIndex = -999;
    }

    public void run(animation animID, bool _loop = false)
    {
        prevRunner = runner;
        runner = new MA_Runner(animID, _loop);
        play = true;
        transition = 0;
        initFired = false;
        lastFiredScriptIndex = -999;

    }

    public void run(animation[] animIDs, float blend, bool _loop = false)
    {
        prevRunner = runner;
        runner = new MA_Runner(animIDs, blend, _loop);
        play = true;
        transition = 0;
        initFired = false;
        lastFiredScriptIndex = -999;

    }
    public bool isEnded()
    {
        return runner.animeEnd;
    }

    public static hod2v1_Part InterpolateTransform(hod2v1_Part a, hod2v1_Part b, float t)
    {
        hod2v1_Part iMT = new hod2v1_Part();
        if (IsZeroQuaternion(a.rotation))
            a.rotation = Quaternion.identity;
        if (IsZeroQuaternion(b.rotation))
            b.rotation = Quaternion.identity;

        iMT.position = Vector3.Lerp(a.position, b.position, t);
        iMT.rotation = Quaternion.Lerp(a.rotation, b.rotation, t);
        iMT.scale = Vector3.Lerp(a.scale, b.scale, t);

        return iMT;
    }

    static bool IsZeroQuaternion(Quaternion q)
    {
        return q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f;
    }
}
