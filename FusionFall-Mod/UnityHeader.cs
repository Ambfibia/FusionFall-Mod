namespace FusionFall_Mod.Models
{
    /// <summary>
    /// Класс, представляющий заголовок Unity-файла.
    /// </summary>
    public class UnityHeader
    {
        // Константы, описывающие размеры и флаги.
        public const int MainHeaderSize = 64;
        public const int DataStartOffset = 512;
        public const int InitialDataSize = 513;

        public const string DefaultFlag = "UnityWeb";
        public const string RetroFlag = "streamed";

        /// <summary>
        /// Флаг, определяющий тип файла. По умолчанию - DefaultFlag.
        /// </summary>
        public string FlagFile { get; }

        /// <summary>
        /// Основная (мажорная) версия.
        /// </summary>
        public byte MajorVersion { get; set; } = 2;

        /// <summary>
        /// Дополнительная информация о версии.
        /// </summary>
        public string VersionInfo { get; set; } = "fusion-2.x.x";

        /// <summary>
        /// Инфо о сборке.
        /// </summary>
        public string BuildInfo { get; set; } = "2.5.4b5";

        /// <summary>
        /// Полный размер файла.
        /// </summary>
        public int FileSize { get; set; } = 0;

        /// <summary>
        /// Смещение начала данных (по умолчанию указывает на MainHeaderSize).
        /// </summary>
        public int FirstOffset { get; set; } = MainHeaderSize;

        /// <summary>
        /// Размер сжатых данных.
        /// </summary>
        public int FileZipSize { get; set; } = 0;

        /// <summary>
        /// Размер распакованных данных (по умолчанию InitialDataSize).
        /// </summary>
        public int FileUnzipSize { get; set; } = InitialDataSize;

        /// <summary>
        /// Смещение конца данных (или конец файла).
        /// </summary>
        public int LastOffset { get; set; } = 0;

        /// <summary>
        /// Конструктор, принимающий флаг файла.
        /// </summary>
        /// <param name="flag">
        /// Допустимые значения: DefaultFlag ("UnityWeb") или RetroFlag ("streamed").
        /// Если строка не совпадает ни с одним из них, автоматически устанавливается DefaultFlag.
        /// </param>
        public UnityHeader(string flag)
        {
            if (string.Equals(flag, DefaultFlag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(flag, RetroFlag, StringComparison.OrdinalIgnoreCase))
            {
                FlagFile = flag;
            }
            else
            {
                FlagFile = DefaultFlag;
            }
        }
    }
}

