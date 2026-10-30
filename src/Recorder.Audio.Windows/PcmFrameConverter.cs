using System.Buffers.Binary;
using NAudio.Wave;

namespace Recorder.Audio.Windows;

internal static class PcmFrameConverter
{
    internal static short[] ToMono16Pcm48k(ReadOnlySpan<byte> input, WaveFormat source)
    {
        var channels = Math.Max(1, source.Channels);
        var sourceFrames = source.BlockAlign == 0 ? 0 : input.Length / source.BlockAlign;
        if (sourceFrames <= 0)
            return [];

        var mono = new float[sourceFrames];
        DecodeToMono(source, input, mono, channels);
        return ResampleTo48kAndConvert(source.SampleRate, mono);
    }

    private static void DecodeToMono(WaveFormat source, ReadOnlySpan<byte> input, float[] mono, int channels)
    {
        if (source.Encoding == WaveFormatEncoding.Pcm && source.BitsPerSample == 16)
        {
            for (var frame = 0; frame < mono.Length; frame++)
            {
                var sum = 0f;
                var frameOffset = frame * source.BlockAlign;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sampleOffset = frameOffset + (channel * 2);
                    short value = BinaryPrimitives.ReadInt16LittleEndian(input.Slice(sampleOffset, 2));
                    sum += value / 32768f;
                }

                mono[frame] = sum / channels;
            }

            return;
        }

        if (source.Encoding == WaveFormatEncoding.IeeeFloat && source.BitsPerSample == 32)
        {
            for (var frame = 0; frame < mono.Length; frame++)
            {
                var sum = 0f;
                var frameOffset = frame * source.BlockAlign;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sampleOffset = frameOffset + (channel * 4);
                    var bits = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(sampleOffset, 4));
                    sum += BitConverter.Int32BitsToSingle(bits);
                }

                mono[frame] = sum / channels;
            }

            return;
        }

        throw new NotSupportedException($"Unsupported input format: {source.Encoding} {source.BitsPerSample}-bit.");
    }

    private static short[] ResampleTo48kAndConvert(int sourceRate, float[] mono)
    {
        if (sourceRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRate));

        if (sourceRate == PcmChunkWriter.SampleRate)
            return mono.Select(FloatToPcm16).ToArray();

        var outputFrames = (int)Math.Round(mono.Length * (double)PcmChunkWriter.SampleRate / sourceRate);
        if (outputFrames <= 0)
            return [];

        var output = new short[outputFrames];
        for (var i = 0; i < outputFrames; i++)
        {
            var srcPos = i * (double)(mono.Length - 1) / Math.Max(1, outputFrames - 1);
            var left = (int)Math.Floor(srcPos);
            var right = Math.Min(left + 1, mono.Length - 1);
            var frac = (float)(srcPos - left);
            var sample = (mono[left] * (1f - frac)) + (mono[right] * frac);
            output[i] = FloatToPcm16(sample);
        }

        return output;
    }

    private static short FloatToPcm16(float value)
    {
        value = Math.Clamp(value, -1f, 1f);
        if (value <= -1f)
            return short.MinValue;
        if (value >= 1f)
            return short.MaxValue;
        return (short)Math.Round(value * short.MaxValue);
    }
}
