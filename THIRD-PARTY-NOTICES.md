# Third-party software in Interview Recorder for Windows

| Component | Version | Licence | Use in the app | Source |
| --- | --- | --- | --- | --- |
| NAudio | 3.3.0 | MIT | WASAPI audio capture | https://github.com/naudio/NAudio |
| Whisper.net and Whisper.net.Runtime | 1.9.1 | MIT | runs whisper.cpp for offline transcription | https://github.com/sandrohanea/whisper.net |
| whisper.cpp (inside Whisper.net.Runtime) | TO BE CONFIRMED | MIT | speech recognition engine | https://github.com/ggml-org/whisper.cpp |
| Whisper base model (ggml format) | TO BE CONFIRMED | MIT | speech recognition model weights | https://huggingface.co/ggerganov/whisper.cpp |
| DocumentFormat.OpenXml | 3.5.1 | MIT | writes the Word (.docx) file | https://github.com/dotnet/Open-XML-SDK |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | user interface view models | https://github.com/CommunityToolkit/dotnet |
| System.Security.Cryptography.ProtectedData | 10.0.14 | MIT | Windows DPAPI key protection | https://github.com/dotnet/runtime |
| FFmpeg (LGPL build) | TO BE CONFIRMED | LGPL-2.1-or-later | converts the recording to AAC audio in an MP4 file | https://ffmpeg.org |
| .NET runtime (self-contained) | 10.0 | MIT | runs the app | https://github.com/dotnet/runtime |

## FFmpeg (LGPL)

- The app uses an FFmpeg build that is configured without GPL and without nonfree parts.
- The app runs ffmpeg.exe as a separate program.
- The app does not change the FFmpeg source code.
- The installer includes the FFmpeg licence text.
- The FFmpeg source code for the exact build is available from the build's publisher; the release notes give the link.
- Users may replace ffmpeg.exe with another compatible build.
