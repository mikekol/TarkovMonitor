"""Screenshot watcher — parses EFT screenshot filenames for player position."""

from __future__ import annotations

import logging
import math
import os
import re
from dataclasses import dataclass
from pathlib import Path

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
    """Convert quaternion rotation to yaw angle (matches C# GameWatcher.QuarternionsToYaw)."""
    siny_cosp = 2.0 * (rw * ry - rz * rx)
    cosy_cosp = 1.0 - 2.0 * (rx * rx + ry * ry)
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

    rotation = quaternions_to_yaw(rx, ry, rz, rw)

    return PlayerPosition(
        x=x, y=y, z=z,
        rotation=rotation,
        filename=filename,
        map_name=current_map,
    )


def get_screenshots_path() -> Path:
    """Return the default EFT screenshots path."""
    docs = Path(os.path.expanduser("~/Documents"))
    return docs / "Escape From Tarkov" / "Screenshots"
