using System.Windows;
using System.Windows.Controls;
using TelDiskCleaner.App.ViewModels;
using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.App.Views;

public partial class ProjectCleanerView : UserControl
{
    public ProjectCleanerView()
    {
        InitializeComponent();
    }

    private void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectCleanerViewModel viewModel) return;

        var selected = viewModel.GetSelectedCandidates();
        if (selected.Count == 0)
        {
            MessageBox.Show("Select at least one project first.", "Nothing selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var paths = selected.SelectMany(c => c.Candidate.Artifacts.Select(a => a.Path));
        var totalBytes = selected.Sum(c => c.Candidate.TotalArtifactSize);

        var dialog = new ConfirmDeleteDialog(paths, SizeFormatter.Format(totalBytes))
        {
            Owner = Window.GetWindow(this),
        };

        if (dialog.ShowDialog() == true && dialog.Confirmed)
        {
            viewModel.DeleteCandidates(selected);
        }
    }
}
