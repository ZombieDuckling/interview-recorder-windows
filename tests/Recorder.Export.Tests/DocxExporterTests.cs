using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Recorder.Core.Abstractions;
using Recorder.Export.Transcript;

namespace Recorder.Export.Tests;

public sealed class DocxExporterTests
{
    [Fact]
    public async Task ExportAsync_produces_valid_docx_with_expected_content()
    {
        var dir = CreateTempDir();
        var output = Path.Combine(dir, "interview_transcript.docx");
        var recorded = new DateTimeOffset(2026, 10, 31, 9, 15, 0, TimeSpan.FromHours(2));
        var request = new DocxExportRequest(
            SessionCode: "IR-20261031-091500-7F3A",
            RecordedAt: recorded,
            Duration: TimeSpan.FromMinutes(6.1),
            StudyCode: "STUDY-01",
            EngineVersion: "1.8.2",
            ModelName: "ggml-base-q5_1",
            Segments:
            [
                new DocxSegment(TimeSpan.FromSeconds(0.48), "Researcher", true, "Thank you for joining."),
                new DocxSegment(TimeSpan.FromSeconds(4.9), "Participant", false, "Yes, I can."),
            ],
            OutputPath: output,
            ExportedAt: new DateTimeOffset(2026, 10, 31, 10, 40, 12, TimeSpan.FromHours(2)),
            ReviewerInitials: "JS",
            ReviewedAt: new DateTimeOffset(2026, 10, 31, 11, 0, 0, TimeSpan.FromHours(2)));

        var exporter = new DocxExporter();
        await exporter.ExportAsync(request, CancellationToken.None);

        Assert.True(File.Exists(output));
        ValidateDocument(output);

        var text = ExtractPlainText(output);
        Assert.Contains("Interview transcript", text, StringComparison.Ordinal);
        Assert.Contains("IR-20261031-091500-7F3A", text, StringComparison.Ordinal);
        Assert.Contains(DocxExporter.DisclaimerText, text, StringComparison.Ordinal);
        Assert.Contains("[00:00:00]", text, StringComparison.Ordinal);
        Assert.Contains("Researcher", text, StringComparison.Ordinal);
        Assert.Contains("Thank you for joining.", text, StringComparison.Ordinal);
        Assert.Contains("[00:00:04]", text, StringComparison.Ordinal);
        Assert.Contains("Participant (unverified)", text, StringComparison.Ordinal);
        Assert.Contains("Yes, I can.", text, StringComparison.Ordinal);
        Assert.Contains("Reviewed by JS on 2026-10-31.", text, StringComparison.Ordinal);

        var thankYouIndex = text.IndexOf("Thank you for joining.", StringComparison.Ordinal);
        var yesIndex = text.IndexOf("Yes, I can.", StringComparison.Ordinal);
        Assert.True(thankYouIndex >= 0 && yesIndex > thankYouIndex);
    }

    [Fact]
    public async Task ExportAsync_large_transcript_stays_within_memory_budget()
    {
        var dir = CreateTempDir();
        var output = Path.Combine(dir, "large.docx");
        var segments = new List<DocxSegment>(capacity: 5000);
        for (var i = 0; i < 5000; i++)
        {
            var start = TimeSpan.FromSeconds(i * 2.16);
            segments.Add(new DocxSegment(start, i % 2 == 0 ? "Researcher" : "Participant", i % 3 == 0, $"Segment {i}."));
        }

        var request = new DocxExportRequest(
            SessionCode: "IR-20261031-091500-7F3A",
            RecordedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.FromHours(3),
            StudyCode: null,
            EngineVersion: "1.8.2",
            ModelName: "ggml-base-q5_1",
            Segments: segments,
            OutputPath: output,
            ExportedAt: DateTimeOffset.UtcNow);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var exporter = new DocxExporter();
        await exporter.ExportAsync(request, CancellationToken.None);

        var after = GC.GetTotalMemory(forceFullCollection: true);
        var deltaMb = (after - before) / (1024.0 * 1024.0);
        Assert.True(deltaMb < 64, $"Export retained {deltaMb:F1} MB beyond baseline.");
        ValidateDocument(output);
    }

    [Fact]
    public void FormatTimestamp_floors_to_whole_seconds()
    {
        Assert.Equal("00:00:00", DocxExporter.FormatTimestamp(TimeSpan.FromMilliseconds(999)));
        Assert.Equal("00:01:05", DocxExporter.FormatTimestamp(TimeSpan.FromSeconds(65.9)));
        Assert.Equal("03:00:00", DocxExporter.FormatTimestamp(TimeSpan.FromHours(3)));
    }

    [Fact]
    public void TranscriptEditor_records_history_without_text()
    {
        var doc = new TranscriptFile
        {
            SessionId = "7f3a9c1e-2b4d-4e6f-8a1b-3c5d7e9f0a2b",
            SessionCode = "IR-20261031-091500-7F3A",
            Segments =
            [
                new TranscriptSegmentRecord
                {
                    Id = 1,
                    Start = 0,
                    End = 1,
                    Speaker = "Researcher",
                    SpeakerVerified = true,
                    Source = "mic",
                    Text = "Hello",
                    OriginalText = "Hello",
                },
            ],
        };

        var at = new DateTimeOffset(2026, 10, 31, 8, 40, 12, 250, TimeSpan.Zero);
        TranscriptEditor.EditSegmentText(doc, 1, "Hello!", at);
        Assert.Single(doc.History);
        Assert.DoesNotContain("Hello", doc.History[0].Field, StringComparison.Ordinal);
        Assert.Equal(5, doc.History[0].OldLength);
        Assert.Equal(6, doc.History[0].NewLength);
    }

    private static void ValidateDocument(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var validator = new OpenXmlValidator();
        var errors = validator.Validate(document).ToList();
        if (errors.Count > 0)
        {
            var details = string.Join("; ", errors.Select(e => $"{e.Description ?? "error"} @ {e.Path?.XPath ?? "?"}"));
            Assert.Fail($"OpenXml validation failed: {details}");
        }
    }

    private static string ExtractPlainText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            body.Descendants<Text>().Select(t => t.Text));
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "recorder-export-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
