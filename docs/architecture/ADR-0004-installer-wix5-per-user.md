# ADR-0004: Per-user WiX 5 installer

Status: Accepted (2026-10-30)

## Context

The plan requires a Windows x64 installer .exe built with WiX Burn or another native installer tool.
It must add a Start Menu shortcut and an uninstall entry.
It must bundle the .NET runtime, the model and FFmpeg.
Normal use must not need administrator rights.

## Decision

Use WiX Toolset version 5.0.2.
Build a per-user MSI package (Scope perUser).
It installs to the folder %LocalAppData%\Programs\Interview Recorder.
Build a WiX Burn bundle .exe that contains only this MSI, so Windows does not ask for administrator rights.
Publish the app self-contained for win-x64, so no separate .NET runtime installation is needed.
Do not use WiX 6 or WiX 7, because their binary releases come with the Open Source Maintenance Fee agreement, which can require a paid fee.
WiX 5.0.2 is under the Microsoft Reciprocal License.
The GitHub Actions job "installer" builds the MSI and the .exe on windows-latest and writes SHA-256 hashes.
Signing runs only when a code-signing certificate is stored as a repository secret.

## Consequences

The installer is unsigned until a certificate exists.
Windows SmartScreen shows a warning for an unsigned installer.
The user selects "More info", then "Run anyway".
Each user of a computer installs the app separately.
An upgrade replaces the old version (major upgrade).
Uninstall removes the program files and the Start Menu shortcut.
Uninstall does not remove the user's interview data folder.
