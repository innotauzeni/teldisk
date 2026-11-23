using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TelDiskCleaner.App.ViewModels;

namespace TelDiskCleaner.App.Views;

public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
    }

    private BackupViewModel? ViewModel => DataContext as BackupViewModel;

    private string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
    }

    private void OnAddSourceClick(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Choose a folder to back up") is { } folder) ViewModel?.AddSource(folder);
    }

    private void OnBrowseDestinationClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedJob is { } job && PickFolder("Choose where to save backups (a USB or external drive is best)") is { } folder)
        {
            job.Destination = folder;
        }
    }

    private async void OnDeleteJobClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedJob is not { } job) return;
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            $"Remove the backup \"{job.Name}\" from TelDiskCleaner?\n\nFiles already backed up stay on the backup drive.",
            "Delete backup", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) await ViewModel.DeleteSelectedAsync();
    }

    private async void OnRestoreOriginalClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedJob is not { } job) return;
        var answer = MessageBox.Show(Window.GetWindow(this)!,
            $"Copy the latest backup of \"{job.Name}\" back to its original folders?\n\n" +
            "Missing files are put back. If a file on your PC is different from the backup, yours is kept and renamed with \"(before restore)\".",
            "Restore", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) await ViewModel.RestoreAsync(null);
    }

    private async void OnRestoreElsewhereClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (PickFolder("Choose an empty folder to restore into") is { } folder) await ViewModel.RestoreAsync(folder);
    }
}
