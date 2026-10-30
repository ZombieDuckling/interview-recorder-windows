using System.Security.Cryptography;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Media.Tests;

public sealed class FfmpegMediaFinalizerTests
{
    [MediaFact, Trait("Category", "Media")]
    public async Task Finalize_concatenates_chunks_delays_and_pads_to_longest_stream()
    {
        using var fixture = new MediaFixture();
        StreamFiles mic = fixture.Stream(StreamRole.Microphone, seconds: .4, amplitude: .1, offset: .2, chunks: 2, chunkName: "a ' quoted ");
        StreamFiles meeting = fixture.Stream(StreamRole.Meeting, seconds: 1, amplitude: 0);
        var progress = new ProgressRecorder();
        var finalizer = new FfmpegMediaFinalizer(MediaFactAttribute.ExecutablePath);
        FinalizeResult result = await finalizer.FinalizeAsync(new CaptureResult([mic, meeting], TimeSpan.FromSeconds(99), true), fixture.Output, progress, CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Duration);
        Assert.Equal(fixture.Output, result.Mp4Path);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fixture.Output))), result.Sha256);
        float[] audio = await fixture.DecodeAsync(fixture.Output);
        Assert.True(Rms(audio, .02, .15) < .001);
        Assert.InRange(Rms(audio, .25, .55), .06, .08);
        Assert.True(Rms(audio, .7, .95) < .001);
        Assert.All(mic.ChunkPaths.Concat(meeting.ChunkPaths), path => Assert.True(File.Exists(path)));
        Assert.Equal(1d, progress.Values[^1]);
        Assert.All(progress.Values, value => Assert.InRange(value, 0, 1));
        Assert.True(progress.Values.SequenceEqual(progress.Values.Order()));
        fixture.AssertNoTemporaryFiles();
    }

    [MediaFact, Trait("Category", "Media")]
    public async Task Finalize_uses_offset_plus_samples_for_duration_and_validates_output_format()
    {
        using var fixture = new MediaFixture();
        StreamFiles mic = fixture.Stream(StreamRole.Microphone, seconds: 1, offset: .5);
        var finalizer = new FfmpegMediaFinalizer(MediaFactAttribute.ExecutablePath);
        FinalizeResult result = await finalizer.FinalizeAsync(new CaptureResult([mic], TimeSpan.FromSeconds(1), true), fixture.Output, null, CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(1.5), result.Duration);
        using var probe = await MediaFixture.ProbeAsync(fixture.Output);
        var streams = probe.RootElement.GetProperty("streams");
        Assert.Equal(1, streams.GetArrayLength());
        Assert.Equal("audio", streams[0].GetProperty("codec_type").GetString());
        Assert.Equal("aac", streams[0].GetProperty("codec_name").GetString());
        Assert.Equal("LC", streams[0].GetProperty("profile").GetString());
        Assert.Equal("48000", streams[0].GetProperty("sample_rate").GetString());
        Assert.Equal(1, streams[0].GetProperty("channels").GetInt32());
        Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture), 1.49, 1.53);
        Assert.Equal(MediaFixture.SessionCode, probe.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString());
        byte[] bytes = await File.ReadAllBytesAsync(fixture.Output);
        Assert.True(bytes.AsSpan().IndexOf("moov"u8) < bytes.AsSpan().IndexOf("mdat"u8));
    }

    [MediaFact, Trait("Category", "Media")]
    public async Task Finalize_sums_equal_gain_without_automatic_limiter_makeup()
    {
        using var fixture = new MediaFixture();
        StreamFiles mic = fixture.Stream(StreamRole.Microphone, amplitude: .1);
        StreamFiles meeting = fixture.Stream(StreamRole.Meeting, amplitude: .1);
        var finalizer = new FfmpegMediaFinalizer(MediaFactAttribute.ExecutablePath);
        await finalizer.FinalizeAsync(new CaptureResult([mic, meeting], TimeSpan.FromSeconds(1), true), fixture.Output, null, CancellationToken.None);
        float[] audio = await fixture.DecodeAsync(fixture.Output);
        Assert.InRange(Rms(audio, .1, .9), .136, .146);
    }

    [MediaFact, Trait("Category", "Media")]
    public async Task Finalize_limits_overloaded_mix_to_minus_one_db_before_lossy_encoding()
    {
        using var fixture = new MediaFixture();
        StreamFiles mic = fixture.Stream(StreamRole.Microphone, amplitude: .8);
        StreamFiles meeting = fixture.Stream(StreamRole.Meeting, amplitude: .8);
        var finalizer = new FfmpegMediaFinalizer(MediaFactAttribute.ExecutablePath);
        await finalizer.FinalizeAsync(new CaptureResult([mic, meeting], TimeSpan.FromSeconds(1), true), fixture.Output, null, CancellationToken.None);
        float[] audio = await fixture.DecodeAsync(fixture.Output);
        // AAC reconstruction can overshoot the pre-encode sample peak slightly.
        double peak = audio.Skip(4800).Take(38400).Max(sample => Math.Abs(sample));
        Assert.InRange(peak, .86, .93);
    }

    [MediaFact, Trait("Category", "Media")]
    public async Task Transcription_concatenates_to_pcm16_mono16k_without_start_offset()
    {
        using var fixture = new MediaFixture();
        StreamFiles mic = fixture.Stream(StreamRole.Microphone, seconds: .8, offset: 2, chunks: 2);
        string output = Path.Combine(fixture.SessionDirectory, "mic16k.wav");
        var finalizer = new FfmpegMediaFinalizer(MediaFactAttribute.ExecutablePath);
        Assert.Equal(output, await finalizer.ConvertForTranscriptionAsync(mic, output, CancellationToken.None));
        using var probe = await MediaFixture.ProbeAsync(output);
        var audio = probe.RootElement.GetProperty("streams")[0];
        Assert.Equal("pcm_s16le", audio.GetProperty("codec_name").GetString());
        Assert.Equal("16000", audio.GetProperty("sample_rate").GetString());
        Assert.Equal(1, audio.GetProperty("channels").GetInt32());
        Assert.Equal("0.800000", audio.GetProperty("duration").GetString());
        fixture.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Rejects_output_overwriting_an_original_chunk()
    {
        using var fixture = new MediaFixture();
        StreamFiles stream = fixture.Stream(StreamRole.Microphone);
        var finalizer = new FfmpegMediaFinalizer();
        await Assert.ThrowsAsync<ArgumentException>(() => finalizer.ConvertForTranscriptionAsync(stream, stream.ChunkPaths[0], CancellationToken.None));
        Assert.True(File.Exists(stream.ChunkPaths[0]));
    }

    [Fact]
    public async Task Rejects_chunk_paths_that_can_inject_concat_directives()
    {
        using var fixture = new MediaFixture();
        StreamFiles stream = fixture.Stream(StreamRole.Microphone) with { ChunkPaths = ["bad\nfile '/private/secret.wav'"] };
        var finalizer = new FfmpegMediaFinalizer();
        await Assert.ThrowsAsync<ArgumentException>(() => finalizer.ConvertForTranscriptionAsync(stream, Path.Combine(fixture.SessionDirectory, "mic.wav"), CancellationToken.None));
        fixture.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Rejects_non_capture_wave_format_before_starting_process()
    {
        using var fixture = new MediaFixture();
        StreamFiles stream = fixture.Stream(StreamRole.Microphone, sampleRate: 44100) with { SampleRate = 48000 };
        var finalizer = new FfmpegMediaFinalizer();
        await Assert.ThrowsAsync<InvalidDataException>(() => finalizer.ConvertForTranscriptionAsync(stream, Path.Combine(fixture.SessionDirectory, "mic.wav"), CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_non_session_folder_as_metadata_title()
    {
        using var fixture = new MediaFixture();
        StreamFiles stream = fixture.Stream(StreamRole.Microphone);
        string output = Path.Combine(stream.Directory, "private-name.mp4");
        var finalizer = new FfmpegMediaFinalizer();
        await Assert.ThrowsAsync<ArgumentException>(() => finalizer.FinalizeAsync(new CaptureResult([stream], stream.Duration, true), output, null, CancellationToken.None));
    }

    [Fact]
    public async Task Precancelled_operation_preserves_existing_output_and_leaves_no_temps()
    {
        using var fixture = new MediaFixture();
        StreamFiles stream = fixture.Stream(StreamRole.Microphone);
        await File.WriteAllTextAsync(fixture.Output, "old output");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var finalizer = new FfmpegMediaFinalizer();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => finalizer.FinalizeAsync(new CaptureResult([stream], stream.Duration, true), fixture.Output, null, cancelled.Token));
        Assert.Equal("old output", await File.ReadAllTextAsync(fixture.Output));
        fixture.AssertNoTemporaryFiles();
    }

    [MediaFact, Trait("Category", "Media")]
    public async Task Process_failure_preserves_existing_output_and_redacts_paths()
    {
        using var fixture = new MediaFixture();
        StreamFiles stream = fixture.Stream(StreamRole.Microphone);
        await File.WriteAllTextAsync(fixture.Output, "old output");
        // ffprobe is a real executable that rejects the FFmpeg filter/encoder options.
        var finalizer = new FfmpegMediaFinalizer(MediaFactAttribute.ProbePath);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => finalizer.FinalizeAsync(new CaptureResult([stream], stream.Duration, true), fixture.Output, null, CancellationToken.None));
        Assert.DoesNotContain(fixture.SessionDirectory, error.ToString(), StringComparison.Ordinal);
        Assert.Equal("old output", await File.ReadAllTextAsync(fixture.Output));
        fixture.AssertNoTemporaryFiles();
    }

    private static double Rms(float[] audio, double start, double end)
        => Math.Sqrt(audio.Skip((int)(start * 48000)).Take((int)((end - start) * 48000)).Average(sample => (double)sample * sample));

    private sealed class ProgressRecorder : IProgress<double>
    {
        internal List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }
}
