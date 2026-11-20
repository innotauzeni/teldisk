using System.Windows;
using TelDiskCleaner.App.ViewModels;

namespace TelDiskCleaner.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
