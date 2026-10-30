using NAudio.Wave;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

internal delegate void CapturePacketHandler(ReadOnlySpan<byte> data, long deviceFrame, long qpc, bool timestampError, bool discontinuity);
internal sealed record CaptureDeviceChange(string? Id, AudioDeviceKind Kind, CaptureFaultKind Reason, bool DefaultChanged = false);

internal interface ICaptureInput : IAsyncDisposable
{
    event CapturePacketHandler? Packet;
    event Action<Exception?>? Stopped;
    WaveFormat Format { get; }
    string? DeviceId { get; }
    bool ProcessLoopback { get; }
    bool IsRunning { get; }
    void Start();
}

internal interface ICaptureInputFactory : IDisposable
{
    event Action<CaptureDeviceChange>? Changed;
    Task<ICaptureInput> OpenAsync(StreamRole role, CaptureRequest request, bool allowProcessLoopback);
}
