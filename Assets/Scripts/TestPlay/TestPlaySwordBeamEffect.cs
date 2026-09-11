using UnityEngine;

public class TestPlaySwordBeamEffect : MonoBehaviour
{
    const float MotionEpsilon = 0.0005f;

    Transform anchor;
    Transform[] primaryPlanes;
    Transform[] linePlanes;
    Mesh[] lineMeshes;
    Vector3[][] lineVertices;
    Renderer[] lineRenderers;
    Vector3 previousRootWorld;
    Vector3 previousTipWorld;
    bool hasPreviousPose;

    public Transform Anchor => anchor;
    public float CurrentLength { get; private set; }
    public float TargetLength { get; private set; }
    public float LineWidth => TestPlayPresentationCore.OriginalSwordBeamWidth;
    public int PrimaryTextureId { get; private set; }
    public int LineTextureId { get; private set; }
    public bool HasPrimaryLayer => primaryPlanes != null && primaryPlanes.Length > 0;
    public bool HasLineLayer => linePlanes != null && linePlanes.Length > 0;
    public bool IsLineBlurVisible { get; private set; }
    public int PrimaryPlaneCount => primaryPlanes != null ? primaryPlanes.Length : 0;
    public int LinePlaneCount => linePlanes != null ? linePlanes.Length : 0;

    public Transform GetPrimaryPlane(int index)
    {
        return primaryPlanes != null && index >= 0 && index < primaryPlanes.Length
            ? primaryPlanes[index]
            : null;
    }

    public Transform GetLinePlane(int index)
    {
        return linePlanes != null && index >= 0 && index < linePlanes.Length
            ? linePlanes[index]
            : null;
    }

    public void Initialize(
        Transform sourceAnchor,
        Transform[] primaryLayerPlanes,
        Transform[] lineLayerPlanes,
        int primaryTextureId,
        int lineTextureId,
        bool reverseDirection,
        float initialLength,
        float targetLength)
    {
        anchor = sourceAnchor;
        primaryPlanes = primaryLayerPlanes ?? new Transform[0];
        linePlanes = lineLayerPlanes ?? new Transform[0];
        PrimaryTextureId = primaryTextureId;
        LineTextureId = lineTextureId;
        TargetLength = targetLength;

        if (anchor != null)
        {
            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = reverseDirection
                ? Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        PrepareLineBlurMeshes();
        SetLength(initialLength);
        CaptureCurrentPose();
        SetLineBlurVisible(false);
    }

    public void SetLength(float length)
    {
        CurrentLength = length;
        float displayLength = Mathf.Max(0.0001f, Mathf.Abs(length));
        ConfigureCrossedPlanes(
            primaryPlanes,
            displayLength,
            TestPlayPresentationCore.OriginalSwordBeamWidth * 2f);
    }

    static void ConfigureCrossedPlanes(Transform[] planes, float length, float width)
    {
        if (planes == null)
            return;

        for (int i = 0; i < planes.Length; i++)
        {
            Transform plane = planes[i];
            if (plane == null)
                continue;

            plane.localPosition = Vector3.forward * (length * 0.5f);
            // Both source textures are vertically authored. Keep local Y mapped to
            // the saber's local Z+ on both crossed planes; only the plane normal
            // changes. Scaling local X as the length rotated the second texture
            // sideways even though the quad geometry still crossed the first.
            plane.localRotation = (i & 1) == 0
                ? Quaternion.LookRotation(Vector3.down, Vector3.forward)
                : Quaternion.LookRotation(Vector3.right, Vector3.forward);
            plane.localScale = new Vector3(width, length, 1f);
        }
    }

    void LateUpdate()
    {
        UpdateMotionBlur();
    }

    void UpdateMotionBlur()
    {
        if (anchor == null || linePlanes == null || linePlanes.Length == 0)
        {
            SetLineBlurVisible(false);
            hasPreviousPose = false;
            return;
        }

        Vector3 currentRootWorld = transform.position;
        Vector3 currentTipWorld = transform.TransformPoint(
            Vector3.forward * Mathf.Max(0f, Mathf.Abs(CurrentLength)));
        if (!hasPreviousPose)
        {
            previousRootWorld = currentRootWorld;
            previousTipWorld = currentTipWorld;
            hasPreviousPose = true;
            SetLineBlurVisible(false);
            return;
        }

        float thresholdSqr = MotionEpsilon * MotionEpsilon;
        bool moved = (currentRootWorld - previousRootWorld).sqrMagnitude > thresholdSqr ||
            (currentTipWorld - previousTipWorld).sqrMagnitude > thresholdSqr;
        bool visible = moved && Mathf.Abs(CurrentLength) > MotionEpsilon;
        if (visible)
        {
            UpdateLineBlurGeometry(
                previousRootWorld,
                previousTipWorld,
                currentRootWorld,
                currentTipWorld);
        }
        SetLineBlurVisible(visible);

        previousRootWorld = currentRootWorld;
        previousTipWorld = currentTipWorld;
    }

    void PrepareLineBlurMeshes()
    {
        if (linePlanes == null)
            return;

        lineMeshes = new Mesh[linePlanes.Length];
        lineVertices = new Vector3[linePlanes.Length][];
        lineRenderers = new Renderer[linePlanes.Length];
        for (int i = 0; i < linePlanes.Length; i++)
        {
            Transform plane = linePlanes[i];
            if (plane == null)
                continue;

            plane.localPosition = Vector3.zero;
            plane.localRotation = Quaternion.identity;
            plane.localScale = Vector3.one;
            lineRenderers[i] = plane.GetComponent<Renderer>();
            MeshFilter filter = plane.GetComponent<MeshFilter>();
            if (filter == null)
                continue;

            Mesh mesh = new Mesh
            {
                name = plane.name + "_MotionBlurMesh",
                hideFlags = HideFlags.DontSave
            };
            Vector3[] vertices = new Vector3[4];
            mesh.vertices = vertices;
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            filter.sharedMesh = mesh;
            lineMeshes[i] = mesh;
            lineVertices[i] = vertices;
        }
    }

    void UpdateLineBlurGeometry(
        Vector3 oldRootWorld,
        Vector3 oldTipWorld,
        Vector3 currentRootWorld,
        Vector3 currentTipWorld)
    {
        if (lineMeshes == null)
            return;

        Vector3 bladeDirection = currentTipWorld - currentRootWorld;
        Vector3 centerMotion = (currentRootWorld + currentTipWorld) - (oldRootWorld + oldTipWorld);
        Vector3 offsetDirection = Vector3.Cross(bladeDirection, centerMotion);
        if (offsetDirection.sqrMagnitude <= 0.000001f)
            offsetDirection = transform.right;
        else
            offsetDirection.Normalize();

        float halfWidth = TestPlayPresentationCore.OriginalSwordBeamWidth * 0.5f;
        for (int i = 0; i < lineMeshes.Length; i++)
        {
            Mesh mesh = lineMeshes[i];
            Vector3[] vertices = lineVertices != null && i < lineVertices.Length
                ? lineVertices[i]
                : null;
            if (mesh == null || vertices == null)
                continue;

            float offsetSign = (i & 1) == 0 ? -1f : 1f;
            Vector3 offset = offsetDirection * (halfWidth * offsetSign);
            vertices[0] = transform.InverseTransformPoint(oldRootWorld + offset);
            vertices[1] = transform.InverseTransformPoint(oldTipWorld + offset);
            vertices[2] = transform.InverseTransformPoint(currentTipWorld + offset);
            vertices[3] = transform.InverseTransformPoint(currentRootWorld + offset);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
    }

    void CaptureCurrentPose()
    {
        previousRootWorld = transform.position;
        previousTipWorld = transform.TransformPoint(
            Vector3.forward * Mathf.Max(0f, Mathf.Abs(CurrentLength)));
        hasPreviousPose = true;
    }

    void SetLineBlurVisible(bool visible)
    {
        IsLineBlurVisible = visible;
        if (linePlanes == null)
            return;

        for (int i = 0; i < linePlanes.Length; i++)
        {
            Renderer renderer = lineRenderers != null && i < lineRenderers.Length
                ? lineRenderers[i]
                : null;
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    void OnDestroy()
    {
        if (lineMeshes == null)
            return;

        for (int i = 0; i < lineMeshes.Length; i++)
        {
            Mesh mesh = lineMeshes[i];
            if (mesh == null)
                continue;
            if (Application.isPlaying)
                Destroy(mesh);
            else
                DestroyImmediate(mesh);
        }
    }
}
