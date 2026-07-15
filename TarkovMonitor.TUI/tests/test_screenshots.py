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
