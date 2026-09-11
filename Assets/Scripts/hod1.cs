using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public struct hod1_Part
{
    public int treeDepth;
    public int childCount;
    public string name;
    public Matrix4x4 transform;
}

public class hod1
{
    string filename;
    public List<hod1_Part> parts;
    public hod1(string name)
    {
        filename = name;
    }
    public bool loadFromBinary(ref BinaryReader br)
    {
        LegacyHodSource ignored;
        return loadFromBinary(ref br, out ignored);
    }

    internal bool loadFromBinary(ref BinaryReader br, out LegacyHodSource legacySource)
    {
        legacySource = new LegacyHodSource();
        byte[] signatureBytes = ReadRequiredBytes(br, 3, "HOD signature");
        string signature = ASCIIEncoding.ASCII.GetString(signatureBytes);
        legacySource.signatureBytes = signatureBytes;
        if (signature != "HOD")
        {
            Debug.LogWarning("Warning: 署名が 'HOD' ではありません。強制的に 'HOD' として処理を続行します。");
            // ここで署名を「HOD」として扱う
            // 必要に応じて、他の処理を追加することもできます
        }
        
        parts = new List<hod1_Part>();
        int partCount = br.ReadInt32();
        long maximumPartCount = (br.BaseStream.Length - br.BaseStream.Position) / 328L;
        
        if (partCount <= 0 || partCount > maximumPartCount)
        {
            Debug.LogError($"Error: HODパーツ数が不正です: {partCount}");
            return false;
        }

        for (int i = 0; i < partCount; i++)
        {
            hod1_Part nPart = new hod1_Part();
            nPart.treeDepth = br.ReadInt32();
            nPart.childCount = br.ReadInt32();
            byte[] nameBytes = ReadRequiredBytes(br, 256, $"HOD part {i} name");
            string sourceName = ASCIIEncoding.ASCII.GetString(nameBytes).TrimEnd('\0');
            nPart.name = sourceName;

            if (string.IsNullOrEmpty(nPart.name))
            {
                Debug.LogWarning($"Warning: パーツ {i} の名前が空です。仮の名前 'NoNamedParts' を設定します。");
                nPart.name = "NoNamedParts";
            }

            byte[] matrixBytes = ReadRequiredBytes(br, 64, $"HOD part {i} matrix");
            nPart.transform = new Matrix4x4();
            nPart.transform.m00 = BitConverter.ToSingle(matrixBytes, 0);
            nPart.transform.m10 = BitConverter.ToSingle(matrixBytes, 4);
            nPart.transform.m20 = BitConverter.ToSingle(matrixBytes, 8);
            nPart.transform.m30 = BitConverter.ToSingle(matrixBytes, 12);
            nPart.transform.m01 = BitConverter.ToSingle(matrixBytes, 16);
            nPart.transform.m11 = BitConverter.ToSingle(matrixBytes, 20);
            nPart.transform.m21 = BitConverter.ToSingle(matrixBytes, 24);
            nPart.transform.m31 = BitConverter.ToSingle(matrixBytes, 28);
            nPart.transform.m02 = BitConverter.ToSingle(matrixBytes, 32);
            nPart.transform.m12 = BitConverter.ToSingle(matrixBytes, 36);
            nPart.transform.m22 = BitConverter.ToSingle(matrixBytes, 40);
            nPart.transform.m32 = BitConverter.ToSingle(matrixBytes, 44);
            nPart.transform.m03 = BitConverter.ToSingle(matrixBytes, 48);
            nPart.transform.m13 = BitConverter.ToSingle(matrixBytes, 52);
            nPart.transform.m23 = BitConverter.ToSingle(matrixBytes, 56);
            nPart.transform.m33 = BitConverter.ToSingle(matrixBytes, 60);
            parts.Add(nPart);

            LegacyHodPartSource sourcePart = new LegacyHodPartSource();
            sourcePart.nameBytes = nameBytes;
            sourcePart.modelName = nPart.name;
            sourcePart.matrixBytes = matrixBytes;
            sourcePart.position = Utils.GetPosition(nPart.transform);
            sourcePart.rotation = Utils.GetRotation(nPart.transform);
            sourcePart.scale = Utils.GetScale(nPart.transform);
            legacySource.parts.Add(sourcePart);
        }

        return true;
    }

    public void saveToBinary(ref BinaryWriter bw)
    {
        bw.Write(ASCIIEncoding.ASCII.GetBytes("HOD"));
        bw.Write(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            bw.Write(parts[i].treeDepth);
            bw.Write(parts[i].childCount);
            WriteFixedASCII(bw, parts[i].name, 256);
            bw.Write(parts[i].transform.m00);
            bw.Write(parts[i].transform.m10);
            bw.Write(parts[i].transform.m20);
            bw.Write(parts[i].transform.m30);
            bw.Write(parts[i].transform.m01);
            bw.Write(parts[i].transform.m11);
            bw.Write(parts[i].transform.m21);
            bw.Write(parts[i].transform.m31);
            bw.Write(parts[i].transform.m02);
            bw.Write(parts[i].transform.m12);
            bw.Write(parts[i].transform.m22);
            bw.Write(parts[i].transform.m32);
            bw.Write(parts[i].transform.m03);
            bw.Write(parts[i].transform.m13);
            bw.Write(parts[i].transform.m23);
            bw.Write(parts[i].transform.m33);
        }
    }

    static void WriteFixedASCII(BinaryWriter bw, string value, int byteLength)
    {
        byte[] text = ASCIIEncoding.ASCII.GetBytes(value ?? "");
        if (text.Length > byteLength)
            throw new InvalidDataException($"Fixed string is too long: {text.Length}/{byteLength} bytes.");

        bw.Write(text);
        for (int i = text.Length; i < byteLength; i++)
            bw.Write((byte)0);
    }

    internal static void SaveFromHod2v0(
        ref BinaryWriter bw,
        hod2v0 hod,
        LegacyHodSource legacySource)
    {
        if (hod == null || hod.parts == null)
            throw new InvalidDataException("Legacy ANI structure HOD is missing.");

        bw.Write(ASCIIEncoding.ASCII.GetBytes("HOD"));
        bw.Write(hod.parts.Count);
        for (int i = 0; i < hod.parts.Count; i++)
        {
            hod2v0_Part part = hod.parts[i];
            LegacyHodPartSource sourcePart = GetSourcePart(legacySource, i);
            bw.Write(part.treeDepth);
            bw.Write(part.childCount);
            WriteLegacyPartName(bw, part.name, sourcePart);
            WriteLegacyMatrix(
                bw,
                part.position,
                part.rotation,
                part.scale,
                sourcePart);
        }
    }

    internal static void SaveFromHod2v1(
        ref BinaryWriter bw,
        hod2v1 hod,
        LegacyHodSource legacySource)
    {
        if (hod == null || hod.parts == null)
            throw new InvalidDataException("Legacy ANI frame HOD is missing.");

        bw.Write(ASCIIEncoding.ASCII.GetBytes("HOD"));
        bw.Write(hod.parts.Count);
        for (int i = 0; i < hod.parts.Count; i++)
        {
            hod2v1_Part part = hod.parts[i];
            LegacyHodPartSource sourcePart = GetSourcePart(legacySource, i);
            bw.Write(part.treeDepth);
            bw.Write(part.childCount);
            WriteLegacyPartName(bw, part.name, sourcePart);
            WriteLegacyMatrix(
                bw,
                part.position,
                part.rotation,
                part.scale,
                sourcePart);
        }
    }

    static LegacyHodPartSource GetSourcePart(LegacyHodSource legacySource, int index)
    {
        if (legacySource == null || legacySource.parts == null
            || index < 0 || index >= legacySource.parts.Count)
        {
            return null;
        }

        return legacySource.parts[index];
    }

    static void WriteLegacyPartName(
        BinaryWriter bw,
        string name,
        LegacyHodPartSource sourcePart)
    {
        if (sourcePart != null && name == sourcePart.modelName
            && sourcePart.nameBytes != null && sourcePart.nameBytes.Length == 256)
        {
            bw.Write(sourcePart.nameBytes);
            return;
        }

        WriteFixedASCII(bw, name, 256);
    }

    static void WriteLegacyMatrix(
        BinaryWriter bw,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        LegacyHodPartSource sourcePart)
    {
        if (sourcePart != null
            && LegacyAniSourceValues.VectorExactlyEquals(position, sourcePart.position)
            && LegacyAniSourceValues.QuaternionExactlyEquals(rotation, sourcePart.rotation)
            && LegacyAniSourceValues.VectorExactlyEquals(scale, sourcePart.scale)
            && sourcePart.matrixBytes != null && sourcePart.matrixBytes.Length == 64)
        {
            bw.Write(sourcePart.matrixBytes);
            return;
        }

        Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
        bw.Write(matrix.m00);
        bw.Write(matrix.m10);
        bw.Write(matrix.m20);
        bw.Write(matrix.m30);
        bw.Write(matrix.m01);
        bw.Write(matrix.m11);
        bw.Write(matrix.m21);
        bw.Write(matrix.m31);
        bw.Write(matrix.m02);
        bw.Write(matrix.m12);
        bw.Write(matrix.m22);
        bw.Write(matrix.m32);
        bw.Write(matrix.m03);
        bw.Write(matrix.m13);
        bw.Write(matrix.m23);
        bw.Write(matrix.m33);
    }

    static byte[] ReadRequiredBytes(BinaryReader br, int byteCount, string field)
    {
        byte[] bytes = br.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
            throw new EndOfStreamException($"Unexpected end of file while reading {field}.");
        return bytes;
    }

    public hod2v0 convertToHod2v0()
    {
        hod2v0 hod = new hod2v0(filename);
        hod.parts = new List<hod2v0_Part>();
        for (int i = 0; i < parts.Count; i++)
        {
            hod2v0_Part nPart = new hod2v0_Part();
            nPart.treeDepth = parts[i].treeDepth;
            nPart.childCount = parts[i].childCount;
            nPart.name = parts[i].name;
            nPart.rotation = Utils.GetRotation(parts[i].transform);
            nPart.scale = Utils.GetScale(parts[i].transform);
            nPart.position = Utils.GetPosition(parts[i].transform);
            nPart.flag = 1;
            nPart.unk = new Vector3(1.0f, 1.0f, 1.0f);
            hod.parts.Add(nPart);  
        }
        return hod;
    }

    public hod2v1 convertToHod2v1()
    {
        hod2v1 hod = new hod2v1(filename);
        hod.parts = new List<hod2v1_Part>();
        for (int i = 0; i < parts.Count; i++)
        {
            hod2v1_Part nPart = new hod2v1_Part();
            nPart.treeDepth = parts[i].treeDepth;
            nPart.childCount = parts[i].childCount;
            nPart.name = parts[i].name;
            nPart.rotation = Utils.GetRotation(parts[i].transform);
            nPart.scale = Utils.GetScale(parts[i].transform);
            nPart.position = Utils.GetPosition(parts[i].transform);
            nPart.unk1 = Utils.GetRotation(parts[i].transform);
            nPart.unk2 = Utils.GetRotation(parts[i].transform);
            nPart.unk3 = Utils.GetRotation(parts[i].transform);
            hod.parts.Add(nPart);
        }
        return hod;
    }

    public void createFromHod2v0(hod2v0 hod)
    {
        filename = hod.filename;
        parts = new List<hod1_Part>();
        for (int i = 0; i < hod.parts.Count; i++)
        {
            hod1_Part nPart = new hod1_Part();
            nPart.name = hod.parts[i].name;
            nPart.treeDepth = hod.parts[i].treeDepth;
            nPart.childCount = hod.parts[i].childCount;
            nPart.transform.SetTRS(hod.parts[i].position, hod.parts[i].rotation, hod.parts[i].scale);
            parts.Add(nPart);
            
        }
    }
    public void createFromHod2v1(hod2v1 hod)
    {
        filename = hod.filename;
        parts = new List<hod1_Part>();
        for (int i = 0; i < hod.parts.Count; i++)
        {
            hod1_Part nPart = new hod1_Part();
            nPart.name = hod.parts[i].name;
            nPart.treeDepth = hod.parts[i].treeDepth;
            nPart.childCount = hod.parts[i].childCount;
            nPart.transform.SetTRS(hod.parts[i].position,hod.parts[i].rotation,hod.parts[i].scale);
            parts.Add(nPart);
        }
    }
}
