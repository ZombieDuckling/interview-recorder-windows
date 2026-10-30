// OWNER: win-lead. Turns session.json stream entries into absolute chunk paths (spec section 3: chunk "file" is session-relative, '/'-separated).
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Core.SessionManagement;

public static class CaptureStreams
{
    public static IReadOnlyList<StreamFiles> FromManifest(string sessionDirectory, SessionManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var result = new List<StreamFiles>();
        foreach (SessionStreamInfo s in manifest.Streams)
        {
            if (s.Chunks.Count == 0)
            {
                continue;
            }

            string folder = s.Kind == StreamJsonKind.Mic ? "mic" : "meeting";
            var chunks = s.Chunks
                .OrderBy(c => c.File, StringComparer.Ordinal)
                .Select(c => Path.GetFullPath(Path.Combine(sessionDirectory, c.File.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();
            long frames = s.Chunks.Sum(c => (long)c.Frames);
            int rate = s.SampleRate > 0 ? s.SampleRate : 48000;
            result.Add(new StreamFiles(
                s.Kind == StreamJsonKind.Mic ? StreamRole.Microphone : StreamRole.Meeting,
                Path.Combine(sessionDirectory, "capture", folder),
                chunks,
                rate,
                s.Channels > 0 ? s.Channels : 1,
                TimeSpan.FromSeconds((double)frames / rate),
                TimeSpan.FromSeconds((double)s.FirstFrameOffset / rate),
                s.Source == StreamCaptureSource.WasapiProcessLoopback));
        }

        return result;
    }
}
