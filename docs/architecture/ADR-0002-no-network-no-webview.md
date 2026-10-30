# ADR-0002: No network access and no web view

Status: Accepted (2026-10-30)

## Context

The app handles research interview audio and transcripts. The plan forbids hosted services, telemetry, web UI, web views and a localhost server. The plan requires that network monitoring shows no research content leaves the machine.

## Decision

The app makes no network calls. There is no update check and no auto-updater in version 1; a new version is a new installer. No NuGet package that adds a web view is allowed, for example Microsoft.Web.WebView2. The build runs a guard script. The guard fails the build if source files use WebView2, CoreWebView2, HttpClient, WebClient, Socket or TcpClient, or if packages.lock.json contains Microsoft.Web.WebView2. A runtime test checks that the app process does not load EmbeddedBrowserWebView.dll or WebView2Loader.dll.

## Consequences

Model files and FFmpeg ship with the installer. The release tests include a network monitoring run during a full session. A future feature that needs the network needs a new ADR and the approval of the project owner.
