using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Утилиты для распаковки и сборки Unity assets версии 6.
    /// </summary>
    public static class UnityAssetsV6Packer
    {
        // ---- Публичные API ----

        public static void UnpackAssetsV6(string assetsPath, string outDir)
        {
            using var fs = File.OpenRead(assetsPath);
            using var br = new BinaryReader(fs);

            var hdr = ReadHeaderV6(br);
            if (hdr.Version != 6)
                throw new NotSupportedException($"SerializedFile version {hdr.Version} != 6");

            var dataBase = hdr.DataOffset != 0 ? hdr.DataOffset : hdr.MetaSize;
            if (dataBase <= 0 || dataBase > fs.Length)
                throw new InvalidDataException("Bad dataBase");

            fs.Position = 0;
            var metadata = br.ReadBytes((int)dataBase);

            var table = FindObjectTable(metadata, (long)fs.Length, (int)dataBase)
                       ?? throw new InvalidDataException("Object table not found.");

            Directory.CreateDirectory(outDir);
            var metaPath = Path.Combine(outDir, "metadata.bin");
            var objDir = Path.Combine(outDir, "objects");
            Directory.CreateDirectory(objDir);
            File.WriteAllBytes(metaPath, metadata);

            var entries = new List<ObjectEntry>();
            for (int i = 0; i < table.Count; i++)
            {
                var e = table[i];
                var abs = dataBase + (long)e.OffsetRel;
                if (abs < 0 || abs + e.Size > fs.Length)
                    throw new InvalidDataException($"Entry {i}: bad data range");

                fs.Position = abs;
                var data = br.ReadBytes((int)e.Size);
                var objName = $"{i:D5}__pid-{e.PathId}__typ-{e.TypeIndex}.bin";
                File.WriteAllBytes(Path.Combine(objDir, objName), data);

                entries.Add(e);
            }

            var manifest = new Manifest
            {
                Version = (int)hdr.Version,
                MetaSize = (int)hdr.MetaSize,
                FileSize = (long)hdr.FileSize,
                DataBase = (int)dataBase,
                TableOffset = table.TableOffset,
                EntrySize = table.EntrySize,
                PathIdIs64 = table.PathIdIs64,
                Count = table.Count,
                Entries = entries
            };
            File.WriteAllText(Path.Combine(outDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        // ---- Внутренняя кухня ----

        private static void Align4(BinaryWriter bw)
        {
            long pad = (4 - (bw.BaseStream.Position & 3)) & 3;
            if (pad > 0) bw.Write(new byte[pad]);
        }

        private static HeaderV6 ReadHeaderV6(BinaryReader br)
        {
            br.BaseStream.Position = 0;
            var metaSize = ReadBE32(br);
            var fileSize = ReadBE32(br);
            var version = ReadBE32(br);
            var dataOff = ReadBE32(br);
            var endianTag = ReadBE32(br);
            return new HeaderV6
            {
                MetaSize = metaSize,
                FileSize = fileSize,
                Version = version,
                DataOffset = dataOff,
                EndianTag = endianTag
            };
        }

        private static uint ReadBE32(BinaryReader br)
        {
            Span<byte> b = stackalloc byte[4];
            br.Read(b);
            return BinaryPrimitives.ReadUInt32BigEndian(b);
        }

        private static ObjectTable? FindObjectTable(byte[] metadata, long fileSize, int dataBase)
        {
            int start = 20;
            int limit = Math.Min(metadata.Length, dataBase);
            int bestCount = 0;
            ObjectTable? best = null;

            for (int pos = start; pos + 4 < limit; pos++)
            {
                int count = BitConverter.ToInt32(metadata, pos);
                if (count <= 0 || count > 200000) continue;

                foreach (var scheme in new[] { ("le64", 20), ("le32", 16) })
                {
                    bool ok = true;
                    int recSize = scheme.Item2;
                    long cursor = pos + 4;

                    for (int i = 0; i < count; i++)
                    {
                        long basePos = cursor + i * recSize;
                        if (basePos + recSize > limit) { ok = false; break; }

                        long pathId;
                        int offRel, size, typeIndex;
                        if (scheme.Item1 == "le64")
                        {
                            pathId = BitConverter.ToInt64(metadata, (int)basePos + 0);
                            offRel = BitConverter.ToInt32(metadata, (int)basePos + 8);
                            size = BitConverter.ToInt32(metadata, (int)basePos + 12);
                            typeIndex = BitConverter.ToInt32(metadata, (int)basePos + 16);
                        }
                        else
                        {
                            pathId = BitConverter.ToInt32(metadata, (int)basePos + 0);
                            offRel = BitConverter.ToInt32(metadata, (int)basePos + 4);
                            size = BitConverter.ToInt32(metadata, (int)basePos + 8);
                            typeIndex = BitConverter.ToInt32(metadata, (int)basePos + 12);
                        }

                        if (size <= 0) { ok = false; break; }
                        long abs = dataBase + (uint)offRel;
                        if (abs <= 0 || abs + (uint)size > fileSize) { ok = false; break; }
                        if (typeIndex < 0 || typeIndex > 4096) { ok = false; break; }
                    }

                    if (ok && count > bestCount)
                    {
                        var list = new List<ObjectEntry>(count);
                        long basePos = pos + 4;
                        for (int i = 0; i < count; i++)
                        {
                            long p = basePos + i * recSize;
                            long pathId;
                            int offRel, size, typeIndex;
                            if (scheme.Item1 == "le64")
                            {
                                pathId = BitConverter.ToInt64(metadata, (int)p + 0);
                                offRel = BitConverter.ToInt32(metadata, (int)p + 8);
                                size = BitConverter.ToInt32(metadata, (int)p + 12);
                                typeIndex = BitConverter.ToInt32(metadata, (int)p + 16);
                            }
                            else
                            {
                                pathId = BitConverter.ToInt32(metadata, (int)p + 0);
                                offRel = BitConverter.ToInt32(metadata, (int)p + 4);
                                size = BitConverter.ToInt32(metadata, (int)p + 8);
                                typeIndex = BitConverter.ToInt32(metadata, (int)p + 12);
                            }
                            list.Add(new ObjectEntry
                            {
                                PathId = pathId,
                                OffsetRel = (uint)offRel,
                                Size = (uint)size,
                                TypeIndex = typeIndex
                            });
                        }

                        best = new ObjectTable
                        {
                            TableOffset = pos,
                            PathIdIs64 = scheme.Item1 == "le64",
                            EntrySize = recSize,
                            Count = count,
                            Entries = list
                        };
                        bestCount = count;
                    }
                }
            }
            return best;
        }

        // ---- DTOs ----
        private struct HeaderV6
        {
            public uint MetaSize;
            public uint FileSize;
            public uint Version;
            public uint DataOffset;
            public uint EndianTag;
        }

        private class ObjectEntry
        {
            public long PathId { get; set; }
            public uint OffsetRel { get; set; }
            public uint Size { get; set; }
            public int TypeIndex { get; set; }
        }

        private class ObjectTable
        {
            public int TableOffset { get; set; }
            public bool PathIdIs64 { get; set; }
            public int EntrySize { get; set; }
            public int Count { get; set; }
            public List<ObjectEntry> Entries { get; set; } = new();
            public ObjectEntry this[int i] => Entries[i];
        }

        private class Manifest
        {
            public int Version { get; set; }
            public int MetaSize { get; set; }
            public long FileSize { get; set; }
            public int DataBase { get; set; }
            public int TableOffset { get; set; }
            public int EntrySize { get; set; }
            public bool PathIdIs64 { get; set; }
            public int Count { get; set; }
            public List<ObjectEntry> Entries { get; set; } = new();
        }

        // --- Публичный API для сборки ---
        public static void BuildAssetsV6(string inDir, string outAssetsPath)
        {
            var manifestPath = Path.Combine(inDir, "manifest.json");
            var metaPath = Path.Combine(inDir, "metadata.bin");
            var objDir = Path.Combine(inDir, "objects");

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                           ?? throw new InvalidDataException("manifest.json invalid");

            var metaBytesFull = File.ReadAllBytes(metaPath);
            byte[] metaCore;
            if (metaBytesFull.Length == manifest.MetaSize) metaCore = metaBytesFull;
            else if (metaBytesFull.Length == manifest.MetaSize + 20) metaCore = metaBytesFull.AsSpan(20, manifest.MetaSize).ToArray();
            else if (metaBytesFull.Length > manifest.MetaSize) metaCore = metaBytesFull.AsSpan(metaBytesFull.Length - manifest.MetaSize, manifest.MetaSize).ToArray();
            else throw new InvalidDataException($"metadata.bin size {metaBytesFull.Length} != MetaSize {manifest.MetaSize}");

            var tbl = FindObjectTableInMeta(metaCore, manifest.Count)
                      ?? throw new InvalidDataException("Object table not found in metadata.bin");

            var files = Directory.GetFiles(objDir, "*.bin").OrderBy(p => p).ToArray();
            if (files.Length != manifest.Count)
                throw new InvalidDataException($"objects count mismatch: {files.Length} vs {manifest.Count}");

            using var fs = File.Create(outAssetsPath);
            using var bw = new BinaryWriter(fs);

            bw.Write(new byte[20]);
            bw.Write(metaCore);
            long dataBase = 20 + metaCore.Length;

            var newOffsets = new (long PathId, uint OffRel, uint Size)[manifest.Count];
            for (int i = 0; i < manifest.Count; i++)
            {
                Align4(bw);
                uint rel = (uint)(fs.Position - dataBase);

                var bytes = File.ReadAllBytes(files[i]);
                bw.Write(bytes);

                newOffsets[i] = (manifest.Entries[i].PathId, rel, (uint)bytes.Length);
            }

            long tableAbsOffset = 20 + tbl.TableOffset;
            fs.Position = tableAbsOffset;
            Span<byte> buf = stackalloc byte[8];

            BinaryPrimitives.WriteInt32LittleEndian(buf[..4], manifest.Count);
            bw.Write(buf[..4]);

            for (int i = 0; i < manifest.Count; i++)
            {
                var m = manifest.Entries[i];
                var p = newOffsets[i];

                if (tbl.PathIdIs64)
                {
                    BinaryPrimitives.WriteInt64LittleEndian(buf[..8], p.PathId);
                    bw.Write(buf[..8]);
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(buf[..4], checked((int)p.PathId));
                    bw.Write(buf[..4]);
                }

                BinaryPrimitives.WriteUInt32LittleEndian(buf[..4], p.OffRel); bw.Write(buf[..4]);
                BinaryPrimitives.WriteUInt32LittleEndian(buf[..4], p.Size); bw.Write(buf[..4]);
                BinaryPrimitives.WriteInt32LittleEndian(buf[..4], m.TypeIndex); bw.Write(buf[..4]);
            }

            long fileSize = fs.Length;
            fs.Position = 0;
            Span<byte> head = stackalloc byte[20];
            BinaryPrimitives.WriteUInt32BigEndian(head[..4], (uint)metaCore.Length);
            BinaryPrimitives.WriteUInt32BigEndian(head.Slice(4, 4), (uint)fileSize);
            BinaryPrimitives.WriteUInt32BigEndian(head.Slice(8, 4), 6);
            BinaryPrimitives.WriteUInt32BigEndian(head.Slice(12, 4), 0);
            BinaryPrimitives.WriteUInt32BigEndian(head.Slice(16, 4), 0);
            bw.Write(head);
        }

        private static ObjectTable? FindObjectTableInMeta(byte[] metaCore, int expectedCount)
        {
            int start = 0;
            int limit = metaCore.Length;
            ObjectTable? best = null;

            for (int pos = start; pos + 4 <= limit - 16; pos++)
            {
                int count = BitConverter.ToInt32(metaCore, pos);
                if (count <= 0 || count > 200000) continue;
                if (expectedCount > 0 && count != expectedCount) continue;

                foreach (var scheme in new[] { ("le64", 20), ("le32", 16) })
                {
                    bool ok = true;
                    int recSize = scheme.Item2;

                    long blockEnd = pos + 4L + (long)count * recSize;
                    if (blockEnd > limit) { ok = false; }

                    if (ok)
                    {
                        best = new ObjectTable
                        {
                            TableOffset = pos,
                            PathIdIs64 = scheme.Item1 == "le64",
                            EntrySize = recSize,
                            Count = count
                        };
                        return best;
                    }
                }
            }
            return best;
        }
    }
}
