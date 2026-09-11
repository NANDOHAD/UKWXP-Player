using UnityEngine;

public class TestPlayOriginalEffect : MonoBehaviour
{
    public Texture2D sourceTexture;
    public Vector2 displaySize = Vector2.one;
    public float lifeSeconds;
    public bool billboard = true;

    Material runtimeMaterial;
    Color tint = Color.white;
    float age;

    public Color CurrentTint => tint;

    public void Initialize(Texture2D texture, Shader shader, Vector2 size, float life, Color color, bool faceCamera)
    {
        sourceTexture = texture;
        displaySize = new Vector2(Mathf.Max(0.01f, Mathf.Abs(size.x)), Mathf.Max(0.01f, Mathf.Abs(size.y)));
        lifeSeconds = Mathf.Max(0f, life);
        tint = color;
        billboard = faceCamera;
        transform.localScale = new Vector3(displaySize.x, displaySize.y, 1f);

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null || shader == null || texture == null)
            return;

        runtimeMaterial = new Material(shader)
        {
            name = "TestPlayOriginalEffect_" + texture.name,
            hideFlags = HideFlags.DontSave
        };
        runtimeMaterial.SetTexture("_MainTex", texture);
        runtimeMaterial.SetColor("_TintColor", tint);
        meshRenderer.sharedMaterial = runtimeMaterial;
    }

    public void SetTint(Color color)
    {
        tint = color;
        if (runtimeMaterial != null)
            runtimeMaterial.SetColor("_TintColor", tint);
    }

    public void SetDisplaySize(Vector2 size)
    {
        displaySize = new Vector2(
            Mathf.Max(0.01f, Mathf.Abs(size.x)),
            Mathf.Max(0.01f, Mathf.Abs(size.y)));
        transform.localScale = new Vector3(displaySize.x, displaySize.y, 1f);
    }

    void LateUpdate()
    {
        if (billboard)
        {
            Camera camera = Camera.main;
            if (camera != null)
                transform.rotation = camera.transform.rotation;
        }

        if (lifeSeconds <= 0f)
            return;

        age += Time.deltaTime;
        float remaining = 1f - Mathf.Clamp01(age / lifeSeconds);
        if (runtimeMaterial != null)
        {
            Color faded = tint;
            faded.a *= remaining;
            runtimeMaterial.SetColor("_TintColor", faded);
        }

        if (age >= lifeSeconds)
        {
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);
        }
    }
}
