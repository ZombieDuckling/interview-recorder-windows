using Recorder.Core.Settings;

namespace Recorder.App.Services;

public sealed class UserSettingsStore : IUserSettingsStore
{
    private UserSettings _current;

    public UserSettingsStore()
    {
        var loaded = AppSettingsStore.LoadOrDefault();
        _current = Map(loaded.Settings);
        CorruptSettingsMessage = loaded.CorruptSettingsMessage;
    }

    public UserSettings Current => _current;

    public string? CorruptSettingsMessage { get; }

    public event EventHandler? SettingsChanged;

    public void Save(UserSettings settings)
    {
        AppSettingsStore.SaveAsync(MapToAppSettings(settings)).GetAwaiter().GetResult();
        _current = settings;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static UserSettings Map(AppSettings settings) =>
        new()
        {
            DataRoot = settings.DataRoot,
            RetentionDays = settings.RetentionDays ?? 365,
            MaxTranscriptionThreads = settings.ThreadCap,
            TranscriptionPreset = settings.Preset,
            FirstRunCompleted = settings.FirstRunCompleted,
        };

    private static AppSettings MapToAppSettings(UserSettings settings) =>
        new()
        {
            DataRoot = settings.DataRoot,
            RetentionDays = settings.RetentionDays,
            ThreadCap = settings.MaxTranscriptionThreads,
            Preset = settings.TranscriptionPreset,
            FirstRunCompleted = settings.FirstRunCompleted,
        };
}
