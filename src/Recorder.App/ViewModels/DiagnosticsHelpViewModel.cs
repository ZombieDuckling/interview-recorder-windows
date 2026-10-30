using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Recorder.App.Services;

namespace Recorder.App.ViewModels;

public partial class HelpTopicViewModel : ObservableObject
{
    public HelpTopicViewModel(string title, string body)
    {
        Title = title;
        Body = body;
    }

    public string Title { get; }

    public string Body { get; }
}

public partial class DiagnosticsHelpViewModel : ObservableObject
{
    private readonly AppServices _services;

    public DiagnosticsHelpViewModel(AppServices services)
    {
        _services = services;
        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        OsVersion = Environment.OSVersion.VersionString;
        Arch = Environment.Is64BitProcess ? "x64" : "x86";
        ModelText = ReadModelLine();
        BuildHelpTopics();
    }

    public IList<HelpTopicViewModel> Topics { get; } = new List<HelpTopicViewModel>();

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _osVersion = string.Empty;

    [ObservableProperty]
    private string _arch = string.Empty;

    [ObservableProperty]
    private string _modelText = string.Empty;

    [ObservableProperty]
    private HelpTopicViewModel? _selectedTopic;

    [RelayCommand]
    private void OpenLogFolder()
    {
        var logRoot = Path.Combine(_services.Settings.Current.DataRoot, "logs");
        Directory.CreateDirectory(logRoot);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = logRoot,
            UseShellExecute = true,
        });
    }

    private static string ReadModelLine()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models", "manifest.json");
        return File.Exists(path) ? "Model manifest found next to the app." : "Model manifest not found.";
    }

    private void BuildHelpTopics()
    {
        Topics.Add(new HelpTopicViewModel(
            "Full disk",
            "If free space drops below about 2 GB, stop the interview and free space on the data drive. Recordings need continuous space while chunks are written."));
        Topics.Add(new HelpTopicViewModel(
            "Microphone permission denied",
            "Open Windows Settings → Privacy → Microphone and allow desktop apps to use the microphone. Restart Interview Recorder after changing permission."));
        Topics.Add(new HelpTopicViewModel(
            "Unsupported capture API",
            "This PC may not support the required WASAPI mode. Check Windows version and audio drivers, or use a wired USB headset."));
        Topics.Add(new HelpTopicViewModel(
            "Missing transcription model",
            "Install the offline model pack with the app installer. Without it, transcription cannot start after recording."));
        Topics.Add(new HelpTopicViewModel(
            "Encoding failure",
            "Final MP4 creation failed. Open the session folder, keep capture chunks safe, and use Retry from the progress screen or contact support with processing.log codes only."));
        Topics.Add(new HelpTopicViewModel(
            "Corrupted session",
            "If session.json or hashes do not match, the app reports tampering or corruption. Do not delete capture chunks until recovery is attempted."));
        SelectedTopic = Topics.FirstOrDefault();
    }
}
