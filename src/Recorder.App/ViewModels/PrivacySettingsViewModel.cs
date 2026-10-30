using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Helpers;
using Recorder.App.Services;
using Recorder.Core.Abstractions;

namespace Recorder.App.ViewModels;

public partial class PrivacySettingsViewModel : ObservableObject
{
    private readonly AppServices _services;

    public PrivacySettingsViewModel(AppServices services)
    {
        _services = services;
        LoadFromSettings();
    }

    [ObservableProperty]
    private string _dataRoot = string.Empty;

    [ObservableProperty]
    private int _retentionDays;

    [ObservableProperty]
    private int _maxThreads;

    [ObservableProperty]
    private TranscriptionPreset _preset;

    [ObservableProperty]
    private string _syncFolderWarning = string.Empty;

    [ObservableProperty]
    private string _encryptionStatus =
        "What is protected: each session folder is restricted to your Windows account (other accounts on this PC cannot open it). " +
        "What is not encrypted by the app: the recording chunks, the MP4, the transcript and the Word file are ordinary files. " +
        "Turn on BitLocker or Windows device encryption to encrypt them on disk. Nothing is ever sent over the network.";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    partial void OnDataRootChanged(string value) =>
        SyncFolderWarning = SyncFolderDetector.IsLikelySyncFolder(value)
            ? "Warning: this folder may sync to the cloud. Prefer a local, non-synced location."
            : string.Empty;

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
    private void Save()
    {
        var settings = _services.Settings.Current;
        settings.DataRoot = DataRoot;
        settings.RetentionDays = RetentionDays;
        settings.MaxTranscriptionThreads = MaxThreads;
        settings.TranscriptionPreset = Preset;
        _services.Settings.Save(settings);
        StatusMessage = "Privacy and retention settings saved.";
    }

    [RelayCommand]
    private void ApplyFastPreset()
    {
        Preset = TranscriptionPreset.Fast;
        MaxThreads = 2;
    }

    [RelayCommand]
    private void ApplyBalancedPreset()
    {
        Preset = TranscriptionPreset.Balanced;
        MaxThreads = 4;
    }

    private void LoadFromSettings()
    {
        var s = _services.Settings.Current;
        DataRoot = s.DataRoot;
        RetentionDays = s.RetentionDays;
        MaxThreads = s.MaxTranscriptionThreads;
        Preset = s.TranscriptionPreset;
        OnDataRootChanged(DataRoot);
    }
}
