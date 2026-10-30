using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

internal sealed class CaptureManifest(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly string path = Path.Combine(Path.GetFullPath(directory), "session.json");

    internal void Begin(DateTimeOffset startedAt)
    {
        lock (gate)
        {
            var manifest = Load();
            if (manifest["consent"]?["confirmed"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Consent must be confirmed before capture starts.");

            var recording = EnsureObject(manifest, "recording");
            recording["startedAt"] = FormatUtc(startedAt);
            Save(manifest);
        }
    }

    internal void RegisterStream(StreamRole role, long offset, bool processLoopback)
    {
        lock (gate)
        {
            var manifest = Load();
            var streams = EnsureArray(manifest, "streams");
            var kind = ToKind(role);

            JsonObject? existing = null;
            foreach (var stream in streams)
            {
                if (stream is JsonObject streamObject && streamObject["kind"]?.GetValue<string>() == kind)
                {
                    existing = streamObject;
                    break;
                }
            }

            var entry = existing ?? new JsonObject();
            entry["kind"] = kind;
            entry["source"] = processLoopback
                ? "wasapi-process-loopback"
                : (role == StreamRole.Meeting ? "wasapi-loopback" : "wasapi-capture");
            entry["deviceType"] = "unknown";
            entry["sampleRate"] = PcmChunkWriter.SampleRate;
            entry["channels"] = 1;
            entry["bitsPerSample"] = 16;
            entry["chunkFrames"] = PcmChunkWriter.ChunkFrames;
            entry["firstFrameOffset"] = offset;
            entry["chunks"] ??= new JsonArray();

            if (existing is null)
                streams.Add(entry);

            Save(manifest);
        }
    }

    internal void CloseChunk(StreamRole role, ClosedChunk chunk)
    {
        lock (gate)
        {
            var manifest = Load();
            var streams = EnsureArray(manifest, "streams");
            var kind = ToKind(role);

            JsonObject? streamEntry = null;
            foreach (var stream in streams)
            {
                if (stream is JsonObject streamObject && streamObject["kind"]?.GetValue<string>() == kind)
                {
                    streamEntry = streamObject;
                    break;
                }
            }

            if (streamEntry is null)
                throw new InvalidOperationException($"Stream '{kind}' is not registered.");

            var chunks = EnsureArray(streamEntry, "chunks");
            var chunkNode = new JsonObject
            {
                ["file"] = chunk.File,
                ["frames"] = chunk.Frames,
                ["sha256"] = chunk.Sha256,
            };
            if (chunk.Recovered)
                chunkNode["recovered"] = true;
            chunks.Add(chunkNode);

            var hashes = EnsureObject(manifest, "hashes");
            hashes[chunk.File] = chunk.Sha256;
            Save(manifest);
        }
    }

    private JsonObject Load()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
        if (manifest is null)
            throw new InvalidDataException("session.json is missing or invalid.");
        return manifest;
    }

    private void Save(JsonObject manifest) => File.WriteAllText(path, manifest.ToJsonString(JsonOptions));

    private static string ToKind(StreamRole role) => role == StreamRole.Meeting ? "meeting" : "mic";

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static JsonArray EnsureArray(JsonObject parent, string name)
    {
        if (parent[name] is JsonArray array)
            return array;
        array = [];
        parent[name] = array;
        return array;
    }

    private static JsonObject EnsureObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject obj)
            return obj;
        obj = [];
        parent[name] = obj;
        return obj;
    }
}
