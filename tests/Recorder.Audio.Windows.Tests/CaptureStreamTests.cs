using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows.Tests;

public sealed class CaptureStreamTests
{
    [Fact]
    public async Task StopWritesTwoSilentStreamsToTheSameFrameWithoutChangingState()
    {
        using var folder = new TestFolder();
        var manifest = new CaptureManifest(folder.Path);
        using var meeting = new CaptureStream(folder.Path, StreamRole.Meeting, manifest, () => 0, () => long.MaxValue, _ => { }, _ => { });
        using var mic = new CaptureStream(folder.Path, StreamRole.Microphone, manifest, () => 0, () => long.MaxValue, _ => { }, _ => { });
        var streams = await Task.WhenAll(meeting.StopAsync(492000), mic.StopAsync(492000));
        Assert.All(streams, s =>
        {
            Assert.Equal(TimeSpan.FromSeconds(10.25), s.Duration);
            Assert.Equal(TimeSpan.Zero, s.StartOffset);
            Assert.Equal(2, s.ChunkPaths.Count);
            Assert.All(s.ChunkPaths, path => Assert.True(File.Exists(path)));
        });
        Assert.Equal("Recording", folder.Manifest["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task DiskReserveFaultStopsTheWriterAndLeavesIncompleteResult()
    {
        using var folder = new TestFolder();
        var fault = new TaskCompletionSource<CaptureFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new CaptureStream(folder.Path, StreamRole.Meeting, new CaptureManifest(folder.Path),
            () => 48000, () => 0, _ => { }, f => fault.TrySetResult(f));
        var observed = await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stream.StopAsync(48000);
        Assert.Equal(CaptureFaultKind.DiskLow, observed.Kind);
        Assert.False(observed.Recovered);
        Assert.False(stream.Complete);
    }

    [Fact]
    public async Task OverflowIsReportedAndMissingFramesRemainOnTimeline()
    {
        using var folder = new TestFolder();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var faults = new List<CaptureFault>();
        using var stream = new CaptureStream(folder.Path, StreamRole.Meeting, new CaptureManifest(folder.Path), () => 0,
            () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); return long.MaxValue; }, _ => { }, faults.Add);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            for (var i = 0; i < 80; i++) stream.Accept(i * 4800, new short[4800]);
        }
        finally { release.Set(); }
        var result = await stream.StopAsync(384000);
        Assert.False(stream.Complete);
        Assert.Contains(faults, f => f.Kind == CaptureFaultKind.WriteFailed && f.Recovered);
        Assert.Equal(TimeSpan.FromSeconds(8), result.Duration);
    }

    [Fact]
    public async Task DiskFullIsClassifiedAndDoesNotEscapeWriterThread()
    {
        using var folder = new TestFolder();
        var fault = new TaskCompletionSource<CaptureFault>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new CaptureStream(folder.Path, StreamRole.Microphone, new CaptureManifest(folder.Path), () => 48000,
            () => throw new IOException("PRIVATE_PATH_MUST_NOT_BE_LOGGED", unchecked((int)0x80070070)), _ => { }, f => fault.TrySetResult(f));
        Assert.Equal(CaptureFaultKind.DiskFull, (await fault.Task.WaitAsync(TimeSpan.FromSeconds(5))).Kind);
        await stream.StopAsync(48000);
        Assert.DoesNotContain("PRIVATE_PATH", File.ReadAllText(System.IO.Path.Combine(folder.Path, "processing.log")), StringComparison.Ordinal);
    }
}

public sealed class PartialFailureTests
{
    [Fact]
    public async Task FailedOpenTailIsNotCountedInReturnedClosedAudioDuration()
    {
        using var folder = new TestFolder();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        long now = 0;
        var probes = 0;
        var fault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stream = new CaptureStream(folder.Path, StreamRole.Meeting, new CaptureManifest(folder.Path), () => Interlocked.Read(ref now),
            () =>
            {
                if (Interlocked.Increment(ref probes) == 1)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                    return long.MaxValue;
                }
                return 0;
            }, _ => { }, _ => fault.TrySetResult());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        stream.Accept(0, new short[4800]);
        Interlocked.Exchange(ref now, 48000);
        release.Set();
        await fault.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await stream.StopAsync(48000);
        Assert.Empty(result.ChunkPaths);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.NotNull(await new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None));
    }
}
