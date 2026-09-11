using UnityEngine;
using UnityEngine.Rendering;

public enum TestPlayWindEffectKind
{
    WindLine,
    WindRing
}

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TestPlayWindEffect : MonoBehaviour
{
    const int RingSegmentCount = 24;

    static Mesh sharedLineMesh;
    static Mesh sharedRingMesh;

    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    Material material;
    Color baseColor = Color.white;
    float lifeSeconds;
    float ageSeconds;
    float expansionPerSecond;

    public TestPlayWindEffectKind Kind { get; private set; }
    public float DisplayWidth { get; private set; }
    public float DisplayLength { get; private set; }
    public float DisplayRadius { get; private set; }

    public void ConfigureWindLine(
        float width,
        float length,
        float life,
        Color color,
        Shader preferredShader)
    {
        EnsureInitialized();
        Kind = TestPlayWindEffectKind.WindLine;
        DisplayWidth = Mathf.Max(0.001f, width);
        DisplayLength = Mathf.Max(0.001f, length);
        DisplayRadius = 0f;
        lifeSeconds = Mathf.Max(0f, life);
        expansionPerSecond = 0f;
        baseColor = color;
        meshFilter.sharedMesh = GetSharedLineMesh();
        transform.localScale = new Vector3(DisplayWidth, DisplayWidth, DisplayLength);
        ReplaceMaterial(preferredShader);
    }

    public void ConfigureWindRing(
        float radius,
        float life,
        float expansionSpeed,
        Color color,
        Shader preferredShader)
    {
        EnsureInitialized();
        Kind = TestPlayWindEffectKind.WindRing;
        DisplayWidth = 0f;
        DisplayLength = 0f;
        DisplayRadius = Mathf.Max(0.001f, radius);
        lifeSeconds = Mathf.Max(0f, life);
        expansionPerSecond = Mathf.Max(0f, expansionSpeed);
        baseColor = color;
        meshFilter.sharedMesh = GetSharedRingMesh();
        transform.localScale = Vector3.one * DisplayRadius;
        ReplaceMaterial(preferredShader);
    }

    void Awake()
    {
        EnsureInitialized();
    }

    void Update()
    {
        if (expansionPerSecond > 0f)
            transform.localScale += Vector3.one * (expansionPerSecond * Time.deltaTime);

        if (lifeSeconds <= 0f)
            return;

        ageSeconds += Time.deltaTime;
        float remaining = 1f - Mathf.Clamp01(ageSeconds / lifeSeconds);
        SetMaterialColor(material, new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * remaining));
        if (ageSeconds >= lifeSeconds)
            Destroy(gameObject);
    }

    void EnsureInitialized()
    {
        if (meshRenderer != null)
            return;

        meshFilter = GetComponent<MeshFilter>();
        meshFilter.sharedMesh = GetSharedLineMesh();
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    void ReplaceMaterial(Shader preferredShader)
    {
        DestroyMaterial();
        Shader shader = preferredShader;
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
        {
            meshRenderer.enabled = false;
            return;
        }

        meshRenderer.enabled = true;
        material = new Material(shader)
        {
            name = Kind == TestPlayWindEffectKind.WindLine
                ? "TestPlayWindLineMaterial"
                : "TestPlayWindRingMaterial",
            hideFlags = HideFlags.DontSave
        };
        SetMaterialColor(material, baseColor);
        material.renderQueue = (int)RenderQueue.Transparent;
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_SrcBlend"))
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetInt("_DstBlend", (int)BlendMode.One);
        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 0);
        if (material.HasProperty("_Cull"))
            material.SetInt("_Cull", (int)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        meshRenderer.sharedMaterial = material;
    }

    static void SetMaterialColor(Material target, Color color)
    {
        if (target == null)
            return;
        if (target.HasProperty("_TintColor"))
            target.SetColor("_TintColor", color);
        if (target.HasProperty("_BaseColor"))
            target.SetColor("_BaseColor", color);
        if (target.HasProperty("_Color"))
            target.SetColor("_Color", color);
    }

    static Mesh GetSharedLineMesh()
    {
        if (sharedLineMesh != null)
            return sharedLineMesh;

        Vector3[] vertices =
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 0f, 1f), new Vector3(0.5f, 0f, 1f),
            new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f),
            new Vector3(0f, -0.5f, 1f), new Vector3(0f, 0.5f, 1f)
        };
        int[] triangles =
        {
            0, 2, 1, 1, 2, 3,
            4, 5, 6, 5, 7, 6
        };
        sharedLineMesh = new Mesh { name = "TestPlayWindLine" };
        sharedLineMesh.vertices = vertices;
        sharedLineMesh.triangles = triangles;
        sharedLineMesh.RecalculateNormals();
        sharedLineMesh.RecalculateBounds();
        return sharedLineMesh;
    }

    static Mesh GetSharedRingMesh()
    {
        if (sharedRingMesh != null)
            return sharedRingMesh;

        Vector3[] vertices = new Vector3[RingSegmentCount * 2];
        int[] triangles = new int[RingSegmentCount * 6];
        for (int i = 0; i < RingSegmentCount; i++)
        {
            float angle = Mathf.PI * 2f * i / RingSegmentCount;
            float x = Mathf.Cos(angle);
            float y = Mathf.Sin(angle);
            vertices[i * 2] = new Vector3(x, y, 0f);
            vertices[i * 2 + 1] = new Vector3(x * 0.78f, y * 0.78f, 0f);

            int next = (i + 1) % RingSegmentCount;
            int triangle = i * 6;
            triangles[triangle] = i * 2;
            triangles[triangle + 1] = next * 2;
            triangles[triangle + 2] = i * 2 + 1;
            triangles[triangle + 3] = i * 2 + 1;
            triangles[triangle + 4] = next * 2;
            triangles[triangle + 5] = next * 2 + 1;
        }

        sharedRingMesh = new Mesh { name = "TestPlayWindRing" };
        sharedRingMesh.vertices = vertices;
        sharedRingMesh.triangles = triangles;
        sharedRingMesh.RecalculateNormals();
        sharedRingMesh.RecalculateBounds();
        return sharedRingMesh;
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
