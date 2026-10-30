using Recorder.Core.Abstractions;

namespace Recorder.App.Services;

/// <summary>
/// Application service locator until win-lead wires <c>Composition/</c> and <c>SessionPipeline</c>.
/// </summary>
public sealed class AppServices
{
    public AppServices(
        IUserSettingsStore settings,
        ISessionWorkflow workflow,
        IAudioCapture audioCapture,
        IRetentionService retention,
        IFileProtector fileProtector)
    {
        Settings = settings;
        Workflow = workflow;
        AudioCapture = audioCapture;
        Retention = retention;
        FileProtector = fileProtector;
    }

    public IUserSettingsStore Settings { get; }

    public ISessionWorkflow Workflow { get; }

    public IAudioCapture AudioCapture { get; }

    public IRetentionService Retention { get; }

    public IFileProtector FileProtector { get; }

    public static AppServices Instance { get; private set; } = null!;

    public static void Initialize(AppServices services) => Instance = services;
}
