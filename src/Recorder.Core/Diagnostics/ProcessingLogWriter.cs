using System.Globalization;
using System.Text;

namespace Recorder.Core.Diagnostics;

public enum ProcessingLogCode
{
    AppStart,
    SessionCreated,
    StateChanged,
    StateRefused,
    CaptureStarted,
    CaptureDeviceChanged,
    CaptureGap,
    CaptureError,
    ChunkClosed,
    ChunkRecovered,
    DiskLow,
    PermissionDenied,
    EncodeStarted,
    EncodeDone,
    EncodeError,
    TranscribeStarted,
    TranscribeProgress,
    TranscribeCancelled,
    TranscribeDone,
    TranscribeError,
    ModelMissing,
    ExportDone,
    ExportError,
    HashMismatch,
    RecoveryFound,
    RecoveryDone,
    SessionDeleted,
}

public enum ProcessingLogLevel
{
    Info,
    Warn,
    Error,
}

public sealed class ProcessingLogWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public ProcessingLogWriter(string logPath)
    {
        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8)
        {
            AutoFlush = true,
        };
    }

    public void Info(ProcessingLogCode code) => Write(ProcessingLogLevel.Info, code);

    public void Warn(ProcessingLogCode code) => Write(ProcessingLogLevel.Warn, code);

    public void Error(ProcessingLogCode code) => Write(ProcessingLogLevel.Error, code);

    public void Info(ProcessingLogCode code, IReadOnlyList<ProcessingLogValue> values) =>
        Write(ProcessingLogLevel.Info, code, values);

    public void Warn(ProcessingLogCode code, IReadOnlyList<ProcessingLogValue> values) =>
        Write(ProcessingLogLevel.Warn, code, values);

    public void Error(ProcessingLogCode code, IReadOnlyList<ProcessingLogValue> values) =>
        Write(ProcessingLogLevel.Error, code, values);

    public void Dispose() => _writer.Dispose();

    private void Write(ProcessingLogLevel level, ProcessingLogCode code, IReadOnlyList<ProcessingLogValue>? values = null)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var builder = new StringBuilder()
            .Append(timestamp)
            .Append(' ')
            .Append(level.ToString().ToUpperInvariant())
            .Append(' ')
            .Append(ToCode(code));

        if (values is not null)
        {
            foreach (var value in values)
            {
                builder.Append(' ').Append(value.Name).Append('=').Append(value.Format());
            }
        }

        lock (_gate)
        {
            _writer.WriteLine(builder.ToString());
        }
    }

    private static string ToCode(ProcessingLogCode code) =>
        code switch
        {
            ProcessingLogCode.AppStart => "APP_START",
            ProcessingLogCode.SessionCreated => "SESSION_CREATED",
            ProcessingLogCode.StateChanged => "STATE_CHANGED",
            ProcessingLogCode.StateRefused => "STATE_REFUSED",
            ProcessingLogCode.CaptureStarted => "CAPTURE_STARTED",
            ProcessingLogCode.CaptureDeviceChanged => "CAPTURE_DEVICE_CHANGED",
            ProcessingLogCode.CaptureGap => "CAPTURE_GAP",
            ProcessingLogCode.CaptureError => "CAPTURE_ERROR",
            ProcessingLogCode.ChunkClosed => "CHUNK_CLOSED",
            ProcessingLogCode.ChunkRecovered => "CHUNK_RECOVERED",
            ProcessingLogCode.DiskLow => "DISK_LOW",
            ProcessingLogCode.PermissionDenied => "PERMISSION_DENIED",
            ProcessingLogCode.EncodeStarted => "ENCODE_STARTED",
            ProcessingLogCode.EncodeDone => "ENCODE_DONE",
            ProcessingLogCode.EncodeError => "ENCODE_ERROR",
            ProcessingLogCode.TranscribeStarted => "TRANSCRIBE_STARTED",
            ProcessingLogCode.TranscribeProgress => "TRANSCRIBE_PROGRESS",
            ProcessingLogCode.TranscribeCancelled => "TRANSCRIBE_CANCELLED",
            ProcessingLogCode.TranscribeDone => "TRANSCRIBE_DONE",
            ProcessingLogCode.TranscribeError => "TRANSCRIBE_ERROR",
            ProcessingLogCode.ModelMissing => "MODEL_MISSING",
            ProcessingLogCode.ExportDone => "EXPORT_DONE",
            ProcessingLogCode.ExportError => "EXPORT_ERROR",
            ProcessingLogCode.HashMismatch => "HASH_MISMATCH",
            ProcessingLogCode.RecoveryFound => "RECOVERY_FOUND",
            ProcessingLogCode.RecoveryDone => "RECOVERY_DONE",
            ProcessingLogCode.SessionDeleted => "SESSION_DELETED",
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
}

public readonly struct ProcessingLogValue
{
    public ProcessingLogValue(string name, long number)
    {
        Name = name;
        Kind = ProcessingLogValueKind.Number;
        Number = number;
    }

    public ProcessingLogValue(string name, double durationSeconds)
    {
        Name = name;
        Kind = ProcessingLogValueKind.DurationSeconds;
        DurationSeconds = durationSeconds;
    }

    public ProcessingLogValue(string name, SessionRelativePath path)
    {
        Name = name;
        Kind = ProcessingLogValueKind.SessionRelativePath;
        Path = path;
    }

    public ProcessingLogValue(string name, ProcessingLogCode nestedCode)
    {
        Name = name;
        Kind = ProcessingLogValueKind.Code;
        NestedCode = nestedCode;
    }

    public string Name { get; }

    public ProcessingLogValueKind Kind { get; }

    public long Number { get; }

    public double DurationSeconds { get; }

    public SessionRelativePath Path { get; }

    public ProcessingLogCode NestedCode { get; }

    public string Format() =>
        Kind switch
        {
            ProcessingLogValueKind.Number => Number.ToString(CultureInfo.InvariantCulture),
            ProcessingLogValueKind.DurationSeconds => DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            ProcessingLogValueKind.SessionRelativePath => Path.Value,
            ProcessingLogValueKind.Code => ToLogCode(NestedCode),
            _ => throw new InvalidOperationException("Unknown value kind."),
        };

    private static string ToLogCode(ProcessingLogCode code) =>
        code switch
        {
            ProcessingLogCode.AppStart => "APP_START",
            ProcessingLogCode.SessionCreated => "SESSION_CREATED",
            ProcessingLogCode.StateChanged => "STATE_CHANGED",
            ProcessingLogCode.StateRefused => "STATE_REFUSED",
            ProcessingLogCode.CaptureStarted => "CAPTURE_STARTED",
            ProcessingLogCode.CaptureDeviceChanged => "CAPTURE_DEVICE_CHANGED",
            ProcessingLogCode.CaptureGap => "CAPTURE_GAP",
            ProcessingLogCode.CaptureError => "CAPTURE_ERROR",
            ProcessingLogCode.ChunkClosed => "CHUNK_CLOSED",
            ProcessingLogCode.ChunkRecovered => "CHUNK_RECOVERED",
            ProcessingLogCode.DiskLow => "DISK_LOW",
            ProcessingLogCode.PermissionDenied => "PERMISSION_DENIED",
            ProcessingLogCode.EncodeStarted => "ENCODE_STARTED",
            ProcessingLogCode.EncodeDone => "ENCODE_DONE",
            ProcessingLogCode.EncodeError => "ENCODE_ERROR",
            ProcessingLogCode.TranscribeStarted => "TRANSCRIBE_STARTED",
            ProcessingLogCode.TranscribeProgress => "TRANSCRIBE_PROGRESS",
            ProcessingLogCode.TranscribeCancelled => "TRANSCRIBE_CANCELLED",
            ProcessingLogCode.TranscribeDone => "TRANSCRIBE_DONE",
            ProcessingLogCode.TranscribeError => "TRANSCRIBE_ERROR",
            ProcessingLogCode.ModelMissing => "MODEL_MISSING",
            ProcessingLogCode.ExportDone => "EXPORT_DONE",
            ProcessingLogCode.ExportError => "EXPORT_ERROR",
            ProcessingLogCode.HashMismatch => "HASH_MISMATCH",
            ProcessingLogCode.RecoveryFound => "RECOVERY_FOUND",
            ProcessingLogCode.RecoveryDone => "RECOVERY_DONE",
            ProcessingLogCode.SessionDeleted => "SESSION_DELETED",
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
}

public enum ProcessingLogValueKind
{
    Number,
    DurationSeconds,
    SessionRelativePath,
    Code,
}

public readonly struct SessionRelativePath
{
    public SessionRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            throw new ArgumentException("Path must be a non-empty session-relative path.", nameof(value));
        }

        Value = value.Replace('\\', '/');
    }

    public string Value { get; }
}
