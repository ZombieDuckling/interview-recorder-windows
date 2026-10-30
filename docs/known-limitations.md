# Known limitations (version 1.0.0)

- The installer is not code-signed. SmartScreen shows a warning.
- The app is built for Windows x64. On Windows on ARM it runs under x64 emulation. A native ARM64 build comes after tests on ARM64 hardware.
- Ordinary system-audio capture (endpoint loopback) records all sounds that play on the selected output device, not only Google Meet. Use headphones and close other programs that play sound. Process-specific capture is used only where Windows supports it.
- Automated transcription can contain errors. Check the transcript before you use it.
- Speaker labels come from the 2 inputs: the microphone is "Researcher" and the meeting audio is "Participant". Several remote speakers all get the label "Participant" or "Unknown". Correct the labels by hand.
- The exported MP4 and DOCX files are not encrypted by the app. Use BitLocker or Windows device encryption.
- Deletion on SSD drives is best effort. Device encryption is the main protection.
- TO BE CONFIRMED: results of the acceptance tests on a real Windows computer.
- Very short spoken turns (about 3 seconds or less, for example a one-line question) are sometimes missed by the automatic transcript. In a 1-hour test with synthetic voices, longer answers were found 77-100% of the time, short questions about 40-55%. Always check the transcript against the audio in the transcript editor (click a line to play from that point).
- Speaker labels come from which input the speech was on, not from voice recognition: the microphone is "Researcher", the meeting audio is "Participant".
