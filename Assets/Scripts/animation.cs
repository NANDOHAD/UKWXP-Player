using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;
public struct script
{
    public int unk;
    public float time;
    public string squirrel;
    // Follow the script value through copies/reordering, independently of timing edits.
    internal AniScriptTextSource sourceText;
}

internal sealed class AniScriptTextSource
{
    readonly string modelText;
    readonly byte[] bytes;

    public AniScriptTextSource(byte[] raw, string decoded)
    {
        bytes = (byte[])raw.Clone();
        modelText = decoded;
    }

    public static string Decode(byte[] raw)
    {
        try { return USEncoder.ToEncoding.ToUnicode(raw); }
        catch (System.IndexOutOfRangeException)
        {
            // A dangling lead byte in a comment must not prevent opening the file.
            // The snapshot still owns the original bytes for unchanged legacy saves.
            return USEncoder.ToEncoding.ToUnicode(MakeCommentsSafe(raw));
        }
    }

    public static byte[] GetBytes(string text, AniScriptTextSource source, bool normalizeLines,
        bool originalCompatible = true)
    {
        // USEncoder cannot round-trip every original byte (e.g. EB -> U+0000).
        // Unedited source bytes are authoritative, including comments and line endings.
        if (source != null && text == source.modelText)
            return originalCompatible ? MakeCommentsSafe(source.bytes) : source.bytes;
        byte[] encoded = normalizeLines
            ? LegacyAniSourceValues.EncodeScriptText(text)
            : USEncoder.ToEncoding.ToSJIS(text ?? "");
        return MakeCommentsSafe(encoded);
    }

    static bool IsLead(byte value) => (value >= 0x81 && value <= 0x9f)
        || (value >= 0xe0 && value <= 0xfc);
    static bool IsTrail(byte value) => (value >= 0x40 && value <= 0x7e)
        || (value >= 0x80 && value <= 0xfc);

    static byte[] MakeCommentsSafe(byte[] input)
    {
        var output = new List<byte>(input.Length);
        bool comment = false, quoted = false;
        for (int i = 0; i < input.Length; i++)
        {
            byte value = input[i];
            if (comment)
            {
                // FUN_00583760 skips SJIS lead + trail, and ends a comment only
                // at CRLF. Invalid leads must not swallow the following newline.
                if (value == 0) { output.Add((byte)' '); continue; }
                if (value == '\r' || value == '\n')
                {
                    if (value == '\r' && i + 1 < input.Length && input[i + 1] == '\n') i++;
                    output.Add(13); output.Add(10); comment = false;
                    continue;
                }
                if (IsLead(value))
                {
                    if (i + 1 < input.Length && IsTrail(input[i + 1]))
                    { output.Add(value); output.Add(input[++i]); }
                    else output.Add((byte)'?');
                    continue;
                }
                output.Add(value);
                continue;
            }

            // Do not treat an apostrophe in a quoted filename as a comment, or
            // a SJIS trail byte as a quote/backslash. Commands are never repaired.
            if (value == 0)
            {
                bool terminal = !quoted;
                for (int j = i; j < input.Length; j++) terminal &= input[j] == 0;
                if (!terminal)
                    throw new InvalidDataException("スクリプトの命令または引用文字列に変換できない文字があります。コメント以外は自動修正できません。");
                output.AddRange(new byte[input.Length - i]);
                break;
            }
            output.Add(value);
            if (IsLead(value) && i + 1 < input.Length && IsTrail(input[i + 1]))
            { output.Add(input[++i]); continue; }
            if (quoted && value == '\\' && i + 1 < input.Length
                && (input[i + 1] == '"' || input[i + 1] == '\\'))
            { output.Add(input[++i]); continue; }
            if (value == '"') quoted = !quoted;
            else if (!quoted && value == '\'') comment = true;
        }
        return output.ToArray();
    }

    public static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; i++)
            if (left[i] != right[i]) return false;
        return true;
    }
}

public class animation
{
    public string name;
    public string squirrelInit = "";
    internal AniScriptTextSource initialSourceText;
    public List<hod2v1> frames;
    public List<script> scripts;
    public void loadFromAni(ref BinaryReader br, ref hod2v0 structure)
    {
        frames = new List<hod2v1>();
        scripts = new List<script>();

        //load Name
        //Encoding ShiftJis = Encoding.GetEncoding(932);
        long position = br.BaseStream.Position;
        try
        {
            name = USEncoder.ToEncoding.ToUnicode(br.ReadBytes(256)).TrimEnd('\0');
            //Debug.Log(name);
        }
        catch
        {
            br.BaseStream.Seek(position + 256, SeekOrigin.Begin);
        }

        //Read Initial Script
        int textLength = br.ReadInt32();
        byte[] initialBytes = LegacyAniSourceValues.ReadRequiredBytes(br, textLength, "AN2 initial script");
        squirrelInit = AniScriptTextSource.Decode(initialBytes);
        initialSourceText = new AniScriptTextSource(initialBytes, squirrelInit);

        //Read Hod Files
        int hodCount = br.ReadInt32();
        for (int i = 0; i < hodCount; i++)
        {
            short nameLength = br.ReadInt16();
            hod2v1 nHod = new hod2v1(USEncoder.ToEncoding.ToUnicode(br.ReadBytes(nameLength)));
            nHod.loadFromBinary(ref br, ref structure);
            frames.Add(nHod);
        }

        //Read Script Files
        int scriptCount = br.ReadInt32();
        for (int i = 0; i < scriptCount; i++)
        {
            script ns = new script();
            ns.unk = br.ReadInt32();
            ns.time = br.ReadSingle();
            textLength = br.ReadInt32();
            byte[] textBytes = LegacyAniSourceValues.ReadRequiredBytes(br, textLength, "AN2 script text");
            ns.squirrel = AniScriptTextSource.Decode(textBytes);
            ns.sourceText = new AniScriptTextSource(textBytes, ns.squirrel);
            scripts.Add(ns);
        }
    }

    public void loadFromAniOld(ref BinaryReader br)
    {
        LegacyAnimationSource ignored;
        loadFromAniOld(ref br, out ignored);
    }

    internal void loadFromAniOld(
        ref BinaryReader br,
        out LegacyAnimationSource legacySource)
    {
        frames = new List<hod2v1>();
        scripts = new List<script>();
        legacySource = new LegacyAnimationSource();

        //load Name
        //Encoding ShiftJis = Encoding.GetEncoding(932);
        legacySource.name = LegacyAniSourceValues.ReadFixedSjis(
            br, 256, "legacy animation name");
        name = legacySource.name.modelValue;

        //Read Hod Files
        int hodCount = br.ReadInt32();
        ValidateCount(hodCount, 100000, "legacy animation HOD count");
        for (int i = 0; i < hodCount; i++)
        {
            LegacyFixedStringSource frameName = LegacyAniSourceValues.ReadFixedSjis(
                br, 30, $"legacy animation HOD {i} filename");
            hod1 nHod = new hod1(frameName.modelValue);
            LegacyHodSource hodSource;
            if (!nHod.loadFromBinary(ref br, out hodSource))
                throw new InvalidDataException($"Legacy animation HOD {i} is invalid.");
            frames.Add(nHod.convertToHod2v1());
            legacySource.frameNames.Add(frameName);
            legacySource.frames.Add(hodSource);
        }

        //Read Script Files
        int scriptCount = br.ReadInt32();
        ValidateCount(scriptCount, 100000, "legacy animation script count");
        for (int i = 0; i < scriptCount; i++)
        {
            script ns = new script();
            ns.unk = br.ReadInt32();
            ns.time = br.ReadSingle();
            int textLength = br.ReadInt32();
            if (textLength < 0 || textLength > br.BaseStream.Length - br.BaseStream.Position)
                throw new InvalidDataException($"Legacy animation script {i} has an invalid text length: {textLength}.");
            byte[] textBytes = LegacyAniSourceValues.ReadRequiredBytes(
                br, textLength, $"legacy animation script {i} text");
            ns.squirrel = AniScriptTextSource.Decode(textBytes);
            ns.sourceText = new AniScriptTextSource(textBytes, ns.squirrel);
            scripts.Add(ns);
            legacySource.scripts.Add(new LegacyScriptSource
            {
                unk = ns.unk,
                time = ns.time,
                modelText = ns.squirrel,
                textBytes = textBytes
            });
        }
    }

    static void ValidateCount(int count, int maximum, string field)
    {
        if (count < 0 || count > maximum)
            throw new InvalidDataException($"Invalid {field}: {count}.");
    }

    public void saveToAni(ref BinaryWriter bw)
    {
        //Encoding ShiftJis = Encoding.GetEncoding(932);
        WriteFixedSJIS(bw, name, 256);
        byte[] shiftjistext = AniScriptTextSource.GetBytes(squirrelInit, initialSourceText, false);
        bw.Write(shiftjistext.Length);
        if (shiftjistext.Length > 0)
            bw.Write(shiftjistext);
        bw.Write(frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            frames[i].saveToBinary(ref bw);
        }

        bw.Write(scripts.Count);
        for (int i = 0; i < scripts.Count; i++)
        {
            bw.Write(scripts[i].unk);
            bw.Write(scripts[i].time);
            byte[] textBytes = AniScriptTextSource.GetBytes(
                scripts[i].squirrel, scripts[i].sourceText, true);
            bw.Write(textBytes.Length);
            bw.Write(textBytes);
        }
    }

    internal void saveToAniOld(
        ref BinaryWriter bw,
        LegacyAnimationSource legacySource)
    {
        LegacyAniSourceValues.WriteFixedSjis(
            bw, name, 256, legacySource != null ? legacySource.name : null);
        bw.Write(frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            LegacyFixedStringSource frameNameSource = legacySource != null
                && i < legacySource.frameNames.Count
                ? legacySource.frameNames[i]
                : null;
            LegacyHodSource hodSource = legacySource != null
                && i < legacySource.frames.Count
                ? legacySource.frames[i]
                : null;
            LegacyAniSourceValues.WriteFixedSjis(
                bw, frames[i].filename, 30, frameNameSource);
            hod1.SaveFromHod2v1(ref bw, frames[i], hodSource);
        }

        bw.Write(scripts.Count);
        for (int i = 0; i < scripts.Count; i++)
        {
            script current = scripts[i];
            bw.Write(current.unk);
            bw.Write(current.time);

            byte[] textBytes = AniScriptTextSource.GetBytes(current.squirrel, current.sourceText, true, false);

            bw.Write(textBytes.Length);
            if (textBytes.Length > 0)
                bw.Write(textBytes);
        }
    }

    static void WriteFixedSJIS(BinaryWriter bw, string value, int byteLength)
    {
        byte[] text = USEncoder.ToEncoding.ToSJIS(value ?? "");
        if (text.Length > byteLength)
            throw new InvalidDataException($"Fixed string is too long: {text.Length}/{byteLength} bytes.");

        bw.Write(text);
        for (int i = text.Length; i < byteLength; i++)
            bw.Write((byte)0);
    }

    public hod2v1_Part interpolatePart(int frame, int part, float time)
    {
        hod2v1_Part iPart = new hod2v1_Part();
        if (frame < frames.Count - 1)
        {
            iPart.position = Vector3.Lerp(frames[frame].parts[part].position, frames[frame + 1].parts[part].position, time);
            iPart.rotation = Quaternion.Lerp(frames[frame].parts[part].rotation, frames[frame + 1].parts[part].rotation, time);
            iPart.scale = Vector3.Lerp(frames[frame].parts[part].scale, frames[frame + 1].parts[part].scale, time);
        }
        else
        {
            iPart.position = frames[frames.Count - 1].parts[part].position;
            iPart.rotation = frames[frames.Count - 1].parts[part].rotation;
            iPart.scale = frames[frames.Count - 1].parts[part].scale;
        }
        return iPart;
    }
}

