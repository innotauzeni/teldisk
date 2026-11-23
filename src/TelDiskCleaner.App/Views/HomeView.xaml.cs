using System.Windows;
using System.Windows.Controls;
using TelDiskCleaner.App.ViewModels;
using TelDiskCleaner.Core.Common;

namespace TelDiskCleaner.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private async void OnCleanNowClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not HomeViewModel viewModel) return;

        if (viewModel.HasFullAccess)
        {
            var answer = MessageBox.Show(Window.GetWindow(this)!,
                $"Remove about {SizeFormatter.Format(viewModel.JunkBytes)} of temp files, Recycle Bin items and caches?\n\nThis can't be undone, but these files are safe to remove.",
                "Clean now", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes) return;
        }

        await viewModel.CleanNowAsync();
    }
}
