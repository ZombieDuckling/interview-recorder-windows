#if DEBUG
using Recorder.App.Services;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.App.DesignTime;

/// <summary>
/// Layout and Blend preview only. Never registered in Release startup.
/// </summary>
public sealed class FakeSessionWorkflow : ISessionWorkflow, IDisposable
{
    private readonly List<SessionListItem> _sessions =
    [
        new("IR-20261031-091500-7F3A", @"C:\Fake\IR-20261031-091500-7F3A", SessionState.Exported, DateTimeOffset.Now.AddDays(-1), TimeSpan.FromMinutes(42)),
        new("IR-20261030-141200-A1B2", @"C:\Fake\IR-20261030-141200-A1B2", SessionState.ReadyToTranscribe, DateTimeOffset.Now.AddDays(-2), TimeSpan.FromMinutes(55)),
    ];

    private SessionListItem? _active;
    private WorkflowStatus _status = new("Ready.", 0, false, false, false, null);
    private bool _isRecording;
    private TimeSpan _elapsed;
    private Timer? _timer;

    public event EventHandler? Changed;

    public SessionListItem? ActiveSession => _active;

    public WorkflowStatus Status => _status;

    public bool IsRecording => _isRecording;

    public TimeSpan RecordingElapsed => _elapsed;

    public event EventHandler<LevelSample>? LevelsAvailable;

    public IReadOnlyList<SessionListItem> ListSessions(string dataRoot) => _sessions;

    public Task<SessionListItem> StartAsync(SessionStartRequest request, CancellationToken cancellationToken)
    {
        _active = new SessionListItem(
            "IR-20261031-120000-FAKE",
            Path.Combine(UserSettings.DefaultDataRoot(), "IR-20261031-120000-FAKE"),
            SessionState.Recording,
            DateTimeOffset.Now,
            null);
        _isRecording = true;
        _elapsed = TimeSpan.Zero;
        _status = new WorkflowStatus("Recording (preview).", 0, true, false, false, null);
        _timer = new Timer(_ =>
        {
            _elapsed += TimeSpan.FromSeconds(1);
            LevelsAvailable?.Invoke(this, new LevelSample(StreamRole.Microphone, -12f, -24f));
            LevelsAvailable?.Invoke(this, new LevelSample(StreamRole.Meeting, -18f, -30f));
            Changed?.Invoke(this, EventArgs.Empty);
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(_active);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        _isRecording = false;
        if (_active is not null)
        {
            _active = _active with { State = SessionState.Finalizing, Duration = _elapsed };
        }

        _status = new WorkflowStatus("Finalizing recording…", 0.35, true, true, false, null);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task TranscribeAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        _status = new WorkflowStatus("Transcribing offline…", 0.5, true, true, false, null);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task ExportAsync(string sessionDirectory, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RecoverAsync(string sessionDirectory, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SaveTranscriptAsync(string sessionDirectory, Recorder.Core.Formats.Transcript transcript, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CancelCurrentOperationAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        _status = new WorkflowStatus("Cancelled.", 0, false, false, true, null);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task RetryCurrentOperationAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        _status = new WorkflowStatus("Retrying…", 0.1, true, true, false, null);
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
#endif
