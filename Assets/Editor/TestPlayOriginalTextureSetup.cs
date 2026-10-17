using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TestPlayOriginalTextureSetup
{
    const string SourceFolder = "Windom_Data/Texture";
    const string EffectShaderName = "WindomXP/TestPlayOriginalEffect";

    struct OriginalTextureSpec
    {
        public readonly int loadSequence;
        public readonly int scriptTextureId;
        public readonly string fileName;

        public OriginalTextureSpec(int loadSequence, string fileName, int scriptTextureId = -1)
        {
            this.loadSequence = loadSequence;
            this.scriptTextureId = scriptTextureId;
            this.fileName = fileName;
        }
    }

    // Load order is original-confirmed at WindomXP_orig_decompiled.c lines 21410..21453.
    // scriptTextureId is the separate GUIDE4.txt mapping and remains guide-derived where it differs.
    static readonly OriginalTextureSpec[] OriginalTextures =
    {
        new OriginalTextureSpec(0, "Beam.bmp", 0),
        new OriginalTextureSpec(1, "BlueBall.png", 1),
        new OriginalTextureSpec(2, "explode2.png", 2),
        new OriginalTextureSpec(3, "explode1.png"),
        new OriginalTextureSpec(4, "Beam2.bmp", 4),
        new OriginalTextureSpec(5, "smokeline.png", 5),
        new OriginalTextureSpec(6, "laser2.bmp", 7),
        new OriginalTextureSpec(7, "burner.png", 8),
        new OriginalTextureSpec(8, "burner2.png", 9),
        new OriginalTextureSpec(9, "beamHit3.png", 10),
        new OriginalTextureSpec(10, "GSmoke.png", 6),
        new OriginalTextureSpec(11, "sabel.png", 12),
        new OriginalTextureSpec(12, "sabel_line.png", 13),
        new OriginalTextureSpec(13, "beam2.png", 14),
        new OriginalTextureSpec(14, "Beam3.bmp"),
        new OriginalTextureSpec(15, "BlueRedBeam.png", 16),
        new OriginalTextureSpec(16, "smoke.png", 17),
        new OriginalTextureSpec(17, "smoke2.png", 18),
        new OriginalTextureSpec(18, "smokeline_b.png", 19),
        new OriginalTextureSpec(19, "WindRing.png", 20),
        new OriginalTextureSpec(20, "beam3.png", 21),
        new OriginalTextureSpec(21, "beam3Hit.png", 22),
        new OriginalTextureSpec(22, "summon.png", 23),
        new OriginalTextureSpec(23, "BlueRedBeam2.png", 24),
        new OriginalTextureSpec(24, "Beam4.png", 25),
        new OriginalTextureSpec(25, "Beam5.png", 26),
        new OriginalTextureSpec(26, "Beam6.png", 27),
        new OriginalTextureSpec(27, "Beam7.png", 28),
        new OriginalTextureSpec(28, "Beam8.png", 29),
        new OriginalTextureSpec(29, "sabel_line2.png", 30),
        new OriginalTextureSpec(30, "fire.png", 31),
        new OriginalTextureSpec(31, "light.png"),
        new OriginalTextureSpec(32, "magic.png", 33),
        new OriginalTextureSpec(33, "beamHit2.png", 34),
        new OriginalTextureSpec(34, "Wing.png", 35),
        new OriginalTextureSpec(35, "Wind.png", 36),
        new OriginalTextureSpec(36, "Bomb.png", 37),
        new OriginalTextureSpec(37, "m_circle4.png", 38),
        new OriginalTextureSpec(38, "blueLight.bmp", 39),
        new OriginalTextureSpec(39, "hinoko.png", 40),
        new OriginalTextureSpec(40, "fireAnime2.png", 41),
        new OriginalTextureSpec(41, "sabel_line3.png", 42),
        new OriginalTextureSpec(42, "burner3.png", 43),
        new OriginalTextureSpec(43, "burner4.png", 44)
    };

    [MenuItem("Tools/WindomXP/Test Play/Rebuild Original IMG_TX Mappings")]
    public static void RebuildCurrentSceneMappings()
    {
        TestPlayPresentationRuntime presentation = UnityEngine.Object.FindFirstObjectByType<TestPlayPresentationRuntime>(FindObjectsInactive.Include);
        if (presentation == null)
            throw new InvalidOperationException("TestPlayPresentationRuntime was not found in the current scene.");

        List<string> missing;
        int mapped = ApplyMappings(presentation, out missing);
        if (presentation.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(presentation.gameObject.scene);
            EditorSceneManager.SaveScene(presentation.gameObject.scene);
        }

        string message = "[TestPlay][Effect] Rebuilt " + mapped + "/" + OriginalTextures.Length +
            " original-confirmed texture mappings from " + SourceFolder + ".";
        if (missing.Count > 0)
            Debug.Log(message + " Missing source files: " + string.Join(", ", missing));
        else
            Debug.Log(message);
    }

    public static int ApplyMappings(TestPlayPresentationRuntime presentation, out List<string> missing)
    {
        if (presentation == null) throw new ArgumentNullException(nameof(presentation));
        presentation.LoadExternalResources();
        missing = new List<string>();
        foreach (var spec in TestPlayExternalResourceCatalog.Textures)
            if (!presentation.HasOriginalNamedTexture(spec.originalFileName)) missing.Add(spec.originalFileName);
        return presentation.MappedTextureBindingCount;
    }

    public static bool TryGetLoadSequenceFileName(int loadSequence, out string fileName)
    {
        for (int i = 0; i < OriginalTextures.Length; i++)
        {
            if (OriginalTextures[i].loadSequence == loadSequence)
            {
                fileName = OriginalTextures[i].fileName;
                return true;
            }
        }
        fileName = null;
        return false;
    }

    public static bool TryGetScriptTextureFileName(int scriptTextureId, out string fileName)
    {
        for (int i = 0; i < OriginalTextures.Length; i++)
        {
            if (OriginalTextures[i].scriptTextureId == scriptTextureId)
            {
                fileName = OriginalTextures[i].fileName;
                return true;
            }
        }
        fileName = null;
        return false;
    }

}
