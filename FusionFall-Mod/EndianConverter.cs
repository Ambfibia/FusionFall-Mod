using System;

namespace FusionFall_Mod.Utilities
{
    /// <summary>
    /// Утилитный класс для конвертации целых чисел в Big-Endian и обратно.
    /// </summary>
    public static class EndianConverter
    {
        /// <summary>
        /// Преобразует int в массив байт Big-Endian.
        /// </summary>
        public static byte[] ToBigEndian(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        /// <summary>
        /// Читает int из массива байт в Big-Endian формате, начиная с указанного индекса.
        /// </summary>
        public static int FromBigEndian(byte[] bytes, int startIndex = 0)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (startIndex < 0 || startIndex + 4 > bytes.Length)
                throw new ArgumentOutOfRangeException(nameof(startIndex), "Недопустимый индекс старта в массиве байт.");

            // Создаем временный массив, копируем нужные 4 байта
            byte[] temp = new byte[4];
            Array.Copy(bytes, startIndex, temp, 0, 4);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(temp);

            return BitConverter.ToInt32(temp, 0);
        }
    }
}

