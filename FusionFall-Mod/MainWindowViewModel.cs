using Avalonia.Controls;
using FusionFall_Mod.Models;
using FusionFall_Mod.Utilities;
using MsBox.Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;

namespace FusionFall_Mod
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly Window _window;
        private string _selectedFlag;

        public event PropertyChangedEventHandler? PropertyChanged;


        public MainWindowViewModel(Window window)
        {
            _window = window;
            HeaderFlags = new List<string> { "UnityWeb", "streamed" };
            _selectedFlag = HeaderFlags[0];

            Files = new ObservableCollection<string>();

            PackCommand = new AsyncCommand(() => PackUnity3D(true));
            PackUncompressedCommand = new AsyncCommand(() => PackUnity3D(false));
            ExtractCommand = new AsyncCommand(ExtractFiles);
            ExtractRawCommand = new AsyncCommand(ExtractRawHeader);
        }

        public List<string> HeaderFlags { get; }

        public string SelectedFlag
        {
            get => _selectedFlag;
            set
            {
                if (_selectedFlag != value)
                {
                    _selectedFlag = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedFlag)));
                }
            }
        }

        public ObservableCollection<string> Files { get; }

        public ICommand PackCommand { get; }
        public ICommand PackUncompressedCommand { get; }
        public ICommand ExtractCommand { get; }
        public ICommand ExtractRawCommand { get; }

        // Упаковка ресурсов в файл unity3d
        private async Task PackUnity3D(bool compress)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Unity web player", Extensions = { "unity3d" } },
                    new FileDialogFilter { Name = "All Files", Extensions = { "*" } }
                }
            };

            string? outputFilename = await sfd.ShowAsync(_window);
            if (string.IsNullOrWhiteSpace(outputFilename))
            {
                return;
            }

            OpenFolderDialog ofd = new OpenFolderDialog();
            string? folderPath = await ofd.ShowAsync(_window);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            List<FileEntry>? fileEntries = CollectFileEntries(folderPath);
            if (fileEntries == null || fileEntries.Count == 0)
            {
                return;
            }

            Files.Clear();
            foreach (var entry in fileEntries)
            {
                Files.Add(entry.FileName);
            }

            string selectedFlag = SelectedFlag;
            UnityHeader header = new UnityHeader(selectedFlag);

            byte[] headerData = await BuildHeaderData(fileEntries, header);
            header.FileUnzipSize = headerData.Length;

            byte[] compData = compress ? LzmaHelper.CompressData(headerData) : headerData;

            byte[] mainHeader = BuildMainHeader(header, compData.Length);

            byte[] outputData = new byte[mainHeader.Length + compData.Length];
            Buffer.BlockCopy(mainHeader, 0, outputData, 0, mainHeader.Length);
            Buffer.BlockCopy(compData, 0, outputData, mainHeader.Length, compData.Length);

            try
            {
                await File.WriteAllBytesAsync(outputFilename, outputData);
                await MessageBoxManager.GetMessageBoxStandard("Success", "Packing completed successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to write output file:\n{ex.Message}").ShowAsync();
            }
        }

        // Сбор всех файлов из указанной папки
        private List<FileEntry>? CollectFileEntries(string folderPath)
        {
            List<string> files = Directory.GetFiles(folderPath).Select(Path.GetFileName).ToList();
            if (files.Count == 0)
            {
                _ = MessageBoxManager.GetMessageBoxStandard("Ошибка", "В выбранной папке нет файлов.").ShowAsync();
                return null;
            }

            var scriptAssemblies = files
                .Where(f => f.StartsWith("Assembly", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            var engineAssemblies = files
                .Where(f => (f.StartsWith("Mono", StringComparison.OrdinalIgnoreCase) || f.StartsWith("System", StringComparison.OrdinalIgnoreCase)) && !f.StartsWith("Assembly", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            var otherAssemblies = files
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                            !f.StartsWith("Assembly", StringComparison.OrdinalIgnoreCase) &&
                            !f.StartsWith("Mono", StringComparison.OrdinalIgnoreCase) &&
                            !f.StartsWith("System", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            var assets = files
                .Where(f => !f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.Ordinal);

            List<string> ordered = scriptAssemblies
                .Concat(engineAssemblies)
                .Concat(otherAssemblies)
                .Concat(assets)
                .ToList();

            List<FileEntry> entries = new List<FileEntry>();
            foreach (string fileName in ordered)
            {
                string fullPath = Path.Combine(folderPath, fileName);
                long size = new FileInfo(fullPath).Length;
                entries.Add(new FileEntry(fileName, fullPath, size));
            }
            return entries;
        }

        // Построение данных заголовка
        private async Task<byte[]> BuildHeaderData(List<FileEntry> fileEntries, UnityHeader header)
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
                byte[] fileBytes = await File.ReadAllBytesAsync(fileEntry.FullPath);
                Buffer.BlockCopy(fileBytes, 0, headerData, fileDataPos, fileBytes.Length);
                fileDataPos += fileBytes.Length;
            }

            return headerData;
        }

        // Построение основного заголовка
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

        // Показ диалога выбора файла Unity
        private async Task<string?> ShowUnityFileDialog()
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                AllowMultiple = false,
                Filters = new List<FileDialogFilter>
                {
                    new FileDialogFilter { Name = "Unity web player", Extensions = { "unity3d" } },
                    new FileDialogFilter { Name = "All Files", Extensions = { "*" } }
                }
            };
            string[]? result = await ofd.ShowAsync(_window);
            if (result == null || result.Length == 0)
                return null;
            return result[0];
        }

        // Извлечение файлов из пакета
        private async Task ExtractFiles()
        {
            string? inputFilename = await ShowUnityFileDialog();
            if (inputFilename == null)
                return;
            string? inputDir = Path.GetDirectoryName(inputFilename);
            string outputDir = Path.Combine(inputDir!, "uncompressfiles");
            Directory.CreateDirectory(outputDir);

            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(inputFilename);
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
                    await File.WriteAllBytesAsync(outPath, fileData);
                }
                await MessageBoxManager.GetMessageBoxStandard("Success", "Files extracted successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", $"Extraction failed:\n{ex.Message}").ShowAsync();
            }
        }

        // Извлечение необработанного заголовка
        private async Task ExtractRawHeader()
        {
            string? inputFilename = await ShowUnityFileDialog();
            if (inputFilename == null)
                return;
            string? inputDir = Path.GetDirectoryName(inputFilename);
            string outputFile = Path.Combine(inputDir!, "uncompress_file");

            try
            {
                byte[] fileContent = await File.ReadAllBytesAsync(inputFilename);
                if (fileContent.Length < UnityHeader.MainHeaderSize)
                    throw new Exception("Invalid header.");
                int compDataLength = fileContent.Length - UnityHeader.MainHeaderSize;
                byte[] compData = new byte[compDataLength];
                Buffer.BlockCopy(fileContent, UnityHeader.MainHeaderSize, compData, 0, compDataLength);
                byte[] decomData = LzmaHelper.DecompressData(compData);
                await File.WriteAllBytesAsync(outputFile, decomData);
                await MessageBoxManager.GetMessageBoxStandard("Success", "Raw header extracted successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", $"Extraction failed:\n{ex.Message}").ShowAsync();
            }
        }
    }
}

