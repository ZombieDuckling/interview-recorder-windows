using System.Text.Json;
using System.Text.Json.Serialization;
using Recorder.Core.Abstractions;
using Recorder.Core.IO;

namespace Recorder.Core.Settings;

public sealed class AppSettings
{
    [JsonPropertyOrder(0)]
    public string DataRoot { get; set; } = AppSettingsPaths.DefaultDataRoot;

    [JsonPropertyOrder(1)]
    public int? RetentionDays { get; set; } = 365;

    [JsonPropertyOrder(2)]
    public int ThreadCap { get; set; } = 4;

    [JsonPropertyOrder(3)]
    public TranscriptionPreset Preset { get; set; } = TranscriptionPreset.Balanced;

    [JsonPropertyOrder(4)]
    public bool FirstRunCompleted { get; set; }
}

public static class AppSettingsPaths
{
    public static string DefaultDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Interview Recorder");

    public static string SettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InterviewRecorder", "settings.json");
}

public static class SyncFolderDetection
{
    public static bool IsKnownSyncFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        if (ContainsKnownSyncSegment(full))
        {
            return true;
        }

        foreach (var env in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var root = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(root))
            {
                var envRoot = Path.GetFullPath(root);
                if (full.StartsWith(envRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsKnownSyncSegment(string fullPath)
    {
        ReadOnlySpan<string> markers =
        [
            $"{Path.DirectorySeparatorChar}OneDrive",
            $"{Path.DirectorySeparatorChar}Dropbox",
            $"{Path.DirectorySeparatorChar}Google Drive",
            $"{Path.DirectorySeparatorChar}iCloudDrive",
            $"{Path.DirectorySeparatorChar}iCloud Drive",
        ];

        foreach (var marker in markers)
        {
            if (fullPath.Contains(marker, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IndentSize = 2,
    };

    public static AppSettingsLoadResult LoadOrDefault()
    {
        var path = AppSettingsPaths.SettingsFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            return new AppSettingsLoadResult(new AppSettings(), CorruptSettingsMessage: null);
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return new AppSettingsLoadResult(settings, CorruptSettingsMessage: null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var backup = path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            try
            {
                File.Move(path, backup, overwrite: false);
            }
            catch (IOException)
            {
            }

            var message =
                "Your settings file could not be read and was renamed so it is not lost. "
                + "Choose your data folder again in Welcome or Privacy settings.";
            return new AppSettingsLoadResult(new AppSettings(), message);
        }
    }

    public static async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsPaths.SettingsFilePath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await AtomicFileWrite.WriteAllTextAsync(AppSettingsPaths.SettingsFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.RetentionDays is < 1 or > 3650)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Retention must be between 1 and 3650 days.");
        }

        if (settings.ThreadCap is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Thread cap must be between 1 and 32.");
        }
    }
}

public sealed record AppSettingsLoadResult(AppSettings Settings, string? CorruptSettingsMessage);
