using System.Runtime.InteropServices;
using NAudio.Wave;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows.Tests;

public sealed class TimelineTests
{
    [Theory]
    [InlineData(8000)]
    [InlineData(44100)]
    [InlineData(96000)]
    public void StreamingConversionIsIndependentOfPacketBoundaries(int rate)
    {
        var samples = Enumerable.Range(0, rate).Select(i => (short)(Math.Sin(i * 0.031) * 12000)).ToArray();
        var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        var format = new WaveFormat(rate, 16, 1);
        var expected = PcmFrameConverter.ToMono16Pcm48k(bytes, format);
        var converter = new PcmFrameConverter(format);
        var actual = new List<short>();
        for (var i = 0; i < bytes.Length; i += 274)
            actual.AddRange(converter.Convert(bytes.AsSpan(i, Math.Min(274, bytes.Length - i))));
        actual.AddRange(converter.Flush());
        Assert.Equal(48000, actual.Count);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ClockCalculationsNeverInsertNegativeSilence()
    {
        Assert.Equal(0, StreamClockAligner.ComputeMissingFrames(48000, 24000, 0));
        Assert.Equal(0, StreamClockAligner.ComputeMissingFrames(0, 24000, 30000));
        Assert.Equal(24000, StreamClockAligner.ComputeMissingFrames(48000, 96000, 24000));
        Assert.False(StreamClockAligner.IsGapLongerThanTwoSeconds(96000));
        Assert.True(StreamClockAligner.IsGapLongerThanTwoSeconds(96001));
    }
}

public sealed class SilenceTests
{
    [Fact]
    public void SilentInputAdvancesAcrossChunkBoundariesAndReportsCumulativeGapOnce()
    {
        using var folder = new TestFolder();
        using var writer = new PcmChunkWriter(folder.Path, StreamRole.Meeting, _ => { });
        var gaps = new List<long>();
        var aligner = new StreamClockAligner(writer, 0, gaps.Add);
        for (var frame = 2400; frame <= 600000; frame += 2400) aligner.AdvanceTo(frame);
        writer.Complete();
        Assert.Equal(600000, writer.FramesWritten);
        Assert.Equal(2, writer.Chunks.Count);
        Assert.Single(gaps);
        Assert.True(gaps[0] > 96000);
    }

    [Fact]
    public void PacketsKeepTheirTimeThroughGapsOverlapAndReattachment()
    {
        using var folder = new TestFolder();
        using var writer = new PcmChunkWriter(folder.Path, StreamRole.Microphone, _ => { });
        var aligner = new StreamClockAligner(writer, 0, _ => { });
        aligner.Append(2, new short[] { 10, 20, 30 });
        aligner.Append(4, new short[] { 30, 40 });
        aligner.Append(9, new short[] { 50 });
        aligner.Append(0, new short[] { 99, 99 });
        aligner.AdvanceTo(12);
        writer.Complete();
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(folder.Path, "capture/mic/000001.wav"));
        Assert.Equal(new short[] { 0, 0, 10, 20, 30, 40, 0, 0, 0, 50, 0, 0 }, MemoryMarshal.Cast<byte, short>(bytes.AsSpan(44)).ToArray());
    }
}
