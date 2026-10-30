using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;
using Whisper.net;

namespace Recorder.Transcription;

/// <summary>Raised when the speech model file is missing or does not match models/manifest.json.</summary>
public sealed class ModelUnavailableException(string message) : Exception(message);

/// <summary>whisper.cpp (Whisper.net) over 16 kHz WAV files, in bounded overlapping windows read from disk.</summary>
public sealed partial class WhisperTranscriber : ITranscriber
{
    internal const double WindowSeconds = 30;
    internal const double OverlapSeconds = 2;
    private const int Attempts = 3;
    private const float SilenceRms = 1e-4f;

    public async Task<Transcript> TranscribeAsync(TranscriptionRequest request, IProgress<TranscriptionProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ModelEntry model = await VerifyModelAsync(request.ModelPath, cancellationToken).ConfigureAwait(false);

        var readers = new List<(TranscriptionInput Input, PcmWindowReader Reader)>();
        try
        {
            foreach (TranscriptionInput input in request.Inputs) readers.Add((input, new PcmWindowReader(input.Wav16kPath)));
            double totalSeconds = readers.Sum(r => (double)r.Reader.FrameCount / PcmWindowReader.SampleRate);
            double doneSeconds = 0;
            var stitcher = new SegmentStitcher();
            int threads = Math.Clamp(request.MaxThreads, 1, Environment.ProcessorCount);
            using var factory = WhisperFactory.FromPath(request.ModelPath);
            foreach ((TranscriptionInput input, PcmWindowReader reader) in readers)
            {
                await using var processor = Build(factory, request, threads);
                int window = (int)(WindowSeconds * PcmWindowReader.SampleRate);
                int step = (int)((WindowSeconds - OverlapSeconds) * PcmWindowReader.SampleRate);
                var buffer = new float[window];
                // Transcribe only where this stream has speech: whisper snaps timestamps to the window start and stretches
                // segments across silence, which would make the mic and meeting streams appear to overlap (label Unknown).
                IReadOnlyList<(long Start, long End)> regions = SpeechRegions.Find(reader, cancellationToken);
                long scanned = 0;
                foreach ((long regionStart, long regionEnd) in regions)
                {
                    for (long start = regionStart; start < regionEnd; start += step)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int want = (int)Math.Min(window, regionEnd - start);
                        int count = reader.Read(start, buffer.AsSpan(0, want));
                        if (count > 0 && Rms(buffer.AsSpan(0, count)) >= SilenceRms)
                        {
                            List<WindowSegment> found = await TranscribeWindowAsync(processor, buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                            stitcher.AddWindow(input.Role, input.StartOffset.TotalSeconds, (double)start / PcmWindowReader.SampleRate, (double)count / PcmWindowReader.SampleRate, found);
                        }

                        if (start + want >= regionEnd) break;
                    }

                    doneSeconds += (double)(regionEnd - scanned) / PcmWindowReader.SampleRate;
                    scanned = regionEnd;
                    progress?.Report(new TranscriptionProgress(Math.Clamp(doneSeconds / totalSeconds, 0, 1), TimeSpan.FromSeconds(doneSeconds), TimeSpan.FromSeconds(totalSeconds)));
                }

                doneSeconds += (double)(reader.FrameCount - scanned) / PcmWindowReader.SampleRate;
                progress?.Report(new TranscriptionProgress(Math.Clamp(doneSeconds / totalSeconds, 0, 1), TimeSpan.FromSeconds(doneSeconds), TimeSpan.FromSeconds(totalSeconds)));
            }
            // Map to the frozen transcript.json model (spec section 5). SessionId/SessionCode are filled by the caller.
            var segments = stitcher.Build()
                .Select((s, i) => new TranscriptSegment
                {
                    Id = i + 1,
                    Start = Math.Round(s.Start.TotalSeconds, 3),
                    End = Math.Round(s.End.TotalSeconds, 3),
                    Speaker = s.Speaker.ToString(),
                    SpeakerVerified = s.SpeakerVerified,
                    Source = s.Role == StreamRole.Microphone ? TranscriptSegmentSource.Mic : TranscriptSegmentSource.Meeting,
                    Text = s.Text,
                    OriginalText = s.Text,
                })
                .ToList();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return new Transcript
            {
                Language = request.Language ?? "auto",
                Engine = new TranscriptEngineInfo { Version = EngineVersion(), Model = model.Name, ModelSha256 = model.Sha256 },
                CreatedAt = now,
                UpdatedAt = now,
                Segments = segments,
            };
        }
        finally
        {
            foreach ((_, PcmWindowReader reader) in readers) reader.Dispose();
        }
    }

    private static WhisperProcessor Build(WhisperFactory factory, TranscriptionRequest request, int threads)
    {
        WhisperProcessorBuilder builder = factory.CreateBuilder().WithThreads(threads).WithLanguage(request.Language ?? "auto");
        builder = request.Preset == TranscriptionPreset.Fast
            ? builder.WithGreedySamplingStrategy(g => g.WithBestOf(1))
            : builder.WithBeamSearchSamplingStrategy(b => b.WithBeamSize(5));
        return builder.Build();
    }

    private static async Task<List<WindowSegment>> TranscribeWindowAsync(WhisperProcessor processor, ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var found = new List<WindowSegment>();
                await foreach (SegmentData segment in processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
                {
                    // whisper emits non-speech markers such as [BLANK_AUDIO], (silence), [MUSIC]: never put them in a transcript
                    string text = segment.Text.Trim();
                    if (text.Length == 0 || NonSpeechMarker().IsMatch(text)) continue;
                    found.Add(new WindowSegment(segment.Start.TotalSeconds, segment.End.TotalSeconds, text));
                }
                return found;
            }
            catch (Exception) when (attempt < Attempts && !cancellationToken.IsCancellationRequested)
            {
                // Retry the same window; the processor keeps no state that survives a failed run.
            }
        }
    }

    private static float Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (float sample in samples) sum += (double)sample * sample;
        return samples.Length == 0 ? 0 : (float)Math.Sqrt(sum / samples.Length);
    }

    internal sealed record ModelEntry(string Name, string Sha256);

    internal static async Task<ModelEntry> VerifyModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(modelPath))
            throw new ModelUnavailableException("The speech model file is missing. Copy the models folder from the installer next to the application, or reinstall.");
        string manifestPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(modelPath))!, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new ModelUnavailableException("The speech model list (manifest.json) is missing next to the model. Reinstall the application.");
        string file = Path.GetFileName(modelPath);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        JsonElement? entry = manifest.RootElement.GetProperty("models").EnumerateArray()
            .Where(m => m.GetProperty("file").GetString() == file).Select(m => (JsonElement?)m).FirstOrDefault();
        if (entry is null) throw new ModelUnavailableException("This speech model is not on the approved list. Reinstall the application.");
        string expected = entry.Value.GetProperty("sha256").GetString()!;
        await using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new ModelUnavailableException("The speech model file is damaged or has been changed. Reinstall the application.");
        return new ModelEntry(entry.Value.GetProperty("name").GetString()!, expected.ToLowerInvariant());
    }

    private static string EngineVersion()
    {
        string version = typeof(WhisperFactory).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(WhisperFactory).Assembly.GetName().Version?.ToString() ?? "unknown";
        return "whisper.cpp/Whisper.net " + version.Split('+')[0];
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(\[[^\]]*\]|\([^)]*\)|\*[^*]*\*)(\s*(\[[^\]]*\]|\([^)]*\)|\*[^*]*\*))*$")]
    private static partial System.Text.RegularExpressions.Regex NonSpeechMarker();
}
