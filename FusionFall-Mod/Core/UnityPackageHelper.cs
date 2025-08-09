using FusionFall_Mod.Models;
using System.Text;
using System.Text.Json;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Общие методы для работы с пакетами Unity3D.
    /// </summary>
    public static class UnityPackageHelper
    {
        /// <summary>
        /// Сбор всех файлов из указанной папки.
        /// </summary>
        public static List<FileEntry> CollectFileEntries(string folderPath)
        {
            List<string> files = Directory.GetFiles(folderPath).Select(Path.GetFileName).ToList();

            var scriptAssemblies = files
                .Where(f => f.StartsWith("Assembly", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            var engineAssemblies = files
                .Where(f => (f.StartsWith("Mono", StringComparison.OrdinalIgnoreCase) || f.StartsWith("System", StringComparison.OrdinalIgnoreCase)) && !f.StartsWith("Assembly", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            var otherAssemblies = files
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                            !f.StartsWith("Assembly", StringComparison.OrdinalIgnoreCase) &&
                            !f.StartsWith("Mono", StringComparison.OrdinalIgnoreCase) &&
                            !f.StartsWith("System", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            var assets = files
                .Where(f => !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            List<string> ordered = scriptAssemblies
                .Concat(engineAssemblies)
                .Concat(otherAssemblies)
                .Concat(assets)
                .ToList();

            List<FileEntry> entries = new List<FileEntry>();
            foreach (string fileName in ordered)
            {
                string fullPath = Path.Combine(folderPath, fileName);
                long size = new FileInfo(fullPath).Length;
                entries.Add(new FileEntry(fileName, fullPath, size));
            }
            return entries;
        }

        /// <summary>
        /// Формирование данных заголовка без сжатия.
        /// </summary>
        public static async Task<byte[]> BuildHeaderData(List<FileEntry> fileEntries)
        {
            int indexLength = 4;
            foreach (FileEntry entry in fileEntries)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FileName);
                indexLength += nameBytes.Length + 1 + 4 + 4;
            }

            int dataStart = Align(indexLength, UnityHeader.DataStartOffset);

            // расчёт полного размера блока с учётом выравнивания файлов
            int headerDataSize = dataStart;
            foreach (FileEntry entry in fileEntries)
            {
                headerDataSize += (int)entry.Size;
                headerDataSize = Align(headerDataSize, 4);
            }

            byte[] headerData = new byte[headerDataSize];

            int fileCount = fileEntries.Count;
            Buffer.BlockCopy(EndianConverter.ToBigEndian(fileCount), 0, headerData, 0, 4);

            int metadataPosition = 4;
            int fileDataOffset = dataStart;

            foreach (FileEntry entry in fileEntries)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FileName);
                Buffer.BlockCopy(nameBytes, 0, headerData, metadataPosition, nameBytes.Length);
                metadataPosition += nameBytes.Length;
                headerData[metadataPosition++] = 0;

                Buffer.BlockCopy(EndianConverter.ToBigEndian(fileDataOffset), 0, headerData, metadataPosition, 4);
                metadataPosition += 4;
                Buffer.BlockCopy(EndianConverter.ToBigEndian((int)entry.Size), 0, headerData, metadataPosition, 4);
                metadataPosition += 4;

                // смещение следующего файла с учётом выравнивания по 4 байта
                fileDataOffset += (int)entry.Size;
                fileDataOffset = Align(fileDataOffset, 4);
            }

            int fileDataPosition = dataStart;
            foreach (FileEntry entry in fileEntries)
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(entry.FullPath);
                Buffer.BlockCopy(fileBytes, 0, headerData, fileDataPosition, fileBytes.Length);
                fileDataPosition += fileBytes.Length;

                // добавление паддинга между файлами
                int padding = Align(fileDataPosition, 4) - fileDataPosition;
                if (padding > 0)
                {
                    headerData[fileDataPosition] = 0x01;
                    fileDataPosition += padding;
                }
            }

            return headerData;
        }

        /// <summary>
        /// Формирование данных заголовка на основе папки.
        /// </summary>
        public static async Task<byte[]> BuildHeaderData(string folderPath)
        {
            List<FileEntry> entries = CollectFileEntries(folderPath);
            return await BuildHeaderData(entries);
        }

        /// <summary>
        /// Формирование полного файла UnityWeb.
        /// </summary>
        private static byte[] BuildUnityWebFile(byte[] compressedData, int uncompressedSize, UnityHeader header)
        {
            using var outputStream = new MemoryStream(1024 + compressedData.Length);
            using var writer = new BinaryWriter(outputStream, Encoding.ASCII, leaveOpen: true);

            // сигнатура
            writer.Write(Encoding.ASCII.GetBytes(header.FlagFile)); // 8 байт

            // u32 0
            writer.Write(0u);

            // версия
            writer.Write(header.Info.MajorVersion);

            // версии (C-строки)
            WriteCString(writer, header.Info.VersionInfo);
            WriteCString(writer, header.Info.BuildInfo);

            // SIZE/LastOffset (BE u32) — заполним позже
            long sizePos = outputStream.Position;
            writer.Write(new byte[4]);

            // u16 0
            writer.Write((byte)0);
            writer.Write((byte)0);

            // место для first_offset (BE u16)
            long firstOffsetPos = outputStream.Position;
            WriteBEUInt16(writer, 0); // placeholder

            // служебные поля
            writer.Write(EndianConverter.ToBigEndian(1));
            writer.Write(EndianConverter.ToBigEndian(1));
            writer.Write(EndianConverter.ToBigEndian(compressedData.Length));
            writer.Write(EndianConverter.ToBigEndian(uncompressedSize));

            // дублирующий last_offset — тоже заполним позже
            long lastOffset2Pos = outputStream.Position;
            writer.Write(new byte[4]);

            // завершающий байт 0
            writer.Write((byte)0);

            // === Новый расчёт смещения ===
            int endOfHeader = (int)outputStream.Position;

            // «хотим минимум 1 байт паддинга» → затем выравниваем до 4 байт
            int offset = Align(endOfHeader + 1, 4);

            // проставляем first_offset (BE u16)
            long cur = outputStream.Position;
            outputStream.Position = firstOffsetPos;
            WriteBEUInt16(writer, (ushort)offset);
            outputStream.Position = cur;

            // считаем last_offset = offset + compressedData.Length
            int finalLastOffset = offset + compressedData.Length;

            // проставляем оба last_offset (оба в BE u32)
            outputStream.Position = sizePos;
            writer.Write(EndianConverter.ToBigEndian(finalLastOffset));
            outputStream.Position = lastOffset2Pos;
            writer.Write(EndianConverter.ToBigEndian(finalLastOffset));
            outputStream.Position = cur;

            // паддинг до offset
            while (outputStream.Position < offset) writer.Write((byte)0);

            // сжатые данные
            writer.Write(compressedData);

            return outputStream.ToArray();
        }


        /// <summary>
        /// Запись строки с завершающим нулём.
        /// </summary>
        private static void WriteCString(BinaryWriter writer, string value)
        {
            byte[] stringBytes = Encoding.UTF8.GetBytes(value);
            writer.Write(stringBytes);
            writer.Write((byte)0);
        }

        /// <summary>
        /// Чтение строки до нулевого байта.
        /// </summary>
        private static string ReadCString(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0)
            {
                bytes.Add(b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Запись 16-битного числа в Big-Endian.
        /// </summary>
        private static void WriteBEUInt16(BinaryWriter writer, ushort value)
        {
            writer.Write((byte)(value >> 8));
            writer.Write((byte)(value & 0xFF));
        }

        /// <summary>
        /// Выравнивание значения до ближайшего кратного.
        /// </summary>
        private static int Align(int value, int alignment)
        {
            if (alignment <= 0)
            {
                return value;
            }
            int remainder = value % alignment;
            return remainder == 0 ? value : value + (alignment - remainder);
        }

        /// <summary>
        /// Упаковка файлов в формат unity3d.
        /// </summary>
        public static async Task PackAsync(List<FileEntry> fileEntries, string outputFile, UnityHeader header)
        {
            byte[] headerData = await BuildHeaderData(fileEntries);
            byte[] compressedData = LzmaHelper.CompressData(headerData);
            byte[] finalFile = BuildUnityWebFile(compressedData, headerData.Length, header);
            await File.WriteAllBytesAsync(outputFile, finalFile);
        }

        /// <summary>
        /// Упаковка файлов из папки.
        /// </summary>
        public static async Task PackAsync(string folderPath, string outputFile, string flag)
        {
            UnityHeader header = new UnityHeader(flag);
            string jsonPath = Path.Combine(folderPath, "header.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    HeaderInfo? info = JsonSerializer.Deserialize<HeaderInfo>(await File.ReadAllTextAsync(jsonPath));
                    if (info != null)
                    {
                        header.Info.MajorVersion = info.MajorVersion;
                        header.Info.VersionInfo = info.VersionInfo;
                        header.Info.BuildInfo = info.BuildInfo;
                    }
                }
                catch
                {
                    // если JSON некорректен, используем значения по умолчанию
                }
            }

            List<FileEntry> entries = CollectFileEntries(folderPath);
            entries.RemoveAll(e => e.FileName.Equals("header.json", StringComparison.OrdinalIgnoreCase));
            await PackAsync(entries, outputFile, header);
        }

        /// <summary>
        /// Извлечение файлов из пакета.
        /// </summary>
        public static async Task ExtractAsync(string inputFile, string outputDir)
        {
            byte[] fileContent = await File.ReadAllBytesAsync(inputFile);
            using var stream = new MemoryStream(fileContent);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            // 1) Заголовок
            string flag = Encoding.ASCII.GetString(reader.ReadBytes(8)); // "UnityWeb" или "streamed"
            _ = reader.ReadUInt32();                                     // u32 0
            byte major = reader.ReadByte();                              // версия формата (2 или 3)
            string versionInfo = ReadCString(reader);
            string buildInfo = ReadCString(reader);

            // Поле SIZE/LastOffset (BE u32) — заглушка, но читаем для целостности
            int lastOffsetBE1 = ReadBEInt32(reader);

            // u16 0
            _ = ReadBEUInt16(reader);

            // Смещение до сжатых данных (BE u16) — ключевое поле
            ushort firstOffset = ReadBEUInt16(reader);

            // Служебные поля
            _ = ReadBEInt32(reader); // 1
            _ = ReadBEInt32(reader); // 1
            int compressedSize = ReadBEInt32(reader);
            int uncompressedSize = ReadBEInt32(reader);
            int lastOffsetBE2 = ReadBEInt32(reader);
            _ = reader.ReadByte(); // 0

            // 2) Сохраняем header.json (как у вас было)
            HeaderInfo info = new HeaderInfo
            {
                MajorVersion = major,
                VersionInfo = versionInfo,
                BuildInfo = buildInfo
            };
            Directory.CreateDirectory(outputDir);
            string jsonText = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(outputDir, "header.json"), jsonText);

            // 3) Забираем ровно compressedSize байт начиная с firstOffset
            int start = firstOffset;
            if (start < 0 || start >= fileContent.Length) throw new InvalidDataException("Некорректное смещение до данных.");
            if (compressedSize <= 0 || start + compressedSize > fileContent.Length)
            {
                // подстраховка на случай «битых» чисел в заголовке
                compressedSize = fileContent.Length - start;
            }

            byte[] compData = new byte[compressedSize];
            Buffer.BlockCopy(fileContent, start, compData, 0, compressedSize);

            // 4) LZMA → заголовок содержимого
            byte[] decomData = LzmaHelper.DecompressData(compData);

            // 5) Парсим индекс и пишем файлы (UTF-8 имена!)
            int numFiles = EndianConverter.FromBigEndian(decomData, 0);
            int pos = 4;
            for (int i = 0; i < numFiles; i++)
            {
                int nameStart = pos;
                while (pos < decomData.Length && decomData[pos] != 0) pos++;
                if (pos >= decomData.Length) throw new InvalidDataException("Повреждён индекс имён.");
                string filename = Encoding.UTF8.GetString(decomData, nameStart, pos - nameStart);
                pos++; // нулевой терминатор

                int offset = EndianConverter.FromBigEndian(decomData, pos); pos += 4;
                int size = EndianConverter.FromBigEndian(decomData, pos); pos += 4;
                if (offset < 0 || size < 0 || offset + size > decomData.Length)
                    throw new InvalidDataException("Выход за пределы распакованных данных.");

                byte[] fileData = new byte[size];
                Buffer.BlockCopy(decomData, offset, fileData, 0, size);

                string outPath = Path.Combine(outputDir, filename);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                await File.WriteAllBytesAsync(outPath, fileData);
            }
        }

        private static ushort ReadBEUInt16(BinaryReader reader)
        {
            int b1 = reader.ReadByte();
            int b2 = reader.ReadByte();
            return (ushort)((b1 << 8) | b2);
        }

        private static int ReadBEInt32(BinaryReader reader)
        {
            int b1 = reader.ReadByte();
            int b2 = reader.ReadByte();
            int b3 = reader.ReadByte();
            int b4 = reader.ReadByte();
            return (b1 << 24) | (b2 << 16) | (b3 << 8) | b4;
        }
    }
}
