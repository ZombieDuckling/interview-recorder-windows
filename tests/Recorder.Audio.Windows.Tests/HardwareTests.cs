using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows.Tests;

public sealed class HardwareTests
{
    // Opt-in: the operator explicitly starts this real recording and plays/speaks a test signal.
    [Fact]
    [Trait("Category", "Hardware")]
    [Trait("Category", "Windows")]
    public async Task DefaultDevicesProduceClosedHashed48kChunks()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("RECORDER_RUN_HARDWARE_TESTS") != "1") return;
        using var folder = new TestFolder();
        var capture = new WasapiAudioCapture();
        Assert.NotEmpty(capture.ListDevices(AudioDeviceKind.Render));
        Assert.NotEmpty(capture.ListDevices(AudioDeviceKind.Capture));
        await using var session = await capture.StartAsync(new(folder.Path, null, null), CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(11));
        var result = await session.StopAsync(CancellationToken.None);
        Assert.True(result.Complete);
        Assert.Equal(2, result.Streams.Count);
        foreach (var stream in result.Streams)
        {
            Assert.Equal(2, stream.ChunkPaths.Count);
            using var reader = new NAudio.Wave.WaveFileReader(stream.ChunkPaths[0]);
            Assert.Equal(48000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        }
        var reopened = await new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None);
        Assert.Equal(result.Duration, reopened!.Duration);
        Assert.Equal(SessionState.Recording.ToString(), folder.Manifest["state"]!.GetValue<string>());
    }
}
