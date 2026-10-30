using System.Windows;
using Recorder.App.Helpers;
using Recorder.App.Services;
using Recorder.App.ViewModels;

namespace Recorder.App;

public partial class App : Application
{
    internal static MainViewModel MainViewModel { get; private set; } = null!;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var settings = new UserSettingsStore();
        var services = Composition.AppComposition.Build(settings);
        AppServices.Initialize(services);
        MainViewModel = new MainViewModel(services);
        HighContrastTheme.ApplyIfNeeded(this);

        if (!string.IsNullOrWhiteSpace(settings.CorruptSettingsMessage))
        {
            MessageBox.Show(
                settings.CorruptSettingsMessage,
                "Settings could not be loaded",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var window = new MainWindow();
        window.Show();
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        (AppServices.Instance?.Workflow as IDisposable)?.Dispose();
    }
}
