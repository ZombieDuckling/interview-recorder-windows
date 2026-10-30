# Encryption at rest and deletion

## What version 1.0 protects, and what it does not

- **Not encrypted by the app:** the recording chunks (`capture/`), `interview_audio.mp4`, `transcript.json`, the Word file and `processing.log` are ordinary files on disk.
- **Protected by the app:** each session folder is restricted to your Windows account and SYSTEM, so other Windows accounts on the PC cannot open it. Nothing is ever sent over the network.
- **Encryption at rest is your device's job:** turn on BitLocker or Windows device encryption (below). This is the "documented volume-level encryption" route of the project plan. The app never claims that its files are encrypted.

## Turn on device encryption

Windows 11 Pro and Windows 10 Pro: turn on BitLocker in Settings, Privacy and security, Device encryption, or in Control Panel, BitLocker Drive Encryption. Windows Home: use "Device encryption" if Settings shows it. Keep the recovery key in a safe place that is not the same computer.

## Deletion

The app overwrites each file of a session once, then deletes it. On SSD drives, the drive can keep old copies of data in places that the app cannot overwrite. For this reason, device encryption is the main protection. A deleted session cannot be recovered by the app.
