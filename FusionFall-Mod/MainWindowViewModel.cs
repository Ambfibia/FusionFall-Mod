using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FusionFall_Mod.Core;
using FusionFall_Mod.Models;
using MsBox.Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
            FilePickerSaveOptions sfo = new FilePickerSaveOptions
            {
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Unity web player") { Patterns = new[] { "*.unity3d" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            };

            IStorageFile? saveFile = await _window.StorageProvider.SaveFilePickerAsync(sfo);
            string? outputFilename = saveFile?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(outputFilename))
            {
                return;
            }

            IReadOnlyList<IStorageFolder> folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());
            if (folders.Count == 0)
            {
                return;
            }

            string? folderPath = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            List<FileEntry> fileEntries = UnityPackageHelper.CollectFileEntries(folderPath);
            if (fileEntries.Count == 0)
            {
                _ = MessageBoxManager.GetMessageBoxStandard("Ошибка", "В выбранной папке нет файлов.").ShowAsync();
                return;
            }

            Files.Clear();
            foreach (var entry in fileEntries)
            {
                Files.Add(entry.FileName);
            }

            try
            {
                await UnityPackageHelper.PackAsync(fileEntries, outputFilename, compress, SelectedFlag);
                await MessageBoxManager.GetMessageBoxStandard("Success", "Packing completed successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                await MessageBoxManager.GetMessageBoxStandard("Error", $"Failed to write output file:\n{ex.Message}").ShowAsync();
            }
        }

        // Показ диалога выбора файла Unity
        private async Task<string?> ShowUnityFileDialog()
        {
            FilePickerOpenOptions fpo = new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Unity web player") { Patterns = new[] { "*.unity3d" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            };
            IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(fpo);
            if (files == null || files.Count == 0)
                return null;
            return files[0].TryGetLocalPath();
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
                await UnityPackageHelper.ExtractAsync(inputFilename, outputDir);
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
                byte[] decomData = await UnityPackageHelper.ExtractRawAsync(inputFilename);
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

