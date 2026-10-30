using System.Text;

namespace Recorder.Core.IO;

/// <summary>Atomic UTF-8 writes per shared spec section 8 (temp file, flush, replace).</summary>
public static class AtomicFileWrite
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static Action<int>? TestBytesWrittenHook { get; set; }

    public static void WriteAllText(string targetPath, ReadOnlySpan<char> contents) =>
        WriteAllTextAsync(targetPath, contents.ToString(), CancellationToken.None).GetAwaiter().GetResult();

    public static async Task WriteAllTextAsync(string targetPath, string contents, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        var directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Target path must include a directory.", nameof(targetPath));
        }

        Directory.CreateDirectory(directory);
        var fileName = Path.GetFileName(targetPath);
        var tempPath = Path.Combine(directory, $".{fileName}.tmp-{Guid.NewGuid():N}");

        try
        {
            await WriteTempFileAsync(tempPath, contents, cancellationToken).ConfigureAwait(false);
            ReplaceExisting(tempPath, targetPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static async Task WriteTempFileAsync(string tempPath, string contents, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.WriteThrough);

        await using var writer = new StreamWriter(stream, Utf8NoBom);
        await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
        TestBytesWrittenHook?.Invoke(contents.Length);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceExisting(string tempPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            if (OperatingSystem.IsWindows())
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }

            File.Move(tempPath, targetPath, overwrite: true);
            return;
        }

        File.Move(tempPath, targetPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
