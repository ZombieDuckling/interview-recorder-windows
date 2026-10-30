using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

public sealed class CaptureRecovery : ICaptureRecovery
{
    private const string SchemaVersion = "1.0.0";
    private const int MinimumChunkFrames = PcmChunkWriter.SampleRate / 10;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Task<CaptureResult?> RecoverAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(workingDirectory);
        var manifestPath = Path.Combine(root, "session.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
            ?? throw new InvalidDataException("session.json is missing or invalid.");

        if (manifest["schemaVersion"]?.GetValue<string>() != SchemaVersion)
            throw new NotSupportedException("Unsupported session schema version.");

        var streams = EnsureArray(manifest, "streams");
        var hashes = EnsureObject(manifest, "hashes");
        var changed = false;
        var recovered = false;
        var resultStreams = new List<StreamFiles>(2);

        foreach (var role in new[] { StreamRole.Meeting, StreamRole.Microphone })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = ToKind(role);
            var folder = Path.Combine(root, "capture", kind);
            if (!Directory.Exists(folder))
                continue;

            var files = Directory.GetFiles(folder, "*.wav").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (files.Length == 0)
                continue;

            var streamNode = FindStreamByKind(streams, kind);
            if (streamNode is null)
            {
                streamNode = CreateDefaultStream(kind, role);
                streams.Add(streamNode);
                changed = true;
            }

            var chunkArray = EnsureArray(streamNode, "chunks");
            var trackedByFile = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (var chunkNode in chunkArray)
            {
                if (chunkNode is not JsonObject chunkObject)
                    continue;
                var file = chunkObject["file"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(file))
                    trackedByFile[file] = chunkObject;
            }

            foreach (var tracked in trackedByFile)
            {
                var fullPath = Path.Combine(root, tracked.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    continue;

                var expectedHash = hashes[tracked.Key]?.GetValue<string>() ?? tracked.Value["sha256"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(expectedHash))
                    continue;

                using var stream = File.OpenRead(fullPath);
                var actualHash = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                    throw new InvalidDataException($"Chunk hash mismatch: {tracked.Key}");
            }

            foreach (var file in files)
            {
                var relative = ToRelativeCapturePath(root, file);
                if (trackedByFile.ContainsKey(relative))
                    continue;

                var repaired = RepairRecoverableChunk(file);
                if (repaired is null)
                {
                    File.Delete(file);
                    changed = true;
                    continue;
                }

                chunkArray.Add(new JsonObject
                {
                    ["file"] = relative,
                    ["frames"] = repaired.Frames,
                    ["sha256"] = repaired.Sha256,
                    ["recovered"] = true,
                });
                hashes[relative] = repaired.Sha256;
                trackedByFile[relative] = new JsonObject();
                changed = true;
                recovered = true;
            }

            var ordered = chunkArray
                .OfType<JsonObject>()
                .OrderBy(node => node["file"]?.GetValue<string>(), StringComparer.Ordinal)
                .ToArray();

            var rebuilt = new JsonArray();
            foreach (var node in ordered)
                rebuilt.Add(node.DeepClone());
            streamNode["chunks"] = rebuilt;

            long totalFrames = 0;
            var chunkPaths = new List<string>(ordered.Length);
            foreach (var chunk in ordered)
            {
                totalFrames += chunk["frames"]?.GetValue<long>() ?? 0;
                var chunkFile = chunk["file"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(chunkFile))
                    chunkPaths.Add(chunkFile);
            }

            if (chunkPaths.Count == 0)
                continue;

            var startOffsetFrames = streamNode["firstFrameOffset"]?.GetValue<long>() ?? 0;
            var duration = TimeSpan.FromSeconds(totalFrames / (double)PcmChunkWriter.SampleRate);
            resultStreams.Add(new StreamFiles(
                role,
                Path.Combine(root, "capture", kind),
                chunkPaths,
                PcmChunkWriter.SampleRate,
                1,
                duration,
                TimeSpan.FromSeconds(startOffsetFrames / (double)PcmChunkWriter.SampleRate),
                streamNode["source"]?.GetValue<string>() == "wasapi-process-loopback"));
        }

        if (resultStreams.Count == 0)
        {
            if (changed)
                File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions));
            return Task.FromResult<CaptureResult?>(null);
        }

        if (changed)
            File.WriteAllText(manifestPath, manifest.ToJsonString(JsonOptions));

        var end = resultStreams.Max(stream => stream.StartOffset + stream.Duration);
        return Task.FromResult<CaptureResult?>(new CaptureResult(resultStreams, end, Complete: !recovered));
    }

    private static ClosedChunk? RepairRecoverableChunk(string path)
    {
        var info = new FileInfo(path);
        if (info.Length <= PcmChunkWriter.HeaderSize)
            return null;

        var dataBytes = info.Length - PcmChunkWriter.HeaderSize;
        dataBytes -= dataBytes % 2;
        var frames = dataBytes / 2;
        if (frames < MinimumChunkFrames)
            return null;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length != PcmChunkWriter.HeaderSize + dataBytes)
            stream.SetLength(PcmChunkWriter.HeaderSize + dataBytes);

        PcmChunkWriter.WriteHeader(stream, checked((int)dataBytes));
        stream.Flush(flushToDisk: true);
        stream.Position = 0;
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return new ClosedChunk(File: string.Empty, Frames: frames, Sha256: hash, Recovered: true);
    }

    private static string ToKind(StreamRole role) => role == StreamRole.Meeting ? "meeting" : "mic";

    private static string ToRelativeCapturePath(string root, string absolutePath)
    {
        var relative = Path.GetRelativePath(root, absolutePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static JsonObject? FindStreamByKind(JsonArray streams, string kind)
    {
        foreach (var stream in streams)
        {
            if (stream is JsonObject streamObject && streamObject["kind"]?.GetValue<string>() == kind)
                return streamObject;
        }

        return null;
    }

    private static JsonObject CreateDefaultStream(string kind, StreamRole role) =>
        new()
        {
            ["kind"] = kind,
            ["source"] = role == StreamRole.Meeting ? "wasapi-loopback" : "wasapi-capture",
            ["deviceType"] = "unknown",
            ["sampleRate"] = PcmChunkWriter.SampleRate,
            ["channels"] = 1,
            ["bitsPerSample"] = 16,
            ["chunkFrames"] = PcmChunkWriter.ChunkFrames,
            ["firstFrameOffset"] = 0,
            ["chunks"] = new JsonArray(),
        };

    private static JsonObject EnsureObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject obj)
            return obj;
        obj = [];
        parent[name] = obj;
        return obj;
    }

    private static JsonArray EnsureArray(JsonObject parent, string name)
    {
        if (parent[name] is JsonArray array)
            return array;
        array = [];
        parent[name] = array;
        return array;
    }
}
