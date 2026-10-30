using System.Buffers.Binary;
using NAudio.Wave;

namespace Recorder.Audio.Windows.Tests;

public sealed class ConverterTests
{
    [Fact]
    public void Pcm16PreservesEveryBitAt48k()
    {
        short[] samples = [short.MinValue, -12345, -1, 0, 1, 12345, short.MaxValue];
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), samples[i]);
        Assert.Equal(samples, PcmFrameConverter.ToMono16Pcm48k(bytes, new WaveFormat(48000, 16, 1)));
    }

    [Fact]
    public void ConvertsStereoExtensiblePcm24()
    {
        var format = new WaveFormatExtensible(48000, 24, 2);
        Assert.Equal(new short[] { 16384, -16384 }, PcmFrameConverter.ToMono16Pcm48k(
            [0, 0, 64, 0, 0, 64, 0, 0, 192, 0, 0, 192], format));
    }

    [Fact]
    public void FloatDownmixClipsAndSanitizesNonFiniteSamples()
    {
        float[] samples = [0.5f, -0.5f, 2, 2, -2, -2, float.NaN, float.PositiveInfinity];
        var bytes = new byte[samples.Length * 4];
        for (var i = 0; i < samples.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), samples[i]);
        Assert.Equal(new short[] { 0, short.MaxValue, short.MinValue, 0 },
            PcmFrameConverter.ToMono16Pcm48k(bytes, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));
    }

    [Fact]
    public void IncompleteInputFramesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => PcmFrameConverter.ToMono16Pcm48k([0, 1, 2], new WaveFormat(48000, 16, 1)));
    }
}
