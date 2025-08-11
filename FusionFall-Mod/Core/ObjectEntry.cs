namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Запись объекта в таблице объектов.
    /// </summary>
    internal class ObjectEntry
    {
        public long PathId { get; set; }
        public uint OffsetRel { get; set; }
        public uint Size { get; set; }
        public int TypeIndex { get; set; }
        public int? ClassId { get; set; }
        public int? Flags { get; set; }
    }
}

