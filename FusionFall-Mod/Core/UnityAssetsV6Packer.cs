using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
            byte[] fileBytes = File.ReadAllBytes(assetsPath);
            if (fileBytes.Length < 20)
            {
                throw new InvalidDataException("Файл слишком мал.");
            }

            uint metaSize = BinaryPrimitives.ReadUInt32BigEndian(fileBytes.AsSpan(0, 4));
            uint fileSize = BinaryPrimitives.ReadUInt32BigEndian(fileBytes.AsSpan(4, 4));
            uint version = BinaryPrimitives.ReadUInt32BigEndian(fileBytes.AsSpan(8, 4));
            if (version != 6)
            {
                throw new NotSupportedException($"SerializedFile version {version} != 6");
            }

            long metaStart = (long)fileSize - metaSize;
            if (metaStart < 0 || metaStart > fileBytes.LongLength)
            {
                throw new InvalidDataException("Неверное положение метаданных.");
            }

            byte[] meta = new byte[metaSize];
            Array.Copy(fileBytes, (int)metaStart, meta, 0, (int)metaSize);
            bool littleEndian = meta[0] == 0;
            int pos = 1;

            Dictionary<int, string> typeNames = ParseTypesV6(meta, ref pos, littleEndian);

            int objectCount = ReadInt32(meta, ref pos, littleEndian);
            (List<ObjectEntry> Objects, int RecLen)? parsed =
                TryParseObjects(meta, pos, objectCount, littleEndian, (int)metaStart);
            if (parsed == null)
            {
                throw new InvalidDataException("Не удалось разобрать таблицу объектов.");
            }

            List<ObjectEntry> objects = parsed.Value.Objects;
            int recLen = parsed.Value.RecLen;

            ObjectTable table = new ObjectTable
            {
                TableOffset = pos - 4,
                PathIdIs64 = false,
                EntrySize = recLen,
                Count = objectCount,
                Entries = objects
            };

            Directory.CreateDirectory(outDir);
            string metaPath = Path.Combine(outDir, "metadata.bin");
            string objDir = Path.Combine(outDir, "objects");
            Directory.CreateDirectory(objDir);
            File.WriteAllBytes(metaPath, meta);

            List<ObjectEntry> entries = new List<ObjectEntry>();
            for (int i = 0; i < table.Count; i++)
            {
                ObjectEntry e = table[i];
                long abs = e.OffsetRel;
                if (abs < 0 || abs + e.Size > metaStart)
                {
                    throw new InvalidDataException($"Запись {i}: неверный диапазон данных");
                }

                byte[] data = fileBytes.AsSpan((int)abs, (int)e.Size).ToArray();
                string objName = $"{i:D4}_pid{e.PathId}_t{e.TypeIndex}.bin";
                File.WriteAllBytes(Path.Combine(objDir, objName), data);
                entries.Add(e);
            }

            Manifest manifest = new Manifest
            {
                Version = (int)version,
                MetaSize = (int)metaSize,
                FileSize = fileSize,
                DataBase = (int)metaStart,
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
            uint metaSize = ReadBE32(br);
            uint fileSize = ReadBE32(br);
            uint version = ReadBE32(br);
            uint dataOff = ReadBE32(br);
            uint endianTag = ReadBE32(br);
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

        private static int ReadInt32(byte[] data, ref int pos, bool littleEndian)
        {
            int value = littleEndian
                ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4))
                : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos, 4));
            pos += 4;
            return value;
        }

        private static uint ReadUInt32(byte[] data, ref int pos, bool littleEndian)
        {
            uint value = littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            pos += 4;
            return value;
        }

        private static string ReadCString(byte[] data, ref int pos)
        {
            int end = pos;
            while (end < data.Length && data[end] != 0)
            {
                end++;
            }
            if (end >= data.Length)
            {
                throw new InvalidDataException($"Не найден конец строки по адресу {pos}");
            }
            string s = Encoding.ASCII.GetString(data.AsSpan(pos, end - pos));
            pos = end + 1;
            return s;
        }

        private static bool LooksLikeAscii(byte[] data, int pos, int minLen = 3)
        {
            int end = pos;
            int limit = Math.Min(data.Length, pos + 128);
            while (end < limit && data[end] != 0)
            {
                byte c = data[end];
                if (c < 32 || c >= 127)
                {
                    return false;
                }
                end++;
            }
            if (end == pos || end - pos < minLen || end >= data.Length)
            {
                return false;
            }
            return true;
        }

        private class Node
        {
            public string Type = string.Empty;
            public string Name = string.Empty;
            public int Size;
            public int Index;
            public int IsArray;
            public int Version;
            public int Flags;
            public List<Node> Children = new List<Node>();
        }

        private static Node ReadNodeV1(byte[] meta, ref int pos, bool littleEndian)
        {
            string type = ReadCString(meta, ref pos);
            string name = ReadCString(meta, ref pos);
            int size = ReadInt32(meta, ref pos, littleEndian);
            int index = ReadInt32(meta, ref pos, littleEndian);
            int isArray = ReadInt32(meta, ref pos, littleEndian);
            int version = ReadInt32(meta, ref pos, littleEndian);
            int flags = ReadInt32(meta, ref pos, littleEndian);
            int childCount = ReadInt32(meta, ref pos, littleEndian);
            List<Node> children = new List<Node>();
            for (int i = 0; i < childCount; i++)
            {
                Node ch = ReadNodeV1(meta, ref pos, littleEndian);
                children.Add(ch);
            }
            return new Node
            {
                Type = type,
                Name = name,
                Size = size,
                Index = index,
                IsArray = isArray,
                Version = version,
                Flags = flags,
                Children = children
            };
        }

        private static Dictionary<int, string> ParseTypesV6(byte[] meta, ref int pos, bool littleEndian)
        {
            Dictionary<int, string> classToName = new Dictionary<int, string>();
            uint typeCount = ReadUInt32(meta, ref pos, littleEndian);
            for (int i = 0; i < typeCount; i++)
            {
                int classId = ReadInt32(meta, ref pos, littleEndian);
                if (classId < 0)
                {
                    if (!LooksLikeAscii(meta, pos))
                    {
                        pos += 16;
                    }
                    if (!LooksLikeAscii(meta, pos))
                    {
                        pos += 16;
                    }
                }
                Node node = ReadNodeV1(meta, ref pos, littleEndian);
                classToName[classId] = node.Type;
            }
            return classToName;
        }

        private static (List<ObjectEntry> Objects, int RecLen)? TryParseObjects(byte[] meta, int pos, int count, bool littleEndian, int dataAreaLen)
        {
            (string Name, int RecLen, Func<int, (int Path, uint Off, uint Size, int Type)> Reader)[] layouts =
                new (string Name, int RecLen, Func<int, (int Path, uint Off, uint Size, int Type)> Reader)[]
            {
                ("p-o-s-t-16", 16, p => (ReadInt32(meta, ref p, littleEndian), ReadUInt32(meta, ref p, littleEndian), ReadUInt32(meta, ref p, littleEndian), ReadInt32(meta, ref p, littleEndian))),
                ("p-o-s-t-20", 20, p => { int path = ReadInt32(meta, ref p, littleEndian); uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); p += 4; return (path, off, size, type); }),
                ("p-o-s-t-24", 24, p => { int path = ReadInt32(meta, ref p, littleEndian); uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); p += 8; return (path, off, size, type); }),
                ("o-s-t-p-16", 16, p => { uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); int path = ReadInt32(meta, ref p, littleEndian); return (path, off, size, type); }),
                ("p-t-o-s-16", 16, p => { int path = ReadInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); return (path, off, size, type); })
            };

            double bestValid = 0.0;
            double bestMono = 0.0;
            List<ObjectEntry>? bestObjects = null;
            int bestRec = 0;

            foreach (var layout in layouts)
            {
                int end = pos + count * layout.RecLen;
                if (end > meta.Length)
                {
                    continue;
                }

                int ok = 0;
                int step = Math.Max(1, count / 128);
                for (int i = 0; i < count; i += step)
                {
                    int p = pos + i * layout.RecLen;
                    (int Path, uint Off, uint Size, int Type) r = layout.Reader(p);
                    bool cond = r.Off < dataAreaLen && r.Size > 0 && r.Off + r.Size <= dataAreaLen && (r.Off % 4 == 0);
                    if (cond)
                    {
                        ok++;
                    }
                }
                if (ok < (count / step) * 9 / 10)
                {
                    continue;
                }

                int okFull = 0;
                int mono = 0;
                List<ObjectEntry> objs = new List<ObjectEntry>(count);
                int lastOff = -1;
                for (int i = 0; i < count; i++)
                {
                    int p = pos + i * layout.RecLen;
                    (int Path, uint Off, uint Size, int Type) r = layout.Reader(p);
                    bool cond = r.Off < dataAreaLen && r.Size > 0 && r.Off + r.Size <= dataAreaLen && (r.Off % 4 == 0);
                    if (cond)
                    {
                        okFull++;
                        if (r.Off >= (uint)lastOff)
                        {
                            mono++;
                            lastOff = (int)r.Off;
                        }
                        objs.Add(new ObjectEntry
                        {
                            PathId = r.Path,
                            OffsetRel = r.Off,
                            Size = r.Size,
                            TypeIndex = r.Type
                        });
                    }
                    else
                    {
                        objs.Add(new ObjectEntry
                        {
                            PathId = r.Path,
                            OffsetRel = r.Off,
                            Size = r.Size,
                            TypeIndex = r.Type
                        });
                    }
                }

                double validRatio = (double)okFull / count;
                double monoRatio = okFull > 0 ? (double)mono / okFull : 0.0;
                if (validRatio > bestValid || (Math.Abs(validRatio - bestValid) < 0.0001 && monoRatio > bestMono))
                {
                    bestValid = validRatio;
                    bestMono = monoRatio;
                    bestObjects = objs;
                    bestRec = layout.RecLen;
                }
            }

            if (bestObjects == null || bestValid < 0.95)
            {
                return null;
            }

            return (bestObjects, bestRec);
        }

        // --- Публичный API для сборки ---
        public static void BuildAssetsV6(string inDir, string outAssetsPath)
        {
            string manifestPath = Path.Combine(inDir, "manifest.json");
            string metaPath = Path.Combine(inDir, "metadata.bin");
            string objDir = Path.Combine(inDir, "objects");

            Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                           ?? throw new InvalidDataException("manifest.json invalid");

            byte[] metaBytesFull = File.ReadAllBytes(metaPath);
            byte[] metaCore;
            if (metaBytesFull.Length == manifest.MetaSize) metaCore = metaBytesFull;
            else if (metaBytesFull.Length == manifest.MetaSize + 20) metaCore = metaBytesFull.AsSpan(20, manifest.MetaSize).ToArray();
            else if (metaBytesFull.Length > manifest.MetaSize) metaCore = metaBytesFull.AsSpan(metaBytesFull.Length - manifest.MetaSize, manifest.MetaSize).ToArray();
            else throw new InvalidDataException($"metadata.bin size {metaBytesFull.Length} != MetaSize {manifest.MetaSize}");

            ObjectTable tbl = FindObjectTableInMeta(metaCore, manifest.Count)
                      ?? throw new InvalidDataException("Object table not found in metadata.bin");

            string[] files = Directory.GetFiles(objDir, "*.bin").OrderBy(p => p).ToArray();
            if (files.Length != manifest.Count)
                throw new InvalidDataException($"objects count mismatch: {files.Length} vs {manifest.Count}");

            using FileStream fs = File.Create(outAssetsPath);
            using BinaryWriter bw = new BinaryWriter(fs);

            bw.Write(new byte[20]);
            bw.Write(metaCore);
            long dataBase = 20 + metaCore.Length;

            (long PathId, uint OffRel, uint Size)[] newOffsets = new (long PathId, uint OffRel, uint Size)[manifest.Count];
            for (int i = 0; i < manifest.Count; i++)
            {
                Align4(bw);
                uint rel = (uint)(fs.Position - dataBase);

                byte[] bytes = File.ReadAllBytes(files[i]);
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
                ObjectEntry m = manifest.Entries[i];
                (long PathId, uint OffRel, uint Size) p = newOffsets[i];

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

                foreach ((string, int) scheme in new[] { ("le64", 20), ("le32", 16) })
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
