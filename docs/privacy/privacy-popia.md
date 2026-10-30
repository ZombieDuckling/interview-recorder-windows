# Privacy and POPIA notes

## What the app does

- records audio of a consented interview on this computer only.
- Transcribes on this computer only.
- Makes no network connections.
- Has no telemetry and no analytics.
- Needs no account.

## What the researcher must do

- get research approval and informed consent;
- use a pseudonymous participant ID, not a name;
- keep the data folder outside cloud sync folders (OneDrive, Dropbox, Google Drive, iCloud), because sync sends files to a cloud service;
- set a retention period;
- delete sessions when the retention period ends;
- report a breach under the institution's policy.

## What software cannot do

Software alone cannot make a study POPIA compliant. Compliance also depends on research approvals, consent wording, institutional policies, device security, breach handling and retention decisions.

## Data stored per interview

- session.json
- the original recording chunks (capture folder)
- interview_audio.mp4
- transcript.json
- interview_transcript.docx
- processing.log

processing.log has codes and times only, never speech, transcript text or names.
