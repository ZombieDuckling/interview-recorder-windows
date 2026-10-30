using Recorder.Core.Diagnostics;

namespace Recorder.Audio.Windows;

internal static class CaptureDiagnostics
{
    private static readonly object Gate = new();

    // Only typed numeric values and validated session-relative paths enter this log, never exceptions/device names.
    internal static void Write(string directory, ProcessingLogCode code, params ProcessingLogValue[] values)
    {
        lock (Gate)
        {
            try
            {
                using var log = new ProcessingLogWriter(Path.Combine(directory, "processing.log"));
                log.Info(code, values);
            }
            catch (IOException) { /* Disk-full faults must still reach the UI when the log cannot be written. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
