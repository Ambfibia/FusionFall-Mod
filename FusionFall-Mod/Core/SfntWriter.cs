using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Построение шрифта TrueType.
    /// </summary>
    public static class SfntWriter
    {
        private record TableEntry(string Tag, byte[] Data, uint Checksum);

        public static byte[] BuildSfnt(Dictionary<string, byte[]> tables, int glyphCount)
        {
            List<TableEntry> entries = new List<TableEntry>();
            foreach (KeyValuePair<string, byte[]> kv in tables)
            {
                byte[] data = Align4(kv.Value);
                uint checksum = CheckSum(data);
                entries.Add(new TableEntry(kv.Key, data, checksum));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

            int numTables = entries.Count;
            int searchRange = HighestPowerOf2(numTables) * 16;
            int entrySelector = Log2(HighestPowerOf2(numTables));
            int rangeShift = numTables * 16 - searchRange;

            MemoryStream memoryStream = new MemoryStream();
            BEWriter writer = new BEWriter(memoryStream);
            writer.U32(0x00010000);
            writer.U16((ushort)numTables);
            writer.U16((ushort)searchRange);
            writer.U16((ushort)entrySelector);
            writer.U16((ushort)rangeShift);

            int offset = 12 + numTables * 16;
            foreach (TableEntry entry in entries)
            {
                writer.Tag(entry.Tag);
                writer.U32(entry.Checksum);
                writer.U32((uint)offset);
                writer.U32((uint)entry.Data.Length);
                offset += entry.Data.Length;
            }
            foreach (TableEntry entry in entries)
            {
                writer.Bytes(entry.Data);
            }

            byte[] fontBytes = memoryStream.ToArray();
            FixCheckSumAdjustment(fontBytes);
            return fontBytes;
        }

        private static void FixCheckSumAdjustment(byte[] fontBytes)
        {
            int numTables = BinaryPrimitives.ReadUInt16BigEndian(fontBytes.AsSpan(4));
            int dirOffset = 12;
            int headOffset = -1;
            for (int i = 0; i < numTables; i++)
            {
                int off = dirOffset + i * 16;
                string tag = Encoding.ASCII.GetString(fontBytes, off, 4);
                uint tableOffset = BinaryPrimitives.ReadUInt32BigEndian(fontBytes.AsSpan(off + 8));
                if (tag == "head")
                {
                    headOffset = (int)tableOffset;
                    break;
                }
            }
            if (headOffset < 0)
            {
                throw new Exception("Таблица head не найдена.");
            }
            fontBytes[headOffset + 8] = 0;
            fontBytes[headOffset + 9] = 0;
            fontBytes[headOffset + 10] = 0;
            fontBytes[headOffset + 11] = 0;
            uint sum = CheckSum(fontBytes);
            uint adjust = 0xB1B0AFBA - sum;
            BinaryPrimitives.WriteUInt32BigEndian(fontBytes.AsSpan(headOffset + 8), adjust);
        }

        private static int HighestPowerOf2(int value)
        {
            int power = 1;
            while (power * 2 <= value)
            {
                power *= 2;
            }
            return power;
        }

        private static int Log2(int value)
        {
            int n = 0;
            while ((1 << (n + 1)) <= value)
            {
                n++;
            }
            return n;
        }

        private static byte[] Align4(byte[] data)
        {
            int pad = (4 - (data.Length % 4)) % 4;
            if (pad == 0)
            {
                return data;
            }
            byte[] result = new byte[data.Length + pad];
            Buffer.BlockCopy(data, 0, result, 0, data.Length);
            return result;
        }

        public static uint CheckSum(byte[] data)
        {
            uint sum = 0;
            int i = 0;
            int len = data.Length;
            while (len > 3)
            {
                sum += BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
                i += 4;
                len -= 4;
            }
            if (len > 0)
            {
                Span<byte> last = stackalloc byte[4];
                for (int j = 0; j < len; j++)
                {
                    last[j] = data[i + j];
                }
                sum += BinaryPrimitives.ReadUInt32BigEndian(last);
            }
            return sum;
        }
    }
}
