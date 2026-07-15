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
