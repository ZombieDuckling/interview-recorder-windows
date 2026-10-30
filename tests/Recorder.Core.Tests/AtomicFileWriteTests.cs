using Recorder.Core.IO;

namespace Recorder.Core.Tests;

public sealed class AtomicFileWriteTests
{
    [Fact]
    public async Task InterruptedWrite_LeavesOriginalFileIntact()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ir-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "session.json");
        try
        {
            await AtomicFileWrite.WriteAllTextAsync(target, "original-content");
            AtomicFileWrite.TestBytesWrittenHook = bytes =>
            {
                if (bytes > 4)
                {
                    throw new IOException("simulated crash mid-write");
                }
            };

            await Assert.ThrowsAsync<IOException>(() =>
                AtomicFileWrite.WriteAllTextAsync(target, new string('x', 256)));

            Assert.Equal("original-content", await File.ReadAllTextAsync(target));
            var tempLeftovers = Directory.GetFiles(directory, ".session.json.tmp-*");
            Assert.Empty(tempLeftovers);
        }
        finally
        {
            AtomicFileWrite.TestBytesWrittenHook = null;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadSession_InvalidJson_ThrowsCorruptedFileException()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ir-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "session.json");
        try
        {
            File.WriteAllText(target, "{ not json");
            var ex = Assert.Throws<CorruptedFileException>(() => SessionManifestIO.Read(target));
            Assert.Equal(target, ex.FilePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
