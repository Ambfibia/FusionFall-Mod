namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Запись имени шрифта.
    /// </summary>
    public record NameRecord(string Kind, ushort NameID, string Value)
    {
        public NameRecord(string kind, int nameId, string value) : this(kind, (ushort)nameId, value)
        {
        }
    }
}
