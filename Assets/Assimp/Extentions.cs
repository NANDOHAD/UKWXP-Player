using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Assets
{
    public static class Extentions
    {
        public static Vector3 ToUnityVector3(this Assimp.Vector3D vector)
        {
            //Debug.Log($"Converting Assimp.Vector3D to Unity.Vector3: ({vector.X}, {vector.Y}, {vector.Z})");
            return new Vector3(vector.X, vector.Y, vector.Z);
        }

        public static Mesh ToUnityMesh(this Assimp.Mesh mesh)
        {
            return ToUnityMesh(mesh, false);
        }

        public static Mesh ToUnityMesh(this Assimp.Mesh mesh, bool mirrorZ)
        {
            //Debug.Log($"Converting Assimp.Mesh: {mesh.Name}");
            Vector3[] vertices = mesh.Vertices.Select(x => x.ToUnityVector3()).ToArray();
            if (mirrorZ)
                MirrorZ(vertices);

            if (vertices.Length == 0)
                throw new InvalidOperationException($"Assimp mesh '{mesh.Name}' has no vertices.");

            ValidateFinite(vertices, mesh.Name, "vertex");

            int[] triangles = mesh.GetIntIndices().Reverse().ToArray();
            if (triangles.Length == 0)
                throw new InvalidOperationException($"Assimp mesh '{mesh.Name}' has no triangle indices.");
            if (triangles.Length % 3 != 0)
                throw new InvalidOperationException($"Assimp mesh '{mesh.Name}' has invalid triangle index count: {triangles.Length}");

            ValidateIndices(triangles, vertices.Length, mesh.Name);

            Mesh result = new Mesh
            {
                name = mesh.Name,
                vertices = vertices,
                triangles = triangles
            };

            Vector3[] normals = mesh.Normals == null
                ? new Vector3[0]
                : mesh.Normals.Select(x => x.ToUnityVector3()).ToArray();
            if (mirrorZ)
                MirrorZ(normals);

            if (normals.Length == vertices.Length)
            {
                ValidateFinite(normals, mesh.Name, "normal");
                result.normals = normals;
            }

            var textureCoords = mesh.GetTextureCoords(0);
            Vector2[] uv = textureCoords == null
                ? new Vector2[0]
                : textureCoords.Select(x => new Vector2(x.X, x.Y)).ToArray();
            if (uv.Length == vertices.Length)
            {
                ValidateFinite(uv, mesh.Name, "uv");
                result.uv = uv;
            }

            //Debug.Log($"Vertices count: {result.vertices.Length}, Triangles count: {result.triangles.Length}, Normals count: {result.normals.Length}, UVs count: {result.uv.Length}");
            return result;
        }

        static void MirrorZ(Vector3[] values)
        {
            for (int i = 0; i < values.Length; i++)
                values[i].z = -values[i].z;
        }

        static void ValidateFinite(Vector3[] values, string meshName, string channel)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!IsFinite(values[i]))
                    throw new InvalidOperationException($"Assimp mesh '{meshName}' contains invalid {channel} #{i}: {values[i]}");
            }
        }

        static void ValidateFinite(Vector2[] values, string meshName, string channel)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!IsFinite(values[i]))
                    throw new InvalidOperationException($"Assimp mesh '{meshName}' contains invalid {channel} #{i}: {values[i]}");
            }
        }

        static void ValidateIndices(int[] indices, int vertexCount, string meshName)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] < 0 || indices[i] >= vertexCount)
                    throw new InvalidOperationException($"Assimp mesh '{meshName}' contains invalid triangle index #{i}: {indices[i]} / {vertexCount}");
            }
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static Matrix4x4 ToUnityMatrix(this Assimp.Matrix4x4 matrix)
        {
            //Debug.Log($"Converting Assimp.Matrix4x4 to Unity.Matrix4x4");
            Matrix4x4 unityMatrix = new Matrix4x4();

            unityMatrix.SetRow(0, new Vector4(matrix.A1, matrix.A2, matrix.A3, matrix.A4));
            unityMatrix.SetRow(1, new Vector4(matrix.B1, matrix.B2, matrix.B3, matrix.B4));
            unityMatrix.SetRow(2, new Vector4(matrix.C1, matrix.C2, matrix.C3, matrix.C4));
            unityMatrix.SetRow(3, new Vector4(matrix.D1, matrix.D2, matrix.D3, matrix.D4));

            //Debug.Log($"Unity Matrix: {unityMatrix}");
            return unityMatrix;
        }

        public static Color ToUnityColor(this Assimp.Color4D color)
        {
            //Debug.Log($"Converting Assimp.Color4D to Unity.Color: ({color.R}, {color.G}, {color.B}, {color.A})");
            return new Color(color.R, color.G, color.B, color.A);
        }
    }
}
