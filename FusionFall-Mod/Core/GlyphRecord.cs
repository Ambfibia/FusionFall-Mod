using System;
using System.Drawing;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Запись глифа в BIN-файле.
    /// </summary>
    public record GlyphRecord(float U0, float V0, float DU, float DV, float XOffset, float YOffset, float Advance, float YMin, float PixelSize, int Codepoint)
    {
        public Rectangle RectPixels(int atlasWidth, int atlasHeight)
        {
            int left = Math.Max(0, (int)Math.Round(U0 * atlasWidth));
            int top = Math.Max(0, (int)Math.Round(V0 * atlasHeight));
            int right = Math.Min(atlasWidth, (int)Math.Round((U0 + DU) * atlasWidth));
            int bottom = Math.Min(atlasHeight, (int)Math.Round((V0 + DV) * atlasHeight));
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }
}
