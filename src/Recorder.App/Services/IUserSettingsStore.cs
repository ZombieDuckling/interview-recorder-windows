namespace Recorder.App.Services;

using Recorder.Core.Abstractions;

public sealed class UserSettings
{
    public string DataRoot { get; set; } = DefaultDataRoot();

    public int RetentionDays { get; set; } = 90;

    public int MaxTranscriptionThreads { get; set; } = 2;

    public TranscriptionPreset TranscriptionPreset { get; set; } = TranscriptionPreset.Balanced;

    public bool FirstRunCompleted { get; set; }

    public static string DefaultDataRoot()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "Interview Recorder");
    }
}

public interface IUserSettingsStore
{
    UserSettings Current { get; }

    void Save(UserSettings settings);

    event EventHandler? SettingsChanged;
}
