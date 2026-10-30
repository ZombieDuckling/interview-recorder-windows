using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Services;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;
using System.Windows.Threading;

namespace Recorder.App.ViewModels;

public partial class RecordingConsoleViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _uiTimer;
    private LevelSample _latestMic = new(StreamRole.Microphone, -60f, -60f);
    private LevelSample _latestMeeting = new(StreamRole.Meeting, -60f, -60f);
    private bool _disposed;

    public RecordingConsoleViewModel(AppServices services)
    {
        _services = services;
        MicMeter = new LevelMeterViewModel { Label = "Microphone" };
        MeetingMeter = new LevelMeterViewModel { Label = "Meeting audio" };
        _services.Workflow.LevelsAvailable += OnLevels;
        _services.Workflow.Changed += OnWorkflowChanged;
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _uiTimer.Tick += (_, _) => RefreshUi();
        _uiTimer.Start();
        RefreshUi();
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
        if (sample.Role == StreamRole.Microphone)
        {
            _latestMic = sample;
        }
        else
        {
            _latestMeeting = sample;
        }
    }

    private void OnWorkflowChanged(object? sender, EventArgs e) => RefreshUi();

    private void RefreshUi()
    {
        MicMeter.PeakDbfs = _latestMic.PeakDbfs;
        MicMeter.RmsDbfs = _latestMic.RmsDbfs;
        MeetingMeter.PeakDbfs = _latestMeeting.PeakDbfs;
        MeetingMeter.RmsDbfs = _latestMeeting.RmsDbfs;
        IsRecording = _services.Workflow.IsRecording;
        Elapsed = _services.Workflow.RecordingElapsed;
        StatusMessage = _services.Workflow.Status.Message;
        FreeDiskText = ReadFreeDisk();
    }

    private string ReadFreeDisk()
    {
        try
        {
            var root = _services.Settings.Current.DataRoot;
            var pathRoot = Path.GetPathRoot(Path.GetFullPath(root));
            if (string.IsNullOrEmpty(pathRoot))
            {
                return "Disk space unknown for this storage location.";
            }

            var drive = new DriveInfo(pathRoot);
            var freeGb = drive.AvailableFreeSpace / (1024d * 1024 * 1024);
            return freeGb < 2
                ? $"Low disk space: {freeGb:F1} GB free — stop soon to avoid losing the recording."
                : $"{freeGb:F1} GB free disk space";
        }
        catch (Exception)
        {
            return "Disk space unknown.";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiTimer.Stop();
        _services.Workflow.LevelsAvailable -= OnLevels;
        _services.Workflow.Changed -= OnWorkflowChanged;
        GC.SuppressFinalize(this);
    }
}
