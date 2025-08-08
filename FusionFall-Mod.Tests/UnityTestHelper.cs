using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FusionFall_Mod.Models;
using FusionFall_Mod.Utilities;

namespace FusionFall_Mod.Tests;

/// <summary>
/// Вспомогательный класс для упаковки и распаковки файлов Unity3D.
/// </summary>
internal static class UnityTestHelper
{
    /// <summary>
    /// Упаковывает файлы из папки в формат unity3d.
    /// </summary>
    public static async Task PackAsync(string folderPath, string outputFile, bool compress, string flag)
    {
        List<FileEntry> entries = CollectFileEntries(folderPath);
        UnityHeader header = new UnityHeader(flag);

        byte[] headerData = await BuildHeaderData(entries);
        header.FileUnzipSize = headerData.Length;
        byte[] compData = compress ? LzmaHelper.CompressData(headerData) : headerData;
        byte[] mainHeader = BuildMainHeader(header, compData.Length);

        byte[] outputData = new byte[mainHeader.Length + compData.Length];
        Buffer.BlockCopy(mainHeader, 0, outputData, 0, mainHeader.Length);
        Buffer.BlockCopy(compData, 0, outputData, mainHeader.Length, compData.Length);

        await File.WriteAllBytesAsync(outputFile, outputData);
    }

    /// <summary>
    /// Собирает список файлов для упаковки.
    /// </summary>
    private static List<FileEntry> CollectFileEntries(string folderPath)
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
    /// Формирует данные заголовка без сжатия.
    /// </summary>
    public static async Task<byte[]> BuildHeaderData(string folderPath)
    {
        List<FileEntry> entries = CollectFileEntries(folderPath);
        return await BuildHeaderData(entries);
    }

    /// <summary>
    /// Формирует данные заголовка без сжатия на основе списка файлов.
    /// </summary>
    private static async Task<byte[]> BuildHeaderData(List<FileEntry> fileEntries)
    {
        long totalFileDataSize = fileEntries.Sum(entry => entry.Size);
        int finalBytes = 2;
        int headerDataSize = UnityHeader.DataStartOffset + (int)totalFileDataSize + finalBytes;
        byte[] headerData = new byte[headerDataSize];

        int numFiles = fileEntries.Count;
        Buffer.BlockCopy(EndianConverter.ToBigEndian(numFiles), 0, headerData, 0, 4);

        int metadataPos = 4;
        int fileDataOffset = UnityHeader.DataStartOffset;

        foreach (FileEntry fileEntry in fileEntries)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(fileEntry.FileName);
            Buffer.BlockCopy(nameBytes, 0, headerData, metadataPos, nameBytes.Length);
            metadataPos += nameBytes.Length;
            headerData[metadataPos++] = 0;

            Buffer.BlockCopy(EndianConverter.ToBigEndian(fileDataOffset), 0, headerData, metadataPos, 4);
            metadataPos += 4;
            Buffer.BlockCopy(EndianConverter.ToBigEndian((int)fileEntry.Size), 0, headerData, metadataPos, 4);
            metadataPos += 4;

            fileDataOffset += (int)fileEntry.Size;
        }

        int fileDataPos = UnityHeader.DataStartOffset;
        foreach (var fileEntry in fileEntries)
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(fileEntry.FullPath);
            Buffer.BlockCopy(fileBytes, 0, headerData, fileDataPos, fileBytes.Length);
            fileDataPos += fileBytes.Length;
        }

        return headerData;
    }

    /// <summary>
    /// Формирует основной заголовок.
    /// </summary>
    private static byte[] BuildMainHeader(UnityHeader header, int compressedDataLength)
    {
        byte[] mainHeader = new byte[UnityHeader.MainHeaderSize];

        byte[] flagBytes = Encoding.ASCII.GetBytes(header.FlagFile);
        Buffer.BlockCopy(flagBytes, 0, mainHeader, 0, Math.Min(flagBytes.Length, 8));

        mainHeader[12] = header.MajorVersion;
        byte[] ver2Bytes = Encoding.ASCII.GetBytes(header.VersionInfo.PadRight(12, '\0'));
        Buffer.BlockCopy(ver2Bytes, 0, mainHeader, 13, 12);
        byte[] ver3Bytes = Encoding.ASCII.GetBytes(header.BuildInfo.PadRight(7, '\0'));
        Buffer.BlockCopy(ver3Bytes, 0, mainHeader, 26, 7);

        header.FileSize = UnityHeader.MainHeaderSize + compressedDataLength;
        Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FileSize), 0, mainHeader, 34, 4);
        Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FirstOffset), 0, mainHeader, 38, 4);

        mainHeader[45] = 1;
        mainHeader[49] = 1;

        header.FileZipSize = compressedDataLength;
        Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FileZipSize), 0, mainHeader, 50, 4);
        Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FileUnzipSize), 0, mainHeader, 54, 4);

        header.LastOffset = UnityHeader.MainHeaderSize + compressedDataLength;
        Buffer.BlockCopy(EndianConverter.ToBigEndian(header.LastOffset), 0, mainHeader, 58, 4);

        return mainHeader;
    }

    /// <summary>
    /// Извлекает файлы из пакета.
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

    /// <summary>
    /// Извлекает необработанный заголовок из файла unity3d.
    /// </summary>
    public static async Task<byte[]> ExtractRawAsync(string inputFile)
    {
        byte[] fileContent = await File.ReadAllBytesAsync(inputFile);
        byte[] compData = new byte[fileContent.Length - UnityHeader.MainHeaderSize];
        Buffer.BlockCopy(fileContent, UnityHeader.MainHeaderSize, compData, 0, compData.Length);
        return LzmaHelper.DecompressData(compData);
    }

    /// <summary>
    /// Сравнивает содержимое двух директорий.
    /// </summary>
    public static IEnumerable<string> CompareDirectories(string dir1, string dir2)
    {
        var files1 = Directory.GetFiles(dir1);
        var files2 = Directory.GetFiles(dir2);
        HashSet<string> names1 = files1.Select(Path.GetFileName).ToHashSet();
        HashSet<string> names2 = files2.Select(Path.GetFileName).ToHashSet();
        List<string> diffs = new List<string>();
        foreach (string name in names1.Union(names2))
        {
            string path1 = Path.Combine(dir1, name);
            string path2 = Path.Combine(dir2, name);
            if (!File.Exists(path1) || !File.Exists(path2))
            {
                diffs.Add(name);
            }
            else
            {
                byte[] b1 = File.ReadAllBytes(path1);
                byte[] b2 = File.ReadAllBytes(path2);
                if (!b1.SequenceEqual(b2))
                {
                    diffs.Add(name);
                }
            }
        }
        return diffs;
    }

    /// <summary>
    /// Читает несжатые данные из файла unity3d.
    /// </summary>
    public static byte[] ReadUncompressedData(string inputFile)
    {
        byte[] fileContent = File.ReadAllBytes(inputFile);
        byte[] data = new byte[fileContent.Length - UnityHeader.MainHeaderSize];
        Buffer.BlockCopy(fileContent, UnityHeader.MainHeaderSize, data, 0, data.Length);
        return data;
    }
}
