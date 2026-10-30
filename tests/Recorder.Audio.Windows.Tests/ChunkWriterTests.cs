using System.Buffers.Binary;
using System.Security.Cryptography;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows.Tests;

public sealed class ChunkWriterTests
{
    [Fact]
    public void RotatesAt480000FramesAndPersistsHashBeforeOpeningNextChunk()
    {
        using var folder = new TestFolder();
        var closed = new List<ClosedChunk>();
        using var writer = new PcmChunkWriter(folder.Path, StreamRole.Meeting, chunk =>
        {
            Assert.False(File.Exists(System.IO.Path.Combine(folder.Path, "capture/meeting/000002.wav")));
            using var file = File.OpenRead(System.IO.Path.Combine(folder.Path, chunk.File));
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file)), chunk.Sha256);
            closed.Add(chunk);
        });
        writer.Write(new short[480001]);
        var chunk = Assert.Single(closed);
        Assert.Equal(480000, chunk.Frames);
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(folder.Path, chunk.File));
        Assert.Equal(960044, bytes.Length);
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal(960036, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal("WAVEfmt "u8.ToArray(), bytes[8..16]);
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(20)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(48000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)));
        Assert.Equal(960000, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
        // The callback above is only for the first rotation.
        writer.ChunkClosed = closed.Add;
        writer.Complete();
        Assert.Equal(1, closed[1].Frames);
        Assert.Equal("capture/meeting/000002.wav", closed[1].File);
    }

    [Fact]
    public void ExactRotationDoesNotLeaveAnEmptyNextFile()
    {
        using var folder = new TestFolder();
        using var writer = new PcmChunkWriter(folder.Path, StreamRole.Microphone, _ => { });
        writer.WriteSilence(960000);
        writer.Complete();
        Assert.Equal(2, Directory.GetFiles(System.IO.Path.Combine(folder.Path, "capture/mic")).Length);
    }

    [Fact]
    public void WritesSignedLittleEndianAndNeverOverwritesOriginals()
    {
        using var folder = new TestFolder();
        using (var writer = new PcmChunkWriter(folder.Path, StreamRole.Microphone, _ => { }))
        {
            writer.Write([short.MinValue, 1234, short.MaxValue]);
            writer.Complete();
        }
        var path = System.IO.Path.Combine(folder.Path, "capture/mic/000001.wav");
        Assert.Equal(new byte[] { 0, 128, 210, 4, 255, 127 }, File.ReadAllBytes(path)[44..]);
        Assert.Throws<IOException>(() => new PcmChunkWriter(folder.Path, StreamRole.Microphone, _ => { }));
    }

    [Fact]
    public void JournalFailurePreventsNextChunkFromOpening()
    {
        using var folder = new TestFolder();
        using var writer = new PcmChunkWriter(folder.Path, StreamRole.Meeting, _ => throw new IOException("test"));
        Assert.Throws<IOException>(() => writer.Write(new short[480001]));
        Assert.False(File.Exists(System.IO.Path.Combine(folder.Path, "capture/meeting/000002.wav")));
    }
}
