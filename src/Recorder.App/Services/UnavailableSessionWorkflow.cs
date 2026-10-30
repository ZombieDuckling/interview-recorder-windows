using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.App.Services;

/// <summary>
/// Placeholder until win-lead registers <c>SessionPipeline</c> in Release startup.
/// </summary>
internal sealed class UnavailableSessionWorkflow : ISessionWorkflow
{
    private static readonly WorkflowStatus Blocked = new(
        "Recording and processing are not wired yet. win-lead must connect SessionPipeline.",
        0,
        IsBusy: false,
        CanCancel: false,
        CanRetry: false,
        ErrorDetail: null);

#pragma warning disable CS0067
    public event EventHandler? Changed;
#pragma warning restore CS0067

    public SessionListItem? ActiveSession => null;

    public WorkflowStatus Status => Blocked;

    public bool IsRecording => false;

    public TimeSpan RecordingElapsed => TimeSpan.Zero;

#pragma warning disable CS0067
    public event EventHandler<LevelSample>? LevelsAvailable;
#pragma warning restore CS0067

    public IReadOnlyList<SessionListItem> ListSessions(string dataRoot) =>
        SessionCatalog.ReadSessions(dataRoot);

    public Task<SessionListItem> StartAsync(SessionStartRequest request, CancellationToken cancellationToken) =>
        Task.FromException<SessionListItem>(new InvalidOperationException(Status.Message));

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(Status.Message));

    public Task TranscribeAsync(CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(Status.Message));

    public Task ExportAsync(string sessionDirectory, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(Status.Message));

    public Task RecoverAsync(string sessionDirectory, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(Status.Message));

    public Task CancelCurrentOperationAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RetryCurrentOperationAsync(CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException(Status.Message));
}
