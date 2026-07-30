# TUI Screenshot-to-Location Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Watch EFT's screenshots folder for new PNG files, parse player position/rotation from the filename, and relay it to one or more tarkov.dev browser remotes via WebSocket — matching the feature the .NET GUI provides.

**Architecture:** Three layers — (1) fix the existing quaternion formula and add an async file watcher in `screenshots.py`; (2) a new `socket_client.py` that maintains persistent WebSocket connections to `socket.tarkov.dev` and broadcasts position/map-change commands; (3) `app.py` wires them together with four new settings fields and restarts the watcher/client on settings save.

**Tech Stack:** Python 3.11+, Textual, `watchfiles` (already a dep), `websockets>=12.0` (new dep), `pytest-asyncio`.

## Global Constraints

- All code lives under `TarkovMonitor.TUI/tarkovmonitor_tui/` and tests under `TarkovMonitor.TUI/tests/`.
- Run tests from `TarkovMonitor.TUI/` with `.venv\Scripts\python -m pytest tests/ -v`.
- `asyncio_mode = "auto"` — no `@pytest.mark.asyncio` decorator needed on any test.
- No new deps beyond `websockets>=12.0` — `watchfiles` is already in `pyproject.toml`.
- YAGNI: no 30-minute idle-disconnect timer (add later if needed), no per-remote reconnect backoff.
- Settings key names must match exactly: `remote_ids`, `auto_zoom`, `view_radius`, `navigate_map_on_position`.
- WebSocket URL template: `wss://socket.tarkov.dev?sessionid={remote_id}-tm`
- `viewRadius` is only sent when `auto_zoom` is enabled AND the map has changed since the last send.
- `view_radius` setting is an integer 50–5000; default 500.

---

### Task 1: Fix quaternion formula and add async ScreenshotWatcher

The existing `quaternions_to_yaw` in `screenshots.py` has two bugs versus the C# reference.  
C# `QuarternionsToYaw` is called as `QuarternionsToYaw(rx, ry, rz, rw)` with parameter names `(x, z, y, w)`, meaning the function body uses `rw*ry` and `rx*rz` for `siny_cosp`, and `rz²+ry²` for `cosy_cosp`. The Python version uses the wrong signs and wrong squared term.  
`ScreenshotWatcher` uses `watchfiles.awatch` (already a dep) to watch a directory asynchronously and calls a sync callback for each new `.png` file.

**Files:**
- Modify: `TarkovMonitor.TUI/tarkovmonitor_tui/screenshots.py`
- Create: `TarkovMonitor.TUI/tests/test_screenshots.py`

**Interfaces:**
- Produces:
  - `quaternions_to_yaw(rx, ry, rz, rw) -> float` — corrected formula
  - `class ScreenshotWatcher` — `__init__(path: Path, on_screenshot: Callable[[str], None])`, `start() -> None`, `stop() -> None`
  - All existing exports (`PlayerPosition`, `parse_screenshot`, `get_screenshots_path`) unchanged in signature

- [ ] **Step 1: Write failing tests for the quaternion fix and ScreenshotWatcher**

Create `TarkovMonitor.TUI/tests/test_screenshots.py`:

```python
"""Tests for screenshots.py — quaternion formula and ScreenshotWatcher."""
from __future__ import annotations

import asyncio
import math
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from tarkovmonitor_tui.screenshots import (
    ScreenshotWatcher,
    PlayerPosition,
    parse_screenshot,
    quaternions_to_yaw,
)


# --- quaternions_to_yaw ---

def test_quaternions_to_yaw_identity():
    # Identity quaternion (no rotation) → 0 degrees
    assert quaternions_to_yaw(0.0, 0.0, 0.0, 1.0) == pytest.approx(0.0, abs=1e-4)


def test_quaternions_to_yaw_90_degrees():
    # 90° yaw around Y: quaternion is (0, sin(45°), 0, cos(45°)) = (0, ~0.7071, 0, ~0.7071)
    s = math.sqrt(2) / 2
    result = quaternions_to_yaw(0.0, s, 0.0, s)
    assert result == pytest.approx(90.0, abs=0.01)


def test_quaternions_to_yaw_180_degrees():
    # 180° yaw: (0, 1, 0, 0)
    result = quaternions_to_yaw(0.0, 1.0, 0.0, 0.0)
    assert abs(result) == pytest.approx(180.0, abs=0.01)


def test_quaternions_to_yaw_negative_90():
    s = math.sqrt(2) / 2
    result = quaternions_to_yaw(0.0, -s, 0.0, s)
    assert result == pytest.approx(-90.0, abs=0.01)


# --- parse_screenshot ---

def test_parse_screenshot_returns_position():
    fname = "2024-01-15[12-30]_1.23, 45.67, -89.01_0.1, 0.7071, 0.0, 0.7071 (1).png"
    result = parse_screenshot(fname, current_map="bigmap")
    assert result is not None
    assert result.x == pytest.approx(1.23, abs=0.01)
    assert result.y == pytest.approx(45.67, abs=0.01)
    assert result.z == pytest.approx(-89.01, abs=0.01)
    assert result.map_name == "bigmap"
    assert result.filename == fname


def test_parse_screenshot_non_matching_returns_none():
    assert parse_screenshot("random_file.png") is None


def test_parse_screenshot_empty_map_defaults_empty():
    fname = "2024-01-15[12-30]_1.23, 45.67, -89.01_0.1, 0.7071, 0.0, 0.7071 (1).png"
    result = parse_screenshot(fname)
    assert result is not None
    assert result.map_name == ""


# --- ScreenshotWatcher ---

async def test_screenshot_watcher_calls_callback_for_new_png(tmp_path):
    received: list[str] = []
    watcher = ScreenshotWatcher(tmp_path, lambda fn: received.append(fn))

    from watchfiles import Change
    fake_changes = [{(Change.added, str(tmp_path / "shot.png"))}]

    async def fake_awatch(*args, **kwargs):
        for batch in fake_changes:
            yield batch

    with patch("tarkovmonitor_tui.screenshots.awatch", fake_awatch):
        watcher.start()
        await asyncio.sleep(0.05)
        watcher.stop()

    assert received == ["shot.png"]


async def test_screenshot_watcher_ignores_non_png(tmp_path):
    received: list[str] = []
    watcher = ScreenshotWatcher(tmp_path, lambda fn: received.append(fn))

    from watchfiles import Change
    fake_changes = [{(Change.added, str(tmp_path / "notes.txt"))}]

    async def fake_awatch(*args, **kwargs):
        for batch in fake_changes:
            yield batch

    with patch("tarkovmonitor_tui.screenshots.awatch", lambda *a, **k: fake_awatch()):
        watcher.start()
        await asyncio.sleep(0.05)
        watcher.stop()

    assert received == []


async def test_screenshot_watcher_ignores_modified_and_deleted(tmp_path):
    received: list[str] = []
    watcher = ScreenshotWatcher(tmp_path, lambda fn: received.append(fn))

    from watchfiles import Change
    fake_changes = [
        {(Change.modified, str(tmp_path / "shot.png"))},
        {(Change.deleted, str(tmp_path / "shot2.png"))},
    ]

    async def fake_awatch(*args, **kwargs):
        for batch in fake_changes:
            yield batch

    with patch("tarkovmonitor_tui.screenshots.awatch", lambda *a, **k: fake_awatch()):
        watcher.start()
        await asyncio.sleep(0.05)
        watcher.stop()

    assert received == []
```

- [ ] **Step 2: Run tests to verify they fail**

```
cd TarkovMonitor.TUI
.venv\Scripts\python -m pytest tests/test_screenshots.py -v
```

Expected: Multiple failures including `ImportError: cannot import name 'ScreenshotWatcher'` and failing quaternion assertions.

- [ ] **Step 3: Fix `quaternions_to_yaw` and add `ScreenshotWatcher`**

Replace the contents of `TarkovMonitor.TUI/tarkovmonitor_tui/screenshots.py` with:

```python
"""Screenshot watcher — parses EFT screenshot filenames for player position."""

from __future__ import annotations

import asyncio
import logging
import math
import os
import re
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from watchfiles import Change, awatch

log = logging.getLogger(__name__)

SCREENSHOT_PATTERN = re.compile(
    r"\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_?(?P<position>.+) \(\d\)\.png"
)
POSITION_PATTERN = re.compile(
    r"(?P<x>-?[\d]+\.[\d]{2}), (?P<y>-?[\d]+\.[\d]{2}), (?P<z>-?[\d]+\.[\d]{2})_?"
    r"(?P<rx>-?[\d.]{1}\.[\d]{1,5}), (?P<ry>-?[\d.]{1}\.[\d]{1,5}), "
    r"(?P<rz>-?[\d.]{1}\.[\d]{1,5}), (?P<rw>-?[\d.]{1}\.[\d]{1,5})"
)


@dataclass
class PlayerPosition:
    x: float
    y: float
    z: float
    rotation: float
    filename: str
    map_name: str


def quaternions_to_yaw(rx: float, ry: float, rz: float, rw: float) -> float:
    """Convert EFT quaternion to yaw degrees (matches C# GameWatcher.QuarternionsToYaw).

    C# calls QuarternionsToYaw(rx, ry, rz, rw) with parameter names (x, z, y, w),
    so ry→z, rz→y in the formula: siny = 2*(w*z + x*y) = 2*(rw*ry + rx*rz).
    """
    siny_cosp = 2.0 * (rw * ry + rx * rz)
    cosy_cosp = 1.0 - 2.0 * (rz * rz + ry * ry)
    return math.atan2(siny_cosp, cosy_cosp) * (180.0 / math.pi)


def parse_screenshot(filename: str, current_map: str = "") -> PlayerPosition | None:
    """Parse an EFT screenshot filename and extract position + rotation."""
    match = SCREENSHOT_PATTERN.match(filename)
    if not match:
        return None

    pos_match = POSITION_PATTERN.search(match.group("position"))
    if not pos_match:
        return None

    x = float(pos_match.group("x"))
    y = float(pos_match.group("y"))
    z = float(pos_match.group("z"))
    rx = float(pos_match.group("rx"))
    ry = float(pos_match.group("ry"))
    rz = float(pos_match.group("rz"))
    rw = float(pos_match.group("rw"))

    return PlayerPosition(
        x=x, y=y, z=z,
        rotation=quaternions_to_yaw(rx, ry, rz, rw),
        filename=filename,
        map_name=current_map,
    )


def get_screenshots_path() -> Path:
    """Return the default EFT screenshots path."""
    docs = Path(os.path.expanduser("~/Documents"))
    return docs / "Escape From Tarkov" / "Screenshots"


class ScreenshotWatcher:
    """Watches a directory for new EFT screenshot PNG files using watchfiles."""

    def __init__(self, path: Path, on_screenshot: Callable[[str], None]) -> None:
        self._path = path
        self._on_screenshot = on_screenshot
        self._task: asyncio.Task | None = None

    def start(self) -> None:
        if self._task is not None:
            self._task.cancel()
        self._task = asyncio.create_task(self._watch())

    def stop(self) -> None:
        if self._task is not None:
            self._task.cancel()
            self._task = None

    async def _watch(self) -> None:
        try:
            async for changes in awatch(str(self._path)):
                for change_type, changed_path in changes:
                    if change_type == Change.added and changed_path.lower().endswith(".png"):
                        self._on_screenshot(Path(changed_path).name)
        except asyncio.CancelledError:
            pass
        except Exception:
            log.exception("Screenshot watcher error")
```

- [ ] **Step 4: Run tests to verify they pass**

```
cd TarkovMonitor.TUI
.venv\Scripts\python -m pytest tests/test_screenshots.py -v
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add TarkovMonitor.TUI/tarkovmonitor_tui/screenshots.py TarkovMonitor.TUI/tests/test_screenshots.py
git commit -m "fix(tui): fix quaternion yaw formula and add ScreenshotWatcher"
```

---

### Task 2: TarkovSocketClient — WebSocket relay to tarkov.dev browser remote

New module `socket_client.py` that manages one persistent WebSocket connection per remote ID and broadcasts player position and map-navigate commands. Reconnects automatically on send failure. Add `websockets>=12.0` to deps.

**Files:**
- Create: `TarkovMonitor.TUI/tarkovmonitor_tui/socket_client.py`
- Create: `TarkovMonitor.TUI/tests/test_socket_client.py`
- Modify: `TarkovMonitor.TUI/pyproject.toml` (add `websockets>=12.0`)

**Interfaces:**
- Consumes: `PlayerPosition` from `screenshots.py` (x, y, z, rotation fields)
- Produces:
  - `class TarkovSocketClient` — `__init__(remote_ids: list[str])`, `async send_player_position(position, map_normalized_name, view_radius=None) -> None`, `async navigate_to_map(map_normalized_name) -> None`, `async close() -> None`

- [ ] **Step 1: Write failing tests**

Create `TarkovMonitor.TUI/tests/test_socket_client.py`:

```python
"""Tests for socket_client.py."""
from __future__ import annotations

import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from tarkovmonitor_tui.screenshots import PlayerPosition
from tarkovmonitor_tui.socket_client import TarkovSocketClient

WS_URL = "wss://socket.tarkov.dev"


def _make_ws(sent: list[str]) -> MagicMock:
    ws = MagicMock()
    ws.closed = False
    ws.send = AsyncMock(side_effect=lambda m: sent.append(m))
    ws.close = AsyncMock()
    return ws


async def test_send_player_position_broadcasts_to_all_remotes():
    sent: list[str] = []
    ws = _make_ws(sent)

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["remote1", "remote2"])
        pos = PlayerPosition(x=100.0, y=5.5, z=-200.0, rotation=45.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "woods")
        await client.close()

    assert len(sent) == 2
    payload0 = json.loads(sent[0])
    assert payload0["type"] == "command"
    assert payload0["sessionID"] == "remote1"
    assert payload0["data"]["type"] == "playerPosition"
    assert payload0["data"]["map"] == "woods"
    assert payload0["data"]["position"] == {"x": 100.0, "y": 5.5, "z": -200.0}
    assert payload0["data"]["rotation"] == pytest.approx(45.0)
    assert "viewRadius" not in payload0["data"]


async def test_send_player_position_includes_view_radius_when_provided():
    sent: list[str] = []
    ws = _make_ws(sent)

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["r1"])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "customs", view_radius=300)
        await client.close()

    payload = json.loads(sent[0])
    assert payload["data"]["viewRadius"] == 300


async def test_navigate_to_map_sends_map_command():
    sent: list[str] = []
    ws = _make_ws(sent)

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["r1"])
        await client.navigate_to_map("interchange")
        await client.close()

    payload = json.loads(sent[0])
    assert payload["data"]["type"] == "map"
    assert payload["data"]["value"] == "interchange"


async def test_ws_url_includes_session_suffix():
    connect_calls: list[str] = []
    ws = _make_ws([])

    async def fake_connect(url, **kwargs):
        connect_calls.append(url)
        return ws

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", fake_connect):
        client = TarkovSocketClient(["myremote"])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "factory")
        await client.close()

    assert connect_calls[0] == f"{WS_URL}?sessionid=myremote-tm"


async def test_send_failure_clears_connection_and_does_not_raise():
    ws = MagicMock()
    ws.closed = False
    ws.send = AsyncMock(side_effect=Exception("connection reset"))
    ws.close = AsyncMock()

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["r1"])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        # must not raise
        await client.send_player_position(pos, "woods")
        await client.close()


async def test_empty_remote_ids_sends_nothing():
    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock) as mock_connect:
        client = TarkovSocketClient([])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "woods")
        await client.close()
    mock_connect.assert_not_called()
```

- [ ] **Step 2: Run tests to verify they fail**

```
cd TarkovMonitor.TUI
.venv\Scripts\python -m pytest tests/test_socket_client.py -v
```

Expected: `ImportError: No module named 'tarkovmonitor_tui.socket_client'`

- [ ] **Step 3: Add `websockets` to pyproject.toml**

In `TarkovMonitor.TUI/pyproject.toml`, add `"websockets>=12.0",` to the `dependencies` list:

```toml
dependencies = [
    "textual>=3.0.0",
    "grpcio>=1.70.0",
    "grpcio-tools>=1.70.0",
    "httpx>=0.28.0",
    "watchfiles>=1.0.0",
    "websockets>=12.0",
]
```

Install it:

```
cd TarkovMonitor.TUI
.venv\Scripts\pip install websockets>=12.0
```

- [ ] **Step 4: Create `socket_client.py`**

Create `TarkovMonitor.TUI/tarkovmonitor_tui/socket_client.py`:

```python
"""WebSocket client for tarkov.dev browser remote — relays player position and map commands."""

from __future__ import annotations

import json
import logging
from typing import Any

import websockets

from .screenshots import PlayerPosition

log = logging.getLogger(__name__)

WS_BASE = "wss://socket.tarkov.dev"


class TarkovSocketClient:
    """Maintains one persistent WebSocket connection per remote ID."""

    def __init__(self, remote_ids: list[str]) -> None:
        self._remote_ids = remote_ids
        self._connections: dict[str, Any] = {}

    async def send_player_position(
        self,
        position: PlayerPosition,
        map_normalized_name: str,
        view_radius: int | None = None,
    ) -> None:
        data: dict[str, Any] = {
            "type": "playerPosition",
            "map": map_normalized_name,
            "position": {"x": position.x, "y": position.y, "z": position.z},
            "rotation": position.rotation,
        }
        if view_radius is not None:
            data["viewRadius"] = view_radius
        await self._broadcast(data)

    async def navigate_to_map(self, map_normalized_name: str) -> None:
        await self._broadcast({"type": "map", "value": map_normalized_name})

    async def close(self) -> None:
        for ws in list(self._connections.values()):
            try:
                await ws.close()
            except Exception:
                pass
        self._connections.clear()

    async def _get_ws(self, remote_id: str) -> Any:
        ws = self._connections.get(remote_id)
        if ws is None or ws.closed:
            url = f"{WS_BASE}?sessionid={remote_id}-tm"
            ws = await websockets.connect(url)
            self._connections[remote_id] = ws
        return ws

    async def _broadcast(self, data: dict) -> None:
        for remote_id in self._remote_ids:
            try:
                ws = await self._get_ws(remote_id)
                payload = json.dumps({
                    "type": "command",
                    "sessionID": remote_id,
                    "data": data,
                })
                await ws.send(payload)
            except Exception:
                log.debug("WebSocket send failed for remote %s", remote_id, exc_info=True)
                self._connections.pop(remote_id, None)
```

- [ ] **Step 5: Run tests to verify they pass**

```
cd TarkovMonitor.TUI
.venv\Scripts\python -m pytest tests/test_socket_client.py -v
```

Expected: All 6 tests pass.

- [ ] **Step 6: Run full suite to confirm no regressions**

```
cd TarkovMonitor.TUI
.venv\Scripts\python -m pytest tests/ -v
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add TarkovMonitor.TUI/tarkovmonitor_tui/socket_client.py TarkovMonitor.TUI/tests/test_socket_client.py TarkovMonitor.TUI/pyproject.toml
git commit -m "feat(tui): add TarkovSocketClient for tarkov.dev browser remote WebSocket relay"
```

---

### Task 3: Settings UI and app.py integration

Wire `ScreenshotWatcher` and `TarkovSocketClient` into `app.py`. Add four new settings fields. On each new screenshot: parse → resolve map → relay to socket client. Restart watcher/client on settings save.

**Files:**
- Modify: `TarkovMonitor.TUI/tarkovmonitor_tui/app.py`

**Interfaces:**
- Consumes from Task 1: `ScreenshotWatcher(path, callback)`, `parse_screenshot(filename, current_map) -> PlayerPosition | None`
- Consumes from Task 2: `TarkovSocketClient(remote_ids)`, `.send_player_position(pos, normalized_name, view_radius=None)`, `.navigate_to_map(normalized_name)`
- Consumes from existing tarkov_dev: `self._tarkov_dev.maps` — each map has `.name_id: str` and `.normalized_name: str`

**Key logic — when a new screenshot arrives:**
```
1. Parse filename → PlayerPosition (or skip if None)
2. Look up TarkovMap by map.name_id == self._current_raid_map (or settings["custom_map"])
   → get map.normalized_name
3. Determine view_radius:
   if settings["auto_zoom"] and normalized_name != self._last_sent_map:
       vr = int(settings["view_radius"])  # default 500
   else:
       vr = None
4. await self._socket_client.send_player_position(pos, normalized_name, vr)
5. if settings["navigate_map_on_position"]:
       await self._socket_client.navigate_to_map(normalized_name)
6. self._last_sent_map = normalized_name
7. Log: "Position on {map_display_name}: ({x:.1f}, {y:.1f}, {z:.1f}) → sent to {N} remote(s)"
```

**New instance variables (add in `__init__` after existing vars):**
```python
self._screenshot_watcher: ScreenshotWatcher | None = None
self._socket_client: TarkovSocketClient | None = None
self._last_sent_map: str = ""
```

**New settings fields** — add after the existing Screenshots Path row (before the Save button), using the exact same `Horizontal(classes="setting-row")` pattern:
```
remote_ids        → Input, id="input-remote-ids", placeholder="Comma-separated remote IDs"
auto_zoom         → Checkbox, id="check-auto-zoom"
view_radius       → Input, id="input-view-radius", default "500"
navigate_map_on_position → Checkbox, id="check-navigate-map"
```

- [ ] **Step 1: Update imports in app.py**

At the top of `app.py`, add imports for the two new modules. Find the existing import block:

```python
from .screenshots import get_screenshots_path, parse_screenshot
```

Replace with:

```python
from .screenshots import ScreenshotWatcher, get_screenshots_path, parse_screenshot
from .socket_client import TarkovSocketClient
```

- [ ] **Step 2: Add new instance variables in `__init__`**

Find the `__init__` method. After the line that initializes `self._tt_client`, add:

```python
        self._screenshot_watcher: ScreenshotWatcher | None = None
        self._socket_client: TarkovSocketClient | None = None
        self._last_sent_map: str = ""
```

- [ ] **Step 3: Add four new settings fields in `compose()`**

In the `compose()` method, find the Screenshots Path row (ends with `id="input-screenshots-path"`). After its closing `Horizontal` block and before `yield Button("Save & Push to Service", ...)`, add:

```python
                    with Horizontal(classes="setting-row"):
                        yield Label("Remote IDs:", classes="setting-label")
                        yield Input(
                            value=self._settings.get("remote_ids", ""),
                            placeholder="Comma-separated tarkov.dev remote IDs",
                            id="input-remote-ids",
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("Auto-zoom on map change:", classes="setting-label")
                        yield Checkbox(
                            value=bool(self._settings.get("auto_zoom", False)),
                            id="check-auto-zoom",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("View radius (m):", classes="setting-label")
                        yield Input(
                            value=str(self._settings.get("view_radius", 500)),
                            placeholder="50–5000",
                            id="input-view-radius",
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("Navigate map on position:", classes="setting-label")
                        yield Checkbox(
                            value=bool(self._settings.get("navigate_map_on_position", False)),
                            id="check-navigate-map",
                        )
```

- [ ] **Step 4: Start watcher and socket client in `on_mount()`**

Find `on_mount()`. After the existing `self.refresh_tarkov_dev_periodically()` line, add:

```python
        self._restart_screenshot_watcher()
        self._restart_socket_client()
```

- [ ] **Step 5: Add `_restart_screenshot_watcher` and `_restart_socket_client` helper methods**

Add these two methods after `_push_config()`:

```python
    def _restart_screenshot_watcher(self) -> None:
        if self._screenshot_watcher is not None:
            self._screenshot_watcher.stop()
            self._screenshot_watcher = None
        path_str = self._settings.get("screenshots_path", str(get_screenshots_path()))
        path = Path(path_str)
        if path.exists():
            self._screenshot_watcher = ScreenshotWatcher(path, self._on_new_screenshot)
            self._screenshot_watcher.start()
        else:
            log.debug("Screenshots path does not exist, watcher not started: %s", path)

    def _restart_socket_client(self) -> None:
        if self._socket_client is not None:
            asyncio.create_task(self._socket_client.close())
            self._socket_client = None
        raw = self._settings.get("remote_ids", "")
        ids = [r.strip() for r in raw.replace(";", ",").split(",") if r.strip()]
        if ids:
            self._socket_client = TarkovSocketClient(ids)
```

- [ ] **Step 6: Add `_on_new_screenshot` handler**

Add this method after `_restart_socket_client`:

```python
    def _on_new_screenshot(self, filename: str) -> None:
        self._tarkov_dev.record_activity()
        map_name_id = self._current_raid_map or self._settings.get("custom_map", "")
        pos = parse_screenshot(filename, current_map=map_name_id)
        if pos is None:
            return

        tarkov_map = self._tarkov_dev.find_map(map_name_id)
        normalized = tarkov_map.normalized_name if tarkov_map else map_name_id
        display = tarkov_map.name if tarkov_map else (map_name_id or "Unknown")

        self._log_message(
            f"Position on {display}: ({pos.x:.1f}, {pos.y:.1f}, {pos.z:.1f})",
            "position",
        )

        if self._socket_client is None:
            return

        view_radius: int | None = None
        if self._settings.get("auto_zoom") and normalized != self._last_sent_map:
            try:
                view_radius = max(50, min(5000, int(self._settings.get("view_radius", 500))))
            except (ValueError, TypeError):
                view_radius = 500

        async def _relay() -> None:
            await self._socket_client.send_player_position(pos, normalized, view_radius)
            if self._settings.get("navigate_map_on_position"):
                await self._socket_client.navigate_to_map(normalized)

        asyncio.create_task(_relay())
        self._last_sent_map = normalized
        n = len(self._socket_client._remote_ids)
        self._log_message(f"  → relayed to {n} remote(s)", "position")
```

- [ ] **Step 7: Update `save_settings` to persist new fields and restart watcher/client**

In the `save_settings` method, find the existing `self._settings["submit_queue_time"]` line. After it and before `save_settings(self._settings)`, insert the four new fields:

```python
        self._settings["remote_ids"] = self.query_one("#input-remote-ids", Input).value
        self._settings["auto_zoom"] = self.query_one("#check-auto-zoom", Checkbox).value
        self._settings["navigate_map_on_position"] = self.query_one("#check-navigate-map", Checkbox).value
        try:
            self._settings["view_radius"] = max(50, min(5000, int(self.query_one("#input-view-radius", Input).value)))
        except (ValueError, TypeError):
            self._settings["view_radius"] = 500
```

`screenshots_path` is already saved by an existing line in `save_settings` — do not add it again.

Then after `self._log_message("Settings saved", "system")`, add:

```python
        self._restart_screenshot_watcher()
        self._restart_socket_client()
```

- [ ] **Step 8: Run the full test suite**

```
cd TarkovMonitor.TUI
.venv\Scripts\python -m pytest tests/ -v
```

Expected: All tests pass. No new tests needed for app.py — the logic is covered by the unit tests in Tasks 1 and 2, and the Textual app requires a running event loop that makes isolated testing complex.

- [ ] **Step 9: Manual smoke test** *(describe — do not run automatically)*

Start the TUI (`run.bat` or `.venv\Scripts\python -m tarkovmonitor_tui.app`). Open Settings tab and confirm:
- "Remote IDs" input field is present
- "Auto-zoom on map change" checkbox is present
- "View radius (m)" input shows "500"
- "Navigate map on position" checkbox is present

- [ ] **Step 10: Commit**

```bash
git add TarkovMonitor.TUI/tarkovmonitor_tui/app.py
git commit -m "feat(tui): wire screenshot watcher and tarkov.dev socket relay into app"
```
