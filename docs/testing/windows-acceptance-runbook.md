# Windows acceptance runbook (Interview Recorder 1.0)

This runbook tells a tester how to run the acceptance tests for Interview Recorder 1.0 on a real Windows PC. It records a Google Meet interview: meeting audio and microphone as 2 separate inputs. After Stop it makes interview_audio.mp4, transcript.json and interview_transcript.docx in a session folder under Documents\Interview Recorder.

Do the tests in order. Write every result in docs/testing/windows-acceptance-matrix.md.

## Before you start

1. Use Windows 11 or Windows 10 22H2, x64, with 8 GB RAM if possible.
2. Install from InterviewRecorder-Setup-x64.exe as a standard user. Do not use an administrator account.
3. Have a second person or a second device join the Google Meet call as the participant.
4. Use headphones for the researcher.
5. Note the RAM in Task Manager before each test.
6. Write every test result in docs/testing/windows-acceptance-matrix.md, in the columns Status and Evidence.

## Evidence for every test

1. A screenshot of the app.
2. The session folder name.
3. For audio tests, play interview_audio.mp4 and write if both voices are clear (yes/no).
4. Open interview_transcript.docx in Word and write if it opens (yes/no).
5. Copy processing.log (it has no speech or names).

## Common steps

"Record a test session" means these 10 steps:

1. Start Interview Recorder from the Start Menu.
2. Select New interview.
3. Enter participant ID P-TEST-<number>.
4. Select the meeting playback device and the microphone.
5. Tick the consent box.
6. Select Start.
7. Check that the red recording indicator and the timer show.
8. Talk for the time the test says, with the participant speaking too.
9. Select Stop and confirm.
10. Wait until the transcript editor opens.

## Tests

### W01 Meet in Chrome

Steps:
1. Join the Meet call in Chrome.
2. Record a test session of 10 minutes.

Pass when: both voices are clear in the MP4 and the transcript has Researcher and Participant lines.

### W02 Meet in Edge

Steps:
1. Join the Meet call in Microsoft Edge.
2. Record a test session of 10 minutes.

Pass when: both voices are clear in the MP4 and the transcript has Researcher and Participant lines.

### W03 Default playback device

Steps:
1. Leave the default device selected.
2. Record 2 minutes.

Pass when: the participant voice is in the MP4.

### W04 Selected playback device

Steps:
1. Connect a second output device.
2. Set Meet to play on it.
3. Select that device in the app.
4. Record 2 minutes.

Pass when: the participant voice is in the MP4.

### W05 USB headset

Steps:
1. Use a USB headset for Meet and the app.
2. Record 5 minutes.

Pass when: both voices are clear.

### W06 Bluetooth headset

Steps:
1. Use a Bluetooth headset.
2. Start recording, then start talking in Meet (the headset may switch to hands-free mode).
3. Record 5 minutes.

Pass when: recording continues after the switch and both voices are in the MP4.

### W07 Laptop microphone

Steps:
1. Use the built-in microphone with headphones.
2. Record 2 minutes.

Pass when: the researcher voice is clear.

### W08 Muted in Meet

Steps:
1. Record a test session.
2. Mute the microphone in Meet for 1 minute during recording.

Pass when: the app keeps recording and the MP4 has the researcher voice from the app microphone.

### W09 Muted in Windows

Steps:
1. Record a test session.
2. Mute the microphone in Windows sound settings for 1 minute.

Pass when: the app shows the mic level at zero and continues with no crash.

### W10 48 kHz device

Steps:
1. Set the devices to 48000 Hz in Sound settings, Advanced.
2. Record 2 minutes.

Pass when: the MP4 plays at normal speed.

### W11 44.1 kHz device

Steps:
1. Set the devices to 44100 Hz.
2. Record 2 minutes.

Pass when: the MP4 plays at normal speed and the two voices stay in step.

### W12 Notification

Steps:
1. Record a test session.
2. Send a Windows notification (for example an email) during recording.

Pass when: recording continues; note in the result that the notification sound is in the meeting audio.

### W13 Unplug and reconnect

Steps:
1. Record a test session.
2. Unplug the USB headset for 10 seconds during recording.
3. Reconnect it.

Pass when: the app shows a device message, keeps recording, and the MP4 has audio before and after.

### W14 Lock screen

Steps:
1. Record a test session.
2. Press Windows+L for 30 seconds during recording, then unlock.

Pass when: recording continues and the timer is correct.

### W15 UAC prompt

Steps:
1. Record a test session.
2. Start a program that asks for administrator rights during recording, then cancel it.

Pass when: recording continues.

### W16 Power interruption

Steps:
1. During recording, hold the power button to turn the PC off.
2. Start the PC and the app again.

Pass when: the app shows the session as interrupted, and Recover makes an MP4 with the audio up to the power loss.

### W17 Sleep and wake

Steps:
1. Record a test session.
2. Close the laptop lid for 1 minute during recording, then open it.

Pass when: the app continues or recovers, and the MP4 has the audio before sleep.

### W18 Low disk

Steps:
1. Fill the disk until less than 1 GB is free, then record.

Pass when: the app shows a disk-space warning.

### W19 Two-hour recording

Steps:
1. Record a test session of 2 hours with Chrome and Meet open.
2. Note the RAM and CPU of InterviewRecorder.exe in Task Manager every 30 minutes.

Pass when: the recording finishes, the MP4 plays, and RAM stays under 1 GB.

### W20 One-hour transcription

Steps:
1. Transcribe a 1-hour session.
2. Note the time and the peak RAM.

Pass when: the transcript finishes; note the time.

### W21 Standard-user install

Steps:
1. On a fresh Windows 11 and Windows 10 22H2 user account without administrator rights, run the installer.

Pass when: no administrator prompt appears and the app starts.

### W22 Offline

Steps:
1. Turn off Wi-Fi and the network cable.
2. Install and record a full test session.

Pass when: everything works offline.

### W23 No network use

Steps:
1. Run Resource Monitor (resmon), tab Network, during a full session.

Pass when: InterviewRecorder.exe has no network activity.

### W24 MP4 plays

Steps:
1. Open interview_audio.mp4 in the Media Player app.

Pass when: it plays with sound and no video.

### W25 DOCX opens

Steps:
1. Open interview_transcript.docx in Word and in LibreOffice.

Pass when: both open without a repair message.

### W26 Re-export

Steps:
1. Edit a line in the transcript editor and save.
2. Export again.

Pass when: a new file is in the exports folder and interview_audio.mp4 is unchanged (same SHA-256 in session.json).

### W27 Uninstall

Steps:
1. Uninstall from Settings, Apps.

Pass when: the program folder %LocalAppData%\Programs\Interview Recorder is gone and the interview data folder is still there.
