// CONTRACT (win-lead owns this folder). Implemented by Recorder.Audio.Windows (win-audio).
using Recorder.Core.Formats;

namespace Recorder.Core.Abstractions;

public enum AudioDeviceKind
{
    Render,
    Capture,
}

public sealed record AudioDeviceInfo(string Id, string FriendlyName, AudioDeviceKind Kind, bool IsDefault, int SampleRate, int Channels);

/// <param name="WorkingDirectory">Session working folder; the capture writes one sub-folder per stream (chunked, crash-resilient).</param>
/// <param name="MeetingEndpointId">Render endpoint to loop back; null = default render endpoint.</param>
/// <param name="MicrophoneEndpointId">Capture endpoint; null = default communications/console capture endpoint.</param>
/// <param name="PreferProcessLoopback">Try process loopback on <paramref name="BrowserProcessId"/> where the OS supports it.</param>
public sealed record CaptureRequest(
    string WorkingDirectory,
    string? MeetingEndpointId,
    string? MicrophoneEndpointId,
    bool PreferProcessLoopback = false,
    int? BrowserProcessId = null);

/// <summary>Peak/RMS in dBFS per stream, raised about 20 times a second for the level meters.</summary>
public sealed record LevelSample(StreamRole Role, float PeakDbfs, float RmsDbfs);

public enum CaptureFaultKind
{
    DeviceRemoved,
    DefaultDeviceChanged,
    FormatChanged,
    DiskLow,
    DiskFull,
    WriteFailed,
    AccessDenied,
    Unsupported,
    Other,
}

/// <summary>A capture problem. <paramref name="Recovered"/> = capture continues (e.g. re-attached to the new default device).</summary>
public sealed record CaptureFault(StreamRole Role, CaptureFaultKind Kind, bool Recovered, int DiagnosticCode);

/// <summary>One stream's files on disk after stop or recovery: ordered PCM chunks (WAV) at <paramref name="SampleRate"/>.</summary>
public sealed record StreamFiles(
    StreamRole Role,
    string Directory,
    IReadOnlyList<string> ChunkPaths,
    int SampleRate,
    int Channels,
    TimeSpan Duration,
    TimeSpan StartOffset,
    bool IsProcessLoopback);

public sealed record CaptureResult(IReadOnlyList<StreamFiles> Streams, TimeSpan Duration, bool Complete);

public interface IAudioCapture
{
    IReadOnlyList<AudioDeviceInfo> ListDevices(AudioDeviceKind kind);

    /// <summary>True when this OS build supports process-loopback capture.</summary>
    bool IsProcessLoopbackSupported { get; }

    Task<ICaptureSession> StartAsync(CaptureRequest request, CancellationToken cancellationToken);
}

public interface ICaptureSession : IAsyncDisposable
{
    event EventHandler<LevelSample>? LevelsAvailable;

    event EventHandler<CaptureFault>? FaultRaised;

    TimeSpan Elapsed { get; }

    Task<CaptureResult> StopAsync(CancellationToken cancellationToken);
}

/// <summary>Rebuilds what was captured from an unfinished working folder (crash, power loss).</summary>
public interface ICaptureRecovery
{
    Task<CaptureResult?> RecoverAsync(string workingDirectory, CancellationToken cancellationToken);
}
