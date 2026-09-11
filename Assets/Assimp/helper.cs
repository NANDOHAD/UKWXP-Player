using UnityEngine;
using System.Collections.Generic;
using System.IO;

public static class Helper
{
        public const Assimp.PostProcessSteps PostProcessStepflags = Assimp.PostProcessSteps.OptimizeMeshes |
        Assimp.PostProcessSteps.OptimizeGraph |
        Assimp.PostProcessSteps.RemoveRedundantMaterials |
        Assimp.PostProcessSteps.SortByPrimitiveType |
        Assimp.PostProcessSteps.SplitLargeMeshes |
        Assimp.PostProcessSteps.Triangulate |
        Assimp.PostProcessSteps.CalculateTangentSpace |
        Assimp.PostProcessSteps.GenerateUVCoords |
        Assimp.PostProcessSteps.GenerateSmoothNormals |
        Assimp.PostProcessSteps.RemoveComponent |
        Assimp.PostProcessSteps.JoinIdenticalVertices |
        Assimp.PostProcessSteps.JoinIdenticalVertices |
        Assimp.PostProcessSteps.MakeLeftHanded;

        // 0032 text X files may use meshless Frames as pivots. Avoid graph/mesh/material
        // optimization so MeshIndex remains tied to the original Frame transform.
        public const Assimp.PostProcessSteps PostProcessStepflagsForTextFloat32XFile =
        Assimp.PostProcessSteps.Triangulate;

        public static Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>();

        public static Texture2D LoadTexture(string path)
        {
            Texture2D result;

            if (!TextureCache.TryGetValue(path, out result))
            {
                result = new Texture2D(0, 0);
                result.LoadImage(File.ReadAllBytes(path));
                result.name = Path.GetFileNameWithoutExtension(path);
                TextureCache[path] = result;
            }

            return result;
        }

        public static Texture2D LoadTextureEncrypted(string path, ref CypherTranscoder transcoder)
        {
            Texture2D result;

            if (!TextureCache.TryGetValue(path, out result))
            {
                result = new Texture2D(0, 0);
                byte[] imageData = transcoder.Transcode(path);
                
                if (imageData == null || imageData.Length == 0)
                {
                    return null; // エラー処理
                }

                if (!result.LoadImage(imageData))
                {
                    return null; // エラー処理
                }

                result.name = Path.GetFileNameWithoutExtension(path);
                TextureCache[path] = result;
            }

            return result;
        }
}
