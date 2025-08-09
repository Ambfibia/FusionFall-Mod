namespace FusionFall_Mod.Models
{
    /// <summary>
    /// Сведения о заголовке Unity-файла.
    /// </summary>
    public class HeaderInfo
    {
        /// <summary>
        /// Основная (мажорная) версия.
        /// </summary>
        public byte MajorVersion { get; set; }

        /// <summary>
        /// Дополнительная информация о версии.
        /// </summary>
        public string VersionInfo { get; set; } = string.Empty;

        /// <summary>
        /// Информация о сборке.
        /// </summary>
        public string BuildInfo { get; set; } = string.Empty;
    }
}
