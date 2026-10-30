using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Media.Tests;

internal sealed class MediaFixture : IDisposable
{
    internal const string SessionCode = "IR-20261031-091500-7F3A";
    private readonly string root = Path.Combine(Path.GetTempPath(), "recorder-media-" + Guid.NewGuid().ToString("N"));
    internal string SessionDirectory { get; }
    internal string Output => Path.Combine(SessionDirectory, "interview_audio.mp4");

    internal MediaFixture()
    {
        SessionDirectory = Path.Combine(root, SessionCode);
        Directory.CreateDirectory(SessionDirectory);
    }

    internal StreamFiles Stream(StreamRole role, double seconds = 1, double amplitude = .1, double offset = 0,
        int chunks = 1, string chunkName = "chunk", int sampleRate = 48000)
    {
        string directory = Path.Combine(SessionDirectory, role.ToString());
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        for (int index = 0; index < chunks; index++)
        {
            string path = Path.Combine(directory, chunkName + index.ToString(CultureInfo.InvariantCulture) + ".wav");
            WriteWave(path, (int)Math.Round(seconds * sampleRate / chunks), sampleRate, amplitude);
            paths.Add(path);
        }
        return new StreamFiles(role, directory, paths, sampleRate, 1, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(offset), false);
    }

    internal static void WriteWave(string path, int frames, int rate, double amplitude)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8, false);
        writer.Write("RIFF"u8); writer.Write(36 + frames * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(frames * 2);
        for (int frame = 0; frame < frames; frame++)
            writer.Write((short)Math.Round(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * 1000 * frame / rate)));
    }

    internal static async Task<JsonDocument> ProbeAsync(string path)
    {
        string output = await RunAsync(MediaFactAttribute.ProbePath!, ["-v", "error", "-show_streams", "-show_format", "-of", "json", path]);
        return JsonDocument.Parse(output);
    }

    internal async Task<float[]> DecodeAsync(string path)
    {
        string raw = Path.Combine(SessionDirectory, "test-decode.f32");
        await RunAsync(MediaFactAttribute.ExecutablePath!, ["-v", "error", "-y", "-i", path, "-f", "f32le", "-ac", "1", "-ar", "48000", raw]);
        byte[] bytes = await File.ReadAllBytesAsync(raw);
        File.Delete(raw);
        var result = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    internal static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true } };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    internal void AssertNoTemporaryFiles()
        => Assert.Empty(Directory.EnumerateFileSystemEntries(SessionDirectory, ".recorder-*"));

    public void Dispose() => Directory.Delete(root, true);
}

public sealed class MediaFactAttribute : FactAttribute
{
    internal static string? ExecutablePath { get; } = FindExecutable("ffmpeg");
    internal static string? ProbePath { get; } = FindExecutable("ffprobe");

    public MediaFactAttribute()
    {
        if (ExecutablePath is null || ProbePath is null)
            Skip = "FFmpeg and ffprobe must be installed or staged in third_party/ffmpeg/bin; set RECORDER_TEST_FFMPEG and RECORDER_TEST_FFPROBE for explicit paths.";
    }

    private static string? FindExecutable(string name)
    {
        string? explicitPath = Environment.GetEnvironmentVariable("RECORDER_TEST_" + name.ToUpperInvariant());
        if (explicitPath is not null && File.Exists(explicitPath)) return explicitPath;
        string executable = name + (OperatingSystem.IsWindows() ? ".exe" : "");
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, executable), Path.Combine("/opt/homebrew/bin", executable) };
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            candidates.Add(Path.Combine(directory.FullName, "third_party", "ffmpeg", "bin", executable));
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(path => Path.Combine(path, executable)));
        return candidates.FirstOrDefault(File.Exists);
    }
}
