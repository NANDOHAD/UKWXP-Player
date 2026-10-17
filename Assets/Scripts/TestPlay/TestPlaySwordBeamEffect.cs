using UnityEngine;
using System.Collections.Generic;

public class TestPlaySwordBeamEffect : MonoBehaviour
{
    const float MotionEpsilon = 0.0005f;

    Transform anchor;
    Transform[] primaryPlanes;
    Transform[] linePlanes;
    Mesh[] lineMeshes;
    struct BladePose { public Vector3 root, direction; }
    readonly List<BladePose> history = new List<BladePose>();
    public int HistoryCount => history.Count;
    Renderer[] lineRenderers;


    public Transform Anchor => anchor;
    public float CurrentLength { get; private set; }
    public float TargetLength { get; private set; }
    public float CurrentPrimaryWidth { get; private set; }
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
        CurrentLength = initialLength;
        CurrentPrimaryWidth = TestPlayPresentationCore.OriginalSwordBeamInitialFullWidth;
        ApplyPrimaryGeometry();
        CaptureCurrentPose();
        SetLineBlurVisible(false);
    }

    public void SetLength(float length)
    {
        bool lengthGrew = length > CurrentLength;
        CurrentLength = length;
        CurrentPrimaryWidth = TestPlayPresentationCore.AdvanceOriginalSwordBeamPrimaryWidth(
            CurrentPrimaryWidth,
            lengthGrew);
        ApplyPrimaryGeometry();
    }

    void ApplyPrimaryGeometry()
    {
        float displayLength = Mathf.Max(0.0001f, Mathf.Abs(CurrentLength));
        ConfigureCrossedPlanes(
            primaryPlanes,
            displayLength,
            CurrentPrimaryWidth);
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

            // The source quad is forward-anchored (local Y 0..1), matching
            // FUN_00573750, so its root remains exactly on WEAPONPOINT.
            plane.localPosition = Vector3.zero;
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

    // Called once after the controller has applied pose, root motion and aim.
    // 00497cd0 / 00502cb0: five root+direction samples; never advance on a render frame.
    public void SampleOriginalTick() => UpdateMotionBlur();

    void UpdateMotionBlur()
    {
        if (anchor == null || linePlanes == null || linePlanes.Length == 0)
        { history.Clear(); SetLineBlurVisible(false); return; }
        history.Add(new BladePose { root=transform.position, direction=transform.forward });
        while (history.Count > 5) history.RemoveAt(0);
        bool moved=false;
        for(int i=1;i<history.Count;i++)
            moved |= (history[i].root-history[0].root).sqrMagnitude > MotionEpsilon*MotionEpsilon ||
                (history[i].direction-history[0].direction).sqrMagnitude > MotionEpsilon*MotionEpsilon;
        bool visible=moved && Mathf.Abs(CurrentLength)>MotionEpsilon;
        if(visible) UpdateLineBlurGeometry();
        SetLineBlurVisible(visible);
    }

    void PrepareLineBlurMeshes()
    {
        lineMeshes = new Mesh[linePlanes.Length]; lineRenderers = new Renderer[linePlanes.Length];
        for(int i=0;i<linePlanes.Length;i++)
        {
            var plane=linePlanes[i]; if(plane==null)continue;
            plane.localPosition=Vector3.zero;plane.localRotation=Quaternion.identity;plane.localScale=Vector3.one;
            lineRenderers[i]=plane.GetComponent<Renderer>();
            var filter=plane.GetComponent<MeshFilter>(); if(filter==null)continue;
            lineMeshes[i]=new Mesh { name="SwordHistory",hideFlags=HideFlags.DontSave };
            filter.sharedMesh=lineMeshes[i];
        }
    }
    static Vector3 Catmull(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float t)
    { return .5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t); }
    void UpdateLineBlurGeometry()
    {
        // 00512090 subdivides each root and blade-tip history span into five.
        // One swept surface avoids doubling the same Beam_Line layer additively.
        int samples=(history.Count-1)*5+1;
        var vertices=new Vector3[samples*2];var uv=new Vector2[vertices.Length];var triangles=new int[(samples-1)*6];
        for(int n=0;n<samples;n++)
        {
            int i=Mathf.Min(n/5,history.Count-2);float t=(n-i*5)/5f;
            var a=history[Mathf.Max(0,i-1)];var b=history[i];var c=history[i+1];var d=history[Mathf.Min(history.Count-1,i+2)];
            Vector3 root=Catmull(a.root,b.root,c.root,d.root,t);
            Vector3 tip=Catmull(a.root+a.direction*CurrentLength,b.root+b.direction*CurrentLength,c.root+c.direction*CurrentLength,d.root+d.direction*CurrentLength,t);
            vertices[n*2]=transform.InverseTransformPoint(root); vertices[n*2+1]=transform.InverseTransformPoint(tip);
            float v=1-n/(float)(samples-1);uv[n*2]=new Vector2(0,v);uv[n*2+1]=new Vector2(1,v);
            if(n==samples-1)continue;
            int k=n*6,j=n*2;triangles[k]=j;triangles[k+1]=j+1;triangles[k+2]=j+2;triangles[k+3]=j+1;triangles[k+4]=j+3;triangles[k+5]=j+2;
        }
        foreach(var mesh in lineMeshes)if(mesh!=null)
        {mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();}
    }
    void CaptureCurrentPose()
    {
        history.Clear();history.Add(new BladePose {root=transform.position,direction=transform.forward});
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
