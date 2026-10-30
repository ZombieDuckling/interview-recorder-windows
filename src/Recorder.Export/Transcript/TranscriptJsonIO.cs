using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Export.Transcript;

public static class TranscriptJsonIO
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TranscriptFile Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<TranscriptFile>(json, Options)
            ?? throw new InvalidDataException("transcript.json is empty or invalid.");
        return document;
    }

    public static async Task SaveAsync(string path, TranscriptFile document, CancellationToken cancellationToken)
    {
        document.UpdatedAt = FormatUtcNow();
        var json = JsonSerializer.Serialize(document, Options);
        await AtomicFileWriter.WriteAsync(
            path,
            async tempPath =>
            {
                await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static TranscriptFile FromCoreTranscript(
        string sessionId,
        string sessionCode,
        string language,
        Core.Formats.Transcript transcript)
    {
        var now = FormatUtcNow();
        var segments = new List<TranscriptSegmentRecord>();
        foreach (var segment in transcript.Segments)
        {
            segments.Add(new TranscriptSegmentRecord
            {
                Id = segment.Index,
                Start = RoundSeconds(segment.Start),
                End = RoundSeconds(segment.End),
                Speaker = SpeakerLabelMapping.ToJson(segment.Speaker),
                SpeakerVerified = segment.SpeakerVerified,
                Source = segment.Speaker switch
                {
                    SpeakerLabel.Researcher => "mic",
                    SpeakerLabel.Participant => "meeting",
                    _ => "mixed",
                },
                Text = segment.Text,
                OriginalText = segment.Text,
                Edited = false,
                Confidence = null,
            });
        }

        return new TranscriptFile
        {
            SessionId = sessionId,
            SessionCode = sessionCode,
            Language = language,
            Engine = new TranscriptEngine
            {
                Version = transcript.EngineVersion,
                Model = transcript.ModelName,
                ModelSha256 = transcript.ModelSha256,
            },
            CreatedAt = now,
            UpdatedAt = now,
            DisclaimerText = TranscriptFile.Disclaimer,
            Segments = segments,
            History = [],
            Review = null,
        };
    }

    public static IReadOnlyList<DocxSegment> ToDocxSegments(TranscriptFile document)
    {
        return document.Segments
            .OrderBy(s => s.Start)
            .Select(s => new DocxSegment(
                TimeSpan.FromSeconds(s.Start),
                s.Speaker,
                s.SpeakerVerified,
                s.Text))
            .ToList();
    }

    public static string FormatUtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static double RoundSeconds(TimeSpan time) =>
        Math.Round(time.TotalSeconds, 3, MidpointRounding.AwayFromZero);
}

internal static class SpeakerLabelMapping
{
    public static string ToJson(SpeakerLabel label) => label switch
    {
        SpeakerLabel.Researcher => "Researcher",
        SpeakerLabel.Participant => "Participant",
        SpeakerLabel.Unknown => "Unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(label)),
    };
}
