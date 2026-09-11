using UnityEngine;
using System.Collections;

public static class Utils
{


    public static Quaternion GetRotation(Matrix4x4 matrix)
    {
        // Legacy HOD stores a TRS matrix, not a rotation matrix. Normalize
        // its columns before extracting rotation, including hidden parts at
        // scale 1e-6. Vector3.normalized would discard those small axes.
        Vector3 scale = GetScale(matrix);
        Vector3 x = UnitAxis(matrix.GetColumn(0), scale.x);
        Vector3 y = UnitAxis(matrix.GetColumn(1), scale.y);
        Vector3 z = UnitAxis(matrix.GetColumn(2), scale.z);

        // A collapsed axis carries no rotation information. Recover it from
        // the other two where possible; one/zero axes use a deterministic
        // orientation. The original matrix bytes remain available for saving.
        if (y.sqrMagnitude == 0f) y = Vector3.Cross(z, x).normalized;
        if (z.sqrMagnitude == 0f) z = Vector3.Cross(x, y).normalized;
        if (Vector3.Cross(y, z).sqrMagnitude > 0f)
            return Quaternion.LookRotation(z, y).normalized;
        if (z.sqrMagnitude > 0f) return Quaternion.FromToRotation(Vector3.forward, z);
        if (y.sqrMagnitude > 0f) return Quaternion.FromToRotation(Vector3.up, y);
        if (x.sqrMagnitude > 0f) return Quaternion.FromToRotation(Vector3.right, x);
        return Quaternion.identity;
    }

    public static Vector3 GetPosition(Matrix4x4 matrix)
    {
        return matrix.GetColumn(3);
    }

    public static Vector3 GetScale(Matrix4x4 m)
    {
        // Unity TRS scales columns. Row lengths mix rotation and nonuniform
        // scale. Assign a reflection to X so rotation stays right-handed.
        float x = AxisLength(m.GetColumn(0));
        float y = AxisLength(m.GetColumn(1));
        float z = AxisLength(m.GetColumn(2));
        if (Vector3.Dot(Vector3.Cross(UnitAxis(m.GetColumn(0), x),
            UnitAxis(m.GetColumn(1), y)), UnitAxis(m.GetColumn(2), z)) < 0f)
            x = -x;
        return new Vector3(x, y, z);
    }

    static float AxisLength(Vector3 axis)
    {
        return (float)System.Math.Sqrt((double)axis.x * axis.x
            + (double)axis.y * axis.y + (double)axis.z * axis.z);
    }

    static Vector3 UnitAxis(Vector3 axis, float length)
    {
        return length == 0f || float.IsNaN(length) || float.IsInfinity(length)
            ? Vector3.zero : axis / length;
    }
}
