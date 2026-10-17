using UnityEngine;
using System.Collections.Generic;

public class TestPlayThunderEffect : MonoBehaviour
{
    TestPlayType1TrailEffect trail;
    readonly List<Vector3> positions = new List<Vector3>();
    int activeTicks;
    public IReadOnlyList<Vector3> Points => positions;
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
    public bool HasTextureLayer => trail != null;

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
        trail = texturePlanes != null && texturePlanes.Length > 0 ? texturePlanes[0].GetComponent<TestPlayType1TrailEffect>() : null;
        activeTicks = parameters.activeTicks;
        positions.Clear(); positions.Add(position);
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
        bool advance = elapsedTicks > AppliedTicks;
        AppliedTicks = elapsedTicks;

        Vector3 localScatter = elapsedTicks > 0
            ? new Vector3(
                SampleSigned(WeaponPointId, elapsedTicks, 0),
                SampleSigned(WeaponPointId, elapsedTicks, 1),
                SampleSigned(WeaponPointId, elapsedTicks, 2)) * ScatterRadius
            : Vector3.zero;
        transform.position = spawnPosition + movementDirection * travelDistance + spawnRotation * localScatter;
        transform.rotation = spawnRotation;
        // The original adds scattered samples only while active; fade keeps the
        // existing world-space history. RNG values remain a deterministic adapter.
        if (advance && elapsedTicks < activeTicks)
        {
            positions.Add(transform.position);
            while (positions.Count > Mathf.Max(1, OriginalLength)) positions.RemoveAt(0);
        }
        trail?.SetTrail(positions); trail?.SetWidth(Mathf.Max(0, CurrentWidth));
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
