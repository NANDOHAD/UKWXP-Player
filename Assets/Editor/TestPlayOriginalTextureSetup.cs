using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TestPlayOriginalTextureSetup
{
    const string SourceFolder = "Assets/IMG_TX";
    const string OutputFolder = "Assets/Generated/TestPlay/OriginalTextures";
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
        if (presentation == null)
            throw new ArgumentNullException(nameof(presentation));

        EnsureOutputFolder();
        Dictionary<string, string> sourcePaths = FindSourceFiles();
        List<KeyValuePair<OriginalTextureSpec, string>> generated = new List<KeyValuePair<OriginalTextureSpec, string>>();
        missing = new List<string>();

        for (int i = 0; i < OriginalTextures.Length; i++)
        {
            OriginalTextureSpec spec = OriginalTextures[i];
            if (!sourcePaths.TryGetValue(spec.fileName, out string sourceAssetPath))
            {
                missing.Add(spec.loadSequence + ":" + spec.fileName);
                continue;
            }

            string outputAssetPath = OutputFolder + "/" + spec.loadSequence.ToString("D2") + "_" + Path.GetFileName(spec.fileName);
            WriteDecryptedImage(sourceAssetPath, outputAssetPath);
            generated.Add(new KeyValuePair<OriginalTextureSpec, string>(spec, outputAssetPath));
        }

        if (!sourcePaths.ContainsKey("blueLight.bmp") && sourcePaths.TryGetValue("beamHit4.png", out string guide39Source))
        {
            OriginalTextureSpec guide39 = new OriginalTextureSpec(-1, "beamHit4.png", 39);
            string outputAssetPath = OutputFolder + "/Guide39_beamHit4.png";
            WriteDecryptedImage(guide39Source, outputAssetPath);
            generated.Add(new KeyValuePair<OriginalTextureSpec, string>(guide39, outputAssetPath));
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        for (int i = 0; i < generated.Count; i++)
        {
            AssetDatabase.ImportAsset(
                generated[i].Value,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }
        List<TestPlayTextureBinding> bindings = new List<TestPlayTextureBinding>();
        for (int i = 0; i < generated.Count; i++)
        {
            OriginalTextureSpec spec = generated[i].Key;
            string assetPath = generated[i].Value;
            ConfigureGeneratedTexture(assetPath, spec);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
                throw new InvalidDataException("Decrypted texture could not be imported: " + assetPath);
            bindings.Add(new TestPlayTextureBinding
            {
                loadSequence = spec.loadSequence,
                scriptTextureId = spec.scriptTextureId,
                originalFileName = spec.fileName,
                texture = texture
            });
        }

        Shader shader = Shader.Find(EffectShaderName);
        if (shader == null)
            throw new InvalidOperationException("Original effect shader was not found: " + EffectShaderName);

        Undo.RecordObject(presentation, "Rebuild original IMG_TX mappings");
        presentation.originalTextures = bindings;
        presentation.originalEffectShader = shader;
        EditorUtility.SetDirty(presentation);
        return generated.Count;
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

    static Dictionary<string, string> FindSourceFiles()
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string absoluteSource = ToAbsolutePath(SourceFolder);
        if (!Directory.Exists(absoluteSource))
            return result;

        string[] files = Directory.GetFiles(absoluteSource, "*", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string extension = Path.GetExtension(files[i]);
            if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                continue;
            string fileName = Path.GetFileName(files[i]);
            if (result.ContainsKey(fileName))
                throw new InvalidOperationException("Duplicate original texture filename: " + fileName);
            result.Add(fileName, SourceFolder + "/" + fileName);
        }
        return result;
    }

    static void WriteDecryptedImage(string sourceAssetPath, string outputAssetPath)
    {
        string sourceAbsolutePath = ToAbsolutePath(sourceAssetPath);
        byte[] sourceBytes = File.ReadAllBytes(sourceAbsolutePath);
        string extension = Path.GetExtension(sourceAssetPath);
        bool expectsPng = extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
        bool expectsBmp = extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        byte[] imageBytes;
        if ((expectsPng && HasPngSignature(sourceBytes)) || (expectsBmp && HasBmpSignature(sourceBytes)))
        {
            imageBytes = sourceBytes;
        }
        else
        {
            CypherTranscoder transcoder = new CypherTranscoder();
            if (expectsPng && !transcoder.findCypher(sourceAbsolutePath))
                throw new InvalidDataException("Could not determine original texture cipher: " + sourceAssetPath);
            imageBytes = TranscodeAllImageBytes(sourceBytes, transcoder.cypher);
        }

        if ((expectsPng && !HasPngSignature(imageBytes)) || (expectsBmp && !HasBmpSignature(imageBytes)))
            throw new InvalidDataException("Decrypted data has an invalid image signature: " + sourceAssetPath);
        if (expectsPng)
            imageBytes = NormalizePngForUnityImporter(imageBytes, sourceAssetPath);
        File.WriteAllBytes(ToAbsolutePath(outputAssetPath), imageBytes);
    }

    static byte[] NormalizePngForUnityImporter(byte[] imageBytes, string sourceAssetPath)
    {
        // The DX9-era files decode through ImageConversion.LoadImage, but some contain
        // legacy PNG details rejected by Unity 6's asset importer. Re-encoding only the
        // generated copy keeps the original encrypted source untouched.
        Texture2D decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(decoded, imageBytes, false))
                throw new InvalidDataException("Decrypted PNG data could not be decoded: " + sourceAssetPath);
            byte[] normalized = ImageConversion.EncodeToPNG(decoded);
            if (normalized == null || normalized.Length == 0)
                throw new InvalidDataException("Decrypted PNG data could not be normalized: " + sourceAssetPath);
            return normalized;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(decoded);
        }
    }

    static byte[] TranscodeAllImageBytes(byte[] sourceBytes, uint cypher)
    {
        // CypherTranscoder intentionally leaves incomplete 4-byte blocks untouched for
        // Script.spt compatibility. Image files need the repeating key applied through
        // the final byte so their PNG/BMP trailer remains valid for Unity's importer.
        byte[] imageBytes = (byte[])sourceBytes.Clone();
        byte[] cypherBytes = BitConverter.GetBytes(cypher);
        for (int i = 0; i < imageBytes.Length; i++)
            imageBytes[i] ^= cypherBytes[i & 3];
        return imageBytes;
    }

    static bool HasPngSignature(byte[] bytes)
    {
        return bytes != null && bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47 &&
            bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a && bytes[7] == 0x0a;
    }

    static bool HasBmpSignature(byte[] bytes)
    {
        return bytes != null && bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4d;
    }

    static void ConfigureGeneratedTexture(string assetPath, OriginalTextureSpec spec)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.isReadable = false;
        importer.userData = "WindomXP original effect loadSequence=" + spec.loadSequence +
            " scriptTextureId=" + spec.scriptTextureId + " source=" + spec.fileName;
        importer.SaveAndReimport();
    }

    static void EnsureOutputFolder()
    {
        Directory.CreateDirectory(ToAbsolutePath(OutputFolder));
    }

    static string ToAbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
