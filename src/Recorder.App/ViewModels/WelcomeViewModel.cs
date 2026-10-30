using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Helpers;
using Recorder.App.Services;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.App.ViewModels;

public partial class WelcomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private ICaptureSession? _previewSession;

    public WelcomeViewModel(AppServices services)
    {
        _services = services;
        DataRoot = services.Settings.Current.DataRoot;
        UpdateSyncWarning();
        MicMeter = new LevelMeterViewModel { Label = "Microphone" };
        MeetingMeter = new LevelMeterViewModel { Label = "Meeting audio" };
    }

    public LevelMeterViewModel MicMeter { get; }

    public LevelMeterViewModel MeetingMeter { get; }

    [ObservableProperty]
    private string _dataRoot = string.Empty;

    [ObservableProperty]
    private string _syncFolderWarning = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Choose where interview files are stored, then test your microphone and meeting audio.";

    [ObservableProperty]
    private bool _isTesting;

    partial void OnDataRootChanged(string value) => UpdateSyncWarning();

    [RelayCommand]
    private void BrowseDataRoot()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose interview data folder",
            InitialDirectory = Directory.Exists(DataRoot) ? DataRoot : UserSettings.DefaultDataRoot(),
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            DataRoot = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void SaveDataRoot()
    {
        var settings = _services.Settings.Current;
        settings.DataRoot = DataRoot;
        settings.FirstRunCompleted = true;
        _services.Settings.Save(settings);
        StatusMessage = "Storage location saved.";
    }

    [RelayCommand]
    private async Task StartAudioTestAsync()
    {
        if (IsTesting)
        {
            return;
        }

        try
        {
            IsTesting = true;
            StatusMessage = "Listening for microphone and meeting audio… Play a short sound in your meeting browser.";
            var temp = Path.Combine(Path.GetTempPath(), "InterviewRecorder-preview");
            Directory.CreateDirectory(temp);
            var request = new CaptureRequest(temp, MeetingEndpointId: null, MicrophoneEndpointId: null);
            _previewSession = await _services.AudioCapture.StartAsync(request, CancellationToken.None);
            _previewSession.LevelsAvailable += OnPreviewLevels;
        }
        catch (Exception ex)
        {
            IsTesting = false;
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StopAudioTestAsync()
    {
        if (_previewSession is null)
        {
            IsTesting = false;
            return;
        }

        try
        {
            _previewSession.LevelsAvailable -= OnPreviewLevels;
            await _previewSession.StopAsync(CancellationToken.None);
            await _previewSession.DisposeAsync();
        }
        catch (IOException)
        {
        }
        finally
        {
            _previewSession = null;
            IsTesting = false;
            StatusMessage = "Audio test stopped.";
        }
    }

    private void OnPreviewLevels(object? sender, LevelSample sample)
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
    }

    private void UpdateSyncWarning()
    {
        SyncFolderWarning = SyncFolderDetector.IsLikelySyncFolder(DataRoot)
            ? "This folder may sync to the cloud (OneDrive, iCloud, Dropbox, or Google Drive). Interview audio could leave this PC. Choose a local folder that is not synced."
            : string.Empty;
    }
}
