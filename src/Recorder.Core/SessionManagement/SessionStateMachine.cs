using Recorder.Core.Formats;
using Recorder.Core.IO;

namespace Recorder.Core.SessionManagement;

public sealed class SessionTransitionRefusedException : Exception
{
    public SessionTransitionRefusedException(SessionState? from, SessionState to, string code)
        : base(from is null
            ? $"Refused initial transition to {to} ({code})."
            : $"Refused transition from {from} to {to} ({code}).")
    {
        From = from;
        To = to;
        Code = code;
    }

    public SessionState? From { get; }

    public SessionState To { get; }

    public string Code { get; }
}

public static class SessionStateRules
{
    public static void EnsureConsentBeforeRecording(SessionManifest manifest)
    {
        if (!manifest.Consent.Confirmed || manifest.Consent.ConfirmedAt == default)
        {
            throw new SessionTransitionRefusedException(manifest.State, SessionState.Recording, "CONSENT_REQUIRED");
        }
    }

    public static bool IsTransitionAllowed(SessionState? from, SessionState to)
    {
        if (from is null)
        {
            return to == SessionState.Recording;
        }

        return Allowed.TryGetValue(from.Value, out var targets) && targets.Contains(to);
    }

    public static bool IsAllowed(SessionState from, SessionState to) => IsTransitionAllowed(from, to);

    internal static IReadOnlyDictionary<SessionState, HashSet<SessionState>> TransitionTable => Allowed;

    private static readonly Dictionary<SessionState, HashSet<SessionState>> Allowed =
        new()
        {
            [SessionState.Recording] =
            [
                SessionState.Finalizing,
                SessionState.Recovered,
                SessionState.Failed,
            ],
            [SessionState.Finalizing] =
            [
                SessionState.ReadyToTranscribe,
                SessionState.Recovered,
                SessionState.Failed,
            ],
            [SessionState.ReadyToTranscribe] =
            [
                SessionState.Transcribing,
                SessionState.Failed,
            ],
            [SessionState.Transcribing] =
            [
                SessionState.Exported,
                SessionState.ReadyToTranscribe,
                SessionState.Recovered,
                SessionState.Failed,
            ],
            [SessionState.Exported] =
            [
                SessionState.Exported,
                SessionState.Transcribing,
            ],
            [SessionState.Recovered] =
            [
                SessionState.Finalizing,
                SessionState.ReadyToTranscribe,
                SessionState.Failed,
            ],
            [SessionState.Failed] =
            [
                SessionState.Finalizing,
                SessionState.ReadyToTranscribe,
                SessionState.Failed,
            ],
        };
}

public sealed class SessionStateMachine
{
    private readonly string _sessionFilePath;

    public SessionStateMachine(SessionManifest manifest, string sessionFilePath)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        _sessionFilePath = sessionFilePath ?? throw new ArgumentNullException(nameof(sessionFilePath));
    }

    public SessionManifest Manifest { get; }

    public Task TransitionToAsync(SessionState next, DateTimeOffset atUtc, CancellationToken cancellationToken = default) =>
        TransitionToAsync(next, atUtc, failure: null, cancellationToken);

    public async Task TransitionToAsync(
        SessionState next,
        DateTimeOffset atUtc,
        SessionErrorInfo? failure,
        CancellationToken cancellationToken = default)
    {
        if (next == SessionState.Failed && failure is null)
        {
            var from = Manifest.StateHistory.Count == 0 ? (SessionState?)null : Manifest.State;
            throw new SessionTransitionRefusedException(from, next, "ERROR_REQUIRED");
        }

        if (Manifest.StateHistory.Count == 0)
        {
            if (next != SessionState.Recording)
            {
                throw new SessionTransitionRefusedException(null, next, "STATE_REFUSED");
            }

            SessionStateRules.EnsureConsentBeforeRecording(Manifest);
        }
        else
        {
            var from = Manifest.State;
            if (!SessionStateRules.IsAllowed(from, next))
            {
                throw new SessionTransitionRefusedException(from, next, "STATE_REFUSED");
            }

            if (next == SessionState.Recording)
            {
                SessionStateRules.EnsureConsentBeforeRecording(Manifest);
            }
        }

        var previousState = Manifest.State;
        var previousError = Manifest.Error;
        var historyCountBefore = Manifest.StateHistory.Count;

        Manifest.State = next;
        Manifest.StateHistory.Add(new SessionStateHistoryEntry { State = next, At = atUtc });
        if (next == SessionState.Failed)
        {
            Manifest.Error = failure;
        }

        try
        {
            await SessionManifestIO.WriteAsync(_sessionFilePath, Manifest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Manifest.State = previousState;
            Manifest.Error = previousError;
            if (Manifest.StateHistory.Count > historyCountBefore)
            {
                Manifest.StateHistory.RemoveAt(Manifest.StateHistory.Count - 1);
            }

            throw;
        }
    }
}
