using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Recorder.Security;

internal static class FrameAad
{
    internal static byte[] Build(ulong frameIndex, bool isFinal)
    {
        var aad = new byte[CryptoConstants.FrameIndexBytes + 1];
        BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(0, 8), frameIndex);
        aad[8] = isFinal ? (byte)1 : (byte)0;
        return aad;
    }
}

internal static class FrameNonce
{
    internal static byte[] Build(ReadOnlySpan<byte> fileNoncePrefix, ulong frameIndex)
    {
        if (fileNoncePrefix.Length != CryptoConstants.FileNoncePrefixBytes)
        {
            throw new ArgumentException("Invalid file nonce prefix length.", nameof(fileNoncePrefix));
        }

        var nonce = new byte[CryptoConstants.GcmNonceBytes];
        fileNoncePrefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(CryptoConstants.FileNoncePrefixBytes), frameIndex);
        return nonce;
    }
}

internal sealed class EncryptedFrameWriteStream : Stream
{
    private readonly FileStream _file;
    private readonly byte[] _sessionKey;
    private readonly byte[] _fileNoncePrefix;
    private readonly byte[] _plaintextBuffer;
    private int _bufferFill;
    private ulong _nextFrameIndex;
    private bool _finalFrameWritten;
    private bool _disposed;

    public EncryptedFrameWriteStream(string path, byte[] sessionKey)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        if (sessionKey.Length != CryptoConstants.SessionKeyBytes)
        {
            throw new ArgumentException("Invalid session key length.", nameof(sessionKey));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _sessionKey = sessionKey;
        _file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.SequentialScan);
        _fileNoncePrefix = RandomNumberGenerator.GetBytes(CryptoConstants.FileNoncePrefixBytes);
        _plaintextBuffer = new byte[CryptoConstants.FramePlaintextBytes];

        _file.Write(CryptoConstants.EncryptedFileMagic);
        _file.Write(_fileNoncePrefix);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => _file.Flush(flushToDisk: true);

    public override Task FlushAsync(CancellationToken cancellationToken) => _file.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finalFrameWritten)
        {
            throw new InvalidOperationException("Cannot write after the final frame was sealed.");
        }

        var remaining = buffer;
        while (!remaining.IsEmpty)
        {
            var space = _plaintextBuffer.Length - _bufferFill;
            var take = Math.Min(space, remaining.Length);
            remaining.Slice(0, take).CopyTo(_plaintextBuffer.AsSpan(_bufferFill));
            _bufferFill += take;
            remaining = remaining.Slice(take);

            if (_bufferFill == _plaintextBuffer.Length)
            {
                WriteFrame(_plaintextBuffer.AsSpan(0, _bufferFill), isFinal: false);
                _bufferFill = 0;
            }
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        await WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finalFrameWritten)
        {
            throw new InvalidOperationException("Cannot write after the final frame was sealed.");
        }

        var remaining = buffer;
        while (!remaining.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var space = _plaintextBuffer.Length - _bufferFill;
            var take = Math.Min(space, remaining.Length);
            remaining.Slice(0, take).CopyTo(_plaintextBuffer.AsMemory(_bufferFill));
            _bufferFill += take;
            remaining = remaining.Slice(take);

            if (_bufferFill == _plaintextBuffer.Length)
            {
                await WriteFrameAsync(_plaintextBuffer.AsMemory(0, _bufferFill), isFinal: false, cancellationToken)
                    .ConfigureAwait(false);
                _bufferFill = 0;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            if (!_finalFrameWritten)
            {
                WriteFrame(_plaintextBuffer.AsSpan(0, _bufferFill), isFinal: true);
            }

            _file.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    private void WriteFrame(ReadOnlySpan<byte> plaintext, bool isFinal)
    {
        var frameIndex = _nextFrameIndex++;
        var nonce = FrameNonce.Build(_fileNoncePrefix, frameIndex);
        var aad = FrameAad.Build(frameIndex, isFinal);
        var ciphertext = plaintext.Length == 0 ? [] : new byte[plaintext.Length];
        var tag = new byte[CryptoConstants.GcmTagBytes];

        using (var gcm = new AesGcm(_sessionKey, CryptoConstants.GcmTagBytes))
        {
            gcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        Span<byte> header = stackalloc byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)plaintext.Length);
        header[4] = isFinal ? (byte)1 : (byte)0;
        _file.Write(header);
        if (ciphertext.Length > 0)
        {
            _file.Write(ciphertext);
        }

        _file.Write(tag);

        if (isFinal)
        {
            _finalFrameWritten = true;
        }
    }

    private async Task WriteFrameAsync(ReadOnlyMemory<byte> plaintext, bool isFinal, CancellationToken cancellationToken)
    {
        var frameIndex = _nextFrameIndex++;
        var nonce = FrameNonce.Build(_fileNoncePrefix, frameIndex);
        var aad = FrameAad.Build(frameIndex, isFinal);
        var ciphertext = plaintext.Length == 0 ? [] : new byte[plaintext.Length];
        var tag = new byte[CryptoConstants.GcmTagBytes];

        using (var gcm = new AesGcm(_sessionKey, CryptoConstants.GcmTagBytes))
        {
            gcm.Encrypt(nonce, plaintext.Span, ciphertext, tag, aad);
        }

        var headerBytes = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(headerBytes.AsSpan(0, 4), (uint)plaintext.Length);
        headerBytes[4] = isFinal ? (byte)1 : (byte)0;
        await _file.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        if (ciphertext.Length > 0)
        {
            await _file.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
        }

        await _file.WriteAsync(tag, cancellationToken).ConfigureAwait(false);

        if (isFinal)
        {
            _finalFrameWritten = true;
        }
    }
}

internal sealed class EncryptedFrameReadStream : Stream
{
    private readonly FileStream _file;
    private readonly byte[] _sessionKey;
    private readonly byte[] _fileNoncePrefix;
    private readonly byte[] _framePlaintext;
    private int _framePlaintextOffset;
    private int _framePlaintextCount;
    private ulong _nextFrameIndex;
    private bool _finalFrameSeen;
    private bool _disposed;
    private bool _eof;

    public EncryptedFrameReadStream(string path, byte[] sessionKey)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        if (sessionKey.Length != CryptoConstants.SessionKeyBytes)
        {
            throw new ArgumentException("Invalid session key length.", nameof(sessionKey));
        }

        _sessionKey = sessionKey;
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan);
        _framePlaintext = new byte[CryptoConstants.FramePlaintextBytes];
        _fileNoncePrefix = new byte[CryptoConstants.FileNoncePrefixBytes];
        ReadHeader();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || _eof)
        {
            return 0;
        }

        var total = 0;
        while (!buffer.IsEmpty)
        {
            if (_framePlaintextOffset >= _framePlaintextCount)
            {
                if (!LoadNextFrame())
                {
                    _eof = true;
                    break;
                }
            }

            var available = _framePlaintextCount - _framePlaintextOffset;
            var take = Math.Min(available, buffer.Length);
            _framePlaintext.AsSpan(_framePlaintextOffset, take).CopyTo(buffer);
            _framePlaintextOffset += take;
            buffer = buffer[take..];
            total += take;
        }

        return total;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || _eof)
        {
            return 0;
        }

        var total = 0;
        while (!buffer.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_framePlaintextOffset >= _framePlaintextCount)
            {
                if (!await LoadNextFrameAsync(cancellationToken).ConfigureAwait(false))
                {
                    _eof = true;
                    break;
                }
            }

            var available = _framePlaintextCount - _framePlaintextOffset;
            var take = Math.Min(available, buffer.Length);
            _framePlaintext.AsMemory(_framePlaintextOffset, take).CopyTo(buffer);
            _framePlaintextOffset += take;
            buffer = buffer[take..];
            total += take;
        }

        return total;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _file.Dispose();
        }

        _disposed = true;
        base.Dispose(disposing);
    }

    private void ReadHeader()
    {
        Span<byte> magic = stackalloc byte[4];
        ReadExactly(_file, magic);
        if (!magic.SequenceEqual(CryptoConstants.EncryptedFileMagic))
        {
            throw new CryptographicException("Invalid encrypted file header.");
        }

        ReadExactly(_file, _fileNoncePrefix);
    }

    private bool LoadNextFrame()
    {
        if (_finalFrameSeen)
        {
            EnsureNoTrailingBytes();
            return false;
        }

        if (_file.Position == _file.Length)
        {
            throw new CryptographicException("Encrypted file missing final frame.");
        }

        var frameIndex = _nextFrameIndex;
        Span<byte> header = stackalloc byte[5];
        ReadExactly(_file, header);
        var plaintextLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var isFinal = header[4] == 1;
        if (plaintextLength > CryptoConstants.FramePlaintextBytes)
        {
            throw new CryptographicException("Invalid frame length.");
        }

        var ciphertext = plaintextLength == 0 ? [] : new byte[plaintextLength];
        if (plaintextLength > 0)
        {
            ReadExactly(_file, ciphertext);
        }

        var tag = new byte[CryptoConstants.GcmTagBytes];
        ReadExactly(_file, tag);

        var nonce = FrameNonce.Build(_fileNoncePrefix, frameIndex);
        var aad = FrameAad.Build(frameIndex, isFinal);
        using (var gcm = new AesGcm(_sessionKey, CryptoConstants.GcmTagBytes))
        {
            gcm.Decrypt(nonce, ciphertext, tag, _framePlaintext.AsSpan(0, (int)plaintextLength), aad);
        }

        _framePlaintextOffset = 0;
        _framePlaintextCount = (int)plaintextLength;
        _nextFrameIndex++;

        if (isFinal)
        {
            _finalFrameSeen = true;
            if (_file.Position < _file.Length)
            {
                throw new CryptographicException("Encrypted file has trailing data after the final frame.");
            }
        }

        return true;
    }

    private async Task<bool> LoadNextFrameAsync(CancellationToken cancellationToken)
    {
        if (_finalFrameSeen)
        {
            await EnsureNoTrailingBytesAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (_file.Position == _file.Length)
        {
            throw new CryptographicException("Encrypted file missing final frame.");
        }

        var frameIndex = _nextFrameIndex;
        var header = new byte[5];
        await ReadExactlyAsync(_file, header, cancellationToken).ConfigureAwait(false);
        var plaintextLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var isFinal = header[4] == 1;
        if (plaintextLength > CryptoConstants.FramePlaintextBytes)
        {
            throw new CryptographicException("Invalid frame length.");
        }

        var ciphertext = plaintextLength == 0 ? [] : new byte[plaintextLength];
        if (plaintextLength > 0)
        {
            await ReadExactlyAsync(_file, ciphertext, cancellationToken).ConfigureAwait(false);
        }

        var tag = new byte[CryptoConstants.GcmTagBytes];
        await ReadExactlyAsync(_file, tag, cancellationToken).ConfigureAwait(false);

        var nonce = FrameNonce.Build(_fileNoncePrefix, frameIndex);
        var aad = FrameAad.Build(frameIndex, isFinal);
        using (var gcm = new AesGcm(_sessionKey, CryptoConstants.GcmTagBytes))
        {
            gcm.Decrypt(nonce, ciphertext, tag, _framePlaintext.AsSpan(0, (int)plaintextLength), aad);
        }

        _framePlaintextOffset = 0;
        _framePlaintextCount = (int)plaintextLength;
        _nextFrameIndex++;

        if (isFinal)
        {
            _finalFrameSeen = true;
            if (_file.Position < _file.Length)
            {
                throw new CryptographicException("Encrypted file has trailing data after the final frame.");
            }
        }

        return true;
    }

    private void EnsureNoTrailingBytes()
    {
        if (_file.Position != _file.Length)
        {
            throw new CryptographicException("Encrypted file has unexpected trailing data.");
        }
    }

    private async Task EnsureNoTrailingBytesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_file.Position != _file.Length)
        {
            throw new CryptographicException("Encrypted file has unexpected trailing data.");
        }

        await Task.CompletedTask;
    }

    private static void ReadExactly(FileStream stream, Span<byte> buffer)
    {
        if (!TryReadExactly(stream, buffer))
        {
            throw new CryptographicException("Truncated encrypted file.");
        }
    }

    private static bool TryReadExactly(FileStream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer[read..]);
            if (n == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new CryptographicException("Truncated encrypted file.");
            }

            read += n;
        }

        return true;
    }

    private static async Task ReadExactlyAsync(FileStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                throw new CryptographicException("Truncated encrypted file.");
            }

            read += n;
        }
    }
}
