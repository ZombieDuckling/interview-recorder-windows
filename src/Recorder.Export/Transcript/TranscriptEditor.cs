using System.Text.RegularExpressions;
using Recorder.Core.Formats;
using TranscriptDocument = Recorder.Core.Formats.Transcript;

namespace Recorder.Export.Transcript;

public static partial class TranscriptEditor
{
    private static readonly Regex InitialsPattern = ReviewerInitialsRegex();

    public static void EditSegmentText(TranscriptDocument document, int segmentId, string newText, DateTimeOffset atUtc)
    {
        var segment = GetSegment(document, segmentId);
        var oldLength = segment.Text.Length;
        var trimmed = newText ?? string.Empty;
        if (trimmed == segment.Text)
        {
            return;
        }

        segment.Text = trimmed;
        segment.Edited = trimmed != segment.OriginalText;
        AppendHistory(document, segmentId, TranscriptHistoryField.Text, atUtc, oldLength, trimmed.Length);
    }

    public static void RelabelSpeaker(
        TranscriptDocument document,
        int segmentId,
        string speaker,
        bool speakerVerified,
        DateTimeOffset atUtc)
    {
        ValidateSpeakerLabel(speaker);
        var segment = GetSegment(document, segmentId);
        var oldSpeaker = segment.Speaker;
        var oldVerified = segment.SpeakerVerified;
        var trimmed = speaker.Trim();
        if (trimmed == oldSpeaker && speakerVerified == oldVerified)
        {
            return;
        }

        segment.Speaker = trimmed;
        segment.SpeakerVerified = speakerVerified;
        AppendHistory(document, segmentId, TranscriptHistoryField.Speaker, atUtc, oldSpeaker.Length, trimmed.Length);
        if (speakerVerified != oldVerified)
        {
            AppendHistory(
                document,
                segmentId,
                TranscriptHistoryField.SpeakerVerified,
                atUtc,
                oldVerified ? 1 : 0,
                speakerVerified ? 1 : 0);
        }
    }

    public static void SetReview(TranscriptDocument document, string reviewerInitials, string? note, DateTimeOffset atUtc)
    {
        var initials = reviewerInitials.Trim();
        if (!InitialsPattern.IsMatch(initials))
        {
            throw new ArgumentException("Reviewer initials must be 1-8 letters.", nameof(reviewerInitials));
        }

        if (note is { Length: > 200 })
        {
            throw new ArgumentException("Review note must be at most 200 characters.", nameof(note));
        }

        document.Review = new TranscriptReview
        {
            ReviewerInitials = initials,
            At = atUtc,
            Note = note,
        };
    }

    public static void ClearReview(TranscriptDocument document) => document.Review = null;

    private static TranscriptSegment GetSegment(Transcript document, int segmentId)
    {
        var segment = document.Segments.FirstOrDefault(s => s.Id == segmentId);
        if (segment is null)
        {
            throw new KeyNotFoundException($"Segment {segmentId} was not found.");
        }

        return segment;
    }

    private static void AppendHistory(
        TranscriptDocument document,
        int segmentId,
        TranscriptHistoryField field,
        DateTimeOffset atUtc,
        int oldLength,
        int newLength)
    {
        document.History.Add(new TranscriptHistoryEntry
        {
            SegmentId = segmentId,
            Field = field,
            At = atUtc,
            OldLength = oldLength,
            NewLength = newLength,
        });
    }

    public static void ValidateSpeakerLabel(string speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker))
        {
            throw new ArgumentException("Speaker label is required.", nameof(speaker));
        }

        var trimmed = speaker.Trim();
        if (trimmed.Length is < 1 or > 40)
        {
            throw new ArgumentException("Speaker label must be 1-40 characters.", nameof(speaker));
        }

        if (Enum.TryParse<SpeakerLabel>(trimmed, ignoreCase: false, out _))
        {
            return;
        }

        if (!Regex.IsMatch(trimmed, @"^[\p{L}\p{N}\p{P}\p{S}\s]+$"))
        {
            throw new ArgumentException("Speaker label contains invalid characters.", nameof(speaker));
        }
    }

    [GeneratedRegex("^[A-Za-z]{1,8}$")]
    private static partial Regex ReviewerInitialsRegex();
}
