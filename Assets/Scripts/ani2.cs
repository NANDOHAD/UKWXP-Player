using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using System.Threading.Tasks;

public enum AniContainerFormat
{
    Unknown,
    LegacyAni,
    An2,
    Hod
}

internal sealed class LegacyFixedStringSource
{
    public byte[] bytes;
    public string modelValue;
}

internal sealed class LegacyHodPartSource
{
    public byte[] nameBytes;
    public string modelName;
    public byte[] matrixBytes;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;
}

internal sealed class LegacyHodSource
{
    public byte[] signatureBytes;
    public readonly List<LegacyHodPartSource> parts = new List<LegacyHodPartSource>();
}

internal sealed class LegacyScriptSource
{
    public int unk;
    public float time;
    public string modelText;
    public byte[] textBytes;
}

internal sealed class LegacyAnimationSource
{
    public LegacyFixedStringSource name;
    public readonly List<LegacyFixedStringSource> frameNames = new List<LegacyFixedStringSource>();
    public readonly List<LegacyHodSource> frames = new List<LegacyHodSource>();
    public readonly List<LegacyScriptSource> scripts = new List<LegacyScriptSource>();
}

internal sealed class LegacyIkDataRecord
{
    public const int PartPayloadLength = 13;

    public int payloadLength;
    public byte[] payloadBytes;
    public byte flag;
    public Vector3 unk;

    public bool IsPartPayload => payloadLength == PartPayloadLength
        && payloadBytes != null && payloadBytes.Length == PartPayloadLength;
}

internal sealed class LegacyIkDataSection
{
    static readonly byte[] Signature = ASCIIEncoding.ASCII.GetBytes("IKDATA");

    public readonly List<LegacyIkDataRecord> records = new List<LegacyIkDataRecord>();
    public byte[] trailingBytes = Array.Empty<byte>();
    public bool isPartIndexed;

    public static bool TryParse(
        byte[] data,
        int structurePartCount,
        out LegacyIkDataSection section,
        out string error)
    {
        section = null;
        error = "";
        if (!StartsWith(data, Signature))
        {
            error = "IKDATA署名がありません。";
            return false;
        }

        try
        {
            using (MemoryStream stream = new MemoryStream(data, false))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.ReadBytes(Signature.Length);
                if (stream.Length - stream.Position < sizeof(int))
                    throw new InvalidDataException("IKDATAのレコード数がありません。");

                int recordCount = reader.ReadInt32();
                if (recordCount < 0 || recordCount > 1000000)
                    throw new InvalidDataException($"IKDATAのレコード数が不正です: {recordCount}");

                LegacyIkDataSection parsed = new LegacyIkDataSection();
                bool allPartPayloads = true;
                for (int i = 0; i < recordCount; i++)
                {
                    if (stream.Length - stream.Position < sizeof(int))
                        throw new EndOfStreamException($"IKDATAレコード {i} の長さがありません。");

                    int payloadLength = reader.ReadInt32();
                    long remaining = stream.Length - stream.Position;
                    if (payloadLength < 0 || payloadLength > remaining)
                    {
                        throw new InvalidDataException(
                            $"IKDATAレコード {i} の長さが不正です: {payloadLength}/{remaining}");
                    }

                    byte[] payload = reader.ReadBytes(payloadLength);
                    LegacyIkDataRecord record = new LegacyIkDataRecord
                    {
                        payloadLength = payloadLength,
                        payloadBytes = payload
                    };
                    if (record.IsPartPayload)
                    {
                        record.flag = payload[0];
                        record.unk = new Vector3(
                            BitConverter.ToSingle(payload, 1),
                            BitConverter.ToSingle(payload, 5),
                            BitConverter.ToSingle(payload, 9));
                    }
                    else
                    {
                        allPartPayloads = false;
                    }
                    parsed.records.Add(record);
                }

                int trailingLength = checked((int)(stream.Length - stream.Position));
                parsed.trailingBytes = reader.ReadBytes(trailingLength);
                parsed.isPartIndexed = allPartPayloads
                    && parsed.trailingBytes.Length == 0
                    && parsed.records.Count <= structurePartCount;
                section = parsed;
                return true;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void ApplyTo(List<hod2v0_Part> parts)
    {
        if (!isPartIndexed || parts == null || records.Count > parts.Count)
            return;

        for (int i = 0; i < records.Count; i++)
        {
            hod2v0_Part part = parts[i];
            part.flag = records[i].flag;
            part.unk = records[i].unk;
            parts[i] = part;
        }
    }

    public bool IsEquivalentTo(IList<hod2v0_Part> parts)
    {
        if (!isPartIndexed || parts == null || parts.Count < records.Count)
            return false;

        for (int i = 0; i < records.Count; i++)
        {
            if (parts[i].flag != records[i].flag
                || !LegacyAniSourceValues.VectorExactlyEquals(parts[i].unk, records[i].unk))
            {
                return false;
            }
        }

        Vector3 defaultUnk = Vector3.one;
        for (int i = records.Count; i < parts.Count; i++)
        {
            if (parts[i].flag != 1
                || !LegacyAniSourceValues.VectorExactlyEquals(parts[i].unk, defaultUnk))
            {
                return false;
            }
        }
        return true;
    }

    public static void WritePartIndexed(BinaryWriter writer, IList<hod2v0_Part> parts)
    {
        writer.Write(Signature);
        writer.Write(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            writer.Write(LegacyIkDataRecord.PartPayloadLength);
            writer.Write(parts[i].flag);
            writer.Write(parts[i].unk.x);
            writer.Write(parts[i].unk.y);
            writer.Write(parts[i].unk.z);
        }
    }

    static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data == null || prefix == null || data.Length < prefix.Length)
            return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i])
                return false;
        }
        return true;
    }
}

internal sealed class LegacyAniSource
{
    public LegacyFixedStringSource structureFilename;
    public LegacyHodSource structure;
    public readonly List<LegacyAnimationSource> animations = new List<LegacyAnimationSource>();
    public readonly List<string> structurePartNames = new List<string>();
    public byte[] tailBytes = Array.Empty<byte>();
    public LegacyIkDataSection ikData;

    public bool HasTailData => tailBytes != null && tailBytes.Length > 0;

    public bool TailStartsWithIkData
    {
        get
        {
            byte[] signature = ASCIIEncoding.ASCII.GetBytes("IKDATA");
            if (tailBytes == null || tailBytes.Length < signature.Length)
                return false;
            for (int i = 0; i < signature.Length; i++)
            {
                if (tailBytes[i] != signature[i])
                    return false;
            }
            return true;
        }
    }
}

internal sealed class AniFramePartsEdit
{
    public hod2v1 frame;
    public List<hod2v1_Part> parts;
}

internal static class LegacyAniSourceValues
{
    public static byte[] ReadRequiredBytes(BinaryReader br, int byteCount, string field)
    {
        byte[] bytes = br.ReadBytes(byteCount);
        if (bytes.Length != byteCount)
            throw new EndOfStreamException($"Unexpected end of file while reading {field}.");
        return bytes;
    }

    public static LegacyFixedStringSource ReadFixedSjis(
        BinaryReader br,
        int byteCount,
        string field)
    {
        byte[] bytes = ReadRequiredBytes(br, byteCount, field);
        return new LegacyFixedStringSource
        {
            bytes = bytes,
            modelValue = USEncoder.ToEncoding.ToUnicode(bytes).TrimEnd('\0')
        };
    }

    public static void WriteFixedSjis(
        BinaryWriter bw,
        string value,
        int byteCount,
        LegacyFixedStringSource source)
    {
        if (source != null && value == source.modelValue
            && source.bytes != null && source.bytes.Length == byteCount)
        {
            bw.Write(source.bytes);
            return;
        }

        byte[] text = USEncoder.ToEncoding.ToSJIS(value ?? "");
        if (text.Length > byteCount)
            throw new InvalidDataException($"Fixed string is too long: {text.Length}/{byteCount} bytes.");
        bw.Write(text);
        for (int i = text.Length; i < byteCount; i++)
            bw.Write((byte)0);
    }

    public static byte[] EncodeScriptText(string value)
    {
        List<byte> bytes = new List<byte>(USEncoder.ToEncoding.ToSJIS(value ?? ""));
        for (int i = 0; i < bytes.Count; i++)
        {
            if (bytes[i] == 0x0A && (i == 0 || bytes[i - 1] != 0x0D))
            {
                bytes.Insert(i, 0x0D);
                i++;
            }
        }
        return bytes.ToArray();
    }

    public static bool VectorExactlyEquals(Vector3 left, Vector3 right)
    {
        return left.x.Equals(right.x)
            && left.y.Equals(right.y)
            && left.z.Equals(right.z);
    }

    public static bool QuaternionExactlyEquals(Quaternion left, Quaternion right)
    {
        return left.x.Equals(right.x)
            && left.y.Equals(right.y)
            && left.z.Equals(right.z)
            && left.w.Equals(right.w);
    }

    public static bool IsAscii(string value)
    {
        if (value == null)
            return true;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] > 0x7f)
                return false;
        }
        return true;
    }
}

public class ani2
{
    public const int OriginalGameAnimationLimit = 200;
    public int originalGameAnimationCount => Math.Min(animations?.Count ?? 0, OriginalGameAnimationLimit);
    public int editorOnlyAnimationCount => Math.Max(0, (animations?.Count ?? 0) - OriginalGameAnimationLimit);
    public int an2DeclaredAnimationCount { get; private set; }
    byte[] an2TrailingBytes = Array.Empty<byte>();
    public hod2v0 structure;
    public List<animation> animations;
    public string _filename;
    public AniContainerFormat sourceFormat { get; private set; } = AniContainerFormat.Unknown;
    public int legacyAnimationSlotCount => legacySource != null ? legacySource.animations.Count : 0;
    public bool hasLegacyTailData => legacySource != null && legacySource.HasTailData;
    public bool hasLegacyIkData => legacySource != null && legacySource.TailStartsWithIkData;
    public int legacyIkRecordCount => legacySource != null && legacySource.ikData != null
        ? legacySource.ikData.records.Count : 0;
    public bool legacyIkDataSupportsPartEdits => legacySource != null
        && legacySource.ikData != null && legacySource.ikData.isPartIndexed;

    LegacyAniSource legacySource;

    public async Task<bool> load(string filename, IProgress<int> progress = null)
    {
        _filename = filename;
        sourceFormat = AniContainerFormat.Unknown;
        legacySource = null;
        an2TrailingBytes = Array.Empty<byte>();
        an2DeclaredAnimationCount = 0;
        return await Task.Run(() =>
        {
            try
            {
                BinaryReader br = new BinaryReader(File.Open(filename, FileMode.Open, FileAccess.Read));
                using (br)
                {
                    return LoadFromReader(ref br, filename, progress);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error loading file {filename}: {ex.Message}");
                return false;
            }
        });
    }

    bool LoadFromReader(ref BinaryReader br, string filename, IProgress<int> progress)
    {
        string signature = ASCIIEncoding.ASCII.GetString(
            LegacyAniSourceValues.ReadRequiredBytes(br, 3, "ANI signature"));
        if (signature == "AN2")
        {
            sourceFormat = AniContainerFormat.An2;
            animations = new List<animation>();
            string robohod = USEncoder.ToEncoding.ToUnicode(br.ReadBytes(256)).TrimEnd('\0');
            structure = new hod2v0(robohod);
            if (!structure.loadFromBinary(ref br))
                return false;

            int aCount = br.ReadInt32();
            if (aCount < 0 || aCount > 100000)
                throw new InvalidDataException("AN2のアニメーション件数が不正です。");
            an2DeclaredAnimationCount = aCount;
            for (int i = 0; i < aCount; i++)
            {
                try
                {
                    animation aData = new animation();
                    aData.loadFromAni(ref br, ref structure);
                    animations.Add(aData);
                    if (aCount > 0)
                        progress?.Report((i + 1) * 100 / aCount);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Animation loading failed at index {i}: {ex.Message}");
                    return false;
                }
            }
            ReadAn2Extensions(ref br);
            return true;
        }

        if (signature == "ANI")
        {
            sourceFormat = AniContainerFormat.LegacyAni;
            legacySource = new LegacyAniSource();
            animations = new List<animation>();
            legacySource.structureFilename = LegacyAniSourceValues.ReadFixedSjis(
                br, 256, "legacy ANI structure filename");
            hod1 oldStructure = new hod1(legacySource.structureFilename.modelValue);
            LegacyHodSource oldStructureSource;
            if (!oldStructure.loadFromBinary(ref br, out oldStructureSource))
                return false;
            legacySource.structure = oldStructureSource;
            structure = oldStructure.convertToHod2v0();
            for (int i = 0; i < structure.parts.Count; i++)
                legacySource.structurePartNames.Add(structure.parts[i].name);

            for (int i = 0; i < 200; i++)
            {
                try
                {
                    animation aData = new animation();
                    LegacyAnimationSource animationSource;
                    aData.loadFromAniOld(ref br, out animationSource);
                    animations.Add(aData);
                    legacySource.animations.Add(animationSource);
                    progress?.Report((i + 1) * 100 / 200);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Old animation loading failed at index {i}: {ex.Message}");
                    return false;
                }
            }

            ReadLegacyExtensions(ref br);
            ReadAndApplyLegacyIkData();
            return true;
        }

        if (signature == "HOD")
        {
            sourceFormat = AniContainerFormat.Hod;
            br.BaseStream.Seek(0, SeekOrigin.Begin);
            animations = new List<animation>();
            hod1 hodfile = new hod1("HOD1 FILE");
            if (!hodfile.loadFromBinary(ref br))
                return false;
            structure = hodfile.convertToHod2v0();
            animation aData = new animation();
            aData.frames = new List<hod2v1>();
            aData.frames.Add(hodfile.convertToHod2v1());
            aData.scripts = new List<script>();
            animations.Add(aData);
            return true;
        }

        Debug.LogError($"Unsupported file signature '{signature}' in {filename}");
        return false;
    }

    void ReadAn2Extensions(ref BinaryReader br)
    {
        int remaining = checked((int)(br.BaseStream.Length - br.BaseStream.Position));
        an2TrailingBytes = LegacyAniSourceValues.ReadRequiredBytes(br, remaining, "AN2 trailing data");
        if (remaining == 0 || an2DeclaredAnimationCount != OriginalGameAnimationLimit)
            return;

        // Same layout as the original-game-tested count-200 diagnostic: additional
        // animation records follow the declared region, without new game-visible fields.
        // Only promote a COMPLETE, bounded record sequence; unknown tails stay opaque.
        try
        {
            using (var stream = new MemoryStream(an2TrailingBytes, false))
            {
                var reader = new BinaryReader(stream);
                void Skip(long length)
                {
                    if (length < 0 || length > stream.Length - stream.Position)
                        throw new InvalidDataException("Invalid AN2 extension boundary.");
                    stream.Position += length;
                }
                int Count()
                {
                    int value = reader.ReadInt32();
                    if (value < 0 || value > 100000) throw new InvalidDataException("Invalid AN2 extension count.");
                    return value;
                }
                int count = 0;
                while (stream.Position < stream.Length)
                {
                    if (++count > 4096) throw new InvalidDataException("Too many AN2 extensions.");
                    Skip(256); Skip(reader.ReadInt32());
                    int frames = Count();
                    for (int f = 0; f < frames; f++)
                    {
                        Skip(reader.ReadInt16());
                        if (reader.ReadByte() != 'H' || reader.ReadByte() != 'D' || reader.ReadByte() != '2'
                            || reader.ReadInt32() != 1 || reader.ReadInt32() != structure.parts.Count)
                            throw new InvalidDataException("Invalid AN2 extension HOD.");
                        Skip(179L * structure.parts.Count);
                    }
                    int scripts = Count();
                    for (int s = 0; s < scripts; s++) { Skip(8); Skip(reader.ReadInt32()); }
                }
                stream.Position = 0;
                var extensions = new List<animation>();
                for (int i = 0; i < count; i++)
                {
                    var item = new animation();
                    item.loadFromAni(ref reader, ref structure);
                    extensions.Add(item);
                }
                animations.AddRange(extensions);
                an2TrailingBytes = Array.Empty<byte>();
            }
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException
            || ex is ArgumentException || ex is IndexOutOfRangeException)
        {
            // Preserve all bytes if any candidate record cannot be read safely.
        }
    }

    void ReadLegacyExtensions(ref BinaryReader br)
    {
        long remainingLength = br.BaseStream.Length - br.BaseStream.Position;
        if (remainingLength <= 0)
            return;
        if (remainingLength > int.MaxValue)
            throw new InvalidDataException("Legacy ANI trailing data is too large.");

        byte[] remainder = LegacyAniSourceValues.ReadRequiredBytes(
            br, (int)remainingLength, "legacy ANI trailing data");
        byte[] ikSignature = ASCIIEncoding.ASCII.GetBytes("IKDATA");
        int searchStart = 0;
        while (searchStart <= remainder.Length - ikSignature.Length)
        {
            int ikOffset = IndexOf(remainder, ikSignature, searchStart);
            if (ikOffset < 0)
                break;

            List<animation> extensionAnimations;
            List<LegacyAnimationSource> extensionSources;
            if (TryParseLegacyAnimationRegion(
                remainder, ikOffset, out extensionAnimations, out extensionSources))
            {
                animations.AddRange(extensionAnimations);
                legacySource.animations.AddRange(extensionSources);
                legacySource.tailBytes = Slice(remainder, ikOffset, remainder.Length - ikOffset);
                return;
            }

            searchStart = ikOffset + 1;
        }

        legacySource.tailBytes = remainder;
    }

    void ReadAndApplyLegacyIkData()
    {
        if (legacySource == null || !legacySource.TailStartsWithIkData)
            return;

        LegacyIkDataSection section;
        string error;
        if (!LegacyIkDataSection.TryParse(
            legacySource.tailBytes,
            structure.parts.Count,
            out section,
            out error))
        {
            Debug.LogWarning("[ani2] IKDATAを解析できないため、原バイトのまま保持します: " + error);
            return;
        }

        legacySource.ikData = section;
        if (!section.isPartIndexed)
        {
            Debug.LogWarning(
                "[ani2] IKDATAは既知のパーツ属性形式ではないため、原バイトのまま保持します。");
            return;
        }

        section.ApplyTo(structure.parts);
    }

    static bool TryParseLegacyAnimationRegion(
        byte[] data,
        int byteCount,
        out List<animation> extensionAnimations,
        out List<LegacyAnimationSource> extensionSources)
    {
        extensionAnimations = new List<animation>();
        extensionSources = new List<LegacyAnimationSource>();
        if (byteCount == 0)
            return true;

        try
        {
            using (MemoryStream stream = new MemoryStream(data, 0, byteCount, false, true))
            {
                BinaryReader reader = new BinaryReader(stream);
                try
                {
                    while (stream.Position < stream.Length)
                    {
                        if (stream.Length - stream.Position < 264)
                            return false;
                        if (extensionAnimations.Count >= 4096)
                            return false;

                        long start = stream.Position;
                        animation animationData = new animation();
                        LegacyAnimationSource animationSource;
                        animationData.loadFromAniOld(ref reader, out animationSource);
                        if (stream.Position <= start || stream.Position > stream.Length)
                            return false;

                        extensionAnimations.Add(animationData);
                        extensionSources.Add(animationSource);
                    }
                }
                finally
                {
                    reader.Dispose();
                }
            }
        }
        catch
        {
            extensionAnimations.Clear();
            extensionSources.Clear();
            return false;
        }

        return extensionAnimations.Count > 0;
    }

    static int IndexOf(byte[] data, byte[] pattern, int startIndex)
    {
        for (int i = Math.Max(0, startIndex); i <= data.Length - pattern.Length; i++)
        {
            bool matches = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
                return i;
        }
        return -1;
    }

    static byte[] Slice(byte[] data, int offset, int count)
    {
        byte[] result = new byte[count];
        Buffer.BlockCopy(data, offset, result, 0, count);
        return result;
    }

    public void save(string filename = "")
    {
        if (filename == "")
            filename = _filename;

        if (sourceFormat == AniContainerFormat.LegacyAni)
            SaveLegacyAni(filename);
        else
            SaveAn2(filename);
    }

    public void saveAsAn2(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            throw new ArgumentException("AN2 destination filename is required.", nameof(filename));
        SaveAn2(filename);
    }

    void SaveAn2(string filename)
    {
        // AN2出力は原作のchildCount階層を保つ正規化コピーから行う。
        // 旧ANIの原バイト保存に必要な、編集中の生の階層列は変更しない。
        ani2 export = CreateAn2ExportCopy(out _);
        WriteAtomically(filename, export.WriteAn2, path =>
        {
            var reloaded = new ani2();
            var reader = new BinaryReader(File.OpenRead(path));
            using (reader)
            {
                if (!reloaded.LoadFromReader(ref reader, path, null)
                    || reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("AN2の再読込検証に失敗しました。");
            }
            export.ValidateAn2Export(reloaded);
        });
    }

    internal void WriteAn2(BinaryWriter writer)
    {
        writer.Write(ASCIIEncoding.ASCII.GetBytes("AN2"));
        WriteFixedSJIS(writer, structure.filename, 256);
        structure.saveToBinary(ref writer);
        writer.Write(originalGameAnimationCount);
        foreach (animation item in animations)
            item.saveToAni(ref writer);
        writer.Write(an2TrailingBytes);
    }

    internal ani2 CreateAn2ExportCopy(out HodHierarchyRepairPlan plan)
    {
        if (structure == null || structure.parts == null || animations == null)
            throw new InvalidDataException("構造HODまたはアニメーション情報がありません。");
        var copy = new ani2
        {
            structure = new hod2v0(structure.filename)
            {
                parts = new List<hod2v0_Part>(structure.parts)
            },
            animations = new List<animation>(),
            an2TrailingBytes = (byte[])an2TrailingBytes.Clone()
        };
        foreach (animation source in animations)
        {
            if (source == null || source.frames == null || source.scripts == null)
                throw new InvalidDataException("アニメーション情報がありません。");
            var target = new animation
            {
                name = source.name,
                squirrelInit = source.squirrelInit,
                initialSourceText = source.initialSourceText,
                scripts = new List<script>(source.scripts),
                frames = new List<hod2v1>()
            };
            foreach (hod2v1 frame in source.frames)
            {
                if (frame == null || frame.parts == null)
                    throw new InvalidDataException("フレームのパーツ情報がありません。");
                target.frames.Add(new hod2v1(frame.filename)
                {
                    parts = new List<hod2v1_Part>(frame.parts)
                });
            }
            copy.animations.Add(target);
        }
        if (!HodHierarchyRepair.TryApplyChildCountFirst(copy, out plan, out string error))
            throw new InvalidDataException(error);
        return copy;
    }

    // Compare transforms and script payload bytes. A lossy decoder can make two
    // different byte strings look identical, so decoded script equality is insufficient.
    internal void ValidateAn2Export(ani2 actual)
    {
        void Require(bool condition, string field)
        {
            if (!condition)
                throw new InvalidDataException("AN2再読込でデータが一致しません: " + field);
        }
        Require(actual != null && actual.sourceFormat == AniContainerFormat.An2
            && actual.structure?.parts != null && actual.animations != null, "container");
        Require((structure.filename ?? "") == actual.structure.filename, "structure filename");
        Require(structure.parts.Count == actual.structure.parts.Count, "structure count");
        for (int p = 0; p < structure.parts.Count; p++)
        {
            var expected = structure.parts[p];
            expected.name = expected.name ?? "";
            Require(expected.Equals(actual.structure.parts[p]), "structure part " + p);
        }
        Require(animations.Count == actual.animations.Count, "animation count");
        Require(actual.an2DeclaredAnimationCount == originalGameAnimationCount, "original game animation count");
        Require(ByteArraysEqual(an2TrailingBytes, actual.an2TrailingBytes), "opaque AN2 trailing bytes");
        for (int a = 0; a < animations.Count; a++)
        {
            var expected = animations[a];
            var loaded = actual.animations[a];
            Require((expected.name ?? "") == loaded.name, "animation name " + a);
            Require(AniScriptTextSource.BytesEqual(
                AniScriptTextSource.GetBytes(expected.squirrelInit, expected.initialSourceText, false),
                AniScriptTextSource.GetBytes(loaded.squirrelInit, loaded.initialSourceText, false, false)),
                "initial script bytes " + a);
            Require(expected.frames.Count == loaded.frames.Count
                && expected.scripts.Count == loaded.scripts.Count, "animation counts " + a);
            for (int f = 0; f < expected.frames.Count; f++)
            {
                var left = expected.frames[f];
                var right = loaded.frames[f];
                Require((left.filename ?? "") == right.filename
                    && left.parts.Count == right.parts.Count, $"frame {a}/{f}");
                for (int p = 0; p < left.parts.Count; p++)
                {
                    var part = left.parts[p];
                    part.name = structure.parts[p].name ?? "";
                    Require(part.Equals(right.parts[p]), $"frame part {a}/{f}/{p}");
                }
            }
            for (int s = 0; s < expected.scripts.Count; s++)
            {
                var left = expected.scripts[s];
                var right = loaded.scripts[s];
                Require(left.unk == right.unk && left.time.Equals(right.time)
                    && AniScriptTextSource.BytesEqual(
                        AniScriptTextSource.GetBytes(left.squirrel, left.sourceText, true),
                        AniScriptTextSource.GetBytes(right.squirrel, right.sourceText, true, false)),
                    $"script {a}/{s}");
            }
        }
    }

    void SaveLegacyAni(string filename)
    {
        string validationError;
        if (!canSaveAsLegacyAni(out validationError))
            throw new InvalidDataException(validationError);

        int discardedConstraintCount = CountLegacyOnlyRotationConstraintEdits();
        if (discardedConstraintCount > 0)
        {
            Debug.LogWarning(
                $"[ani2] 旧ANI形式では回転制約を保存できないため、"
                + $"{discardedConstraintCount}件のunk1〜unk3を保存時に破棄し、rotationを保存します。");
        }

        WriteAtomically(filename, bw =>
        {
            bw.Write(ASCIIEncoding.ASCII.GetBytes("ANI"));
            LegacyAniSourceValues.WriteFixedSjis(
                bw, structure.filename, 256, legacySource.structureFilename);
            hod1.SaveFromHod2v0(ref bw, structure, legacySource.structure);

            for (int i = 0; i < animations.Count; i++)
                animations[i].saveToAniOld(ref bw, legacySource.animations[i]);

            WriteLegacyTail(bw);
        }, ValidateLegacyRoundTrip);
    }

    void WriteLegacyTail(BinaryWriter writer)
    {
        if (legacySource.tailBytes == null || legacySource.tailBytes.Length == 0)
            return;

        LegacyIkDataSection ikData = legacySource.ikData;
        if (ikData != null && ikData.isPartIndexed
            && !ikData.IsEquivalentTo(structure.parts))
        {
            LegacyIkDataSection.WritePartIndexed(writer, structure.parts);
            return;
        }

        writer.Write(legacySource.tailBytes);
    }

    public bool canChangeLegacyStructure(out string error)
    {
        error = "";
        if (sourceFormat != AniContainerFormat.LegacyAni || legacySource == null)
        {
            error = "旧ANIとして読み込まれたデータではありません。";
            return false;
        }
        if (structure == null || structure.parts == null)
        {
            error = "構造HODがありません。";
            return false;
        }

        if (legacySource.HasTailData && !CanRewriteLegacyIkData())
        {
            error = legacySource.TailStartsWithIkData
                ? "未知形式のIKDATAを含む旧ANIでは、パーツの追加・削除を安全に保存できません。AN2として保存してください。"
                : "未解析の末尾データを含む旧ANIでは、パーツの追加・削除を安全に保存できません。AN2として保存してください。";
            return false;
        }

        if (!CanRewriteLegacyIkData())
        {
            for (int partIndex = 0; partIndex < structure.parts.Count; partIndex++)
            {
                hod2v0_Part part = structure.parts[partIndex];
                if (part.flag != 1
                    || !LegacyAniSourceValues.VectorExactlyEquals(part.unk, Vector3.one))
                {
                    error = $"構造HODのパーツ {partIndex} のflag/unkは旧HOD単体では表現できず、"
                        + "対応するIKDATAもありません。AN2として保存してください。";
                    return false;
                }
            }
        }

        return true;
    }

    public bool canSaveAsLegacyAni(out string error)
    {
        error = "";
        if (sourceFormat != AniContainerFormat.LegacyAni || legacySource == null)
        {
            error = "旧ANIとして読み込まれたデータではありません。";
            return false;
        }
        if (structure == null || structure.parts == null)
        {
            error = "構造HODがありません。";
            return false;
        }
        if (animations == null || animations.Count != legacySource.animations.Count)
        {
            error = $"旧ANIのアニメーション枠数が読み込み時から変更されています（{animations?.Count ?? 0}/{legacySource.animations.Count}）。";
            return false;
        }
        if (!ValidateFixedSjis(structure.filename, 256, "構造HODファイル名", out error))
            return false;

        if (legacySource.HasTailData && !HasOriginalStructureLayout()
            && !CanRewriteLegacyIkData())
        {
            error = legacySource.TailStartsWithIkData
                ? "IKDATAを含む旧ANIでは、パーツ数・順序・名前を変更した状態を安全に保存できません。AN2として保存してください。"
                : "未解析の末尾データを含む旧ANIでは、パーツ数・順序・名前を変更した状態を安全に保存できません。AN2として保存してください。";
            return false;
        }

        if (!CanRewriteLegacyIkData())
        {
            for (int partIndex = 0; partIndex < structure.parts.Count; partIndex++)
            {
                hod2v0_Part part = structure.parts[partIndex];
                if (part.flag != 1
                    || !LegacyAniSourceValues.VectorExactlyEquals(part.unk, Vector3.one))
                {
                    error = $"構造HODのパーツ {partIndex} のflag/unkは旧HOD単体では表現できず、"
                        + "対応するIKDATAもありません。AN2として保存してください。";
                    return false;
                }
            }
        }

        for (int partIndex = 0; partIndex < structure.parts.Count; partIndex++)
        {
            if (!ValidateLegacyPartName(
                structure.parts[partIndex].name,
                $"構造HODのパーツ {partIndex}",
                out error))
            {
                return false;
            }
        }

        for (int animationIndex = 0; animationIndex < animations.Count; animationIndex++)
        {
            animation animationData = animations[animationIndex];
            if (animationData == null || animationData.frames == null || animationData.scripts == null)
            {
                error = $"アニメーション {animationIndex} のデータがありません。";
                return false;
            }
            if (!string.IsNullOrEmpty(animationData.squirrelInit))
            {
                error = $"アニメーション {animationIndex} の初期スクリプトは旧ANI形式に保存できません。";
                return false;
            }
            if (!ValidateFixedSjis(
                animationData.name, 256, $"アニメーション {animationIndex} の名前", out error))
            {
                return false;
            }

            for (int frameIndex = 0; frameIndex < animationData.frames.Count; frameIndex++)
            {
                hod2v1 frame = animationData.frames[frameIndex];
                if (frame == null || frame.parts == null
                    || frame.parts.Count != structure.parts.Count)
                {
                    error = $"アニメーション {animationIndex} のフレーム {frameIndex} と構造HODのパーツ数が一致しません。";
                    return false;
                }
                if (!ValidateFixedSjis(
                    frame.filename,
                    30,
                    $"アニメーション {animationIndex} のフレーム {frameIndex} のHODファイル名",
                    out error))
                {
                    return false;
                }

                for (int partIndex = 0; partIndex < frame.parts.Count; partIndex++)
                {
                    hod2v1_Part part = frame.parts[partIndex];
                    if (!ValidateLegacyPartName(
                        part.name,
                        $"アニメーション {animationIndex}、フレーム {frameIndex}、パーツ {partIndex}",
                        out error))
                    {
                        return false;
                    }
                    // 旧ANIのHODにはTRS行列しかなく、unk1〜unk3の回転制約欄はありません。
                    // 保存時はrotationを行列へ書き出し、制約値は旧形式では保持しません。
                }
            }
        }

        return true;
    }

    int CountLegacyOnlyRotationConstraintEdits()
    {
        int count = 0;
        for (int animationIndex = 0; animationIndex < animations.Count; animationIndex++)
        {
            animation animationData = animations[animationIndex];
            for (int frameIndex = 0; frameIndex < animationData.frames.Count; frameIndex++)
            {
                hod2v1 frame = animationData.frames[frameIndex];
                for (int partIndex = 0; partIndex < frame.parts.Count; partIndex++)
                {
                    hod2v1_Part part = frame.parts[partIndex];
                    if (!LegacyAniSourceValues.QuaternionExactlyEquals(part.rotation, part.unk1)
                        || !LegacyAniSourceValues.QuaternionExactlyEquals(part.rotation, part.unk2)
                        || !LegacyAniSourceValues.QuaternionExactlyEquals(part.rotation, part.unk3))
                    {
                        count++;
                    }
                }
            }
        }
        return count;
    }

    void ValidateLegacyRoundTrip(string filename)
    {
        ani2 reloaded = new ani2();
        reloaded._filename = filename;
        BinaryReader reader = new BinaryReader(File.OpenRead(filename));
        try
        {
            if (!reloaded.LoadFromReader(ref reader, filename, null))
                throw new InvalidDataException("保存した旧ANIを再読み込みできませんでした。");
        }
        finally
        {
            reader.Dispose();
        }

        string difference;
        if (!TryCompareLegacyRoundTrip(reloaded, out difference))
            throw new InvalidDataException("保存した旧ANIの再読み込み検証に失敗しました: " + difference);

        string validationError;
        if (!reloaded.canSaveAsLegacyAni(out validationError))
        {
            throw new InvalidDataException(
                "保存した旧ANIが再保存条件を満たしません: " + validationError);
        }
    }

    bool TryCompareLegacyRoundTrip(ani2 actual, out string error)
    {
        error = "";
        if (actual == null || actual.sourceFormat != AniContainerFormat.LegacyAni)
        {
            error = "コンテナ形式が旧ANIではありません。";
            return false;
        }
        if (actual.structure == null || actual.structure.parts == null
            || actual.structure.parts.Count != structure.parts.Count)
        {
            error = "構造HODのパーツ数が一致しません。";
            return false;
        }
        if (actual.structure.filename != structure.filename)
        {
            error = "構造HODファイル名が一致しません。";
            return false;
        }

        for (int i = 0; i < structure.parts.Count; i++)
        {
            hod2v0_Part expectedPart = structure.parts[i];
            hod2v0_Part actualPart = actual.structure.parts[i];
            LegacyHodPartSource expectedSourcePart = GetLegacyStructurePartSource(legacySource, i);
            LegacyHodPartSource actualSourcePart = GetLegacyStructurePartSource(actual.legacySource, i);
            if (expectedPart.name != actualPart.name
                || expectedPart.treeDepth != actualPart.treeDepth
                || expectedPart.childCount != actualPart.childCount
                || expectedPart.flag != actualPart.flag
                || !LegacyMatrixMatches(
                    expectedPart.position,
                    expectedPart.rotation,
                    expectedPart.scale,
                    expectedSourcePart,
                    actualSourcePart)
                || !LegacyAniSourceValues.VectorExactlyEquals(expectedPart.unk, actualPart.unk))
            {
                error = $"構造HODのパーツ {i} が一致しません"
                    + $"（name={expectedPart.name == actualPart.name}"
                    + $" depth={expectedPart.treeDepth}/{actualPart.treeDepth}"
                    + $" children={expectedPart.childCount}/{actualPart.childCount}"
                    + $" flag={expectedPart.flag}/{actualPart.flag}"
                    + $" matrix={LegacyMatrixMatches(expectedPart.position, expectedPart.rotation, expectedPart.scale, expectedSourcePart, actualSourcePart)}"
                    + $" unk={expectedPart.unk}/{actualPart.unk}）。";
                return false;
            }
        }

        if (actual.animations == null || actual.animations.Count != animations.Count)
        {
            error = "アニメーション枠数が一致しません。";
            return false;
        }
        for (int animationIndex = 0; animationIndex < animations.Count; animationIndex++)
        {
            animation expectedAnimation = animations[animationIndex];
            animation actualAnimation = actual.animations[animationIndex];
            if (expectedAnimation.name != actualAnimation.name
                || !string.IsNullOrEmpty(actualAnimation.squirrelInit)
                || expectedAnimation.frames.Count != actualAnimation.frames.Count
                || expectedAnimation.scripts.Count != actualAnimation.scripts.Count)
            {
                error = $"アニメーション {animationIndex} の基本情報が一致しません。";
                return false;
            }

            for (int frameIndex = 0; frameIndex < expectedAnimation.frames.Count; frameIndex++)
            {
                hod2v1 expectedFrame = expectedAnimation.frames[frameIndex];
                hod2v1 actualFrame = actualAnimation.frames[frameIndex];
                if (expectedFrame.filename != actualFrame.filename
                    || expectedFrame.parts.Count != actualFrame.parts.Count)
                {
                    error = $"アニメーション {animationIndex}、フレーム {frameIndex} の基本情報が一致しません。";
                    return false;
                }

                for (int partIndex = 0; partIndex < expectedFrame.parts.Count; partIndex++)
                {
                    hod2v1_Part expectedPart = expectedFrame.parts[partIndex];
                    hod2v1_Part actualPart = actualFrame.parts[partIndex];
                    LegacyHodPartSource expectedSourcePart = GetLegacyFramePartSource(
                        legacySource, animationIndex, frameIndex, partIndex);
                    LegacyHodPartSource actualSourcePart = GetLegacyFramePartSource(
                        actual.legacySource, animationIndex, frameIndex, partIndex);
                    if (expectedPart.name != actualPart.name
                        || expectedPart.treeDepth != actualPart.treeDepth
                        || expectedPart.childCount != actualPart.childCount
                        || !LegacyMatrixMatches(
                            expectedPart.position,
                            expectedPart.rotation,
                            expectedPart.scale,
                            expectedSourcePart,
                            actualSourcePart))
                    {
                        error = $"アニメーション {animationIndex}、フレーム {frameIndex}、パーツ {partIndex} が一致しません。";
                        return false;
                    }
                }
            }

            for (int scriptIndex = 0; scriptIndex < expectedAnimation.scripts.Count; scriptIndex++)
            {
                script expectedScript = expectedAnimation.scripts[scriptIndex];
                script actualScript = actualAnimation.scripts[scriptIndex];
                if (expectedScript.unk != actualScript.unk
                    || !expectedScript.time.Equals(actualScript.time)
                    || !ByteArraysEqual(
                        AniScriptTextSource.GetBytes(expectedScript.squirrel, expectedScript.sourceText, true, false),
                        AniScriptTextSource.GetBytes(actualScript.squirrel, actualScript.sourceText, true, false)))
                {
                    error = $"アニメーション {animationIndex}、スクリプト {scriptIndex} が一致しません。";
                    return false;
                }
            }
        }

        bool rewroteIkData = legacySource.ikData != null
            && legacySource.ikData.isPartIndexed
            && !legacySource.ikData.IsEquivalentTo(structure.parts);
        if (rewroteIkData)
        {
            if (!actual.legacyIkDataSupportsPartEdits
                || actual.legacyIkRecordCount != structure.parts.Count)
            {
                error = "再構築したIKDATAのパーツ属性数が一致しません。";
                return false;
            }
        }
        else if (!ByteArraysEqual(
            legacySource.tailBytes,
            actual.legacySource != null ? actual.legacySource.tailBytes : null))
        {
            error = "未変更の末尾データが一致しません。";
            return false;
        }

        return true;
    }

    static LegacyHodPartSource GetLegacyStructurePartSource(LegacyAniSource source, int partIndex)
    {
        if (source == null || source.structure == null || source.structure.parts == null
            || partIndex < 0 || partIndex >= source.structure.parts.Count)
        {
            return null;
        }

        return source.structure.parts[partIndex];
    }

    static LegacyHodPartSource GetLegacyFramePartSource(
        LegacyAniSource source,
        int animationIndex,
        int frameIndex,
        int partIndex)
    {
        if (source == null || source.animations == null
            || animationIndex < 0 || animationIndex >= source.animations.Count)
        {
            return null;
        }

        LegacyAnimationSource animationSource = source.animations[animationIndex];
        if (animationSource == null || animationSource.frames == null
            || frameIndex < 0 || frameIndex >= animationSource.frames.Count)
        {
            return null;
        }

        LegacyHodSource frameSource = animationSource.frames[frameIndex];
        if (frameSource == null || frameSource.parts == null
            || partIndex < 0 || partIndex >= frameSource.parts.Count)
        {
            return null;
        }

        return frameSource.parts[partIndex];
    }

    static bool LegacyMatrixMatches(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        LegacyHodPartSource originalSource,
        LegacyHodPartSource savedSource)
    {
        if (savedSource == null || savedSource.matrixBytes == null
            || savedSource.matrixBytes.Length != 64)
        {
            return false;
        }

        if (originalSource != null
            && LegacyAniSourceValues.VectorExactlyEquals(position, originalSource.position)
            && LegacyAniSourceValues.QuaternionExactlyEquals(rotation, originalSource.rotation)
            && LegacyAniSourceValues.VectorExactlyEquals(scale, originalSource.scale)
            && originalSource.matrixBytes != null && originalSource.matrixBytes.Length == 64)
        {
            return ByteArraysEqual(originalSource.matrixBytes, savedSource.matrixBytes);
        }

        Matrix4x4 matrix = Matrix4x4.TRS(position, rotation, scale);
        float[] values =
        {
            matrix.m00, matrix.m10, matrix.m20, matrix.m30,
            matrix.m01, matrix.m11, matrix.m21, matrix.m31,
            matrix.m02, matrix.m12, matrix.m22, matrix.m32,
            matrix.m03, matrix.m13, matrix.m23, matrix.m33
        };
        for (int i = 0; i < values.Length; i++)
        {
            if (!BitConverter.ToSingle(savedSource.matrixBytes, i * 4).Equals(values[i]))
                return false;
        }
        return true;
    }

    static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }

    bool CanRewriteLegacyIkData()
    {
        return legacySource != null && legacySource.ikData != null
            && legacySource.ikData.isPartIndexed;
    }

    bool HasOriginalStructureLayout()
    {
        if (structure.parts.Count != legacySource.structurePartNames.Count)
            return false;
        for (int i = 0; i < structure.parts.Count; i++)
        {
            if (structure.parts[i].name != legacySource.structurePartNames[i])
                return false;
        }
        return true;
    }

    static bool ValidateLegacyPartName(string value, string field, out string error)
    {
        error = "";
        if (!LegacyAniSourceValues.IsAscii(value))
        {
            error = $"{field}の名前には旧HODで表現できない文字が含まれています。";
            return false;
        }
        byte[] bytes = ASCIIEncoding.ASCII.GetBytes(value ?? "");
        if (bytes.Length > 256)
        {
            error = $"{field}の名前が256バイトを超えています。";
            return false;
        }
        return true;
    }

    static bool ValidateFixedSjis(
        string value,
        int maximumBytes,
        string field,
        out string error)
    {
        error = "";
        int byteCount = USEncoder.ToEncoding.ToSJIS(value ?? "").Length;
        if (byteCount > maximumBytes)
        {
            error = $"{field}が旧ANIの固定長を超えています（{byteCount}/{maximumBytes}バイト）。";
            return false;
        }
        return true;
    }

    static void WriteAtomically(
        string filename,
        Action<BinaryWriter> write,
        Action<string> validate = null)
    {
        if (string.IsNullOrEmpty(filename))
            throw new InvalidOperationException("Save destination filename is empty.");

        string directory = Path.GetDirectoryName(Path.GetFullPath(filename));
        string tempFilename = Path.Combine(directory, ".windom-save-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool ownsTemporary = false;

        try
        {
            BinaryWriter bw = new BinaryWriter(File.Open(tempFilename, FileMode.CreateNew, FileAccess.ReadWrite));
            ownsTemporary = true;
            try
            {
                write(bw);
            }
            finally
            {
                bw.Close();
            }

            validate?.Invoke(tempFilename);
            ReplaceFile(tempFilename, filename);
        }
        catch
        {
            if (ownsTemporary && File.Exists(tempFilename))
                File.Delete(tempFilename);
            throw;
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

    static void ReplaceFile(string tempFilename, string filename)
    {
        if (File.Exists(filename))
            File.Replace(tempFilename, filename, null);
        else
            File.Move(tempFilename, filename);
    }

    public void addPart(string partName, int parent)
    {
        string error;
        if (!TryAddPart(partName, parent, out error))
            Debug.LogWarning("[ani2] " + error);
    }

    public bool TryAddPart(string partName, int parent, out string error)
    {
        if (!TryPrepareStructureEdit(parent, out List<AniFramePartsEdit> frameEdits, out error))
            return false;

        List<hod2v0_Part> editedStructure = new List<hod2v0_Part>(structure.parts);
        int level = editedStructure[parent].treeDepth + 1;
        hod2v0_Part pHod = editedStructure[parent];
        pHod.childCount++;
        editedStructure[parent] = pHod;
        hod2v0_Part nPart = new hod2v0_Part();
        nPart.name = partName;
        nPart.treeDepth = level;
        nPart.flag = 1;
        nPart.unk = new Vector3(1, 1, 1);
        nPart.position = new Vector3(0, 0, 0);
        nPart.rotation = new Quaternion(0, 0, 0, 1);
        nPart.scale = new Vector3(1, 1, 1);
        int i = parent + 1;
        for (; i < editedStructure.Count; i++)
        {
            if (editedStructure[i].treeDepth <= editedStructure[parent].treeDepth)
            {
                break;
            }
        }
        editedStructure.Insert(i, nPart);

        hod2v1_Part nPart1 = new hod2v1_Part();
        nPart1.name = partName;
        nPart1.treeDepth = level;
        nPart1.position = new Vector3(0, 0, 0);
        nPart1.rotation = new Quaternion(0, 0, 0, 1);
        nPart1.scale = new Vector3(1, 1, 1);
        nPart1.unk1 = nPart1.rotation;
        nPart1.unk2 = nPart1.rotation;
        nPart1.unk3 = nPart1.rotation;
        for (int frameIndex = 0; frameIndex < frameEdits.Count; frameIndex++)
        {
            List<hod2v1_Part> frameParts = frameEdits[frameIndex].parts;
            hod2v1_Part pHod1 = frameParts[parent];
            pHod1.childCount++;
            frameParts[parent] = pHod1;
            frameParts.Insert(i, nPart1);
        }

        if (!TryValidateEditedStructure(editedStructure, frameEdits, out error))
            return false;

        structure.parts = editedStructure;
        CommitFrameEdits(frameEdits);
        error = "";
        return true;
    }

    public bool removePart(int index)
    {
        string error;
        bool result = TryRemovePart(index, out error);
        if (!result && !string.IsNullOrEmpty(error))
            Debug.LogWarning("[ani2] " + error);
        return result;
    }

    public bool TryRemovePart(int index, out string error)
    {
        if (index <= 0)
        {
            error = "ルートパーツは削除できません。";
            return false;
        }

        if (!TryPrepareStructureEdit(index, out List<AniFramePartsEdit> frameEdits, out error))
            return false;

        List<hod2v0_Part> editedStructure = new List<hod2v0_Part>(structure.parts);
        int parentIndex = FindParentIndex(editedStructure, index);
        if (parentIndex < 0)
        {
            error = "削除対象パーツの親を特定できません。";
            return false;
        }

        int removeCount = GetSubtreeEndIndex(editedStructure, index) - index;
        if (removeCount <= 0 || index + removeCount > editedStructure.Count)
        {
            error = "削除対象の子孫範囲を安全に特定できません。";
            return false;
        }

        hod2v0_Part parentPart = editedStructure[parentIndex];
        parentPart.childCount--;
        editedStructure[parentIndex] = parentPart;

        for (int frameIndex = 0; frameIndex < frameEdits.Count; frameIndex++)
        {
            List<hod2v1_Part> frameParts = frameEdits[frameIndex].parts;
            hod2v1_Part frameParentPart = frameParts[parentIndex];
            frameParentPart.childCount = parentPart.childCount;
            frameParts[parentIndex] = frameParentPart;
            frameParts.RemoveRange(index, removeCount);
        }

        editedStructure.RemoveRange(index, removeCount);

        if (!TryValidateEditedStructure(editedStructure, frameEdits, out error))
            return false;

        structure.parts = editedStructure;
        CommitFrameEdits(frameEdits);
        error = "";
        return true;
    }

    bool TryPrepareStructureEdit(
        int selectedIndex,
        out List<AniFramePartsEdit> frameEdits,
        out string error)
    {
        frameEdits = new List<AniFramePartsEdit>();
        if (structure == null || structure.parts == null
            || selectedIndex < 0 || selectedIndex >= structure.parts.Count)
        {
            error = "構造HODまたは選択パーツが不正です。";
            return false;
        }

        if (sourceFormat == AniContainerFormat.LegacyAni
            && !canChangeLegacyStructure(out error))
        {
            return false;
        }

        string hierarchyError;
        if (!HodHierarchyValidator.TryValidate(structure.parts, out hierarchyError))
        {
            error = "HOD階層が構造編集用に正規化されていません。\n" + hierarchyError;
            return false;
        }

        if (animations == null)
        {
            error = "アニメーション情報がありません。";
            return false;
        }

        for (int animationIndex = 0; animationIndex < animations.Count; animationIndex++)
        {
            animation animationData = animations[animationIndex];
            if (animationData == null || animationData.frames == null)
            {
                error = $"アニメーション[{animationIndex}]のフレーム情報がありません。";
                return false;
            }

            for (int frameIndex = 0; frameIndex < animationData.frames.Count; frameIndex++)
            {
                hod2v1 frame = animationData.frames[frameIndex];
                if (frame == null || frame.parts == null || frame.parts.Count != structure.parts.Count)
                {
                    error = $"アニメーション[{animationIndex}] フレーム[{frameIndex}]のパーツ数が構造HODと一致しません。";
                    return false;
                }

                for (int partIndex = 0; partIndex < structure.parts.Count; partIndex++)
                {
                    if (frame.parts[partIndex].treeDepth != structure.parts[partIndex].treeDepth
                        || frame.parts[partIndex].childCount != structure.parts[partIndex].childCount)
                    {
                        error = $"アニメーション[{animationIndex}] フレーム[{frameIndex}]の位置{partIndex}が構造HODの階層列と一致しません。";
                        return false;
                    }
                }

                frameEdits.Add(new AniFramePartsEdit
                {
                    frame = frame,
                    parts = new List<hod2v1_Part>(frame.parts)
                });
            }
        }

        error = "";
        return true;
    }

    static bool TryValidateEditedStructure(
        List<hod2v0_Part> editedStructure,
        List<AniFramePartsEdit> frameEdits,
        out string error)
    {
        if (!HodHierarchyValidator.TryValidate(editedStructure, out error))
            return false;

        for (int frameIndex = 0; frameIndex < frameEdits.Count; frameIndex++)
        {
            List<hod2v1_Part> frameParts = frameEdits[frameIndex].parts;
            if (frameParts.Count != editedStructure.Count)
            {
                error = "構造編集後のフレームパーツ数が構造HODと一致しません。";
                return false;
            }

            for (int partIndex = 0; partIndex < editedStructure.Count; partIndex++)
            {
                if (frameParts[partIndex].treeDepth != editedStructure[partIndex].treeDepth
                    || frameParts[partIndex].childCount != editedStructure[partIndex].childCount)
                {
                    error = $"構造編集後のフレーム位置{partIndex}が構造HODの階層列と一致しません。";
                    return false;
                }
            }
        }

        error = "";
        return true;
    }

    static void CommitFrameEdits(List<AniFramePartsEdit> frameEdits)
    {
        for (int i = 0; i < frameEdits.Count; i++)
            frameEdits[i].frame.parts = frameEdits[i].parts;
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
