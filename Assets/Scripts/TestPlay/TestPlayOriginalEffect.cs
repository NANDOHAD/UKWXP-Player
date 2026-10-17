using UnityEngine;

public class TestPlayOriginalEffect : MonoBehaviour
{
    public Texture2D sourceTexture;
    public Vector2 displaySize = Vector2.one;
    public float lifeSeconds;
    public bool billboard = true;
    public float billboardRoll;
    // Optional world-space streak direction; zero retains ordinary billboarding.
    public Vector3 billboardAxis;

    Material runtimeMaterial;
    Mesh runtimeMesh;
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

    public void UseAlphaBlend()
    {
        if (runtimeMaterial != null) runtimeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    }

    public void UseFireMaterialPlanes()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null || runtimeMesh != null) return;
        // 00493f00: three sheets, 60 degrees apart, rotated around world Y.
        var vertices = new Vector3[12]; var uv = new Vector2[12]; var indices = new int[18];
        var basis = new[] { new Vector3(-.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0) };
        var tex = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
        for (int i=0;i<3;i++)
        {
            for (int v=0;v<4;v++) { vertices[i*4+v]=Quaternion.Euler(0,i*60,0)*basis[v];uv[i*4+v]=tex[v]; }
            int b=i*4,t=i*6;indices[t]=b;indices[t+1]=b+1;indices[t+2]=b+2;indices[t+3]=b+2;indices[t+4]=b+1;indices[t+5]=b+3;
        }
        runtimeMesh = new Mesh {name="OriginalFireThreePlanes",hideFlags=HideFlags.DontSave};
        runtimeMesh.vertices=vertices;runtimeMesh.uv=uv;runtimeMesh.triangles=indices;runtimeMesh.RecalculateBounds();
        filter.sharedMesh=runtimeMesh;billboard=false;
    }

    public void UseCrossedForwardPlanes()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null || runtimeMesh != null) return;
        // 00491720 draws four sheets around the length axis: 0, 90, 135, 225 deg.
        var vertices = new Vector3[16]; var uv = new Vector2[16]; var triangles = new int[24];
        float[] angles = { 0, 90, 135, 225 };
        var basis = new[] { new Vector3(-.5f,0,0), new Vector3(-.5f,1,0), new Vector3(.5f,0,0), new Vector3(.5f,1,0) };
        // Unity texture orientation: burner.png has its nozzle at V=0 and tail at V=1.
        // Keep the anchored geometry unchanged; reverse only the lengthwise UV.
        var tex = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.one };
        for (int i = 0; i < 4; i++)
        {
            for (int v = 0; v < 4; v++) { vertices[i*4+v] = Quaternion.Euler(0,angles[i],0)*basis[v]; uv[i*4+v] = tex[v]; }
            int b=i*4, t=i*6; triangles[t]=b; triangles[t+1]=b+1; triangles[t+2]=b+2;
            triangles[t+3]=b+2; triangles[t+4]=b+1; triangles[t+5]=b+3;
        }
        runtimeMesh = new Mesh { name="OriginalBurnerFourPlanes", hideFlags=HideFlags.DontSave };
        runtimeMesh.vertices=vertices; runtimeMesh.uv=uv; runtimeMesh.triangles=triangles; runtimeMesh.RecalculateBounds();
        filter.sharedMesh=runtimeMesh;
    }

    public void SetTint(Color color)
    {
        tint = color;
        if (runtimeMaterial != null)
            runtimeMaterial.SetColor("_TintColor", tint);
    }

    /// <summary>Unity adapter: wrap a bright-top/dark-bottom ramp around a flash,
    /// instead of exposing its rectangular source-image boundary.</summary>
    public void UseRadialGradient()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null || runtimeMesh != null) return;
        const int segments = 32;
        var vertices = new Vector3[segments + 2];
        var uv = new Vector2[vertices.Length];
        var indices = new int[segments * 3];
        uv[0] = new Vector2(.5f, 1f);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .5f;
            uv[i + 1] = new Vector2(i / (float)segments, 0f);
            if (i == segments) continue;
            indices[i * 3] = 0;
            indices[i * 3 + 1] = i + 2;
            indices[i * 3 + 2] = i + 1;
        }
        runtimeMesh = new Mesh { name = "TestPlayRadialFlash", hideFlags = HideFlags.DontSave };
        runtimeMesh.vertices = vertices; runtimeMesh.uv = uv; runtimeMesh.triangles = indices;
        runtimeMesh.RecalculateBounds();
        filter.sharedMesh = runtimeMesh;
    }

    public void UseForwardAnchoredQuad(bool flipVerticalUv = false)
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null || runtimeMesh != null) return;
        // FUN_00573750: x=-halfWidth..+halfWidth, y=0..length, with
        // UV (0,1), (0,0), (1,1), (1,0). Scale comes from Initialize;
        // the emitter remains the trailing edge for every caller.
        runtimeMesh = new Mesh { name = "TestPlayForwardQuad", hideFlags = HideFlags.DontSave };
        runtimeMesh.vertices = new[] {
            new Vector3(-.5f, 0, 0), new Vector3(-.5f, 1, 0),
            new Vector3(.5f, 0, 0), new Vector3(.5f, 1, 0) };
        runtimeMesh.uv = new[] { new Vector2(0, 1), Vector2.zero, Vector2.one, new Vector2(1, 0) };
        // Unity sword textures need their bottom at the emitter and top at the tip.
        // Keep the shared geometry and other callers' original UV convention intact.
        if (flipVerticalUv)
            runtimeMesh.uv = new[] { Vector2.zero, new Vector2(0, 1), new Vector2(1, 0), Vector2.one };
        runtimeMesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        runtimeMesh.RecalculateBounds();
        filter.sharedMesh = runtimeMesh;
    }

    public void SetDisplaySize(Vector2 size)
    {
        displaySize = new Vector2(
            Mathf.Max(0.01f, Mathf.Abs(size.x)),
            Mathf.Max(0.01f, Mathf.Abs(size.y)));
        transform.localScale = new Vector3(displaySize.x, displaySize.y, 1f);
    }

    public void SetSheetFrame(int columns, int rows, int frameIndex)
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        int frameCount = columns * rows;
        frameIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);
        int column = frameIndex % columns;
        int rowFromTop = frameIndex / columns;
        Vector2 scale = new Vector2(1f / columns, 1f / rows);
        Vector2 offset = new Vector2(column * scale.x, 1f - ((rowFromTop + 1) * scale.y));
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetTextureScale("_MainTex", scale);
            runtimeMaterial.SetTextureOffset("_MainTex", offset);
        }
    }

    public void FaceCamera(Camera camera)
    {
        if (!billboard || camera == null) return;
        transform.rotation = camera.transform.rotation * Quaternion.Euler(0,0,billboardRoll);
        if (billboardAxis.sqrMagnitude > 0.000001f)
        {
            Vector3 projected = camera.transform.InverseTransformDirection(billboardAxis);
            float angle = Mathf.Atan2(projected.y, projected.x) * Mathf.Rad2Deg - 90f;
            transform.rotation *= Quaternion.Euler(0f, 0f, angle);
        }
    }

    void LateUpdate()
    {
        if (billboard)
        {
            Camera camera = Camera.main;
            if (camera != null)
                FaceCamera(camera);
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
        if (runtimeMesh != null)
        {
            if (Application.isPlaying) Destroy(runtimeMesh);
            else DestroyImmediate(runtimeMesh);
        }
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);
        }
    }
}
