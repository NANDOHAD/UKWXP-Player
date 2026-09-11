using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TestPlayBurnerCone : MonoBehaviour
{
    const int SegmentCount = 18;

    static Mesh sharedConeMesh;
    static Mesh sharedPlumeMesh;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Material material;
    Texture2D sourceTexture;
    Shader sourceShader;
    float currentLength;
    float targetLength;
    float radius = 0.2f;
    float fadeSpeed = 18f;

    public bool UsesOriginalTexture => sourceTexture != null;

    public void ConfigureVisual(Texture2D texture, Shader shader)
    {
        EnsureInitialized();
        if (sourceTexture == texture && sourceShader == shader)
            return;

        sourceTexture = texture;
        sourceShader = shader;
        meshFilter.sharedMesh = sourceTexture != null ? GetSharedPlumeMesh() : GetSharedConeMesh();
        ReplaceMaterial();
        ApplyScale();
    }

    public void SetTarget(bool requested, float length, float coneRadius, Color color, float transitionSpeed)
    {
        EnsureInitialized();

        radius = Mathf.Max(0.001f, coneRadius);
        fadeSpeed = Mathf.Max(0f, transitionSpeed);
        targetLength = requested ? Mathf.Max(0f, length) : 0f;

        // The original burner texture already contains its blue-white colour and
        // alpha profile. Preserve those pixels and use only output alpha as tint.
        Color displayColor = UsesOriginalTexture ? new Color(1f, 1f, 1f, color.a) : color;
        SetMaterialColor(displayColor);

        if (requested && !gameObject.activeSelf)
            gameObject.SetActive(true);

        if (fadeSpeed <= 0f)
        {
            currentLength = targetLength;
            ApplyScale();
        }
    }

    public void HideImmediate()
    {
        currentLength = 0f;
        targetLength = 0f;
        ApplyScale();
        gameObject.SetActive(false);
    }

    void Awake()
    {
        EnsureInitialized();
    }

    void LateUpdate()
    {
        if (!gameObject.activeSelf)
            return;

        float step = fadeSpeed <= 0f ? 1f : Mathf.Clamp01(fadeSpeed * Time.deltaTime);
        currentLength = Mathf.Lerp(currentLength, targetLength, step);

        if (targetLength <= 0f && currentLength <= 0.01f)
        {
            currentLength = 0f;
            ApplyScale();
            gameObject.SetActive(false);
            return;
        }

        ApplyScale();
    }

    void EnsureInitialized()
    {
        if (meshRenderer != null)
            return;

        meshFilter = GetComponent<MeshFilter>();
        meshFilter.sharedMesh = GetSharedConeMesh();

        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        ReplaceMaterial();
        ApplyScale();
    }

    void ReplaceMaterial()
    {
        DestroyMaterial();
        material = CreateMaterial(sourceShader, sourceTexture);
        meshRenderer.sharedMaterial = material;
    }

    void ApplyScale()
    {
        float widthScale = UsesOriginalTexture ? radius * 2f : radius;
        transform.localScale = new Vector3(widthScale, widthScale, Mathf.Max(0f, currentLength));
    }

    static Mesh GetSharedConeMesh()
    {
        if (sharedConeMesh != null)
            return sharedConeMesh;

        Vector3[] vertices = new Vector3[SegmentCount + 1];
        int[] triangles = new int[SegmentCount * 3];

        vertices[0] = Vector3.zero;
        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = Mathf.PI * 2f * i / SegmentCount;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 1f);
        }

        for (int i = 0; i < SegmentCount; i++)
        {
            int next = (i + 1) % SegmentCount;
            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = next + 1;
            triangles[tri + 2] = i + 1;
        }

        sharedConeMesh = new Mesh { name = "TestPlayBurnerCone" };
        sharedConeMesh.vertices = vertices;
        sharedConeMesh.triangles = triangles;
        sharedConeMesh.RecalculateNormals();
        sharedConeMesh.RecalculateBounds();
        return sharedConeMesh;
    }

    static Mesh GetSharedPlumeMesh()
    {
        if (sharedPlumeMesh != null)
            return sharedPlumeMesh;

        // Two crossed, double-sided planes keep the original 2D burner readable
        // from gameplay camera angles. Both are rooted at Z=0 and extend toward Z+.
        Vector3[] vertices =
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 0f, 1f), new Vector3(0.5f, 0f, 1f),
            new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f),
            new Vector3(0f, -0.5f, 1f), new Vector3(0f, 0.5f, 1f)
        };
        Vector2[] uv =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f)
        };
        int[] triangles =
        {
            0, 2, 1, 1, 2, 3,
            4, 5, 6, 5, 7, 6
        };

        sharedPlumeMesh = new Mesh { name = "TestPlayBurnerOriginalTexturePlume" };
        sharedPlumeMesh.vertices = vertices;
        sharedPlumeMesh.uv = uv;
        sharedPlumeMesh.triangles = triangles;
        sharedPlumeMesh.RecalculateNormals();
        sharedPlumeMesh.RecalculateBounds();
        return sharedPlumeMesh;
    }

    static Material CreateMaterial(Shader preferredShader, Texture2D texture)
    {
        Shader shader = preferredShader;
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader)
        {
            name = texture != null ? "TestPlayBurnerOriginalTextureMaterial" : "TestPlayBurnerConeMaterial",
            hideFlags = HideFlags.DontSave
        };
        if (texture != null)
        {
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", texture);
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", texture);
        }
        SetMaterialColor(mat, new Color(0.35f, 0.85f, 1f, 0.65f));
        mat.renderQueue = (int)RenderQueue.Transparent;

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_SrcBlend"))
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))
            mat.SetInt("_DstBlend", (int)BlendMode.One);
        if (mat.HasProperty("_ZWrite"))
            mat.SetInt("_ZWrite", 0);
        if (mat.HasProperty("_Cull"))
            mat.SetInt("_Cull", (int)CullMode.Off);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_ALPHABLEND_ON");
        return mat;
    }

    void SetMaterialColor(Color color)
    {
        if (material != null)
            SetMaterialColor(material, color);
    }

    static void SetMaterialColor(Material mat, Color color)
    {
        if (mat.HasProperty("_TintColor"))
            mat.SetColor("_TintColor", color);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
    }

    void OnDestroy()
    {
        DestroyMaterial();
    }

    void DestroyMaterial()
    {
        if (material == null)
            return;

        Material oldMaterial = material;
        material = null;
        if (Application.isPlaying)
            Destroy(oldMaterial);
        else
            DestroyImmediate(oldMaterial);
    }
}
