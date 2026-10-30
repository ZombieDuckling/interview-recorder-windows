namespace Recorder.Audio.Windows;

/// <summary>session.json stores chunk paths with '/' (spec); on Windows they must be normalised before any comparison or file use.</summary>
internal static class SessionPaths
{
    public static string Resolve(string root, string relative) =>
        Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    public static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
