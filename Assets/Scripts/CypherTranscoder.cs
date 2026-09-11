using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;

public struct ctFileType
{
    public string fileExt;
    public uint signature;  
}
public class CypherTranscoder
{
    public uint cypher = 0x0B7E7759;
    public List<ctFileType> filetypes = new List<ctFileType>();

    public CypherTranscoder()
    {
        registerFileType(".png", 1196314761);
        registerFileType(".x", 543584120);
    }
        
    public void registerFileType(string fileExt, uint signature)
    {
        ctFileType fileType = new ctFileType();
        fileType.fileExt = fileExt;
        fileType.signature = signature;
        filetypes.Add(fileType);
    }

    public bool findCypher(string name)
    {
        FileInfo fi = new FileInfo(name);
        if (fi.Length < 4)
            return false;

        byte[] bytes = new byte[4];
        using (FileStream fs = File.OpenRead(name))
        {
            if (fs.Read(bytes, 0, bytes.Length) != bytes.Length)
                return false;
        }

        uint signature = BitConverter.ToUInt32(bytes, 0);

        for (int i = 0; i < filetypes.Count; i++)
        {
            if (filetypes[i].fileExt == fi.Extension)
            {
                uint cypherF = filetypes[i].signature ^ signature;
                signature ^= cypherF;
                if (signature == filetypes[i].signature)
                {
                    cypher = cypherF;
                    return true;
                }
                break;
            }
        }
        return false;
    }

    public byte[] Transcode(string name)
    {
        byte[] bytes = File.ReadAllBytes(name);
        byte[] cypherBytes = BitConverter.GetBytes(cypher);
        int fullBlocks = bytes.Length / 4;

        for (int i = 0; i < fullBlocks * 4; i += 4)
        {
            for (int b = 0; b < cypherBytes.Length; b++)
                bytes[i + b] ^= cypherBytes[b];
        }

        return bytes;
    }

    public byte[] Transcode(byte[] bytes)
    {
        //Debug.log($"Starting transcoding for byte array of length: {bytes.Length}");
        
        byte[] cypherBytes = BitConverter.GetBytes(cypher);
        int fullBlocks = bytes.Length / 4; // 4バイトのブロック数

        // Transcode(string) と同じく、4バイト完全ブロックのみを処理する。
        // 端数バイトまで変換すると、Script.spt 保存後の再読み込み時に末尾が復号されず文字化けする。
        for (int i = 0; i < fullBlocks * 4; i += 4)
        {
            for (int b = 0; b < cypherBytes.Length; b++)
            {
                bytes[i + b] ^= cypherBytes[b];
            }
        }

        //Debug.log($"Completed transcoding for byte array of length: {bytes.Length}");
        return bytes;
    }
}
