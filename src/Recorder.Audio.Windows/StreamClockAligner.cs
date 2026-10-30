namespace Recorder.Audio.Windows;

internal static class StreamClockAligner
{
    internal static long ComputeMissingFrames(long firstFrameOffset, long elapsedFrames, long writtenFrames)
    {
        if (firstFrameOffset < 0 || elapsedFrames <= firstFrameOffset)
            return 0;

        var expectedFrames = elapsedFrames - firstFrameOffset;
        var missing = expectedFrames - writtenFrames;
        return Math.Max(0, missing);
    }

    internal static bool IsGapLongerThanTwoSeconds(long missingFrames) =>
        missingFrames > PcmChunkWriter.SampleRate * 2;
}
