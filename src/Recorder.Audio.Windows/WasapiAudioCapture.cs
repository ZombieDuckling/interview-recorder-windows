using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Recorder.Core.Abstractions;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows;

/// <summary>
/// Capture implementation for Windows WASAPI loopback/mic recording.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WasapiAudioCapture : IAudioCapture
{
    // Process loopback activation was added in Windows Server 2022 / Windows 11 era APIs (build 20348+).
    // Docs: ActivateAudioInterfaceAsync + AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK.
    private const int ProcessLoopbackMinBuild = 20348;

    public bool IsProcessLoopbackSupported => OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= ProcessLoopbackMinBuild;

    public IReadOnlyList<AudioDeviceInfo> ListDevices(AudioDeviceKind kind)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        using var enumerator = new MMDeviceEnumerator();
        var dataFlow = kind == AudioDeviceKind.Render ? DataFlow.Render : DataFlow.Capture;
        var role = kind == AudioDeviceKind.Render ? Role.Multimedia : Role.Communications;
        var defaultDevice = TryGetDefaultDevice(enumerator, dataFlow, role)?.ID;

        var devices = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active))
        {
            try
            {
                var format = device.AudioClient.MixFormat;
                devices.Add(new AudioDeviceInfo(
                    device.ID,
                    device.FriendlyName,
                    kind,
                    string.Equals(device.ID, defaultDevice, StringComparison.Ordinal),
                    format.SampleRate,
                    format.Channels));
            }
            finally
            {
                device.Dispose();
            }
        }

        return devices;
    }

    public Task<ICaptureSession> StartAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WASAPI capture is only available on Windows.");

        Directory.CreateDirectory(request.WorkingDirectory);
        var startedAt = DateTimeOffset.UtcNow;
        var manifest = new CaptureManifest(request.WorkingDirectory);
        manifest.Begin(startedAt);

        using var enumerator = new MMDeviceEnumerator();
        var meetingDevice = ResolveMeetingDevice(enumerator, request.MeetingEndpointId);
        var micDevice = ResolveMicDevice(enumerator, request.MicrophoneEndpointId);

        var session = new WasapiCaptureSession(request.WorkingDirectory, manifest, meetingDevice, micDevice, startedAt);
        session.Start();
        return Task.FromResult<ICaptureSession>(session);
    }

    private static MMDevice ResolveMeetingDevice(MMDeviceEnumerator enumerator, string? endpointId)
    {
        if (!string.IsNullOrWhiteSpace(endpointId))
            return enumerator.GetDevice(endpointId);
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static MMDevice ResolveMicDevice(MMDeviceEnumerator enumerator, string? endpointId)
    {
        if (!string.IsNullOrWhiteSpace(endpointId))
            return enumerator.GetDevice(endpointId);

        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }
    }

    private static MMDevice? TryGetDefaultDevice(MMDeviceEnumerator enumerator, DataFlow dataFlow, Role role)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(dataFlow, role);
        }
        catch
        {
            return null;
        }
    }

    private sealed class WasapiCaptureSession : ICaptureSession
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly StreamCaptureWorker meetingWorker;
        private readonly StreamCaptureWorker micWorker;
        private readonly MMDevice meetingDevice;
        private readonly MMDevice micDevice;
        private readonly WasapiLoopbackCapture meetingCapture;
        private readonly WasapiCapture micCapture;
        private int stopped;
        private bool disposed;

        public event EventHandler<LevelSample>? LevelsAvailable;
        public event EventHandler<CaptureFault>? FaultRaised;

        public TimeSpan Elapsed => clock.Elapsed;

        internal WasapiCaptureSession(
            string workingDirectory,
            CaptureManifest manifest,
            MMDevice meetingDevice,
            MMDevice micDevice,
            DateTimeOffset startedAtUtc)
        {
            this.meetingDevice = meetingDevice;
            this.micDevice = micDevice;

            meetingWorker = new StreamCaptureWorker(workingDirectory, StreamRole.Meeting, manifest, clock, RaiseLevel, RaiseFault);
            micWorker = new StreamCaptureWorker(workingDirectory, StreamRole.Microphone, manifest, clock, RaiseLevel, RaiseFault);

            meetingCapture = new WasapiLoopbackCapture(meetingDevice);
            micCapture = new WasapiCapture(micDevice);

            meetingCapture.DataAvailable += (_, args) => meetingWorker.OnData(args.Buffer, args.BytesRecorded, meetingCapture.WaveFormat);
            meetingCapture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                    RaiseFault(StreamRole.Meeting, CaptureFaultKind.Other, recovered: false, args.Exception.HResult);
            };

            micCapture.DataAvailable += (_, args) => micWorker.OnData(args.Buffer, args.BytesRecorded, micCapture.WaveFormat);
            micCapture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null)
                    RaiseFault(StreamRole.Microphone, CaptureFaultKind.Other, recovered: false, args.Exception.HResult);
            };
        }

        internal void Start()
        {
            meetingCapture.StartRecording();
            micCapture.StartRecording();
        }

        public async Task<CaptureResult> StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref stopped, 1) != 0)
                throw new InvalidOperationException("Capture already stopped.");

            meetingCapture.StopRecording();
            micCapture.StopRecording();

            var meetingStream = await meetingWorker.StopAsync(cancellationToken).ConfigureAwait(false);
            var micStream = await micWorker.StopAsync(cancellationToken).ConfigureAwait(false);
            var streams = new List<StreamFiles>(2);
            if (meetingStream is not null)
                streams.Add(meetingStream);
            if (micStream is not null)
                streams.Add(micStream);

            var duration = streams.Count == 0 ? TimeSpan.Zero : streams.Max(stream => stream.StartOffset + stream.Duration);
            return new CaptureResult(streams, duration, Complete: true);
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;
            disposed = true;

            if (Volatile.Read(ref stopped) == 0)
            {
                try
                {
                    await StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort dispose path.
                }
            }

            meetingCapture.Dispose();
            micCapture.Dispose();
            meetingDevice.Dispose();
            micDevice.Dispose();
        }

        private void RaiseLevel(LevelSample sample) => LevelsAvailable?.Invoke(this, sample);

        private void RaiseFault(StreamRole role, CaptureFaultKind kind, bool recovered, int diagnosticCode) =>
            FaultRaised?.Invoke(this, new CaptureFault(role, kind, recovered, diagnosticCode));
    }

    private sealed class StreamCaptureWorker : IDisposable
    {
        private readonly StreamRole role;
        private readonly CaptureManifest manifest;
        private readonly Stopwatch clock;
        private readonly Action<LevelSample> publishLevels;
        private readonly Action<StreamRole, CaptureFaultKind, bool, int> publishFault;
        private readonly PcmChunkWriter writer;
        private readonly string directory;
        private readonly BlockingCollection<short[]> queue = new(256);
        private readonly Task writerTask;
        private readonly object gate = new();
        private long firstFrameOffset = -1;
        private bool registered;
        private DateTime lastLevelAtUtc = DateTime.MinValue;
        private long lastGapFaultFrame;
        private bool stopped;

        internal StreamCaptureWorker(
            string workingDirectory,
            StreamRole role,
            CaptureManifest manifest,
            Stopwatch clock,
            Action<LevelSample> publishLevels,
            Action<StreamRole, CaptureFaultKind, bool, int> publishFault)
        {
            this.role = role;
            this.manifest = manifest;
            this.clock = clock;
            this.publishLevels = publishLevels;
            this.publishFault = publishFault;
            directory = Path.Combine(Path.GetFullPath(workingDirectory), role == StreamRole.Meeting ? "capture/meeting" : "capture/mic");
            writer = new PcmChunkWriter(workingDirectory, role, chunk => manifest.CloseChunk(role, chunk));
            writerTask = Task.Run(WriterLoopAsync);
        }

        internal void OnData(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            if (bytesRecorded <= 0 || stopped)
                return;

            var converted = PcmFrameConverter.ToMono16Pcm48k(buffer.AsSpan(0, bytesRecorded), format);
            if (converted.Length == 0)
                return;

            EnsureRegistered();
            EmitLevels(converted);

            if (!queue.TryAdd(converted))
            {
                _ = queue.TryTake(out _);
                if (!queue.TryAdd(converted))
                    publishFault(role, CaptureFaultKind.WriteFailed, recovered: false, diagnosticCode: unchecked((int)0x80070070));
            }
        }

        internal async Task<StreamFiles?> StopAsync(CancellationToken cancellationToken)
        {
            stopped = true;
            queue.CompleteAdding();
            await writerTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            writer.Complete();

            var files = writer.Chunks.Select(chunk => chunk.File).ToArray();
            if (files.Length == 0)
                return null;

            var startOffset = Math.Max(0, Volatile.Read(ref firstFrameOffset));
            return new StreamFiles(
                role,
                directory,
                files,
                PcmChunkWriter.SampleRate,
                1,
                TimeSpan.FromSeconds(writer.FramesWritten / (double)PcmChunkWriter.SampleRate),
                TimeSpan.FromSeconds(startOffset / (double)PcmChunkWriter.SampleRate),
                IsProcessLoopback: false);
        }

        public void Dispose()
        {
            queue.CompleteAdding();
            queue.Dispose();
            writer.Dispose();
        }

        private async Task WriterLoopAsync()
        {
            try
            {
                while (!queue.IsCompleted || queue.Count > 0)
                {
                    if (!queue.TryTake(out var block, millisecondsTimeout: 20))
                    {
                        FillSilenceByClock();
                        continue;
                    }

                    writer.Write(block);
                    FillSilenceByClock();
                    await Task.Yield();
                }

                FillSilenceByClock();
            }
            catch (Exception ex)
            {
                publishFault(role, CaptureFaultKind.WriteFailed, recovered: false, ex.HResult);
                throw;
            }
        }

        private void FillSilenceByClock()
        {
            var offset = Volatile.Read(ref firstFrameOffset);
            if (offset < 0)
                return;

            var elapsedFrames = (long)(clock.Elapsed.TotalSeconds * PcmChunkWriter.SampleRate);
            var lag = StreamClockAligner.ComputeMissingFrames(offset, elapsedFrames, writer.FramesWritten);
            if (lag <= 0)
                return;

            writer.WriteSilence(lag);
            if (StreamClockAligner.IsGapLongerThanTwoSeconds(lag) && elapsedFrames - lastGapFaultFrame > PcmChunkWriter.SampleRate * 2)
            {
                lastGapFaultFrame = elapsedFrames;
                publishFault(role, CaptureFaultKind.Other, recovered: true, diagnosticCode: 2001);
            }
        }

        private void EnsureRegistered()
        {
            if (registered)
                return;

            lock (gate)
            {
                if (registered)
                    return;
                var offset = (long)(clock.Elapsed.TotalSeconds * PcmChunkWriter.SampleRate);
                firstFrameOffset = offset;
                manifest.RegisterStream(role, offset, processLoopback: false);
                registered = true;
            }
        }

        private void EmitLevels(short[] samples)
        {
            var now = DateTime.UtcNow;
            if ((now - lastLevelAtUtc).TotalMilliseconds < 50)
                return;
            lastLevelAtUtc = now;

            var peak = 0f;
            double sum = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                var normalized = samples[i] / 32768f;
                var abs = Math.Abs(normalized);
                if (abs > peak)
                    peak = abs;
                sum += normalized * normalized;
            }

            var rms = samples.Length == 0 ? 0f : (float)Math.Sqrt(sum / samples.Length);
            publishLevels(new LevelSample(role, ToDbfs(peak), ToDbfs(rms)));
        }

        private static float ToDbfs(float linear)
        {
            if (linear <= 0.0000001f)
                return -120f;
            return 20f * MathF.Log10(linear);
        }
    }

}
