using Recorder.Core.Formats;

namespace Recorder.Transcription.Tests;

public sealed class WindowingTests
{
    [Fact]
    public void WindowsStayBoundedAndOverlapWithoutLosingTail()
    {
        using var fixture = new WaveFixture(61 * 16000);
        using var reader = new PcmWindowReader(fixture.Path);
        var buffer = new float[30 * 16000];
        Assert.Equal(61 * 16000, reader.FrameCount);
        Assert.Equal(buffer.Length, reader.Read(0, buffer));
        Assert.Equal(-1, buffer[0]);
        Assert.Equal(32767 / 32768f, buffer[1]);
        Assert.Equal(buffer.Length, reader.Read(28 * 16000, buffer));
        Assert.Equal(5 * 16000, reader.Read(56 * 16000, buffer));
        Assert.Equal(0, reader.Read(61 * 16000, buffer));
    }

    [Theory]
    [InlineData(48000, 1, 16)]
    [InlineData(16000, 2, 16)]
    [InlineData(16000, 1, 32)]
    public void RejectsWrongPcmFormat(int rate, short channels, short bits)
    {
        using var fixture = new WaveFixture(16000, rate, channels, bits);
        Assert.Throws<InvalidDataException>(() => new PcmWindowReader(fixture.Path));
    }

    [Fact]
    public void RejectsTruncatedData()
    {
        using var fixture = new WaveFixture(16000);
        using (var file = File.OpenWrite(fixture.Path)) { file.SetLength(file.Length - 4); }
        Assert.Throws<InvalidDataException>(() => new PcmWindowReader(fixture.Path));
    }

    [Fact]
    public void StitchDropsRepeatedOverlapByTimeAndAddsStartOffsetOnce()
    {
        var stitcher = new SegmentStitcher();
        stitcher.AddWindow(StreamRole.Microphone, 5, 0, 30,
            [new(27, 29, "First."), new(29, 30, "Repeated.")]);
        stitcher.AddWindow(StreamRole.Microphone, 5, 28, 30,
            [new(1, 2, "Repeated."), new(2, 4, "Next.")]);
        var segments = stitcher.Build();
        Assert.Equal(3, segments.Count);
        Assert.Equal(new[] { 32d, 34d, 35d }, segments.Select(s => s.Start.TotalSeconds));
        Assert.Equal(new[] { 1, 2, 3 }, segments.Select(s => s.Index));
        Assert.All(segments, s => Assert.Equal(SpeakerLabel.Researcher, s.Speaker));
    }

    [Fact]
    public void CrossStreamOverlapKeepsBothAndMarksUnknownWithoutMarkingTouchingSegments()
    {
        var stitcher = new SegmentStitcher();
        stitcher.AddWindow(StreamRole.Microphone, 0, 0, 30, [new(1, 4, "Question."), new(5, 6, "Next.")]);
        stitcher.AddWindow(StreamRole.Meeting, 0, 0, 30, [new(3, 5, "Answer.")]);
        var segments = stitcher.Build();
        Assert.Equal(3, segments.Count);
        Assert.Equal(new[] { SpeakerLabel.Unknown, SpeakerLabel.Unknown, SpeakerLabel.Researcher }, segments.Select(s => s.Speaker));
        Assert.All(segments, s => Assert.False(s.SpeakerVerified));
    }

    [Fact]
    public void RepeatedWordsOutsideTheOverlapAreNotRemoved()
    {
        var stitcher = new SegmentStitcher();
        stitcher.AddWindow(StreamRole.Meeting, 0, 0, 30, [new(1, 2, "Yes."), new(20, 21, "Yes.")]);
        Assert.Equal(2, stitcher.Build().Count);
    }
}

internal sealed class WaveFixture : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"asr-{Guid.NewGuid():N}.wav");

    public WaveFixture(int frames, int rate = 16000, short channels = 1, short bits = 16)
    {
        using var writer = new BinaryWriter(File.Create(Path));
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        writer.Write("data"u8);
        writer.Write(frames * 2);
        for (var i = 0; i < frames; i++) { writer.Write(i % 2 == 0 ? short.MinValue : short.MaxValue); }
    }

    public void Dispose() => File.Delete(Path);
}
