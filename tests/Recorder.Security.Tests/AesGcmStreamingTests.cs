using System.Security.Cryptography;
using Recorder.Core.Abstractions;

namespace Recorder.Security.Tests;

public sealed class AesGcmStreamingTests
{
    private const int HeaderBytes = 4 + CryptoConstants.FileSaltBytes;
    private const int FrameOverhead = 5 + CryptoConstants.GcmTagBytes;

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

        FlipByte(encryptedPath, fileOffset: HeaderBytes + 6);

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

    [Fact]
    public async Task ComputeMac_DifferentKeys_DifferentMac()
    {
        var dirA = CreateSessionDirectory();
        var dirB = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dirA, CancellationToken.None);
        await _protector.EnsureSessionKeyAsync(dirB, CancellationToken.None);
        var pathA = Path.Combine(dirA, "m.bin");
        var pathB = Path.Combine(dirB, "m.bin");
        await File.WriteAllBytesAsync(pathA, [1, 2, 3]);
        await File.WriteAllBytesAsync(pathB, [1, 2, 3]);

        Assert.NotEqual(
            await _protector.ComputeMacAsync(dirA, pathA, CancellationToken.None),
            await _protector.ComputeMacAsync(dirB, pathB, CancellationToken.None));
    }

    [Fact]
    public async Task VerifyMac_AcceptsGoodRejectsBadAndTampered()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "v.bin");
        await File.WriteAllBytesAsync(path, [5, 6, 7]);
        var mac = await _protector.ComputeMacAsync(dir, path, CancellationToken.None);

        Assert.True(await _protector.VerifyMacAsync(dir, path, mac, CancellationToken.None));
        Assert.True(await _protector.VerifyMacAsync(dir, path, mac.ToUpperInvariant(), CancellationToken.None));
        Assert.False(await _protector.VerifyMacAsync(dir, path, new string('0', 64), CancellationToken.None));
        Assert.False(await _protector.VerifyMacAsync(dir, path, "zz", CancellationToken.None));
        Assert.False(await _protector.VerifyMacAsync(dir, path, new string('g', 64), CancellationToken.None));

        FlipByte(path, 0);
        Assert.False(await _protector.VerifyMacAsync(dir, path, mac, CancellationToken.None));
    }

    [Fact]
    public async Task DroppedFinalFrame_ThrowsMissingFinalFrame()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");
        var payload = new byte[CryptoConstants.FramePlaintextBytes + 10];
        await WritePlaintextAsync(dir, path, payload);

        // frame 0 (full) + final frame (10 bytes). Keep only frame 0: truncation exactly at a frame boundary.
        TruncateFile(path, HeaderBytes + CryptoConstants.FramePlaintextBytes + FrameOverhead);

        var ex = await Assert.ThrowsAnyAsync<CryptographicException>(async () => await ReadPlaintextAsync(dir, path));
        Assert.Contains("final frame", ex.Message);
    }

    [Fact]
    public async Task BytesAppendedAfterFinalFrame_Throw()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");
        await WritePlaintextAsync(dir, path, [1, 2, 3]);
        await using (var f = new FileStream(path, FileMode.Append, FileAccess.Write))
        {
            f.WriteByte(0);
        }

        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await ReadPlaintextAsync(dir, path));
        using var sync = _protector.OpenDecryptedRead(dir, path);
        Assert.ThrowsAny<CryptographicException>(() => sync.ReadExactly(new byte[3]));
    }

    [Fact]
    public async Task FlippedIsFinalByte_Throws()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");
        await WritePlaintextAsync(dir, path, [1, 2, 3]);

        FlipByte(path, HeaderBytes + 4);

        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await ReadPlaintextAsync(dir, path));
    }

    [Fact]
    public async Task WrongKey_Throws()
    {
        var dirA = CreateSessionDirectory();
        var dirB = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dirA, CancellationToken.None);
        await _protector.EnsureSessionKeyAsync(dirB, CancellationToken.None);
        var pathA = Path.Combine(dirA, "p.bin");
        await WritePlaintextAsync(dirA, pathA, [1, 2, 3]);
        var pathB = Path.Combine(dirB, "p.bin");
        File.Copy(pathA, pathB);

        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await ReadPlaintextAsync(dirB, pathB));
    }

    [Fact]
    public async Task PersistedKey_ReloadedInNewProtector_Decrypts()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");
        var payload = GenerateDeterministicPayload(1000);
        await WritePlaintextAsync(dir, path, payload);

        var fresh = new FileProtector();
        await fresh.EnsureSessionKeyAsync(dir, CancellationToken.None);
        await using var stream = fresh.OpenDecryptedRead(dir, path);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public async Task ExactFrameMultiple_WritesEmptyFinalFrame()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");
        var payload = GenerateDeterministicPayload(2 * CryptoConstants.FramePlaintextBytes);
        await WritePlaintextAsync(dir, path, payload);

        Assert.Equal(
            HeaderBytes + 2 * (CryptoConstants.FramePlaintextBytes + FrameOverhead) + FrameOverhead,
            new FileInfo(path).Length);
        Assert.Equal(payload, await ReadPlaintextAsync(dir, path));
    }

    [Fact]
    public async Task ReadAndReadAsync_ReturnSameBytes()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");
        var payload = GenerateDeterministicPayload(CryptoConstants.FramePlaintextBytes * 2 + 123);
        await WritePlaintextAsync(dir, path, payload);

        using var sync = _protector.OpenDecryptedRead(dir, path);
        using var syncMs = new MemoryStream();
        var chunk = new byte[7000];
        int n;
        while ((n = sync.Read(chunk, 0, chunk.Length)) > 0)
        {
            syncMs.Write(chunk, 0, n);
        }

        Assert.Equal(payload, syncMs.ToArray());
        Assert.Equal(payload, await ReadPlaintextAsync(dir, path));
    }

    [Fact]
    public async Task SwappedFiles_FailBecauseContextIsInAad()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var mic = Path.Combine(dir, "mic.bin");
        var meeting = Path.Combine(dir, "meeting.bin");
        await WritePlaintextAsync(dir, mic, [1, 2, 3]);
        await WritePlaintextAsync(dir, meeting, [4, 5, 6]);
        File.Move(mic, mic + ".tmp");
        File.Move(meeting, mic);
        File.Move(mic + ".tmp", meeting);

        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await ReadPlaintextAsync(dir, mic));
    }

    [Fact]
    public async Task TwoFiles_HaveDifferentSaltsAndCiphertext()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var a = Path.Combine(dir, "a.bin");
        var b = Path.Combine(dir, "b.bin");
        var payload = GenerateDeterministicPayload(100);
        await WritePlaintextAsync(dir, a, payload);
        await WritePlaintextAsync(dir, b, payload);

        var bytesA = File.ReadAllBytes(a);
        var bytesB = File.ReadAllBytes(b);
        Assert.NotEqual(bytesA[4..HeaderBytes], bytesB[4..HeaderBytes]);
        Assert.NotEqual(bytesA[HeaderBytes..], bytesB[HeaderBytes..]);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("sub/../../outside.bin")]
    public async Task PathOutsideSessionFolder_IsRejected(string relative)
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);

        Assert.Throws<ArgumentException>(() => _protector.OpenEncryptedWrite(dir, relative));
        Assert.Throws<ArgumentException>(() => _protector.OpenDecryptedRead(dir, relative));
    }

    [Fact]
    public async Task AbsolutePathOutsideSessionFolder_IsRejected()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentException>(() => _protector.OpenEncryptedWrite(dir, outside));
        await Assert.ThrowsAsync<ArgumentException>(() => _protector.ComputeMacAsync(dir, outside, CancellationToken.None));
    }

    [Fact]
    public async Task SessionDirectoryKey_IsNormalised()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var withSlash = dir + Path.DirectorySeparatorChar;
        var path = Path.Combine(dir, "p.bin");

        await using (var w = _protector.OpenEncryptedWrite(withSlash, "p.bin"))
        {
            await w.WriteAsync(new byte[] { 1 });
        }

        Assert.Equal(new byte[] { 1 }, await ReadPlaintextAsync(dir, path));
    }

    [Fact]
    public async Task ParallelEnsureSessionKey_YieldsOneKey()
    {
        var dir = CreateSessionDirectory();
        var protectors = Enumerable.Range(0, 16).Select(_ => new FileProtector()).ToArray();
        await Task.WhenAll(protectors.Select(p => Task.Run(() => p.EnsureSessionKeyAsync(dir, CancellationToken.None))));

        var path = Path.Combine(dir, "p.bin");
        await using (var w = protectors[0].OpenEncryptedWrite(dir, path))
        {
            await w.WriteAsync(new byte[] { 9, 9 });
        }

        foreach (var p in protectors)
        {
            await using var r = p.OpenDecryptedRead(dir, path);
            using var ms = new MemoryStream();
            await r.CopyToAsync(ms);
            Assert.Equal(new byte[] { 9, 9 }, ms.ToArray());
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(dir, CryptoConstants.KeysFolderName), "*tmp*"));
    }

    [Fact]
    public async Task FailedWrite_DoesNotSealFile()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");

        var w = _protector.OpenEncryptedWrite(dir, path);
        using var cts = new CancellationTokenSource();
        await w.WriteAsync(new byte[CryptoConstants.FramePlaintextBytes], CancellationToken.None);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await w.WriteAsync(new byte[10], cts.Token));
        await Assert.ThrowsAsync<IOException>(async () => await w.WriteAsync(new byte[1]));
        await w.DisposeAsync();

        var ex = await Assert.ThrowsAnyAsync<CryptographicException>(async () => await ReadPlaintextAsync(dir, path));
        Assert.Contains("final frame", ex.Message);
    }

    [Fact]
    public async Task ReadStream_ReturnsVerifiedFramesBeforeThrowingOnTruncation()
    {
        var dir = CreateSessionDirectory();
        await _protector.EnsureSessionKeyAsync(dir, CancellationToken.None);
        var path = Path.Combine(dir, "p.bin");
        var payload = GenerateDeterministicPayload(CryptoConstants.FramePlaintextBytes + 5);
        await WritePlaintextAsync(dir, path, payload);
        TruncateFile(path, HeaderBytes + CryptoConstants.FramePlaintextBytes + FrameOverhead);

        await using var r = _protector.OpenDecryptedRead(dir, path);
        var first = new byte[CryptoConstants.FramePlaintextBytes];
        await r.ReadExactlyAsync(first);
        Assert.Equal(payload[..first.Length], first);
        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await r.ReadExactlyAsync(new byte[1]));
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
        const int headerBytes = HeaderBytes;
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
