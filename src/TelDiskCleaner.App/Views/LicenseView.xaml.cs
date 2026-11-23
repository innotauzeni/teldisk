using System.Windows;
using System.Windows.Controls;
using TelDiskCleaner.App.ViewModels;

namespace TelDiskCleaner.App.Views;

public partial class LicenseView : UserControl
{
    public LicenseView()
    {
        InitializeComponent();
    }

    private async void OnDeactivateClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LicenseViewModel viewModel) return;
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            "Remove Pro from this PC so you can use the key on another PC?",
            "Deactivate", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) await viewModel.DeactivateAsync();
    }
}
