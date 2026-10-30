using System.Globalization;
using Recorder.Core.IO;

namespace Recorder.Core.Formats;

public static class SchemaVersionPolicy
{
    public const int SupportedMajorVersion = 1;

    public static void EnsureSessionSupported(string path, string schemaVersion)
    {
        if (!IsSupportedMajor(schemaVersion))
        {
            throw new UnsupportedSchemaVersionException(path, schemaVersion, SupportedMajorVersion);
        }
    }

    public static void EnsureTranscriptSupported(string path, string schemaVersion)
    {
        if (!IsSupportedMajor(schemaVersion))
        {
            throw new UnsupportedSchemaVersionException(path, schemaVersion, SupportedMajorVersion);
        }
    }

    public static bool IsSupportedMajor(string schemaVersion)
    {
        if (string.IsNullOrWhiteSpace(schemaVersion))
        {
            return false;
        }

        var dot = schemaVersion.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            return false;
        }

        return int.TryParse(schemaVersion.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && major == SupportedMajorVersion;
    }
}
