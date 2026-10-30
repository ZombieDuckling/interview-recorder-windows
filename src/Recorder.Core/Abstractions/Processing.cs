// CONTRACT (win-lead owns this folder).
using Recorder.Core.Formats;

namespace Recorder.Core.Abstractions;

public sealed record FinalizeResult(string Mp4Path, TimeSpan Duration, string Sha256);

/// <summary>Implemented by Recorder.Media (win-media-asr): FFmpeg subprocess, streaming, bounded memory.</summary>
public interface IMediaFinalizer
{
    /// <summary>Mix the streams into one audio-only AAC-in-MP4 file. The stream chunk files are kept unchanged.</summary>
    Task<FinalizeResult> FinalizeAsync(CaptureResult capture, string outputMp4Path, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Convert one stream to 16 kHz mono 16-bit PCM WAV for transcription.</summary>
    Task<string> ConvertForTranscriptionAsync(StreamFiles stream, string outputWavPath, CancellationToken cancellationToken);
}

public enum TranscriptionPreset
{
    Fast,
    Balanced,
}

public sealed record TranscriptionInput(StreamRole Role, string Wav16kPath, TimeSpan StartOffset);

public sealed record TranscriptionRequest(
    IReadOnlyList<TranscriptionInput> Inputs,
    string ModelPath,
    TranscriptionPreset Preset,
    int MaxThreads,
    string? Language = null);

public sealed record TranscriptionProgress(double Fraction, TimeSpan Processed, TimeSpan Total);

/// <summary>Implemented by Recorder.Transcription (win-media-asr): whisper.cpp, chunked with overlap, offline.</summary>
public interface ITranscriber
{
    Task<Transcript> TranscribeAsync(TranscriptionRequest request, IProgress<TranscriptionProgress>? progress, CancellationToken cancellationToken);
}

public sealed record DocxExportRequest(
    string SessionCode,
    DateTimeOffset RecordedAt,
    Transcript Transcript,
    string OutputPath,
    string? ReviewerName = null,
    DateTimeOffset? ReviewedAt = null);

/// <summary>Implemented by Recorder.Export (win-export): Open XML SDK, streamed, no audio embedded.</summary>
public interface IDocxExporter
{
    Task ExportAsync(DocxExportRequest request, CancellationToken cancellationToken);
}
