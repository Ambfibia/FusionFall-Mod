using System.Collections.Generic;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Манифест распакованных объектов.
    /// </summary>
    internal class Manifest
    {
        public int Version { get; set; }
        public int MetaSize { get; set; }
        public long FileSize { get; set; }
        public int DataBase { get; set; }
        public int TableOffset { get; set; }
        public int EntrySize { get; set; }
        public bool PathIdIs64 { get; set; }
        public int Count { get; set; }
        public List<ObjectEntry> Entries { get; set; } = new List<ObjectEntry>();
    }
}

