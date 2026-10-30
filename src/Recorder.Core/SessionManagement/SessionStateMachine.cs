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
        if (!manifest.Consent.Confirmed)
        {
            throw new SessionTransitionRefusedException(manifest.State, SessionState.Recording, "CONSENT_REQUIRED");
        }
    }

    public static bool IsAllowed(SessionState from, SessionState to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    private static readonly Dictionary<SessionState, HashSet<SessionState>> Allowed =
        new Dictionary<SessionState, HashSet<SessionState>>
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
                SessionState.Failed,
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

    public async Task TransitionToAsync(SessionState next, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
    {
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

        Manifest.State = next;
        Manifest.StateHistory.Add(new SessionStateHistoryEntry { State = next, At = atUtc });
        await SessionManifestIO.WriteAsync(_sessionFilePath, Manifest, cancellationToken).ConfigureAwait(false);
    }
}
