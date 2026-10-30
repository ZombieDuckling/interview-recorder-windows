using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recorder.Core.Formats;

[JsonConverter(typeof(JsonStringEnumConverter<SessionPlatform>))]
public enum SessionPlatform
{
    [JsonStringEnumMemberName("macos")]
    Macos,

    [JsonStringEnumMemberName("windows")]
    Windows,
}

[JsonConverter(typeof(JsonStringEnumConverter<SessionArchitecture>))]
public enum SessionArchitecture
{
    [JsonStringEnumMemberName("arm64")]
    Arm64,

    [JsonStringEnumMemberName("x86_64")]
    X8664,

    [JsonStringEnumMemberName("x64")]
    X64,
}

[JsonConverter(typeof(JsonStringEnumConverter<StreamJsonKind>))]
public enum StreamJsonKind
{
    [JsonStringEnumMemberName("meeting")]
    Meeting,

    [JsonStringEnumMemberName("mic")]
    Mic,
}

[JsonConverter(typeof(JsonStringEnumConverter<StreamCaptureSource>))]
public enum StreamCaptureSource
{
    [JsonStringEnumMemberName("screencapturekit")]
    ScreenCaptureKit,

    [JsonStringEnumMemberName("coreaudio-tap")]
    CoreAudioTap,

    [JsonStringEnumMemberName("avaudioengine")]
    AvAudioEngine,

    [JsonStringEnumMemberName("wasapi-loopback")]
    WasapiLoopback,

    [JsonStringEnumMemberName("wasapi-process-loopback")]
    WasapiProcessLoopback,

    [JsonStringEnumMemberName("wasapi-capture")]
    WasapiCapture,
}

[JsonConverter(typeof(JsonStringEnumConverter<CaptureDeviceType>))]
public enum CaptureDeviceType
{
    [JsonStringEnumMemberName("builtin")]
    Builtin,

    [JsonStringEnumMemberName("usb")]
    Usb,

    [JsonStringEnumMemberName("bluetooth")]
    Bluetooth,

    [JsonStringEnumMemberName("virtual")]
    Virtual,

    [JsonStringEnumMemberName("system")]
    System,

    [JsonStringEnumMemberName("unknown")]
    Unknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<SessionErrorStage>))]
public enum SessionErrorStage
{
    Recording,
    Finalizing,
    Transcribing,
    Exporting,
    Recovery,
}

[JsonConverter(typeof(JsonStringEnumConverter<IntegrityKeyStore>))]
public enum IntegrityKeyStore
{
    [JsonStringEnumMemberName("macos-keychain")]
    MacosKeychain,

    [JsonStringEnumMemberName("windows-dpapi")]
    WindowsDpapi,
}

[JsonConverter(typeof(JsonStringEnumConverter<SessionAuditEvent>))]
public enum SessionAuditEvent
{
    [JsonStringEnumMemberName("created")]
    Created,

    [JsonStringEnumMemberName("recording_started")]
    RecordingStarted,

    [JsonStringEnumMemberName("recording_stopped")]
    RecordingStopped,

    [JsonStringEnumMemberName("transcribed")]
    Transcribed,

    [JsonStringEnumMemberName("edited")]
    Edited,

    [JsonStringEnumMemberName("exported")]
    Exported,

    [JsonStringEnumMemberName("reviewed")]
    Reviewed,

    [JsonStringEnumMemberName("opened")]
    Opened,

    [JsonStringEnumMemberName("deleted_files")]
    DeletedFiles,
}

public sealed class SessionConsent
{
    [JsonPropertyOrder(0)]
    public bool Confirmed { get; set; }

    [JsonPropertyOrder(1)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset ConfirmedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionStateHistoryEntry
{
    [JsonPropertyOrder(0)]
    public SessionState State { get; set; }

    [JsonPropertyOrder(1)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset At { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionErrorInfo
{
    [JsonPropertyOrder(0)]
    public SessionErrorStage Stage { get; set; }

    [JsonPropertyOrder(1)]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset At { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionAppInfo
{
    [JsonPropertyOrder(0)]
    public string Name { get; set; } = "Interview Recorder";

    [JsonPropertyOrder(1)]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    public SessionPlatform Platform { get; set; }

    [JsonPropertyOrder(3)]
    public string OsVersion { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public SessionArchitecture Arch { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionRecordingInfo
{
    [JsonPropertyOrder(0)]
    [JsonConverter(typeof(NullableRfc3339UtcJsonConverter))]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyOrder(1)]
    [JsonConverter(typeof(NullableRfc3339UtcJsonConverter))]
    public DateTimeOffset? StoppedAt { get; set; }

    [JsonPropertyOrder(2)]
    public double? DurationSeconds { get; set; }

    [JsonPropertyOrder(3)]
    public bool? Recovered { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class CaptureChunkInfo
{
    [JsonPropertyOrder(0)]
    public string File { get; set; } = string.Empty;

    [JsonPropertyOrder(1)]
    public int Frames { get; set; }

    [JsonPropertyOrder(2)]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Recovered { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionStreamInfo
{
    [JsonPropertyOrder(0)]
    public StreamJsonKind Kind { get; set; }

    [JsonPropertyOrder(1)]
    public StreamCaptureSource Source { get; set; }

    [JsonPropertyOrder(2)]
    public CaptureDeviceType DeviceType { get; set; }

    [JsonPropertyOrder(3)]
    public int SampleRate { get; set; }

    [JsonPropertyOrder(4)]
    public int Channels { get; set; }

    [JsonPropertyOrder(5)]
    public int BitsPerSample { get; set; }

    [JsonPropertyOrder(6)]
    public int ChunkFrames { get; set; }

    [JsonPropertyOrder(7)]
    public int FirstFrameOffset { get; set; }

    [JsonPropertyOrder(8)]
    public List<CaptureChunkInfo> Chunks { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionTranscriptionInfo
{
    [JsonPropertyOrder(0)]
    public string Engine { get; set; } = "whisper.cpp";

    [JsonPropertyOrder(1)]
    public string EngineVersion { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    public string ModelSha256 { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyOrder(5)]
    public int Threads { get; set; }

    [JsonPropertyOrder(6)]
    [JsonConverter(typeof(NullableRfc3339UtcJsonConverter))]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyOrder(7)]
    [JsonConverter(typeof(NullableRfc3339UtcJsonConverter))]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyOrder(8)]
    public double? PeakMemoryMB { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionOutputFile
{
    [JsonPropertyOrder(0)]
    public string File { get; set; } = string.Empty;

    [JsonPropertyOrder(1)]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyOrder(2)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset WrittenAt { get; set; }

    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DurationSeconds { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionOutputs
{
    [JsonPropertyOrder(0)]
    public SessionOutputFile? Audio { get; set; }

    [JsonPropertyOrder(1)]
    public SessionOutputFile? TranscriptJson { get; set; }

    [JsonPropertyOrder(2)]
    public SessionOutputFile? Docx { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionIntegrity
{
    [JsonPropertyOrder(0)]
    public string Hmac { get; set; } = string.Empty;

    [JsonPropertyOrder(1)]
    public IntegrityKeyStore KeyStore { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionRetention
{
    [JsonPropertyOrder(0)]
    public int? DeleteAfterDays { get; set; }

    [JsonPropertyOrder(1)]
    [JsonConverter(typeof(NullableRfc3339UtcJsonConverter))]
    public DateTimeOffset? DueAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class SessionAuditEntry
{
    [JsonPropertyOrder(0)]
    public SessionAuditEvent Event { get; set; }

    [JsonPropertyOrder(1)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset At { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>session.json root (shared spec section 2).</summary>
public sealed class SessionManifest
{
    [JsonPropertyOrder(0)]
    public string SchemaVersion { get; set; } = "1.0.0";

    [JsonPropertyOrder(1)]
    public Guid SessionId { get; set; }

    [JsonPropertyOrder(2)]
    public string SessionCode { get; set; } = string.Empty;

    [JsonPropertyOrder(3)]
    public string ParticipantId { get; set; } = string.Empty;

    [JsonPropertyOrder(4)]
    public string? StudyCode { get; set; }

    [JsonPropertyOrder(5)]
    public SessionConsent Consent { get; set; } = new();

    [JsonPropertyOrder(6)]
    [JsonConverter(typeof(Rfc3339UtcJsonConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyOrder(7)]
    public SessionState State { get; set; }

    [JsonPropertyOrder(8)]
    public List<SessionStateHistoryEntry> StateHistory { get; set; } = [];

    [JsonPropertyOrder(9)]
    public SessionErrorInfo? Error { get; set; }

    [JsonPropertyOrder(10)]
    public SessionAppInfo App { get; set; } = new();

    [JsonPropertyOrder(11)]
    public SessionRecordingInfo Recording { get; set; } = new();

    [JsonPropertyOrder(12)]
    public List<SessionStreamInfo> Streams { get; set; } = [];

    [JsonPropertyOrder(13)]
    public SessionTranscriptionInfo? Transcription { get; set; }

    [JsonPropertyOrder(14)]
    public SessionOutputs? Outputs { get; set; }

    [JsonPropertyOrder(15)]
    public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyOrder(16)]
    public SessionIntegrity? Integrity { get; set; }

    [JsonPropertyOrder(17)]
    public SessionRetention Retention { get; set; } = new();

    [JsonPropertyOrder(18)]
    public List<SessionAuditEntry> Audit { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
