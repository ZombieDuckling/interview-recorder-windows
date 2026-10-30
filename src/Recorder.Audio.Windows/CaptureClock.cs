using System.Diagnostics;

namespace Recorder.Audio.Windows;

internal sealed class CaptureClock
{
    private long origin;
    private long finalFrame = -1;
    internal DateTimeOffset StartedAt { get; private set; }
    internal bool Started => Volatile.Read(ref origin) != 0;
    internal void Start()
    {
        StartedAt = DateTimeOffset.UtcNow;
        Volatile.Write(ref origin, Stopwatch.GetTimestamp());
    }

    internal long Frame => Interlocked.Read(ref finalFrame) is var final && final >= 0
        ? final : !Started ? 0 : (long)((Int128)(Stopwatch.GetTimestamp() - Volatile.Read(ref origin)) * 48000 / Stopwatch.Frequency);

    internal long FromQpc(long hundredNanoseconds) => (long)(((Int128)hundredNanoseconds * Stopwatch.Frequency -
        (Int128)Volatile.Read(ref origin) * TimeSpan.TicksPerSecond) * 48000 / (Stopwatch.Frequency * (Int128)TimeSpan.TicksPerSecond));

    internal long Stop()
    {
        Interlocked.CompareExchange(ref finalFrame, Frame, -1);
        return Interlocked.Read(ref finalFrame);
    }
}
