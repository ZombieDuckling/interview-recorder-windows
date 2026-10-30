# ADR-0001: WPF on .NET 10 for the Windows app

Status: Accepted (2026-10-30)

## Context

The plan requires a fully native Windows desktop UI. Target machines have 8 GB RAM. The app must install without administrator rights. The macOS app is a separate native SwiftUI app.

## Decision

Use C# on .NET 10 (long-term support until 14 November 2028). Use WPF for the user interface. Do not use WinUI 3 or the Windows App SDK. Publish the app self-contained, so the installer does not need a .NET runtime installation with administrator rights. Build x64 first. Build ARM64 only after tests on ARM64 hardware.

## Consequences

WPF builds only on Windows. Recorder.Core targets plain net10.0, so its unit tests can run on any operating system. The UI uses system colours so that Windows High Contrast themes work. Every control gets an AutomationId for UI Automation tests and screen readers. The self-contained publish makes the installer larger.
