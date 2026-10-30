using System.Text.Json;
using Recorder.Core.Formats;

namespace Recorder.Core.IO;

public static class SessionManifestIO
{
    public static SessionManifest Read(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return FormatJson.DeserializeSession(json);
        }
        catch (JsonException ex)
        {
            throw new CorruptedFileException(path, "session.json is not valid JSON for the shared schema.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CorruptedFileException(path, "session.json could not be read.", ex);
        }
    }

    public static async Task WriteAsync(string path, SessionManifest manifest, CancellationToken cancellationToken = default)
    {
        var json = FormatJson.SerializeSession(manifest);
        await AtomicFileWrite.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}

public static class TranscriptIO
{
    public static Transcript Read(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return FormatJson.DeserializeTranscript(json);
        }
        catch (JsonException ex)
        {
            throw new CorruptedFileException(path, "transcript.json is not valid JSON for the shared schema.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CorruptedFileException(path, "transcript.json could not be read.", ex);
        }
    }

    public static async Task WriteAsync(string path, Transcript transcript, CancellationToken cancellationToken = default)
    {
        var json = FormatJson.SerializeTranscript(transcript);
        await AtomicFileWrite.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
