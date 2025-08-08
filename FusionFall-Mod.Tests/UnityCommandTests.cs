using FusionFall_Mod.Models;
using FusionFall_Mod.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FusionFall_Mod.Tests;

/// <summary>
/// Тесты для команд упаковки и распаковки.
/// </summary>
public class UnityCommandTests
{
    public static IEnumerable<object[]> Cases => new[]
    {
        new object[] { "1", "main.unity3d" },
        new object[] { "2", "01092002.unity3d" },
        new object[] { "3", "FFHeroes.unity3d" }
    };

    /// <summary>
    /// Проверяет упаковку, распаковку и извлечение необработанных данных.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PackAndExtract(string caseFolder, string fileName)
    {
        // определяем путь к каталогу тестовых данных относительно корня проекта
        string rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string baseDir = Path.Combine(rootDir, "test", caseFolder);
        string originalPath = Path.Combine(baseDir, fileName);
        string unpackedDir = Path.Combine(baseDir, fileName + "_unpacked");
        string workDir = Path.Combine(baseDir, "work");
        Directory.CreateDirectory(workDir);

        // упаковка сжатого файла
        string repackedPath = Path.Combine(workDir, "repacked.unity3d");
        await UnityPackageHelper.PackAsync(unpackedDir, repackedPath, true, UnityHeader.DefaultFlag);
        byte[] originalBytes = await File.ReadAllBytesAsync(originalPath);
        byte[] repackedBytes = await File.ReadAllBytesAsync(repackedPath);
        if (!originalBytes.SequenceEqual(repackedBytes))
        {
            string origExtract = Path.Combine(workDir, "orig");
            string newExtract = Path.Combine(workDir, "new");
            Directory.CreateDirectory(origExtract);
            Directory.CreateDirectory(newExtract);
            await UnityPackageHelper.ExtractAsync(originalPath, origExtract);
            await UnityPackageHelper.ExtractAsync(repackedPath, newExtract);
            var diffs = UnityTestHelper.CompareDirectories(origExtract, newExtract);
            Assert.True(false, "Различия: " + string.Join(", ", diffs));
        }

        // распаковка и сравнение
        string extractDir = Path.Combine(workDir, "extract");
        Directory.CreateDirectory(extractDir);
        await UnityPackageHelper.ExtractAsync(originalPath, extractDir);
        var diff2 = UnityTestHelper.CompareDirectories(unpackedDir, extractDir);
        Assert.Empty(diff2);

        // извлечение необработанного заголовка и проверка
        byte[] expectedRaw = await UnityPackageHelper.BuildHeaderData(unpackedDir);
        byte[] actualRaw = await UnityPackageHelper.ExtractRawAsync(originalPath);
        Assert.Equal(expectedRaw, actualRaw);

        // упаковка без сжатия и проверка
        string repackedRaw = Path.Combine(workDir, "repacked_raw.unity3d");
        await UnityPackageHelper.PackAsync(unpackedDir, repackedRaw, false, UnityHeader.DefaultFlag);
        byte[] uncompressedData = UnityTestHelper.ReadUncompressedData(repackedRaw);
        Assert.Equal(expectedRaw, uncompressedData);
    }
}
