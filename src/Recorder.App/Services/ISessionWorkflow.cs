using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.App.Services;

public sealed record SessionStartRequest(
    string ParticipantId,
    string? StudyCode,
    string? MeetingEndpointId,
    string? MicrophoneEndpointId,
    bool ConsentConfirmed);

public sealed record SessionListItem(
    string SessionCode,
    string SessionDirectory,
    SessionState State,
    DateTimeOffset RecordedAt,
    TimeSpan? Duration);

public sealed record WorkflowStatus(
    string Message,
    double ProgressFraction,
    bool IsBusy,
    bool CanCancel,
    bool CanRetry,
    string? ErrorDetail);

public interface ISessionWorkflow
{
    event EventHandler? Changed;

    SessionListItem? ActiveSession { get; }

    WorkflowStatus Status { get; }

    bool IsRecording { get; }

    TimeSpan RecordingElapsed { get; }

    event EventHandler<LevelSample>? LevelsAvailable;

    IReadOnlyList<SessionListItem> ListSessions(string dataRoot);

    Task<SessionListItem> StartAsync(SessionStartRequest request, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task TranscribeAsync(CancellationToken cancellationToken);

    Task ExportAsync(string sessionDirectory, CancellationToken cancellationToken);

    Task RecoverAsync(string sessionDirectory, CancellationToken cancellationToken);

    Task CancelCurrentOperationAsync(CancellationToken cancellationToken);

    Task RetryCurrentOperationAsync(CancellationToken cancellationToken);
}
