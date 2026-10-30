using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

internal sealed record ClosedChunk(string File, long Frames, string Sha256, bool Recovered = false);

internal sealed class PcmChunkWriter : IDisposable
{
    internal const int SampleRate = 48000;
    internal const int ChunkFrames = 480000;
    internal const int HeaderSize = 44;
    private readonly string root;
    private readonly string relativeDirectory;
    private readonly List<ClosedChunk> chunks = [];
    private FileStream? file;
    private int currentFrames;
    private bool completed;
    private bool failed;

    internal PcmChunkWriter(string directory, StreamRole role, Action<ClosedChunk> chunkClosed)
    {
        root = Path.GetFullPath(directory);
        relativeDirectory = "capture/" + (role == StreamRole.Meeting ? "meeting" : "mic");
        ChunkClosed = chunkClosed;
        var target = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(target);
        if (Directory.EnumerateFileSystemEntries(target).Any())
            throw new IOException("Capture folder is not empty.");
    }

    internal Action<ClosedChunk> ChunkClosed { get; set; }
    internal long FramesWritten { get; private set; }
    internal IReadOnlyList<ClosedChunk> Chunks => chunks;

    internal void Write(ReadOnlySpan<short> samples)
    {
        if (completed || failed) throw new InvalidOperationException("Capture writer is closed.");
        try
        {
            while (!samples.IsEmpty)
            {
                if (file is null)
                {
                    file = new FileStream(Path.Combine(root, NextName()), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                    WriteHeader(file, 0);
                }
                var count = Math.Min(samples.Length, ChunkFrames - currentFrames);
                // Windows and the supported Mac test host are little-endian.
                file.Write(MemoryMarshal.AsBytes(samples[..count]));
                currentFrames += count;
                FramesWritten += count;
                samples = samples[count..];
                if (currentFrames == ChunkFrames) CloseChunk();
            }
        }
        catch
        {
            failed = true;
            throw;
        }
    }

    internal void WriteSilence(long frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        Span<short> silence = stackalloc short[4096];
        silence.Clear();
        while (frames > 0)
        {
            var count = (int)Math.Min(frames, silence.Length);
            Write(silence[..count]);
            frames -= count;
        }
    }

    internal void Complete()
    {
        if (completed) return;
        completed = true;
        if (!failed) CloseChunk();
    }

    private string NextName() => relativeDirectory + "/" + (chunks.Count + 1).ToString("D6", CultureInfo.InvariantCulture) + ".wav";

    private void CloseChunk()
    {
        if (file is null) return;
        var closing = file;
        file = null;
        var relativePath = NextName();
        string hash;
        using (closing)
        {
            WriteHeader(closing, currentFrames * 2);
            closing.Flush(flushToDisk: true);
            closing.Position = 0;
            hash = Convert.ToHexStringLower(SHA256.HashData(closing));
        }
        var chunk = new ClosedChunk(relativePath, currentFrames, hash);
        chunks.Add(chunk);
        currentFrames = 0;
        // Synchronous durable acknowledgement is required before another file may open.
        ChunkClosed(chunk);
    }

    internal static void WriteHeader(Stream output, int dataBytes)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], dataBytes + 36);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataBytes);
        output.Position = 0;
        output.Write(header);
    }

    // Disposal without Complete intentionally leaves a recoverable unfinished chunk.
    public void Dispose() => file?.Dispose();
}
