namespace Recorder.Transcription;

/// <summary>Energy-based speech detection on 16 kHz mono PCM, streamed (bounded memory).</summary>
internal static class SpeechRegions
{
    private const int FrameSamples = 480;          // 30 ms
    private const float SpeechRms = 0.004f;        // about -48 dBFS
    private const int MinSpeechFrames = 8;         // 0.24 s
    private const int MergeGapFrames = 25;         // 0.75 s
    private const int PadFrames = 8;               // 0.24 s each side
    private const int MinRegionSamples = PcmWindowReader.SampleRate * 4; // whisper misses very short clips: give at least 4 s of context

    public static IReadOnlyList<(long Start, long End)> Find(PcmWindowReader reader, CancellationToken cancellationToken)
    {
        var regions = new List<(long Start, long End)>();
        var block = new float[PcmWindowReader.SampleRate * 10];
        long frameIndex = 0;
        long runStart = -1;
        long lastSpeech = -1;
        for (long pos = 0; pos < reader.FrameCount; pos += block.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = reader.Read(pos, block);
            for (int i = 0; i + FrameSamples <= count; i += FrameSamples, frameIndex++)
            {
                double sum = 0;
                for (int k = i; k < i + FrameSamples; k++) sum += block[k] * block[k];
                bool speech = Math.Sqrt(sum / FrameSamples) >= SpeechRms;
                if (!speech) continue;
                if (runStart >= 0 && frameIndex - lastSpeech > MergeGapFrames)
                {
                    Close(regions, runStart, lastSpeech, reader.FrameCount);
                    runStart = -1;
                }

                if (runStart < 0) runStart = frameIndex;
                lastSpeech = frameIndex;
            }

            if (count < block.Length) break;
        }

        if (runStart >= 0) Close(regions, runStart, lastSpeech, reader.FrameCount);
        return regions;
    }

    private static void Close(List<(long Start, long End)> regions, long firstFrame, long lastFrame, long totalSamples)
    {
        if (lastFrame - firstFrame + 1 < MinSpeechFrames) return;
        long start = Math.Max(0, (firstFrame - PadFrames) * FrameSamples);
        long end = Math.Min(totalSamples, (lastFrame + 1 + PadFrames) * FrameSamples);
        if (end - start < MinRegionSamples)
        {
            long grow = (MinRegionSamples - (end - start)) / 2;
            start = Math.Max(0, start - grow);
            end = Math.Min(totalSamples, Math.Max(end + grow, start + MinRegionSamples));
        }
        if (regions.Count > 0 && start <= regions[^1].End)
        {
            regions[^1] = (regions[^1].Start, Math.Max(end, regions[^1].End));
            return;
        }

        regions.Add((start, end));
    }
}
