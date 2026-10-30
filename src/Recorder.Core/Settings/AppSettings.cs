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

    public static AppSettings LoadOrDefault()
    {
        var path = AppSettingsPaths.SettingsFilePath;
        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await AtomicFileWrite.WriteAllTextAsync(AppSettingsPaths.SettingsFilePath, json, cancellationToken).ConfigureAwait(false);
    }
}
