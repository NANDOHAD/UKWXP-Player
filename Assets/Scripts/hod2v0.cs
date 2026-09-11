using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public struct hod2v0_Part
{
    public int treeDepth;
    public int childCount;
    public string name;
    public Quaternion rotation;
    public Vector3 scale;
    public Vector3 position;
    public byte flag;
    public Vector3 unk;
}

public class hod2v0
{
    public string filename;
    public List<hod2v0_Part> parts;
    public hod2v0(string name)
    {
        filename = name;
    }

    public bool loadFromBinary(ref BinaryReader br)
    {
        
        //data = br.ReadBytes(11 + (partCount * 399));
        string signature = new string(br.ReadChars(3));
        int version = br.ReadInt32();
        if (signature == "HD2" && version == 0)
        {
            parts = new List<hod2v0_Part>();
            int partCount = br.ReadInt32();
            for (int i = 0; i < partCount; i++)
            {
                hod2v0_Part nPart = new hod2v0_Part();
                nPart.treeDepth = br.ReadInt32();
                nPart.childCount = br.ReadInt32();
                nPart.name = ASCIIEncoding.ASCII.GetString(br.ReadBytes(256)).TrimEnd('\0');
                nPart.rotation = new Quaternion();
                nPart.rotation.x = br.ReadSingle();
                nPart.rotation.y = br.ReadSingle();
                nPart.rotation.z = br.ReadSingle();
                nPart.rotation.w = br.ReadSingle();
                nPart.scale = new Vector3();
                nPart.scale.x = br.ReadSingle();
                nPart.scale.y = br.ReadSingle();
                nPart.scale.z = br.ReadSingle();
                nPart.position = new Vector3();
                nPart.position.x = br.ReadSingle();
                nPart.position.y = br.ReadSingle();
                nPart.position.z = br.ReadSingle();
                nPart.flag = br.ReadByte();
                nPart.unk = new Vector3();
                nPart.unk.x = br.ReadSingle();
                nPart.unk.y = br.ReadSingle();
                nPart.unk.z = br.ReadSingle();
                br.BaseStream.Seek(82, SeekOrigin.Current);
                parts.Add(nPart);
            }
        }
        else
            return false;

        return true;
    }

    public void saveToBinary(ref BinaryWriter bw)
    {
        bw.Write(ASCIIEncoding.ASCII.GetBytes("HD2"));
        bw.Write(0);
        bw.Write(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            bw.Write(parts[i].treeDepth);
            bw.Write(parts[i].childCount);
            WriteFixedASCII(bw, parts[i].name, 256);
            bw.Write(parts[i].rotation.x);
            bw.Write(parts[i].rotation.y);
            bw.Write(parts[i].rotation.z);
            bw.Write(parts[i].rotation.w);
            bw.Write(parts[i].scale.x);
            bw.Write(parts[i].scale.y);
            bw.Write(parts[i].scale.z);
            bw.Write(parts[i].position.x);
            bw.Write(parts[i].position.y);
            bw.Write(parts[i].position.z);
            bw.Write(parts[i].flag);
            bw.Write(parts[i].unk.x);
            bw.Write(parts[i].unk.y);
            bw.Write(parts[i].unk.z);
            bw.BaseStream.Seek(82, SeekOrigin.Current);
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

}

