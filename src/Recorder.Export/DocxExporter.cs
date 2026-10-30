using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Recorder.Core.Abstractions;

using Recorder.Core.Formats;

namespace Recorder.Export;

public sealed class DocxExporter : IDocxExporter
{
    public const string DisclaimerText = TranscriptDisclaimer.Text;

    public Task ExportAsync(DocxExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (request.Segments.Count == 0 && request.Duration <= TimeSpan.Zero)
        {
            throw new ArgumentException("Export requires at least one segment or a positive duration.", nameof(request));
        }

        return AtomicFileWriter.WriteAsync(
            request.OutputPath,
            tempPath => WriteDocumentAsync(tempPath, request, cancellationToken),
            cancellationToken);
    }

    private static Task WriteDocumentAsync(string path, DocxExportRequest request, CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        AddDefaultStyles(mainPart);
        using var writer = OpenXmlWriter.Create(mainPart);

                writer.WriteStartElement(new Document());
                writer.WriteStartElement(new Body());

                WriteTitle(writer);
                WriteMetadataTable(writer, request);
                WriteDisclaimer(writer);
                WriteSegments(writer, request.Segments, cancellationToken);
        WriteReviewFooter(writer, request);

        writer.WriteStartElement(new SectionProperties());
        writer.WriteElement(new PageSize { Width = 11906U, Height = 16838U });
        writer.WriteElement(new PageMargin
        {
            Top = 1440,
            Right = 1440,
            Bottom = 1440,
            Left = 1440,
            Header = 720,
            Footer = 720,
            Gutter = 0,
        });
        writer.WriteEndElement();

        writer.WriteEndElement(); // Body
                writer.WriteEndElement(); // Document
                writer.Close();

                mainPart.Document?.Save();
            },
            cancellationToken);
    }

    private static void AddDefaultStyles(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(
                    new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                        new FontSize { Val = "22" }))),
            new Style(
                new StyleName { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(),
                new StyleRunProperties(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                    new FontSize { Val = "22" }))
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true,
            },
            new Style(
                new StyleName { Val = "Title" },
                new BasedOn { Val = "Normal" },
                new NextParagraphStyle { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(),
                new StyleRunProperties(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                    new Bold(),
                    new FontSize { Val = "32" }))
            {
                Type = StyleValues.Paragraph,
                StyleId = "Title",
            },
            new Style(
                new StyleName { Val = "Table Grid" },
                new BasedOn { Val = "Normal" },
                new StyleParagraphProperties(),
                new StyleRunProperties(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                    new FontSize { Val = "22" }))
            {
                Type = StyleValues.Table,
                StyleId = "TableGrid",
            });
        stylesPart.Styles.Save();
    }

    private static void WriteTitle(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Paragraph());
        writer.WriteStartElement(new ParagraphProperties());
        writer.WriteElement(new ParagraphStyleId { Val = "Title" });
        writer.WriteEndElement();
        writer.WriteStartElement(new Run());
        writer.WriteStartElement(new RunProperties());
        writer.WriteElement(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
        writer.WriteElement(new FontSize { Val = "22" });
        writer.WriteEndElement();
        writer.WriteElement(new Text("Interview transcript") { Space = SpaceProcessingModeValues.Preserve });
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteMetadataTable(OpenXmlWriter writer, DocxExportRequest request)
    {
        var recordedLocal = request.RecordedAt.ToLocalTime();
        var exportedLocal = request.ExportedAt.ToLocalTime();
        var study = string.IsNullOrWhiteSpace(request.StudyCode) ? "-" : request.StudyCode.Trim();
        var transcription = $"whisper.cpp {request.EngineVersion}, model {request.ModelName}";

        var rows = new (string Label, string Value)[]
        {
            ("Session code", request.SessionCode),
            ("Date", recordedLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Start time", recordedLocal.ToString("HH:mm", CultureInfo.InvariantCulture)),
            ("Duration", FormatDuration(request.Duration)),
            ("Study code", study),
            ("Transcription", transcription),
            ("Exported", exportedLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
        };

        writer.WriteStartElement(new Table());
        writer.WriteStartElement(new TableProperties());
        writer.WriteElement(new TableStyle { Val = "TableGrid" });
        writer.WriteElement(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" });
        writer.WriteEndElement();

        writer.WriteElement(new TableGrid(new GridColumn(), new GridColumn()));

        foreach (var (label, value) in rows)
        {
            writer.WriteStartElement(new TableRow());

            WriteTableCell(writer, label, bold: true);
            WriteTableCell(writer, value, bold: false);

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteTableCell(OpenXmlWriter writer, string text, bool bold)
    {
        writer.WriteStartElement(new TableCell());
        writer.WriteStartElement(new Paragraph());
        writer.WriteStartElement(new Run());
        if (bold)
        {
            writer.WriteStartElement(new RunProperties());
            writer.WriteElement(new Bold());
            writer.WriteEndElement();
        }

        writer.WriteElement(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteDisclaimer(OpenXmlWriter writer)
    {
        writer.WriteStartElement(new Paragraph());
        writer.WriteStartElement(new ParagraphProperties());
        writer.WriteElement(new ParagraphStyleId { Val = "Normal" });
        writer.WriteEndElement();
        writer.WriteStartElement(new Run());
        writer.WriteStartElement(new RunProperties());
        writer.WriteElement(new Italic());
        writer.WriteEndElement();
        writer.WriteElement(new Text(DisclaimerText) { Space = SpaceProcessingModeValues.Preserve });
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteSegments(
        OpenXmlWriter writer,
        IReadOnlyList<DocxSegment> segments,
        CancellationToken cancellationToken)
    {
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timestamp = FormatTimestamp(segment.Start);
            var speaker = FormatSpeakerLabel(segment.Speaker, segment.SpeakerVerified);

            writer.WriteStartElement(new Paragraph());
            writer.WriteStartElement(new ParagraphProperties());
            writer.WriteElement(new ParagraphStyleId { Val = "Normal" });
            writer.WriteEndElement();

            WriteBoldRun(writer, $"[{timestamp}] ");
            WriteBoldRun(writer, speaker);
            WritePlainRun(writer, $": {segment.Text}");

            writer.WriteEndElement();
        }
    }

    private static void WriteReviewFooter(OpenXmlWriter writer, DocxExportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewerInitials) || request.ReviewedAt is null)
        {
            return;
        }

        var reviewedLocal = request.ReviewedAt.Value.ToLocalTime();
        var line = $"Reviewed by {request.ReviewerInitials.Trim()} on {reviewedLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.";

        writer.WriteStartElement(new Paragraph());
        writer.WriteStartElement(new ParagraphProperties());
        writer.WriteElement(new ParagraphStyleId { Val = "Normal" });
        writer.WriteEndElement();
        writer.WriteStartElement(new Run());
        writer.WriteElement(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteBoldRun(OpenXmlWriter writer, string text)
    {
        writer.WriteStartElement(new Run());
        writer.WriteStartElement(new RunProperties());
        writer.WriteElement(new Bold());
        writer.WriteEndElement();
        writer.WriteElement(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        writer.WriteEndElement();
    }

    private static void WritePlainRun(OpenXmlWriter writer, string text)
    {
        writer.WriteStartElement(new Run());
        writer.WriteElement(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        writer.WriteEndElement();
    }

    internal static string FormatTimestamp(TimeSpan start)
    {
        var totalSeconds = (long)Math.Floor(start.TotalSeconds);
        if (totalSeconds < 0)
        {
            totalSeconds = 0;
        }

        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalSeconds = (long)Math.Floor(duration.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }

    internal static string FormatSpeakerLabel(string speaker, bool verified)
    {
        var label = string.IsNullOrWhiteSpace(speaker) ? "Unknown" : speaker.Trim();
        return verified ? label : $"{label} (unverified)";
    }
}
