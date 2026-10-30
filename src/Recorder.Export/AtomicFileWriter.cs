namespace Recorder.Export;

internal static class AtomicFileWriter
{
    public static async Task WriteAsync(string targetPath, Func<string, Task> writeToTempAsync, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var directory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Target path must include a directory.", nameof(targetPath));
        }

        Directory.CreateDirectory(directory);
        var tempName = $".{Path.GetFileName(targetPath)}.tmp-{Guid.NewGuid():N}";
        var tempPath = Path.Combine(directory, tempName);

        try
        {
            await writeToTempAsync(tempPath).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Durable before visible (spec section 8): flush the temp file to disk, then replace.
            using (var flush = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                flush.Flush(flushToDisk: true);
            }

            try
            {
                if (File.Exists(targetPath))
                {
                    File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tempPath, targetPath);
                }
            }
            catch (IOException ex) when (File.Exists(targetPath))
            {
                throw new IOException($"{Path.GetFileName(targetPath)} is open in another program (for example Word). Close it and try again.", ex);
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
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
    }
}
