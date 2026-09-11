using UnityEngine;

public class TestPlayThunderEffect : MonoBehaviour
{
    Transform[] planes;
    Vector3 spawnPosition;
    Quaternion spawnRotation;
    Vector3 movementDirection;

    public int WeaponPointId { get; private set; }
    public int TextureId { get; private set; }
    public int OriginalLength { get; private set; }
    public float InitialWidth { get; private set; }
    public float CurrentWidth { get; private set; }
    public float ScatterRadius { get; private set; }
    public float TravelDistance { get; private set; }
    public int AppliedTicks { get; private set; }
    public Vector3 SpawnPosition => spawnPosition;
    public Quaternion SpawnRotation => spawnRotation;
    public Vector3 MovementDirection => movementDirection;
    public bool HasTextureLayer => planes != null && planes.Length > 0;

    public void Initialize(
        TestPlayThunderEffectParameters parameters,
        Transform[] texturePlanes,
        Vector3 position,
        Quaternion rotation,
        Vector3 direction)
    {
        WeaponPointId = parameters.weaponPointId;
        TextureId = parameters.textureId;
        OriginalLength = parameters.length;
        InitialWidth = parameters.width;
        ScatterRadius = parameters.scatterRadius;
        planes = texturePlanes ?? new Transform[0];
        spawnPosition = position;
        spawnRotation = rotation;
        movementDirection = direction.sqrMagnitude > 0.000001f
            ? direction.normalized
            : rotation * Vector3.forward;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        ApplyOriginalTickState(parameters.width, 0f, 0);
    }

    public void ApplyOriginalTickState(float width, float travelDistance, int elapsedTicks)
    {
        CurrentWidth = width;
        TravelDistance = travelDistance;
        AppliedTicks = elapsedTicks;

        Vector3 localScatter = elapsedTicks > 0
            ? new Vector3(
                SampleSigned(WeaponPointId, elapsedTicks, 0),
                SampleSigned(WeaponPointId, elapsedTicks, 1),
                SampleSigned(WeaponPointId, elapsedTicks, 2)) * ScatterRadius
            : Vector3.zero;
        transform.position = spawnPosition + movementDirection * travelDistance + spawnRotation * localScatter;
        transform.rotation = spawnRotation;
        ConfigureCrossedPlanes();
    }

    void ConfigureCrossedPlanes()
    {
        float displayWidth = Mathf.Max(0.001f, Mathf.Abs(CurrentWidth));
        float displayLength = Mathf.Max(0.001f, Mathf.Abs(OriginalLength));
        for (int i = 0; i < planes.Length; i++)
        {
            Transform plane = planes[i];
            if (plane == null)
                continue;

            plane.localPosition = Vector3.forward * (displayLength * 0.5f);
            if ((i & 1) == 0)
            {
                plane.localRotation = Quaternion.Euler(90f, 0f, 0f);
                plane.localScale = new Vector3(displayWidth, displayLength, 1f);
            }
            else
            {
                plane.localRotation = Quaternion.Euler(0f, -90f, 0f);
                plane.localScale = new Vector3(displayLength, displayWidth, 1f);
            }
        }
    }

    static float SampleSigned(int weaponPointId, int tick, int axis)
    {
        // The executable uses its shared RNG. Keep the confirmed +/-p7 range,
        // but use a deterministic hash so Unity verification and Golden Trace
        // remain reproducible; the exact random sequence is an Adapter boundary.
        unchecked
        {
            uint value = (uint)(weaponPointId + 1) * 0x9E3779B9u;
            value ^= (uint)(tick + 1) * 0x85EBCA6Bu;
            value ^= (uint)(axis + 1) * 0xC2B2AE35u;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 8388607.5f - 1f;
        }
    }
}
