# Third-party software in Interview Recorder for Windows

| Component | Version | Licence | Use in the app | Source |
| --- | --- | --- | --- | --- |
| NAudio | 3.3.0 | MIT | WASAPI audio capture | https://github.com/naudio/NAudio |
| Whisper.net and Whisper.net.Runtime | 1.9.1 | MIT | runs whisper.cpp for offline transcription | https://github.com/sandrohanea/whisper.net |
| whisper.cpp (native library inside Whisper.net.Runtime 1.9.1) | as bundled by Whisper.net 1.9.1 | MIT | speech recognition engine | https://github.com/ggml-org/whisper.cpp |
| Whisper base model, quantised q5_1 (ggml-base-q5_1.bin, SHA-256 422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898) | revision 362722b3fdcd2300b58a8286933ead1c48619667 | MIT | speech recognition model | https://huggingface.co/ggerganov/whisper.cpp |
| DocumentFormat.OpenXml | 3.5.1 | MIT | writes the Word (.docx) file | https://github.com/dotnet/Open-XML-SDK |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | user interface view models | https://github.com/CommunityToolkit/dotnet |
| System.Security.Cryptography.ProtectedData | 10.0.14 | MIT | Windows DPAPI | https://github.com/dotnet/runtime |
| FFmpeg, LGPL build n8.1.3 (BtbN FFmpeg-Builds, autobuild-2026-10-29-19-17, file ffmpeg-n8.1.3-25-g611cec792a-win64-lgpl-8.1.zip) | n8.1.3 | LGPL-2.1-or-later | converts the recording to AAC audio in an MP4 file | https://github.com/BtbN/FFmpeg-Builds |
| .NET runtime (self-contained) | 10.0 | MIT | runs the app | https://github.com/dotnet/runtime |
| WiX Toolset (build tool only, not shipped inside the app) | 5.0.2 | MS-RL | builds the installer | https://github.com/wixtoolset/wix |

## FFmpeg (LGPL)

- The FFmpeg build is configured without GPL and without nonfree components; the build script refuses any build with --enable-gpl or --enable-nonfree.
- The app runs ffmpeg.exe as a separate program and does not link to it.
- The FFmpeg source code is not changed.
- The FFmpeg licence text is installed next to ffmpeg.exe as FFMPEG-LICENSE.txt.
- The corresponding source code is available at https://ffmpeg.org/download.html and the build recipe at https://github.com/BtbN/FFmpeg-Builds.
- Users may replace ffmpeg.exe with another compatible build.

## Network

None of these components are used to send data over the network. The model and FFmpeg are bundled with the installer; the app never downloads them.
