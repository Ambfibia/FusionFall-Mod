using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using SevenZip;
using MessageBox = System.Windows.MessageBox;

namespace FusionFall_Mod
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
        /// Инфо о сборке .
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

    public static class LzmaHelper
    {
        public static byte[] CompressData(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var output = new MemoryStream())
            {
                SevenZip.Compression.LZMA.Encoder encoder = new SevenZip.Compression.LZMA.Encoder();
                CoderPropID[] propIDs =
                {
                    CoderPropID.LitContextBits,
                    CoderPropID.LitPosBits,
                    CoderPropID.PosStateBits,
                    CoderPropID.DictionarySize
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

    public class FileEntry
    {
        public string FileName { get; }
        public string FullPath { get; }
        public long Size { get; }

        public FileEntry(string fileName, string fullPath, long size)
        {
            FileName = fileName;
            FullPath = fullPath;
            Size = size;
        }
    }

    public partial class MainWindow : Window
    {
        private readonly string[] expectedFiles = new string[]
        {
            "Assembly - CSharp - first pass.dll",
            "Assembly - CSharp.dll",
            "Assembly - UnityScript - first pass.dll",
            "Mono.Data.Tds.dll",
            "Mono.Security.dll",
            "System.Configuration.Install.dll",
            "System.Configuration.dll",
            "System.Data.dll",
            "System.Drawing.dll",
            "System.EnterpriseServices.dll",
            "System.Security.dll",
            "System.Transactions.dll",
            "System.Xml.dll",
            "System.dll",
            "mysql.data.dll",
            "mainData",
            "sharedassets0.assets"
        };

        public MainWindow()
        {
            InitializeComponent();
            FlagComboBox.SelectedIndex = 0;
        }


        private void PackButton_Click(object sender, RoutedEventArgs e)
        {
            PackUnity3D(compress: true);
        }

        private void PackUncompressedButton_Click(object sender, RoutedEventArgs e)
        {
            PackUnity3D(compress: false);
        }

        private void ExtractButton_Click(object sender, RoutedEventArgs e)
        {
            ExtractFiles();
        }

        private void ExtractRawButton_Click(object sender, RoutedEventArgs e)
        {
            ExtractRawHeader();
        }

        private void PackUnity3D(bool compress)
        {
            Microsoft.Win32.SaveFileDialog sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Unity web player (*.unity3d)|*.unity3d|All Files (*.*)|*.*"
            };

            if (sfd.ShowDialog() != true)
            {
                return;
            }
                
            string outputFilename = sfd.FileName;

            string folderPath = "";

            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK ||
                    string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    return;
                }
                    
                folderPath = fbd.SelectedPath;
            }

            List<FileEntry> fileEntries = ValidateExpectedFiles(folderPath, expectedFiles);

            if (fileEntries == null)
            {
                return;
            }

            FilesListBox.Items.Clear();

            foreach (FileEntry entry in fileEntries)
            {
                FilesListBox.Items.Add(entry.FileName);
            }

            string selectedFlag = ((ComboBoxItem)FlagComboBox.SelectedItem).Content.ToString();
            UnityHeader header = new UnityHeader(selectedFlag);

            byte[] headerData = BuildHeaderData(fileEntries, header);
            header.FileUnzipSize = headerData.Length;

            byte[] compData = compress ? LzmaHelper.CompressData(headerData) : headerData;

            byte[] mainHeader = BuildMainHeader(header, compData.Length);

            byte[] outputData = new byte[mainHeader.Length + compData.Length];
            Buffer.BlockCopy(mainHeader, 0, outputData, 0, mainHeader.Length);
            Buffer.BlockCopy(compData, 0, outputData, mainHeader.Length, compData.Length);

            try
            {
                File.WriteAllBytes(outputFilename, outputData);
                MessageBox.Show("Packing completed successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to write output file:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private List<FileEntry> ValidateExpectedFiles(string folderPath, string[] expectedFiles)
        {
            List<FileEntry> entries = new List<FileEntry>();
            foreach (string fileName in expectedFiles)
            {
                string fullPath = Path.Combine(folderPath, fileName);
                if (!File.Exists(fullPath))
                {
                    MessageBox.Show($"Expected file '{fileName}' not found in folder.",
                        "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                long size = new FileInfo(fullPath).Length;
                entries.Add(new FileEntry(fileName, fullPath, size));
            }
            return entries;
        }
        private byte[] BuildHeaderData(List<FileEntry> fileEntries, UnityHeader header)
        {
            long totalFileDataSize = fileEntries.Sum(entry => entry.Size);
            
            int finalBytes = 2;

            int headerDataSize = UnityHeader.DataStartOffset + (int)totalFileDataSize + finalBytes;

            byte[] headerData = new byte[headerDataSize];

            int numFiles = fileEntries.Count;
            Buffer.BlockCopy(EndianConverter.ToBigEndian(numFiles), 0, headerData, 0, 4);

            int metadataPos = 4;
            int fileDataOffset = UnityHeader.DataStartOffset;

            foreach (FileEntry fileEntry in fileEntries)
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(fileEntry.FileName);
                Buffer.BlockCopy(nameBytes, 0, headerData, metadataPos, nameBytes.Length);
                metadataPos += nameBytes.Length;
                headerData[metadataPos++] = 0;

                Buffer.BlockCopy(EndianConverter.ToBigEndian(fileDataOffset), 0, headerData, metadataPos, 4);
                metadataPos += 4;
                Buffer.BlockCopy(EndianConverter.ToBigEndian((int)fileEntry.Size), 0, headerData, metadataPos, 4);
                metadataPos += 4;

                fileDataOffset += (int)fileEntry.Size;
            }

            int fileDataPos = UnityHeader.DataStartOffset;
            foreach (var fileEntry in fileEntries)
            {
                byte[] fileBytes = File.ReadAllBytes(fileEntry.FullPath);
                Buffer.BlockCopy(fileBytes, 0, headerData, fileDataPos, fileBytes.Length);
                fileDataPos += fileBytes.Length;
            }


            return headerData;
        }
        private byte[] BuildMainHeader(UnityHeader header, int compressedDataLength)
        {
            byte[] mainHeader = new byte[UnityHeader.MainHeaderSize];

            byte[] flagBytes = Encoding.ASCII.GetBytes(header.FlagFile);
            Buffer.BlockCopy(flagBytes, 0, mainHeader, 0, Math.Min(flagBytes.Length, 8));

            mainHeader[12] = header.MajorVersion;
            byte[] ver2Bytes = Encoding.ASCII.GetBytes(header.VersionInfo.PadRight(12, '\0'));
            Buffer.BlockCopy(ver2Bytes, 0, mainHeader, 13, 12);
            byte[] ver3Bytes = Encoding.ASCII.GetBytes(header.BuildInfo.PadRight(7, '\0'));
            Buffer.BlockCopy(ver3Bytes, 0, mainHeader, 26, 7);

            header.FileSize = UnityHeader.MainHeaderSize + compressedDataLength;
            Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FileSize), 0, mainHeader, 34, 4);

            Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FirstOffset), 0, mainHeader, 38, 4);

            mainHeader[45] = 1;
            mainHeader[49] = 1;

            header.FileZipSize = compressedDataLength;
            Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FileZipSize), 0, mainHeader, 50, 4);

            Buffer.BlockCopy(EndianConverter.ToBigEndian(header.FileUnzipSize), 0, mainHeader, 54, 4);

            header.LastOffset = UnityHeader.MainHeaderSize + compressedDataLength;
            Buffer.BlockCopy(EndianConverter.ToBigEndian(header.LastOffset), 0, mainHeader, 58, 4);

            return mainHeader;
        }
        private void ExtractFiles()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Unity web player (*.unity3d)|*.unity3d|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() != true)
                return;
            string inputFilename = ofd.FileName;
            string inputDir = Path.GetDirectoryName(inputFilename);
            string outputDir = Path.Combine(inputDir, "uncompressfiles");
            Directory.CreateDirectory(outputDir);

            try
            {
                byte[] fileContent = File.ReadAllBytes(inputFilename);
                if (fileContent.Length < UnityHeader.MainHeaderSize)
                    throw new Exception("File too short, invalid header.");

                byte[] mainHeader = new byte[UnityHeader.MainHeaderSize];
                Buffer.BlockCopy(fileContent, 0, mainHeader, 0, UnityHeader.MainHeaderSize);

                int fileSize = EndianConverter.FromBigEndian(mainHeader, 34);
                int firstOffset = EndianConverter.FromBigEndian(mainHeader, 38);
                int fileZipSize = EndianConverter.FromBigEndian(mainHeader, 50);
                int fileUnzipSize = EndianConverter.FromBigEndian(mainHeader, 54);
                int lastOffset = EndianConverter.FromBigEndian(mainHeader, 58);

                int compDataLength = fileContent.Length - UnityHeader.MainHeaderSize;
                byte[] compData = new byte[compDataLength];
                Buffer.BlockCopy(fileContent, UnityHeader.MainHeaderSize, compData, 0, compDataLength);

                byte[] decomData = LzmaHelper.DecompressData(compData);

                int numFiles = EndianConverter.FromBigEndian(decomData, 0);
                int pos = 4;
                for (int i = 0; i < numFiles; i++)
                {
                    int nameStart = pos;
                    while (pos < decomData.Length && decomData[pos] != 0)
                    {
                        pos++;
                    }
                    string filename = Encoding.ASCII.GetString(decomData, nameStart, pos - nameStart);
                    pos++;
                    int offset = EndianConverter.FromBigEndian(decomData, pos);
                    pos += 4;
                    int size = EndianConverter.FromBigEndian(decomData, pos);
                    pos += 4;

                    byte[] fileData = new byte[size];
                    Buffer.BlockCopy(decomData, offset, fileData, 0, size);
                    string outPath = Path.Combine(outputDir, filename);
                    File.WriteAllBytes(outPath, fileData);
                }
                MessageBox.Show("Files extracted successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Extraction failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExtractRawHeader()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Unity web player (*.unity3d)|*.unity3d|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() != true)
                return;
            string inputFilename = ofd.FileName;
            string inputDir = Path.GetDirectoryName(inputFilename);
            string outputFile = Path.Combine(inputDir, "uncompress_file");

            try
            {
                byte[] fileContent = File.ReadAllBytes(inputFilename);
                if (fileContent.Length < UnityHeader.MainHeaderSize)
                    throw new Exception("Invalid header.");
                int compDataLength = fileContent.Length - UnityHeader.MainHeaderSize;
                byte[] compData = new byte[compDataLength];
                Buffer.BlockCopy(fileContent, UnityHeader.MainHeaderSize, compData, 0, compDataLength);
                byte[] decomData = LzmaHelper.DecompressData(compData);
                File.WriteAllBytes(outputFile, decomData);
                MessageBox.Show("Raw header extracted successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Extraction failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
