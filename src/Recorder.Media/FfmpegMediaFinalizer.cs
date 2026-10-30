using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Media;

/// <summary>Streams capture chunks through the bundled (LGPL) FFmpeg executable. No audio is held in managed memory.</summary>
public sealed partial class FfmpegMediaFinalizer : IMediaFinalizer
{
    private const string TempPrefix = ".recorder-";
    private readonly string? executablePath;

    public FfmpegMediaFinalizer(string? executablePath = null) => this.executablePath = executablePath;

    public async Task<FinalizeResult> FinalizeAsync(CaptureResult capture, string outputMp4Path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();
        string output = Path.GetFullPath(outputMp4Path);
        string outputDirectory = Path.GetDirectoryName(output) ?? throw new ArgumentException("The output file needs a folder.", nameof(outputMp4Path));
        string sessionCode = Path.GetFileName(outputDirectory);
        if (!SessionCodePattern().IsMatch(sessionCode))
            throw new ArgumentException("The audio file must be written inside the session folder, which is named with the session code.", nameof(outputMp4Path));
        if (capture.Streams.Count == 0) throw new ArgumentException("There is no recorded audio to finalize.", nameof(capture));

        var inputs = capture.Streams.Select(s => Prepare(s, output)).ToList();
        TimeSpan total = inputs.Max(i => i.StartOffset + i.Length);
        string ffmpeg = LocateExecutable();
        string temp = Path.Combine(outputDirectory, TempPrefix + Guid.NewGuid().ToString("N") + ".mp4");
        var lists = new List<string>();
        try
        {
            var args = new List<string> { "-nostdin", "-hide_banner", "-v", "error", "-nostats", "-progress", "pipe:1", "-y" };
            var filter = new StringBuilder();
            for (int i = 0; i < inputs.Count; i++)
            {
                string list = Path.Combine(outputDirectory, TempPrefix + Guid.NewGuid().ToString("N") + ".txt");
                lists.Add(list);
                await File.WriteAllTextAsync(list, ConcatList(inputs[i].Paths), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                args.AddRange(["-f", "concat", "-safe", "0", "-i", list]);
                filter.Append(CultureInfo.InvariantCulture, $"[{i}:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=mono");
                long delayMs = (long)Math.Round(inputs[i].StartOffset.TotalMilliseconds);
                if (delayMs > 0) filter.Append(CultureInfo.InvariantCulture, $",adelay={delayMs}:all=1");
                filter.Append(CultureInfo.InvariantCulture, $",apad=whole_dur={total.TotalSeconds:0.######}[s{i}];");
            }
            for (int i = 0; i < inputs.Count; i++) filter.Append(CultureInfo.InvariantCulture, $"[s{i}]");
            filter.Append(CultureInfo.InvariantCulture, $"amix=inputs={inputs.Count}:normalize=0:duration=longest,");
            // -1 dBFS; level=0 turns off the limiter's automatic make-up gain so quiet mixes stay unchanged.
            filter.Append("alimiter=limit=0.891251:attack=5:release=50:level=0[out]");
            args.AddRange(["-filter_complex", filter.ToString(), "-map", "[out]", "-map_metadata", "-1", "-map_chapters", "-1",
                "-fflags", "+bitexact", "-flags:a", "+bitexact", "-c:a", "aac", "-profile:a", "aac_low", "-b:a", "128k", "-ar", "48000", "-ac", "1",
                "-metadata", "title=" + sessionCode, "-movflags", "+faststart", "-f", "mp4", temp]);

            await RunAsync(ffmpeg, args, total, progress, cancellationToken).ConfigureAwait(false);
            string hash = await HashAsync(temp, cancellationToken).ConfigureAwait(false);
            File.Move(temp, output, overwrite: true);
            progress?.Report(1);
            return new FinalizeResult(output, total, hash);
        }
        finally
        {
            foreach (string list in lists) TryDelete(list);
            TryDelete(temp);
        }
    }

    public async Task<string> ConvertForTranscriptionAsync(StreamFiles stream, string outputWavPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        string output = Path.GetFullPath(outputWavPath);
        string outputDirectory = Path.GetDirectoryName(output) ?? throw new ArgumentException("The output file needs a folder.", nameof(outputWavPath));
        Input input = Prepare(stream, output);
        string ffmpeg = LocateExecutable();
        string temp = Path.Combine(outputDirectory, TempPrefix + Guid.NewGuid().ToString("N") + ".wav");
        string list = Path.Combine(outputDirectory, TempPrefix + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllTextAsync(list, ConcatList(input.Paths), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await RunAsync(ffmpeg, ["-nostdin", "-hide_banner", "-v", "error", "-nostats", "-y", "-f", "concat", "-safe", "0", "-i", list,
                "-map_metadata", "-1", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", "-f", "wav", temp], null, null, cancellationToken).ConfigureAwait(false);
            File.Move(temp, output, overwrite: true);
            return output;
        }
        finally
        {
            TryDelete(list);
            TryDelete(temp);
        }
    }

    private sealed record Input(IReadOnlyList<string> Paths, TimeSpan StartOffset, TimeSpan Length);

    private static Input Prepare(StreamFiles stream, string output)
    {
        if (stream.ChunkPaths.Count == 0) throw new ArgumentException("A recorded stream has no audio files.", nameof(stream));
        long frames = 0;
        var paths = new List<string>();
        foreach (string chunk in stream.ChunkPaths)
        {
            if (string.IsNullOrWhiteSpace(chunk) || chunk.Any(char.IsControl))
                throw new ArgumentException("A recorded audio file name contains characters that are not allowed.", nameof(stream));
            string full = Path.GetFullPath(chunk);
            if (string.Equals(full, output, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("The output file would overwrite an original recording.", nameof(output));
            paths.Add(full);
        }
        foreach (string full in paths)
        {
            WaveInfo.Header header = WaveInfo.Read(full);
            if (header.SampleRate != stream.SampleRate || header.Channels != stream.Channels)
                throw new InvalidDataException("A recorded audio file does not match the format that was captured.");
            frames += header.Frames;
        }
        return new Input(paths, stream.StartOffset, TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / stream.SampleRate));
    }

    private static string ConcatList(IReadOnlyList<string> paths)
    {
        var text = new StringBuilder();
        foreach (string path in paths)
            text.Append("file '").Append(path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal)).Append("'\n");
        return text.ToString();
    }

    private string LocateExecutable()
    {
        string path = executablePath ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        return File.Exists(path) ? path
            : throw new FileNotFoundException("The audio encoder (ffmpeg) is missing from the application folder. Reinstall the application.");
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan? total, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Win32Exception) { throw new InvalidOperationException("The audio encoder (ffmpeg) could not be started."); }
        using var registration = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });
        // Drain stderr so the process never blocks; its text can contain paths, so it is not surfaced.
        Task drain = process.StandardError.ReadToEndAsync(CancellationToken.None);
        double last = 0;
        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false)) is not null)
        {
            if (progress is null || total is not { TotalSeconds: > 0 } t || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) continue;
            if (!long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long us) || us < 0) continue;
            double fraction = Math.Min(0.99, us / 1e6 / t.TotalSeconds);
            if (fraction <= last) continue;
            last = fraction;
            progress.Report(fraction);
        }
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        await drain.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(FormattableString.Invariant($"The audio encoder stopped with error code {process.ExitCode}."));
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [GeneratedRegex(@"^IR-\d{8}-\d{6}-[0-9A-F]{4}$")]
    private static partial Regex SessionCodePattern();
}
