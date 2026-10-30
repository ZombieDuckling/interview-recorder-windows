using System.Buffers.Binary;
using NAudio.Wave;

namespace Recorder.Audio.Windows;

// Stateful rational-phase linear resampler: packet boundaries cannot accumulate rounding drift.
// Holds one mono input sample, never the recording. A fresh instance is used after a format/gap change.
internal sealed class PcmFrameConverter
{
    private readonly WaveFormat source;
    private readonly bool floatingPoint;
    private double previous;
    private bool completed;

    internal PcmFrameConverter(WaveFormat format)
    {
        source = format is WaveFormatExtensible extended ? extended.ToStandardWaveFormat() : format;
        floatingPoint = source.Encoding == WaveFormatEncoding.IeeeFloat;
        if (source.SampleRate is < 8000 or > 384000 || source.Channels is < 1 or > 32 ||
            source.BlockAlign != source.Channels * source.BitsPerSample / 8 ||
            (!floatingPoint && source.Encoding != WaveFormatEncoding.Pcm) ||
            (floatingPoint ? source.BitsPerSample is not (32 or 64) : source.BitsPerSample is not (8 or 16 or 24 or 32)))
            throw new NotSupportedException("The audio device must provide PCM or IEEE float audio.");
    }

    internal long InputFrames { get; private set; }
    internal long OutputFrames { get; private set; }

    internal short[] Convert(ReadOnlySpan<byte> input)
    {
        if (completed) throw new InvalidOperationException("The converter has been flushed.");
        if (input.Length % source.BlockAlign != 0) throw new InvalidDataException("Incomplete audio input frame.");
        var count = input.Length / source.BlockAlign;
        if (count > source.SampleRate) throw new InvalidDataException("Audio packets must be at most one second.");
        if (count == 0) return [];
        var output = new short[checked((int)Math.Ceiling((count + 1d) * 48000 / source.SampleRate))];
        var written = 0;
        for (var frame = 0; frame < count; frame++)
        {
            var sample = Decode(input.Slice(frame * source.BlockAlign, source.BlockAlign));
            var index = InputFrames++;
            while (OutputFrames * source.SampleRate <= index * 48000)
            {
                var fraction = (OutputFrames * source.SampleRate - (index - 1) * 48000) / 48000d;
                output[written++] = ToPcm(index == 0 ? sample : previous + (sample - previous) * fraction);
                OutputFrames++;
            }
            previous = sample;
        }
        return output[..written];
    }

    internal short[] Flush()
    {
        if (completed) return [];
        completed = true;
        var total = InputFrames * 48000 / source.SampleRate;
        var output = new short[checked((int)Math.Max(0, total - OutputFrames))];
        Array.Fill(output, ToPcm(previous));
        OutputFrames += output.Length;
        return output;
    }

    internal static short[] ToMono16Pcm48k(ReadOnlySpan<byte> input, WaveFormat source)
    {
        var converter = new PcmFrameConverter(source);
        var output = converter.Convert(input);
        return [.. output, .. converter.Flush()];
    }

    private double Decode(ReadOnlySpan<byte> frame)
    {
        double sum = 0;
        var bytes = source.BitsPerSample / 8;
        for (var channel = 0; channel < source.Channels; channel++)
        {
            var sample = frame.Slice(channel * bytes, bytes);
            double value;
            if (floatingPoint)
                value = bytes == 4 ? BinaryPrimitives.ReadSingleLittleEndian(sample) : BinaryPrimitives.ReadDoubleLittleEndian(sample);
            else
                value = bytes switch
                {
                    1 => (sample[0] - 128) / 128d,
                    2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768d,
                    3 => ((sample[0] | (sample[1] << 8) | (sample[2] << 16)) << 8 >> 8) / 8388608d,
                    4 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648d,
                    _ => throw new InvalidDataException("Invalid PCM sample width."),
                };
            sum += double.IsFinite(value) ? value : 0;
        }
        return sum / source.Channels;
    }

    private static short ToPcm(double value) => (short)Math.Clamp(Math.Round(value * 32768), short.MinValue, short.MaxValue);
}
