namespace Recorder.Core.IO;

/// <summary>Thrown when a persisted JSON file cannot be read or validated.</summary>
public sealed class CorruptedFileException : Exception
{
    public CorruptedFileException(string path, string reason, Exception? inner = null)
        : base($"File is corrupted or unreadable: {path}. {reason}", inner)
    {
        FilePath = path;
        Reason = reason;
    }

    public string FilePath { get; }

    public string Reason { get; }
}
