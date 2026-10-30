using System.Collections.Concurrent;
using Recorder.Core.Abstractions;
using Recorder.Core.Diagnostics;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

internal sealed class CaptureStream : IDisposable
{
    internal const int MaxPacketFrames = 4800;
    internal const int QueueCapacity = 40; // 40 x 100 ms = 4 s, at most 384 KB PCM per stream.
    internal const long LowDiskBytes = 256L * 1024 * 1024;
    internal const long ReserveDiskBytes = 16L * 1024 * 1024;
    private readonly BlockingCollection<Packet> queue = new(QueueCapacity);
    private readonly string root;
    private readonly StreamRole role;
    private readonly Func<long> clock;
    private readonly Func<long> freeBytes;
    private readonly Action<LevelSample> levels;
    private readonly Action<CaptureFault> faults;
    private readonly PcmChunkWriter writer;
    private readonly StreamClockAligner aligner;
    private readonly Task consumer;
    private long stopFrame = -1;
    private long lostFrames;
    private long totalLost;
    private long lastLevel;
    private long lastDisk = -48000;
    private bool diskWarned;
    private double energy;
    private float peak;
    private long measuredFrames;
    private bool processLoopback;
    private int fatal;
    private int disposed;

    internal CaptureStream(string directory, StreamRole role, CaptureManifest manifest, Func<long> clock,
        Func<long> freeBytes, Action<LevelSample> levels, Action<CaptureFault> faults, bool processLoopback = false)
    {
        root = Path.GetFullPath(directory);
        this.role = role;
        this.clock = clock;
        this.freeBytes = freeBytes;
        this.levels = levels;
        this.faults = faults;
        this.processLoopback = processLoopback;
        // The first stored sample is timeline silence at zero, even if the first hardware packet arrives late.
        manifest.RegisterStream(role, 0, processLoopback);
        writer = new PcmChunkWriter(root, role, chunk =>
        {
            manifest.CloseChunk(role, chunk);
            CaptureDiagnostics.Write(root, ProcessingLogCode.ChunkClosed, new ProcessingLogValue("file", new SessionRelativePath(chunk.File)), new ProcessingLogValue("frames", chunk.Frames));
            CheckDisk();
        });
        // Loopback (meeting) delivers no packets while nothing plays, so its silence fill is normal, not a gap (spec section 3).
        // The mic always delivers packets, so a mic gap is a real one (device change, sleep, stall) and is logged.
        aligner = new StreamClockAligner(writer, 0, frames =>
        {
            if (role == StreamRole.Microphone)
            {
                CaptureDiagnostics.Write(root, ProcessingLogCode.CaptureGap, new ProcessingLogValue("stream", (long)role), new ProcessingLogValue("frames", frames));
            }
        });
        consumer = Task.Factory.StartNew(Consume, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal bool Failed => Volatile.Read(ref fatal) != 0;
    internal bool Complete => !Failed && Interlocked.Read(ref totalLost) == 0;
    internal void SetProcessLoopback(bool value) => processLoopback = value;

    // Runs on the MMCSS native callback. No disk writes, waiting, or observer callbacks here.
    internal void Accept(long firstFrame, ReadOnlySpan<short> samples)
    {
        if (Failed || queue.IsAddingCompleted) return;
        for (var i = 0; i < samples.Length; i += MaxPacketFrames)
        {
            var count = Math.Min(MaxPacketFrames, samples.Length - i);
            var packet = new Packet(firstFrame + i, samples.Slice(i, count).ToArray());
            try
            {
                if (!queue.TryAdd(packet)) Interlocked.Add(ref lostFrames, count);
            }
            catch (InvalidOperationException) { return; }
        }
    }

    internal async Task<StreamFiles> StopAsync(long finalFrame)
    {
        Interlocked.CompareExchange(ref stopFrame, finalFrame, -1);
        queue.CompleteAdding();
        await consumer.ConfigureAwait(false);
        return new StreamFiles(role, Path.Combine(root, "capture", StreamRoleMapping.ToJsonKind(role)),
            writer.Chunks.Select(c => SessionPaths.Resolve(root, c.File)).ToArray(), 48000, 1,
            TimeSpan.FromSeconds(writer.Chunks.Sum(c => c.Frames) / 48000d), TimeSpan.Zero, processLoopback);
    }

    private void Consume()
    {
        try
        {
            while (!queue.IsCompleted)
            {
                if (queue.TryTake(out var packet, 10))
                {
                    var final = Interlocked.Read(ref stopFrame);
                    var length = final < 0 ? packet.Samples.Length : (int)Math.Clamp(final - packet.FirstFrame, 0, packet.Samples.Length);
                    var skip = (int)Math.Clamp(aligner.EndFrame - packet.FirstFrame, 0, length);
                    aligner.Append(packet.FirstFrame, packet.Samples.AsSpan(0, length));
                    Measure(packet.Samples.AsSpan(skip, length - skip));
                }
                var now = clock();
                if (now - lastDisk >= 48000)
                {
                    CheckDisk();
                    lastDisk = now;
                }
                // A 250 ms watermark leaves room for native delivery jitter. Packet QPCs still set placement.
                if (queue.Count == 0 && Interlocked.Read(ref stopFrame) < 0) aligner.AdvanceTo(Math.Max(0, now - 48000));
                var lost = Interlocked.Exchange(ref lostFrames, 0);
                if (lost != 0)
                {
                    Interlocked.Add(ref totalLost, lost);
                    CaptureDiagnostics.Write(root, ProcessingLogCode.CaptureGap, new ProcessingLogValue("stream", (long)role), new ProcessingLogValue("frames", lost));
                    faults(new CaptureFault(role, CaptureFaultKind.WriteFailed, true, 2002));
                }
                if (now - lastLevel >= 2400)
                {
                    levels(new LevelSample(role, Db(peak), Db(measuredFrames == 0 ? 0 : (float)Math.Sqrt(energy / measuredFrames))));
                    lastLevel = now;
                    peak = 0;
                    energy = 0;
                    measuredFrames = 0;
                }
            }
            aligner.AdvanceTo(Math.Max(0, Interlocked.Read(ref stopFrame)));
            writer.Complete();
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref fatal, 1);
            CaptureDiagnostics.Write(root, ProcessingLogCode.CaptureError, new ProcessingLogValue("stream", (long)role), new ProcessingLogValue("hresult", (long)exception.HResult));
            faults(new CaptureFault(role, Classify(exception), false, exception.HResult));
        }
        finally
        {
            writer.Dispose(); // An unsuccessful write leaves just the recoverable tail.
        }
    }

    private void CheckDisk()
    {
        var available = freeBytes();
        if (available <= ReserveDiskBytes) throw new CaptureDiskReserveException();
        if (available < LowDiskBytes && !diskWarned)
        {
            diskWarned = true;
            CaptureDiagnostics.Write(root, ProcessingLogCode.DiskLow, new ProcessingLogValue("bytes", available));
            faults(new CaptureFault(role, CaptureFaultKind.DiskLow, true, 112));
        }
        if (available >= LowDiskBytes) diskWarned = false;
    }

    internal static CaptureFaultKind Classify(Exception error) => error switch
    {
        CaptureDiskReserveException => CaptureFaultKind.DiskLow,
        UnauthorizedAccessException => CaptureFaultKind.AccessDenied,
        NotSupportedException => CaptureFaultKind.Unsupported,
        _ when (error.HResult & 0xffff) is 112 or 39 => CaptureFaultKind.DiskFull,
        _ when error.HResult == unchecked((int)0x88890004) => CaptureFaultKind.DeviceRemoved,
        _ when error.HResult == unchecked((int)0x88890008) => CaptureFaultKind.FormatChanged,
        _ when error.HResult == unchecked((int)0x80070005) => CaptureFaultKind.AccessDenied,
        _ => CaptureFaultKind.WriteFailed,
    };

    private void Measure(ReadOnlySpan<short> samples)
    {
        foreach (var sample in samples)
        {
            var normalized = sample / 32768f;
            peak = Math.Max(peak, Math.Abs(normalized));
            energy += normalized * normalized;
        }
        measuredFrames += samples.Length;
    }

    private static float Db(float value) => value <= 0.000001f ? -120 : 20 * MathF.Log10(value);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (!consumer.IsCompleted) throw new InvalidOperationException("Stop capture before disposing its writer.");
        queue.Dispose();
        writer.Dispose();
    }

    private sealed record Packet(long FirstFrame, short[] Samples);
    private sealed class CaptureDiskReserveException : IOException;
}
