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
