using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Services;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.App.ViewModels;

public partial class RecordingConsoleViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly System.Timers.Timer _uiTimer;

    public RecordingConsoleViewModel(AppServices services)
    {
        _services = services;
        MicMeter = new LevelMeterViewModel { Label = "Microphone" };
        MeetingMeter = new LevelMeterViewModel { Label = "Meeting audio" };
        _services.Workflow.LevelsAvailable += OnLevels;
        _services.Workflow.Changed += OnWorkflowChanged;
        _uiTimer = new System.Timers.Timer(500);
        _uiTimer.Elapsed += (_, _) => App.Current.Dispatcher.Invoke(UpdateFromWorkflow);
        _uiTimer.Start();
        UpdateFromWorkflow();
        UpdateDisk();
    }

    public LevelMeterViewModel MicMeter { get; }

    public LevelMeterViewModel MeetingMeter { get; }

    [ObservableProperty]
    private TimeSpan _elapsed;

    [ObservableProperty]
    private string _statusMessage = "Recording in progress.";

    [ObservableProperty]
    private string _freeDiskText = string.Empty;

    [ObservableProperty]
    private bool _isRecording;

    public event EventHandler? StopRequested;

    [RelayCommand]
    private void RequestStop() => StopRequested?.Invoke(this, EventArgs.Empty);

    private void OnLevels(object? sender, LevelSample sample)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (sample.Role == StreamRole.Microphone)
            {
                MicMeter.PeakDbfs = sample.PeakDbfs;
                MicMeter.RmsDbfs = sample.RmsDbfs;
            }
            else
            {
                MeetingMeter.PeakDbfs = sample.PeakDbfs;
                MeetingMeter.RmsDbfs = sample.RmsDbfs;
            }
        });
    }

    private void OnWorkflowChanged(object? sender, EventArgs e) =>
        App.Current.Dispatcher.Invoke(UpdateFromWorkflow);

    private void UpdateFromWorkflow()
    {
        IsRecording = _services.Workflow.IsRecording;
        Elapsed = _services.Workflow.RecordingElapsed;
        StatusMessage = _services.Workflow.Status.Message;
        UpdateDisk();
    }

    private void UpdateDisk()
    {
        try
        {
            var root = _services.Settings.Current.DataRoot;
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            var freeGb = drive.AvailableFreeSpace / (1024d * 1024 * 1024);
            FreeDiskText = freeGb < 2
                ? $"Low disk space: {freeGb:F1} GB free — stop soon to avoid losing the recording."
                : $"{freeGb:F1} GB free disk space";
        }
        catch (IOException ex)
        {
            FreeDiskText = ex.Message;
        }
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _services.Workflow.LevelsAvailable -= OnLevels;
        _services.Workflow.Changed -= OnWorkflowChanged;
        GC.SuppressFinalize(this);
    }
}
