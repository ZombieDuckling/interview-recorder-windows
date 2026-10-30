using System.Security.Cryptography;
using Recorder.Core.Abstractions;

namespace Recorder.Security.Tests;

public sealed class AesGcmStreamingTests
{
    private readonly FileProtector _protector = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64 * 1024)]
    [InlineData(64 * 1024 + 1)]
    [InlineData(10 * 1024 * 1024)]
    public async Task RoundTrip_PreservesPlaintext(int size)
    {
        var sessionDir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);

        var expected = GenerateDeterministicPayload(size);
        var encryptedPath = Path.Combine(sessionDir, "payload.bin");

        await WritePlaintextAsync(sessionDir, encryptedPath, expected);
        var actual = await ReadPlaintextAsync(sessionDir, encryptedPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task FlippedCiphertextBit_ThrowsOnRead()
    {
        var sessionDir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);
        var encryptedPath = Path.Combine(sessionDir, "payload.bin");
        await WritePlaintextAsync(sessionDir, encryptedPath, [1, 2, 3, 4]);

        FlipByte(encryptedPath, fileOffset: 20);

        await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
            await ReadPlaintextAsync(sessionDir, encryptedPath));
    }

    [Fact]
    public async Task TruncatedFile_ThrowsOnRead()
    {
        var sessionDir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);
        var encryptedPath = Path.Combine(sessionDir, "payload.bin");
        await WritePlaintextAsync(sessionDir, encryptedPath, new byte[128]);

        TruncateFile(encryptedPath, bytesToKeep: 32);

        await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
            await ReadPlaintextAsync(sessionDir, encryptedPath));
    }

    [Fact]
    public async Task ReorderedFrames_ThrowsOnRead()
    {
        var sessionDir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);
        var encryptedPath = Path.Combine(sessionDir, "payload.bin");
        var payload = new byte[CryptoConstants.FramePlaintextBytes + 16];
        RandomNumberGenerator.Fill(payload);
        await WritePlaintextAsync(sessionDir, encryptedPath, payload);

        SwapFirstTwoFrames(encryptedPath);

        await Assert.ThrowsAnyAsync<CryptographicException>(async () =>
            await ReadPlaintextAsync(sessionDir, encryptedPath));
    }

    [Fact]
    public async Task ComputeMac_IsStableLowercaseHex()
    {
        var sessionDir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);
        var path = Path.Combine(sessionDir, "mac-test.bin");
        await File.WriteAllBytesAsync(path, [9, 8, 7]);

        var mac1 = await _protector.ComputeMacAsync(sessionDir, path, CancellationToken.None);
        var mac2 = await _protector.ComputeMacAsync(sessionDir, path, CancellationToken.None);

        Assert.Equal(mac1, mac2);
        Assert.Matches("^[0-9a-f]{64}$", mac1);
    }

    private async Task WritePlaintextAsync(string sessionDir, string encryptedPath, byte[] plaintext)
    {
        await using var stream = _protector.OpenEncryptedWrite(sessionDir, encryptedPath);
        var offset = 0;
        var chunk = new byte[8192];
        while (offset < plaintext.Length)
        {
            var take = Math.Min(chunk.Length, plaintext.Length - offset);
            await stream.WriteAsync(plaintext.AsMemory(offset, take));
            offset += take;
        }
    }

    private async Task<byte[]> ReadPlaintextAsync(string sessionDir, string encryptedPath)
    {
        await using var stream = _protector.OpenDecryptedRead(sessionDir, encryptedPath);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static byte[] GenerateDeterministicPayload(int size)
    {
        var payload = new byte[size];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        return payload;
    }

    private static string CreateSessionDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "recorder-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void FlipByte(string path, long fileOffset)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = fileOffset;
        var b = stream.ReadByte();
        if (b < 0)
        {
            throw new InvalidOperationException("Offset out of range.");
        }

        stream.Position = fileOffset;
        stream.WriteByte((byte)(b ^ 0x01));
    }

    private static void TruncateFile(string path, long bytesToKeep)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(bytesToKeep);
    }

    private static void SwapFirstTwoFrames(string path)
    {
        var bytes = File.ReadAllBytes(path);
        const int headerBytes = 8;
        if (bytes.Length <= headerBytes)
        {
            throw new InvalidOperationException("File too small.");
        }

        var offset = headerBytes;
        var frame0 = ReadFrame(bytes, ref offset);
        var frame1 = ReadFrame(bytes, ref offset);
        if (frame1.Length == 0)
        {
            throw new InvalidOperationException("Need at least two frames.");
        }

        var rebuilt = new byte[headerBytes + frame1.Length + frame0.Length + (bytes.Length - headerBytes - frame0.Length - frame1.Length)];
        bytes.AsSpan(0, headerBytes).CopyTo(rebuilt);
        frame1.CopyTo(rebuilt.AsSpan(headerBytes));
        frame0.CopyTo(rebuilt.AsSpan(headerBytes + frame1.Length));
        bytes.AsSpan(headerBytes + frame0.Length + frame1.Length).CopyTo(rebuilt.AsSpan(headerBytes + frame0.Length + frame1.Length));

        File.WriteAllBytes(path, rebuilt);
    }

    private static byte[] ReadFrame(byte[] file, ref int offset)
    {
        if (offset + 5 > file.Length)
        {
            throw new InvalidOperationException("Invalid frame header.");
        }

        var plaintextLength = BitConverter.ToUInt32(file, offset);
        var frameStart = offset;
        offset += 5;
        offset += (int)plaintextLength;
        offset += CryptoConstants.GcmTagBytes;
        return file[frameStart..offset];
    }
}
