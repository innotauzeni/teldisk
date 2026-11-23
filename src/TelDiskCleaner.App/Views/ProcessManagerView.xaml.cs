using System.Windows;
using System.Windows.Controls;
using TelDiskCleaner.App.ViewModels;

namespace TelDiskCleaner.App.Views;

public partial class ProcessManagerView : UserControl
{
    public ProcessManagerView()
    {
        InitializeComponent();
    }

    private async void OnEndTaskClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProcessManagerViewModel viewModel) return;
        if ((sender as FrameworkElement)?.DataContext is not ProcessGroupViewModel group) return;

        var what = group.Count > 1 ? $"{group.Name} ({group.Count} processes)" : group.Name;
        var answer = MessageBox.Show(
            Window.GetWindow(this)!,
            $"End {what}?\n\nAny unsaved work in it will be lost.",
            "End task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            await viewModel.EndGroupAsync(group);
        }
    }
}
