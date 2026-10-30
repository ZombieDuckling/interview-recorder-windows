namespace Recorder.App.Services;

using Recorder.Core.Abstractions;
using Recorder.Core.Settings;

public sealed class UserSettings
{
    public string DataRoot { get; set; } = AppSettingsPaths.DefaultDataRoot;

    public int RetentionDays { get; set; } = 365;

    public int MaxTranscriptionThreads { get; set; } = 4;

    public TranscriptionPreset TranscriptionPreset { get; set; } = TranscriptionPreset.Balanced;

    public bool FirstRunCompleted { get; set; }

    public static string DefaultDataRoot() => AppSettingsPaths.DefaultDataRoot;
}

public interface IUserSettingsStore
{
    UserSettings Current { get; }

    string? CorruptSettingsMessage { get; }

    void Save(UserSettings settings);

    event EventHandler? SettingsChanged;
}
