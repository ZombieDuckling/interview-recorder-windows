using NAudio.Wave;
using Recorder.Core.Abstractions;
using Recorder.Core.Diagnostics;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

// The portable session coordinator is exercised with synthetic inputs; only the factory calls WASAPI.
internal sealed class AudioCaptureSession : ICaptureSession
{
    private readonly CaptureRequest request;
    private readonly ICaptureInputFactory factory;
    private readonly CaptureManifest manifest;
    private readonly CaptureClock clock = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lane[] lanes = [new(StreamRole.Meeting), new(StreamRole.Microphone)];
    private readonly Func<long> freeBytes;
    private readonly bool processSupported;
    private readonly object gate = new();
    private readonly List<CaptureFault> pendingFaults = [];
    private EventHandler<CaptureFault>? faultHandlers;
    private Task supervisor = Task.CompletedTask;
    private Task<CaptureResult>? stopTask;
    private int stopping;
    private int disposed;
    private volatile bool started;
    private volatile bool fatal;

    internal AudioCaptureSession(CaptureRequest request, ICaptureInputFactory factory, bool processSupported, Func<long>? freeBytes = null)
    {
        this.request = request;
        this.factory = factory;
        this.processSupported = processSupported;
        manifest = new CaptureManifest(request.WorkingDirectory);
        this.freeBytes = freeBytes ?? (() => SafeFreeBytes(request.WorkingDirectory));
    }

    public event EventHandler<LevelSample>? LevelsAvailable;
    public event EventHandler<CaptureFault>? FaultRaised
    {
        add
        {
            CaptureFault[] replay;
            lock (gate)
            {
                faultHandlers += value;
                replay = pendingFaults.ToArray();
                pendingFaults.Clear();
            }
            foreach (var fault in replay) value?.Invoke(this, fault);
        }
        remove { lock (gate) faultHandlers -= value; }
    }
    public TimeSpan Elapsed => TimeSpan.FromSeconds(clock.Frame / 48000d);

    internal async Task StartAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!manifest.Read().Consent.Confirmed) throw new InvalidOperationException("Consent must be confirmed before capture starts.");
        foreach (var lane in lanes)
        {
            var folder = Path.Combine(request.WorkingDirectory, "capture", StreamRoleMapping.ToJsonKind(lane.Role));
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
                throw new IOException("The original recording folder is not empty.");
        }
        if (freeBytes() <= CaptureStream.ReserveDiskBytes) throw new IOException("There is not enough disk space to start recording.");
        clock.Start();
        manifest.Begin(clock.StartedAt);
        factory.Changed += DeviceChanged;
        foreach (var lane in lanes)
        {
            lane.TryProcess = lane.Role == StreamRole.Meeting && request.PreferProcessLoopback && processSupported && request.BrowserProcessId is > 0;
            if (lane.Role == StreamRole.Meeting && request.PreferProcessLoopback && !lane.TryProcess)
                RaiseFault(new(lane.Role, CaptureFaultKind.Unsupported, true, 20348));
            lane.Writer = new CaptureStream(request.WorkingDirectory, lane.Role, manifest, () => clock.Frame, freeBytes, RaiseLevels, WriterFault, lane.TryProcess);
            token.ThrowIfCancellationRequested();
            lane.Input = await OpenAsync(lane, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        if (lanes.Any(l => l.Writer!.Failed)) throw new IOException("Capture could not write to storage.");
        CaptureDiagnostics.Write(request.WorkingDirectory, ProcessingLogCode.CaptureStarted);
        supervisor = Task.Run(SuperviseAsync, CancellationToken.None);
        started = true;
        if (fatal) throw new IOException("Capture could not write to storage.");
    }

    private async Task<Attachment> OpenAsync(Lane lane, CancellationToken token)
    {
        try { return await OpenAndStartAsync(lane, token).ConfigureAwait(false); }
        catch (Exception error) when (lane.TryProcess && error is not OperationCanceledException)
        {
            lane.TryProcess = false;
            var fallback = await OpenAndStartAsync(lane, token).ConfigureAwait(false);
            RaiseFault(new(lane.Role, CaptureFaultKind.Unsupported, true, error.HResult));
            return fallback;
        }
    }

    private async Task<Attachment> OpenAndStartAsync(Lane lane, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var input = await factory.OpenAsync(lane.Role, request, lane.TryProcess).ConfigureAwait(false);
        var attachment = new Attachment(input);
        input.Packet += (data, deviceFrame, qpc, timestampError, discontinuity) =>
        {
            try { Receive(lane, attachment, data, deviceFrame, qpc, timestampError, discontinuity); }
            catch (Exception error)
            {
                attachment.StartError = error;
                lane.DiagnosticCode = error.HResult;
                Interlocked.Exchange(ref lane.Change, (int)CaptureFaultKind.FormatChanged + 1);
            }
        };
        input.Stopped += error =>
        {
            if (Volatile.Read(ref attachment.Detaching) != 0 || Volatile.Read(ref stopping) != 0) return;
            attachment.StartError = error ?? new IOException("The audio input stopped unexpectedly.");
            lane.DiagnosticCode = attachment.StartError.HResult;
            Interlocked.Exchange(ref lane.Change, (int)(error is null ? CaptureFaultKind.DeviceRemoved : CaptureStream.Classify(error)) + 1);
        };
        try
        {
            token.ThrowIfCancellationRequested();
            input.Start();
            for (var attempt = 0; !input.IsRunning; attempt++)
            {
                if (attachment.StartError is not null) throw attachment.StartError;
                if (attempt == 200) throw new TimeoutException("The audio input did not start.");
                await Task.Delay(10, token).ConfigureAwait(false);
            }
            if (attachment.StartError is not null) throw attachment.StartError;
            _ = new PcmFrameConverter(input.Format); // Refuse unsupported formats even if the device is silent.
            manifest.RegisterStream(lane.Role, 0, input.ProcessLoopback);
            lane.Writer!.SetProcessLoopback(input.ProcessLoopback);
            var selected = lane.Role == StreamRole.Meeting ? request.MeetingEndpointId : request.MicrophoneEndpointId;
            if (selected is not null && input.DeviceId != selected && !input.ProcessLoopback)
                RaiseFault(new(lane.Role, CaptureFaultKind.DeviceRemoved, true, 0));
            return attachment;
        }
        catch
        {
            Interlocked.Exchange(ref attachment.Detaching, 1);
            await input.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void Receive(Lane lane, Attachment input, ReadOnlySpan<byte> data, long deviceFrame, long qpc, bool timestampError, bool discontinuity)
    {
        var format = input.Input.Format;
        var frames = data.Length / format.BlockAlign;
        if (frames == 0) return;
        var now = clock.Frame;
        Volatile.Write(ref input.LastPacketFrame, now);
        var firstFrame = !timestampError && qpc > 0 ? clock.FromQpc(qpc) : now - frames * 48000L / format.SampleRate;
        if (firstFrame > now + 4800 || firstFrame < now - 480000)
            firstFrame = now - frames * 48000L / format.SampleRate;
        // Place packets contiguously: each continues where the previous ended. Per-packet QPC is only accurate to ~0.1-1 ms
        // (worse on Bluetooth), so re-placing every packet from it would splice silence in or drop samples 20-100 times a second.
        // Re-anchor from QPC only at the start, on a discontinuity, or when QPC and the running position disagree by > 100 ms
        // (a real gap, or slow device-clock drift: one 100 ms correction every ~30 min at 50 ppm).
        bool reanchor = input.Converter is null || discontinuity || Math.Abs(firstFrame - input.OutputEnd) > 4800;
        if (reanchor)
        {
            Flush(lane, input);
            input.Converter = new PcmFrameConverter(format);
            input.OutputEnd = firstFrame;
        }

        var converter = input.Converter!;
        var outputStart = input.OutputEnd;
        // A native packet is at most the requested buffer, but split defensively before conversion/allocation.
        var maxBytes = Math.Max(1, format.SampleRate / 10) * format.BlockAlign;
        for (var offset = 0; offset < data.Length; offset += maxBytes)
        {
            var converted = converter.Convert(data.Slice(offset, Math.Min(maxBytes, data.Length - offset)));
            lane.Writer!.Accept(outputStart, converted);
            outputStart += converted.Length;
        }
        input.OutputEnd = outputStart;
        input.ExpectedDeviceFrame = deviceFrame + frames;
    }

    private static void Flush(Lane lane, Attachment attachment)
    {
        if (attachment.Converter is not null)
            lane.Writer!.Accept(attachment.OutputEnd, attachment.Converter.Flush());
        attachment.Converter = null;
    }

    private void DeviceChanged(CaptureDeviceChange change)
    {
        if (Volatile.Read(ref stopping) != 0) return;
        var lane = lanes[change.Kind == AudioDeviceKind.Render ? 0 : 1];
        var input = lane.Input?.Input;
        if (input?.ProcessLoopback == true) return;
        var selected = lane.Role == StreamRole.Meeting ? request.MeetingEndpointId : request.MicrophoneEndpointId;
        var followsDefault = selected is null || input?.DeviceId != selected;
        if (change.DefaultChanged ? followsDefault : input is null || input.DeviceId == change.Id)
            Interlocked.Exchange(ref lane.Change, (int)change.Reason + 1);
    }

    private async Task SuperviseAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                foreach (var lane in lanes)
                {
                    // Stall watchdog (mic only: a mic in event mode delivers packets even in silence; loopback legitimately
                    // delivers nothing while nothing plays). A Bluetooth A2DP->HFP switch, USB hiccup or resume from sleep can
                    // leave WASAPI "capturing" with no packets and no notification: re-attach after 2 s, at most every 5 s.
                    if (lane.Role == StreamRole.Microphone && lane.Input is { } attached && Volatile.Read(ref lane.Change) == 0)
                    {
                        long nowFrame = clock.Frame;
                        long lastActivity = Math.Max(Volatile.Read(ref attached.LastPacketFrame), attached.AttachedAtFrame);
                        if (nowFrame - lastActivity > 96000 && nowFrame - lane.LastStallRestart > 240000)
                        {
                            lane.LastStallRestart = nowFrame;
                            Interlocked.Exchange(ref lane.Change, (int)CaptureFaultKind.DeviceRemoved + 1);
                        }
                    }

                    var change = Interlocked.Exchange(ref lane.Change, 0);
                    if (change == 0 && (lane.Input is not null || clock.Frame < lane.RetryAfter)) continue;
                    if (change != 0) lane.LastReason = (CaptureFaultKind)(change - 1);
                    try
                    {
                        await DetachAsync(lane).ConfigureAwait(false);
                        lifetime.Token.ThrowIfCancellationRequested();
                        lane.Input = await OpenAsync(lane, lifetime.Token).ConfigureAwait(false);
                        lane.Input.AttachedAtFrame = clock.Frame;
                        CaptureDiagnostics.Write(request.WorkingDirectory, ProcessingLogCode.CaptureDeviceChanged,
                            new ProcessingLogValue("stream", (long)lane.Role));
                        RaiseFault(new(lane.Role, lane.LastReason, true, lane.DiagnosticCode));
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        lane.RetryAfter = clock.Frame + 48000;
                        if (!lane.ReportedUnavailable)
                        {
                            RaiseFault(new(lane.Role, lane.LastReason, false, error.HResult));
                            lane.ReportedUnavailable = true;
                        }
                        CaptureDiagnostics.Write(request.WorkingDirectory, ProcessingLogCode.CaptureError,
                            new ProcessingLogValue("stream", (long)lane.Role), new ProcessingLogValue("hresult", (long)error.HResult));
                    }
                    if (lane.Input is not null) lane.ReportedUnavailable = false;
                }
                await Task.Delay(100, lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private static async Task DetachAsync(Lane lane)
    {
        var attachment = lane.Input;
        lane.Input = null;
        if (attachment is null) return;
        Interlocked.Exchange(ref attachment.Detaching, 1);
        await attachment.Input.DisposeAsync().ConfigureAwait(false); // Joins capture before flushing/completing the queue.
        Flush(lane, attachment);
    }

    public Task<CaptureResult> StopAsync(CancellationToken cancellationToken)
    {
        // Cancellation cancels only the caller's wait. Stop/finalization always finishes, and repeated callers share it.
        lock (gate)
        {
            if (stopTask is null)
            {
                Interlocked.Exchange(ref stopping, 1);
                clock.Stop();
                lifetime.Cancel();
                stopTask = Task.Run(StopCoreAsync);
            }
            return stopTask.WaitAsync(cancellationToken);
        }
    }

    private async Task<CaptureResult> StopCoreAsync()
    {
        await supervisor.ConfigureAwait(false);
        factory.Changed -= DeviceChanged;
        var complete = started && !fatal;
        foreach (var lane in lanes)
        {
            if (lane.Input is null) complete = false;
            try { await DetachAsync(lane).ConfigureAwait(false); }
            catch (Exception error)
            {
                complete = false;
                RaiseFault(new(lane.Role, CaptureFaultKind.Other, false, error.HResult));
            }
        }
        var streams = new List<StreamFiles>(2);
        foreach (var lane in lanes)
        {
            if (lane.Writer is null) continue;
            var stream = await lane.Writer.StopAsync(clock.Frame).ConfigureAwait(false);
            complete &= lane.Writer.Complete;
            if (stream.ChunkPaths.Count > 0) streams.Add(stream);
        }
        var duration = streams.Count == 0 ? TimeSpan.Zero : streams.Max(s => s.StartOffset + s.Duration);
        if (clock.Started)
        {
            try { manifest.End(duration); }
            catch (Exception error)
            {
                complete = false;
                RaiseFault(new(StreamRole.Meeting, CaptureStream.Classify(error), false, error.HResult));
            }
        }
        return new CaptureResult(streams, duration, complete);
    }

    private void WriterFault(CaptureFault fault)
    {
        if (!fault.Recovered)
        {
            fatal = true;
            // Neither disk errors nor a failing writer may leave the other input recording invisibly.
            if (started) _ = Task.Run(() => StopAsync(CancellationToken.None));
        }
        RaiseFault(fault);
    }

    private void RaiseFault(CaptureFault fault)
    {
        CaptureDiagnostics.Write(request.WorkingDirectory,
            fault.Kind == CaptureFaultKind.DiskLow ? ProcessingLogCode.DiskLow : ProcessingLogCode.CaptureError,
            new ProcessingLogValue("stream", (long)fault.Role), new ProcessingLogValue("kind", (long)fault.Kind),
            new ProcessingLogValue("recovered", fault.Recovered ? 1L : 0L), new ProcessingLogValue("hresult", (long)fault.DiagnosticCode));
        EventHandler<CaptureFault>? handler;
        lock (gate)
        {
            handler = faultHandlers;
            if (handler is null && pendingFaults.Count < 8) pendingFaults.Add(fault);
        }
        try { handler?.Invoke(this, fault); }
        catch (Exception) { /* UI observers cannot tear down the audio worker. */ }
    }

    private void RaiseLevels(LevelSample sample)
    {
        try { LevelsAvailable?.Invoke(this, sample); }
        catch (Exception) { /* UI observers cannot tear down the audio worker. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var lane in lanes) lane.Writer?.Dispose();
        factory.Dispose();
        lifetime.Dispose();
    }

    private sealed class Lane(StreamRole role)
    {
        internal readonly StreamRole Role = role;
        internal CaptureStream? Writer;
        internal Attachment? Input;
        internal bool TryProcess;
        internal int Change;
        internal int DiagnosticCode;
        internal long RetryAfter;
        internal bool ReportedUnavailable;
        internal CaptureFaultKind LastReason = CaptureFaultKind.DeviceRemoved;
        internal long LastStallRestart = long.MinValue / 2;
    }

    private sealed class Attachment(ICaptureInput input)
    {
        internal ICaptureInput Input { get; } = input;
        internal PcmFrameConverter? Converter;
        internal long ExpectedDeviceFrame = -1;
        internal long OutputEnd;
        internal long LastPacketFrame;
        internal long AttachedAtFrame;
        internal int Detaching;
        internal Exception? StartError;
    }

    /// <summary>Free space of the drive holding the session; long.MaxValue (unknown: skip the check) for UNC or redirected
    /// paths where DriveInfo throws, so a disk check can never be what stops an interview.</summary>
    private static long SafeFreeBytes(string directory)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }
}
