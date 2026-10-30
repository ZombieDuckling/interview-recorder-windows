using Recorder.Core.Abstractions;

namespace Recorder.Media;

/// <summary>Streams capture chunks through the bundled FFmpeg executable.</summary>
public sealed class FfmpegMediaFinalizer : IMediaFinalizer
{
    public FfmpegMediaFinalizer(string? executablePath = null) { }

    public Task<FinalizeResult> FinalizeAsync(CaptureResult capture, string outputMp4Path, IProgress<double>? progress, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    public Task<string> ConvertForTranscriptionAsync(StreamFiles stream, string outputWavPath, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
