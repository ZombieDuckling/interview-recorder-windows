using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

internal sealed class CaptureManifest(string directory)
{
    internal void Begin(DateTimeOffset startedAt) => throw new NotImplementedException(directory);
    internal void RegisterStream(StreamRole role, long offset, bool processLoopback) => throw new NotImplementedException();
    internal void CloseChunk(StreamRole role, ClosedChunk chunk) => throw new NotImplementedException();
}
