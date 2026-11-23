using System.Windows;
using System.Windows.Controls;
using TelDiskCleaner.App.ViewModels;

namespace TelDiskCleaner.App.Views;

public partial class RecoveryView : UserControl
{
    public RecoveryView()
    {
        InitializeComponent();
    }

    private async void OnDeleteNowClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecoveryViewModel viewModel) return;
        var selected = viewModel.Selected();
        if (selected.Count == 0)
        {
            viewModel.StatusMessage = "Tick the files you want to delete for good first.";
            return;
        }

        var answer = MessageBox.Show(Window.GetWindow(this)!,
            $"Delete {selected.Count} file(s) for good?\n\nThey can't be restored after this.",
            "Delete now", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) await viewModel.DeleteSelectedNowAsync();
    }
}
