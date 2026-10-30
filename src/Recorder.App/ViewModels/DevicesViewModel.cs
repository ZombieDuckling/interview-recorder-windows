using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Services;
using Recorder.Core.Abstractions;

namespace Recorder.App.ViewModels;

public partial class DeviceRowViewModel : ObservableObject
{
    public DeviceRowViewModel(AudioDeviceInfo device)
    {
        Id = device.Id;
        FriendlyName = device.FriendlyName;
        Kind = device.Kind;
        IsDefault = device.IsDefault;
        Details = $"{device.SampleRate} Hz, {device.Channels} ch";
    }

    public string Id { get; }

    public string FriendlyName { get; }

    public AudioDeviceKind Kind { get; }

    public bool IsDefault { get; }

    public string Details { get; }

    public string DisplayName => IsDefault ? $"{FriendlyName} (default)" : FriendlyName;
}

public partial class DevicesViewModel : ObservableObject
{
    private readonly AppServices _services;

    public DevicesViewModel(AppServices services)
    {
        _services = services;
        Refresh();
    }

    public IList<DeviceRowViewModel> RenderDevices { get; } = new List<DeviceRowViewModel>();

    public IList<DeviceRowViewModel> CaptureDevices { get; } = new List<DeviceRowViewModel>();

    [ObservableProperty]
    private bool _processLoopbackSupported;

    [ObservableProperty]
    private string _processLoopbackText = string.Empty;

    [ObservableProperty]
    private string _freeDiskText = string.Empty;

    [ObservableProperty]
    private string _modelText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private void Refresh()
    {
        RenderDevices.Clear();
        CaptureDevices.Clear();
        foreach (var d in _services.AudioCapture.ListDevices(AudioDeviceKind.Render))
        {
            RenderDevices.Add(new DeviceRowViewModel(d));
        }

        foreach (var d in _services.AudioCapture.ListDevices(AudioDeviceKind.Capture))
        {
            CaptureDevices.Add(new DeviceRowViewModel(d));
        }

        ProcessLoopbackSupported = _services.AudioCapture.IsProcessLoopbackSupported;
        ProcessLoopbackText = ProcessLoopbackSupported
            ? "This PC supports process loopback capture (may isolate a browser tab when configured)."
            : "This PC uses ordinary endpoint loopback only.";

        FreeDiskText = ReadFreeDisk();
        ModelText = ReadModelStatus();
        StatusMessage = "Device list refreshed.";
    }

    private string ReadFreeDisk()
    {
        try
        {
            var root = _services.Settings.Current.DataRoot;
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root);
            }

            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!);
            var freeGb = drive.AvailableFreeSpace / (1024d * 1024 * 1024);
            return $"{freeGb:F1} GB free on {drive.Name}";
        }
        catch (Exception ex)
        {
            return $"Could not read free disk space: {ex.Message}";
        }
    }

    private static string ReadModelStatus()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "manifest.json");
        manifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(AppContext.BaseDirectory, "models", "manifest.json");
        }

        if (!File.Exists(manifestPath))
        {
            return "Transcription model: not found (offline model must be installed with the app).";
        }

        try
        {
            var text = File.ReadAllText(manifestPath);
            return text.Contains("\"models\": []", StringComparison.Ordinal)
                ? "Transcription model: not bundled yet."
                : "Transcription model: manifest present.";
        }
        catch (IOException)
        {
            return "Transcription model: could not read manifest.";
        }
    }
}
