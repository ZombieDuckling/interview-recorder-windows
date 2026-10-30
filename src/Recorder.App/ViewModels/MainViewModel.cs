using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Navigation;
using Recorder.App.Services;
using Recorder.Core.Formats;
using Recorder.Core.IO;

namespace Recorder.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private RecordingConsoleViewModel? _recordingConsole;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Welcome = new WelcomeViewModel(services);
        Devices = new DevicesViewModel(services);
        NewInterview = new NewInterviewViewModel(services);
        Processing = new ProcessingProgressViewModel(services);
        SessionLibrary = new SessionLibraryViewModel(services);
        Privacy = new PrivacySettingsViewModel(services);
        Diagnostics = new DiagnosticsHelpViewModel(services);

        SessionLibrary.OpenRequested += (_, row) => _ = HandleOpenSessionAsync(row);
        SessionLibrary.RecoverRequested += (_, row) => _ = RecoverSessionAsync(row);

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

    public RecordingConsoleViewModel RecordingConsole =>
        _recordingConsole ??= CreateRecordingConsole();

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

    private RecordingConsoleViewModel CreateRecordingConsole()
    {
        var vm = new RecordingConsoleViewModel(_services);
        vm.StopRequested += (_, _) => _ = ConfirmStopRecordingAsync();
        return vm;
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        if (value is AppPage.Welcome or AppPage.Devices or AppPage.NewInterview or AppPage.SessionLibrary or AppPage.PrivacySettings or AppPage.DiagnosticsHelp)
        {
            SelectedNavItem = NavItems.FirstOrDefault(n => n.Page == value);
        }

        if (value == AppPage.RecordingConsole)
        {
            _ = RecordingConsole;
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
            if (!NewInterview.ConfirmMissingInputs() || !NewInterview.ConfirmScopeNotice())
            {
                return;
            }

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
            string? dir = _services.Workflow.ActiveSession?.SessionDirectory;
            CurrentPage = AppPage.ProcessingProgress;
            await _services.Workflow.StopAsync(CancellationToken.None);
            SyncWorkflowPage();
            if (dir is not null && File.Exists(Path.Combine(dir, "transcript.json")) && _services.Workflow.Status.ErrorDetail is null)
            {
                OpenTranscriptEditor(dir);
            }
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
        App.Current.Dispatcher.BeginInvoke(() =>
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

    private async Task HandleOpenSessionAsync(SessionRowViewModel row)
    {
        if (row.Damaged)
        {
            WindowStatus = row.ListMessage ?? "This session could not be read.";
            return;
        }

        if (row.NeedsRecovery)
        {
            await RecoverSessionAsync(row);
            return;
        }

        if (row.State == SessionState.Exported)
        {
            OpenTranscriptEditor(row.Directory);
            return;
        }

        if (row.State == SessionState.Failed)
        {
            CurrentPage = AppPage.ProcessingProgress;
            try
            {
                var manifest = SessionManifestIO.Read(Path.Combine(row.Directory, "session.json"));
                if (manifest.Error?.Stage is SessionErrorStage.Finalizing or SessionErrorStage.Recording)
                {
                    await _services.Workflow.RecoverAsync(row.Directory, CancellationToken.None);
                }
                else
                {
                    await _services.Workflow.RetryCurrentOperationAsync(row.Directory, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                WindowStatus = ex.Message;
            }

            return;
        }

        if (row.State == SessionState.ReadyToTranscribe)
        {
            CurrentPage = AppPage.ProcessingProgress;
            try
            {
                await _services.Workflow.TranscribeAsync(row.Directory, CancellationToken.None);
            }
            catch (Exception ex)
            {
                WindowStatus = ex.Message;
            }
        }
    }

    [ObservableProperty]
    private TranscriptEditorViewModel? _transcriptEditor;

    private void OpenTranscriptEditor(string sessionDirectory)
    {
        try
        {
            var transcript = TranscriptIO.Read(Path.Combine(sessionDirectory, "transcript.json"));
            TranscriptEditor = new TranscriptEditorViewModel(_services.Workflow, sessionDirectory, transcript);
            CurrentPage = AppPage.TranscriptEditor;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CorruptedFileException or UnsupportedSchemaVersionException)
        {
            WindowStatus = ex.Message;
        }
    }

    private async Task RecoverSessionAsync(SessionRowViewModel row)
    {
        try
        {
            await _services.Workflow.RecoverAsync(row.Directory, CancellationToken.None);
            CurrentPage = AppPage.ProcessingProgress;
            WindowStatus = "Recovery started.";
        }
        catch (Exception ex)
        {
            WindowStatus = ex.Message;
        }
    }
}
