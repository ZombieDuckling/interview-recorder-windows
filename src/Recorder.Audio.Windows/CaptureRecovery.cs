using Recorder.Core.Abstractions;

namespace Recorder.Audio.Windows;

public sealed class CaptureRecovery : ICaptureRecovery
{
    public Task<CaptureResult?> RecoverAsync(string workingDirectory, CancellationToken cancellationToken) =>
        Task.FromException<CaptureResult?>(new NotImplementedException());
}
