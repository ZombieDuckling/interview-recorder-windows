# ADR-0005: Encryption at rest (Recorder.Security)

Status: accepted

## Context

Working files in a session folder (audio, transcripts) are sensitive research data. They must be unreadable without the user's Windows account, tampering and truncation must be detected, and memory stays bounded (files are streamed, never loaded whole).

## Decision

### Keys

- One random 256-bit **session key** per session folder, stored at `<session>/.keys/session.key`, wrapped with **DPAPI `CurrentUser`** (plus a fixed, non-secret app entropy string).
- The key file is written atomically: temp file `.session.key.tmp-<random>` in `.keys/`, flushed to disk, then `File.Move(..., overwrite: false)`. If another caller won the race, the winner's key is loaded. Creation is serialised per session folder in-process. A crash can never leave a truncated `session.key`.
- Release builds contain no plaintext key path. The wrapper is an internal seam (`ISessionKeyWrapper`); only the test assembly installs a plaintext wrapper, and only off Windows.
- Per-file **subkey**: `HKDF-SHA256(ikm = session key, salt = 16 random header bytes, info = "InterviewRecorder-AES-GCM-file-v1")`. Each file therefore has its own AES key, so nonce reuse across files in a session is impossible by construction. Derived keys are zeroed on dispose.
- MAC key: `HKDF-SHA256(session key, info = "InterviewRecorder-HMAC-SHA256-v1")`, zeroed after use. Compare MACs only with `FileProtector.VerifyMacAsync` (constant time), never `==`.

### File format (`IRG2`)

```
offset  size  field
0       4     magic "IRG2"
4       16    file salt (random)
20      ...   frames, repeated:
                4   plaintext length, uint32 little-endian (<= 65536)
                1   isFinal (0 or 1)
                n   ciphertext (n = plaintext length)
                16  GCM tag
```

- Nonce (12 bytes): 4 zero bytes + frame index as uint64 little-endian. Safe because the key is unique per file and the index is unique per frame.
- AAD: frame index (8 bytes LE) + isFinal (1 byte) + context (UTF-8). The **context** is the session-relative path, with `/` separators, lower-cased. Renaming or swapping files (for example `mic` and `meeting`) makes decryption fail.
- Frames hold at most 64 KiB of plaintext, so memory is bounded.
- The last frame has `isFinal = 1` and may be empty (a file whose length is an exact multiple of 64 KiB ends with an empty final frame). A reader reports an error for: wrong magic, bad length, tag failure (flipped bits, reorder, wrong key, wrong context), a missing final frame (truncation at a frame boundary included), and trailing bytes after the final frame.
- Paths passed to `FileProtector` must resolve inside the session folder; anything else (`..`, other roots) throws.

### Crash and failure behaviour

- If any write fails or is cancelled, the writer is **faulted**: later writes throw, and `Dispose` does **not** write a final frame. The reader then reports "missing final frame", so a partial file never looks complete.
- Only whole frames are written to the file. The partial last frame (up to 64 KiB) lives in memory until `Dispose`; `FlushAsync` does not force it out and a crash loses it. The file then has no final frame and cannot be sealed. The reader returns every verified frame before it throws, so recovery code can read the recoverable part.

### What DPAPI `CurrentUser` protects against

Protects against: another Windows account on the same machine, and offline access to the disk (stolen drive, backup copy) without the user's logon secret.

Does not protect against: malware or any process running as the same user (it can call DPAPI itself); a local Administrator who resets the account or takes ownership of the files. The wrapped key sits next to the data, so the protection comes from DPAPI, not from hiding the key file. `RestrictToCurrentUser` sets a protected ACL (current user + SYSTEM only, inherited by new files); local Administrators can still take ownership.

### Deletion

`DeleteSessionAsync` requires `session.json` in the folder (and, when a data root is configured, that the folder is directly under it), refuses folders containing reparse points (junctions or symlinks), clears read-only flags, overwrites each file with zeros, flushes to disk, then deletes.

The overwrite is **best effort**. On SSDs, wear levelling and copy-on-write file systems leave old blocks that an overwrite does not reach. What actually makes the ciphertext unreadable is destroying the DPAPI-wrapped session key, which is deleted with the folder. User-facing privacy text must say this.

## Consequences

- Format change from the first draft (`IRG1`, random 4-byte nonce prefix) is free: nothing has shipped.
- `IFileProtector` is unchanged. `VerifyMacAsync` is an extra public method on `FileProtector`; callers that need it hold the concrete type, or win-lead can promote it into the interface.
