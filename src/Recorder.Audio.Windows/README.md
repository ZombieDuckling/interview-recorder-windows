# Windows capture

Implements Core's `IAudioCapture`, `ICaptureSession`, and `ICaptureRecovery`. Real Windows hardware validation is outstanding; synthetic tests do not establish release readiness.

## Integration

- The caller creates a protected, unique session folder and consented `session.json` before `StartAsync`. Reusing a folder containing original chunks is rejected. Capture never changes `state` or `stateHistory`.
- Capture patches Core's manifest types through `AtomicFileWrite`. The pipeline must await `StopAsync` before its next manifest write, then reload the current manifest so its state transition preserves capture metadata/hashes.
- `StreamFiles.ChunkPaths` are absolute, for the media finalizer. Manifest/log paths remain session-relative. Failed open tails are excluded from the returned closed files and can be recovered separately.
- `StopAsync` is idempotent. Cancelling the caller's wait does not abandon finalization. Await disposal before deleting a preview folder. Fatal storage errors stop both inputs; `Complete` is false after storage failure or queue overflow.
- Subscribe to faults immediately after start. Startup fallback warnings are retained until subscription. Level/fault observers run on background threads and must dispatch to WPF without blocking capture. Recovered device faults mean recording continues. Unrecovered device faults are retried while silence preserves the timeline.

## Timing, storage, and devices

Both streams use one Stopwatch/QPC origin, paired with `recording.startedAt`. Native packet QPCs (100 ns units) set placement; invalid timestamps fall back to the monotonic arrival clock. Stored streams begin with timeline silence at zero (`firstFrameOffset = 0`). Overlap is trimmed, gaps are filled, and no-packet loopback silence advances continuously. A 250 ms writer watermark tolerates callback jitter; stop flushes to the frozen session frame. Missing intervals over two seconds produce `CAPTURE_GAP` even when detected across many small timer ticks.

Input PCM8/16/24/32 or float32/64, including extensible PCM/float, is downmixed and converted with a stateful rational-phase linear resampler. Native packets are split into bounded blocks. Each stream queues at most 40 blocks of 4,800 PCM16 frames (384 KB). Overflow is reported and its missing time is filled rather than compressing the recording. Native capture threads request the MMCSS `Audio` profile. Conversion/writing and meters do not retain recording-length audio arrays.

Chunks contain 480,000 frames (10 s), except the tail: mono PCM16, 48 kHz. Each closes its header, flushes to disk, computes SHA-256, and atomically acknowledges the manifest before opening the next. Levels are produced about every 50 ms, including silence. Disk space is checked every second and at chunk boundaries; 256 MiB warns and 16 MiB reserve stops capture to preserve recovery space. Actual disk-full HRESULTs are reported as `DiskFull`.

Device removal/default changes and audio-format notifications trigger disposal/join of the old input and creation of a fresh converter/input, retaining the writers and clock. An explicitly selected available device is retained across unrelated default changes. Missing selected devices fall back to the default; unavailable devices retry every second. Mic defaults prefer communications, then console. Meeting defaults use multimedia.

Process-tree loopback is attempted when requested with a valid PID and supported OS, with endpoint fallback on activation/start failure. Microsoft specifies **Windows build 20348 or later**: [Application loopback audio capture](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/). This excludes ordinary Windows 10 19045 builds. The process tree includes child processes and is not a browser-tab isolation boundary. Endpoint fallback can capture unrelated system audio; the UI must keep its scope notice visible.

Recovery validates every known closed original/hash before touching either stream's untracked tail. It rejects missing chunks, altered headers/hashes, nonsequential chunks, links, and multiple untracked files. Only the next numbered canonical WAV tail is repaired and flushed; tails shorter than 4,800 frames may be dropped. Unknown schema majors are refused unchanged. Recovery does not change pipeline state.

## Verification

All builds/tests on the shared Mac must use `~/desktop-app/_shared/heavy.sh win-audio ...`, with `-m:1 -nodeReuse:false -p:UseSharedCompilation=false`, followed by `dotnet build-server shutdown`. Run the single audio test project. Non-hardware suite: `--filter 'Category!=Hardware'`.

`HardwareTests.DefaultDevicesProduceClosedHashed48kChunks` is tagged `Category=Hardware` and `Category=Windows`. It records for 11 seconds only when Windows and `RECORDER_RUN_HARDWARE_TESTS=1` are both present. A consenting operator must explicitly opt in, speak, and play a known test sound. The test verifies files and hashes; listening/measurement is still required to prove intelligibility.

Remaining Windows acceptance: Chrome/Edge Meet on Windows 10/11; process-tree isolation and endpoint fallback; muted/no-playback silence; 44.1/48/96 kHz devices; USB/Bluetooth switching; unplug/replug; default changes; sleep/wake; lock/UAC; disk low/full; abrupt process termination/recovery; two-hour synchronization and 8 GB memory/CPU measurement. Confirm both voices audibly and measure drift from correlated signals. No hardware, performance, or real recording result is claimed from the Mac tests.
