using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions;
using System.IO;
using Assets;
using System.Linq;
using UnityEngine.UI;
public delegate void Update_Event();
public class RoboStructure : MonoBehaviour
{
    public GameObject root;
    public UI_InputBox inputBox;
    public Text statusMessege;
    public List<GameObject> parts = new List<GameObject>();
    public List<bool> isTop = new List<bool>();
    public hod2v0 hod;
    public ani2 ani;
    public Assimp.AssimpImporter Importer = new Assimp.AssimpImporter();
    public string folder;
    public string filename;
    public List<Update_Event> updates = new List<Update_Event>();
    public CypherTranscoder transcoder;
    public bool structureEditingAllowed { get; private set; } = true;
    public string structureValidationWarning { get; private set; } = "";
    string structureEditingBlockReason = "";
    
    // Start is called before the first frame update
    internal Action<UnityEngine.Object> RegisterOwnedAsset;

    public void InitializeRuntime()
    {
        if (transcoder == null)
            transcoder = new CypherTranscoder();
    }

    void Start()
    {
        InitializeRuntime();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void buildStructure(hod2v0 Robo)
    {
        int[] parents, depths;
        string error;
        if (!HodHierarchyRepair.TryGetDisplayHierarchy(Robo?.parts, out parents, out depths, out error))
            throw new InvalidDataException(error);

        InitializeRuntime();
        foreach (string file in Directory.GetFiles(folder))
        {
            if (transcoder.findCypher(file))
                break;
        }

        hod = Robo;
        ValidateStructureForEditing(Robo, true);
        DestroyExistingStructureObjects();
        BuildPartTransforms(Robo, parents);
        for (int i = 1; i < Robo.parts.Count; i++)
        {
            try
            {
                ImportModelEncrypted(parts[i], Path.Combine(folder, Robo.parts[i].name));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoboStructure] パーツモデルの読み込みに失敗しました: {Robo.parts[i].name} ({e.Message})");
            }
        }
    }

    public void buildStructureFromLoaded(RoboStructure source, hod2v0 Robo)
    {
        if (source == null)
            return;
        int[] parents, depths;
        string error;
        if (!HodHierarchyRepair.TryGetDisplayHierarchy(Robo?.parts, out parents, out depths, out error))
            throw new InvalidDataException(error);

        folder = source.folder;
        hod = Robo;
        DestroyExistingStructureObjects();
        BuildPartTransforms(Robo, parents);
        for (int i = 0; i < Robo.parts.Count; i++)
        {
            if (i < source.parts.Count && source.parts[i] != null)
                CopyRenderComponents(source.parts[i], parts[i]);
        }
    }

    void BuildPartTransforms(hod2v0 structure, int[] parents)
    {
        for (int i = 0; i < structure.parts.Count; i++)
        {
            var part = new GameObject(structure.parts[i].name);
            parts.Add(part);
            if (parents[i] < 0)
                root = part;
            else
                // 親がscale=0でもworldPositionStaysによる逆行列・NaNを発生させない。
                part.transform.SetParent(parts[parents[i]].transform, false);
            ApplyStructureTransform(part.transform, structure.parts[i]);
        }
    }

    static void ApplyStructureTransform(Transform target, hod2v0_Part part)
    {
        target.localPosition = part.position;
        target.localRotation = part.rotation;
        target.localScale = part.scale;
    }

    static void CopyRenderComponents(GameObject source, GameObject target)
    {
        MeshFilter srcFilter = source.GetComponent<MeshFilter>();
        MeshRenderer srcRenderer = source.GetComponent<MeshRenderer>();
        if (srcFilter != null)
        {
            MeshFilter dstFilter = target.AddComponent<MeshFilter>();
            dstFilter.sharedMesh = srcFilter.sharedMesh;
        }
        if (srcRenderer != null)
        {
            MeshRenderer dstRenderer = target.AddComponent<MeshRenderer>();
            dstRenderer.sharedMaterials = srcRenderer.sharedMaterials;
        }
    }

    void ImportModel(GameObject GO, string file)
    {
        if (File.Exists(file))
        {
            try
            {
                string Modelpath = Path.GetDirectoryName(file);
                bool mirrorZ = IsTextFloat32XFile(file);
                var scen = Importer.ImportFile(file, mirrorZ ? Helper.PostProcessStepflagsForTextFloat32XFile : Helper.PostProcessStepflags);
                if (scen == null)
                {
                    //Debug.logWarning($"Failed to import model: {file}. Assimp could not load the file.");
                    return;
                }

                Mesh mesh = new Mesh();
                RegisterOwnedAsset?.Invoke(mesh);
                List<int> combinedMeshIndices;
                List<CombineInstance> combineInstances = BuildCombineInstances(scen, file, mirrorZ, out combinedMeshIndices, RegisterOwnedAsset);
                if (combineInstances.Count == 0)
                {
                    Debug.LogWarning($"[RoboStructure] 有効なメッシュがありません: {file}");
                    return;
                }
                mesh.CombineMeshes(combineInstances.ToArray(), false);

                Material[] materials = new Material[combinedMeshIndices.Count];

                for (int index = 0; index < materials.Length; index++)
                {
                    int meshIndex = combinedMeshIndices[index];
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    RegisterOwnedAsset?.Invoke(mat);

                    if (scen.Meshes[meshIndex].MaterialIndex < scen.Materials.Length)
                    {
                        if (scen.Materials[scen.Meshes[meshIndex].MaterialIndex] != null)
                        {
                            mat.name = scen.Materials[scen.Meshes[meshIndex].MaterialIndex].Name;
                            var textures = scen.Materials[scen.Meshes[meshIndex].MaterialIndex].GetAllTextures();
                            var color = scen.Materials[scen.Meshes[meshIndex].MaterialIndex].ColorDiffuse;
                            mat.color = new Color(color.R, color.G, color.B, color.A);
                            mat.SetFloat("_Glossiness", scen.Materials[scen.Meshes[meshIndex].MaterialIndex].ShininessStrength);


                            if (textures.Length > 0 && File.Exists(Path.Combine(Modelpath, textures[0].FilePath)))
                            {
                                try
                                {
                                    mat.mainTexture = Helper.LoadTexture(Path.Combine(Modelpath, textures[0].FilePath));
                                    RegisterOwnedAsset?.Invoke(mat.mainTexture);
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                    else
                    {
                        //Debug.logWarning($"Invalid material index for mesh {index}: {scen.Meshes[index].MaterialIndex}");
                    }

                    materials[index] = mat;
                }

                GO.AddComponent<MeshFilter>().mesh = mesh;
                //part.AddComponent<MeshCollider>().sharedMesh = mesh; 
                GO.AddComponent<MeshRenderer>().materials = materials;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RoboStructure] モデル読み込みに失敗しました: {file} ({e.Message})");
            }
        }
    }

    void ImportModelEncrypted(GameObject GO, string file)
    {
        // HOD part names are also used for bones and attachment points. Such
        // nodes intentionally have no standalone .x model, but must remain in
        // the hierarchy so ANI/SPT references can resolve their transforms.
        if (!File.Exists(file))
            return;

        try
        {
            string Modelpath = Path.GetDirectoryName(file);
            byte[] data = transcoder.Transcode(file);
            if (data == null)
            {
                //Debug.logError($"Failed to transcode file: {file}. The result is null.");
                return;
            }
            if (data.Length == 0)
            {
                //Debug.logError($"The transcoded data for file {file} is empty.");
                return;
            }
            if (data.Length > 0)
            {
                string data1 = System.Text.Encoding.GetEncoding("utf-8").GetString(data);
                data1 = XfileStringConverter(data1);
                byte[] data3 = System.Text.Encoding.GetEncoding("utf-8").GetBytes(data1);
                MemoryStream ms = new MemoryStream(data3);
                Assimp.Scene scen = null;
                bool mirrorZ = IsTextFloat32Xfile(data1);
                try
                {
                    Assimp.PostProcessSteps importFlags = mirrorZ
                        ? Helper.PostProcessStepflagsForTextFloat32XFile
                        : Helper.PostProcessStepflags;
                    scen = Importer.ImportFileFromStream(ms, importFlags, "x");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[RoboStructure] 暗号化モデルのAssimp読み込みに失敗しました: {file} ({e.Message})");
                }
                if (scen == null)
                {
                    Debug.LogWarning($"[RoboStructure] 暗号化モデルを読み込めませんでした: {file}");
                    return;
                }



                Mesh mesh = new Mesh();
                RegisterOwnedAsset?.Invoke(mesh);
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                
                try
                {
                    List<int> combinedMeshIndices;
                    List<CombineInstance> combineInstances = BuildCombineInstances(scen, file, mirrorZ, out combinedMeshIndices, RegisterOwnedAsset);
                    if (combineInstances.Count == 0)
                    {
                        Debug.LogWarning($"[RoboStructure] 有効なメッシュがありません: {file}");
                        return;
                    }
                    mesh.CombineMeshes(combineInstances.ToArray(), false);

                    Material[] materials = new Material[combinedMeshIndices.Count];

                    for (int index = 0; index < materials.Length; index++)
                    {
                        int meshIndex = combinedMeshIndices[index];
                        var mat = new Material(Shader.Find("Standard"));
                        RegisterOwnedAsset?.Invoke(mat);
                        if (mat == null)
                        {
                            //Debug.logError($"Shader not found for material: {file}");
                            return;
                        }

                        if (scen.Meshes[meshIndex].MaterialIndex < scen.Materials.Length)
                        {
                            if (scen.Materials[scen.Meshes[meshIndex].MaterialIndex] != null)
                            {
                                mat.name = scen.Materials[scen.Meshes[meshIndex].MaterialIndex].Name;
                                var textures = scen.Materials[scen.Meshes[meshIndex].MaterialIndex].GetAllTextures();
                                var color = scen.Materials[scen.Meshes[meshIndex].MaterialIndex].ColorDiffuse;
                                mat.color = new Color(color.R, color.G, color.B, color.A);
                                mat.SetFloat("_Glossiness", scen.Materials[scen.Meshes[meshIndex].MaterialIndex].ShininessStrength);

                                // シェーダーが設定されていない場合、デフォルトのシェーダーを割り当てる
                                if (string.IsNullOrEmpty(mat.shader.name) || mat.shader == null)
                                {
                                    //Debug.logWarning($"Shader not set for material: {mat.name}. Assigning default shader.");
                                    mat.shader = Shader.Find("Standard"); // デフォルトのシェーダーを設定
                                }

                                if (textures.Length > 0 && File.Exists(Path.Combine(Modelpath, textures[0].FilePath)))
                                {
                                    try
                                    {
                                        mat.mainTexture = Helper.LoadTextureEncrypted(Path.Combine(Modelpath, textures[0].FilePath), ref transcoder);
                                        RegisterOwnedAsset?.Invoke(mat.mainTexture);
                                    }
                                    catch (System.Exception e)
                                    {
                                        Debug.LogWarning($"[RoboStructure] テクスチャ読み込みに失敗しました: {file} ({e.Message})");
                                    }
                                }
                                else
                                {
                                    //Debug.logWarning($"No textures found for material index {scen.Meshes[index].MaterialIndex}");
                                }
                            }
                        }
                        else
                        {
                            //Debug.logWarning($"Invalid material index for mesh {index}: {scen.Meshes[index].MaterialIndex}");
                        }

                        materials[index] = mat;
                    }

                    GO.AddComponent<MeshFilter>().mesh = mesh;
                    //part.AddComponent<MeshCollider>().sharedMesh = mesh;
                    GO.AddComponent<MeshRenderer>().materials = materials;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[RoboStructure] メッシュ結合に失敗しました: {file} ({e.Message})");
                    return;
                }
            }
            else
            {
                //Debug.logWarning($"Failed to decrypt file: {file}. Assimp could not load the file.");
                return;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RoboStructure] 暗号化モデル処理に失敗しました: {file} ({e.Message})");
        }
    }


    static List<CombineInstance> BuildCombineInstances(Assimp.Scene scen, string file, bool mirrorZ, out List<int> combinedMeshIndices, Action<UnityEngine.Object> registerAsset = null)
    {
        combinedMeshIndices = new List<int>();
        List<CombineInstance> combineInstances = new List<CombineInstance>();
        if (scen == null || scen.Meshes == null)
            return combineInstances;

        if (scen.RootNode != null)
            AddNodeMeshes(scen, scen.RootNode, Assimp.Matrix4x4.Identity, file, mirrorZ, combineInstances, combinedMeshIndices, registerAsset);

        return combineInstances;
    }

    static void AddNodeMeshes(Assimp.Scene scen, Assimp.Node node, Assimp.Matrix4x4 parentTransform, string file, bool mirrorZ, List<CombineInstance> combineInstances, List<int> combinedMeshIndices, Action<UnityEngine.Object> registerAsset)
    {
        if (node == null)
            return;

        Assimp.Matrix4x4 nodeTransform = parentTransform * node.Transform;
        Matrix4x4 unityTransform = nodeTransform.ToUnityMatrix();
        if (mirrorZ)
            unityTransform = MirrorZTransform(unityTransform);

        if (node.HasMeshes && node.MeshIndices != null)
        {
            foreach (int meshIndex in node.MeshIndices)
            {
                if (meshIndex < 0 || meshIndex >= scen.Meshes.Length)
                    continue;

                Assimp.Mesh sourceMesh = scen.Meshes[meshIndex];
                try
                {
                    Mesh unityMesh = sourceMesh.ToUnityMesh(mirrorZ);
                    registerAsset?.Invoke(unityMesh);
                    combineInstances.Add(new CombineInstance()
                    {
                        mesh = unityMesh,
                        transform = unityTransform
                    });
                    combinedMeshIndices.Add(meshIndex);
                }
                catch (Exception e)
                {
                    string meshName = sourceMesh != null ? sourceMesh.Name : $"#{meshIndex}";
                    Debug.LogWarning($"[RoboStructure] サブメッシュをスキップしました: {Path.GetFileName(file)} / {meshName} ({e.Message})");
                }
            }
        }

        if (!node.HasChildren || node.Children == null)
            return;

        foreach (Assimp.Node child in node.Children)
            AddNodeMeshes(scen, child, nodeTransform, file, mirrorZ, combineInstances, combinedMeshIndices, registerAsset);
    }

    static Matrix4x4 MirrorZTransform(Matrix4x4 transform)
    {
        Matrix4x4 mirror = Matrix4x4.Scale(new Vector3(1.0f, 1.0f, -1.0f));
        return mirror * transform * mirror;
    }


    public string XfileStringConverter(string data)
    {
        bool preserveFrameTransformSigns = IsTextFloat32Xfile(data);

        if (!data.Trim().EndsWith("}"))
        {
            //Debug.log("文字化けを確認しました");
            int lastBraceIndex = data.LastIndexOf('}');
            if (lastBraceIndex != -1)
            {
                data = data.Substring(0, lastBraceIndex + 1); // 最後の波括弧を残す
            }
            data += "}"; // 新たに波括弧
            //Debug.log("文字化けを対応しました。");
        }

        if (preserveFrameTransformSigns)
            return data;

        // FrameTransformMatrixの部分を探す正規表現        
        string pattern = @"FrameTransformMatrix\s*{([^}]*)}";
        MatchCollection matches = Regex.Matches(data, pattern, RegexOptions.Singleline);

        if (matches.Count >= 2) // 2回目のマッチが存在するか確認
        {
            
            string matrixContent = matches[1].Groups[1].Value;
            //Debug.log("マッチしました:" + matrixContent);
            // 4行目の数値を処理
            string[] lines = matrixContent.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
            //Debug.log("要素数は" + lines.Length);
            if (lines.Length >= 16) // 4行目の数値があるか確認
            {
                //Debug.log("4行目を確認しました。");
                for (int i = 13; i < 16; i++) // 4行目の数値（3つ）を処理
                {
                    lines[i] = lines[i].TrimStart('-'); // マイナス符号を取り除く
                    //Debug.log("符号排除処理をしました");
                }

                // 新しい内容を生成
                string newMatrixContent = string.Join(",", lines);
                string output = data.Replace(matrixContent, newMatrixContent);
                //Debug.log($"書き換えました: {output.Substring(output.Length - 10)}");
                return output;
            }
            else
            {
                //Debug.log("4行目の数値が見つかりませんでした。");
            }
        }
        else
        {
            //Debug.log("FrameTransformMatrixが見つかりませんでした。");
        }



        return data;
    }

    static bool IsTextFloat32Xfile(string data)
    {
        if (string.IsNullOrEmpty(data))
            return false;

        return Regex.IsMatch(data, @"\A\uFEFF?\s*xof\s+\d{4}txt\s+0032", RegexOptions.IgnoreCase);
    }

    static bool IsTextFloat32XFile(string file)
    {
        try
        {
            if (File.Exists(file) && IsTextFloat32Xfile(File.ReadAllText(file)))
                return true;
        }
        catch
        {
        }

        return false;
    }


    void OutputFrameTransformMatrix(Assimp.Node node, string fileName)
    {
        //Debug.log($"ファイル: {fileName} ");
        var transform = node.Transform;
        try
        {
            if (transform.A1 < 0 || transform.A2 < 0 || transform.A3 < 0 ||
            transform.B1 < 0 || transform.B2 < 0 || transform.B3 < 0 ||
            transform.C1 < 0 || transform.C2 < 0 || transform.C3 < 0)
            {
                
                //Debug.log($"マイナス発見: {fileName} has negative Transform values: {transform}");
            }else{
                //Debug.log($"マイナスなし: {fileName} has negative Transform values: {transform}");
            }    
        }catch (System.Exception e)
                {
                    //Debug.log($"発見 {fileName}: {e.Message}");
                    return;
                }

    }
    public void setPose(hod2v0 pose)
    {
        if (pose.parts.Count == parts.Count)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i].transform.localPosition = pose.parts[i].position;
                parts[i].transform.localRotation = pose.parts[i].rotation;
                parts[i].transform.localScale = pose.parts[i].scale;
            }
        }
    }

    public void setPose(hod2v1 pose)
    {
        if (pose.parts.Count == parts.Count)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                parts[i].transform.localPosition = pose.parts[i].position;
                parts[i].transform.localRotation = pose.parts[i].rotation;
                parts[i].transform.localScale = pose.parts[i].scale;
            }
        }
    }

    public void setPose(int AnimID, int HodID)
    {
        if (ani != null)
        {
            setPose(ani.animations[AnimID].frames[HodID]);
        }
    }
    public void updateHod(int AnimID, int HodID)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            updatePart(AnimID, HodID, i);
        }
    }
    public void updatePart(int AnimID, int HodID, int prtID, bool syncRotCont = true)
    {
        if (ani != null)
        {
            hod2v1_Part prt = ani.animations[AnimID].frames[HodID].parts[prtID];
            prt.position = parts[prtID].transform.localPosition;
            prt.rotation = parts[prtID].transform.localRotation;
            prt.scale = parts[prtID].transform.localScale;
            if (syncRotCont)
            {
                prt.unk1 = parts[prtID].transform.localRotation;
                prt.unk2 = parts[prtID].transform.localRotation;
                prt.unk3 = parts[prtID].transform.localRotation;
            }
            ani.animations[AnimID].frames[HodID].parts[prtID] = prt;
        }
    }
    public void updatePart(int prtID, hod2v1_Part prt, Space space = Space.Self)
    {
        if (space == Space.Self)
        {
            parts[prtID].transform.localPosition = prt.position;
            parts[prtID].transform.localRotation = prt.rotation;
            parts[prtID].transform.localScale = prt.scale;
        }
        else
        {
            parts[prtID].transform.position = prt.position;
            parts[prtID].transform.rotation = prt.rotation;
            parts[prtID].transform.localScale = prt.scale;
        }
    }    
    public void updatePart(int AnimID, int HodID, int prtID, hod2v1_Part prt, Space space = Space.Self)
    {
        if (space == Space.Self)
        {
            parts[prtID].transform.localPosition = prt.position;
            parts[prtID].transform.localRotation = prt.rotation;
            parts[prtID].transform.localScale = prt.scale;
        }
        else
        {
            parts[prtID].transform.position = prt.position;
            parts[prtID].transform.rotation = prt.rotation;
            parts[prtID].transform.localScale = prt.scale;
        }
        updatePart(AnimID, HodID, prtID);
        
    }

    public void updateConstraints(int AnimID, int HodID, int prtID, Quaternion c1, Quaternion c2, Quaternion c3)
    {
        if (ani != null)
        {
            hod2v1_Part prt = ani.animations[AnimID].frames[HodID].parts[prtID];
            prt.unk1 = c1;
            prt.unk2 = c2;
            prt.unk3 = c3;
            ani.animations[AnimID].frames[HodID].parts[prtID] = prt;
        }
    }
    public void addPart(string partName, int parent)
    {
        string error;
        if (!TryAddPart(partName, parent, out error))
            ReportBlockedStructureEdit(error);
    }

    public bool TryAddPart(string partName, int parent, out string error)
    {
        string warning;
        if (!CanEditStructure(out warning))
        {
            error = warning;
            return false;
        }
        if (ani != null)
        {
            if (!ani.TryAddPart(partName, parent, out error))
                return false;
            buildStructure(ani.structure);
        }
        else
        {
            for (int i = 0; i < hod.parts.Count; i++)
            {
                hod2v0_Part prt = hod.parts[i];
                prt.position = parts[parent].transform.localPosition;
                prt.rotation = parts[parent].transform.localRotation;
                prt.scale = parts[parent].transform.localScale;
            }
            int level = hod.parts[parent].treeDepth + 1;
            hod2v0_Part pHod = hod.parts[parent];
            pHod.childCount++;
            hod.parts[parent] = pHod;
            hod2v0_Part nPart = new hod2v0_Part();
            nPart.name = partName;
            nPart.treeDepth = level;
            nPart.flag = 1;
            nPart.unk = new Vector3(1, 1, 1);
            nPart.position = new Vector3(0, 0, 0);
            nPart.rotation = new Quaternion();
            nPart.scale = new Vector3(1, 1, 1);
            int j = parent + 1;
            for (; j < hod.parts.Count; j++)
            {
                if (hod.parts[j].treeDepth <= hod.parts[parent].treeDepth)
                {
                    break;
                }
            }
            hod.parts.Insert(j, nPart);

            buildStructure(hod);
        }

        error = "";
        return true;
    }

    public bool removePart(int index)
    {
        string error;
        bool result = TryRemovePart(index, out error);
        if (!result && !string.IsNullOrEmpty(error))
            ReportBlockedStructureEdit(error);
        return result;
    }

    public bool TryRemovePart(int index, out string error)
    {
        string warning;
        if (!CanEditStructure(out warning))
        {
            error = warning;
            return false;
        }
        if (hod == null || hod.parts == null || index <= 0 || index >= hod.parts.Count)
        {
            error = UILocalization.Get(
                UILocalizationKeys.PartDataInvalidDelete,
                "パーツ情報の整合性を確認できないため削除できません。");
            return false;
        }

        if (ani != null)
        {
            if (ani.TryRemovePart(index, out error))
                buildStructure(ani.structure);
            else
                return false;

            error = "";
            return true;
        }

        int parentIndex = FindParentIndex(hod.parts, index);
        if (parentIndex < 0)
        {
            error = UILocalization.Get(
                UILocalizationKeys.PartDataInvalidDelete,
                "パーツ情報の整合性を確認できないため削除できません。");
            return false;
        }

        int removeCount = GetSubtreeEndIndex(hod.parts, index) - index;
        if (removeCount <= 0)
        {
            error = UILocalization.Get(
                UILocalizationKeys.PartDataInvalidDelete,
                "パーツ情報の整合性を確認できないため削除できません。");
            return false;
        }

        int transformCount = Math.Min(hod.parts.Count, parts.Count);
        for (int i = 0; i < transformCount; i++)
        {
            hod2v0_Part prt = hod.parts[i];
            prt.position = parts[i].transform.localPosition;
            prt.rotation = parts[i].transform.localRotation;
            prt.scale = parts[i].transform.localScale;
            hod.parts[i] = prt;
        }

        hod2v0_Part parentPart = hod.parts[parentIndex];
        parentPart.childCount--;
        hod.parts[parentIndex] = parentPart;
        hod.parts.RemoveRange(index, removeCount);

        buildStructure(hod);
        error = "";
        return true;
    }

    public bool CanEditStructure(out string warning)
    {
        ValidateStructureForEditing(hod, false);
        warning = structureValidationWarning;
        return structureEditingAllowed;
    }

    public bool TryCreateLegacyStructureEditPlan(
        out LegacyAniStructureEditPlan plan,
        out string error)
    {
        return LegacyAniStructureEditPlan.TryCreate(ani, out plan, out error);
    }

    public bool TryApplyLegacyStructureEditPlan(
        LegacyAniStructureEditPlan plan,
        LegacyAniHierarchyAuthority authority,
        out string error)
    {
        if (plan == null)
        {
            error = UILocalization.Get(
                "hod.legacy_edit.no_plan",
                "旧ANIの構造編集準備がありません。");
            return false;
        }

        if (!plan.TryApply(ani, authority, out error))
            return false;

        structureEditingBlockReason = "";
        ValidateStructureForEditing(ani.structure, false);
        if (!structureEditingAllowed)
        {
            error = structureValidationWarning;
            return false;
        }

        hod = ani.structure;
        error = "";
        return true;
    }

    public void SetStructureEditingBlockReason(string reason)
    {
        structureEditingBlockReason = reason ?? "";
    }

    void ValidateStructureForEditing(hod2v0 structure, bool reportWarning)
    {
        if (!string.IsNullOrEmpty(structureEditingBlockReason))
        {
            structureEditingAllowed = false;
            structureValidationWarning = structureEditingBlockReason;
            if (reportWarning)
                ReportBlockedStructureEdit(structureValidationWarning);
            return;
        }

        string details = "";
        structureEditingAllowed = structure != null &&
            HodHierarchyValidator.TryValidate(structure.parts, out details);

        if (structureEditingAllowed)
        {
            structureValidationWarning = "";
            return;
        }

        if (structure == null)
            details = "HOD構造データがありません。";

        structureValidationWarning =
            "HODのパーツ階層に不整合があります。読み込みと表示は継続しますが、パーツの追加・削除はできません。\n" +
            details;

        if (reportWarning)
            ReportBlockedStructureEdit(structureValidationWarning);
    }

    void ReportBlockedStructureEdit(string warning)
    {
        Debug.LogWarning("[RoboStructure] " + warning);
        if (statusMessege != null)
            statusMessege.text = warning;
    }

    void DestroyExistingStructureObjects()
    {
        if (parts == null || parts.Count == 0)
        {
            if (root != null)
                GameObject.Destroy(root);
            root = null;
            return;
        }

        HashSet<Transform> knownTransforms = new HashSet<Transform>();
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] != null)
                knownTransforms.Add(parts[i].transform);
        }

        HashSet<GameObject> destroyedRoots = new HashSet<GameObject>();
        for (int i = 0; i < parts.Count; i++)
        {
            GameObject part = parts[i];
            if (part == null)
                continue;

            Transform parent = part.transform.parent;
            if ((parent == null || !knownTransforms.Contains(parent))
                && destroyedRoots.Add(part))
            {
                GameObject.Destroy(part);
            }
        }

        if (root != null && destroyedRoots.Add(root))
            GameObject.Destroy(root);

        root = null;
        parts.Clear();
    }

    public void renamePart(int index, string name)
    {
        
        if (ani != null)
        {
            hod2v0_Part prt = ani.structure.parts[index];
            prt.name = name;
            ani.structure.parts[index] = prt;
            buildStructure(ani.structure);
        }
        else
        {
            hod2v0_Part prt = hod.parts[index];
            prt.name = name;
            hod.parts[index] = prt;
            buildStructure(hod);
        }
        
    }

    public hod2v1 createHod2v1()
    {
        hod2v1 Pose = new hod2v1("Copy");
        Pose.parts = new List<hod2v1_Part>();
        for (int i = 0; i < parts.Count; i++)
        {
            hod2v1_Part prt = new hod2v1_Part();
            prt.position = parts[i].transform.localPosition;
            prt.rotation = parts[i].transform.localRotation;
            prt.scale = parts[i].transform.localScale;
            prt.unk1 = parts[i].transform.localRotation;
            prt.unk2 = parts[i].transform.localRotation;
            prt.unk3 = parts[i].transform.localRotation;
            Pose.parts.Add(prt);

        }
        return Pose;
    }


    public void saveAni()
    {
        ani.save();
    }

    public void saveHOD1()
    {
        hod1 sHOD = new hod1("");
        for (int i = 0; i < parts.Count; i++)
        {
            hod2v0_Part prt = hod.parts[i];
            prt.position = parts[i].transform.localPosition;
            prt.rotation = parts[i].transform.localRotation;
            prt.scale = parts[i].transform.localScale;
            hod.parts[i] = prt;
        }
        sHOD.createFromHod2v0(hod);
        BinaryWriter bw = new BinaryWriter(File.Open(filename, FileMode.OpenOrCreate, FileAccess.ReadWrite));
        sHOD.saveToBinary(ref bw);
        bw.Close();
    }

    public void saveHOD1(string customFilename)
    {
        string dText = "HODファイルを書き出す";
        dText = customFilename;
        inputBox.openDialog(UILocalization.Get(UILocalizationKeys.HodNewName, "新しいHODファイルの名前を入力してください（拡張子.hodは除く）。"), dText , (string rText) =>
        {
            hod1 sHOD = new hod1("");
            for (int i = 0; i < parts.Count; i++)
            {
                hod2v0_Part prt = hod.parts[i];
                prt.position = parts[i].transform.localPosition;
                prt.rotation = parts[i].transform.localRotation;
                prt.scale = parts[i].transform.localScale;
                hod.parts[i] = prt;
            }
            sHOD.createFromHod2v0(hod);
            rText = rText + ".hod";
            string savePath = Path.Combine(folder, rText);
            BinaryWriter bw = new BinaryWriter(File.Open(savePath, FileMode.OpenOrCreate, FileAccess.ReadWrite));
            sHOD.saveToBinary(ref bw);
            bw.Close();
         });
    }

    public void LoadHOD()
    {
        // folder内の.hodファイルを取得
        string[] hodFiles = Directory.GetFiles(folder, "*.hod");
        if (hodFiles.Length == 0)
        {
            Debug.LogError($"フォルダ内にHODファイルが見つかりません: {folder}");
            statusMessege.text = UILocalization.Get(UILocalizationKeys.HodNotFound, "フォルダ内にHODファイルが見つかりません: {0}", folder);
            return;
        }

        // ファイル選択ダイアログを表示
        List<string> fileNames = hodFiles.Select(f => Path.GetFileName(f)).ToList();
        inputBox.openSelectDialog(UILocalization.Get(UILocalizationKeys.HodSelect, "読み込むHODファイルを選択してください"), 
            fileNames,
            (string selectedFile) =>
            {
                string hodFilePath = Path.Combine(folder, selectedFile);
                try
                {
                    BinaryReader br = new BinaryReader(File.Open(hodFilePath, FileMode.Open, FileAccess.Read));
                    try
                    {
                        // シグネチャを確認
                        string signature = new string(br.ReadChars(3));
                        br.BaseStream.Seek(0, SeekOrigin.Begin); // ストリームを先頭に戻す

                        hod2v1 nFrame = new hod2v1(selectedFile);
                        nFrame.parts = new List<hod2v1_Part>();

                        if (signature == "HOD")
                        {
                            // hod1形式の場合
                            hod1 oldStructure = new hod1(selectedFile);
                            if (oldStructure.loadFromBinary(ref br))
                            {
                                // hod2v0形式に変換
                                hod2v0 convertedHOD = oldStructure.convertToHod2v0();
                                // パーツ情報をコピー
                                for (int i = 0; i < convertedHOD.parts.Count; i++)
                                {
                                    hod2v1_Part nPart = new hod2v1_Part();
                                    nPart.name = convertedHOD.parts[i].name;
                                    nPart.treeDepth = convertedHOD.parts[i].treeDepth;
                                    nPart.childCount = convertedHOD.parts[i].childCount;
                                    nPart.position = convertedHOD.parts[i].position;
                                    nPart.rotation = convertedHOD.parts[i].rotation;
                                    nPart.scale = convertedHOD.parts[i].scale;
                                    nPart.unk1 = nPart.rotation;
                                    nPart.unk2 = nPart.rotation;
                                    nPart.unk3 = nPart.rotation;
                                    nFrame.parts.Add(nPart);
                                }
                            }
                            else
                            {
                                Debug.LogError($"HODファイルの読み込みに失敗しました: {selectedFile}");
                                statusMessege.text = UILocalization.Get(UILocalizationKeys.HodLoadFailed, "HODファイルの読み込みに失敗しました: {0}", selectedFile);
                                return;
                            }
                        }
                        else
                        {
                            // hod2v0形式の場合
                            hod2v0 loadedHOD = new hod2v0(selectedFile);
                            if (loadedHOD.loadFromBinary(ref br))
                            {
                                // パーツ情報をコピー
                                for (int i = 0; i < loadedHOD.parts.Count; i++)
                                {
                                    hod2v1_Part nPart = new hod2v1_Part();
                                    nPart.name = loadedHOD.parts[i].name;
                                    nPart.treeDepth = loadedHOD.parts[i].treeDepth;
                                    nPart.childCount = loadedHOD.parts[i].childCount;
                                    nPart.position = loadedHOD.parts[i].position;
                                    nPart.rotation = loadedHOD.parts[i].rotation;
                                    nPart.scale = loadedHOD.parts[i].scale;
                                    nPart.unk1 = nPart.rotation;
                                    nPart.unk2 = nPart.rotation;
                                    nPart.unk3 = nPart.rotation;
                                    nFrame.parts.Add(nPart);
                                }
                            }
                            else
                            {
                                Debug.LogError($"HODファイルの読み込みに失敗しました: {selectedFile}");
                                statusMessege.text = UILocalization.Get(UILocalizationKeys.HodLoadFailed, "HODファイルの読み込みに失敗しました: {0}", selectedFile);
                                return;
                            }
                        }

                        // パーツ数の比較
                        if (nFrame.parts.Count != parts.Count)
                        {
                            Debug.LogWarning($"読み込んだHODファイルのパーツ数({nFrame.parts.Count})が現在のパーツ数({parts.Count})と異なります。読み込みを中止します。");
                            statusMessege.text = UILocalization.Get(UILocalizationKeys.HodPartCountMismatch, "読み込んだHODファイルのパーツ数({0})が現在のパーツ数({1})と異なります。読み込みを中止します。", nFrame.parts.Count, parts.Count);
                            return;
                        }
                        
                        // UI_EditAniから現在の選択位置を取得
                        UI_EditAni editAni = FindObjectOfType<UI_EditAni>();
                        if (editAni != null)
                        {
                            int currentAnimIndex = 0; // 現在選択されているアニメーションのインデックス
                            int currentHodIndex = 0; // 現在選択されているHODのインデックス
                            
                            currentAnimIndex = editAni.animDD.value;
                            currentHodIndex = editAni.hodDD.value;
                            
                            // 現在のアニメーションのフレームリストに新しいフレームを追加
                            if (currentAnimIndex < ani.animations.Count)
                            {
                                if (ani.animations[currentAnimIndex].frames.Count > 0)
                                {
                                    ani.animations[currentAnimIndex].frames.Insert(currentHodIndex + 1, nFrame);
                                }
                                else
                                {
                                    ani.animations[currentAnimIndex].frames.Add(nFrame);
                                }

                                // HODリストを更新
                                editAni.populateHODList();
                            }
                        }
                    }
                    finally
                    {
                        br.Close();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"HODファイルの読み込み中にエラーが発生しました: {e.Message}");
                    statusMessege.text = UILocalization.Get(UILocalizationKeys.HodLoadException, "HODファイルの読み込み中にエラーが発生しました: {0}", e.Message);
                }
            });
    }
    


    static int GetSubtreeEndIndex(IList<hod2v0_Part> parts, int index)
    {
        int subtreeDepth = parts[index].treeDepth;
        int endIndex = index + 1;
        while (endIndex < parts.Count && parts[endIndex].treeDepth > subtreeDepth)
            endIndex++;
        return endIndex;
    }


    static int FindParentIndex(IList<hod2v0_Part> parts, int index)
    {
        int parentDepth = parts[index].treeDepth - 1;
        for (int i = index - 1; i >= 0; i--)
        {
            if (parts[i].treeDepth == parentDepth)
                return i;
        }
        return -1;
    }
}
