namespace Recorder.Export;

public static class ExportPaths
{
    public const string PrimaryDocxFileName = "interview_transcript.docx";

    public static string ReExportFileName(DateTimeOffset exportedAtLocal)
    {
        return $"interview_transcript_{exportedAtLocal:yyyyMMdd-HHmmss}.docx";
    }

    public static string ReExportPath(string sessionFolder, DateTimeOffset exportedAtLocal)
    {
        return Path.Combine(sessionFolder, "exports", ReExportFileName(exportedAtLocal));
    }
}
