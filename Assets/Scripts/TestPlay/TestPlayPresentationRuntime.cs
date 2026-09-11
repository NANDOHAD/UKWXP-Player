using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TestPlayAudioBinding
{
    public string key;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
}

[Serializable]
public class TestPlayEffectBinding
{
    public string key;
    public GameObject prefab;
}

[Serializable]
public class TestPlayTextureBinding
{
    public int loadSequence = -1;
    public int scriptTextureId = -1;
    public string originalFileName;
    public Texture2D texture;
}

public class TestPlayPresentationRuntime : MonoBehaviour
{
    [Header("References")]
    public TestPlayController controller;
    public AudioSource soundSource;
    public AudioSource voiceSource;
    public AudioSource propulsionStartSource;
    public AudioSource propulsionSource;

    [Header("Original resource mappings")]
    public List<TestPlayAudioBinding> sounds = new List<TestPlayAudioBinding>();
    public List<TestPlayAudioBinding> voices = new List<TestPlayAudioBinding>();
    public List<TestPlayEffectBinding> effects = new List<TestPlayEffectBinding>();
    public List<TestPlayTextureBinding> originalTextures = new List<TestPlayTextureBinding>();
    public Shader originalEffectShader;
    public bool logMissingAudio = true;
    public bool logMissingTextures = true;
    public bool preloadMappedAudio = true;
    public bool useSpatialAudio = true;
    public float audioMaxDistance = 40f;

    [Header("Propulsion start + loop (Unity adapter)")]
    public AudioClip propulsionStartClip;
    [Range(0f, 1f)] public float propulsionStartVolume = 1f;
    public AudioClip propulsionLoopClip;
    [Range(0f, 1f)] public float propulsionLoopVolume = 1f;
    [Min(0f)] public float propulsionFadeSeconds = 0.05f;

    public bool PropulsionActiveRequested { get; private set; }
    public bool PropulsionLoopRequested { get; private set; }
    public int PropulsionActivationCount { get; private set; }
    public int SuppressedSoundPlaybackCount { get; private set; }

    readonly HashSet<string> missingAudioKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> missingTextureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void Awake()
    {
        EnsureAudioSources();
        if (preloadMappedAudio)
        {
            PreloadBindings(sounds);
            PreloadBindings(voices);
            if (propulsionStartClip != null && propulsionStartClip.loadState == AudioDataLoadState.Unloaded)
                propulsionStartClip.LoadAudioData();
            if (propulsionLoopClip != null && propulsionLoopClip.loadState == AudioDataLoadState.Unloaded)
                propulsionLoopClip.LoadAudioData();
        }
        if (controller == null)
            controller = GetComponent<TestPlayController>();
    }

    void OnEnable()
    {
        Bind(controller != null ? controller : GetComponent<TestPlayController>());
    }

    void OnDisable()
    {
        Unbind();
        StopPresentation();
    }

    void Update()
    {
        UpdatePropulsionFade();
    }

    public void Bind(TestPlayController source)
    {
        if (controller == source && controller != null)
        {
            controller.PresentationEventRaised -= HandlePresentationEvent;
            controller.PresentationEventRaised += HandlePresentationEvent;
            return;
        }

        Unbind();
        controller = source;
        if (controller != null)
            controller.PresentationEventRaised += HandlePresentationEvent;
    }

    public void StopPresentation()
    {
        if (soundSource != null)
            soundSource.Stop();
        if (voiceSource != null)
            voiceSource.Stop();
        PropulsionActiveRequested = false;
        PropulsionLoopRequested = false;
        if (propulsionStartSource != null)
            propulsionStartSource.Stop();
        if (propulsionSource != null)
        {
            propulsionSource.Stop();
            propulsionSource.volume = 0f;
        }
    }

    public void AttachEmitters(Transform sourceRoot)
    {
        EnsureAudioSources();
        if (sourceRoot == null)
            return;

        AttachEmitter(soundSource, sourceRoot);
        AttachEmitter(voiceSource, sourceRoot);
        AttachEmitter(propulsionStartSource, sourceRoot);
        AttachEmitter(propulsionSource, sourceRoot);
    }

    /// <summary>
    /// Drives a one-shot burner.wav start and a separate burner_f15.wav loop from
    /// aggregate ANI BURNER output. Only the loop fades when output disappears.
    /// This remains an explicit Unity presentation adapter; the fixed Snd(5)
    /// mapping to burner_f15.wav is preserved independently.
    /// </summary>
    public void SetPropulsionLoopActive(bool active)
    {
        EnsureAudioSources();
        bool newlyActivated = active && !PropulsionActiveRequested;
        PropulsionActiveRequested = active;
        PropulsionLoopRequested = active && propulsionLoopClip != null;

        if (active && propulsionStartClip == null)
        {
            const string missingKey = "Propulsion:burner.wav";
            if (logMissingAudio && missingAudioKeys.Add(missingKey))
                Debug.LogWarning("[TestPlay][Audio] Missing original resource mapping: " + missingKey);
        }
        if (active && propulsionLoopClip == null)
        {
            const string missingKey = "Propulsion:burner_f15.wav";
            if (logMissingAudio && missingAudioKeys.Add(missingKey))
                Debug.LogWarning("[TestPlay][Audio] Missing original resource mapping: " + missingKey);
        }

        if (propulsionSource == null)
            return;

        if (propulsionSource.clip != propulsionLoopClip)
        {
            propulsionSource.Stop();
            propulsionSource.clip = propulsionLoopClip;
        }
        propulsionSource.loop = true;

        if (!active)
        {
            if (!Application.isPlaying || propulsionFadeSeconds <= 0f)
            {
                propulsionSource.Stop();
                propulsionSource.volume = 0f;
            }
            return;
        }

        if (newlyActivated)
        {
            PropulsionActivationCount++;
            ConfigureAndStartPropulsionOneShot();
            if (Application.isPlaying && propulsionSource.isPlaying)
                propulsionSource.Stop();
        }

        if (!PropulsionLoopRequested)
            return;

        if (!Application.isPlaying)
        {
            propulsionSource.volume = Mathf.Clamp01(propulsionLoopVolume);
            return;
        }

        if (!propulsionSource.isPlaying)
        {
            propulsionSource.volume = Mathf.Clamp01(propulsionLoopVolume);
            propulsionSource.Play();
        }
    }

    void ConfigureAndStartPropulsionOneShot()
    {
        if (propulsionStartSource == null || propulsionStartClip == null)
            return;

        propulsionStartSource.Stop();
        propulsionStartSource.clip = propulsionStartClip;
        propulsionStartSource.loop = false;
        propulsionStartSource.volume = Mathf.Clamp01(propulsionStartVolume);
        if (Application.isPlaying)
            propulsionStartSource.Play();
    }

    public GameObject CreateMappedEffect(string key, Vector3 position, Quaternion rotation)
    {
        if (effects == null)
            return null;

        for (int i = 0; i < effects.Count; i++)
        {
            TestPlayEffectBinding binding = effects[i];
            if (binding != null && binding.prefab != null && string.Equals(binding.key, key, StringComparison.OrdinalIgnoreCase))
                return Instantiate(binding.prefab, position, rotation);
        }
        return null;
    }

    public GameObject CreateOriginalTextureEffect(int textureId, Vector3 position, Quaternion rotation, Vector2 size, float life, Color tint, bool billboard = true)
    {
        TestPlayTextureBinding binding = FindOriginalTexture(originalTextures, textureId);
        return CreateOriginalTextureEffect(binding, textureId.ToString(), position, rotation, size, life, tint, billboard);
    }

    public GameObject CreateOriginalType1TrailEffect(
        int textureId,
        Vector3 position,
        float width,
        out TestPlayType1TrailEffect trailEffect)
    {
        trailEffect = null;
        TestPlayTextureBinding binding = FindOriginalTexture(originalTextures, textureId);
        if (binding == null || binding.texture == null || originalEffectShader == null)
        {
            string missingKey = textureId.ToString();
            if (logMissingTextures && missingTextureKeys.Add(missingKey))
                Debug.LogWarning("[TestPlay][Effect] Missing original texture mapping: " + missingKey);
            return null;
        }

        GameObject effectObject = new GameObject(
            "TestPlayType1Trail_" + textureId + "_" + binding.texture.name);
        effectObject.transform.position = position;
        trailEffect = effectObject.AddComponent<TestPlayType1TrailEffect>();
        trailEffect.Initialize(binding.texture, originalEffectShader, width, position);
        return effectObject;
    }

    public GameObject CreateOriginalNamedTextureEffect(string fileName, Vector3 position, Quaternion rotation, Vector2 size, float life, Color tint, bool billboard = true)
    {
        TestPlayTextureBinding binding = FindOriginalTextureByName(originalTextures, fileName);
        return CreateOriginalTextureEffect(binding, fileName, position, rotation, size, life, tint, billboard);
    }

    public GameObject CreateOriginalSwordBeamEffect(
        TestPlaySwordBeamParameters parameters,
        Transform anchor,
        bool reverseDirection,
        out TestPlaySwordBeamEffect swordBeam,
        out bool primaryLayerCreated,
        out bool lineLayerCreated)
    {
        swordBeam = null;
        primaryLayerCreated = false;
        lineLayerCreated = false;
        if (anchor == null)
            return null;

        GameObject root = new GameObject("TestPlayOriginalSwordBeam");
        Transform[] primaryPlanes = CreateSwordBeamPlanes(
            root.transform,
            parameters.primaryTextureId,
            "Primary");
        Transform[] linePlanes = CreateSwordBeamPlanes(
            root.transform,
            parameters.lineTextureId,
            "Line");
        primaryLayerCreated = primaryPlanes.Length > 0;
        lineLayerCreated = linePlanes.Length > 0;
        if (!primaryLayerCreated && !lineLayerCreated)
        {
            DestroyRuntimeObject(root);
            return null;
        }

        swordBeam = root.AddComponent<TestPlaySwordBeamEffect>();
        swordBeam.Initialize(
            anchor,
            primaryPlanes,
            linePlanes,
            parameters.primaryTextureId,
            parameters.lineTextureId,
            reverseDirection,
            parameters.initialLength,
            parameters.targetLength);
        return root;
    }

    public GameObject CreateOriginalThunderEffect(
        TestPlayThunderEffectParameters parameters,
        Vector3 position,
        Quaternion rotation,
        Vector3 movementDirection,
        out TestPlayThunderEffect thunderEffect)
    {
        thunderEffect = null;
        GameObject root = new GameObject("TestPlayOriginalThunderEffect");
        Transform[] planes = CreateThunderPlanes(root.transform, parameters.textureId);
        if (planes.Length == 0)
        {
            DestroyRuntimeObject(root);
            return null;
        }

        thunderEffect = root.AddComponent<TestPlayThunderEffect>();
        thunderEffect.Initialize(parameters, planes, position, rotation, movementDirection);
        return root;
    }

    GameObject CreateOriginalTextureEffect(TestPlayTextureBinding binding, string missingKey, Vector3 position, Quaternion rotation, Vector2 size, float life, Color tint, bool billboard)
    {
        if (binding == null || binding.texture == null || originalEffectShader == null)
        {
            if (logMissingTextures && missingTextureKeys.Add(missingKey ?? ""))
                Debug.LogWarning("[TestPlay][Effect] Missing original texture mapping: " + missingKey);
            return null;
        }

        GameObject effectObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Collider collider = effectObject.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
        }

        effectObject.name = "TestPlayOriginalEffect_" + missingKey + "_" + binding.texture.name;
        effectObject.transform.SetPositionAndRotation(position, rotation);
        TestPlayOriginalEffect effect = effectObject.AddComponent<TestPlayOriginalEffect>();
        effect.Initialize(binding.texture, originalEffectShader, size, life, tint, billboard);
        return effectObject;
    }

    Transform[] CreateSwordBeamPlanes(Transform parent, int textureId, string layerName)
    {
        if (parent == null || textureId < 0)
            return new Transform[0];

        GameObject first = CreateOriginalTextureEffect(
            textureId,
            Vector3.zero,
            Quaternion.identity,
            Vector2.one,
            0f,
            Color.white,
            false);
        if (first == null)
            return new Transform[0];

        GameObject second = CreateOriginalTextureEffect(
            textureId,
            Vector3.zero,
            Quaternion.identity,
            Vector2.one,
            0f,
            Color.white,
            false);
        first.name = "TestPlaySwordBeam_" + layerName + "_0";
        first.transform.SetParent(parent, false);
        if (second == null)
            return new[] { first.transform };

        second.name = "TestPlaySwordBeam_" + layerName + "_1";
        second.transform.SetParent(parent, false);
        return new[] { first.transform, second.transform };
    }

    Transform[] CreateThunderPlanes(Transform parent, int textureId)
    {
        if (parent == null || textureId < 0)
            return new Transform[0];

        GameObject first = CreateOriginalTextureEffect(
            textureId,
            Vector3.zero,
            Quaternion.identity,
            Vector2.one,
            0f,
            Color.white,
            false);
        if (first == null)
            return new Transform[0];

        GameObject second = CreateOriginalTextureEffect(
            textureId,
            Vector3.zero,
            Quaternion.identity,
            Vector2.one,
            0f,
            Color.white,
            false);
        first.name = "TestPlayThunderEffect_0";
        first.transform.SetParent(parent, false);
        if (second == null)
            return new[] { first.transform };

        second.name = "TestPlayThunderEffect_1";
        second.transform.SetParent(parent, false);
        return new[] { first.transform, second.transform };
    }

    static void DestroyRuntimeObject(GameObject target)
    {
        if (target == null)
            return;
        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    public bool HasOriginalTexture(int textureId)
    {
        TestPlayTextureBinding binding = FindOriginalTexture(originalTextures, textureId);
        return binding != null && binding.texture != null;
    }

    public Texture2D GetOriginalTexture(int textureId)
    {
        TestPlayTextureBinding binding = FindOriginalTexture(originalTextures, textureId);
        return binding != null ? binding.texture : null;
    }

    public bool HasMappedEffect(string key)
    {
        if (effects == null)
            return false;

        for (int i = 0; i < effects.Count; i++)
        {
            TestPlayEffectBinding binding = effects[i];
            if (binding != null && binding.prefab != null &&
                string.Equals(binding.key, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public TestPlayPresentationAdapterKind ResolveAudioAdapter(TestPlayPresentationEventType type, string key)
    {
        List<TestPlayAudioBinding> bindings = type == TestPlayPresentationEventType.Voice
            ? voices
            : sounds;
        TestPlayAudioBinding binding = FindBinding(bindings, key);
        return binding != null && binding.clip != null
            ? TestPlayPresentationAdapterKind.AudioClip
            : TestPlayPresentationAdapterKind.None;
    }

    void Unbind()
    {
        if (controller != null)
            controller.PresentationEventRaised -= HandlePresentationEvent;
    }

    void HandlePresentationEvent(TestPlayPresentationEvent presentationEvent)
    {
        switch (presentationEvent.type)
        {
            case TestPlayPresentationEventType.Sound:
                if (controller != null && controller.ShouldSuppressPresentationSound(presentationEvent))
                {
                    SuppressedSoundPlaybackCount++;
                    break;
                }
                PlayBinding(sounds, presentationEvent.symbol, soundSource, "Snd");
                break;
            case TestPlayPresentationEventType.Voice:
                PlayBinding(voices, presentationEvent.symbol, voiceSource, "Voice");
                break;
        }
    }

    void PlayBinding(List<TestPlayAudioBinding> bindings, string key, AudioSource source, string command)
    {
        if (source == null)
            return;

        TestPlayAudioBinding binding = FindBinding(bindings, key);
        if (binding == null || binding.clip == null)
        {
            string missingKey = command + ":" + (key ?? "");
            if (logMissingAudio && missingAudioKeys.Add(missingKey))
                Debug.LogWarning("[TestPlay][Audio] Missing original resource mapping: " + missingKey);
            return;
        }

        source.PlayOneShot(binding.clip, Mathf.Clamp01(binding.volume));
    }

    public static TestPlayAudioBinding FindBinding(List<TestPlayAudioBinding> bindings, string key)
    {
        if (bindings == null)
            return null;

        for (int i = 0; i < bindings.Count; i++)
        {
            TestPlayAudioBinding binding = bindings[i];
            if (binding != null && string.Equals(binding.key, key, StringComparison.OrdinalIgnoreCase))
                return binding;
        }
        return null;
    }

    public static TestPlayTextureBinding FindOriginalTexture(List<TestPlayTextureBinding> bindings, int id)
    {
        if (bindings == null)
            return null;

        for (int i = 0; i < bindings.Count; i++)
        {
            TestPlayTextureBinding binding = bindings[i];
            if (binding != null && binding.scriptTextureId == id)
                return binding;
        }
        return null;
    }

    public static TestPlayTextureBinding FindOriginalTextureByName(List<TestPlayTextureBinding> bindings, string fileName)
    {
        if (bindings == null)
            return null;

        for (int i = 0; i < bindings.Count; i++)
        {
            TestPlayTextureBinding binding = bindings[i];
            if (binding != null && string.Equals(binding.originalFileName, fileName, StringComparison.OrdinalIgnoreCase))
                return binding;
        }
        return null;
    }

    static void PreloadBindings(List<TestPlayAudioBinding> bindings)
    {
        if (bindings == null)
            return;

        for (int i = 0; i < bindings.Count; i++)
        {
            AudioClip clip = bindings[i] != null ? bindings[i].clip : null;
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }
    }

    void EnsureAudioSources()
    {
        AudioSource[] existing = GetComponents<AudioSource>();
        if (soundSource == null && existing.Length > 0)
            soundSource = existing[0];
        if (voiceSource == null && existing.Length > 1)
            voiceSource = existing[1];

        if (soundSource == null)
            soundSource = CreateAudioSource("TestPlayAudio_Snd");
        if (voiceSource == null)
            voiceSource = CreateAudioSource("TestPlayAudio_Voice");
        if (propulsionStartSource == null)
            propulsionStartSource = CreateAudioSource("TestPlayAudio_PropulsionStart");
        if (propulsionSource == null)
            propulsionSource = CreateAudioSource("TestPlayAudio_Propulsion");

        ConfigureAudioSource(soundSource);
        ConfigureAudioSource(voiceSource);
        ConfigureAudioSource(propulsionStartSource);
        ConfigureAudioSource(propulsionSource);
        propulsionStartSource.loop = false;
        propulsionSource.loop = true;
        if (!Application.isPlaying && !PropulsionLoopRequested)
            propulsionSource.volume = 0f;
    }

    AudioSource CreateAudioSource(string objectName)
    {
        GameObject emitter = new GameObject(objectName);
        emitter.transform.SetParent(transform, false);
        return emitter.AddComponent<AudioSource>();
    }

    void ConfigureAudioSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.spatialBlend = useSpatialAudio ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = Mathf.Max(1f, audioMaxDistance);
    }

    void AttachEmitter(AudioSource source, Transform sourceRoot)
    {
        if (source == null || source.gameObject == gameObject)
            return;

        source.transform.SetParent(sourceRoot, false);
        source.transform.localPosition = Vector3.zero;
    }

    public bool HasOriginalNamedTexture(string fileName)
    {
        TestPlayTextureBinding binding = FindOriginalTextureByName(originalTextures, fileName);
        return binding != null && binding.texture != null;
    }

    void UpdatePropulsionFade()
    {
        if (!Application.isPlaying || propulsionSource == null)
            return;

        float targetVolume = PropulsionLoopRequested
            ? Mathf.Clamp01(propulsionLoopVolume)
            : 0f;
        if (propulsionFadeSeconds <= 0f)
        {
            propulsionSource.volume = targetVolume;
        }
        else
        {
            float speed = 1f / propulsionFadeSeconds;
            propulsionSource.volume = Mathf.MoveTowards(
                propulsionSource.volume,
                targetVolume,
                speed * Time.unscaledDeltaTime);
        }

        if (!PropulsionLoopRequested && propulsionSource.isPlaying && propulsionSource.volume <= 0.0001f)
            propulsionSource.Stop();
    }
}
