using Recorder.Core.Abstractions;
using Recorder.Core.Formats;
using TranscriptDocument = Recorder.Core.Formats.Transcript;

namespace Recorder.Export.Transcript;

public static class TranscriptMapping
{
    public static IReadOnlyList<DocxSegment> ToDocxSegments(TranscriptDocument document) =>
        document.Segments
            .OrderBy(s => s.Start)
            .Select(s => new DocxSegment(
                TimeSpan.FromSeconds(s.Start),
                s.Speaker,
                s.SpeakerVerified,
                s.Text))
            .ToList();
}
