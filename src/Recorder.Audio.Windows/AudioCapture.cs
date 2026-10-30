namespace Recorder.Audio.Windows;

using Recorder.Core.Abstractions;

public sealed class AudioCapture : IAudioCapture
{
    private readonly WasapiAudioCapture inner = new();

    public IReadOnlyList<AudioDeviceInfo> ListDevices(AudioDeviceKind kind) => inner.ListDevices(kind);

    public bool IsProcessLoopbackSupported => inner.IsProcessLoopbackSupported;

    public Task<ICaptureSession> StartAsync(CaptureRequest request, CancellationToken cancellationToken) =>
        inner.StartAsync(request, cancellationToken);
}
