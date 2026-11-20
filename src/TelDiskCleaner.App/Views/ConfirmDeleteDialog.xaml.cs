using System.Windows;

namespace TelDiskCleaner.App.Views;

public partial class ConfirmDeleteDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDeleteDialog(IEnumerable<string> paths, string totalSizeDisplay)
    {
        InitializeComponent();
        HeaderText.Text = $"This will delete the following items, totalling {totalSizeDisplay}:";
        foreach (var path in paths) PathsList.Items.Add(path);
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
