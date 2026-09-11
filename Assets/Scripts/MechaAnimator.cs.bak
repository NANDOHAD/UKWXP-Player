using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
            scriptTime++;

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
    float fps = 1 / 30;
    float time = 0;
    public MA_Runner runner;
    public MA_Runner prevRunner;
    public float transition = 0;
    public float transitionSpeed = 0.1f;
    public MA_Runner UpperOverride;
    public RoboStructure structure;
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (play && !runner.animeEnd)
        {

            //if (runner.scriptTime == 0)
            //    RunModules(); //runscript

            time += Time.deltaTime;
            if (time >= fps)
            {
                //isUpdated = true;
                time = 0;
                runner.Update();

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
            }
            //else
            //	isUpdated = false;

        }
    }

    public void run(animation animID, bool _loop = false)
    {
        prevRunner = runner;
        runner = new MA_Runner(animID, _loop);
        play = true;
        transition = 0;

    }

    public void run(animation[] animIDs, float blend, bool _loop = false)
    {
        prevRunner = runner;
        runner = new MA_Runner(animIDs, blend, _loop);
        play = true;
        transition = 0;

    }
    public bool isEnded()
    {
        return runner.animeEnd;
    }

    public static hod2v1_Part InterpolateTransform(hod2v1_Part a, hod2v1_Part b, float t)
    {
        hod2v1_Part iMT = new hod2v1_Part();
        if (a.rotation.x + a.rotation.y + a.rotation.z + a.rotation.z == 0)
            a.rotation = Quaternion.identity;
        if (b.rotation.x + b.rotation.y + b.rotation.z + b.rotation.z == 0)
            b.rotation = Quaternion.identity;

        iMT.position = Vector3.Lerp(a.position, b.position, t);
        iMT.rotation = Quaternion.Lerp(a.rotation, b.rotation, t);
        iMT.scale = Vector3.Lerp(a.scale, b.scale, t);

        return iMT;
    }
}
