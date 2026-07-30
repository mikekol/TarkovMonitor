# Screenshot-to-Position: Technical Reference

The screenshot-to-position feature detects when the player takes an in-game screenshot, parses the EFT-encoded coordinates from the filename, and relays the position to one or more tarkov.dev browser remotes via WebSocket.

---

## Overview

EFT embeds the player's world position and orientation quaternion directly in every screenshot filename. The TUI watches the screenshots directory for new PNG files, parses the filename, converts the quaternion to a yaw angle, and sends the result to `socket.tarkov.dev`.

This is entirely Python-side. It does **not** go through gRPC or `TarkovMonitor.Service`.

---

## Components

| Component | File | Role |
|---|---|---|
| `get_screenshots_path()` | `screenshots.py` | Resolves the default EFT screenshots directory |
| `SCREENSHOT_PATTERN` | `screenshots.py` | Regex: validates EFT filename format |
| `POSITION_PATTERN` | `screenshots.py` | Regex: extracts xyz and quaternion from position string |
| `parse_screenshot()` | `screenshots.py` | Parses a filename into a `PlayerPosition` |
| `quaternions_to_yaw()` | `screenshots.py` | Converts quaternion (rx,ry,rz,rw) to yaw degrees |
| `PlayerPosition` | `screenshots.py` | Dataclass: x, y, z, rotation, filename, map_name |
| `ScreenshotWatcher` | `screenshots.py` | Async file watcher; calls a callback on new PNGs |
| `_restart_screenshot_watcher()` | `app.py` | Creates and starts `ScreenshotWatcher` on app init or settings save |
| `_on_new_screenshot()` | `app.py` | Callback: parses filename, logs position, triggers relay |
| `TarkovSocketClient` | `socket_client.py` | Sends position and map commands over WebSocket |

---

## Data Flow

```
EFT saves screenshot to disk
        │
        ▼
ScreenshotWatcher (watchfiles.awatch)
  watches directory for Change.added *.png
        │
        ▼
_on_new_screenshot(filename)         ← app.py
  resolve current map name
  call parse_screenshot(filename, map_name)
        │
        ├─ returns None → drop (filename didn't match pattern)
        │
        ▼
PlayerPosition(x, y, z, rotation, filename, map_name)
  log "Position on {map}: (x, y, z)"
        │
        ├─ no socket client → stop
        │
        ▼
_relay() coroutine (asyncio.create_task)
  send_player_position(pos, normalized_name, view_radius?)
  navigate_to_map(normalized_name)?    ← if navigate_map_on_position is on
        │
        ▼
TarkovSocketClient._broadcast()
  for each remote_id:
    open/reuse WebSocket to wss://socket.tarkov.dev?sessionid={id}-tm
    send JSON command
```

---

## Filename Format

EFT screenshot filenames encode position and orientation:

```
2020-07-02[01-49]_-106.0, 28.6, 190.6_0.0, -0.9, 0.1, -0.4 (0).png
│                  │                   │
│                  xyz world position  quaternion (rx, ry, rz, rw)
date[time]
```

- xyz: 1 or more decimal digits (e.g., `-106.0`, `28.6`)
- quaternion: exactly 1 digit before decimal, 1–5 digits after (e.g., `-0.9`, `0.12345`)
- Screenshot index `(0)` is ignored

### Regex patterns

**`SCREENSHOT_PATTERN`** — validates the filename and captures the position substring:
```
\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_?(?P<position>.+) \(\d\)\.png
```

**`POSITION_PATTERN`** — extracts xyz and quaternion from the position substring:
```
(?P<x>-?[\d]+\.[\d]+), (?P<y>-?[\d]+\.[\d]+), (?P<z>-?[\d]+\.[\d]+)_?
(?P<rx>-?[\d.]{1}\.[\d]{1,5}), (?P<ry>-?[\d.]{1}\.[\d]{1,5}),
(?P<rz>-?[\d.]{1}\.[\d]{1,5}), (?P<rw>-?[\d.]{1}\.[\d]{1,5})
```

---

## Path Resolution

`get_screenshots_path()` in `screenshots.py` reads the real Documents folder path from the Windows registry:

```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders
  → Personal
```

This is necessary because OneDrive folder redirection makes `~/Documents` return the wrong path on many Windows systems. The registry key is authoritative. Falls back to `os.path.expanduser("~/Documents")` on non-Windows.

Default path: `<Documents>\Escape from Tarkov\Screenshots`

The user can override this path in Settings. The value is persisted in `~/.tarkovmonitor-tui/settings.json` under `screenshots_path`.

---

## Startup and Lifecycle

On `on_mount()` (`app.py:327`), the app calls `_restart_screenshot_watcher()`. This method:

1. Stops and discards any existing watcher
2. Reads `screenshots_path` from settings (or computes the default)
3. Checks `path.exists()`:
   - If the path exists: creates `ScreenshotWatcher(path, self._on_new_screenshot)`, starts it, logs the path
   - If the path does not exist: logs an error and does not start the watcher

`_restart_screenshot_watcher()` is called again when settings are saved, allowing the user to correct the path at runtime.

---

## File Watching

`ScreenshotWatcher` wraps `watchfiles.awatch`:

```python
async for changes in awatch(str(self._path)):
    for change_type, changed_path in changes:
        if change_type == Change.added and changed_path.lower().endswith(".png"):
            self._on_screenshot(Path(changed_path).name)
```

Only `Change.added` events for `.png` files trigger the callback. Modified and deleted events are ignored.

The watcher runs as an `asyncio.Task` inside the Textual event loop. `stop()` cancels the task.

---

## Quaternion to Yaw Conversion

`quaternions_to_yaw(rx, ry, rz, rw)` matches `GameWatcher.QuarternionsToYaw` in the C# service. EFT's coordinate system maps the quaternion's y-axis to z in the formula:

```python
siny_cosp = 2.0 * (rw * ry + rx * rz)
cosy_cosp = 1.0 - 2.0 * (rz * rz + ry * ry)
return math.atan2(siny_cosp, cosy_cosp) * (180.0 / math.pi)
```

Result is yaw in degrees. This is stored as `PlayerPosition.rotation` and forwarded to the browser remote.

---

## WebSocket Relay

`TarkovSocketClient` (`socket_client.py`) maintains one persistent WebSocket connection per remote ID. Connections are opened lazily and reopened on failure.

### Player position message

```json
{
  "type": "command",
  "sessionID": "<remote_id>",
  "data": {
    "type": "playerPosition",
    "map": "<normalizedName>",
    "position": {"x": -106.0, "y": 28.6, "z": 190.6},
    "rotation": -172.3,
    "viewRadius": 200
  }
}
```

`viewRadius` is included only when `auto_zoom` is enabled and the map has changed since the last relay. Value is clamped to [50, 5000].

### Map navigation message

```json
{
  "type": "command",
  "sessionID": "<remote_id>",
  "data": {
    "type": "map",
    "value": "<normalizedName>"
  }
}
```

Sent after the position message when `navigate_map_on_position` is enabled.

---

## Settings

All screenshot-related settings live in `~/.tarkovmonitor-tui/settings.json`:

| Key | Type | Default | Description |
|---|---|---|---|
| `screenshots_path` | string | registry-derived | Directory to watch for new PNGs |
| `remote_ids` | string | `""` | Comma-separated tarkov.dev remote session IDs |
| `navigate_map_on_position` | bool | `false` | Send a map-switch command after each position relay |
| `auto_zoom` | bool | `false` | Include `viewRadius` on first relay to a new map |
| `view_radius` | int | `500` | Zoom radius in meters when `auto_zoom` is on |
| `custom_map` | string | `""` | Fallback map ID when no raid is active |

---

## Map Name Resolution

`_on_new_screenshot()` uses the current active raid map (`_current_raid_map`) when in a raid, falling back to `settings["custom_map"]`. This map ID is passed to `parse_screenshot()` as `current_map` (stored verbatim on `PlayerPosition.map_name`).

Before sending to the WebSocket, the ID is resolved through `TarkovDevClient.find_map()` to get:
- `map.normalized_name` — used as the wire value (e.g., `"woods"`)
- `map.name` — used in the TUI log (e.g., `"Woods"`)

---

## Manual Testing

The test server (`TarkovMonitor.Service.TestServer`) has no screenshot simulation capability — screenshot detection bypasses gRPC entirely. To trigger the watcher manually, copy any correctly-named PNG into the watched directory:

```powershell
$dir = "C:\Users\mikey\OneDrive\Documents\Escape from Tarkov\Screenshots"
Copy-Item "$dir\2020-07-02[01-49]_-106.0, 28.6, 190.6_0.0, -0.9, 0.1, -0.4 (0).png" "$dir\test_copy.png"
# Delete test_copy.png afterward
```

The TUI should log `Position on Woods: (-106.0, 28.6, 190.6)` and relay to any configured remotes.
