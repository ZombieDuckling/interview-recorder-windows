using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Transcription.Tests;

public sealed class ModelTests
{
    private static string? FindFile(string relative)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public async Task MissingModelIsReportedInPlainLanguage()
    {
        var error = await Assert.ThrowsAsync<ModelUnavailableException>(() => new WhisperTranscriber().TranscribeAsync(
            new TranscriptionRequest([], Path.Combine(Path.GetTempPath(), "nope.bin"), TranscriptionPreset.Fast, 2), null, CancellationToken.None));
        Assert.Contains("missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModifiedModelFailsHashCheck()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string? manifest = FindFile("models/manifest.json");
            Assert.NotNull(manifest);
            File.Copy(manifest, Path.Combine(directory, "manifest.json"));
            string model = Path.Combine(directory, "ggml-base-q5_1.bin");
            await File.WriteAllBytesAsync(model, [1, 2, 3]);
            var error = await Assert.ThrowsAsync<ModelUnavailableException>(() => WhisperTranscriber.VerifyModelAsync(model, CancellationToken.None));
            Assert.Contains("damaged", error.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, true); }
    }

    // Needs models/ggml-base-q5_1.bin (build/fetch-model.sh) and a speech fixture: tests/fixtures/speech16k.wav
    // (macOS: say -o x.aiff "..." ; afconvert -f WAVE -d LEI16@16000 -c 1 x.aiff speech16k.wav).
    [Fact, Trait("Category", "Model")]
    public async Task TranscribesShortSpeechFixture()
    {
        string? model = FindFile("models/ggml-base-q5_1.bin");
        string? wav = FindFile("tests/Recorder.Transcription.Tests/fixtures/speech16k.wav");
        if (model is null || wav is null) return; // model not provisioned: nothing to test (CI excludes this category)
        var progress = new List<double>();
        Transcript transcript = await new WhisperTranscriber().TranscribeAsync(
            new TranscriptionRequest([new(StreamRole.Meeting, wav, TimeSpan.FromSeconds(10))], model, TranscriptionPreset.Fast, 4, "en"),
            new Progress<TranscriptionProgress>(p => progress.Add(p.Fraction)), CancellationToken.None);
        Assert.Equal("ggml-base-q5_1", transcript.ModelName);
        Assert.Equal(64, transcript.ModelSha256.Length);
        Assert.NotEmpty(transcript.Segments);
        Assert.All(transcript.Segments, s => { Assert.Equal(SpeakerLabel.Participant, s.Speaker); Assert.True(s.Start >= TimeSpan.FromSeconds(10)); });
        string text = string.Join(' ', transcript.Segments.Select(s => s.Text)).ToLowerInvariant();
        Assert.Contains("interview", text, StringComparison.Ordinal);
    }
}
