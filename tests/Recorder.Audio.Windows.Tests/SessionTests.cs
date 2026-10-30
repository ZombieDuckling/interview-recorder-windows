using NAudio.Wave;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task ProcessLoopbackFailureFallsBackAndIsReportedAfterSubscription()
    {
        using var folder = new TestFolder();
        using var factory = new SyntheticFactory { RejectProcess = true };
        await using var session = new AudioCaptureSession(new(folder.Path, null, null, true, 123), factory, true, () => long.MaxValue);
        await session.StartAsync(CancellationToken.None);
        var faults = new List<CaptureFault>();
        session.FaultRaised += (_, f) => faults.Add(f);
        Assert.Equal(1, factory.ProcessAttempts);
        Assert.Contains(faults, f => f.Kind == CaptureFaultKind.Unsupported && f.Recovered);
        await Task.Delay(80);
        var result = await session.StopAsync(CancellationToken.None);
        Assert.Equal(2, result.Streams.Count);
        Assert.All(result.Streams, s => Assert.False(s.IsProcessLoopback));
        Assert.Equal("wasapi-loopback", folder.Manifest["streams"]![0]!["source"]!.GetValue<string>());
    }

    [Fact]
    public async Task DefaultChangeReattachesWithoutResettingChunksOrClock()
    {
        using var folder = new TestFolder();
        using var factory = new SyntheticFactory();
        await using var session = new AudioCaptureSession(new(folder.Path, null, null), factory, false, () => long.MaxValue);
        await session.StartAsync(CancellationToken.None);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.FaultRaised += (_, f) => { if (f.Kind == CaptureFaultKind.DefaultDeviceChanged && f.Recovered) changed.TrySetResult(); };
        var first = factory.Inputs[0];
        factory.Change(new("new-render", AudioDeviceKind.Render, CaptureFaultKind.DefaultDeviceChanged, true));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(first.Disposed);
        Assert.Equal(3, factory.Inputs.Count);
        await Task.Delay(80);
        var result = await session.StopAsync(CancellationToken.None);
        Assert.True(result.Complete);
        Assert.Equal(result.Streams[0].Duration, result.Streams[1].Duration);
        Assert.All(factory.Inputs, input => Assert.True(input.Disposed));
        Assert.Equal("Recording", folder.Manifest["state"]!.GetValue<string>());
    }

    [Fact]
    public async Task FormatChangeRecreatesTheConverterAndInput()
    {
        using var folder = new TestFolder();
        using var factory = new SyntheticFactory();
        await using var session = new AudioCaptureSession(new(folder.Path, null, null), factory, false, () => long.MaxValue);
        await session.StartAsync(CancellationToken.None);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.FaultRaised += (_, f) => { if (f.Kind == CaptureFaultKind.FormatChanged && f.Recovered) changed.TrySetResult(); };
        factory.Format = new WaveFormat(44100, 16, 2);
        factory.Change(new("capture", AudioDeviceKind.Capture, CaptureFaultKind.FormatChanged));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        factory.Inputs[^1].Send(new byte[1764]);
        await Task.Delay(60);
        var result = await session.StopAsync(CancellationToken.None);
        Assert.True(result.Complete);
        Assert.All(result.Streams, s => Assert.Equal(48000, s.SampleRate));
    }

    [Fact]
    public async Task ConcurrentStopIsIdempotentAndCancellationDoesNotAbandonFinalization()
    {
        using var folder = new TestFolder();
        using var factory = new SyntheticFactory();
        await using var session = new AudioCaptureSession(new(folder.Path, null, null), factory, false, () => long.MaxValue);
        await session.StartAsync(CancellationToken.None);
        await Task.Delay(80);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await session.StopAsync(cancellation.Token); }
        catch (OperationCanceledException) { }
        var results = await Task.WhenAll(session.StopAsync(CancellationToken.None), session.StopAsync(CancellationToken.None));
        Assert.Same(results[0], results[1]);
        var elapsed = session.Elapsed;
        await Task.Delay(20);
        Assert.Equal(elapsed, session.Elapsed);
        Assert.All(factory.Inputs, input => Assert.True(input.Disposed));
    }

    [Fact]
    public async Task StartupFailureStopsTheAlreadyOpenedInput()
    {
        using var folder = new TestFolder();
        using var factory = new SyntheticFactory { RejectMic = true };
        var session = new AudioCaptureSession(new(folder.Path, null, null), factory, false, () => long.MaxValue);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.StartAsync(CancellationToken.None));
        await session.DisposeAsync();
        Assert.True(Assert.Single(factory.Inputs).Disposed);
        Assert.True(factory.Disposed);
    }

    [Fact]
    public async Task ConsentIsRequiredBeforeAnyNativeInputOpens()
    {
        using var folder = new TestFolder();
        var manifest = folder.Manifest;
        manifest["consent"]!["confirmed"] = false;
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "session.json"), manifest.ToJsonString());
        using var factory = new SyntheticFactory();
        await using var session = new AudioCaptureSession(new(folder.Path, null, null), factory, false, () => long.MaxValue);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(CancellationToken.None));
        Assert.Empty(factory.Inputs);
    }

    internal sealed class SyntheticFactory : ICaptureInputFactory
    {
        public event Action<CaptureDeviceChange>? Changed;
        internal bool RejectProcess { get; init; }
        internal bool RejectMic { get; init; }
        internal int ProcessAttempts { get; private set; }
        internal List<SyntheticInput> Inputs { get; } = [];
        internal WaveFormat Format { get; set; } = new(48000, 16, 1);
        internal bool Disposed { get; private set; }
        public Task<ICaptureInput> OpenAsync(StreamRole role, CaptureRequest request, bool allowProcessLoopback)
        {
            if (allowProcessLoopback)
            {
                ProcessAttempts++;
                if (RejectProcess) throw new NotSupportedException();
            }
            if (RejectMic && role == StreamRole.Microphone) throw new UnauthorizedAccessException();
            var input = new SyntheticInput(Format, role == StreamRole.Meeting ? "render" : "capture", allowProcessLoopback);
            Inputs.Add(input);
            return Task.FromResult<ICaptureInput>(input);
        }
        internal void Change(CaptureDeviceChange change) => Changed?.Invoke(change);
        public void Dispose() => Disposed = true;
    }

    internal sealed class SyntheticInput(WaveFormat format, string id, bool process) : ICaptureInput
    {
        public event CapturePacketHandler? Packet;
        public event Action<Exception?>? Stopped;
        public WaveFormat Format => format;
        public string DeviceId => id;
        public bool ProcessLoopback => process;
        public bool IsRunning { get; private set; }
        internal bool Disposed { get; private set; }
        internal void Send(byte[] data) => Packet?.Invoke(data, 0, 0, true, false);
        public void Start() => IsRunning = true;
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            IsRunning = false;
            Stopped?.Invoke(null);
            return ValueTask.CompletedTask;
        }
    }
}
