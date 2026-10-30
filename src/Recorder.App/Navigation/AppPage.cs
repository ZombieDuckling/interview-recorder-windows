namespace Recorder.App.Navigation;

public enum AppPage
{
    Welcome,
    Devices,
    NewInterview,
    SessionLibrary,
    PrivacySettings,
    DiagnosticsHelp,
    RecordingConsole,
    ProcessingProgress,
    TranscriptEditor,
}

public sealed record NavItem(AppPage Page, string Title, string AutomationId);
