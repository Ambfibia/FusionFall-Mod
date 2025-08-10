using Avalonia.Controls;
using FusionFall_Mod.ViewModels;

namespace FusionFall_Mod
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // Установка контекста данных на модель представления
            DataContext = new MainWindowViewModel(this);
        }
    }
}

