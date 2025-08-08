using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FusionFall_Mod.Models;

namespace FusionFall_Mod.Tests;

/// <summary>
/// Вспомогательные методы для тестов.
/// </summary>
internal static class UnityTestHelper
{
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
