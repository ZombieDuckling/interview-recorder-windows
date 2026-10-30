// OWNER: win-lead. The one place where capture, finalize, transcription and export meet (spec section 2 state machine).
// Rules: session.json is written by this class only while capture is NOT running (capture writes streams/chunks itself);
// processing.log gets codes and numbers only; every state change goes through SessionStateMachine (atomic write + history).
using System.Runtime.InteropServices;
using Recorder.Core.Abstractions;
using Recorder.Core.Diagnostics;
using Recorder.Core.Formats;
using Recorder.Core.IO;
using Recorder.Core.Naming;
using Recorder.Core.SessionManagement;
using Recorder.Export;
using Recorder.Export.Transcript;

namespace Recorder.App.Services;

public sealed class SessionPipeline : ISessionWorkflow, IDisposable
{
    private const string ManifestFile = "session.json";
    private const string AudioFile = "interview_audio.mp4";
    private const string TranscriptFile = "transcript.json";
    private const string DocxFile = "interview_transcript.docx";

    private readonly IUserSettingsStore _settings;
    private readonly IAudioCapture _capture;
    private readonly ICaptureRecovery _recovery;
    private readonly IMediaFinalizer _finalizer;
    private readonly ITranscriber _transcriber;
    private readonly IDocxExporter _exporter;
    private readonly IFileProtector _protector;
    private readonly string _modelPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ICaptureSession? _captureSession;
    private string? _activeDirectory;
    private string? _recordingDirectory;
    private ProcessingLogWriter? _log;
    private CancellationTokenSource? _operationCts;
    private Timer? _tick;
    private long _micLastSoundTicks;
    private bool _micSilenceWarned;

    public SessionPipeline(
        IUserSettingsStore settings,
        IAudioCapture capture,
        ICaptureRecovery recovery,
        IMediaFinalizer finalizer,
        ITranscriber transcriber,
        IDocxExporter exporter,
        IFileProtector protector,
        string modelPath)
    {
        _settings = settings;
        _capture = capture;
        _recovery = recovery;
        _finalizer = finalizer;
        _transcriber = transcriber;
        _exporter = exporter;
        _protector = protector;
        _modelPath = modelPath;
        Status = new WorkflowStatus("Ready", 0, false, false, false, null);
    }

    public event EventHandler? Changed;

    public event EventHandler<LevelSample>? LevelsAvailable;

    public SessionListItem? ActiveSession { get; private set; }

    public WorkflowStatus Status { get; private set; }

    public bool IsRecording => _captureSession is not null;

    public TimeSpan RecordingElapsed => _captureSession?.Elapsed ?? TimeSpan.Zero;

    public IReadOnlyList<SessionListItem> ListSessions(string dataRoot) => SessionListMapper.List(dataRoot);

    public async Task<SessionListItem> StartAsync(SessionStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConsentConfirmed)
        {
            throw new InvalidOperationException("Recording cannot start until consent is confirmed.");
        }

        if (string.IsNullOrWhiteSpace(request.ParticipantId))
        {
            throw new InvalidOperationException("Enter a pseudonymous participant ID before recording.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_captureSession is not null)
            {
                throw new InvalidOperationException("A recording is already running.");
            }

            string root = _settings.Current.DataRoot;
            Directory.CreateDirectory(root);
            var sessionId = Guid.NewGuid();
            DateTimeOffset startLocal = DateTimeOffset.Now;
            string code = SafeFileNaming.BuildSessionCodeWithCollision(startLocal, sessionId, c => Directory.Exists(Path.Combine(root, c)));
            string dir = Path.Combine(root, code);
            Directory.CreateDirectory(dir);
            if (OperatingSystem.IsWindows())
            {
                _protector.RestrictToCurrentUser(dir);
            }

            _log = new ProcessingLogWriter(Path.Combine(dir, "processing.log"));
            _log.Info(ProcessingLogCode.SessionCreated);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            int? retentionDays = _settings.Current.RetentionDays > 0 ? _settings.Current.RetentionDays : null;
            var manifest = new SessionManifest
            {
                SessionId = sessionId,
                SessionCode = code,
                ParticipantId = request.ParticipantId.Trim(),
                StudyCode = string.IsNullOrWhiteSpace(request.StudyCode) ? null : request.StudyCode.Trim(),
                Consent = new SessionConsent { Confirmed = true, ConfirmedAt = now },
                CreatedAt = now,
                App = new SessionAppInfo
                {
                    Version = typeof(SessionPipeline).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                    Platform = SessionPlatform.Windows,
                    OsVersion = Environment.OSVersion.VersionString,
                    Arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? SessionArchitecture.Arm64 : SessionArchitecture.X64,
                },
                Retention = new SessionRetention
                {
                    DeleteAfterDays = retentionDays,
                    DueAt = SessionRetentionLogic.ComputeDueAt(now, retentionDays),
                },
            };
            SessionAudit.Append(manifest, SessionAuditEvent.Created, now);
            SessionAudit.Append(manifest, SessionAuditEvent.RecordingStarted, now);
            var machine = new SessionStateMachine(manifest, Path.Combine(dir, ManifestFile));
            await machine.TransitionToAsync(SessionState.Recording, now, cancellationToken).ConfigureAwait(false);

            // From here until Stop, only the capture writes session.json (streams, chunks, startedAt).
            ICaptureSession session;
            try
            {
                session = await _capture.StartAsync(
                    new CaptureRequest(dir, request.MeetingEndpointId, request.MicrophoneEndpointId),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ProcessingLogCode.CaptureError);
                await MarkFailedAsync(dir, SessionErrorStage.Recording, "START_FAILED").ConfigureAwait(false);
                SetStatus("Recording could not start: no audio was captured. Check the microphone and playback device, then try again.", 0, busy: false, cancel: false, retry: false, error: "START_FAILED");
                throw new InvalidOperationException("Recording could not start. Check that the microphone and the meeting playback device are connected and allowed, then try again.", ex);
            }

            session.LevelsAvailable += OnLevels;
            session.FaultRaised += OnFault;
            _captureSession = session;
            _activeDirectory = dir;
            _recordingDirectory = dir;
            _log.Info(ProcessingLogCode.CaptureStarted);
            _micLastSoundTicks = Environment.TickCount64;
            _micSilenceWarned = false;
            _tick = new Timer(_ => OnTick(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
            ActiveSession = new SessionListItem(code, dir, SessionState.Recording, startLocal, null, false, false);
            SetStatus("Recording", 0, busy: true, cancel: false, retry: false, error: null);
            return ActiveSession;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        string dir;
        CaptureResult result;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ICaptureSession session = _captureSession ?? throw new InvalidOperationException("No recording is running.");
            dir = _recordingDirectory!;
            SetStatus("Stopping and saving the recording…", 0, busy: true, cancel: false, retry: false, error: null);
            try
            {
                result = await session.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log(dir).Error(ProcessingLogCode.CaptureError);
                await MarkFailedAsync(dir, SessionErrorStage.Recording, "STOP_FAILED").ConfigureAwait(false);
                SetStatus("The recording stopped with an error. The saved part is kept: open the session and press Retry to recover it.", 0, busy: false, cancel: false, retry: true, error: "STOP_FAILED");
                return;
            }
            finally
            {
                session.LevelsAvailable -= OnLevels;
                session.FaultRaised -= OnFault;
                await session.DisposeAsync().ConfigureAwait(false);
                _captureSession = null;
                _recordingDirectory = null;
                _tick?.Dispose();
                _tick = null;
            }

            SessionManifest manifest = SessionManifestIO.Read(Path.Combine(dir, ManifestFile));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            manifest.Recording.StoppedAt = now;
            manifest.Recording.DurationSeconds = Math.Round(result.Duration.TotalSeconds, 3);
            SessionAudit.Append(manifest, SessionAuditEvent.RecordingStopped, now);
            await new SessionStateMachine(manifest, Path.Combine(dir, ManifestFile))
                .TransitionToAsync(SessionState.Finalizing, now, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await FinalizeAsync(dir, result, cancellationToken).ConfigureAwait(false);
        await TranscribeAsync(dir, cancellationToken).ConfigureAwait(false);
    }

    public async Task TranscribeAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        await RunOperationAsync(sessionDirectory, SessionErrorStage.Transcribing, "TRANSCRIBE_FAILED", async ct =>
        {
            string path = Path.Combine(sessionDirectory, ManifestFile);
            SessionManifest manifest = SessionManifestIO.Read(path);
            var machine = new SessionStateMachine(manifest, path);
            if (manifest.State == SessionState.Exported)
            {
                // spec: "transcribe again" moves the old transcript to exports/
                MoveToExports(sessionDirectory, TranscriptFile);
                MoveToExports(sessionDirectory, DocxFile);
                if (manifest.Outputs is not null)
                {
                    manifest.Outputs.TranscriptJson = null;
                    manifest.Outputs.Docx = null;
                }
            }

            await machine.TransitionToAsync(SessionState.Transcribing, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            Log(sessionDirectory).Info(ProcessingLogCode.TranscribeStarted);

            string work = Path.Combine(sessionDirectory, ".work");
            Directory.CreateDirectory(work);
            var inputs = new List<TranscriptionInput>();
            try
            {
                foreach (StreamFiles stream in StreamsFromManifest(sessionDirectory, manifest))
                {
                    string wav = Path.Combine(work, $"{(stream.Role == StreamRole.Microphone ? "mic" : "meeting")}-16k.wav");
                    await _finalizer.ConvertForTranscriptionAsync(stream, wav, ct).ConfigureAwait(false);
                    inputs.Add(new TranscriptionInput(stream.Role, wav, stream.StartOffset));
                }

                DateTimeOffset started = DateTimeOffset.UtcNow;
                int threads = Math.Clamp(_settings.Current.MaxTranscriptionThreads, 1, Environment.ProcessorCount);
                var progress = new Progress<TranscriptionProgress>(p =>
                    SetStatus($"Transcribing offline… {p.Fraction:P0}", p.Fraction, busy: true, cancel: true, retry: false, error: null));
                Transcript transcript = await _transcriber.TranscribeAsync(
                    new TranscriptionRequest(manifest.SessionId, manifest.SessionCode, inputs, _modelPath, _settings.Current.TranscriptionPreset, threads),
                    progress, ct).ConfigureAwait(false);
                transcript.SessionId = manifest.SessionId;
                transcript.SessionCode = manifest.SessionCode;
                string transcriptPath = Path.Combine(sessionDirectory, TranscriptFile);
                await TranscriptIO.WriteAsync(transcriptPath, transcript, ct).ConfigureAwait(false);

                DateTimeOffset now = DateTimeOffset.UtcNow;
                manifest = SessionManifestIO.Read(path);
                manifest.Transcription = new SessionTranscriptionInfo
                {
                    EngineVersion = transcript.Engine.Version,
                    Model = transcript.Engine.Model,
                    ModelSha256 = transcript.Engine.ModelSha256,
                    Language = transcript.Language,
                    Threads = threads,
                    StartedAt = started,
                    CompletedAt = now,
                };
                SetOutput(manifest, TranscriptFile, await Sha256Async(transcriptPath, ct).ConfigureAwait(false), now, null, (o, f) => o.TranscriptJson = f);
                SessionAudit.Append(manifest, SessionAuditEvent.Transcribed, now);
                await SessionManifestIO.WriteAsync(path, manifest, ct).ConfigureAwait(false);
                Log(sessionDirectory).Info(ProcessingLogCode.TranscribeDone);
            }
            finally
            {
                TryDeleteDirectory(work);
            }

            await ExportCoreAsync(sessionDirectory, ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task ExportAsync(string sessionDirectory, CancellationToken cancellationToken) =>
        RunOperationAsync(sessionDirectory, SessionErrorStage.Exporting, "EXPORT_FAILED", ct => ExportCoreAsync(sessionDirectory, ct), cancellationToken);

    public async Task SaveTranscriptAsync(string sessionDirectory, Transcript transcript, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        string transcriptPath = Path.Combine(sessionDirectory, TranscriptFile);
        string path = Path.Combine(sessionDirectory, ManifestFile);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        transcript.UpdatedAt = now;
        await TranscriptIO.WriteAsync(transcriptPath, transcript, cancellationToken).ConfigureAwait(false);
        SessionManifest manifest = SessionManifestIO.Read(path);
        SetOutput(manifest, TranscriptFile, await Sha256Async(transcriptPath, cancellationToken).ConfigureAwait(false), now, null, (o, f) => o.TranscriptJson = f);
        SessionAudit.Append(manifest, SessionAuditEvent.Edited, now);
        if (transcript.Review is not null)
        {
            SessionAudit.Append(manifest, SessionAuditEvent.Reviewed, now);
        }

        await SessionManifestIO.WriteAsync(path, manifest, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecoverAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(sessionDirectory, ManifestFile);
        SessionManifest manifest = SessionManifestIO.Read(path);
        SessionState interrupted = manifest.State;
        var machine = new SessionStateMachine(manifest, path);
        Log(sessionDirectory).Info(ProcessingLogCode.RecoveryFound);
        if (interrupted is SessionState.Recording or SessionState.Finalizing or SessionState.Transcribing)
        {
            manifest.Recording.Recovered = true;
            await machine.TransitionToAsync(SessionState.Recovered, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        if (interrupted == SessionState.Transcribing && File.Exists(Path.Combine(sessionDirectory, AudioFile)))
        {
            await machine.TransitionToAsync(SessionState.ReadyToTranscribe, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            Log(sessionDirectory).Info(ProcessingLogCode.RecoveryDone);
            await TranscribeAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
            return;
        }

        CaptureResult? result = await _recovery.RecoverAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
        if (result is null || result.Streams.Count == 0)
        {
            manifest = SessionManifestIO.Read(path);
            await new SessionStateMachine(manifest, path).TransitionToAsync(
                SessionState.Failed, DateTimeOffset.UtcNow,
                new SessionErrorInfo { Stage = SessionErrorStage.Recovery, Code = "NO_AUDIO", At = DateTimeOffset.UtcNow },
                cancellationToken).ConfigureAwait(false);
            SetStatus("No recorded audio could be recovered from this session.", 0, busy: false, cancel: false, retry: false, error: "NO_AUDIO");
            return;
        }

        manifest = SessionManifestIO.Read(path);
        manifest.Recording.Recovered = true;
        manifest.Recording.DurationSeconds ??= Math.Round(result.Duration.TotalSeconds, 3);
        await new SessionStateMachine(manifest, path).TransitionToAsync(SessionState.Finalizing, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        Log(sessionDirectory).Info(ProcessingLogCode.RecoveryDone);
        await FinalizeAsync(sessionDirectory, result, cancellationToken).ConfigureAwait(false);
        await TranscribeAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
    }

    public Task CancelCurrentOperationAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        _operationCts?.Cancel();
        return Task.CompletedTask;
    }

    public async Task RetryCurrentOperationAsync(string sessionDirectory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(sessionDirectory, ManifestFile);
        SessionManifest manifest = SessionManifestIO.Read(path);
        if (manifest.State == SessionState.Failed && manifest.Error?.Stage is SessionErrorStage.Finalizing or SessionErrorStage.Recording)
        {
            await RecoverAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (manifest.State == SessionState.Failed)
        {
            await new SessionStateMachine(manifest, path).TransitionToAsync(SessionState.ReadyToTranscribe, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        await TranscribeAsync(sessionDirectory, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _tick?.Dispose();
        _operationCts?.Dispose();
        _log?.Dispose();
        _gate.Dispose();
    }

    private async Task FinalizeAsync(string dir, CaptureResult result, CancellationToken cancellationToken)
    {
        await RunOperationAsync(dir, SessionErrorStage.Finalizing, "ENCODE_FAILED", async ct =>
        {
            Log(dir).Info(ProcessingLogCode.EncodeStarted);
            var progress = new Progress<double>(f => SetStatus($"Saving the interview audio… {f:P0}", f, busy: true, cancel: false, retry: false, error: null));
            FinalizeResult final = await _finalizer.FinalizeAsync(result, Path.Combine(dir, AudioFile), progress, ct).ConfigureAwait(false);
            string path = Path.Combine(dir, ManifestFile);
            SessionManifest manifest = SessionManifestIO.Read(path);
            SetOutput(manifest, AudioFile, final.Sha256, DateTimeOffset.UtcNow, Math.Round(final.Duration.TotalSeconds, 3), (o, f) => o.Audio = f);
            await new SessionStateMachine(manifest, path).TransitionToAsync(SessionState.ReadyToTranscribe, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            Log(dir).Info(ProcessingLogCode.EncodeDone);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExportCoreAsync(string dir, CancellationToken ct)
    {
        string path = Path.Combine(dir, ManifestFile);
        SessionManifest manifest = SessionManifestIO.Read(path);
        Transcript transcript = TranscriptIO.Read(Path.Combine(dir, TranscriptFile));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string target = Path.Combine(dir, DocxFile);
        if (File.Exists(target))
        {
            target = ExportPaths.ReExportPath(dir, DateTimeOffset.Now);
        }

        DateTimeOffset recordedAt = manifest.Recording.StartedAt ?? manifest.CreatedAt;
        await _exporter.ExportAsync(
            new DocxExportRequest(
                manifest.SessionCode,
                recordedAt,
                TimeSpan.FromSeconds(manifest.Recording.DurationSeconds ?? 0),
                manifest.StudyCode,
                transcript.Engine.Version,
                transcript.Engine.Model,
                TranscriptMapping.ToDocxSegments(transcript),
                target,
                now,
                transcript.Review?.ReviewerInitials,
                transcript.Review?.At),
            ct).ConfigureAwait(false);

        manifest = SessionManifestIO.Read(path);
        string relative = Path.GetRelativePath(dir, target).Replace('\\', '/');
        SetOutput(manifest, relative, await Sha256Async(target, ct).ConfigureAwait(false), now, null, (o, f) => o.Docx = f);
        SessionAudit.Append(manifest, SessionAuditEvent.Exported, now);
        await new SessionStateMachine(manifest, path).TransitionToAsync(SessionState.Exported, now, ct).ConfigureAwait(false);
        Log(dir).Info(ProcessingLogCode.ExportDone);
        SetStatus("Done: audio, transcript and Word file are saved in the session folder.", 1, busy: false, cancel: false, retry: false, error: null);
    }

    private async Task RunOperationAsync(string dir, SessionErrorStage stage, string code, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCts = cts;
        try
        {
            await operation(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stage == SessionErrorStage.Transcribing)
        {
            string path = Path.Combine(dir, ManifestFile);
            SessionManifest manifest = SessionManifestIO.Read(path);
            if (manifest.State == SessionState.Transcribing)
            {
                if (manifest.Outputs is { } outputs)
                {
                    if (outputs.TranscriptJson is { } t && !File.Exists(Path.Combine(dir, t.File))) outputs.TranscriptJson = null;
                    if (outputs.Docx is { } d && !File.Exists(Path.Combine(dir, d.File))) outputs.Docx = null;
                }

                await new SessionStateMachine(manifest, path).TransitionToAsync(SessionState.ReadyToTranscribe, DateTimeOffset.UtcNow, CancellationToken.None).ConfigureAwait(false);
            }

            Log(dir).Info(ProcessingLogCode.TranscribeCancelled);
            SetStatus("Transcription cancelled. The recording is safe; you can start it again.", 0, busy: false, cancel: false, retry: true, error: null);
        }
#pragma warning disable CA1031 // any failure must end in Failed + a plain-language status, never a stuck "busy" UI
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log(dir).Error(stage switch
            {
                SessionErrorStage.Finalizing => ProcessingLogCode.EncodeError,
                SessionErrorStage.Transcribing => ProcessingLogCode.TranscribeError,
                _ => ProcessingLogCode.ExportError,
            });
            await MarkFailedAsync(dir, stage, code).ConfigureAwait(false);
            SetStatus(PlainLanguage(stage, ex), 0, busy: false, cancel: false, retry: true, error: code);
        }
        finally
        {
            _operationCts = null;
        }
    }

    /// <summary>Best effort: record Failed with stage/code. Never throws, so the original error is what the researcher sees.</summary>
    private static async Task MarkFailedAsync(string dir, SessionErrorStage stage, string code)
    {
        try
        {
            string path = Path.Combine(dir, ManifestFile);
            SessionManifest manifest = SessionManifestIO.Read(path);
            if (manifest.State is not (SessionState.Exported or SessionState.Failed))
            {
                await new SessionStateMachine(manifest, path).TransitionToAsync(
                    SessionState.Failed, DateTimeOffset.UtcNow,
                    new SessionErrorInfo { Stage = stage, Code = code, At = DateTimeOffset.UtcNow },
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // the session file itself may be the problem; status already tells the researcher
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private static string PlainLanguage(SessionErrorStage stage, Exception ex) => (stage, ex) switch
    {
        (_, IOException io) when (io.HResult & 0xFFFF) is 0x70 or 0x27 => "The disk is full. Free some space, then press Retry. The recording itself is kept.",
        (_, UnauthorizedAccessException) => "Windows refused access to the session folder. Check the folder permissions, then press Retry.",
        (SessionErrorStage.Finalizing, _) => "The interview audio could not be saved as MP4. The original recording is kept; press Retry.",
        (SessionErrorStage.Transcribing, _) when ex.GetType().Name == "ModelUnavailableException" => "The speech model is missing or damaged. Reinstall Interview Recorder.",
        (SessionErrorStage.Transcribing, _) => "Transcription failed. The recording is kept; press Retry.",
        _ => "The Word file could not be written. If it is open in Word, close it and press Retry.",
    };

    private static IEnumerable<StreamFiles> StreamsFromManifest(string dir, SessionManifest manifest) =>
        CaptureStreams.FromManifest(dir, manifest)
            .Select(st => st with { ChunkPaths = st.ChunkPaths.Where(File.Exists).ToList() })
            .Where(st => st.ChunkPaths.Count > 0);

    private static void SetOutput(SessionManifest manifest, string file, string sha256, DateTimeOffset at, double? duration, Action<SessionOutputs, SessionOutputFile> assign)
    {
        manifest.Outputs ??= new SessionOutputs();
        assign(manifest.Outputs, new SessionOutputFile { File = file, Sha256 = sha256, WrittenAt = at, DurationSeconds = duration });
        manifest.Hashes[file] = sha256;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void MoveToExports(string dir, string file)
    {
        string source = Path.Combine(dir, file);
        if (!File.Exists(source))
        {
            return;
        }

        string exports = Path.Combine(dir, "exports");
        Directory.CreateDirectory(exports);
        string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        File.Move(source, Path.Combine(exports, $"{Path.GetFileNameWithoutExtension(file)}_{stamp}{Path.GetExtension(file)}"));
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort: .work holds derived 16 kHz copies only
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ProcessingLogWriter Log(string dir)
    {
        if (_log is null || !string.Equals(_activeDirectory, dir, StringComparison.OrdinalIgnoreCase))
        {
            _log?.Dispose();
            _log = new ProcessingLogWriter(Path.Combine(dir, "processing.log"));
            _activeDirectory = dir;
        }

        return _log;
    }

    private void OnLevels(object? sender, LevelSample sample)
    {
        // Exact digital silence (not a quiet room) from the mic usually means Windows blocks microphone access for desktop apps,
        // or the device is muted at the driver: WASAPI then delivers zeros without any error.
        if (sample.Role == StreamRole.Microphone && sample.PeakDbfs > -110f)
        {
            Interlocked.Exchange(ref _micLastSoundTicks, Environment.TickCount64);
        }

        LevelsAvailable?.Invoke(this, sample);
    }

    private void OnTick()
    {
        if (_captureSession is not null && !_micSilenceWarned && Environment.TickCount64 - Interlocked.Read(ref _micLastSoundTicks) > 6000)
        {
            _micSilenceWarned = true;
            if (_recordingDirectory is not null)
            {
                Log(_recordingDirectory).Warn(ProcessingLogCode.PermissionDenied);
            }

            SetStatus("No sound at all from the microphone. Check Settings > Privacy & security > Microphone > \"Let desktop apps access your microphone\", and that the microphone is not muted. Recording continues.",
                0, busy: true, cancel: false, retry: false, error: "MIC_SILENT");
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFault(object? sender, CaptureFault fault)
    {
        if (_recordingDirectory is not null)
        {
            Log(_recordingDirectory).Warn(fault.Kind is CaptureFaultKind.DiskLow or CaptureFaultKind.DiskFull ? ProcessingLogCode.DiskLow
                : fault.Kind is CaptureFaultKind.DeviceRemoved or CaptureFaultKind.DefaultDeviceChanged or CaptureFaultKind.FormatChanged ? ProcessingLogCode.CaptureDeviceChanged
                : fault.Kind == CaptureFaultKind.AccessDenied ? ProcessingLogCode.PermissionDenied
                : ProcessingLogCode.CaptureError);
        }

        string message = fault.Kind switch
        {
            CaptureFaultKind.DeviceRemoved or CaptureFaultKind.DefaultDeviceChanged when fault.Recovered => "An audio device changed. Recording continues on the new device.",
            CaptureFaultKind.DeviceRemoved => "An audio device was disconnected. Reconnect it; the recording so far is safe.",
            CaptureFaultKind.DiskLow => "Disk space is low. Stop the interview soon or free some space.",
            CaptureFaultKind.DiskFull => "The disk is full. The recording so far is saved; free space before continuing.",
            CaptureFaultKind.AccessDenied => "Windows blocked the microphone. Allow microphone access in Settings > Privacy & security > Microphone.",
            CaptureFaultKind.Unsupported => "This audio device or Windows version is not supported for capture.",
            _ => "An audio problem occurred. The recording so far is safe.",
        };
        SetStatus(message, 0, busy: true, cancel: false, retry: false, error: fault.Recovered ? null : message);
    }

    private void SetStatus(string message, double progress, bool busy, bool cancel, bool retry, string? error)
    {
        Status = new WorkflowStatus(message, progress, busy, cancel, retry, error);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
