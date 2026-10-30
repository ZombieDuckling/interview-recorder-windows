using System.IO;
using Recorder.Core.Abstractions;

namespace Recorder.App.Services.Stubs;

internal sealed class UnavailableAudioCapture : IAudioCapture
{
    public bool IsProcessLoopbackSupported => false;

    public IReadOnlyList<AudioDeviceInfo> ListDevices(AudioDeviceKind kind) => Array.Empty<AudioDeviceInfo>();

    public Task<ICaptureSession> StartAsync(CaptureRequest request, CancellationToken cancellationToken) =>
        Task.FromException<ICaptureSession>(new InvalidOperationException("Audio capture is not available on this build."));
}

internal sealed class UnavailableRetentionService : IRetentionService
{
    public IReadOnlyList<RetentionCandidate> FindExpired(string dataDirectory, int retentionDays, DateTimeOffset now) =>
        Array.Empty<RetentionCandidate>();

    public Task DeleteSessionAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        if (Directory.Exists(sessionDirectory))
        {
            Directory.Delete(sessionDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }
}

internal sealed class UnavailableFileProtector : IFileProtector
{
    public Task EnsureSessionKeyAsync(string sessionDirectory, CancellationToken cancellationToken) => Task.CompletedTask;

    public System.IO.Stream OpenEncryptedWrite(string sessionDirectory, string path) =>
        throw new InvalidOperationException("File protection is not available on this build.");

    public System.IO.Stream OpenDecryptedRead(string sessionDirectory, string path) =>
        throw new InvalidOperationException("File protection is not available on this build.");

    public Task<string> ComputeMacAsync(string sessionDirectory, string path, CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);

    public void RestrictToCurrentUser(string directory)
    {
    }
}
