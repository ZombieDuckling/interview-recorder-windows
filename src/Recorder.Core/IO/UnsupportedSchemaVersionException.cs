namespace Recorder.Core.IO;

/// <summary>Thrown when session.json or transcript.json uses an unsupported major schema version.</summary>
public sealed class UnsupportedSchemaVersionException : Exception
{
    public UnsupportedSchemaVersionException(string path, string schemaVersion, int supportedMajor)
        : base($"This version of Interview Recorder cannot open {path}. The file uses schema version {schemaVersion}; only major version {supportedMajor} is supported.")
    {
        FilePath = path;
        SchemaVersion = schemaVersion;
        SupportedMajorVersion = supportedMajor;
    }

    public string FilePath { get; }

    public string SchemaVersion { get; }

    public int SupportedMajorVersion { get; }
}
