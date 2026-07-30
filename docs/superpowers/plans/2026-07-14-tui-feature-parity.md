# TarkovMonitor.TUI Feature Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `TarkovMonitor.TUI` to feature parity with the .NET GUI's tarkov.dev integration — fixing broken goons detection, adding traders/hideout/player data, scav cooldown tracking, air filter alerts, restartable task reminders, goons reporting, queue time submission, game mode switching, auto-refresh, wiki hyperlinks, and TarkovTracker profile display.

**Architecture:** All new HTTP clients follow the existing `TarkovDevClient` pattern (async `httpx.AsyncClient`, instantiated in `__init__`, closed in `close()`). New game logic (cooldown math) lives in a dedicated `cooldown.py` module. Manager API and TarkovTracker clients are standalone modules that `app.py` instantiates and owns. App wiring changes are additive — existing event handlers are extended, not replaced.

**Tech Stack:** Python 3.11+, Textual 3+, httpx 0.28+, pytest 8+, pytest-asyncio 0.24+, pytest-httpx 0.30+

## Global Constraints

- Python `>=3.11`; use `from __future__ import annotations` in all new modules
- All HTTP clients use `httpx.AsyncClient`; close them in `app.py`'s `action_quit`
- tarkov.dev base: `https://json.tarkov.dev`; manager base: `https://manager.tarkov.dev/api`; players base: `https://players.tarkov.dev`
- tarkov.dev game modes: `"regular"` (default) and `"pve"` (when `profileType == "PVE"`)
- Fence trader's `normalizedName` is `"fence"`
- Goon mob names (exact): `"bossKnight"`, `"followerBigPipe"`, `"followerBirdEye"` — checked on both `spawn["mob"]` and each `escort["mob"]`
- Hideout cooldown bonus `type` field (exact string): `"ScavCooldownTimer"`
- Scav cooldown formula: `round(base × karma_bonus − base × karma_bonus × hideout_bonus)` where `hideout_bonus = sum(abs(b.value) for built ScavCooldownTimer bonuses)`
- All tests: `asyncio_mode = "auto"` (set in `pyproject.toml`)
- Test file per module; no monkeypatching — use `pytest-httpx` fixtures for all HTTP mocks
- No new top-level `requirements.txt` changes — dependencies go in `pyproject.toml` only
- Rich markup for log messages; use `[link=url]text[/link]` for clickable wiki links (not raw OSC 8)
- Do not add `TarkovTracker.fetchProgress` polling — only fetch on `InitialReadComplete` event

---

## File Map

| File | Status | Responsibility |
|------|--------|---------------|
| `tarkovmonitor_tui/tarkov_dev.py` | Modify | Data model, all tarkov.dev fetches |
| `tarkovmonitor_tui/cooldown.py` | Create | Scav cooldown math |
| `tarkovmonitor_tui/manager_api.py` | Create | Queue time + goons POST |
| `tarkovmonitor_tui/tarkov_tracker.py` | Create | TarkovTracker progress GET |
| `tarkovmonitor_tui/app.py` | Modify | UI wiring for all new features |
| `pyproject.toml` | Modify | Add dev deps + pytest config |
| `tests/__init__.py` | Create | Empty package marker |
| `tests/test_tarkov_dev.py` | Create | Tests for tarkov_dev.py |
| `tests/test_cooldown.py` | Create | Tests for cooldown.py |
| `tests/test_manager_api.py` | Create | Tests for manager_api.py |
| `tests/test_tarkov_tracker.py` | Create | Tests for tarkov_tracker.py |

---

### Task 1: Expand data model, fix has_goons, add test scaffolding

**Files:**
- Modify: `tarkovmonitor_tui/tarkov_dev.py`
- Modify: `pyproject.toml`
- Create: `tests/__init__.py`
- Create: `tests/test_tarkov_dev.py`

**Interfaces:**
- Produces:
  - `TarkovMap(id, name, name_id, normalized_name, has_goons)` — adds `normalized_name: str`
  - `TarkovTask(id, name, normalized_name, wiki_link, restartable, fail_if_complete)` — adds `fail_if_complete: list[str]`
  - `TraderReputationLevel(minimum_reputation: float, scav_cooldown_modifier: float)`
  - `TarkovTrader(id, name, normalized_name, reputation_levels: list[TraderReputationLevel])`
  - `HideoutStationBonus(type: str, value: float)`
  - `HideoutStationLevel(id: str, level: int, bonuses: list[HideoutStationBonus])`
  - `TarkovHideoutStation(id, name, normalized_name, levels: list[HideoutStationLevel])`
  - `PlayerLevel(level: int, exp: int)`
  - `_has_goons(bosses: list) -> bool` — module-level private function

- [ ] **Step 1: Add dev dependencies and pytest config to pyproject.toml**

Open `pyproject.toml` and add after the `[project]` block:

```toml
[project.optional-dependencies]
dev = [
    "pytest>=8.0.0",
    "pytest-asyncio>=0.24.0",
    "pytest-httpx>=0.30.0",
]

[tool.pytest.ini_options]
asyncio_mode = "auto"
testpaths = ["tests"]
```

Full resulting `pyproject.toml`:
```toml
[project]
name = "tarkovmonitor-tui"
version = "0.1.0"
description = "Python TUI client for TarkovMonitor.Service — cross-language gRPC showcase"
requires-python = ">=3.11"
dependencies = [
    "textual>=3.0.0",
    "grpcio>=1.70.0",
    "grpcio-tools>=1.70.0",
    "httpx>=0.28.0",
    "watchfiles>=1.0.0",
]

[project.scripts]
tarkovmonitor-tui = "tarkovmonitor_tui.app:main"

[project.optional-dependencies]
dev = [
    "pytest>=8.0.0",
    "pytest-asyncio>=0.24.0",
    "pytest-httpx>=0.30.0",
]

[tool.pytest.ini_options]
asyncio_mode = "auto"
testpaths = ["tests"]
```

- [ ] **Step 2: Install dev dependencies**

```
pip install -e ".[dev]"
```

- [ ] **Step 3: Create tests/__init__.py**

Create `tests/__init__.py` as an empty file.

- [ ] **Step 4: Write failing tests for _has_goons**

Create `tests/test_tarkov_dev.py`:

```python
"""Tests for tarkov_dev data model and parsing."""

from __future__ import annotations

import pytest
from tarkovmonitor_tui.tarkov_dev import (
    TarkovDevClient,
    TarkovHideoutStation,
    TarkovMap,
    TarkovTask,
    TarkovTrader,
    PlayerLevel,
    _has_goons,
)


# --- _has_goons ---

def test_has_goons_true_via_spawn_mob():
    bosses = [{"mob": "bossKnight", "escorts": []}]
    assert _has_goons(bosses) is True


def test_has_goons_true_via_escort_mob():
    bosses = [{"mob": "bossSanitar", "escorts": [{"mob": "followerBigPipe"}]}]
    assert _has_goons(bosses) is True


def test_has_goons_false_no_goon_mobs():
    bosses = [
        {"mob": "bossShturman", "escorts": [{"mob": "followerShturman"}]},
        {"mob": "bossSanitar", "escorts": []},
    ]
    assert _has_goons(bosses) is False


def test_has_goons_false_old_name_rejected():
    bosses = [{"mob": "death-knight", "escorts": []}]
    assert _has_goons(bosses) is False


def test_has_goons_empty():
    assert _has_goons([]) is False


def test_has_goons_all_three():
    bosses = [
        {
            "mob": "bossKnight",
            "escorts": [
                {"mob": "followerBigPipe"},
                {"mob": "followerBirdEye"},
            ],
        }
    ]
    assert _has_goons(bosses) is True


# --- TarkovMap ---

def test_tarkov_map_has_normalized_name():
    m = TarkovMap(id="x", name="Customs", name_id="bigmap", normalized_name="customs")
    assert m.normalized_name == "customs"
    assert m.has_goons is False


# --- TarkovTask ---

def test_tarkov_task_has_fail_if_complete():
    t = TarkovTask(
        id="t1", name="Task 1", fail_if_complete=["t2", "t3"]
    )
    assert t.fail_if_complete == ["t2", "t3"]


def test_tarkov_task_fail_if_complete_defaults_empty():
    t = TarkovTask(id="t1", name="Task 1")
    assert t.fail_if_complete == []


# --- _fetch_maps parsing ---

async def test_fetch_maps_parses_goons_and_normalized_name(httpx_mock):
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/maps",
        json={
            "data": {
                "maps": [
                    {
                        "id": "abc",
                        "name": "Customs",
                        "normalizedName": "customs",
                        "nameId": "bigmap",
                        "bosses": [
                            {
                                "mob": "bossKnight",
                                "escorts": [
                                    {"mob": "followerBigPipe"},
                                    {"mob": "followerBirdEye"},
                                ],
                            }
                        ],
                    },
                    {
                        "id": "def",
                        "name": "Woods",
                        "normalizedName": "woods",
                        "nameId": "woods",
                        "bosses": [
                            {"mob": "bossShturman", "escorts": []}
                        ],
                    },
                ]
            }
        },
    )
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/maps_en",
        json={},
    )
    client = TarkovDevClient()
    await client._fetch_maps()
    await client.close()

    assert len(client.maps) == 2
    customs = client.maps[0]
    assert customs.name == "Customs"
    assert customs.normalized_name == "customs"
    assert customs.name_id == "bigmap"
    assert customs.has_goons is True
    woods = client.maps[1]
    assert woods.has_goons is False


# --- _fetch_tasks parsing ---

async def test_fetch_tasks_parses_fail_if_complete(httpx_mock):
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/tasks",
        json={
            "data": {
                "tasks": [
                    {
                        "id": "task-a",
                        "name": "Task A",
                        "normalizedName": "task-a",
                        "wikiLink": "https://wiki.example.com/TaskA",
                        "restartable": True,
                        "failConditions": [],
                    },
                    {
                        "id": "task-b",
                        "name": "Task B",
                        "normalizedName": "task-b",
                        "wikiLink": "",
                        "restartable": False,
                        "failConditions": [
                            {
                                "task": {"id": "task-a"},
                                "status": ["complete"],
                            }
                        ],
                    },
                ]
            }
        },
    )
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/tasks_en",
        json={},
    )
    client = TarkovDevClient()
    await client._fetch_tasks()
    await client.close()

    task_a = client.tasks[0]
    assert task_a.restartable is True
    assert task_a.wiki_link == "https://wiki.example.com/TaskA"
    assert task_a.fail_if_complete == []

    task_b = client.tasks[1]
    assert task_b.restartable is False
    assert task_b.fail_if_complete == ["task-a"]
```

- [ ] **Step 5: Run tests to verify they fail**

```
pytest tests/test_tarkov_dev.py -v
```

Expected: multiple failures — `_has_goons` not imported, `TarkovMap` missing `normalized_name`, `TarkovTask` missing `fail_if_complete`, `TarkovTrader`/`PlayerLevel` not defined.

- [ ] **Step 6: Implement changes in tarkov_dev.py**

Replace the full contents of `tarkovmonitor_tui/tarkov_dev.py`:

```python
"""tarkov.dev API integration — fetches maps, items, tasks, traders, and hideout data."""

from __future__ import annotations

import logging
import time
from dataclasses import dataclass, field

import httpx

log = logging.getLogger(__name__)

API_BASE = "https://json.tarkov.dev"
PLAYERS_BASE = "https://players.tarkov.dev"

GOON_MOBS = frozenset({"bossKnight", "followerBigPipe", "followerBirdEye"})


def _has_goons(bosses: list) -> bool:
    for spawn in bosses:
        if spawn.get("mob") in GOON_MOBS:
            return True
        for escort in spawn.get("escorts", []):
            if escort.get("mob") in GOON_MOBS:
                return True
    return False


@dataclass
class TarkovMap:
    id: str
    name: str
    name_id: str
    normalized_name: str = ""
    has_goons: bool = False


@dataclass
class TarkovItem:
    id: str
    name: str
    normalized_name: str = ""
    link: str = ""


@dataclass
class TarkovTask:
    id: str
    name: str
    normalized_name: str = ""
    wiki_link: str = ""
    restartable: bool = False
    fail_if_complete: list[str] = field(default_factory=list)


@dataclass
class TraderReputationLevel:
    minimum_reputation: float
    scav_cooldown_modifier: float


@dataclass
class TarkovTrader:
    id: str
    name: str
    normalized_name: str
    reputation_levels: list[TraderReputationLevel] = field(default_factory=list)


@dataclass
class HideoutStationBonus:
    type: str
    value: float


@dataclass
class HideoutStationLevel:
    id: str
    level: int
    bonuses: list[HideoutStationBonus] = field(default_factory=list)


@dataclass
class TarkovHideoutStation:
    id: str
    name: str
    normalized_name: str
    levels: list[HideoutStationLevel] = field(default_factory=list)


@dataclass
class PlayerLevel:
    level: int
    exp: int


class TarkovDevClient:
    """Fetches reference data from the tarkov.dev JSON API."""

    def __init__(self, game_mode: str = "regular", lang: str = "en") -> None:
        self.maps: list[TarkovMap] = []
        self.items: list[TarkovItem] = []
        self.tasks: list[TarkovTask] = []
        self.traders: list[TarkovTrader] = []
        self.hideout_stations: list[TarkovHideoutStation] = []
        self.player_levels: list[PlayerLevel] = []
        self.scav_cooldown_base_seconds: int = 1500
        self._game_mode = game_mode
        self._lang = lang
        self._last_fetched: float = 0.0
        self._last_activity: float = 0.0
        self._client = httpx.AsyncClient(
            base_url=API_BASE,
            timeout=15.0,
            headers={"User-Agent": "TarkovMonitor.TUI/0.1.0"},
        )
        self._players_client = httpx.AsyncClient(
            base_url=PLAYERS_BASE,
            timeout=15.0,
            headers={"User-Agent": "TarkovMonitor.TUI/0.1.0"},
        )

    async def _fetch_translations(self, endpoint: str) -> dict[str, str]:
        try:
            resp = await self._client.get(f"/{self._game_mode}/{endpoint}_{self._lang}")
            resp.raise_for_status()
            raw = resp.json()
            data = raw.get("data", raw)
            if isinstance(data, dict):
                return data
        except Exception:
            log.debug("No translations for %s", endpoint)
        return {}

    def _translate(self, value: str, translations: dict[str, str]) -> str:
        return translations.get(value, value)

    def record_activity(self) -> None:
        self._last_activity = time.monotonic()

    async def maybe_refresh(self) -> None:
        now = time.monotonic()
        if (now - self._last_fetched) > 1200 and (now - self._last_activity) < 300:
            await self.update()

    async def set_game_mode(self, mode: str) -> None:
        if mode != self._game_mode:
            self._game_mode = mode
            await self.update()

    async def update(self) -> None:
        try:
            await self._fetch_maps()
            await self._fetch_items()
            await self._fetch_tasks()
            await self._fetch_traders()
            await self._fetch_hideout()
            self._last_fetched = time.monotonic()
            log.info(
                "tarkov.dev: %d maps, %d items, %d tasks, %d traders, %d stations",
                len(self.maps),
                len(self.items),
                len(self.tasks),
                len(self.traders),
                len(self.hideout_stations),
            )
        except Exception:
            log.exception("Failed to fetch tarkov.dev data")

    async def _fetch_maps(self) -> None:
        resp = await self._client.get(f"/{self._game_mode}/maps")
        resp.raise_for_status()
        raw = resp.json()
        tr = await self._fetch_translations("maps")
        data = raw.get("data", raw)
        if isinstance(data, dict) and "maps" in data:
            items = data["maps"]
            if isinstance(items, dict):
                items = list(items.values())
        elif isinstance(data, list):
            items = data
        else:
            items = []
        self.maps = [
            TarkovMap(
                id=m.get("id", ""),
                name=self._translate(m.get("name", ""), tr),
                name_id=m.get("nameId", m.get("normalizedName", "")),
                normalized_name=m.get("normalizedName", ""),
                has_goons=_has_goons(m.get("bosses", [])),
            )
            for m in items
        ]

    async def _fetch_items(self) -> None:
        resp = await self._client.get(f"/{self._game_mode}/items")
        resp.raise_for_status()
        raw = resp.json()
        tr = await self._fetch_translations("items")
        data = raw.get("data", raw)
        if isinstance(data, dict):
            raw_items = data.get("items", [])
            settings = data.get("settings", {})
            raw_levels = data.get("playerLevels", [])
        elif isinstance(data, list):
            raw_items = data
            settings = {}
            raw_levels = []
        else:
            raw_items = []
            settings = {}
            raw_levels = []

        if isinstance(raw_items, dict):
            raw_items = list(raw_items.values())

        self.items = [
            TarkovItem(
                id=i.get("id", ""),
                name=self._translate(i.get("name", ""), tr),
                normalized_name=i.get("normalizedName", ""),
                link=i.get("link", ""),
            )
            for i in raw_items
        ]
        if settings.get("scavCooldownSeconds"):
            self.scav_cooldown_base_seconds = int(settings["scavCooldownSeconds"])
        self.player_levels = [
            PlayerLevel(level=pl.get("level", 1), exp=pl.get("exp", 0))
            for pl in raw_levels
        ]

    async def _fetch_tasks(self) -> None:
        resp = await self._client.get(f"/{self._game_mode}/tasks")
        resp.raise_for_status()
        raw = resp.json()
        tr = await self._fetch_translations("tasks")
        data = raw.get("data", raw)
        if isinstance(data, dict) and "tasks" in data:
            items = data["tasks"]
            if isinstance(items, dict):
                items = list(items.values())
        elif isinstance(data, list):
            items = data
        else:
            items = []
        self.tasks = [
            TarkovTask(
                id=t.get("id", ""),
                name=self._translate(t.get("name", ""), tr),
                normalized_name=t.get("normalizedName", ""),
                wiki_link=t.get("wikiLink", ""),
                restartable=bool(t.get("restartable", False)),
                fail_if_complete=[
                    c["task"]["id"]
                    for c in t.get("failConditions", [])
                    if "complete" in c.get("status", []) and c.get("task", {}).get("id")
                ],
            )
            for t in items
        ]

    async def _fetch_traders(self) -> None:
        resp = await self._client.get(f"/{self._game_mode}/traders")
        resp.raise_for_status()
        raw = resp.json()
        data = raw.get("data", raw)
        if isinstance(data, dict) and "traders" in data:
            traders_raw = data["traders"]
        elif isinstance(data, list):
            traders_raw = data
        else:
            traders_raw = []
        self.traders = [
            TarkovTrader(
                id=t.get("id", ""),
                name=t.get("name", ""),
                normalized_name=t.get("normalizedName", ""),
                reputation_levels=[
                    TraderReputationLevel(
                        minimum_reputation=float(r.get("minimumReputation", 0)),
                        scav_cooldown_modifier=float(r.get("scavCooldownModifier", 1.0)),
                    )
                    for r in t.get("reputationLevels", [])
                ],
            )
            for t in traders_raw
        ]

    async def _fetch_hideout(self) -> None:
        resp = await self._client.get(f"/{self._game_mode}/hideout")
        resp.raise_for_status()
        raw = resp.json()
        data = raw.get("data", raw)
        if isinstance(data, dict) and "stations" in data:
            stations_raw = data["stations"]
        elif isinstance(data, list):
            stations_raw = data
        else:
            stations_raw = []
        self.hideout_stations = [
            TarkovHideoutStation(
                id=s.get("id", ""),
                name=s.get("name", ""),
                normalized_name=s.get("normalizedName", ""),
                levels=[
                    HideoutStationLevel(
                        id=lv.get("id", ""),
                        level=int(lv.get("level", 0)),
                        bonuses=[
                            HideoutStationBonus(
                                type=b.get("type", ""),
                                value=float(b.get("value", 0)),
                            )
                            for b in lv.get("bonuses", [])
                        ],
                    )
                    for lv in s.get("levels", [])
                ],
            )
            for s in stations_raw
        ]

    async def fetch_player_name(self, account_id: str) -> str:
        try:
            resp = await self._players_client.get("/profile/index.json")
            resp.raise_for_status()
            data = resp.json()
            return data.get(account_id, account_id)
        except Exception:
            log.debug("Could not fetch player name for %s", account_id)
            return account_id

    def get_fence(self) -> TarkovTrader | None:
        return next((t for t in self.traders if t.normalized_name == "fence"), None)

    def find_player_level(self, xp: int) -> int:
        matching = [pl for pl in self.player_levels if pl.exp <= xp]
        return matching[-1].level if matching else 1

    def find_map(self, name_id: str) -> TarkovMap | None:
        return next((m for m in self.maps if m.name_id == name_id), None)

    def find_item(self, item_id: str) -> TarkovItem | None:
        return next((i for i in self.items if i.id == item_id), None)

    def find_task(self, task_id: str) -> TarkovTask | None:
        return next((t for t in self.tasks if t.id == task_id), None)

    async def close(self) -> None:
        await self._client.aclose()
        await self._players_client.aclose()
```

- [ ] **Step 7: Run tests to verify they pass**

```
pytest tests/test_tarkov_dev.py -v
```

Expected: all tests PASS.

- [ ] **Step 8: Commit**

```
git add tarkovmonitor_tui/tarkov_dev.py pyproject.toml tests/__init__.py tests/test_tarkov_dev.py
git commit -m "feat(tui): expand data model, fix has_goons, add test scaffolding"
```

---

### Task 2: Fetch traders, hideout, player names — extend tests

**Files:**
- Create: additional tests in `tests/test_tarkov_dev.py`

**Interfaces:**
- Consumes: `TarkovTrader`, `TraderReputationLevel`, `TarkovHideoutStation`, `HideoutStationLevel`, `HideoutStationBonus`, `PlayerLevel`, `TarkovDevClient._fetch_traders`, `TarkovDevClient._fetch_hideout`, `TarkovDevClient.fetch_player_name`, `TarkovDevClient.get_fence`, `TarkovDevClient.find_player_level`, `TarkovDevClient.maybe_refresh`
- Produces: verified coverage of all new fetch methods

- [ ] **Step 1: Write failing tests for traders, hideout, player names, and helpers**

Append to `tests/test_tarkov_dev.py`:

```python
# --- _fetch_traders ---

async def test_fetch_traders_parses_fence_reputation_levels(httpx_mock):
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/traders",
        json={
            "data": {
                "traders": [
                    {
                        "id": "fence-id",
                        "name": "Fence",
                        "normalizedName": "fence",
                        "reputationLevels": [
                            {"minimumReputation": -7.0, "scavCooldownModifier": 1.5},
                            {"minimumReputation": -0.02, "scavCooldownModifier": 1.0},
                            {"minimumReputation": 6.0, "scavCooldownModifier": 0.5},
                        ],
                    }
                ]
            }
        },
    )
    client = TarkovDevClient()
    await client._fetch_traders()
    await client.close()

    assert len(client.traders) == 1
    fence = client.traders[0]
    assert fence.normalized_name == "fence"
    assert len(fence.reputation_levels) == 3
    assert fence.reputation_levels[0].minimum_reputation == -7.0
    assert fence.reputation_levels[0].scav_cooldown_modifier == 1.5
    assert fence.reputation_levels[2].scav_cooldown_modifier == 0.5


# --- get_fence ---

async def test_get_fence_finds_fence_trader(httpx_mock):
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/traders",
        json={
            "data": {
                "traders": [
                    {"id": "1", "name": "Prapor", "normalizedName": "prapor", "reputationLevels": []},
                    {"id": "2", "name": "Fence", "normalizedName": "fence", "reputationLevels": []},
                ]
            }
        },
    )
    client = TarkovDevClient()
    await client._fetch_traders()
    await client.close()

    fence = client.get_fence()
    assert fence is not None
    assert fence.normalized_name == "fence"


def test_get_fence_returns_none_when_not_loaded():
    client = TarkovDevClient.__new__(TarkovDevClient)
    client.traders = []
    assert client.get_fence() is None


# --- _fetch_hideout ---

async def test_fetch_hideout_parses_stations_and_bonuses(httpx_mock):
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/hideout",
        json={
            "data": {
                "stations": [
                    {
                        "id": "station-1",
                        "name": "Air Filtering Unit",
                        "normalizedName": "air-filtering-unit",
                        "levels": [
                            {
                                "id": "level-1",
                                "level": 1,
                                "bonuses": [
                                    {"type": "ScavCooldownTimer", "value": -0.05}
                                ],
                            },
                            {
                                "id": "level-2",
                                "level": 2,
                                "bonuses": [
                                    {"type": "ScavCooldownTimer", "value": -0.1}
                                ],
                            },
                        ],
                    }
                ]
            }
        },
    )
    client = TarkovDevClient()
    await client._fetch_hideout()
    await client.close()

    assert len(client.hideout_stations) == 1
    station = client.hideout_stations[0]
    assert station.normalized_name == "air-filtering-unit"
    assert len(station.levels) == 2
    assert station.levels[0].id == "level-1"
    assert station.levels[0].bonuses[0].type == "ScavCooldownTimer"
    assert station.levels[0].bonuses[0].value == -0.05


# --- _fetch_items (scavCooldownSeconds + playerLevels) ---

async def test_fetch_items_parses_settings_and_player_levels(httpx_mock):
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/items",
        json={
            "data": {
                "items": [],
                "settings": {"scavCooldownSeconds": 2100},
                "playerLevels": [
                    {"level": 1, "exp": 0},
                    {"level": 2, "exp": 1000},
                    {"level": 3, "exp": 3000},
                ],
            }
        },
    )
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/items_en",
        json={},
    )
    client = TarkovDevClient()
    await client._fetch_items()
    await client.close()

    assert client.scav_cooldown_base_seconds == 2100
    assert len(client.player_levels) == 3
    assert client.player_levels[1].level == 2
    assert client.player_levels[1].exp == 1000


# --- find_player_level ---

def test_find_player_level_returns_correct_level():
    client = TarkovDevClient.__new__(TarkovDevClient)
    client.player_levels = [
        PlayerLevel(level=1, exp=0),
        PlayerLevel(level=2, exp=1000),
        PlayerLevel(level=3, exp=3000),
    ]
    assert client.find_player_level(0) == 1
    assert client.find_player_level(999) == 1
    assert client.find_player_level(1000) == 2
    assert client.find_player_level(2999) == 2
    assert client.find_player_level(3000) == 3
    assert client.find_player_level(99999) == 3


def test_find_player_level_empty_returns_1():
    client = TarkovDevClient.__new__(TarkovDevClient)
    client.player_levels = []
    assert client.find_player_level(5000) == 1


# --- fetch_player_name ---

async def test_fetch_player_name_resolves_account_id(httpx_mock):
    httpx_mock.add_response(
        url="https://players.tarkov.dev/profile/index.json",
        json={"acc-123": "TestPlayer", "acc-456": "OtherPlayer"},
    )
    client = TarkovDevClient()
    name = await client.fetch_player_name("acc-123")
    await client.close()
    assert name == "TestPlayer"


async def test_fetch_player_name_fallback_on_missing_id(httpx_mock):
    httpx_mock.add_response(
        url="https://players.tarkov.dev/profile/index.json",
        json={"acc-999": "SomeOne"},
    )
    client = TarkovDevClient()
    name = await client.fetch_player_name("acc-unknown")
    await client.close()
    assert name == "acc-unknown"


async def test_fetch_player_name_fallback_on_http_error(httpx_mock):
    httpx_mock.add_response(
        url="https://players.tarkov.dev/profile/index.json",
        status_code=500,
    )
    client = TarkovDevClient()
    name = await client.fetch_player_name("acc-123")
    await client.close()
    assert name == "acc-123"


# --- maybe_refresh (timing-based — tested with direct _last_fetched manipulation) ---

async def test_maybe_refresh_skips_when_recently_fetched(httpx_mock):
    client = TarkovDevClient()
    import time
    client._last_fetched = time.monotonic()  # just fetched
    client._last_activity = time.monotonic()
    # No HTTP mocks needed — update() should NOT be called
    await client.maybe_refresh()
    await client.close()
    # If update() had been called, pytest-httpx would raise UnmatchedRequests


async def test_maybe_refresh_skips_when_no_recent_activity(httpx_mock):
    client = TarkovDevClient()
    import time
    client._last_fetched = 0.0  # stale
    client._last_activity = 0.0  # no activity
    # No HTTP mocks needed — update() should NOT be called (no activity)
    await client.maybe_refresh()
    await client.close()
```

- [ ] **Step 2: Run tests to verify new ones fail**

```
pytest tests/test_tarkov_dev.py -v -k "trader or hideout or player or fence or maybe_refresh"
```

Expected: failures or import errors since `_fetch_traders`, `_fetch_hideout`, etc. are not yet in the existing code. (If Task 1 was done first, these are already implemented — all should PASS.)

- [ ] **Step 3: Run the full test suite**

```
pytest tests/test_tarkov_dev.py -v
```

Expected: all tests PASS (all methods were implemented in Task 1's tarkov_dev.py rewrite).

- [ ] **Step 4: Commit**

```
git add tests/test_tarkov_dev.py
git commit -m "test(tui): add coverage for traders, hideout, player names, and refresh logic"
```

---

### Task 3: Scav cooldown module + app wiring

**Files:**
- Create: `tarkovmonitor_tui/cooldown.py`
- Create: `tests/test_cooldown.py`
- Modify: `tarkovmonitor_tui/app.py`

**Interfaces:**
- Consumes: `TarkovTrader`, `TraderReputationLevel`, `TarkovHideoutStation`, `HideoutStationLevel`, `HideoutStationBonus` from `tarkov_dev.py`; `TarkovDevClient.get_fence()`, `.scav_cooldown_base_seconds`, `.hideout_stations`, `.record_activity()` from `tarkov_dev.py`
- Produces: `calculate_scav_cooldown(base_seconds, fence_rep_levels, karma, hideout_stations, built_level_ids) -> int`
- App: new setting key `"scav_karma": float`, `_scav_available_at: float | None`, status bar shows scav countdown when active, `scav_available` sound on expiry

- [ ] **Step 1: Write failing tests for calculate_scav_cooldown**

Create `tests/test_cooldown.py`:

```python
"""Tests for scav cooldown calculation."""

from __future__ import annotations

import pytest
from tarkovmonitor_tui.cooldown import calculate_scav_cooldown
from tarkovmonitor_tui.tarkov_dev import (
    HideoutStationBonus,
    HideoutStationLevel,
    TarkovHideoutStation,
    TraderReputationLevel,
)


def _fence_levels() -> list[TraderReputationLevel]:
    return [
        TraderReputationLevel(minimum_reputation=-7.0, scav_cooldown_modifier=1.5),
        TraderReputationLevel(minimum_reputation=-0.02, scav_cooldown_modifier=1.0),
        TraderReputationLevel(minimum_reputation=6.0, scav_cooldown_modifier=0.5),
    ]


def _air_filter_station() -> TarkovHideoutStation:
    return TarkovHideoutStation(
        id="s1",
        name="Air Filtering Unit",
        normalized_name="air-filtering-unit",
        levels=[
            HideoutStationLevel(
                id="lvl-1",
                level=1,
                bonuses=[HideoutStationBonus(type="ScavCooldownTimer", value=-0.05)],
            ),
            HideoutStationLevel(
                id="lvl-2",
                level=2,
                bonuses=[HideoutStationBonus(type="ScavCooldownTimer", value=-0.1)],
            ),
        ],
    )


def test_no_hideout_default_karma():
    # base=1500, karma=0.0 → modifier=1.0, hideout=0 → 1500
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 1500


def test_high_karma_halves_cooldown():
    # base=1500, karma=6.0 → modifier=0.5, hideout=0 → round(750) = 750
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=6.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 750


def test_low_karma_increases_cooldown():
    # base=1500, karma=-7.0 → modifier=1.5, hideout=0 → round(2250) = 2250
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=-7.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 2250


def test_hideout_level1_reduces_cooldown():
    # base=1500, karma=0 → modifier=1.0, hideout=0.05 → round(1500 - 75) = 1425
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids={"lvl-1"},
    )
    assert result == 1425


def test_hideout_level2_reduces_more():
    # base=1500, karma=0 → modifier=1.0, hideout=0.05+0.1=0.15 → round(1500 - 225) = 1275
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids={"lvl-1", "lvl-2"},
    )
    assert result == 1275


def test_combined_karma_and_hideout():
    # base=1500, karma=6.0 → modifier=0.5, base_effective=750
    # hideout=0.05 → round(750 - 37.5) = round(712.5) = 712 (Python banker's rounding)
    # but formula: round(base * karma_bonus - base * karma_bonus * hideout_bonus)
    # = round(750 - 750*0.05) = round(750 - 37.5) = round(712.5) = 712
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=6.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids={"lvl-1"},
    )
    assert result == round(750 - 750 * 0.05)


def test_empty_fence_levels_defaults_modifier_1():
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=[],
        karma=99.0,
        hideout_stations=[],
        built_level_ids=set(),
    )
    assert result == 1500


def test_unbuilt_levels_not_counted():
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[_air_filter_station()],
        built_level_ids=set(),  # nothing built
    )
    assert result == 1500


def test_non_cooldown_bonuses_ignored():
    station = TarkovHideoutStation(
        id="s2",
        name="Other",
        normalized_name="other",
        levels=[
            HideoutStationLevel(
                id="other-lvl",
                level=1,
                bonuses=[
                    HideoutStationBonus(type="HealthRegeneration", value=-0.5),
                    HideoutStationBonus(type="ScavCooldownTimer", value=-0.05),
                ],
            )
        ],
    )
    result = calculate_scav_cooldown(
        base_seconds=1500,
        fence_rep_levels=_fence_levels(),
        karma=0.0,
        hideout_stations=[station],
        built_level_ids={"other-lvl"},
    )
    assert result == 1425  # only ScavCooldownTimer counted
```

- [ ] **Step 2: Run tests to verify they fail**

```
pytest tests/test_cooldown.py -v
```

Expected: `ModuleNotFoundError: No module named 'tarkovmonitor_tui.cooldown'`

- [ ] **Step 3: Create tarkovmonitor_tui/cooldown.py**

```python
"""Scav cooldown calculation matching the .NET GUI formula."""

from __future__ import annotations

from tarkovmonitor_tui.tarkov_dev import (
    TarkovHideoutStation,
    TraderReputationLevel,
)


def calculate_scav_cooldown(
    base_seconds: int,
    fence_rep_levels: list[TraderReputationLevel],
    karma: float,
    hideout_stations: list[TarkovHideoutStation],
    built_level_ids: set[str],
) -> int:
    """Return scav cooldown in seconds.

    Formula: round(base × karma_bonus − base × karma_bonus × hideout_bonus)
    karma_bonus: scavCooldownModifier from the highest matching Fence rep level
    hideout_bonus: sum of abs(value) for all built ScavCooldownTimer station bonuses
    """
    karma_bonus = 1.0
    matching = [r for r in fence_rep_levels if r.minimum_reputation <= karma]
    if matching:
        karma_bonus = matching[-1].scav_cooldown_modifier

    hideout_bonus = sum(
        abs(bonus.value)
        for station in hideout_stations
        for level in station.levels
        if level.id in built_level_ids
        for bonus in level.bonuses
        if bonus.type == "ScavCooldownTimer"
    )

    return round(base_seconds * karma_bonus - base_seconds * karma_bonus * hideout_bonus)
```

- [ ] **Step 4: Run tests to verify they pass**

```
pytest tests/test_cooldown.py -v
```

Expected: all 9 tests PASS.

- [ ] **Step 5: Wire scav cooldown into app.py**

Make the following changes to `tarkovmonitor_tui/app.py`:

**5a.** Add import at top of file (after existing imports):
```python
import time

from .cooldown import calculate_scav_cooldown
```

**5b.** Add `_scav_available_at` and `_scav_karma` to `__init__`:
```python
# in __init__, after self._profile_id = "":
self._scav_available_at: float | None = None
```

**5c.** Add scav karma setting row to the Settings tab in `compose()`, after the TarkovTracker domain row:
```python
with Horizontal(classes="setting-row"):
    yield Label("Fence Karma:", classes="setting-label")
    yield Input(
        value=str(self._settings.get("scav_karma", "0.0")),
        placeholder="Your Fence reputation (e.g. 0.45)",
        id="input-scav-karma",
        classes="setting-input",
    )
```

**5d.** Save scav karma in `save_settings` handler (append to the settings dict updates before `save_settings(self._settings)`):
```python
self._settings["scav_karma"] = self.query_one("#input-scav-karma", Input).value
```

**5e.** Add `_start_scav_countdown` method and `_check_scav_timer` interval method:
```python
def _start_scav_countdown(self) -> None:
    fence = self._tarkov_dev.get_fence()
    if not fence:
        return
    try:
        karma = float(self._settings.get("scav_karma", "0.0"))
    except ValueError:
        karma = 0.0
    seconds = calculate_scav_cooldown(
        base_seconds=self._tarkov_dev.scav_cooldown_base_seconds,
        fence_rep_levels=fence.reputation_levels,
        karma=karma,
        hideout_stations=self._tarkov_dev.hideout_stations,
        built_level_ids=set(),  # no TarkovTracker integration yet
    )
    self._scav_available_at = time.monotonic() + seconds
    mins, secs = divmod(seconds, 60)
    self._log_message(f"Scav cooldown started: {mins}:{secs:02d}", "info")
    self._update_status_bar()
```

**5f.** Update `_on_raid_ended` and `_on_raid_exited` to trigger the cooldown:
```python
def _on_raid_ended(self, event_type: str, data: dict) -> None:
    info = GameEventClient.parse_raid_info(data)
    self.in_raid = False
    map_name = self._resolve_map_name(info.map)
    self._log_message(f"Ended {map_name} raid", "raid")
    self._update_raid_info(None)
    self._tarkov_dev.record_activity()
    self._start_scav_countdown()

def _on_raid_exited(self, event_type: str, data: dict) -> None:
    self.in_raid = False
    map_name = self._resolve_map_name(data.get("map", ""))
    self._log_message(f"Exited {map_name} raid", "raid")
    self._update_raid_info(None)
    self._tarkov_dev.record_activity()
    self._start_scav_countdown()
```

**5g.** Add `record_activity()` calls to `_on_raid_started` and `_on_map_loading`:
```python
def _on_raid_started(self, event_type: str, data: dict) -> None:
    self._tarkov_dev.record_activity()
    # ... rest of existing handler unchanged

def _on_map_loading(self, event_type: str, data: dict) -> None:
    self._tarkov_dev.record_activity()
    # ... rest of existing handler unchanged
```

**5h.** Add a Textual interval worker and update `_update_status_bar` to show countdown. Add to `on_mount`:
```python
def on_mount(self) -> None:
    self._log_message("TarkovMonitor TUI v0.1.0", "system")
    self._log_message(f"Connecting to {self._server_address}...", "info")
    self._update_status_bar()
    self.connect_to_service()
    self.fetch_tarkov_dev_data()
    self.set_interval(1.0, self._tick_scav_timer)
```

**5i.** Add `_tick_scav_timer` method:
```python
def _tick_scav_timer(self) -> None:
    if self._scav_available_at is None:
        return
    remaining = self._scav_available_at - time.monotonic()
    if remaining <= 0:
        self._scav_available_at = None
        self._sound_mgr.play("scav_available")
        self._log_message("Scav available!", "info")
    self._update_status_bar()
```

**5j.** Update `_update_status_bar` to append scav countdown when active:
```python
def _update_status_bar(self) -> None:
    try:
        bar = self.query_one("#status-bar", Static)
    except NoMatches:
        return
    status = self.connection_status
    if status == "Connected":
        text = f" [green]●[/green] Connected to {self._server_address}"
    else:
        text = f" [red]●[/red] Disconnected"

    if self._scav_available_at is not None:
        remaining = max(0, self._scav_available_at - time.monotonic())
        mins, secs = divmod(int(remaining), 60)
        text += f" | [cyan]Scav: {mins}:{secs:02d}[/cyan]"

    bar.update(text)
```

- [ ] **Step 6: Commit**

```
git add tarkovmonitor_tui/cooldown.py tests/test_cooldown.py tarkovmonitor_tui/app.py
git commit -m "feat(tui): add scav cooldown calculation and live countdown in status bar"
```

---

### Task 4: Air filter alerts + restartable task reminder

**Files:**
- Modify: `tarkovmonitor_tui/app.py`

**Interfaces:**
- Consumes: `SoundManager.play("air_filter_on")`, `SoundManager.play("air_filter_off")`, `SoundManager.play("restart_failed_tasks")`; `TarkovTask.restartable`, `TarkovTask.fail_if_complete`; settings key `"air_filter_installed": bool`
- Produces: air filter sounds on raid enter/exit; restartable task log + sound on MapLoading; fail cascade tracking on TaskFinished

- [ ] **Step 1: Add air_filter_installed setting to compose()**

In `tarkovmonitor_tui/app.py`, in the `compose()` method, add after the Fence Karma input row:

```python
with Horizontal(classes="setting-row"):
    yield Label("Air filter built:", classes="setting-label")
    yield Checkbox(
        value=bool(self._settings.get("air_filter_installed", False)),
        id="check-air-filter",
    )
```

- [ ] **Step 2: Save the checkbox in save_settings**

In the `save_settings` handler, add before `save_settings(self._settings)`:

```python
self._settings["air_filter_installed"] = self.query_one("#check-air-filter", Checkbox).value
```

- [ ] **Step 3: Add _failed_task_ids tracking to __init__**

In `__init__`, after `self._scav_available_at`:

```python
self._failed_task_ids: set[str] = set()
```

- [ ] **Step 4: Play air filter sounds on raid start/end**

Replace `_on_raid_starting`:
```python
def _on_raid_starting(self, event_type: str, data: dict) -> None:
    self._sound_mgr.play("raid_starting")
    if self._settings.get("air_filter_installed"):
        self._sound_mgr.play("air_filter_on")
    self._log_message("Raid starting...", "raid")
```

Replace `_on_raid_ended`:
```python
def _on_raid_ended(self, event_type: str, data: dict) -> None:
    info = GameEventClient.parse_raid_info(data)
    self.in_raid = False
    map_name = self._resolve_map_name(info.map)
    self._log_message(f"Ended {map_name} raid", "raid")
    self._update_raid_info(None)
    self._tarkov_dev.record_activity()
    self._start_scav_countdown()
    if self._settings.get("air_filter_installed"):
        self._sound_mgr.play("air_filter_off")
```

Replace `_on_raid_exited`:
```python
def _on_raid_exited(self, event_type: str, data: dict) -> None:
    self.in_raid = False
    map_name = self._resolve_map_name(data.get("map", ""))
    self._log_message(f"Exited {map_name} raid", "raid")
    self._update_raid_info(None)
    self._tarkov_dev.record_activity()
    self._start_scav_countdown()
    if self._settings.get("air_filter_installed"):
        self._sound_mgr.play("air_filter_off")
```

- [ ] **Step 5: Track failed tasks and fail cascade on task events**

Replace `_on_task_event`:
```python
def _on_task_event(self, event_type: str, data: dict) -> None:
    task_id = data.get("taskId", "")
    task = self._tarkov_dev.find_task(task_id)
    task_name = task.name if task else task_id

    if event_type == "TaskFailed":
        self._failed_task_ids.add(task_id)

    if event_type == "TaskFinished" and task:
        for other_task in self._tarkov_dev.tasks:
            if task_id in other_task.fail_if_complete:
                self._failed_task_ids.add(other_task.id)

    action = {
        "TaskStarted": "Started",
        "TaskFinished": "Completed",
        "TaskFailed": "Failed",
    }.get(event_type, event_type)
    link = f"[link={task.wiki_link}]{task_name}[/link]" if task and task.wiki_link else task_name
    self._log_message(f"{action} task: {link}", "quest")
```

- [ ] **Step 6: Show restartable task reminder on MapLoading**

Replace `_on_map_loading`:
```python
def _on_map_loading(self, event_type: str, data: dict) -> None:
    self._tarkov_dev.record_activity()
    info = GameEventClient.parse_raid_info(data)
    map_name = self._resolve_map_name(info.map)
    self._log_message(f"Loading map: {map_name}", "raid")

    restartable_failed = [
        t for t in self._tarkov_dev.tasks
        if t.id in self._failed_task_ids and t.restartable
    ]
    if restartable_failed:
        self._sound_mgr.play("restart_failed_tasks")
        for t in restartable_failed:
            link = f"[link={t.wiki_link}]{t.name}[/link]" if t.wiki_link else t.name
            self._log_message(f"Reminder: restart failed task — {link}", "quest")
```

- [ ] **Step 7: Verify the app imports Checkbox**

Confirm `Checkbox` is in the Textual imports at the top of `app.py`. It already appears in the import block. If not, add it:
```python
from textual.widgets import (
    Button,
    Checkbox,
    ...
)
```

- [ ] **Step 8: Manually smoke-test**

Start the test server (`dotnet run --project TarkovMonitor.Service.TestServer`), start the TUI (`python -m tarkovmonitor_tui.app`).

Verify:
1. Settings tab shows "Air filter built" checkbox
2. Enabling the checkbox, saving, then triggering a raid start plays the air filter sound
3. No crash on `MapLoading` events when task list is empty

- [ ] **Step 9: Commit**

```
git add tarkovmonitor_tui/app.py
git commit -m "feat(tui): add air filter alerts, restartable task reminders, fail cascade tracking, wiki links"
```

---

### Task 5: Manager API client — queue time + goons reporting

**Files:**
- Create: `tarkovmonitor_tui/manager_api.py`
- Create: `tests/test_manager_api.py`
- Modify: `tarkovmonitor_tui/app.py`

**Interfaces:**
- Produces:
  - `ManagerApiClient.post_queue_time(map_id, queue_time, raid_type) -> None`
  - `ManagerApiClient.post_goons_sighting(map_id) -> None`
  - `ManagerApiClient.close() -> None`
- App: new setting `"submit_queue_time": bool`; "Report Goons" button visible when current map `has_goons`

- [ ] **Step 1: Write failing tests for ManagerApiClient**

Create `tests/test_manager_api.py`:

```python
"""Tests for manager.tarkov.dev API client."""

from __future__ import annotations

import pytest
from tarkovmonitor_tui.manager_api import ManagerApiClient


async def test_post_queue_time_sends_correct_payload(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/queue",
        method="POST",
        status_code=200,
        json={"status": "ok"},
    )
    client = ManagerApiClient()
    await client.post_queue_time(map_id="bigmap", queue_time=47.3, raid_type="pmc")
    await client.close()

    request = httpx_mock.get_requests()[0]
    import json
    body = json.loads(request.content)
    assert body["mapId"] == "bigmap"
    assert body["queueTime"] == pytest.approx(47.3)
    assert body["raidType"] == "pmc"


async def test_post_queue_time_swallows_http_errors(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/queue",
        method="POST",
        status_code=500,
    )
    client = ManagerApiClient()
    # Should not raise
    await client.post_queue_time(map_id="bigmap", queue_time=10.0, raid_type="scav")
    await client.close()


async def test_post_goons_sighting_sends_map_id(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/goons",
        method="POST",
        status_code=200,
        json={"status": "ok"},
    )
    client = ManagerApiClient()
    await client.post_goons_sighting(map_id="customs")
    await client.close()

    request = httpx_mock.get_requests()[0]
    import json
    body = json.loads(request.content)
    assert body["mapId"] == "customs"
    assert "timestamp" in body


async def test_post_goons_swallows_errors(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/goons",
        method="POST",
        status_code=503,
    )
    client = ManagerApiClient()
    await client.post_goons_sighting(map_id="customs")
    await client.close()
```

- [ ] **Step 2: Run tests to verify they fail**

```
pytest tests/test_manager_api.py -v
```

Expected: `ModuleNotFoundError: No module named 'tarkovmonitor_tui.manager_api'`

- [ ] **Step 3: Create tarkovmonitor_tui/manager_api.py**

```python
"""Client for manager.tarkov.dev — queue time and goons sighting submission."""

from __future__ import annotations

import logging
from datetime import datetime, timezone

import httpx

log = logging.getLogger(__name__)

MANAGER_BASE = "https://manager.tarkov.dev/api"


class ManagerApiClient:
    def __init__(self) -> None:
        self._client = httpx.AsyncClient(
            base_url=MANAGER_BASE,
            timeout=10.0,
            headers={"User-Agent": "TarkovMonitor.TUI/0.1.0"},
        )

    async def post_queue_time(self, map_id: str, queue_time: float, raid_type: str) -> None:
        try:
            resp = await self._client.post(
                "/queue",
                json={"mapId": map_id, "queueTime": queue_time, "raidType": raid_type},
            )
            resp.raise_for_status()
        except Exception:
            log.debug("Failed to post queue time for %s", map_id)

    async def post_goons_sighting(self, map_id: str) -> None:
        try:
            ts = datetime.now(timezone.utc).isoformat()
            resp = await self._client.post(
                "/goons",
                json={"mapId": map_id, "timestamp": ts},
            )
            resp.raise_for_status()
        except Exception:
            log.debug("Failed to report goons on %s", map_id)

    async def close(self) -> None:
        await self._client.aclose()
```

- [ ] **Step 4: Run tests to verify they pass**

```
pytest tests/test_manager_api.py -v
```

Expected: all 4 tests PASS.

- [ ] **Step 5: Wire ManagerApiClient into app.py**

**5a.** Add import (after existing imports):
```python
from .manager_api import ManagerApiClient
```

**5b.** Instantiate in `__init__` (after `self._sound_mgr`):
```python
self._manager_api = ManagerApiClient()
```

**5c.** Close in `action_quit`:
```python
async def action_quit(self) -> None:
    await self._client.disconnect()
    await self._tarkov_dev.close()
    await self._manager_api.close()
    self.exit()
```

**5d.** Add "Submit queue times" setting to compose() (after the air filter row):
```python
with Horizontal(classes="setting-row"):
    yield Label("Submit queue times:", classes="setting-label")
    yield Checkbox(
        value=bool(self._settings.get("submit_queue_time", False)),
        id="check-submit-queue",
    )
```

**5e.** Save the checkbox in `save_settings`:
```python
self._settings["submit_queue_time"] = self.query_one("#check-submit-queue", Checkbox).value
```

**5f.** Submit queue time on `_on_match_found` (it fires when match is found, which has queue_time):
```python
def _on_match_found(self, event_type: str, data: dict) -> None:
    info = GameEventClient.parse_raid_info(data)
    self._sound_mgr.play("match_found")
    map_name = self._resolve_map_name(info.map)
    self._log_message(
        f"Match found on {map_name} after {info.queue_time:.0f}s",
        "match",
    )
    if self._settings.get("submit_queue_time") and info.map:
        raid_type = info.raid_type.value.lower() if info.raid_type else "pmc"
        import asyncio
        asyncio.create_task(
            self._manager_api.post_queue_time(info.map, info.queue_time, raid_type)
        )
```

**5g.** Add a goons button to the dashboard. In `compose()`, inside the dashboard `TabPane`, add after the `RichLog`:
```python
yield Button("Report Goons Sighting", id="btn-report-goons", variant="warning")
```

**5h.** Add CSS for the goons button (hidden by default):
```css
#btn-report-goons {
    display: none;
    dock: bottom;
    margin: 0 1;
}
#btn-report-goons.active {
    display: block;
}
```

Add this CSS to the `CSS` class variable in `TarkovMonitorApp`.

**5i.** Show/hide the goons button in `_update_raid_info`:
```python
def _update_raid_info(self, info) -> None:
    try:
        bar = self.query_one("#raid-info", Static)
    except NoMatches:
        return
    if info and info.map:
        map_name = self._resolve_map_name(info.map)
        bar.update(
            f" [bold]In Raid:[/bold] {map_name} | "
            f"Type: {info.raid_type.value} | "
            f"Queue: {info.queue_time:.0f}s"
        )
    else:
        bar.update(" [dim]Not in raid[/dim]")

    try:
        btn = self.query_one("#btn-report-goons", Button)
        current_map = self._tarkov_dev.find_map(self._current_raid_map)
        if current_map and current_map.has_goons:
            btn.add_class("active")
        else:
            btn.remove_class("active")
    except NoMatches:
        pass
```

**5j.** Handle goons button press in `on_button_pressed`:
```python
@on(Button.Pressed)
def on_button_pressed(self, event: Button.Pressed) -> None:
    btn_id = event.button.id or ""
    if btn_id == "btn-report-goons":
        if self._current_raid_map:
            import asyncio
            asyncio.create_task(self._manager_api.post_goons_sighting(self._current_raid_map))
            self._log_message(f"Goons sighting reported for {self._resolve_map_name(self._current_raid_map)}", "info")
        return
    if btn_id.startswith("test-"):
        key = btn_id[5:]
        self._sound_mgr.play(key)
```

- [ ] **Step 6: Run full test suite**

```
pytest tests/ -v
```

Expected: all tests PASS.

- [ ] **Step 7: Commit**

```
git add tarkovmonitor_tui/manager_api.py tests/test_manager_api.py tarkovmonitor_tui/app.py
git commit -m "feat(tui): add manager API client, queue time submission, goons report button"
```

---

### Task 6: TarkovTracker progress display + game mode switching

**Files:**
- Create: `tarkovmonitor_tui/tarkov_tracker.py`
- Create: `tests/test_tarkov_tracker.py`
- Modify: `tarkovmonitor_tui/app.py`

**Interfaces:**
- Produces: `TarkovTrackerClient.fetch_progress(token, domain) -> dict | None` returning `{"displayName", "playerLevel", "pmcFaction", "hideoutModulesProgress": [{"id", "complete"}]}`
- App: on `InitialReadComplete`, if TT token set, fetch and log displayName/playerLevel/pmcFaction; game mode switching on `ProfileChanged`

- [ ] **Step 1: Write failing tests for TarkovTrackerClient**

Create `tests/test_tarkov_tracker.py`:

```python
"""Tests for TarkovTracker API client."""

from __future__ import annotations

import pytest
from tarkovmonitor_tui.tarkov_tracker import TarkovTrackerClient


async def test_fetch_progress_returns_data(httpx_mock):
    httpx_mock.add_response(
        url="https://tarkovtracker.io/api/v2/progress",
        json={
            "data": {
                "displayName": "TestPlayer",
                "playerLevel": 42,
                "pmcFaction": "USEC",
                "hideoutModulesProgress": [
                    {"id": "module-1", "complete": True},
                    {"id": "module-2", "complete": False},
                ],
            }
        },
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="mytoken", domain="tarkovtracker.io")
    await client.close()

    assert result is not None
    assert result["displayName"] == "TestPlayer"
    assert result["playerLevel"] == 42
    assert result["pmcFaction"] == "USEC"
    assert len(result["hideoutModulesProgress"]) == 2
    assert result["hideoutModulesProgress"][0]["complete"] is True


async def test_fetch_progress_sends_bearer_token(httpx_mock):
    httpx_mock.add_response(
        url="https://tarkovtracker.io/api/v2/progress",
        json={"data": {"displayName": "X", "playerLevel": 1, "pmcFaction": "USEC", "hideoutModulesProgress": []}},
    )
    client = TarkovTrackerClient()
    await client.fetch_progress(token="secret-token", domain="tarkovtracker.io")
    await client.close()

    request = httpx_mock.get_requests()[0]
    assert request.headers["Authorization"] == "Bearer secret-token"


async def test_fetch_progress_returns_none_on_401(httpx_mock):
    httpx_mock.add_response(
        url="https://tarkovtracker.io/api/v2/progress",
        status_code=401,
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="bad-token", domain="tarkovtracker.io")
    await client.close()
    assert result is None


async def test_fetch_progress_uses_custom_domain(httpx_mock):
    httpx_mock.add_response(
        url="https://custom.tracker.io/api/v2/progress",
        json={"data": {"displayName": "X", "playerLevel": 1, "pmcFaction": "BEAR", "hideoutModulesProgress": []}},
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="tok", domain="custom.tracker.io")
    await client.close()
    assert result is not None
    assert result["pmcFaction"] == "BEAR"


async def test_fetch_progress_returns_none_on_network_error(httpx_mock):
    import httpx as _httpx
    httpx_mock.add_exception(
        _httpx.ConnectError("Network unreachable"),
        url="https://tarkovtracker.io/api/v2/progress",
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="tok", domain="tarkovtracker.io")
    await client.close()
    assert result is None
```

- [ ] **Step 2: Run tests to verify they fail**

```
pytest tests/test_tarkov_tracker.py -v
```

Expected: `ModuleNotFoundError: No module named 'tarkovmonitor_tui.tarkov_tracker'`

- [ ] **Step 3: Create tarkovmonitor_tui/tarkov_tracker.py**

```python
"""TarkovTracker API client — fetches progress data for the current profile."""

from __future__ import annotations

import logging

import httpx

log = logging.getLogger(__name__)


class TarkovTrackerClient:
    def __init__(self) -> None:
        self._client: httpx.AsyncClient | None = None

    def _get_client(self, domain: str) -> httpx.AsyncClient:
        base = f"https://{domain}"
        if self._client is None or str(self._client.base_url) != base + "/":
            if self._client is not None:
                # Will be garbage-collected; close lazily via close()
                pass
            self._client = httpx.AsyncClient(
                base_url=base,
                timeout=15.0,
                headers={"User-Agent": "TarkovMonitor.TUI/0.1.0"},
            )
        return self._client

    async def fetch_progress(self, token: str, domain: str = "tarkovtracker.io") -> dict | None:
        try:
            client = self._get_client(domain)
            resp = await client.get(
                "/api/v2/progress",
                headers={"Authorization": f"Bearer {token}"},
            )
            resp.raise_for_status()
            raw = resp.json()
            return raw.get("data")
        except Exception:
            log.debug("TarkovTracker fetch failed for domain %s", domain)
            return None

    async def close(self) -> None:
        if self._client is not None:
            await self._client.aclose()
            self._client = None
```

- [ ] **Step 4: Run tests to verify they pass**

```
pytest tests/test_tarkov_tracker.py -v
```

Expected: all 5 tests PASS.

- [ ] **Step 5: Wire TarkovTrackerClient + game mode switching into app.py**

**5a.** Add import:
```python
from .tarkov_tracker import TarkovTrackerClient
```

**5b.** Instantiate in `__init__` (after `self._manager_api`):
```python
self._tt_client = TarkovTrackerClient()
```

**5c.** Close in `action_quit`:
```python
async def action_quit(self) -> None:
    await self._client.disconnect()
    await self._tarkov_dev.close()
    await self._manager_api.close()
    await self._tt_client.close()
    self.exit()
```

**5d.** Replace `_on_profile_changed` to add game mode switching:
```python
def _on_profile_changed(self, event_type: str, data: dict) -> None:
    self._profile_id = data.get("profileId", "")
    ptype = data.get("profileType", "Regular")
    self._log_message(f"Using {ptype} profile", "profile")
    game_mode = "pve" if ptype == "PVE" else "regular"
    self._tarkov_dev.record_activity()
    if game_mode != self._tarkov_dev._game_mode:
        self._tarkov_dev._game_mode = game_mode
        self.fetch_tarkov_dev_data()
```

**5e.** Replace `_on_initial_read` to fetch TarkovTracker profile on startup:
```python
def _on_initial_read(self, event_type: str, data: dict) -> None:
    self._profile_id = data.get("profileId", "")
    ptype = data.get("profileType", "Regular")
    self._log_message(f"Initial read complete — {ptype} profile", "profile")
    game_mode = "pve" if ptype == "PVE" else "regular"
    self._tarkov_dev.record_activity()
    if game_mode != self._tarkov_dev._game_mode:
        self._tarkov_dev._game_mode = game_mode
        self.fetch_tarkov_dev_data()
    token = self._settings.get("tarkov_tracker_token", "")
    domain = self._settings.get("tarkov_tracker_domain", "tarkovtracker.io")
    if token:
        import asyncio
        asyncio.create_task(self._fetch_tt_profile(token, domain))
```

**5f.** Add `_fetch_tt_profile` async method:
```python
async def _fetch_tt_profile(self, token: str, domain: str) -> None:
    progress = await self._tt_client.fetch_progress(token=token, domain=domain)
    if not progress:
        self._log_message("TarkovTracker: could not fetch progress (check token/domain)", "error")
        return
    name = progress.get("displayName", "Unknown")
    level = progress.get("playerLevel", "?")
    faction = progress.get("pmcFaction", "?")
    self._log_message(
        f"TarkovTracker: {name} | Level {level} | {faction}",
        "profile",
    )
```

- [ ] **Step 6: Run the full test suite**

```
pytest tests/ -v
```

Expected: all tests PASS.

- [ ] **Step 7: Run auto-refresh worker**

Add a background refresh worker in `on_mount` (after `set_interval`):

```python
self.refresh_tarkov_dev_periodically()
```

Add the worker method:
```python
@work(exclusive=True, group="tarkov_dev_refresh")
async def refresh_tarkov_dev_periodically(self) -> None:
    while True:
        await asyncio.sleep(60)
        await self._tarkov_dev.maybe_refresh()
```

- [ ] **Step 8: Commit**

```
git add tarkovmonitor_tui/tarkov_tracker.py tests/test_tarkov_tracker.py tarkovmonitor_tui/app.py
git commit -m "feat(tui): add TarkovTracker profile display, game mode switching, auto-refresh"
```

---

## Self-Review

### Spec coverage

| Feature | Task | Status |
|---------|------|--------|
| Fix has_goons (wrong name, unused) | 1 | ✅ |
| Map normalized_name | 1 | ✅ |
| fail_if_complete on tasks | 1 | ✅ |
| Fetch traders (Fence rep levels) | 1 | ✅ |
| Fetch hideout stations | 1 | ✅ |
| settings.scavCooldownSeconds | 1 | ✅ |
| playerLevels[] | 1 | ✅ |
| fetch_player_name (players.tarkov.dev) | 1 | ✅ |
| auto-refresh (20 min, gated on 5 min activity) | 1+6 | ✅ |
| Game mode switching (regular/pve) | 6 | ✅ |
| Scav cooldown calculation | 3 | ✅ |
| Scav cooldown live countdown | 3 | ✅ |
| Scav available sound alert | 3 | ✅ |
| Air filter sound on raid start | 4 | ✅ |
| Air filter sound on raid end/exit | 4 | ✅ |
| Restartable task reminder on MapLoading | 4 | ✅ |
| fail_if_complete cascade on TaskFinished | 4 | ✅ |
| Wiki link in task log output | 4 | ✅ |
| Queue time submission | 5 | ✅ |
| Goons sighting report | 5 | ✅ |
| TarkovTracker profile display | 6 | ✅ |

### Known limitations (not spec regressions)

- **Scav cooldown hideout bonus** uses `built_level_ids=set()` (no TarkovTracker hideout progress wiring). The formula is correct; the set is empty until TarkovTracker hideout data is plumbed through (future work).
- **fetch_player_name** is implemented but not called from the app yet — it's available for future use when `GroupInviteAccept` events carry account IDs.
- **TarkovTrackerClient** creates a new `httpx.AsyncClient` per domain. If domain changes rapidly, old clients are not closed immediately. This is acceptable for the infrequent profile-change case.
- **asyncio.create_task()** inside sync event handlers requires that the gRPC worker runs within the app's event loop (true for Textual's worker model). If this causes issues at runtime, replace with `self.call_later(coro)` which is Textual's explicit mechanism.
