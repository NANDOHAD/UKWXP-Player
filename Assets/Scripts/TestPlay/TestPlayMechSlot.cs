using UnityEngine;

/// <summary>
/// Scene-placed empty host for standalone session mechs.
/// Runtime still builds RoboStructure parts under this object; the slot itself is not owned by MechAssetLease.
/// </summary>
public sealed class TestPlayMechSlot : MonoBehaviour
{
    [Tooltip("1=自機, 2=相手。Inspector表示用。")]
    public int slotIndex = 1;

    public Vector3 SpawnPosition => transform.position;
    public Quaternion SpawnRotation => transform.rotation;

    /// <summary>True when this slot has no leftover session components or part children.</summary>
    public bool IsEmpty()
    {
        if (GetComponent<RoboStructure>() != null || GetComponent<TestPlayController>() != null)
            return false;
        return transform.childCount == 0;
    }
}
