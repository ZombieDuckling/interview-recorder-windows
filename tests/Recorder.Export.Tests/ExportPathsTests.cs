using Recorder.Export;

namespace Recorder.Export.Tests;

public sealed class ExportPathsTests
{
    [Fact]
    public void ReExportFileName_uses_local_timestamp_pattern()
    {
        var exportedAt = new DateTimeOffset(2026, 10, 31, 14, 30, 0, TimeSpan.FromHours(2));
        Assert.Equal("interview_transcript_20261031-143000.docx", ExportPaths.ReExportFileName(exportedAt));
    }

    [Fact]
    public void ReExportPath_places_file_under_exports_folder()
    {
        var sessionFolder = Path.Combine(Path.GetTempPath(), "IR-20261031-091500-7F3A");
        var exportedAt = new DateTimeOffset(2026, 10, 31, 14, 30, 0, TimeSpan.FromHours(2));
        var path = ExportPaths.ReExportPath(sessionFolder, exportedAt);
        Assert.EndsWith(
            Path.Combine("exports", "interview_transcript_20261031-143000.docx"),
            path,
            StringComparison.Ordinal);
    }
}
