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

            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Left for cleanup on next session open (spec section 8).
                }
            }
        }
    }
}
