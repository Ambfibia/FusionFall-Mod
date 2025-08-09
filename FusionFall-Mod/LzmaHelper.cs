using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Методы для LZMA-упаковки и распаковки данных.
    /// </summary>
    public static class LzmaHelper
    {
        /// <summary>
        /// Сжатие массива байт в формат LZMA-Alone.
        /// </summary>
        public static byte[] CompressData(byte[] data)
        {
            using var inputStream = new MemoryStream(data, writable: false);
            using var outputStream = new MemoryStream();

            var encoder = new SevenZip.Compression.LZMA.Encoder();
            var properties = new Dictionary<SevenZip.CoderPropID, object>
            {
                { SevenZip.CoderPropID.DictionarySize, 1 << 23 },
                { SevenZip.CoderPropID.LitContextBits, 3 },
                { SevenZip.CoderPropID.LitPosBits, 0 },
                { SevenZip.CoderPropID.PosStateBits, 2 },
                { SevenZip.CoderPropID.Algorithm, 2 },
                { SevenZip.CoderPropID.NumFastBytes, 128 },
                { SevenZip.CoderPropID.MatchFinder, "bt4" },
                { SevenZip.CoderPropID.EndMarker, false }
            };
            encoder.SetCoderProperties(properties.Keys.ToArray(), properties.Values.ToArray());
            encoder.WriteCoderProperties(outputStream);

            long uncompressedSize = data.LongLength;
            for (int i = 0; i < 8; i++)
            {
                outputStream.WriteByte((byte)(uncompressedSize >> (8 * i)));
            }

            encoder.Code(inputStream, outputStream, inputStream.Length, -1, null);
            return outputStream.ToArray();
        }

        /// <summary>
        /// Распаковка данных из формата LZMA-Alone.
        /// </summary>
        public static byte[] DecompressData(byte[] data)
        {
            using var inputStream = new MemoryStream(data, writable: false);
            var decoder = new SevenZip.Compression.LZMA.Decoder();

            var properties = new byte[5];
            if (inputStream.Read(properties, 0, 5) != 5)
            {
                throw new Exception("Слишком короткий поток .lzma");
            }
            decoder.SetDecoderProperties(properties);

            var sizeBytes = new byte[8];
            if (inputStream.Read(sizeBytes, 0, 8) != 8)
            {
                throw new Exception("Слишком короткий поток .lzma");
            }
            long uncompressedSize = BitConverter.ToInt64(sizeBytes, 0);

            using var outputStream = new MemoryStream();
            decoder.Code(inputStream, outputStream, inputStream.Length - inputStream.Position, uncompressedSize, null);
            return outputStream.ToArray();
        }
    }
}

