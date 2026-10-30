# Interview Recorder - Windows Native Development Plan

Private, offline-first research interview recorder | .exe installer | 8 GB RAM target

Repository: `interview-recorder-windows`

Plan version: 1.0 | 30 October 2026

## Platform and technology selection

C# on supported .NET LTS, WPF for fully native Windows desktop UI; Windows Core Audio/WASAPI for capture; a small native interop layer for loopback and microphone streams; bundled FFmpeg executable/libraries with license review; whisper.cpp native binary via P/Invoke or isolated subprocess; Open XML SDK for .docx; DPAPI for per-user secret protection and BitLocker/device encryption guidance.

## Platform-specific audio capture

Use WASAPI loopback on the selected meeting playback endpoint plus WASAPI microphone capture. If the system supports process-loopback capture, evaluate it to isolate Google Meet browser audio; otherwise explain that ordinary endpoint loopback can include unrelated system sounds. Keep mic capture separate. Support Windows 10/11 only after testing each selected API and documenting build minimums. Handle Bluetooth hands-free mode switching and sample-rate mismatches.

## Product objective

Build a single-purpose, offline-first research interview recorder for consented Google Meet interviews. The researcher runs Google Meet in a normal browser and operates a separate native desktop app. Capture the meeting audio and local microphone, preserve the original recording, transcribe after the session, and export an audio-only .mp4 and editable .docx. No hosted services, web UI, or background web server.

## Non-negotiable constraints

Native desktop UI and native application lifecycle. No Electron, Tauri, Chromium, WebView, React, HTML/CSS/JavaScript UI, localhost HTTP server, or mandatory account. Target machines have 8 GB total RAM. Never transmit research audio or transcripts. No telemetry or analytics by default. Never silently record; visible recording state and explicit researcher action required.

## Scope and user workflow

1. Researcher installs and launches the app. 2. First-run wizard checks OS permissions and storage location, displays consent reminder, and tests microphone plus meeting-audio capture. 3. Researcher enters pseudonymous participant ID and optional study code, confirms consent, and starts recording. 4. During interview, a large recording timer, audio level meters, disk-space indicator, and error status remain visible. 5. Researcher stops and verifies file finalization. 6. Offline transcription starts after recording, with progress, cancellation and retry. 7. Researcher reviews and corrects transcript, optionally labels speakers, and exports .mp4 and .docx. 8. Researcher may reopen a session and regenerate exports without altering the original audio.

## Data model and output layout

One session folder per interview: session.json (version, pseudonymous ID, timestamps, capture-device IDs, consent-confirmed flag, model/version, status, hashes); original lossless capture in a protected working format; final interview_audio.mp4 (AAC audio only); interview_transcript.docx; transcript.json with segments and timestamps; processing.log with no interview content. Keep participant names out of filenames by default. Use atomic writes and deterministic safe filename generation. A Word document contains session code, date, editable transcript, speaker labels where verified, and HH:MM:SS timestamps. Do not embed audio inside the Word file by default; deliver the MP4 alongside it.

## Platform-specific installer and release

Produce a signed Windows x64 installer .exe using WiX Burn or another native installer tool, with Start Menu shortcut, uninstall entry, bundled runtime where needed, model and FFmpeg. Test on fresh standard-user Windows installs; request only required microphone permission and never require administrator privileges for normal use. Provide ARM64 only after validation.

## Capture and media architecture

Capture remote/system playback and local microphone as separate logical inputs. Timestamp each stream using a monotonic clock; resample to a common sample rate; manage drift and silence; combine to a clear stereo or mono deliverable while preserving independent tracks in a recovery/intermediate file if practical. Write continuously to a crash-resilient container or chunked PCM, not directly to a fragile MP4 during the call. Finalize and encode to AAC-in-MP4 only after stop. Make capture robust against device unplugging, sleep/wake, changing sample rates, and system notifications. Never capture unrelated audio without warning: recommend headphones and an isolated meeting output device where possible, and display a clear system-audio scope notice.

## Transcription engine

Bundle whisper.cpp and a tested quantized multilingual model (start with tiny or base, measure quality on South African English and actual interview conditions). Run transcription only after capture completes by default. Convert to 16 kHz mono PCM for inference; process in chunks with overlap and stitch timestamped segments. Cap threads and working memory; expose conservative quality/speed presets. No network model downloads during a study unless deliberately provisioned by an administrator. Ship model weights with the installer or provide a separately verified offline model pack. Record model version in metadata.

## Speaker handling and accuracy

Do not promise reliable diarization from a mixed audio track. Prefer separate local-mic and meeting-audio streams to label Researcher versus Participant where possible. For multiple remote speakers or overlapping speech, use Unknown speaker and manual correction. Word exports must clearly state that automated transcription may contain errors. Include optional reviewer sign-off and transcript correction history without modifying source audio.

## Privacy and POPIA-conscious design

Local-only storage, explicit consent confirmation, no third-party APIs, no automatic cloud sync, and no application telemetry. Offer user-selected data directory with a warning about OneDrive/iCloud/Dropbox sync folders. Encryption at rest must be implemented using OS-backed key protection or documented volume-level encryption; never claim that simple local storage is encrypted. Provide configurable retention period, manual secure deletion guidance, access controls, pseudonymous IDs, and export audit information. Encrypt sensitive temporary files or keep them within a protected workspace and securely clean them up. POPIA compliance also depends on research approvals, consent wording, institutional policies, device security, breach handling, and retention decisions; software alone cannot guarantee compliance.

## Performance budget and resource controls

Design for 8 GB system RAM while Google Meet and a browser are running. Recording path should use low CPU and bounded buffers; transcription runs after the call with adjustable thread count, conservative model size, and no full-recording load into memory. Stream media conversion and DOCX generation. Reserve headroom for browser and OS; avoid large UI frameworks or memory-mapped multi-gigabyte models. Record baseline RAM/CPU metrics on 8 GB hardware, including a two-hour interview and a one-hour transcription.

## Error recovery and observability

A durable session manifest tracks Recording, Finalizing, ReadyToTranscribe, Transcribing, Exported, Failed, and Recovered states. On relaunch, detect unfinished captures, recover playable audio chunks, and offer resume transcription. Logs must contain diagnostic codes and timings but never raw speech, transcript text, or participant personal data. Handle full disk, denied permissions, unsupported capture API, missing model, encoding failure, and corrupted session metadata with plain-language remediation.

## UX screens

Native welcome and study setup; permission and device diagnostics; new interview form; recording console with start/stop, visible indicator, levels and timer; session library; transcription progress; transcript editor with timecode seek; export settings; privacy/retention settings; diagnostics and help. Keyboard accessible controls, high contrast, readable status text, and safe confirmation on exit while recording.

## Testing and release gates

Unit tests for file naming, timestamps, session state transitions, transcript segmentation, export formatting and retention logic. Integration tests for device capture, dual-stream sync, MP4 playback, DOCX opening in Word/LibreOffice, and interruption recovery. Manual end-to-end tests with Chrome and Edge/Safari as applicable, headphones, Bluetooth devices, muted microphones, device changes, low disk, sleep, and a two-hour Meet session. Security tests: no network egress, restricted filesystem permissions, no PII in logs, tamper detection. Build installers in CI, sign artifacts, publish SHA-256 hashes, and maintain an offline installation test.

## Milestones and agent handoff

M0: lock scope and collect OS test machines. M1: native UI shell, settings, consent workflow, session schema. M2: robust two-input audio capture and recoverable recording. M3: local encoding and export. M4: whisper.cpp integration and transcript editor. M5: security, privacy and retention controls. M6: installer signing, 8 GB stress testing, user acceptance. Require each milestone to deliver code, tests, README, architecture decision records, a reproducible build, and an explicit demonstration. Do not mark features complete on mock recordings alone.

## Definition of done

On a clean 8 GB laptop, an authorized researcher can install without developer tooling, start and stop a real consented Meet interview, capture both participant and researcher speech intelligibly, recover from an interrupted session, generate an audio-only MP4 and editable DOCX fully offline, reopen the session, and uninstall cleanly. Network traffic monitoring confirms no research content leaves the machine. Any unsupported OS version or capture limitation is clearly disclosed.

## Platform-specific risks

WASAPI loopback may include notifications and non-Meet audio; headphones and device selection reduce bleed but cannot guarantee isolation. Browser output-device changes may silently redirect playback; detect and prompt. Antivirus false positives, SmartScreen warnings without signing, and encrypted-folder recovery need explicit testing.

## Proposed repository structure

src/Recorder.App (WPF); src/Recorder.Core (sessions/state); src/Recorder.Audio.Windows (WASAPI); src/Recorder.Transcription (whisper.cpp adapter); src/Recorder.Media (FFmpeg); src/Recorder.Export (DOCX); src/Recorder.Security; tests/; packaging/windows/; docs/architecture/; docs/privacy/; models/manifest.json; .github/workflows/build.yml.

## Windows-specific acceptance tests

Chrome/Edge Meet, default and manually selected playback endpoints, USB headset, Bluetooth headset, laptop microphone, microphone muted in Meet versus OS muted, 48 kHz and 44.1 kHz devices, Windows notification while recording, device unplug and reconnect, Windows lock screen, UAC prompt, power interruption and recovery.
