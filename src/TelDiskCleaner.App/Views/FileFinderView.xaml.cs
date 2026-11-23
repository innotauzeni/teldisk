using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TelDiskCleaner.App.ViewModels;
using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.App.Views;

public partial class FileFinderView : UserControl
{
    public FileFinderView()
    {
        InitializeComponent();
    }

    private FileFinderViewModel? ViewModel => DataContext as FileFinderViewModel;

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var dialog = new OpenFolderDialog { Title = "Choose a folder or drive to search", InitialDirectory = ViewModel.Folder };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) ViewModel.Folder = dialog.FolderName;
    }

    private void OnShowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FoundFileViewModel file) FileFinderViewModel.OpenLocation(file.File.Path);
    }

    private async void OnRemoveLargeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        await Remove(ViewModel.SelectedLarge(), "Large file", status => ViewModel.LargeStatus = status);
    }

    private async void OnRemoveDuplicatesClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        await Remove(ViewModel.SelectedDuplicates(), "Duplicate", status => ViewModel.DuplicateStatus = status);
    }

    private async Task Remove(List<FoundFileViewModel> selected, string reason, Action<string> report)
    {
        if (selected.Count == 0)
        {
            report("Tick the files you want to remove first.");
            return;
        }

        var dialog = new ConfirmDeleteDialog(selected.Select(f => f.File.Path), SizeFormatter.Format(selected.Sum(f => f.File.SizeBytes)))
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() == true && dialog.Confirmed)
        {
            report(await ViewModel!.MoveToRecoveryAsync(selected, reason));
        }
    }
}
