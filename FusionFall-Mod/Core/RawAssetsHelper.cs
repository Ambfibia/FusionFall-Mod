using System.IO;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Обёртки для распаковки и сборки файлов sharedassets*.assets.
    /// </summary>
    public static class RawAssetsHelper
    {
        /// <summary>
        /// Распаковать файл .assets в указанную папку.
        /// </summary>
        public static void Unpack(string inputPath, string outDir)
        {
            UnityAssetsV6Packer.UnpackAssetsV6(inputPath, outDir);
        }

        /// <summary>
        /// Собрать файл .assets из распакованной структуры.
        /// </summary>
        public static void Pack(string unpackDir, string outAssets)
        {
            UnityAssetsV6Packer.BuildAssetsV6(unpackDir, outAssets);
        }
    }
}
