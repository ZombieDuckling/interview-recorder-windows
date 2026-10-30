using System.Buffers.Binary;

namespace Recorder.Media;

/// <summary>Reads only the RIFF header of a capture chunk; audio is never loaded.</summary>
internal static class WaveInfo
{
    internal sealed record Header(int SampleRate, int Channels, long Frames);

    internal static Header Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> head = stackalloc byte[16];
        try
        {
            stream.ReadExactly(head[..12]);
            if (!head[..4].SequenceEqual("RIFF"u8) || !head[8..12].SequenceEqual("WAVE"u8)) throw Invalid();
            int rate = 0, channels = 0, blockAlign = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                stream.ReadExactly(head[..8]);
                long size = BinaryPrimitives.ReadUInt32LittleEndian(head[4..8]);
                bool isData = head[..4].SequenceEqual("data"u8);
                if (head[..4].SequenceEqual("fmt "u8) && size >= 16)
                {
                    stream.ReadExactly(head);
                    ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(head);
                    if (tag != 1 && tag != 3 && tag != 0xFFFE) throw Invalid();
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(head[2..]);
                    rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
                    blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(head[12..]);
                    stream.Position += size - 16;
                }
                else if (isData)
                {
                    if (rate <= 0 || channels <= 0 || blockAlign <= 0) throw Invalid();
                    // A crashed writer may leave a stale size field: trust the bytes actually present.
                    long available = stream.Length - stream.Position;
                    long bytes = size == 0 || size > available ? available : size;
                    return new Header(rate, channels, bytes / blockAlign);
                }
                else stream.Position += size;
                if ((size & 1) == 1) stream.Position++;
            }
        }
        catch (EndOfStreamException)
        {
        }
        throw Invalid();
    }

    private static InvalidDataException Invalid() =>
        new("A recorded audio file is not a readable WAV file in the expected capture format.");
}
