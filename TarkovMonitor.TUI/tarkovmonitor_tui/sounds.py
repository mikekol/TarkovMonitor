"""Sound playback for event notifications — uses winsound on Windows, aplay on Linux."""

from __future__ import annotations

import json
import logging
import os
import platform
import subprocess
import threading
from pathlib import Path

log = logging.getLogger(__name__)

SOUND_EVENTS = [
    "raid_starting",
    "match_found",
    "runthrough_over",
    "air_filter_on",
    "air_filter_off",
    "scav_available",
    "restart_failed_tasks",
    "quest_items",
]

SOUND_LABELS = {
    "raid_starting": "Raid Starting",
    "match_found": "Match Found",
    "runthrough_over": "Runthrough Over",
    "air_filter_on": "Air Filter On",
    "air_filter_off": "Air Filter Off",
    "scav_available": "Scav Available",
    "restart_failed_tasks": "Restart Failed Tasks",
    "quest_items": "Quest Items",
}


class SoundManager:
    """Plays WAV files for game events. Supports custom sound overrides."""

    def __init__(self, sounds_dir: Path | None = None) -> None:
        self._sounds_dir = sounds_dir or Path.home() / ".tarkovmonitor-tui" / "sounds"
        self._config_path = Path.home() / ".tarkovmonitor-tui" / "sound_config.json"
        self._enabled: dict[str, bool] = {k: True for k in SOUND_EVENTS}
        self._load_config()

    def _load_config(self) -> None:
        if self._config_path.exists():
            try:
                data = json.loads(self._config_path.read_text())
                for k in SOUND_EVENTS:
                    self._enabled[k] = data.get(k, True)
            except Exception:
                log.exception("Error loading sound config")

    def _save_config(self) -> None:
        self._config_path.parent.mkdir(parents=True, exist_ok=True)
        self._config_path.write_text(json.dumps(self._enabled, indent=2))

    def is_enabled(self, key: str) -> bool:
        return self._enabled.get(key, True)

    def set_enabled(self, key: str, enabled: bool) -> None:
        self._enabled[key] = enabled
        self._save_config()

    def get_sound_path(self, key: str) -> Path | None:
        custom = self._sounds_dir / f"{key}.wav"
        if custom.exists():
            return custom
        bundled = Path(__file__).parent / "sounds" / f"{key}.wav"
        if bundled.exists():
            return bundled
        return None

    def play(self, key: str) -> None:
        if not self.is_enabled(key):
            return
        path = self.get_sound_path(key)
        if not path:
            log.debug("No sound file for %s", key)
            return
        threading.Thread(target=self._play_file, args=(path,), daemon=True).start()

    @staticmethod
    def _play_file(path: Path) -> None:
        try:
            if platform.system() == "Windows":
                import winsound
                winsound.PlaySound(str(path), winsound.SND_FILENAME | winsound.SND_ASYNC)
            else:
                subprocess.run(["aplay", str(path)], check=True, capture_output=True)
        except Exception:
            log.exception("Error playing sound %s", path)
