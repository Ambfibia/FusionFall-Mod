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

            Dictionary<int, Node> typeTrees = ParseTypeTreesV6(meta, ref pos, littleEndian, out Dictionary<int, string> typeNames);

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
                ObjectEntry entry = table[i];
                long absolute = entry.OffsetRel;
                if (absolute < 0 || absolute + entry.Size > metaStart)
                {
                    throw new InvalidDataException("Запись " + i + ": неверный диапазон данных");
                }

                byte[] data = fileBytes.AsSpan((int)absolute, (int)entry.Size).ToArray();

                int key = entry.ClassId.HasValue ? entry.ClassId.Value : entry.TypeIndex;
                string typeName;
                if (!typeNames.TryGetValue(key, out typeName))
                {
                    typeName = key.ToString();
                }

                string objectNameSuffix = string.Empty;
                Node tree;
                if (typeTrees.TryGetValue(key, out tree))
                {
                    string extractedName;
                    if (TryExtractMName(fileBytes, (int)absolute, (int)entry.Size, tree, littleEndian, out extractedName) && !string.IsNullOrWhiteSpace(extractedName))
                    {
                        objectNameSuffix = "_" + San(extractedName);
                    }
                }

                string objectFileName = i.ToString("D4") + "_pid" + entry.PathId + "_" + San(typeName) + objectNameSuffix + ".bin";
                File.WriteAllBytes(Path.Combine(objDir, objectFileName), data);
                entries.Add(entry);
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

        private static Dictionary<int, Node> ParseTypeTreesV6(byte[] meta, ref int pos, bool littleEndian,
                                                              out Dictionary<int, string> classToTypeName)
        {
            classToTypeName = new Dictionary<int, string>();
            Dictionary<int, Node> trees = new Dictionary<int, Node>();
            uint typeCount = ReadUInt32(meta, ref pos, littleEndian);
            for (int i = 0; i < typeCount; i++)
            {
                int classId = ReadInt32(meta, ref pos, littleEndian);
                if (classId < 0)
                {
                    if (!LooksLikeAscii(meta, pos)) pos += 16;
                    if (!LooksLikeAscii(meta, pos)) pos += 16;
                }
                Node root = ReadNodeV1(meta, ref pos, littleEndian);
                trees[classId] = root;
                classToTypeName[classId] = root.Type;
            }
            return trees;
        }

        private const int KAlignFlag = 0x4000;

        private static void Align4(ref int position)
        {
            position = (position + 3) & ~3;
        }

        private static int PrimitiveSize(string type)
        {
            return type switch
            {
                "SInt8" or "UInt8" or "char" or "bool" => 1,
                "SInt16" or "UInt16" => 2,
                "SInt32" or "UInt32" or "float" => 4,
                "SInt64" or "UInt64" or "double" => 8,
                _ => -1
            };
        }

        private static string ReadAlignedString(byte[] buffer, int end, ref int position, bool littleEndian)
        {
            if (position + 4 > end)
            {
                return string.Empty;
            }
            int length = littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(position, 4))
                                      : BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(position, 4));
            position += 4;
            if (length < 0 || position + length > end)
            {
                length = Math.Max(0, Math.Min(length, end - position));
            }
            string result = Encoding.UTF8.GetString(buffer, position, Math.Max(0, length));
            position += Math.Max(0, length);
            Align4(ref position);
            return result;
        }

        private static bool TryExtractMName(byte[] file, int start, int size, Node root, bool littleEndian, out string name)
        {
            name = string.Empty;
            int position = start;
            int end = start + size;
            string foundName = string.Empty;

            bool Consume(Node node, ref int readPosition)
            {
                if (node.Name == "m_Name" && node.Type == "string")
                {
                    foundName = ReadAlignedString(file, end, ref readPosition, littleEndian);
                    return true;
                }

                if (node.Type == "string")
                {
                    _ = ReadAlignedString(file, end, ref readPosition, littleEndian);
                    return false;
                }

                if (node.IsArray != 0)
                {
                    if (readPosition + 4 > end)
                    {
                        readPosition = end;
                        return false;
                    }
                    int count = littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(readPosition, 4))
                                             : BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(readPosition, 4));
                    readPosition += 4;
                    Node element = node.Children.Count > 0 ? node.Children[0] : null;
                    if (element == null)
                    {
                        return false;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        if (Consume(element, ref readPosition)) return true;
                    }
                    if ((node.Flags & KAlignFlag) != 0) Align4(ref readPosition);
                    return false;
                }

                Node arrayNode = node.Children.FirstOrDefault(c => c.Name == "Array");
                if (arrayNode != null && arrayNode.Children.Count >= 2)
                {
                    if (readPosition + 4 > end)
                    {
                        readPosition = end;
                        return false;
                    }
                    int count = littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(readPosition, 4))
                                             : BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(readPosition, 4));
                    readPosition += 4;
                    Node dataNode = arrayNode.Children[1];
                    Node element = dataNode.Children.Count > 0 ? dataNode.Children[0] : dataNode;
                    for (int i = 0; i < count; i++)
                    {
                        if (Consume(element, ref readPosition)) return true;
                    }
                    if ((node.Flags & KAlignFlag) != 0) Align4(ref readPosition);
                    return false;
                }

                int primitiveSize = PrimitiveSize(node.Type);
                if (primitiveSize > 0 && node.Children.Count == 0)
                {
                    readPosition = Math.Min(end, readPosition + primitiveSize);
                    if ((node.Flags & KAlignFlag) != 0) Align4(ref readPosition);
                    return false;
                }

                foreach (Node child in node.Children)
                {
                    if (Consume(child, ref readPosition)) return true;
                }
                if ((node.Flags & KAlignFlag) != 0) Align4(ref readPosition);
                return false;
            }

            foreach (Node child in root.Children)
            {
                if (Consume(child, ref position))
                {
                    name = foundName;
                    return true;
                }
            }
            name = foundName;
            return false;
        }

        private static string San(string input, int maxLen = 80)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                builder.Append(invalid.Contains(c) ? '_' : c);
            }
            string result = builder.ToString().Trim();
            if (string.IsNullOrEmpty(result)) return "noname";
            return result.Length > maxLen ? result.Substring(0, maxLen) : result;
        }

        private static (List<ObjectEntry> Objects, int RecLen)? TryParseObjects(byte[] meta, int pos, int count, bool littleEndian, int dataAreaLen)
        {
            (string Name, int RecLen, Func<int, (int PathId, uint Offset, uint Size, int TypeIndex, int? ClassId, int? Flags)> Reader)[] layouts =
                new (string Name, int RecLen, Func<int, (int PathId, uint Offset, uint Size, int TypeIndex, int? ClassId, int? Flags)> Reader)[]
            {
                ("p-o-s-t-16", 16, p => (ReadInt32(meta, ref p, littleEndian), ReadUInt32(meta, ref p, littleEndian), ReadUInt32(meta, ref p, littleEndian), ReadInt32(meta, ref p, littleEndian), null, null)),
                ("p-o-s-t-20", 20, p => { int path = ReadInt32(meta, ref p, littleEndian); uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); uint tail = ReadUInt32(meta, ref p, littleEndian); int classId = (int)(tail & 0xFFFF); int flags = (int)(tail >> 16); return (path, off, size, type, classId, flags); }),
                ("p-o-s-t-24", 24, p => { int path = ReadInt32(meta, ref p, littleEndian); uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); p += 4; uint tail = ReadUInt32(meta, ref p, littleEndian); int classId = (int)(tail & 0xFFFF); int flags = (int)(tail >> 16); return (path, off, size, type, classId, flags); }),
                ("o-s-t-p-16", 16, p => { uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); int path = ReadInt32(meta, ref p, littleEndian); return (path, off, size, type, null, null); }),
                ("p-t-o-s-16", 16, p => { int path = ReadInt32(meta, ref p, littleEndian); int type = ReadInt32(meta, ref p, littleEndian); uint off = ReadUInt32(meta, ref p, littleEndian); uint size = ReadUInt32(meta, ref p, littleEndian); return (path, off, size, type, null, null); })
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
                    (int PathId, uint Offset, uint Size, int TypeIndex, int? ClassId, int? Flags) r = layout.Reader(p);
                    bool cond = r.Offset < dataAreaLen && r.Size > 0 && r.Offset + r.Size <= dataAreaLen && (r.Offset % 4 == 0);
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
                    (int PathId, uint Offset, uint Size, int TypeIndex, int? ClassId, int? Flags) r = layout.Reader(p);
                    bool cond = r.Offset < dataAreaLen && r.Size > 0 && r.Offset + r.Size <= dataAreaLen && (r.Offset % 4 == 0);
                    if (cond)
                    {
                        okFull++;
                        if (r.Offset >= (uint)lastOff)
                        {
                            mono++;
                            lastOff = (int)r.Offset;
                        }
                        objs.Add(new ObjectEntry
                        {
                            PathId = r.PathId,
                            OffsetRel = r.Offset,
                            Size = r.Size,
                            TypeIndex = r.TypeIndex,
                            ClassId = r.ClassId,
                            Flags = r.Flags
                        });
                    }
                    else
                    {
                        objs.Add(new ObjectEntry
                        {
                            PathId = r.PathId,
                            OffsetRel = r.Offset,
                            Size = r.Size,
                            TypeIndex = r.TypeIndex,
                            ClassId = r.ClassId,
                            Flags = r.Flags
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
