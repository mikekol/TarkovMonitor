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
                "tasks": {
                    "task-a": {
                        "id": "task-a",
                        "name": "Task A",
                        "normalizedName": "task-a",
                        "wikiLink": "https://wiki.example.com/TaskA",
                        "restartable": True,
                        "failConditions": [],
                    },
                    "task-b": {
                        "id": "task-b",
                        "name": "Task B",
                        "normalizedName": "task-b",
                        "wikiLink": "",
                        "restartable": False,
                        "failConditions": [
                            {
                                "type": "taskStatus",
                                "task": "task-a",
                                "status": ["complete"],
                            }
                        ],
                    },
                }
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


async def test_fetch_tasks_null_task_in_fail_conditions(httpx_mock):
    """failConditions with "task": null must not crash the parser."""
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/tasks",
        json={
            "data": {
                "tasks": {
                    "task-x": {
                        "id": "task-x",
                        "name": "Task X",
                        "normalizedName": "task-x",
                        "wikiLink": "",
                        "restartable": False,
                        "failConditions": [
                            {"type": "taskStatus", "task": None, "status": ["complete"]},
                        ],
                    },
                }
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

    assert len(client.tasks) == 1
    assert client.tasks[0].fail_if_complete == []


# --- _fetch_traders ---

async def test_fetch_traders_parses_fence_reputation_levels(httpx_mock):
    httpx_mock.add_response(
        url="https://json.tarkov.dev/regular/traders",
        json={
            "data": {
                "fence-id": {
                    "id": "fence-id",
                    "name": "Fence",
                    "normalizedName": "fence",
                    "reputationLevels": [
                        {"minimumReputation": -7.0, "scavCooldownModifier": 1.5},
                        {"minimumReputation": -0.02, "scavCooldownModifier": 1.0},
                        {"minimumReputation": 6.0, "scavCooldownModifier": 0.5},
                    ],
                }
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
                "1": {"id": "1", "name": "Prapor", "normalizedName": "prapor", "reputationLevels": []},
                "2": {"id": "2", "name": "Fence", "normalizedName": "fence", "reputationLevels": []},
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
                "station-1": {
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
