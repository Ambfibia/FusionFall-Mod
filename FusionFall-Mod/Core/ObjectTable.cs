using System.Collections.Generic;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Таблица объектов, полученная из метаданных.
    /// </summary>
    internal class ObjectTable
    {
        public int TableOffset { get; set; }
        public bool PathIdIs64 { get; set; }
        public int EntrySize { get; set; }
        public int Count { get; set; }
        public List<ObjectEntry> Entries { get; set; } = new List<ObjectEntry>();
        public ObjectEntry this[int index] => Entries[index];
    }
}

