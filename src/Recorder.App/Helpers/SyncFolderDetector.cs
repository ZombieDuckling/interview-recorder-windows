using Recorder.Core.Settings;

namespace Recorder.App.Helpers;

internal static class SyncFolderDetector
{
    public static bool IsLikelySyncFolder(string path) => SyncFolderDetection.IsKnownSyncFolder(path);
}
