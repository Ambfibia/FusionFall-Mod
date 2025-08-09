using FusionFall_Mod.Models;
using System.Text;

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
        private static byte[] BuildUnityWebFile(byte[] compressedData, int uncompressedSize, string versionInfo, string buildInfo, int offset)
        {
            using var outputStream = new MemoryStream(offset + compressedData.Length + 64);
            using var binaryWriter = new BinaryWriter(outputStream, Encoding.ASCII, leaveOpen: true);

            // запись сигнатуры
            binaryWriter.Write(Encoding.ASCII.GetBytes("UnityWeb"));

            // u32 0
            binaryWriter.Write(new byte[4]);

            // версия
            binaryWriter.Write((byte)2);

            // строки версий
            WriteCString(binaryWriter, versionInfo);
            WriteCString(binaryWriter, buildInfo);

            // поле SIZE (будет перезаписано после вычисления last_offset)
            long sizePosition = outputStream.Position;
            binaryWriter.Write(new byte[4]);

            // u16 0
            binaryWriter.Write(new byte[2]);

            long offsetPosition = outputStream.Position;
            // смещение до сжатых данных
            WriteBEUInt16(binaryWriter, (ushort)offset);

            // служебные поля
            binaryWriter.Write(EndianConverter.ToBigEndian(1));
            binaryWriter.Write(EndianConverter.ToBigEndian(1));
            binaryWriter.Write(EndianConverter.ToBigEndian(compressedData.Length));
            binaryWriter.Write(EndianConverter.ToBigEndian(uncompressedSize));
            binaryWriter.Write(EndianConverter.ToBigEndian(offset + compressedData.Length));
            binaryWriter.Write((byte)0);

            if (outputStream.Position > offset)
            {
                offset = Align((int)outputStream.Position, 16);
                long current = outputStream.Position;
                outputStream.Position = offsetPosition;
                WriteBEUInt16(binaryWriter, (ushort)offset);
                outputStream.Position = current;

                current = outputStream.Position;
                outputStream.Position = offsetPosition + 2 + 4 + 4;
                binaryWriter.Write(EndianConverter.ToBigEndian(compressedData.Length));
                binaryWriter.Write(EndianConverter.ToBigEndian(uncompressedSize));
                binaryWriter.Write(EndianConverter.ToBigEndian(offset + compressedData.Length));
                binaryWriter.Write((byte)0);
                outputStream.Position = current;
            }

            int finalLastOffset = offset + compressedData.Length;
            long tempPosition = outputStream.Position;
            outputStream.Position = sizePosition;
            binaryWriter.Write(EndianConverter.ToBigEndian(finalLastOffset));
            outputStream.Position = tempPosition;

            while (outputStream.Position < offset)
            {
                binaryWriter.Write((byte)0);
            }

            binaryWriter.Write(compressedData);
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
        public static async Task PackAsync(List<FileEntry> fileEntries, string outputFile, string flag)
        {
            byte[] headerData = await BuildHeaderData(fileEntries);
            byte[] compressedData = LzmaHelper.CompressData(headerData);
            byte[] finalFile = BuildUnityWebFile(compressedData, headerData.Length, "fusion-2.x.x", "2.5.4b5", UnityHeader.MainHeaderSize);
            await File.WriteAllBytesAsync(outputFile, finalFile);
        }

        /// <summary>
        /// Упаковка файлов из папки.
        /// </summary>
        public static async Task PackAsync(string folderPath, string outputFile, string flag)
        {
            List<FileEntry> entries = CollectFileEntries(folderPath);
            await PackAsync(entries, outputFile, flag);
        }

        /// <summary>
        /// Извлечение файлов из пакета.
        /// </summary>
        public static async Task ExtractAsync(string inputFile, string outputDir)
        {
            byte[] fileContent = await File.ReadAllBytesAsync(inputFile);
            byte[] compData = new byte[fileContent.Length - UnityHeader.MainHeaderSize];
            Buffer.BlockCopy(fileContent, UnityHeader.MainHeaderSize, compData, 0, compData.Length);
            byte[] decomData = LzmaHelper.DecompressData(compData);

            int numFiles = EndianConverter.FromBigEndian(decomData, 0);
            int pos = 4;
            for (int i = 0; i < numFiles; i++)
            {
                int nameStart = pos;
                while (pos < decomData.Length && decomData[pos] != 0)
                {
                    pos++;
                }
                string filename = Encoding.ASCII.GetString(decomData, nameStart, pos - nameStart);
                pos++;
                int offset = EndianConverter.FromBigEndian(decomData, pos);
                pos += 4;
                int size = EndianConverter.FromBigEndian(decomData, pos);
                pos += 4;

                byte[] fileData = new byte[size];
                Buffer.BlockCopy(decomData, offset, fileData, 0, size);
                string outPath = Path.Combine(outputDir, filename);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                await File.WriteAllBytesAsync(outPath, fileData);
            }
        }

    }
}
