using System.ComponentModel;
using System.Windows;
using Recorder.App.ViewModels;

namespace Recorder.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainViewModel;
        Loaded += (_, _) => SyncNavSelection();
    }

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private void SyncNavSelection()
    {
        ViewModel.SelectedNavItem = ViewModel.NavItems.FirstOrDefault(n => n.Page == ViewModel.CurrentPage);
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.ConfirmExit())
        {
            e.Cancel = true;
        }
    }

    private void ExitMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
