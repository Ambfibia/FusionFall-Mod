using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Набор функций для работы с BIN и TTF.
    /// </summary>
    public static class UnityFontBinFunctions
    {
        public const int GlyphRecSize = 40;

        public static BinFont ParseBin(byte[] data)
        {
            if (data.Length < 0x38)
            {
                throw new ArgumentException("BIN слишком мал.");
            }
            uint nameLen = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0));
            string name = System.Text.Encoding.ASCII.GetString(data, 4, (int)nameLen);
            int glyphCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x30));
            int tableOff = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0x34));
            if (glyphCount <= 0 || tableOff <= 0 || tableOff + glyphCount * GlyphRecSize > data.Length)
            {
                throw new InvalidDataException("Некорректная таблица глифов.");
            }
            List<GlyphRecord> records = new List<GlyphRecord>(glyphCount);
            for (int i = 0; i < glyphCount; i++)
            {
                int baseOff = tableOff + i * GlyphRecSize;
                float u0 = BitConverter.ToSingle(data, baseOff + 0);
                float v0 = BitConverter.ToSingle(data, baseOff + 4);
                float du = BitConverter.ToSingle(data, baseOff + 8);
                float dv = BitConverter.ToSingle(data, baseOff + 12);
                float xoff = BitConverter.ToSingle(data, baseOff + 16);
                float yoff = BitConverter.ToSingle(data, baseOff + 20);
                float adv = BitConverter.ToSingle(data, baseOff + 24);
                float ymin = BitConverter.ToSingle(data, baseOff + 28);
                float px = BitConverter.ToSingle(data, baseOff + 32);
                int code = BitConverter.ToInt32(data, baseOff + 36);
                records.Add(new GlyphRecord(u0, v0, du, dv, xoff, yoff, adv, ymin, px, code));
            }
            int after = tableOff + glyphCount * GlyphRecSize;
            return new BinFont(name, glyphCount, tableOff, after, records);
        }

        public static (int W, int H, int Lead, byte[] Payload) GuessDxt5Payload(ReadOnlySpan<byte> tail, int preferW = 1024)
        {
            int bestScore = int.MaxValue;
            int bestW = 0;
            int bestH = 0;
            int bestLead = 0;
            byte[]? best = null;
            int[] widths = new[] { preferW, 2048, 512, 1024, 768, 640, 4096, 256 };
            for (int wi = 0; wi < widths.Length; wi++)
            {
                int w = widths[wi];
                for (int h = 256; h <= 1024; h += 16)
                {
                    int blocks = ((w + 3) / 4) * ((h + 3) / 4);
                    int size = blocks * 16;
                    if (size > tail.Length)
                    {
                        continue;
                    }
                    int lead = tail.Length - size;
                    int score = Math.Abs(lead) + (w == preferW ? 0 : 50) + (lead < 0 ? 1000 : 0);
                    if (lead >= 0 && score < bestScore)
                    {
                        bestScore = score;
                        bestW = w;
                        bestH = h;
                        bestLead = lead;
                        best = tail.Slice(lead, size).ToArray();
                    }
                }
            }
            if (best == null)
            {
                throw new InvalidDataException("Не удалось определить DXT5.");
            }
            return (bestW, bestH, bestLead, best);
        }

        public static Bitmap DecodeDxt5(byte[] payload, int width, int height)
        {
            int wb = (width + 3) / 4;
            int hb = (height + 3) / 4;
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                unsafe
                {
                    byte* dst0 = (byte*)data.Scan0;
                    int stride = data.Stride;
                    int pos = 0;
                    fixed (byte* src = payload)
                    {
                        for (int by = 0; by < hb; by++)
                        {
                            for (int bx = 0; bx < wb; bx++)
                            {
                                byte a0 = src[pos + 0];
                                byte a1 = src[pos + 1];
                                ulong aCode = BitConverter.ToUInt64(payload, pos) >> 16;
                                pos += 8;
                                byte[] alphas = new byte[8];
                                alphas[0] = a0;
                                alphas[1] = a1;
                                if (a0 > a1)
                                {
                                    for (int i = 1; i <= 6; i++)
                                    {
                                        alphas[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                                    }
                                }
                                else
                                {
                                    for (int i = 1; i <= 4; i++)
                                    {
                                        alphas[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
                                    }
                                    alphas[6] = 0;
                                    alphas[7] = 255;
                                }
                                ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pos + 0));
                                ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(pos + 2));
                                uint code = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(pos + 4));
                                pos += 8;
                                (byte r0, byte g0, byte b0) = Unpack565(c0);
                                (byte r1, byte g1, byte b1) = Unpack565(c1);
                                (byte r, byte g, byte b)[] cols = new (byte r, byte g, byte b)[4];
                                cols[0] = (r0, g0, b0);
                                cols[1] = (r1, g1, b1);
                                if (c0 > c1)
                                {
                                    cols[2] = ((byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3));
                                    cols[3] = ((byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3));
                                }
                                else
                                {
                                    cols[2] = ((byte)((r0 + r1) / 2), (byte)((g0 + g1) / 2), (byte)((b0 + b1) / 2));
                                    cols[3] = (0, 0, 0);
                                }
                                for (int py = 0; py < 4; py++)
                                {
                                    for (int px = 0; px < 4; px++)
                                    {
                                        int i = py * 4 + px;
                                        int cx = (int)((code >> (2 * i)) & 0x3);
                                        int ax = (int)((aCode >> (3 * i)) & 0x7);
                                        int y = by * 4 + py;
                                        int x = bx * 4 + px;
                                        if (x < width && y < height)
                                        {
                                            (byte rr, byte gg, byte bb) = cols[cx];
                                            byte a = alphas[ax];
                                            byte* d = dst0 + y * stride + x * 4;
                                            d[0] = bb;
                                            d[1] = gg;
                                            d[2] = rr;
                                            d[3] = a;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        private static (byte r, byte g, byte b) Unpack565(ushort c)
        {
            int r = (c >> 11) & 0x1F;
            int g = (c >> 5) & 0x3F;
            int b = c & 0x1F;
            r = (r << 3) | (r >> 2);
            g = (g << 2) | (g >> 4);
            b = (b << 3) | (b >> 2);
            return ((byte)r, (byte)g, (byte)b);
        }

        public static (Dictionary<int, byte[]> PngByCodepoint, Dictionary<int, int> AdvanceByCodepoint) ExtractGlyphPNGs(BinFont font, Bitmap atlas)
        {
            Dictionary<int, byte[]> pngs = new Dictionary<int, byte[]>();
            Dictionary<int, int> adv = new Dictionary<int, int>();
            foreach (GlyphRecord gr in font.Records)
            {
                if (gr.Codepoint == 0)
                {
                    continue;
                }
                Rectangle r = gr.RectPixels(atlas.Width, atlas.Height);
                if (r.Width <= 0 || r.Height <= 0)
                {
                    continue;
                }
                using Bitmap crop = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(crop))
                {
                    g.DrawImage(atlas, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
                }
                pngs[gr.Codepoint] = BitmapToPngBytes(crop);
                adv[gr.Codepoint] = (int)Math.Round(gr.Advance);
            }
            return (pngs, adv);
        }

        public static (byte[] TtfBytes, Bitmap Atlas) BinToTtf(byte[] bin, int? ppem = null, string? family = null)
        {
            BinFont bf = ParseBin(bin);
            ReadOnlySpan<byte> tail = bin.AsSpan(bf.AfterTable);
            (int W, int H, int Lead, byte[] Payload) guess = GuessDxt5Payload(tail);
            Bitmap atlas = DecodeDxt5(guess.Payload, guess.W, guess.H);
            (Dictionary<int, byte[]> pngs, Dictionary<int, int> adv) = ExtractGlyphPNGs(bf, atlas);
            int strikePpem = ppem ?? (int)Math.Round(bf.Records.FirstOrDefault()?.PixelSize ?? 12);
            byte[] ttf = BuildSbixTtf(family ?? bf.Name ?? "UnityBitmap", strikePpem, pngs, adv);
            return (ttf, atlas);
        }

        public static (Bitmap Atlas, List<GlyphRecord> Updated) BuildAtlasFromTtf(byte[] ttfBytes, int ppem, int atlasW, int atlasH, List<GlyphRecord> templateRecords)
        {
            System.Drawing.Text.PrivateFontCollection pfc = new System.Drawing.Text.PrivateFontCollection();
            GCHandle handle = GCHandle.Alloc(ttfBytes, GCHandleType.Pinned);
            try
            {
                pfc.AddMemoryFont(handle.AddrOfPinnedObject(), ttfBytes.Length);
            }
            finally
            {
                handle.Free();
            }
            using FontFamily family = pfc.Families[0];
            Bitmap atlas = new Bitmap(atlasW, atlasH, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(atlas);
            g.Clear(Color.Transparent);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            int x = 0;
            int y = 0;
            int rowH = 0;
            List<GlyphRecord> updated = new List<GlyphRecord>(templateRecords.Count);
            foreach (GlyphRecord rec in templateRecords)
            {
                if (rec.Codepoint == 0)
                {
                    updated.Add(rec);
                    continue;
                }
                string s = char.ConvertFromUtf32(rec.Codepoint);
                SizeF size;
                using (Bitmap tmp = new Bitmap(8, 8))
                using (Graphics gg = Graphics.FromImage(tmp))
                using (Font f = new Font(family, ppem, GraphicsUnit.Pixel))
                {
                    size = gg.MeasureString(s, f);
                }
                int gw = Math.Max(1, (int)Math.Ceiling(size.Width));
                int gh = Math.Max(1, (int)Math.Ceiling(size.Height));
                if (x + gw > atlasW)
                {
                    x = 0;
                    y += rowH + 1;
                    rowH = 0;
                }
                if (y + gh > atlasH)
                {
                    throw new Exception("Atlas overflow");
                }
                using (Font fnt = new Font(family, ppem, GraphicsUnit.Pixel))
                using (SolidBrush brush = new SolidBrush(Color.White))
                {
                    g.DrawString(s, fnt, brush, new PointF(x, y));
                }
                float u0 = (float)x / atlasW;
                float v0 = (float)y / atlasH;
                float du = (float)gw / atlasW;
                float dv = (float)gh / atlasH;
                updated.Add(rec with { U0 = u0, V0 = v0, DU = du, DV = dv, PixelSize = ppem });
                x += gw + 1;
                if (gh > rowH)
                {
                    rowH = gh;
                }
            }
            return (atlas, updated);
        }

        public static byte[] EncodeDxt5WithNvcompress(Bitmap atlas, string nvcompressPath = "nvcompress")
        {
            string tmpPng = Path.GetTempFileName() + ".png";
            string tmpDds = Path.GetTempFileName() + ".dds";
            try
            {
                atlas.Save(tmpPng, ImageFormat.Png);
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = nvcompressPath,
                    Arguments = $"-bc3 \"{tmpPng}\" \"{tmpDds}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using Process p = Process.Start(psi)!;
                p.WaitForExit();
                if (p.ExitCode != 0)
                {
                    throw new Exception("nvcompress завершился с ошибкой");
                }
                byte[] dds = File.ReadAllBytes(tmpDds);
                if (dds.Length < 128 || dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
                {
                    throw new Exception("Неверный формат DDS");
                }
                byte[] payload = new byte[dds.Length - 128];
                Buffer.BlockCopy(dds, 128, payload, 0, payload.Length);
                return payload;
            }
            finally
            {
                TryDelete(tmpPng);
                TryDelete(tmpDds);
            }
        }

        public static byte[] RebuildBin(byte[] template, List<GlyphRecord> records, byte[] dxt5Payload, int tableOffset)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter bw = new BinaryWriter(ms);
            bw.Write(template, 0, tableOffset);
            foreach (GlyphRecord r in records)
            {
                bw.Write(BitConverter.GetBytes(r.U0));
                bw.Write(BitConverter.GetBytes(r.V0));
                bw.Write(BitConverter.GetBytes(r.DU));
                bw.Write(BitConverter.GetBytes(r.DV));
                bw.Write(BitConverter.GetBytes(r.XOffset));
                bw.Write(BitConverter.GetBytes(r.YOffset));
                bw.Write(BitConverter.GetBytes(r.Advance));
                bw.Write(BitConverter.GetBytes(r.YMin));
                bw.Write(BitConverter.GetBytes(r.PixelSize));
                bw.Write(BitConverter.GetBytes(r.Codepoint));
            }
            bw.Write(dxt5Payload);
            return ms.ToArray();
        }

        public static byte[] PackBinFromTtf(byte[] templateBin, byte[] ttf, int ppem, int atlasW, int atlasH, Func<Bitmap, byte[]> encodeDxt5)
        {
            BinFont tpl = ParseBin(templateBin);
            (Bitmap atlas, List<GlyphRecord> updated) = BuildAtlasFromTtf(ttf, ppem, atlasW, atlasH, tpl.Records);
            using (atlas)
            {
                byte[] payload = encodeDxt5(atlas);
                return RebuildBin(templateBin, updated, payload, tpl.TableOffset);
            }
        }

        public static byte[] BuildSbixTtf(string family, int ppem, Dictionary<int, byte[]> glyphPngByCodepoint, Dictionary<int, int> advances)
        {
            List<int> codepoints = glyphPngByCodepoint.Keys.OrderBy(c => c).ToList();
            List<string> glyphOrder = new List<string> { ".notdef" };
            foreach (int cp in codepoints)
            {
                glyphOrder.Add($"uni{cp:X4}");
            }
            Dictionary<string, byte[]> tables = new Dictionary<string, byte[]>();
            tables["head"] = Build_head();
            tables["hhea"] = Build_hhea(ppem, glyphOrder.Count);
            tables["maxp"] = Build_maxp(glyphOrder.Count);
            tables["OS/2"] = Build_OS2(ppem, advances);
            tables["hmtx"] = Build_hmtx(ppem, codepoints, advances);
            tables["cmap"] = Build_cmap12(codepoints);
            tables["post"] = Build_post();
            tables["glyf"] = Array.Empty<byte>();
            tables["loca"] = Build_loca_zero(glyphOrder.Count);
            tables["name"] = Build_name(family);
            tables["sbix"] = Build_sbix_png(ppem, codepoints, glyphPngByCodepoint);
            return SfntWriter.BuildSfnt(tables, glyphOrder.Count);
        }

        private static byte[] Build_head()
        {
            BEWriter w = new BEWriter();
            w.U32(0x00010000);
            w.U32(0x00010000);
            w.U32(0);
            w.U32(0x5F0F3CF5);
            w.U16(0);
            w.U16(2048);
            long ts = DateTimeToLongDate();
            w.S64(ts);
            w.S64(ts);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.U16(0);
            w.U16(0);
            w.S16(2);
            w.S16(0);
            w.S16(0);
            return w.BytesOut();
        }

        private static byte[] Build_hhea(int ppem, int numGlyphs)
        {
            BEWriter w = new BEWriter();
            w.U32(0x00010000);
            short asc = (short)(ppem * 0.8 * 64);
            short desc = (short)-(ppem * 0.2 * 64);
            w.S16(asc);
            w.S16(desc);
            w.S16(0);
            w.U16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.U16((ushort)numGlyphs);
            return w.BytesOut();
        }

        private static byte[] Build_maxp(int numGlyphs)
        {
            BEWriter w = new BEWriter();
            w.U32(0x00010000);
            w.U16((ushort)numGlyphs);
            w.Reserve(28);
            return w.BytesOut();
        }

        private static byte[] Build_OS2(int ppem, Dictionary<int, int> adv)
        {
            BEWriter w = new BEWriter();
            w.U16(4);
            short avg = (short)adv.Values.DefaultIfEmpty(500).Average();
            w.S16(avg);
            w.U16(400);
            w.U16(5);
            w.U16(0);
            short asc = (short)(ppem * 0.8 * 64);
            short desc = (short)-(ppem * 0.2 * 64);
            w.S16(asc);
            w.S16(asc);
            w.S16(0);
            w.S16(0);
            w.S16(asc);
            w.S16(asc);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.Reserve(10);
            w.U32(0);
            w.U32(0);
            w.U32(0);
            w.U32(0);
            w.Tag("UNIF");
            w.U16(0);
            w.U16(0);
            w.U16(0xFFFF);
            w.S16(asc);
            w.S16(desc);
            w.S16(0);
            w.U16((ushort)asc);
            w.U16((ushort)(-desc));
            w.U32(0);
            w.U32(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.S16(0);
            w.U16(2);
            w.U16(1);
            w.U16(0);
            return w.BytesOut();
        }

        private static byte[] Build_hmtx(int ppem, List<int> cps, Dictionary<int, int> adv)
        {
            BEWriter w = new BEWriter();
            w.U16((ushort)ppem);
            w.S16(0);
            foreach (int cp in cps)
            {
                int a = adv.TryGetValue(cp, out int v) ? v : ppem;
                w.U16((ushort)a);
                w.S16(0);
            }
            return w.BytesOut();
        }

        private static byte[] Build_loca_zero(int numGlyphs)
        {
            BEWriter w = new BEWriter();
            for (int i = 0; i < numGlyphs + 1; i++)
            {
                w.U16(0);
            }
            return w.BytesOut();
        }

        private static byte[] Build_post()
        {
            BEWriter w = new BEWriter();
            w.U32(0x00030000);
            w.U32(0);
            w.U32(0);
            w.U32(0);
            w.U32(0);
            w.U32(0);
            w.U32(0);
            return w.BytesOut();
        }

        private static byte[] Build_name(string family)
        {
            List<NameRecord> recs = new List<NameRecord>
            {
                new NameRecord("Family", 1, family),
                new NameRecord("Subfamily", 2, "Regular"),
                new NameRecord("Full", 4, family + " Bitmap")
            };
            return NameTableBuilder.Build(recs);
        }

        private static byte[] Build_cmap12(List<int> cps)
        {
            List<(uint, uint, uint)> groups = cps.Select(cp => ((uint)cp, (uint)cp, (uint)(cps.IndexOf(cp) + 1))).OrderBy(t => t.Item1).ToList();
            BEWriter sub = new BEWriter();
            sub.U16(12);
            sub.U16(0);
            sub.U32(16 + 12u * (uint)groups.Count);
            sub.U32(0);
            sub.U32((uint)groups.Count);
            foreach ((uint startChar, uint endChar, uint startGlyphId) g in groups)
            {
                sub.U32(g.Item1);
                sub.U32(g.Item2);
                sub.U32(g.Item3);
            }
            byte[] subBytes = sub.BytesOut();
            BEWriter w = new BEWriter();
            w.U16(0);
            w.U16(1);
            w.U16(3);
            w.U16(10);
            w.U32(4 + 8);
            w.Bytes(subBytes);
            return w.BytesOut();
        }

        private static byte[] Build_sbix_png(int ppem, List<int> cps, Dictionary<int, byte[]> pngByCp)
        {
            int numGlyphs = cps.Count + 1;
            List<byte[]> glyphBlocks = new List<byte[]>(numGlyphs);
            glyphBlocks.Add(Array.Empty<byte>());
            foreach (int cp in cps)
            {
                BEWriter w = new BEWriter();
                w.S16(0);
                w.S16(0);
                w.Tag("png ");
                w.Bytes(pngByCp[cp]);
                glyphBlocks.Add(w.BytesOut());
            }
            BEWriter strike = new BEWriter();
            strike.U16((ushort)ppem);
            strike.U16(72);
            int cur = 4 + numGlyphs * 4;
            for (int i = 0; i < numGlyphs; i++)
            {
                strike.U32((uint)cur);
                cur += Align4(glyphBlocks[i]).Length;
            }
            foreach (byte[] gb in glyphBlocks)
            {
                strike.Bytes(Align4(gb));
            }
            byte[] strikeBytes = strike.BytesOut();
            BEWriter w2 = new BEWriter();
            w2.U16(1);
            w2.U16(0);
            w2.U32(1);
            w2.U32(12);
            w2.Bytes(strikeBytes);
            return w2.BytesOut();
        }

        private static byte[] Align4(byte[] b)
        {
            int pad = (4 - (b.Length % 4)) % 4;
            if (pad == 0)
            {
                return b;
            }
            byte[] z = new byte[b.Length + pad];
            Buffer.BlockCopy(b, 0, z, 0, b.Length);
            return z;
        }

        private static long DateTimeToLongDate()
        {
            DateTime epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(DateTime.UtcNow - epoch).TotalSeconds;
        }

        public static byte[] BitmapToPngBytes(Bitmap bmp)
        {
            using MemoryStream ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
