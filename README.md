# Interview Recorder for Windows

The app records consented Google Meet research interviews. The researcher runs Google Meet in a normal browser. The app is a separate native Windows program. It captures the meeting audio and the local microphone as 2 separate inputs. After the session it transcribes the audio offline with whisper.cpp. It exports an audio-only MP4 file and an editable Word (.docx) file. It works fully offline. It sends no data over the network. It has no telemetry.

## Status

Milestone M1 is in progress (native UI shell, settings, consent workflow, session schema). Nothing is released.

## Requirements

Windows 10 22H2 or Windows 11, x64. 8 GB RAM. No administrator rights for normal use. To build: .NET 10 SDK on Windows.

## Repository layout

| Folder | Contents |
| --- | --- |
| src/Recorder.App | WPF user interface. |
| src/Recorder.Core | sessions, session state machine, file formats, settings, diagnostics log. |
| src/Recorder.Audio.Windows | WASAPI capture of meeting audio and microphone. |
| src/Recorder.Transcription | whisper.cpp adapter. |
| src/Recorder.Media | FFmpeg encoding to AAC in MP4. |
| src/Recorder.Export | Word (.docx) export. |
| src/Recorder.Security | encryption at rest with DPAPI-protected keys. |
| tests/ | unit and integration tests. |
| packaging/windows/ | WiX Burn installer. |
| docs/architecture/ | architecture decision records (ADR). |
| docs/privacy/ | privacy and POPIA notes. |
| models/manifest.json | names and SHA-256 checksums of model files (model files are not in git). |

## Build

```
dotnet restore --locked-mode
dotnet build -c Release
dotnet test -c Release
```

The CI workflow .github/workflows/build.yml runs the same steps on windows-latest.

## Rules

- Native Windows UI only (WPF).
- No web view, no Electron, no HTML user interface, no localhost server.
- No network traffic.
- No telemetry.
- Research audio and transcripts never leave the computer.
- Recording is never silent: the researcher starts it and the app shows that it records.
- Logs contain diagnostic codes and timings only, never speech, transcript text or personal data.
- Model files, recordings and secrets are never committed to git.

## Privacy

The app helps with POPIA-conscious research. Software alone cannot make a study POPIA compliant. Compliance also depends on research approvals, consent wording, institutional policy, device security, breach handling and retention decisions.
