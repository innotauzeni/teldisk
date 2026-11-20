using System.Windows;
using System.Windows.Controls;
using TelDiskCleaner.App.ViewModels;
using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.App.Views;

public partial class TempCleanupView : UserControl
{
    public TempCleanupView()
    {
        InitializeComponent();
    }

    private async void OnCleanSelectedClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TempCleanupViewModel viewModel) return;

        var selected = viewModel.GetSelectedLocations();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one location first.", "Nothing selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var paths = selected.Select(l => l.Name);
        var totalBytes = selected.Sum(l => l.Location.SizeBytes);

        var dialog = new ConfirmDeleteDialog(paths, SizeFormatter.Format(totalBytes))
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true && dialog.Confirmed)
        {
            await viewModel.CleanLocationsAsync(selected);
        }
    }
}
