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
            var manifest = FormatJson.DeserializeSession(json);
            SchemaVersionPolicy.EnsureSessionSupported(path, manifest.SchemaVersion);
            return manifest;
        }
        catch (UnsupportedSchemaVersionException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new CorruptedFileException(path, "session.json is not valid JSON for the shared schema.", ex);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
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
        SchemaVersionPolicy.EnsureSessionSupported(path, manifest.SchemaVersion);
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
            var transcript = FormatJson.DeserializeTranscript(json);
            SchemaVersionPolicy.EnsureTranscriptSupported(path, transcript.SchemaVersion);
            return transcript;
        }
        catch (UnsupportedSchemaVersionException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new CorruptedFileException(path, "transcript.json is not valid JSON for the shared schema.", ex);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
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
        SchemaVersionPolicy.EnsureTranscriptSupported(path, transcript.SchemaVersion);
        var json = FormatJson.SerializeTranscript(transcript);
        await AtomicFileWrite.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
