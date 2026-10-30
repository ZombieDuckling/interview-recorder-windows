using System.Text.Json;
using Recorder.Core.Formats;
using Recorder.Core.IO;

namespace Recorder.Audio.Windows;

// The pipeline owns state/stateHistory. It must await StopAsync before its next manifest write.
internal sealed class CaptureManifest(string directory)
{
    private readonly object gate = new();
    private readonly string path = Path.Combine(Path.GetFullPath(directory), "session.json");

    internal void Begin(DateTimeOffset startedAt) => Update(manifest =>
    {
        if (!manifest.Consent.Confirmed)
            throw new InvalidOperationException("Consent must be confirmed before capture starts.");
        if (manifest.Streams.Any(s => s.Chunks.Count != 0))
            throw new InvalidOperationException("A recording with original chunks cannot be overwritten.");
        manifest.Recording.StartedAt = startedAt;
        manifest.Recording.StoppedAt = null;
        manifest.Recording.DurationSeconds = null;
    });

    internal void End(TimeSpan duration) => Update(manifest =>
    {
        manifest.Recording.DurationSeconds = Math.Round(duration.TotalSeconds, 3);
        manifest.Recording.StoppedAt = manifest.Recording.StartedAt + duration;
    });

    internal void RegisterStream(StreamRole role, long offset, bool processLoopback) => Update(manifest =>
    {
        var kind = ToKind(role);
        var stream = manifest.Streams.SingleOrDefault(s => s.Kind == kind);
        if (stream is null)
        {
            stream = NewStream(role, offset, processLoopback);
            manifest.Streams.Add(stream);
        }
        else
        {
            stream.Source = Source(role, processLoopback);
            // Re-attachment never changes the original timeline origin.
        }
    });

    internal void CloseChunk(StreamRole role, ClosedChunk chunk) => Update(manifest =>
    {
        var stream = manifest.Streams.Single(s => s.Kind == ToKind(role));
        stream.Chunks.Add(new CaptureChunkInfo
        {
            File = chunk.File, Frames = checked((int)chunk.Frames), Sha256 = chunk.Sha256,
            Recovered = chunk.Recovered ? true : null,
        });
        manifest.Hashes.Add(chunk.File, chunk.Sha256);
    });

    internal SessionManifest Read()
    {
        lock (gate)
        {
            using var input = File.OpenRead(path);
            if (input.Length > 32 * 1024 * 1024) throw new InvalidDataException("Session manifest is too large.");
            using var document = JsonDocument.Parse(input);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var version) ||
                !SchemaVersionPolicy.IsSupportedMajor(version.GetString() ?? ""))
                throw new NotSupportedException("This session uses a newer or unsupported format. Update the app to open it.");
            return document.RootElement.Deserialize<SessionManifest>(FormatJson.Options)
                ?? throw new InvalidDataException("session.json is invalid.");
        }
    }

    internal void Update(Action<SessionManifest> update)
    {
        lock (gate)
        {
            // Windows Defender, the search indexer or a sync client briefly hold freshly written files.
            // One sharing violation must not stop an interview: retry for up to ~2 s (inside the ~4 s capture queue).
            WithRetry(() =>
            {
                var manifest = Read();
                update(manifest);
                AtomicFileWrite.WriteAllText(path, FormatJson.SerializeSession(manifest));
            });
        }
    }

    internal void Save(SessionManifest manifest)
    {
        lock (gate) WithRetry(() => AtomicFileWrite.WriteAllText(path, FormatJson.SerializeSession(manifest)));
    }

    private static void WithRetry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < 12 && (ex is IOException or UnauthorizedAccessException))
            {
                Thread.Sleep(Math.Min(50 * attempt, 250));
            }
        }
    }

    internal static StreamJsonKind ToKind(StreamRole role) => role == StreamRole.Meeting ? StreamJsonKind.Meeting : StreamJsonKind.Mic;

    internal static SessionStreamInfo NewStream(StreamRole role, long offset, bool processLoopback) => new()
    {
        Kind = ToKind(role), Source = Source(role, processLoopback), DeviceType = CaptureDeviceType.Unknown,
        SampleRate = PcmChunkWriter.SampleRate, Channels = 1, BitsPerSample = 16,
        ChunkFrames = PcmChunkWriter.ChunkFrames, FirstFrameOffset = checked((int)offset),
    };

    private static StreamCaptureSource Source(StreamRole role, bool processLoopback) => processLoopback
        ? StreamCaptureSource.WasapiProcessLoopback
        : role == StreamRole.Meeting ? StreamCaptureSource.WasapiLoopback : StreamCaptureSource.WasapiCapture;
}
