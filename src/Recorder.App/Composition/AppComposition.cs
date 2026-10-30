// OWNER: win-lead. Composition root: builds the real services. No network-capable service exists in this graph.
using Recorder.App.Services;
using Recorder.Audio.Windows;
using Recorder.Export;
using Recorder.Media;
using Recorder.Security;
using Recorder.Transcription;

namespace Recorder.App.Composition;

public static class AppComposition
{
    /// <summary>The model and ffmpeg.exe ship next to InterviewRecorder.exe (installer layout: models\, ffmpeg.exe).</summary>
    public static AppServices Build(IUserSettingsStore settings)
    {
        string baseDir = AppContext.BaseDirectory;
        string modelPath = Path.Combine(baseDir, "models", "ggml-base-q5_1.bin");
        string ffmpegPath = Path.Combine(baseDir, "ffmpeg.exe");
        var capture = new WasapiAudioCapture();
        var protector = new FileProtector();
        var pipeline = new SessionPipeline(
            settings,
            capture,
            new CaptureRecovery(),
            new FfmpegMediaFinalizer(File.Exists(ffmpegPath) ? ffmpegPath : null),
            new WhisperTranscriber(),
            new DocxExporter(),
            protector,
            modelPath);
        return new AppServices(settings, pipeline, capture, new RetentionService(), protector);
    }
}
