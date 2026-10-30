using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Recorder.Core.Abstractions;
using Recorder.Core.Diagnostics;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

public sealed class CaptureRecovery : ICaptureRecovery
{
    public Task<CaptureResult?> RecoverAsync(string workingDirectory, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            try { return Recover(workingDirectory, cancellationToken); }
            catch (InvalidDataException)
            {
                CaptureDiagnostics.Write(workingDirectory, ProcessingLogCode.HashMismatch);
                throw;
            }
        }, cancellationToken);

    private static CaptureResult? Recover(string workingDirectory, CancellationToken token)
    {
        var root = Path.GetFullPath(workingDirectory);
        var journal = new CaptureManifest(root);
        var manifest = journal.Read();
        var plans = new List<RecoveryPlan>();
        // Validate ALL closed originals before touching any unfinished tail, including the other stream.
        foreach (var role in new[] { StreamRole.Meeting, StreamRole.Microphone })
        {
            token.ThrowIfCancellationRequested();
            var kind = StreamRoleMapping.ToJsonKind(role);
            var prefix = "capture/" + kind + "/";
            var folder = Path.Combine(root, "capture", kind);
            RejectLink(Path.Combine(root, "capture"));
            RejectLink(folder);
            var entries = manifest.Streams.Where(s => s.Kind == CaptureManifest.ToKind(role)).ToArray();
            if (entries.Length > 1) throw Corrupt(root);
            var stream = entries.SingleOrDefault() ?? CaptureManifest.NewStream(role, 0, false);
            if (stream.SampleRate != 48000 || stream.Channels != 1 || stream.BitsPerSample != 16 ||
                stream.ChunkFrames != 480000 || stream.FirstFrameOffset < 0) throw Corrupt(root);
            var ordered = stream.Chunks.OrderBy(c => c.File, StringComparer.Ordinal).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var chunk = ordered[i];
                var expected = prefix + (i + 1).ToString("D6", CultureInfo.InvariantCulture) + ".wav";
                if (chunk.File != expected || chunk.Frames is <= 0 or > PcmChunkWriter.ChunkFrames) throw Corrupt(root);
                if (i < ordered.Length - 1 && chunk.Frames != PcmChunkWriter.ChunkFrames) throw Corrupt(root);
                var path = Path.Combine(root, chunk.File);
                RejectLink(path);
                if (!File.Exists(path)) throw Corrupt(root);
                using var file = File.OpenRead(path);
                ValidateHeader(file, closedFrames: chunk.Frames);
                file.Position = 0;
                var actual = Convert.ToHexStringLower(SHA256.HashData(file));
                if (actual != chunk.Sha256 || !manifest.Hashes.TryGetValue(chunk.File, out var hash) || actual != hash)
                    throw Corrupt(root);
            }
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.wav").Order(StringComparer.Ordinal).ToArray() : [];
            var tracked = ordered.Select(c => Path.Combine(root, c.File)).ToHashSet(StringComparer.Ordinal);
            if (manifest.Hashes.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal) && !tracked.Contains(Path.Combine(root, key))))
                throw Corrupt(root);
            var tails = files.Where(p => !tracked.Contains(p)).ToArray();
            if (tails.Length > 1) throw Corrupt(root);
            string? tail = tails.SingleOrDefault();
            if (tail is not null)
            {
                if (Path.GetFileName(tail) != (ordered.Length + 1).ToString("D6", CultureInfo.InvariantCulture) + ".wav" ||
                    (ordered.Length > 0 && ordered[^1].Frames != PcmChunkWriter.ChunkFrames)) throw Corrupt(root);
                RejectLink(tail);
                using var file = File.OpenRead(tail);
                if (file.Length >= PcmChunkWriter.HeaderSize) ValidateHeader(file, closedFrames: null);
            }
            plans.Add(new RecoveryPlan(stream, folder, tail, entries.Length == 0));
        }

        var changed = false;
        foreach (var plan in plans)
        {
            token.ThrowIfCancellationRequested();
            if (plan.Tail is null) continue;
            var info = new FileInfo(plan.Tail);
            var frames = Math.Max(0, (info.Length - PcmChunkWriter.HeaderSize) / 2);
            if (frames < 4800)
            {
                File.Delete(plan.Tail);
                changed = true;
                manifest.Recording.Recovered = true;
                journal.Save(manifest);
                CaptureDiagnostics.Write(root, ProcessingLogCode.ChunkRecovered, new ProcessingLogValue("droppedFrames", frames));
                continue;
            }
            using var file = new FileStream(plan.Tail, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            file.SetLength(PcmChunkWriter.HeaderSize + frames * 2);
            PcmChunkWriter.WriteHeader(file, checked((int)(frames * 2)));
            file.Flush(flushToDisk: true);
            file.Position = 0;
            var hash = Convert.ToHexStringLower(SHA256.HashData(file));
            var relative = Path.GetRelativePath(root, plan.Tail).Replace(Path.DirectorySeparatorChar, '/');
            plan.Stream.Chunks.Add(new CaptureChunkInfo { File = relative, Frames = (int)frames, Sha256 = hash, Recovered = true });
            manifest.Hashes.Add(relative, hash);
            if (plan.NewStream) manifest.Streams.Add(plan.Stream);
            changed = true;
            manifest.Recording.Recovered = true;
            // Persist each acknowledged tail independently; retry is safe after an interrupted recovery.
            journal.Save(manifest);
            CaptureDiagnostics.Write(root, ProcessingLogCode.ChunkRecovered, new("file", new SessionRelativePath(relative)), new("frames", frames));
        }
        var streams = plans.Where(p => p.Stream.Chunks.Count > 0).Select(p => new StreamFiles(
            p.Stream.Kind == StreamJsonKind.Meeting ? StreamRole.Meeting : StreamRole.Microphone,
            p.Folder, p.Stream.Chunks.OrderBy(c => c.File, StringComparer.Ordinal).Select(c => Path.Combine(root, c.File)).ToArray(),
            48000, 1, TimeSpan.FromSeconds(p.Stream.Chunks.Sum(c => (long)c.Frames) / 48000d),
            TimeSpan.FromSeconds(p.Stream.FirstFrameOffset / 48000d), p.Stream.Source == StreamCaptureSource.WasapiProcessLoopback)).ToArray();
        if (streams.Length == 0) return null;
        var duration = streams.Max(s => s.StartOffset + s.Duration);
        if (changed)
        {
            manifest.Recording.DurationSeconds = Math.Round(duration.TotalSeconds, 3);
            journal.Save(manifest);
        }
        return new CaptureResult(streams, duration, Complete: manifest.Recording.Recovered != true &&
            !manifest.Streams.Any(s => s.Chunks.Any(c => c.Recovered == true)));
    }

    private static void RejectLink(string path)
    {
        if ((Directory.Exists(path) || File.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Capture paths cannot be links.");
    }

    private static InvalidDataException Corrupt(string root)
    {
        CaptureDiagnostics.Write(root, ProcessingLogCode.HashMismatch);
        return new InvalidDataException("Original capture files or metadata are missing or corrupt. They have not been repaired.");
    }

    private static void ValidateHeader(Stream file, int? closedFrames)
    {
        Span<byte> header = stackalloc byte[PcmChunkWriter.HeaderSize];
        if (file.Length < header.Length || file.Length > header.Length + PcmChunkWriter.ChunkFrames * 2 + 1)
            throw new InvalidDataException("Invalid capture chunk size.");
        file.ReadExactly(header);
        using var expected = new MemoryStream();
        PcmChunkWriter.WriteHeader(expected, 0);
        var reference = expected.ToArray();
        // Only RIFF/data lengths may be incomplete after a crash.
        if (!header[..4].SequenceEqual(reference.AsSpan(0, 4)) || !header[8..40].SequenceEqual(reference.AsSpan(8, 32)))
            throw new InvalidDataException("Capture is not canonical 48 kHz mono PCM16 WAV.");
        if (closedFrames is int count && (file.Length != 44 + count * 2 ||
            BinaryPrimitives.ReadInt32LittleEndian(header[4..]) != 36 + count * 2 ||
            BinaryPrimitives.ReadInt32LittleEndian(header[40..]) != count * 2))
            throw new InvalidDataException("A closed capture chunk has invalid lengths.");
    }

    private sealed record RecoveryPlan(SessionStreamInfo Stream, string Folder, string? Tail, bool NewStream);
}
