using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unity presentation adapter for the original RunProc2 type 1 LZ_Beam trail.
/// The original keeps a bounded position history. Render that history as a
/// camera-facing ribbon instead of pre-extending one billboard quad by the
/// eventual trail capacity.
/// </summary>
public class TestPlayType1TrailEffect : MonoBehaviour
{
    public Texture2D sourceTexture;
    public float displayWidth;

    LineRenderer trailRenderer;
    Material runtimeMaterial;

    public LineRenderer TrailRenderer => trailRenderer;
    public int PositionCount => trailRenderer != null ? trailRenderer.positionCount : 0;

    public void Initialize(Texture2D texture, Shader shader, float width, Vector3 initialPosition)
    {
        sourceTexture = texture;
        displayWidth = Mathf.Max(0.01f, Mathf.Abs(width));

        trailRenderer = GetComponent<LineRenderer>();
        if (trailRenderer == null)
            trailRenderer = gameObject.AddComponent<LineRenderer>();
        trailRenderer.useWorldSpace = true;
        trailRenderer.alignment = LineAlignment.View;
        trailRenderer.textureMode = LineTextureMode.Stretch;
        trailRenderer.widthMultiplier = displayWidth;
        trailRenderer.numCapVertices = 0;
        trailRenderer.numCornerVertices = 0;
        trailRenderer.positionCount = 1;
        trailRenderer.SetPosition(0, initialPosition);

        if (shader == null || texture == null)
            return;

        runtimeMaterial = new Material(shader)
        {
            name = "TestPlayType1Trail_" + texture.name,
            hideFlags = HideFlags.DontSave
        };
        runtimeMaterial.SetTexture("_MainTex", texture);
        runtimeMaterial.SetColor("_TintColor", Color.white);
        trailRenderer.sharedMaterial = runtimeMaterial;
    }

    public void SetTrail(IReadOnlyList<Vector3> positions)
    {
        if (trailRenderer == null || positions == null || positions.Count == 0)
            return;

        trailRenderer.positionCount = positions.Count;
        for (int i = 0; i < positions.Count; i++)
            trailRenderer.SetPosition(i, positions[i]);
    }

    void OnDestroy()
    {
        if (runtimeMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeMaterial);
        else
            DestroyImmediate(runtimeMaterial);
    }
}
