using System.Text.Json;

namespace Recorder.App.Services;

public sealed class UserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private UserSettings _current;

    public UserSettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(appData, "Interview Recorder");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "user-settings.json");
        _current = LoadFromDisk() ?? new UserSettings();
    }

    public UserSettings Current => _current;

    public event EventHandler? SettingsChanged;

    public void Save(UserSettings settings)
    {
        _current = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private UserSettings? LoadFromDisk()
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<UserSettings>(json);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
