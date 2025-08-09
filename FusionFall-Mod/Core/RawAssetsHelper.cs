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
    /// Утилиты для распаковки и упаковки файлов sharedassets*.assets.
    /// </summary>
    public static class RawAssetsHelper
    {
        // --- Минимальные утилиты для (де)сериализации ---
        private static class BE
        {
            public static uint ReadU32(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadUInt32BigEndian(s.Slice(off, 4));
            public static void WriteU32(Span<byte> s, int off, uint v) => BinaryPrimitives.WriteUInt32BigEndian(s.Slice(off, 4), v);
        }

        private static class LE
        {
            public static int ReadS32(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadInt32LittleEndian(s.Slice(off, 4));
            public static uint ReadU32(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(off, 4));
            public static long ReadS64(ReadOnlySpan<byte> s, int off) => BinaryPrimitives.ReadInt64LittleEndian(s.Slice(off, 8));
            public static void WriteS32(Span<byte> s, int off, int v) => BinaryPrimitives.WriteInt32LittleEndian(s.Slice(off, 4), v);
            public static void WriteU32(Span<byte> s, int off, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(off, 4), v);
        }

        // --- Модель манифеста для упаковки ---
        private record RawEntry(int PathID, uint Offset, uint Size, int TypeId, byte[] Extra);

        private record Manifest(
            string UnityVersion,
            int TargetPlatform,
            int HeaderVersion,
            uint MetaDataOffset,
            uint FileSize,
            byte Endianness,
            int EntrySize,
            string MetaPrefixBase64,
            string MetaSuffixBase64,
            List<RawEntry> Entries);

        /// <summary>
        /// Распаковка файла .assets в указанную папку.
        /// </summary>
        public static void Unpack(string inputPath, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var bytes = File.ReadAllBytes(inputPath);
            var ro = new ReadOnlySpan<byte>(bytes);

            // ---- Прочитать заголовок (big-endian) ----
            uint metaSize = BE.ReadU32(ro, 0);
            uint fileSize = BE.ReadU32(ro, 4);
            int headerVer = (int)BE.ReadU32(ro, 8);
            uint dataOffset = BE.ReadU32(ro, 12);
            byte endian = ro[16]; // 0 = little, 1 = big (метаданные)
            int p = 20;

            // UnityVersion (ASCIIZ)
            string unityVer = ReadCString(ro, ref p);
            int targetPlatform = LE.ReadS32(ro, p); p += 4;

            // Поиск начала таблицы объектов
            int metaStart = p;
            int metaEnd = (int)dataOffset;
            var (tableOff, entrySize, n) = FindObjectTable(ro, metaStart, metaEnd, dataOffset, fileSize);
            int tableBytesStart = tableOff + 4;
            int tableBytesEnd = tableBytesStart + n * entrySize;

            byte[] metaPrefix = ro.Slice(0, tableOff).ToArray();
            byte[] metaSuffix = ro.Slice(tableBytesEnd, metaEnd - tableBytesEnd).ToArray();

            var entries = new List<RawEntry>(n);
            for (int i = 0; i < n; i++)
            {
                int epos = tableBytesStart + i * entrySize;
                int pathId = LE.ReadS32(ro, epos + 0);
                uint off = LE.ReadU32(ro, epos + 4);
                uint size = LE.ReadU32(ro, epos + 8);
                int typeId = LE.ReadS32(ro, epos + 12);
                byte[] extra = entrySize > 16 ? ro.Slice(epos + 16, entrySize - 16).ToArray() : Array.Empty<byte>();
                entries.Add(new RawEntry(pathId, off, size, typeId, extra));
            }

            // Пишем объекты
            var objDir = Path.Combine(outDir, "Objects");
            Directory.CreateDirectory(objDir);
            foreach (var e in entries)
            {
                var obj = ro.Slice((int)e.Offset, checked((int)e.Size)).ToArray();
                File.WriteAllBytes(Path.Combine(objDir, $"{e.PathID}.bin"), obj);
            }

            // Пишем манифест
            var manifest = new Manifest(
                UnityVersion: unityVer,
                TargetPlatform: targetPlatform,
                HeaderVersion: headerVer,
                MetaDataOffset: dataOffset,
                FileSize: fileSize,
                Endianness: endian,
                EntrySize: entrySize,
                MetaPrefixBase64: Convert.ToBase64String(metaPrefix),
                MetaSuffixBase64: Convert.ToBase64String(metaSuffix),
                Entries: entries
            );
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(outDir, "out.manifest.json"), manifestJson, new UTF8Encoding(false));

            Console.WriteLine($"Unpacked {n} objects to {objDir}");
        }

        /// <summary>
        /// Упаковка распакованных объектов обратно в .assets.
        /// </summary>
        public static void Pack(string originalAssets, string unpackDir, string outAssets)
        {
            var orig = File.ReadAllBytes(originalAssets);

            var manifestPath = Path.Combine(unpackDir, "out.manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Не найден манифест out.manifest.json в папке распаковки.", manifestPath);

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))
                           ?? throw new Exception("Не удалось разобрать manifest JSON.");

            var metaPrefix = Convert.FromBase64String(manifest.MetaPrefixBase64);
            var metaSuffix = Convert.FromBase64String(manifest.MetaSuffixBase64);

            int entrySize = manifest.EntrySize;
            uint dataOffsetNew = (uint)(metaPrefix.Length + 4 + manifest.Entries.Count * entrySize + metaSuffix.Length);

            using var fsOut = new FileStream(outAssets, FileMode.Create, FileAccess.Write, FileShare.None);
            using var msMeta = new MemoryStream();
            msMeta.Write(metaPrefix, 0, metaPrefix.Length);

            Span<byte> tmp4 = stackalloc byte[4];
            LE.WriteS32(tmp4, 0, manifest.Entries.Count);
            msMeta.Write(tmp4);

            var newEntries = new List<RawEntry>(manifest.Entries.Count);
            long cursor = dataOffsetNew;
            const int ALIGN = 4;
            foreach (var e in manifest.Entries)
            {
                var customPath = Path.Combine(unpackDir, "Objects", $"{e.PathID}.bin");
                byte[] payload = File.Exists(customPath)
                    ? File.ReadAllBytes(customPath)
                    : new ReadOnlySpan<byte>(orig, (int)e.Offset, (int)e.Size).ToArray();

                long pad = (ALIGN - (cursor % ALIGN)) % ALIGN;
                cursor += pad;

                var ne = new RawEntry(e.PathID, (uint)cursor, (uint)payload.Length, e.TypeId, e.Extra);
                newEntries.Add(ne);
                cursor += payload.Length;
            }

            foreach (var e in newEntries)
            {
                Span<byte> row = stackalloc byte[entrySize];
                LE.WriteS32(row, 0, e.PathID);
                LE.WriteU32(row, 4, e.Offset);
                LE.WriteU32(row, 8, e.Size);
                LE.WriteS32(row, 12, e.TypeId);
                if (entrySize > 16)
                    e.Extra.CopyTo(row.Slice(16));
                msMeta.Write(row);
            }
            msMeta.Write(metaSuffix, 0, metaSuffix.Length);

            byte[] metaAll = msMeta.ToArray();
            uint metaSizeNew = (uint)metaAll.Length;
            byte[] header = new byte[20];
            Array.Copy(orig, 0, header, 0, 20);
            BE.WriteU32(header, 0, metaSizeNew);
            BE.WriteU32(header, 8, (uint)manifest.HeaderVersion);
            BE.WriteU32(header, 12, metaSizeNew);

            fsOut.Write(metaAll, 0, metaAll.Length);

            long dataStartPos = fsOut.Position;
            foreach (var e in newEntries)
            {
                long current = fsOut.Position;
                long pad = (ALIGN - (current % ALIGN)) % ALIGN;
                if (pad > 0) fsOut.Write(new byte[pad], 0, (int)pad);

                byte[] payload;
                var customPath = Path.Combine(unpackDir, "Objects", $"{e.PathID}.bin");
                if (File.Exists(customPath)) payload = File.ReadAllBytes(customPath);
                else payload = new ReadOnlySpan<byte>(orig, (int)manifest.Entries.First(x => x.PathID == e.PathID).Offset,
                                                       (int)manifest.Entries.First(x => x.PathID == e.PathID).Size).ToArray();
                fsOut.Write(payload, 0, payload.Length);
            }

            long finalSize = fsOut.Length;
            fsOut.Position = 4;
            Span<byte> be4 = stackalloc byte[4];
            BE.WriteU32(be4, 0, (uint)finalSize);
            fsOut.Write(be4);
            fsOut.Flush();

            Console.WriteLine($"Packed {newEntries.Count} objects -> {outAssets}");
        }

        // --- Вспомогательные методы ---
        private static (int tableOff, int entrySize, int count) FindObjectTable(ReadOnlySpan<byte> data, int searchStart, int searchEnd, uint dataOffset, uint fileSize)
        {
            for (int s = searchStart; s <= searchEnd - 4; s++)
            {
                int n = LE.ReadS32(data, s);
                if (n <= 0 || n > 1_000_000) continue;

                foreach (int entrySize in new[] { 16, 20, 24 })
                {
                    long endPos = s + 4L + (long)n * entrySize;
                    if (endPos > searchEnd) continue;

                    bool ok = true;
                    int checks = Math.Min(n, 64);
                    for (int i = 0; i < checks; i++)
                    {
                        int epos = s + 4 + i * entrySize;
                        uint off = LE.ReadU32(data, epos + 4);
                        uint sz = LE.ReadU32(data, epos + 8);
                        if (!(off >= dataOffset && off + sz <= fileSize && sz > 0))
                        {
                            ok = false;
                            break;
                        }
                    }
                    if (ok) return (s, entrySize, n);
                }
            }
            throw new Exception("Не удалось найти таблицу объектов в метаданных.");
        }

        private static string ReadCString(ReadOnlySpan<byte> src, ref int pos)
        {
            int end = pos;
            while (end < src.Length && src[end] != 0) end++;
            var s = Encoding.UTF8.GetString(src.Slice(pos, end - pos));
            pos = end + 1;
            return s;
        }
    }
}

