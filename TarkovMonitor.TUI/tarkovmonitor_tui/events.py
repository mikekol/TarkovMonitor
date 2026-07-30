"""Per-event command execution for TarkovMonitor TUI."""

from __future__ import annotations

import json
import logging
import subprocess
import threading
from pathlib import Path
from typing import Callable

log = logging.getLogger(__name__)

EVENT_KEYS: list[str] = [
    "raid_starting",
    "raid_stopping",
    "raid_ended",
    "match_found",
    "runthrough_over",
    "air_filter_on",
    "air_filter_off",
    "scav_available",
    "restart_failed_tasks",
    "quest_items",
]

EVENT_LABELS: dict[str, str] = {
    "raid_starting": "Raid Starting",
    "raid_stopping": "Raid Stopping",
    "raid_ended": "Raid Ended",
    "match_found": "Match Found",
    "runthrough_over": "Runthrough Over",
    "air_filter_on": "Air Filter On",
    "air_filter_off": "Air Filter Off",
    "scav_available": "Scav Available",
    "restart_failed_tasks": "Restart Failed Tasks",
    "quest_items": "Quest Items",
}


class EventManager:
    """Manages per-event enable toggle and optional shell command execution."""

    _CONFIG_PATH = Path.home() / ".tarkovmonitor-tui" / "event_config.json"

    def __init__(self) -> None:
        self._config: dict[str, dict] = {
            k: {"enabled": False, "command": "", "verbose": False}
            for k in EVENT_KEYS
        }
        self._load_config()

    def _load_config(self) -> None:
        if self._CONFIG_PATH.exists():
            try:
                data = json.loads(self._CONFIG_PATH.read_text())
                for k in EVENT_KEYS:
                    if k in data:
                        self._config[k].update(data[k])
            except Exception:
                log.exception("Error loading event config")

    def _save_config(self) -> None:
        self._CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
        self._CONFIG_PATH.write_text(json.dumps(self._config, indent=2))

    def is_enabled(self, key: str) -> bool:
        return bool(self._config.get(key, {}).get("enabled", True))

    def set_enabled(self, key: str, enabled: bool) -> None:
        self._config.setdefault(key, {})["enabled"] = enabled
        self._save_config()

    def get_command(self, key: str) -> str:
        return str(self._config.get(key, {}).get("command", ""))

    def is_verbose(self, key: str) -> bool:
        return bool(self._config.get(key, {}).get("verbose", False))

    def set_command(self, key: str, command: str, verbose: bool) -> None:
        self._config.setdefault(key, {})["command"] = command
        self._config.setdefault(key, {})["verbose"] = verbose
        self._save_config()

    def run(
        self,
        key: str,
        on_output: Callable[[str], None] | None = None,
        force_verbose: bool = False,
    ) -> None:
        if not self.is_enabled(key):
            return
        cmd = self.get_command(key)
        if not cmd:
            if on_output and force_verbose:
                on_output(f"[event:{key}] no command configured")
            return
        verbose = force_verbose or self.is_verbose(key)
        threading.Thread(
            target=self._execute,
            args=(key, cmd, verbose, on_output),
            daemon=True,
        ).start()

    @staticmethod
    def _execute(
        key: str,
        cmd: str,
        verbose: bool,
        on_output: Callable[[str], None] | None,
    ) -> None:
        try:
            result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
            if on_output is None:
                return
            if verbose:
                if result.stdout.strip():
                    on_output(f"[event:{key}] {result.stdout.strip()}")
                if result.stderr.strip():
                    on_output(f"[event:{key}] stderr: {result.stderr.strip()}")
                if result.returncode != 0:
                    on_output(f"[event:{key}] exit code {result.returncode}")
                elif not result.stdout.strip() and not result.stderr.strip():
                    on_output(f"[event:{key}] completed (no output)")
            elif result.returncode != 0:
                msg = result.stderr.strip() or f"exit code {result.returncode}"
                on_output(f"[event:{key}] error: {msg}")
        except Exception as exc:
            if on_output:
                on_output(f"[event:{key}] failed to launch: {exc}")
