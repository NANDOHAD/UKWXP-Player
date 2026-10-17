using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TestPlayOriginalSoundSetup
{
    const string SoundFolder = "Windom_Data/Sound";
    const string PropulsionStartFileName = "burner.wav";
    const string PropulsionLoopFileName = "burner_f15.wav";

    struct OriginalSndSpec
    {
        public readonly int id;
        public readonly string fileName;

        public OriginalSndSpec(int id, string fileName)
        {
            this.id = id;
            this.fileName = fileName;
        }
    }

    // Original-confirmed: WindomXP_orig_decompiled.c, startup resource registration at lines 23781..24027.
    // Keep sparse IDs 97..99; 21..96 are intentionally not inferred.
    static readonly OriginalSndSpec[] OriginalSnd =
    {
        new OriginalSndSpec(0, "shot.wav"),
        new OriginalSndSpec(1, "asioto.wav"),
        new OriginalSndSpec(2, "tyakuti.wav"),
        new OriginalSndSpec(3, "explode_m.wav"),
        new OriginalSndSpec(4, "explode_l.wav"),
        new OriginalSndSpec(5, "burner_f15.wav"),
        new OriginalSndSpec(6, "shot2.wav"),
        new OriginalSndSpec(7, "BeamHit.wav"),
        new OriginalSndSpec(8, "Fannel.wav"),
        new OriginalSndSpec(9, "sword.wav"),
        new OriginalSndSpec(10, "sword2.wav"),
        new OriginalSndSpec(11, "BeamHit2.wav"),
        new OriginalSndSpec(12, "guard.wav"),
        new OriginalSndSpec(13, "Bullet.wav"),
        new OriginalSndSpec(14, "burst01.wav"),
        new OriginalSndSpec(15, "BulletHit.wav"),
        new OriginalSndSpec(16, "BeamReflect.wav"),
        new OriginalSndSpec(17, "Henkei.wav"),
        new OriginalSndSpec(18, "oc.wav"),
        new OriginalSndSpec(19, "magic01.wav"),
        new OriginalSndSpec(20, "magic02.wav"),
        new OriginalSndSpec(97, "PI2.wav"),
        new OriginalSndSpec(98, "PI.wav"),
        new OriginalSndSpec(99, "cursor27.wav")
    };

    [MenuItem("Tools/WindomXP/Test Play/Rebuild Original SND_SE Mappings")]
    public static void RebuildCurrentSceneMappings()
    {
        TestPlayPresentationRuntime presentation = UnityEngine.Object.FindFirstObjectByType<TestPlayPresentationRuntime>(FindObjectsInactive.Include);
        if (presentation == null)
            throw new InvalidOperationException("TestPlayPresentationRuntime was not found in the current scene.");

        int mapped = ApplyMappings(presentation);
        if (presentation.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(presentation.gameObject.scene);
            EditorSceneManager.SaveScene(presentation.gameObject.scene);
        }

        Debug.Log("[TestPlay][Audio] Rebuilt " + mapped +
            " original-confirmed Snd mappings and the propulsion Unity adapter from " + SoundFolder + ".");
    }

    public static int ApplyMappings(TestPlayPresentationRuntime presentation)
    {
        if (presentation == null) throw new ArgumentNullException(nameof(presentation));
        presentation.LoadExternalResources();
        return presentation.sounds.Count;
    }

    public static bool TryGetFileName(int id, out string fileName)
    {
        for (int i = 0; i < OriginalSnd.Length; i++)
        {
            if (OriginalSnd[i].id == id)
            {
                fileName = OriginalSnd[i].fileName;
                return true;
            }
        }

        fileName = null;
        return false;
    }

    public static bool TryGetPropulsionAdapterFileName(out string fileName)
    {
        fileName = PropulsionStartFileName;
        return true;
    }

    public static bool TryGetPropulsionAdapterFileNames(out string startFileName, out string loopFileName)
    {
        startFileName = PropulsionStartFileName;
        loopFileName = PropulsionLoopFileName;
        return true;
    }

}
