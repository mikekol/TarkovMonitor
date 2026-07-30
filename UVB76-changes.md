# UVB76 Branch: Summary of Changes

Hi all. This document describes the changes we've made on the `UVB76` branch and the thinking behind them. The goal was to explore what a service-first architecture might look like for TarkovMonitor — separating log monitoring from the UI so it can run in the background, and opening the door for other clients (like a streamer overlay) to consume game events without needing the main UI running.

Everything here is experimental and offered for discussion. We tried to stay close to the existing patterns and conventions wherever possible.

---

## What Changed at a High Level

The branch introduces a **Windows service** that owns all EFT log monitoring and TarkovTracker API calls. The existing UI becomes a **gRPC client** that connects to the service to receive game events and manage configuration. The core logic (GameWatcher, LogMonitor, etc.) is extracted into a shared library so both the service and UI can reference it without pulling in WinForms.

### New Projects

| Project | Purpose |
|---------|---------|
| `TarkovMonitor.Core` | Shared library containing GameWatcher, LogMonitor, LogLine, LogMessageTypes, GetProcessFilename, RegistrySettings, CoreJsonContext, and TarkovTrackerClient. No WinForms dependency. |
| `TarkovMonitor.Service` | Windows service (hosted via `Microsoft.Extensions.Hosting`). Runs GameWatcher, broadcasts events over gRPC streaming, handles TarkovTracker API updates. Listens on `localhost:50051`. |
| `TarkovMonitor.Service.Contracts` | Shared project containing the proto-generated gRPC types. Both the service and any client project reference this. |
| `TarkovMonitor.Service.TestServer` | Interactive CLI that implements the same gRPC interface as the real service. Lets you fire synthetic events (`raid_start`, `task`, etc.) to test the UI without needing EFT running. |
| `TarkovMonitor.StreamerDashboard` | A WPF app that subscribes to the same gRPC event stream and serves a browser-based overlay (SignalR → HTML/CSS/JS) for OBS or similar. Early-stage. |
| `TarkovMonitor.Installer` | WiX 4.0.6 MSI installer. Packages the service, UI, and dashboard into a single installer with feature selection, firewall rule, and Start Menu/Desktop shortcuts. |
| `Scripts/` | PowerShell scripts for building the MSI (`Build-Installer.ps1`) and manually installing/uninstalling the service via `sc.exe` during development. |

### Modified Existing Files

The main UI project (`TarkovMonitor/`) still works the same way from the user's perspective — same Blazor UI, same event handlers, same settings. The internal wiring changed:

- **`MainBlazorUI.cs`** — No longer instantiates `GameWatcher` directly. Instead creates a `GameEventClient` (gRPC client) that provides the same typed event surface. Connects to the service on startup with a retry loop, and reconnects automatically if the service restarts.
- **`TimersManager.cs`** — Constructor takes `GameEventClient` instead of `GameWatcher`. Same timer logic, different event source.
- **`Stats.cs`** — `AddFleaSale` now takes plain parameters instead of `FleaSoldMessageLogContent` + `Profile`. This avoids a dependency on the Core types for what's essentially an INSERT statement.
- **`TarkovTracker.cs`** — Token and domain are now per-EFT-profile (more on this below).
- **`Settings.razor`** — Token and domain fields read/write per the active EFT profile.
- **`Properties/Settings.settings`** — Added `tarkovTrackerDomains` setting (JSON dictionary, same shape as `tarkovTrackerTokens`).

---

## The Service Architecture

### Why a Service?

The main motivation was that TarkovTracker task updates only fire when the UI is open. If you close TarkovMonitor mid-raid, quest completions are missed. A background service solves this — GameWatcher runs continuously, and the TarkovTracker API is called from the service regardless of whether the UI is up.

A secondary benefit is that other applications can subscribe to the same event stream. The StreamerDashboard is an example of this — it connects to the service just like the UI does and turns game events into OBS overlay updates.

### How It Works

The service is a standard .NET `IHostedService` application using Kestrel for HTTP/2 (required by gRPC). On startup:

1. `GameWatcherHostedService` starts `GameWatcher`, which auto-detects the EFT log path from the registry (or uses a custom path from config).
2. `GameEventBroadcasterService` subscribes to all GameWatcher events and serializes them into a generic `GameEvent` proto message with a `map<string, string>` data payload. Connected clients receive these via server-side streaming.
3. `TarkovTrackerUpdaterService` subscribes to task-related GameWatcher events and calls the TarkovTracker API directly, using the token and domain configured for the active EFT profile.

### The Proto Contract

We went with a single generic event message rather than per-event proto messages:

```protobuf
message GameEvent {
  string event_type = 1;
  int64 timestamp_ms = 2;
  map<string, string> data = 3;
}
```

The tradeoff here is that adding a new event type doesn't require a proto change or regenerating clients — you just add a subscription in the service and a dispatch case in the client. The downside is less type safety at the wire level, but the typed event args are reconstructed on the client side in `GameEventClient.DispatchGameEvent()`.

### Configuration RPCs

The service exposes `GetConfig`, `UpdateConfig`, and `GetStatus` RPCs. The UI uses these to push settings changes (custom log path, TarkovTracker tokens/domains, custom map) to the service. Configuration is persisted in `appsettings.json` on the service side.

`UpdateConfig` uses partial-update semantics for the token and domain maps — only keys present in the request are modified. This means the UI can push a single profile's token without affecting other profiles.

---

## TarkovMonitor.Core: Shared Library

The existing files `GameWatcher.cs`, `LogMonitor.cs`, `LogLine.cs`, `LogMessageTypes.cs`, and `GetProcessFilename.cs` were moved from the `TarkovMonitor/` project into `TarkovMonitor.Core/`. The namespace was updated but the logic is unchanged.

The reason for the move is straightforward: the service needs to reference GameWatcher and its dependencies, but it shouldn't need to pull in WinForms. Core has no `UseWindowsForms` — it targets `net10.0-windows` for registry access but is otherwise a plain class library.

A few new files were added to Core:

- **`RegistrySettings.cs`** — Wraps `HKLM\SOFTWARE\TarkovMonitor` for reading/writing the custom log path. The service (running as LocalSystem) writes; the UI only reads.
- **`CoreJsonContext.cs`** — Source-generated `JsonSerializerContext` for all types that cross the gRPC boundary. This avoids runtime reflection and is required for Native AOT compatibility (which the service is architecturally ready for, though we haven't enabled it yet).
- **`TarkovTrackerClient.cs`** — A stateless Refit HTTP client for the TarkovTracker API. The service's `TarkovTrackerUpdaterService` creates a fresh instance per event call to avoid holding stale tokens.

---

## Per-Profile TarkovTracker Configuration

This was motivated by a practical scenario: if multiple family members play EFT on the same PC, each with their own BSG account, a single global TarkovTracker token and domain means the last person to open settings overwrites everyone else's configuration.

The fix: both tokens and domains are now keyed by EFT profile ID (the unique identifier BSG assigns to each account). Each profile gets its own independent TarkovTracker settings.

### What changed

- **`TarkovTracker.cs`** in the UI gained `GetDomain(profileId)`, `SetDomain(profileId, domain)`, and a `_profileDomains` dictionary persisted to `Properties.Settings.Default.tarkovTrackerDomains`.
- **`SetProfile()`** reinitialises the Refit API client when switching to a profile that uses a different domain. It also includes a one-time migration: the first time a profile ID is encountered, it seeds the per-profile domain from the legacy `tarkovTrackerDomain` single-string setting (if present), so existing users don't lose their configuration.
- **Proto and service config**: `UpdateConfigRequest` and `ServiceConfig` both carry `map<string, string> tarkov_tracker_tokens` and `map<string, string> tarkov_tracker_domains`. The old singular fields are retained in the proto for backward compatibility but marked deprecated.
- **`TarkovTrackerUpdaterService`** in the service uses `_config.GetDomainForProfile(profileId)` when constructing the Refit client, so each profile's API calls go to the correct TarkovTracker host.

---

## .NET 10 Upgrade

All projects were upgraded from `net6.0-windows` to `net10.0-windows`. Package references were updated accordingly:

| Package | Before | After |
|---------|--------|-------|
| Microsoft.AspNetCore.Components.WebView.WindowsForms | 6.0.424 | 10.0.71 |
| Microsoft.Extensions.Localization | 6.0.28 | 10.0.9 |
| MudBlazor | 6.19.1 | 9.5.0 |
| Humanizer | 2.14.1 | 3.0.10 |
| NAudio | 2.2.1 | 2.3.0 |
| Refit | 7.2.22 | 11.0.1 |
| Websocket.Client | 5.2.0 | 5.5.0 |

`Directory.Build.props` was updated to centralize versioning (`ProjectVersion = 2.10.15.0`) and enable `PublishReadyToRun` for pre-compiled hot paths at publish time.

One new warning suppression was added to `TarkovMonitor.csproj`: `WFO1000`, which is a .NET 10 WinForms analyzer warning triggered by the pre-existing `Splash.cs` file. Not a code change — just suppressing a new diagnostic.

---

## Media Control: TUI Events vs. GUI MediaController

The main GUI (introduced in a recent upstream merge) includes `MediaController.cs` — a Windows-native class that automatically pauses music players at raid start and resumes them afterward. The TUI takes a deliberately different approach via its **Events system**.

### What MediaController does

`MediaController` uses two Windows-only APIs:

- **Windows Media Sessions** (`GlobalSystemMediaTransportControlsSessionManager`) — the same API powering the Win+K overlay. It discovers any running music app (Spotify, foobar2000, Winamp, etc.) and sends play/pause commands directly.
- **NAudio/WASAPI** — direct access to per-process audio session volumes, enabling a smooth 3-second cubic-eased fade-out before pause and fade-in on resume.

The full flow on raid start: discover playing music sessions → identify music (vs. video/browser audio) → fade out over 3 seconds → send pause command → hold muted until audio peak drops below threshold → restore volume. On raid stop: wait 2 seconds → fade back in → retry on `ExitedPostRaidMenus` for any sessions that didn't resume cleanly. All of this is automatic, requires no setup, and is gated by a single `pauseMediaOnRaid` checkbox.

### What the TUI Events system does

The TUI has no access to Windows Media Sessions or WASAPI (it's Python, and targeting cross-platform portability). Instead, the **Events tab** lets users assign an arbitrary shell command to any game event (`raid_starting`, `raid_stopping`, `raid_ended`, etc.). When the event fires, the command runs in a background thread and its output is optionally surfaced in the TUI log.

For media control specifically, the recommended setup is wiring `raid_starting` → `python scripts/streamdeck_press.py pause` and `raid_stopping` → `python scripts/streamdeck_press.py play`. The `streamdeck_press.py` script controls Spotify via the Elgato MCP server → Stream Deck → Schmotify plugin chain.

### Comparison

| | GUI `MediaController` | TUI Events + `streamdeck_press.py` |
|---|---|---|
| **Setup required** | None (checkbox only) | Elgato MCP server + Stream Deck + Schmotify plugin |
| **Apps controlled** | Any music app (universal) | Spotify only |
| **Volume fade** | 3-second cubic ease-in/out | None (hard cut) |
| **Silence detection** | Yes (waits for audio peak < 0.001) | No |
| **Retry on resume** | Yes (`ExitedPostRaidMenus`) | No |
| **Extensibility** | Media control only | Any shell command |

### Why the TUI approach is more powerful

The Events system's real value is **composability**. Every game event can trigger any command, not just media control. Examples of what users wire to events today:

- `raid_starting` → pause Spotify via Schmotify
- `raid_stopping` → resume Spotify
- `raid_starting` → dim desk lamp via a Home Assistant CLI command
- `match_found` → send a Discord webhook notification
- `air_filter_on` / `air_filter_off` → run a custom script to record filter state
- `scav_available` → trigger a Stream Deck profile switch

The GUI's MediaController is excellent at what it does — zero setup, works with every music player, smooth fade. The TUI's Events system trades the polish of that one feature for the ability to automate arbitrary responses to every game event.

### Developer TODO — winsdk Python wrapper

To bring TUI media control closer to `MediaController` parity on Windows without requiring Stream Deck, evaluate wrapping `Windows.Media.Control` via the `winsdk` Python package (which exposes WinRT APIs). This would allow the TUI to discover playing music sessions and send play/pause via the same Windows Media Session API the GUI uses — no external dependencies required. The `winsdk` package is Windows-only and relatively young; verify it supports `GlobalSystemMediaTransportControlsSessionManager` before committing to it. If viable, implement as a standalone script (e.g., `scripts/media_control.py pause`) so it stays composable with the Events system rather than being built in.

---

## MSI Installer

The `TarkovMonitor.Installer` project uses WiX 4.0.6 to build a standard MSI. The installer packages:

- **Service** — installed as a Windows service (`TarkovMonitor.Service.exe`) with automatic startup. Includes a firewall rule allowing local connections on port 50051.
- **UI** — the existing TarkovMonitor application, with Start Menu and Desktop shortcuts.
- **StreamerDashboard** — optional, same shortcut treatment.
- **Tools** — management console shortcut (for the `Install-TarkovMonitorService.ps1` script).

The feature tree lets users choose which components to install. `Build-Installer.ps1` handles the full pipeline: publishes all projects, generates harvest WXS files (WiX's file-listing format), and builds the MSI into `dist/`.

**Developer note — why WiX 4 and not WiX 5:** WiX 3 was the traditional choice but relies on .NET Framework MSBuild tooling that's incompatible with SDK-style `.csproj` files. WiX 4 integrates cleanly via `dotnet tool install` and is what the project was originally built on. WiX 5 exists but introduces breaking changes to WXS authoring syntax — particularly around custom actions (`WixQuietExec` was removed in favour of `WixToolset.Mba.Core`) and the Windows Service installer extensions — that would require a non-trivial rewrite of `Service.wxs`, `ConfigureService.wxs`, and `ServiceAccount.wxs` for no functional gain at this time. Known WiX 4.0.6 quirk: `HarvestDirectory` in the `.wixproj` is silently broken (files are not harvested), so `Build-Installer.ps1` calls `wix heat dir` directly as a workaround. Evaluate WiX 5 once it has broader ecosystem adoption and once the Windows Service authoring story stabilises.

---

## Client Developer Documentation

A new `client-dev.md` file documents the gRPC contract, event types, and reconnection patterns for anyone building a client against the service. It includes the proto definitions, example event payloads, and C# / TypeScript client code samples.

---

## What's Not Changed

- The Blazor UI itself — same pages, same layout, same user experience. The wiring underneath is different, but the user-facing behavior is the same.
- Game event detection logic — GameWatcher, LogMonitor, and all the log parsing is unchanged. It just runs in the service now instead of the UI process.
- The tarkov.dev integration, sound notifications, group tracking, goon tracking, and statistics features are all untouched.
- The existing `Properties.Settings.Default` storage for UI-side preferences is unchanged. The service has its own `appsettings.json` for service-side config.

---

## Current Status

The implementation is complete and builds cleanly. We're in the testing phase:

- [ ] End-to-end: service + UI, verify events flow through
- [ ] TestServer: fire events from CLI, verify UI reacts
- [ ] Reconnection: kill service while UI is running, restart, verify auto-reconnect
- [ ] MSI: install, verify service registers, firewall rule, shortcuts, uninstall cleanup
- [ ] Multi-profile: configure different tokens/domains for two profiles, verify isolation

---

## Questions and Feedback

We'd appreciate any feedback on the approach, the proto design, or anything that looks off. This was built as an experiment to see if the architecture holds up — nothing here is set in stone, and we're happy to rework anything that doesn't fit with the project's direction.
