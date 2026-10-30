namespace Recorder.App.Helpers;

internal static class SyncFolderDetector
{
    private static readonly string[] Markers =
    [
        "OneDrive",
        "iCloudDrive",
        "Dropbox",
        "Google Drive",
    ];

    public static bool IsLikelySyncFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            foreach (var marker in Markers)
            {
                if (full.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }
}
