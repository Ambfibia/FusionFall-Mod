using System.Collections.Generic;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Представление BIN-шрифта.
    /// </summary>
    public record BinFont(string Name, int GlyphCount, int TableOffset, int AfterTable, List<GlyphRecord> Records);
}
