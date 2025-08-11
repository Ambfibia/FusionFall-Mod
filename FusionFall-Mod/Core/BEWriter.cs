using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Запись big-endian данных.
    /// </summary>
    public class BEWriter
    {
        private readonly MemoryStream stream;

        public BEWriter()
        {
            stream = new MemoryStream();
        }

        public BEWriter(Stream externalStream)
        {
            stream = (MemoryStream)externalStream;
        }

        public void U16(ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
            stream.Write(buffer);
        }

        public void S16(short value)
        {
            U16((ushort)value);
        }

        public void U32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        public void S32(int value)
        {
            U32((uint)value);
        }

        public void U64(ulong value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
            stream.Write(buffer);
        }

        public void S64(long value)
        {
            U64((ulong)value);
        }

        public void Tag(string tag)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(tag);
            if (bytes.Length != 4)
            {
                throw new ArgumentException("Длина тега должна быть 4.");
            }
            stream.Write(bytes, 0, bytes.Length);
        }

        public void Bytes(byte[] bytes)
        {
            stream.Write(bytes, 0, bytes.Length);
            int pad = (4 - (bytes.Length % 4)) % 4;
            for (int i = 0; i < pad; i++)
            {
                stream.WriteByte(0);
            }
        }

        public void Reserve(int count)
        {
            for (int i = 0; i < count; i++)
            {
                stream.WriteByte(0);
            }
        }

        public byte[] BytesOut()
        {
            return stream.ToArray();
        }
    }
}
