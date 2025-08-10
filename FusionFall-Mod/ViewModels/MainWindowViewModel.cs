using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FusionFall_Mod.Core;
using FusionFall_Mod.Infrastructure;
using FusionFall_Mod.Models;
using MsBox.Avalonia;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;

namespace FusionFall_Mod.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly Window _window;
        private readonly StringBuilder _console = new StringBuilder();

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindowViewModel(Window window)
        {
            _window = window;
            PackCommand = new AsyncCommand(PackUnity3D);
            ExtractCommand = new AsyncCommand(ExtractFiles);
            PackAssetsCommand = new AsyncCommand(PackAssets);
            UnpackAssetsCommand = new AsyncCommand(UnpackAssets);
        }

        public string ConsoleText => _console.ToString();

        public ICommand PackCommand { get; }
        public ICommand ExtractCommand { get; }
        public ICommand PackAssetsCommand { get; }
        public ICommand UnpackAssetsCommand { get; }

        // Добавление сообщения в консоль
        private void Log(string message)
        {
            _console.AppendLine(message);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConsoleText)));
        }

        // Упаковка ресурсов в файл unity3d
        private async Task PackUnity3D()
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

            try
            {
                Log("Начало упаковки файлов.");
                await UnityPackageHelper.PackAsync(folderPath, outputFilename, UnityHeader.DefaultFlag);
                Log("Упаковка завершена.");
                await MessageBoxManager.GetMessageBoxStandard("Success", "Packing completed successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                Log($"Ошибка упаковки: {ex.Message}");
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
            string fileBase = Path.GetFileNameWithoutExtension(inputFilename);
            string outputDir = Path.Combine(inputDir!, $"{fileBase}_unpacked");
            // Формирование пути распакованной папки
            Directory.CreateDirectory(outputDir);

            try
            {
                Log("Начало распаковки файлов.");
                await UnityPackageHelper.ExtractAsync(inputFilename, outputDir);
                Log("Распаковка завершена.");
                await MessageBoxManager.GetMessageBoxStandard("Success", "Files extracted successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                Log($"Ошибка распаковки: {ex.Message}");
                await MessageBoxManager.GetMessageBoxStandard("Error", $"Extraction failed:\n{ex.Message}").ShowAsync();
            }
        }

        // Показ диалога выбора файла .assets
        private async Task<string?> ShowAssetsFileDialog()
        {
            FilePickerOpenOptions fpo = new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Unity assets") { Patterns = new[] { "*.assets" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            };
            IReadOnlyList<IStorageFile> files = await _window.StorageProvider.OpenFilePickerAsync(fpo);
            if (files == null || files.Count == 0)
                return null;
            return files[0].TryGetLocalPath();
        }

        // Упаковка файла sharedassets
        private async Task PackAssets()
        {
            IReadOnlyList<IStorageFolder> folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());
            if (folders.Count == 0)
                return;
            string? folderPath = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            FilePickerSaveOptions sfo = new FilePickerSaveOptions
            {
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new FilePickerFileType("Unity assets") { Patterns = new[] { "*.assets" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
                }
            };
            IStorageFile? saveFile = await _window.StorageProvider.SaveFilePickerAsync(sfo);
            string? output = saveFile?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(output))
                return;

            try
            {
                Log("Начало сборки assets.");
                await Task.Run(() => RawAssetsHelper.Pack(folderPath, output));
                Log("Сборка завершена.");
                await MessageBoxManager.GetMessageBoxStandard("Success", "Assets packed successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                Log($"Ошибка упаковки: {ex.Message}");
                await MessageBoxManager.GetMessageBoxStandard("Error", $"Packing failed:\n{ex.Message}").ShowAsync();
            }
        }

        // Распаковка файла sharedassets
        private async Task UnpackAssets()
        {
            string? inputFilename = await ShowAssetsFileDialog();
            if (inputFilename == null)
                return;
            string? inputDir = Path.GetDirectoryName(inputFilename);
            string fileBase = Path.GetFileNameWithoutExtension(inputFilename);
            string outputDir = Path.Combine(inputDir!, $"{fileBase}_unpacked");
            Directory.CreateDirectory(outputDir);

            try
            {
                Log("Начало распаковки assets.");
                await Task.Run(() => RawAssetsHelper.Unpack(inputFilename, outputDir));
                Log("Распаковка завершена.");
                await MessageBoxManager.GetMessageBoxStandard("Success", "Assets unpacked successfully.").ShowAsync();
            }
            catch (Exception ex)
            {
                Log($"Ошибка распаковки: {ex.Message}");
                await MessageBoxManager.GetMessageBoxStandard("Error", $"Unpacking failed:\n{ex.Message}").ShowAsync();
            }
        }

    }
}

