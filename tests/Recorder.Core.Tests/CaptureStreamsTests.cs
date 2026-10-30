using Recorder.Core.Formats;
using Recorder.Core.IO;
using Recorder.Core.SessionManagement;

namespace Recorder.Core.Tests;

public sealed class CaptureStreamsTests
{
    [Fact]
    public void Chunk_paths_from_the_spec_example_resolve_inside_the_session_folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "IR-20261031-091500-7F3A");
        SessionManifest manifest = SessionManifestIO.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "session.example.json"));
        var streams = CaptureStreams.FromManifest(dir, manifest);

        Assert.NotEmpty(streams);
        foreach (var stream in streams)
        {
            Assert.NotEmpty(stream.ChunkPaths);
            foreach (string path in stream.ChunkPaths)
            {
                // regression: the relative "capture/<kind>/NNNNNN.wav" must not be joined under capture/<kind>/ again
                Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(Path.Combine(path, "..", "..", "..")));
                Assert.DoesNotContain("capture" + Path.DirectorySeparatorChar + "capture", path, StringComparison.Ordinal);
                if (Path.DirectorySeparatorChar == '\\') Assert.DoesNotContain('/', path);
            }
        }
    }
}
