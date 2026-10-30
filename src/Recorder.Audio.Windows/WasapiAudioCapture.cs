using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

[SupportedOSPlatform("windows")]
public sealed class WasapiAudioCapture : IAudioCapture
{
    // Microsoft explicitly requires Windows build 20348+ for process-tree loopback:
    // https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/
    // Ordinary Windows 10 builds (e.g. 19045) use endpoint loopback instead.
    public bool IsProcessLoopbackSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);

    public IReadOnlyList<AudioDeviceInfo> ListDevices(AudioDeviceKind kind)
    {
        if (!OperatingSystem.IsWindows()) return [];
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = TryDefault(enumerator, kind);
        var devices = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(Flow(kind), DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    using var client = device.CreateAudioClient();
                    var format = client.MixFormat;
                    devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, kind, device.ID == defaultDevice?.ID,
                        format.SampleRate, format.Channels));
                }
                catch (System.Runtime.InteropServices.COMException) { /* It was removed during enumeration. */ }
            }
        }
        return devices;
    }

    public Task<ICaptureSession> StartAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WASAPI capture is only available on Windows.");
        // Construct native clients off the WPF context; NAudio must never marshal stop notifications to a blocked UI.
        return Task.Run<ICaptureSession>(async () =>
        {
            var factory = new WasapiInputFactory();
            AudioCaptureSession? session = null;
            try
            {
                session = new AudioCaptureSession(request, factory, IsProcessLoopbackSupported);
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                return session;
            }
            catch
            {
                if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
                else factory.Dispose();
                throw;
            }
        }, cancellationToken);
    }

    private static DataFlow Flow(AudioDeviceKind kind) => kind == AudioDeviceKind.Render ? DataFlow.Render : DataFlow.Capture;
    private static Role DeviceRole(AudioDeviceKind kind) => kind == AudioDeviceKind.Render ? Role.Multimedia : Role.Communications;

    private static MMDevice? TryDefault(MMDeviceEnumerator enumerator, AudioDeviceKind kind)
    {
        try { return enumerator.GetDefaultAudioEndpoint(Flow(kind), DeviceRole(kind)); }
        catch (System.Runtime.InteropServices.COMException) when (kind == AudioDeviceKind.Capture)
        {
            try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); }
            catch (System.Runtime.InteropServices.COMException) { return null; }
        }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    private sealed class WasapiInputFactory : ICaptureInputFactory
    {
        private readonly MMDeviceEnumerator enumerator = new();
        private readonly MMDeviceNotificationClient notifications;
        public event Action<CaptureDeviceChange>? Changed;

        internal WasapiInputFactory()
        {
            notifications = enumerator.CreateNotificationClient(useSynchronizationContext: false);
            notifications.DefaultDeviceChanged += (_, args) =>
            {
                var kind = args.Flow == DataFlow.Render ? AudioDeviceKind.Render : AudioDeviceKind.Capture;
                if (args.Role == DeviceRole(kind) || (kind == AudioDeviceKind.Capture && args.Role == Role.Console))
                    Changed?.Invoke(new(args.DeviceId, kind, CaptureFaultKind.DefaultDeviceChanged, true));
            };
            notifications.DeviceRemoved += (_, args) => NotifyBoth(args.DeviceId, CaptureFaultKind.DeviceRemoved);
            notifications.DeviceStateChanged += (_, args) =>
            {
                if (args.NewState != DeviceState.Active) NotifyBoth(args.DeviceId, CaptureFaultKind.DeviceRemoved);
            };
            notifications.PropertyValueChanged += (_, args) =>
            {
                if (args.PropertyKey.Equals(PropertyKeys.PKEY_AudioEngine_DeviceFormat) || args.PropertyKey.Equals(PropertyKeys.PKEY_AudioEngine_OEMFormat))
                    NotifyBoth(args.DeviceId, CaptureFaultKind.FormatChanged);
            };
        }

        private void NotifyBoth(string? id, CaptureFaultKind reason)
        {
            Changed?.Invoke(new(id, AudioDeviceKind.Render, reason));
            Changed?.Invoke(new(id, AudioDeviceKind.Capture, reason));
        }

        public async Task<ICaptureInput> OpenAsync(StreamRole role, CaptureRequest request, bool allowProcessLoopback)
        {
            MMDevice? device = null;
            try
            {
                var builder = new WasapiRecorderBuilder().WithSharedMode().WithBufferLength(50).WithMmcssThreadPriority("Audio");
                var process = role == StreamRole.Meeting && allowProcessLoopback;
                if (process)
                {
                    if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
                        throw new PlatformNotSupportedException("Process loopback requires Windows build 20348.");
                    builder.WithProcessLoopback(checked((uint)request.BrowserProcessId!.Value), ProcessLoopbackMode.IncludeTargetProcessTree)
                        .WithFormat(new WaveFormat(48000, 16, 1));
                }
                else
                {
                    var kind = role == StreamRole.Meeting ? AudioDeviceKind.Render : AudioDeviceKind.Capture;
                    var id = role == StreamRole.Meeting ? request.MeetingEndpointId : request.MicrophoneEndpointId;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        try
                        {
                            device = enumerator.GetDevice(id);
                            if (device.State != DeviceState.Active) { device.Dispose(); device = null; }
                        }
                        catch (System.Runtime.InteropServices.COMException)
                        {
                            device?.Dispose();
                            device = null;
                        }
                    }
                    device ??= TryDefault(enumerator, kind) ?? throw new InvalidOperationException("No active audio endpoint is available.");
                    builder.WithDevice(device);
                    if (role == StreamRole.Meeting) builder.WithLoopbackCapture().WithPollingSync();
                }
                var recorder = await builder.BuildAsync().ConfigureAwait(false);
                return new WasapiInput(recorder, device, process);
            }
            catch
            {
                device?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            notifications.Dispose();
            enumerator.Dispose();
        }
    }

    private sealed class WasapiInput : ICaptureInput
    {
        private readonly WasapiRecorder recorder;
        private readonly MMDevice? device;
        public bool ProcessLoopback { get; }
        public string? DeviceId => recorder.DeviceId;
        public WaveFormat Format => recorder.WaveFormat;
        public bool IsRunning => recorder.CaptureState == CaptureState.Capturing;
        public event CapturePacketHandler? Packet;
        public event Action<Exception?>? Stopped;

        internal WasapiInput(WasapiRecorder recorder, MMDevice? device, bool process)
        {
            this.recorder = recorder;
            this.device = device;
            ProcessLoopback = process;
            recorder.DataAvailable += (buffer, flags, position, qpc) => Packet?.Invoke(buffer, position, qpc,
                (flags & AudioClientBufferFlags.TimestampError) != 0, (flags & AudioClientBufferFlags.DataDiscontinuity) != 0);
            recorder.RecordingStopped += (_, args) => Stopped?.Invoke(args.Exception);
        }

        public void Start() => recorder.StartRecording();
        public async ValueTask DisposeAsync()
        {
            try { await recorder.DisposeAsync().ConfigureAwait(false); }
            finally { device?.Dispose(); }
        }
    }
}
