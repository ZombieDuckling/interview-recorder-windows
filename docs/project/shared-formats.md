# Interview Recorder: shared formats (frozen on landing)

Version **1.0.0**, 2026-10-30. Author: mac-lead. Applies to the macOS and the Windows builds alike.
Schemas: `session.schema.json`, `transcript.schema.json` (JSON Schema draft 2020-12) in this folder.
Changes after landing go through wR. The key words MUST, MUST NOT and SHOULD are used as in RFC 2119.

## 1. Data root and session folder

- Data root: chosen by the researcher on first run. Default: macOS `~/Documents/Interview Recorder/`, Windows
  `%USERPROFILE%\Documents\Interview Recorder\`. The app warns when the root is inside a sync folder (iCloud Drive,
  OneDrive, Dropbox, Google Drive) and lets the researcher keep or change it.
- One folder per interview, directly under the root, named with the **session code**:

  `IR-YYYYMMDD-HHMMSS-XXXX`, for example `IR-20261031-091500-7F3A`.

  The date and time are the local time when recording started. `XXXX` is the first 4 hex digits (upper case) of the
  session UUID. A session code never contains a participant ID, study code or name. If the folder exists, take the
  next 4 hex digits of the UUID (`7F3A` → next 4), repeat until unique.
- Folder permissions: owner only (macOS `0700`; Windows: an ACL that grants only the current user and SYSTEM).

```
IR-20261031-091500-7F3A/
  session.json                   manifest (section 2), the single source of truth for state
  processing.log                 diagnostic log (section 6), no interview content
  capture/
    meeting/000001.wav ...       meeting/system audio, chunked (section 3)
    mic/000001.wav ...           local microphone, chunked
  interview_audio.mp4            final mix, AAC in MP4, audio only (section 4)
  transcript.json                transcript (section 5)
  interview_transcript.docx      editable Word export (section 7)
  exports/                       re-exports: interview_transcript_YYYYMMDD-HHMMSS.docx (older exports are kept)
```

The `capture/` chunks are the **original recording**. Nothing after recording modifies or deletes them, except the
researcher's explicit retention/deletion action.

## 2. session.json

Schema: `session.schema.json`. Rules:
- UTF-8, no BOM, 2-space indentation, keys in the order of the schema. Timestamps are RFC 3339 UTC with milliseconds
  (`2026-10-31T07:15:00.123Z`). Durations are seconds as numbers with up to 3 decimals.
- `schemaVersion` is `"1.0.0"`. A reader MUST refuse a different major version with a plain-language message and
  MUST NOT change that file.
- `participantId` is pseudonymous: 1-32 characters, `^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$`. The UI also refuses values
  that look like an e-mail address or contain a space (names), with a reason.
- `consent.confirmed` MUST be `true` before the state can become `Recording`. `consent.confirmedAt` is set at that moment.
- `hashes` hold SHA-256 (lower-case hex) of each closed chunk and each output file, written when the file is closed.
  A mismatch on open is reported as tampering or corruption (status unchanged, error recorded in the log).

### State machine

States (exact strings): `Recording`, `Finalizing`, `ReadyToTranscribe`, `Transcribing`, `Exported`, `Failed`, `Recovered`.

| From | To | When |
|---|---|---|
| (new) | `Recording` | researcher pressed Start, consent confirmed, both inputs opened (or the researcher accepted a mic-only/meeting-only warning) |
| `Recording` | `Finalizing` | researcher pressed Stop (or a fatal capture error after at least one closed chunk) |
| `Finalizing` | `ReadyToTranscribe` | `interview_audio.mp4` written and hashed |
| `ReadyToTranscribe` | `Transcribing` | transcription started |
| `Transcribing` | `Exported` | `transcript.json` and `interview_transcript.docx` written |
| `Transcribing` | `ReadyToTranscribe` | transcription cancelled by the researcher |
| `Exported` | `Exported` | transcript edited and re-exported (the history grows) |
| `Exported` | `Transcribing` | researcher chose "transcribe again" (the old transcript is moved to `exports/`) |
| `Recording`, `Finalizing`, `Transcribing` | `Recovered` | at launch, a session is found in one of these states and its process is not running |
| `Recovered` | `Finalizing` | the app (or researcher) finalizes the closed chunks; a `Transcribing` session goes to `ReadyToTranscribe` instead |
| `Recovered` | `ReadyToTranscribe` | the session was in `Transcribing` when interrupted and `interview_audio.mp4` exists |
| any except `Exported` | `Failed` | an unrecoverable error; `error.stage` names the step, `error.code` the reason |
| `Failed` | `Finalizing` / `ReadyToTranscribe` | researcher pressed Retry; target depends on `error.stage` |

Every change appends `{state, at}` to `stateHistory` and rewrites `session.json` atomically (section 8). Any other
transition is refused and logged as `STATE_REFUSED`.

## 3. Capture chunks

- Two logical streams: `meeting` (system/meeting playback) and `mic` (local microphone).
- Each stream is written to **WAV chunks** in its own folder: names `000001.wav`, `000002.wav`, ... (6 digits).
- Format of every chunk: RIFF/WAVE, `WAVE_FORMAT_PCM` (1), **48 000 Hz, 1 channel (mono), 16-bit signed little-endian**.
  Inputs at other rates or channel counts are converted (downmix, resample) before writing.
- Chunk length: **10 s** of audio (480 000 frames); the last chunk is shorter. A chunk is closed (header sizes
  written, file flushed, SHA-256 recorded in `session.json`) before the next one is opened.
- A crash leaves at most one open chunk per stream. Recovery: compute the data size from the file length, rewrite the
  RIFF and data sizes, then treat it as closed. A chunk with fewer than 4 800 frames (0.1 s) of data may be dropped.
- Alignment: both streams share one monotonic clock started at `recording.startedAt`. Each stream records in
  `session.json` the offset in frames of its first sample from that start (`firstFrameOffset`). Gaps (device change,
  sleep) are filled with silence so that frame N of every stream is the same instant. A gap longer than 2 s is also
  logged (`CAPTURE_GAP`).

## 4. Final audio: `interview_audio.mp4`

- MPEG-4 container, **one AAC-LC audio track, 48 kHz, mono, 128 kbit/s**. No video track. No metadata with names
  or the participant ID (title = session code only).
- Content: `meeting` + `mic` mixed sample by sample with equal gain, then peak-limited to -1 dBFS. Duration equals the
  longest stream.
- Made only after Stop (`Finalizing`), streamed from the chunks; never written during the call.

## 5. transcript.json

Schema: `transcript.schema.json`. Rules:
- `segments` are ordered by `start`. Times are seconds from the start of the recording (3 decimals).
- `speaker` is one of `Researcher` (from the `mic` stream), `Participant` (from the `meeting` stream), `Unknown`
  (overlap, mixed source, or more than one remote speaker), or a researcher-entered label (1-40 characters).
  `speakerVerified` is `true` only when the researcher confirmed it.
- `text` is what the researcher sees and edits. `originalText` keeps the machine text of the first transcription.
- `history` records each edit: segment id, time, field, and **no text** (only lengths), so the log of corrections does
  not copy content outside the transcript. The transcript file itself holds the text.
- `review` holds the optional reviewer sign-off (reviewer initials 1-8 letters, time, note up to 200 characters).
- `disclaimer` holds the fixed sentence of section 7.

## 6. processing.log

- UTF-8 text, one event per line: `<RFC 3339 UTC ms> <LEVEL> <CODE> key=value key=value ...`
  LEVEL is `INFO`, `WARN` or `ERROR`. CODE is upper case with underscores (list below; new codes may be added).
- Allowed values: numbers, durations, sizes, sample rates, device **types** (`builtin`, `usb`, `bluetooth`,
  `virtual`), state names, error codes, file names that are session-relative (`capture/mic/000003.wav`), app/OS/model
  versions, thread counts, RAM and CPU figures.
- MUST NOT contain: speech, transcript text, participant ID, study code, names, device names (they can contain a
  person's name: "Josh's AirPods"), user names, absolute paths under the user's home folder.
- Codes: `APP_START`, `SESSION_CREATED`, `STATE_CHANGED`, `STATE_REFUSED`, `CAPTURE_STARTED`, `CAPTURE_DEVICE_CHANGED`,
  `CAPTURE_GAP`, `CAPTURE_ERROR`, `CHUNK_CLOSED`, `CHUNK_RECOVERED`, `DISK_LOW`, `PERMISSION_DENIED`, `ENCODE_STARTED`,
  `ENCODE_DONE`, `ENCODE_ERROR`, `TRANSCRIBE_STARTED`, `TRANSCRIBE_PROGRESS`, `TRANSCRIBE_CANCELLED`, `TRANSCRIBE_DONE`,
  `TRANSCRIBE_ERROR`, `MODEL_MISSING`, `EXPORT_DONE`, `EXPORT_ERROR`, `HASH_MISMATCH`, `RECOVERY_FOUND`, `RECOVERY_DONE`,
  `SESSION_DELETED`.

## 7. DOCX layout (`interview_transcript.docx`)

A4, margins 2.54 cm, font Calibri 11 pt (Word substitutes when missing), no images, no embedded audio.
1. Title (style Title): `Interview transcript`
2. A two-column table without borders: `Session code` | value; `Date` | `YYYY-MM-DD` (recording start, local);
   `Start time` | `HH:MM`; `Duration` | `HH:MM:SS`; `Study code` | value or `-`; `Transcription` | `whisper.cpp <version>,
   model <name>`; `Exported` | `YYYY-MM-DD HH:MM`.
3. Notice paragraph, italic: **"This transcript was produced by automated speech recognition and may contain errors.
   Check it against the audio recording before you rely on it."**
4. One paragraph per segment: `[HH:MM:SS]` (bold) + space + speaker label (bold) + `: ` + text. Timestamps are the
   segment start, floored to whole seconds, from the recording start. An unverified speaker label is followed by ` (unverified)`.
5. If the transcript was reviewed: a final paragraph `Reviewed by <initials> on YYYY-MM-DD.`
The participant ID is **not** written in the DOCX. Paragraph text uses the Word styles `Normal`, `Title`, `Table Grid`
(not shown) only, so the document stays editable.

## 8. Safe naming and atomic writes

- File and folder names are ASCII only: `[A-Za-z0-9._-]`, at most 64 characters, no leading dot, not a Windows
  reserved name (`CON`, `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`, case-insensitive, with or without extension).
  Any other character is replaced by `-`; runs of `-` collapse to one.
- Atomic write of every JSON and DOCX file: write to `.<name>.tmp-<random>` in the same folder, flush (macOS `fsync`/
  `F_FULLFSYNC`; Windows `FlushFileBuffers`), then rename over the target (macOS `rename`; Windows `ReplaceFile` or
  `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH`). Leftover `.*.tmp-*` files are deleted on the
  next open of the session.

## 9. Retention, audit and tamper detection (shared fields)

- `retention.deleteAfterDays` (1-3650 or `null` for keep) is copied from the settings into each session at creation;
  `retention.dueAt` follows from it. Deletion is always an explicit researcher action after a reminder; the app never
  deletes silently.
- `audit` is a list of `{event, at}` with events `created`, `recording_started`, `recording_stopped`, `transcribed`,
  `edited`, `exported`, `reviewed`, `opened`, `deleted_files`. No content, no user names.
- `integrity.hmac` (optional) is an HMAC-SHA256 over the canonical `hashes` object, with a key held by the OS
  (macOS Keychain, Windows DPAPI). Absent when the key store is not available.
