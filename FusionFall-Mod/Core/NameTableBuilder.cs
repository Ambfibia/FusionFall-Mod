using System.Collections.Generic;
using System.Text;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Построение таблицы имен.
    /// </summary>
    public static class NameTableBuilder
    {
        public static byte[] Build(List<NameRecord> records)
        {
            ushort platformId = 3;
            ushort encodingId = 1;
            ushort languageId = 1033;
            List<(ushort, ushort, ushort, ushort, ushort, ushort, byte[])> nameRecords = new List<(ushort, ushort, ushort, ushort, ushort, ushort, byte[])>();
            foreach (NameRecord record in records)
            {
                byte[] bytes = Encoding.BigEndianUnicode.GetBytes(record.Value);
                nameRecords.Add((platformId, encodingId, languageId, record.NameID, (ushort)bytes.Length, 0, bytes));
            }
            int count = nameRecords.Count;
            BEWriter writer = new BEWriter();
            writer.U16(0);
            writer.U16((ushort)count);
            writer.U16((ushort)(6 + count * 12));
            int offset = 0;
            foreach ((ushort, ushort, ushort, ushort, ushort, ushort, byte[]) nr in nameRecords)
            {
                writer.U16(nr.Item1);
                writer.U16(nr.Item2);
                writer.U16(nr.Item3);
                writer.U16(nr.Item4);
                writer.U16(nr.Item5);
                writer.U16((ushort)offset);
                offset += nr.Item5;
            }
            foreach ((ushort, ushort, ushort, ushort, ushort, ushort, byte[]) nr in nameRecords)
            {
                writer.Bytes(nr.Item7);
            }
            return writer.BytesOut();
        }
    }
}
