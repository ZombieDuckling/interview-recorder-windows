// usage: PipelineProbe <sessionDir with capture/mic/*.wav and capture/meeting/*.wav> <model.bin> <ffmpeg> [threads]
using System.Diagnostics;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;
using Recorder.Core.IO;
using Recorder.Export;
using Recorder.Export.Transcript;
using Recorder.Media;
using Recorder.Transcription;

string dir = Path.GetFullPath(args[0]);
string model = args[1];
string ffmpeg = args[2];
int threads = args.Length > 3 ? int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 2;

StreamFiles Stream(StreamRole role, string folder)
{
    var chunks = Directory.GetFiles(Path.Combine(dir, "capture", folder), "*.wav").Order(StringComparer.Ordinal).ToList();
    long bytes = chunks.Sum(c => new FileInfo(c).Length - 44);
    return new StreamFiles(role, Path.Combine(dir, "capture", folder), chunks, 48000, 1, TimeSpan.FromSeconds(bytes / 2.0 / 48000), TimeSpan.Zero, false);
}

var streams = new[] { Stream(StreamRole.Microphone, "mic"), Stream(StreamRole.Meeting, "meeting") };
var capture = new CaptureResult(streams, streams.Max(s => s.Duration), true);
var sw = Stopwatch.StartNew();
var finalizer = new FfmpegMediaFinalizer(ffmpeg);
FinalizeResult mp4 = await finalizer.FinalizeAsync(capture, Path.Combine(dir, "interview_audio.mp4"), null, CancellationToken.None);
Console.WriteLine($"FINALIZE ok {mp4.Duration.TotalSeconds:F1}s sha256={mp4.Sha256[..12]} in {sw.Elapsed.TotalSeconds:F1}s");

Directory.CreateDirectory(Path.Combine(dir, ".work"));
var inputs = new List<TranscriptionInput>();
foreach (var s in streams)
{
    string wav = Path.Combine(dir, ".work", s.Role + "-16k.wav");
    await finalizer.ConvertForTranscriptionAsync(s, wav, CancellationToken.None);
    inputs.Add(new TranscriptionInput(s.Role, wav, s.StartOffset));
}

sw.Restart();
Transcript t = await new WhisperTranscriber().TranscribeAsync(
    new TranscriptionRequest(Guid.NewGuid(), "IR-20261030-160000-7F3A", inputs, model, TranscriptionPreset.Balanced, threads, "en"),
    new Progress<TranscriptionProgress>(p => { if ((int)(p.Fraction * 100) % 10 == 0) Console.Error.Write($"\r{p.Fraction:P0} "); }),
    CancellationToken.None);
Console.WriteLine($"\nTRANSCRIBE ok {t.Segments.Count} segments in {sw.Elapsed.TotalSeconds:F1}s (threads={threads})");
await TranscriptIO.WriteAsync(Path.Combine(dir, "transcript.json"), t, CancellationToken.None);
foreach (var seg in t.Segments.Take(6)) Console.WriteLine($"  [{seg.Start,7:F1}] {seg.Speaker,-11} {seg.Text}");

string docx = Path.Combine(dir, "interview_transcript.docx");
await new DocxExporter().ExportAsync(new DocxExportRequest("IR-20261030-160000-7F3A", DateTimeOffset.Now, mp4.Duration, null,
    t.Engine.Version, t.Engine.Model, TranscriptMapping.ToDocxSegments(t), docx, DateTimeOffset.Now), CancellationToken.None);
Console.WriteLine($"DOCX ok {new FileInfo(docx).Length} bytes");
Console.WriteLine($"PEAK_WS_MB {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576}");
