using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Navigation;
using Recorder.App.Services;
using Recorder.Core.Formats;

namespace Recorder.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Welcome = new WelcomeViewModel(services);
        Devices = new DevicesViewModel(services);
        NewInterview = new NewInterviewViewModel(services);
        RecordingConsole = new RecordingConsoleViewModel(services);
        Processing = new ProcessingProgressViewModel(services);
        SessionLibrary = new SessionLibraryViewModel(services);
        Privacy = new PrivacySettingsViewModel(services);
        Diagnostics = new DiagnosticsHelpViewModel(services);

        RecordingConsole.StopRequested += (_, _) => _ = ConfirmStopRecordingAsync();
        SessionLibrary.OpenRequested += (_, row) => HandleOpenSession(row);

        _services.Workflow.Changed += OnWorkflowChanged;

        NavItems =
        [
            new NavItem(AppPage.Welcome, "Welcome", "NavWelcome"),
            new NavItem(AppPage.Devices, "Devices", "NavDevices"),
            new NavItem(AppPage.NewInterview, "New interview", "NavNewInterview"),
            new NavItem(AppPage.SessionLibrary, "Session library", "NavLibrary"),
            new NavItem(AppPage.PrivacySettings, "Privacy", "NavPrivacy"),
            new NavItem(AppPage.DiagnosticsHelp, "Diagnostics", "NavDiagnostics"),
        ];

        CurrentPage = services.Settings.Current.FirstRunCompleted ? AppPage.NewInterview : AppPage.Welcome;
        SyncWorkflowPage();
    }

    public WelcomeViewModel Welcome { get; }

    public DevicesViewModel Devices { get; }

    public NewInterviewViewModel NewInterview { get; }

    public RecordingConsoleViewModel RecordingConsole { get; }

    public ProcessingProgressViewModel Processing { get; }

    public SessionLibraryViewModel SessionLibrary { get; }

    public PrivacySettingsViewModel Privacy { get; }

    public DiagnosticsHelpViewModel Diagnostics { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    private AppPage _currentPage;

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private string _windowStatus = "Ready";

    public bool IsRecording => _services.Workflow.IsRecording;

    partial void OnCurrentPageChanged(AppPage value)
    {
        if (value is AppPage.Welcome or AppPage.Devices or AppPage.NewInterview or AppPage.SessionLibrary or AppPage.PrivacySettings or AppPage.DiagnosticsHelp)
        {
            SelectedNavItem = NavItems.FirstOrDefault(n => n.Page == value);
        }
    }

    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        if (value is not null && !IsRecording && !_services.Workflow.Status.IsBusy)
        {
            CurrentPage = value.Page;
        }
    }

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        if (IsRecording || _services.Workflow.Status.IsBusy)
        {
            return;
        }

        CurrentPage = page;
        SelectedNavItem = NavItems.FirstOrDefault(n => n.Page == page);
    }

    [RelayCommand]
    private async Task StartInterviewAsync()
    {
        try
        {
            var request = NewInterview.BuildStartRequest();
            await _services.Workflow.StartAsync(request, CancellationToken.None);
            SyncWorkflowPage();
            WindowStatus = "Recording";
        }
        catch (Exception ex)
        {
            NewInterview.StatusMessage = ex.Message;
            WindowStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ConfirmStopRecordingAsync()
    {
        var confirm = System.Windows.MessageBox.Show(
            "Stop recording? The interview will move to finalization.",
            "Stop recording",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _services.Workflow.StopAsync(CancellationToken.None);
            SyncWorkflowPage();
        }
        catch (Exception ex)
        {
            WindowStatus = ex.Message;
        }
    }

    public bool ConfirmExit()
    {
        if (!IsRecording)
        {
            return true;
        }

        System.Windows.MessageBox.Show(
            "A recording is in progress. Stop the recording before you close Interview Recorder.",
            "Recording in progress",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
        return false;
    }

    private void OnWorkflowChanged(object? sender, EventArgs e) =>
        App.Current.Dispatcher.Invoke(() =>
        {
            SyncWorkflowPage();
            OnPropertyChanged(nameof(IsRecording));
            WindowStatus = _services.Workflow.Status.Message;
            Processing.Refresh();
        });

    private void SyncWorkflowPage()
    {
        if (_services.Workflow.IsRecording)
        {
            CurrentPage = AppPage.RecordingConsole;
            return;
        }

        var state = _services.Workflow.ActiveSession?.State;
        if (_services.Workflow.Status.IsBusy
            || state is SessionState.Finalizing
            or SessionState.Transcribing
            or SessionState.Failed)
        {
            CurrentPage = AppPage.ProcessingProgress;
        }
    }

    private void HandleOpenSession(SessionRowViewModel row)
    {
        WindowStatus = $"Selected {row.SessionCode} ({row.State}).";
        if (row.State is SessionState.ReadyToTranscribe or SessionState.Failed)
        {
            CurrentPage = AppPage.ProcessingProgress;
            _ = _services.Workflow.TranscribeAsync(CancellationToken.None);
        }
    }
}
