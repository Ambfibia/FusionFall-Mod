namespace FusionFall_Mod.Models
{
    public class FileEntry
    {
        public string FileName { get; }
        public string FullPath { get; }
        public long Size { get; }

        public FileEntry(string fileName, string fullPath, long size)
        {
            FileName = fileName;
            FullPath = fullPath;
            Size = size;
        }
    }
}

