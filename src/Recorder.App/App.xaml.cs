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
        ISessionWorkflow workflow = new UnavailableSessionWorkflow();
        var services = new AppServices(
            settings,
            workflow,
            BackendFactory.CreateAudioCapture(),
            BackendFactory.CreateRetention(),
            BackendFactory.CreateFileProtector());
        AppServices.Initialize(services);
        MainViewModel = new MainViewModel(services);
        HighContrastTheme.ApplyIfNeeded(this);

        var window = new MainWindow();
        window.Show();
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
    }
}
