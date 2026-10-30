# Encryption at rest and deletion

## What the app encrypts

The original recording chunks and temporary working files are encrypted with AES-GCM. The key for each session is protected by Windows DPAPI for the current Windows user. Another Windows user account cannot read them.

## What the app does not encrypt

The exported MP4 and DOCX files are normal files, so that other programs can open them. Protect them with device encryption.

## Turn on device encryption

Windows 11 Pro and Windows 10 Pro: turn on BitLocker in Settings, Privacy and security, Device encryption, or in Control Panel, BitLocker Drive Encryption. Windows Home: use "Device encryption" if Settings shows it. Keep the recovery key in a safe place that is not the same computer.

## Deletion

The app overwrites each file of a session once, then deletes it. On SSD drives, the drive can keep old copies of data in places that the app cannot overwrite. For this reason, device encryption is the main protection. A deleted session cannot be recovered by the app.
