using System.Buffers.Binary;

namespace Recorder.Transcription;

/// <summary>Seekable RIFF PCM reader. Only one bounded inference window is decoded.</summary>
internal sealed class PcmWindowReader : IDisposable
{
    internal const int SampleRate = 16000;
    private readonly FileStream stream;
    private readonly long dataOffset;
    private readonly byte[] scratch = new byte[8192];

    public long FrameCount { get; }

    public PcmWindowReader(string path)
    {
        stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> header = stackalloc byte[16];
            stream.ReadExactly(header[..12]);
            if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
                throw InvalidWave();
            var riffEnd = 8L + BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
            if (riffEnd > stream.Length || riffEnd < 12) throw InvalidWave();
            var hasFormat = false;
            var hasData = false;
            while (stream.Position + 8 <= riffEnd)
            {
                stream.ReadExactly(header[..8]);
                var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
                var next = stream.Position + size + (size & 1);
                if (next > riffEnd) throw InvalidWave();
                if (header[..4].SequenceEqual("fmt "u8))
                {
                    if (hasFormat || size < 16) throw InvalidWave();
                    stream.ReadExactly(header);
                    if (BinaryPrimitives.ReadUInt16LittleEndian(header) != 1 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(header[2..]) != 1 ||
                        BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) != SampleRate ||
                        BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != SampleRate * 2 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(header[12..]) != 2 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(header[14..]) != 16)
                        throw InvalidWave();
                    hasFormat = true;
                }
                else if (header[..4].SequenceEqual("data"u8))
                {
                    if (hasData || size % 2 != 0) throw InvalidWave();
                    dataOffset = stream.Position;
                    FrameCount = size / 2;
                    hasData = true;
                }
                stream.Position = next;
            }
            if (!hasFormat || !hasData || FrameCount == 0) throw InvalidWave();
        }
        catch (EndOfStreamException)
        {
            stream.Dispose();
            throw InvalidWave();
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public int Read(long startFrame, Span<float> samples)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startFrame);
        if (startFrame >= FrameCount) return 0;
        var count = (int)Math.Min(samples.Length, FrameCount - startFrame);
        stream.Position = dataOffset + startFrame * 2;
        for (var copied = 0; copied < count;)
        {
            var batch = Math.Min(scratch.Length / 2, count - copied);
            stream.ReadExactly(scratch.AsSpan(0, batch * 2));
            for (var index = 0; index < batch; index++)
                samples[copied + index] = BinaryPrimitives.ReadInt16LittleEndian(scratch.AsSpan(index * 2, 2)) / 32768f;
            copied += batch;
        }
        return count;
    }

    public void Dispose() => stream.Dispose();

    private static InvalidDataException InvalidWave() =>
        new("The transcription audio must be a complete 16 kHz mono 16-bit PCM WAV file. Convert the recording again.");
}
