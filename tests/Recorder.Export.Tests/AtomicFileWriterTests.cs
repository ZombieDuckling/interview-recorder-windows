using Recorder.Export;

namespace Recorder.Export.Tests;

public sealed class AtomicFileWriterTests
{
    [Fact]
    public async Task WriteAsync_when_writer_throws_leaves_existing_target_unchanged_and_no_partial_target()
    {
        var dir = Path.Combine(Path.GetTempPath(), "recorder-export-atomic", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "interview_transcript.docx");
        await File.WriteAllTextAsync(target, "original-content");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AtomicFileWriter.WriteAsync(
                target,
                _ => throw new InvalidOperationException("simulated failure"),
                CancellationToken.None));

        Assert.Equal("original-content", await File.ReadAllTextAsync(target));
        var tempLeftovers = Directory.GetFiles(dir, ".*.tmp-*");
        Assert.Empty(tempLeftovers);
    }
}
