namespace Recorder.Audio.Windows;

// Positions are absolute 48 kHz frame indices on the session QPC timeline.
internal sealed class StreamClockAligner(PcmChunkWriter writer, long firstFrameOffset, Action<long> reportGap)
{
    private long gapStart = -1;
    private bool gapReported;
    internal long EndFrame => firstFrameOffset + writer.FramesWritten;

    internal void Append(long startFrame, ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return;
        AdvanceTo(startFrame);
        var skip = Math.Min(samples.Length, Math.Max(0, EndFrame - startFrame));
        if (skip == samples.Length) return; // Late/duplicate packets cannot move the clock backwards.
        writer.Write(samples[(int)skip..]);
        gapStart = -1;
        gapReported = false;
    }

    internal void AdvanceTo(long frame)
    {
        var missing = ComputeMissingFrames(firstFrameOffset, frame, writer.FramesWritten);
        if (missing == 0) return;
        if (gapStart < 0) gapStart = EndFrame;
        writer.WriteSilence(missing);
        if (!gapReported && IsGapLongerThanTwoSeconds(frame - gapStart))
        {
            gapReported = true;
            reportGap(frame - gapStart);
        }
    }

    internal static long ComputeMissingFrames(long firstFrameOffset, long elapsedFrames, long writtenFrames) =>
        firstFrameOffset < 0 || elapsedFrames <= firstFrameOffset ? 0 : Math.Max(0, elapsedFrames - firstFrameOffset - writtenFrames);

    internal static bool IsGapLongerThanTwoSeconds(long missingFrames) => missingFrames > 48000 * 2;
}
