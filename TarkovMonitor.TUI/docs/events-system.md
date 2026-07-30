# TarkovMonitor TUI — Events System

The Events tab is the TUI's automation hub. Every game event the service detects can trigger an arbitrary shell command. This document covers how the system works, what events are available, and how it compares to the GUI's built-in `MediaController`.

---

## Overview

When a game event fires (e.g., raid starting, scav available, air filter turned on), the TUI checks whether a shell command has been configured for that event and, if so, runs it in a background thread. Output from the command is optionally captured and surfaced in the TUI log.

This design is intentionally composable: the TUI does not assume what you want to do when an event fires. Pausing music, switching a Stream Deck profile, sending a Discord notification, dimming a desk lamp, or running an arbitrary script — all are first-class options through the same mechanism.

---

## Implementation

| Component | File | Role |
|---|---|---|
| `EVENT_KEYS` | `events.py` | Master list of all supported event keys |
| `EVENT_LABELS` | `events.py` | Human-readable name for each event key |
| `EventManager` | `events.py` | Loads/saves config, executes commands |
| `EVENT_CATEGORIES` | `app.py` | Maps event keys to log category tags |
| Event handlers | `app.py` | Called by `grpc_client.py`; invoke `_run_event()` |

### Config storage

Per-event settings are persisted to `~/.tarkovmonitor-tui/event_config.json`:

```json
{
  "raid_starting": {
    "enabled": true,
    "command": "python scripts/streamdeck_press.py pause",
    "verbose": false
  },
  "raid_stopping": {
    "enabled": true,
    "command": "python scripts/streamdeck_press.py play",
    "verbose": false
  }
}
```

### Execution

`EventManager.run(key, on_output, force_verbose)` checks whether the event is enabled and has a command, then spawns a `daemon=True` thread running `EventManager._execute()`:

```python
threading.Thread(
    target=self._execute,
    args=(key, cmd, verbose, on_output),
    daemon=True,
).start()
```

`subprocess.run(cmd, shell=True, capture_output=True, text=True)` runs the command and, if `verbose` mode is on (or the command exits non-zero), forwards stdout/stderr to the TUI log via `on_output`.

### Thread safety

Because events arrive on background gRPC threads, `on_output` is called back into the Textual UI via `call_from_thread`. The `_run_event()` method in `app.py` captures the main thread ID at call time and chooses the right dispatch path:

```python
def _run_event(self, key: str, force_verbose: bool = False) -> None:
    main_thread_id = threading.get_ident()
    def on_output(msg: str) -> None:
        if threading.get_ident() == main_thread_id:
            self._log_message(msg, "system")
        else:
            self.call_from_thread(self._log_message, msg, "system")
    self._event_mgr.run(key, on_output=on_output, force_verbose=force_verbose)
```

---

## Supported Events

| Key | Label | Fires when... |
|---|---|---|
| `raid_starting` | Raid Starting | PMC countdown begins |
| `raid_stopping` | Raid Stopping | Game signals raid end (before post-raid screen) |
| `raid_ended` | Raid Ended | Post-raid screen shown |
| `match_found` | Match Found | Server locked in, queueTime available |
| `runthrough_over` | Runthrough Over | Runthrough time elapsed |
| `air_filter_on` | Air Filter On | Air filter activated |
| `air_filter_off` | Air Filter Off | Air filter deactivated |
| `scav_available` | Scav Available | Scav cooldown expires |
| `restart_failed_tasks` | Restart Failed Tasks | MapLoading fires with restartable failed tasks |
| `quest_items` | Quest Items | Reminder fires on match found |

---

## Comparison with GUI MediaController

The GUI introduced `MediaController.cs` (upstream merge), a Windows-native class that automatically pauses and resumes music players around raids. The TUI takes a different approach — composable shell commands instead of a built-in feature — with deliberate trade-offs.

### What MediaController does

- Discovers all running music apps via **Windows Media Sessions API** (`GlobalSystemMediaTransportControlsSessionManager`) — works with Spotify, foobar2000, Winamp, any app that exposes a media session.
- Excludes browser sources (Chrome, Edge, Firefox, etc.) and non-music playback types automatically.
- **Fades out** audio over 3 seconds with cubic easing via **WASAPI** before sending the pause command.
- Holds volume at zero until the audio peak drops below 0.001 (silence detection), preventing buffer blips.
- On resume: waits 2 seconds, then **fades back in**.
- Retries on `ExitedPostRaidMenus` for sessions that didn't resume cleanly after `RaidStopping`.
- Requires no user configuration — a single `pauseMediaOnRaid` checkbox enables the whole thing.

### What the TUI Events system provides

- Any shell command wired to `raid_starting` / `raid_stopping` / any other event.
- For Spotify specifically: `scripts/streamdeck_press.py pause` / `play`, which routes through the Elgato MCP server → Stream Deck → Schmotify plugin.
- No fade, no silence detection, no automatic retry — but also no required infrastructure beyond whatever your command invokes.

### Side-by-side

| | GUI `MediaController` | TUI Events |
|---|---|---|
| **Apps controlled** | Any music app (universal) | Depends on command — Spotify via Schmotify by default |
| **Setup** | One checkbox | Configure Events tab; Elgato MCP + Stream Deck for Schmotify |
| **Volume fade** | 3-second cubic ease | None |
| **Silence detection** | Yes | No |
| **Automatic retry** | Yes (`ExitedPostRaidMenus`) | No |
| **Other game events** | Media only | Any event → any command |
| **Non-media automation** | Not supported | Discord, smart home, Stream Deck, scripts, anything |

### The composability advantage

The TUI's Events system is not a media controller — it's an automation trigger for every game event. Users in the community wire it to things the GUI can't do at all:

- `raid_starting` → switch Stream Deck profile to "in-raid" layout
- `match_found` → send a Discord webhook (e.g., "found match on Lighthouse")
- `air_filter_on` → toggle a smart plug via Home Assistant CLI
- `scav_available` → play a custom sound via a standalone player
- `raid_stopping` → run a backup script for screenshots

The GUI's `MediaController` is excellent at one thing and requires no setup to use. The TUI's Events system requires more configuration but generalises to any response to any event.

---

## Developer TODO — winsdk Media Control

To bring TUI media control closer to `MediaController` parity on Windows without requiring Stream Deck or Elgato MCP, evaluate wrapping `Windows.Media.Control` via the [`winsdk`](https://pypi.org/project/winsdk/) Python package, which exposes WinRT APIs to Python.

The target implementation would be a standalone script at `scripts/media_control.py`:

```
usage: media_control.py [pause|play|status]
```

- `pause` — discover playing music sessions via `GlobalSystemMediaTransportControlsSessionManager`, skip browsers and non-music types, call `TryPauseAsync()` on each.
- `play` — call `TryPlayAsync()` on previously paused sessions (state persisted to a temp file between invocations).
- `status` — list current sessions and their playback state.

Wire to Events exactly as `streamdeck_press.py` is today — no changes to the Events system needed.

**Before implementing:** verify that `winsdk` supports `GlobalSystemMediaTransportControlsSessionManager` on the target Python and Windows versions. As of mid-2026 `winsdk` is functional but the WinRT async bridging can be finicky. The `winrt-Windows.Media.Control` sub-package is the relevant one.

Fade-in/fade-out (WASAPI) is out of scope for an initial implementation — that would require `pycaw` or similar, which is a separate dependency. A hard pause/play that matches the basic `pauseMediaOnRaid` behavior is the first milestone.
