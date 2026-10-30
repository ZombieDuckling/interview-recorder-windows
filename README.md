# Interview Recorder for Windows

## Download for Windows

### [**Download InterviewRecorder-Setup-x64.exe**](https://github.com/ZombieDuckling/interview-recorder-windows/releases/latest/download/InterviewRecorder-Setup-x64.exe)

Windows 10 22H2 or Windows 11, 64-bit. No administrator rights needed. All versions and checksums: [Releases](https://github.com/ZombieDuckling/interview-recorder-windows/releases).

1. Run the downloaded file. Windows may say "Windows protected your PC" because the installer is not code-signed yet: select **More info**, then **Run anyway**.
2. Read the terms, tick **I agree**, select **Install**, then **Launch**.
3. On first start, choose where interviews are saved and test the microphone and meeting audio.

The app records consented Google Meet research interviews. The researcher runs Google Meet in a normal browser. The app is a separate native Windows program. It captures the meeting audio and the local microphone as 2 separate inputs. After the session it transcribes the audio offline with whisper.cpp. It exports an audio-only MP4 file and an editable Word (.docx) file. It works fully offline. It sends no data over the network. It has no telemetry.

## Status

Version 1.0.0 is being prepared. See docs/known-limitations.md.

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

## Install

1. Download InterviewRecorder-Setup-x64.exe and SHA256SUMS.txt from the release.
2. Check the hash in PowerShell: Get-FileHash .\InterviewRecorder-Setup-x64.exe -Algorithm SHA256. Compare it with SHA256SUMS.txt.
3. Run the installer. No administrator rights are needed.
4. If SmartScreen shows "Windows protected your PC", select "More info", then "Run anyway" (the installer is not signed yet).
5. Start "Interview Recorder" from the Start Menu.

## Uninstall

Open Settings, Apps, Installed apps. Find "Interview Recorder". Select Uninstall. Your interviews stay in your data folder (default: Documents\Interview Recorder). Delete them from the app's session library first if you want to remove them.
