namespace FusionFall_Mod.Utilities
{
    public static class LzmaHelper
    {
        public static byte[] CompressData(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var output = new MemoryStream())
            {
                SevenZip.Compression.LZMA.Encoder encoder = new SevenZip.Compression.LZMA.Encoder();
                SevenZip.CoderPropID[] propIDs =
                {
                    SevenZip.CoderPropID.LitContextBits,
                    SevenZip.CoderPropID.LitPosBits,
                    SevenZip.CoderPropID.PosStateBits,
                    SevenZip.CoderPropID.DictionarySize
                };
                object[] properties =
                {
                    3,
                    0,
                    2,
                    128 * 1024 * 4
                };
                encoder.SetCoderProperties(propIDs, properties);
                encoder.WriteCoderProperties(output);
                output.Write(BitConverter.GetBytes((long)data.Length), 0, 8);
                encoder.Code(input, output, data.Length, -1, null);
                return output.ToArray();
            }
        }

        public static byte[] DecompressData(byte[] data)
        {
            using (MemoryStream input = new MemoryStream(data))
            {
                SevenZip.Compression.LZMA.Decoder decoder = new SevenZip.Compression.LZMA.Decoder();
                byte[] properties = new byte[5];
                if (input.Read(properties, 0, 5) != 5)
                    throw new Exception("Input .lzma file is too short");
                decoder.SetDecoderProperties(properties);
                byte[] fileLengthBytes = new byte[8];
                if (input.Read(fileLengthBytes, 0, 8) != 8)
                    throw new Exception("Input .lzma file is too short");
                long fileLength = BitConverter.ToInt64(fileLengthBytes, 0);
                using (MemoryStream output = new MemoryStream())
                {
                    decoder.Code(input, output, input.Length - input.Position, fileLength, null);
                    return output.ToArray();
                }
            }
        }
    }
}

