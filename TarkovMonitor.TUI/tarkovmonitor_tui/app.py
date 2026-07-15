"""TarkovMonitor TUI — a Textual-based terminal client for TarkovMonitor.Service."""

from __future__ import annotations

import asyncio
import json
import logging
import os
import time
from datetime import datetime
from pathlib import Path

from textual import on, work
from textual.app import App, ComposeResult
from textual.binding import Binding
from textual.containers import Container, Horizontal, Vertical, VerticalScroll
from textual.css.query import NoMatches
from textual.reactive import reactive
from textual.widgets import (
    Button,
    Checkbox,
    Footer,
    Header,
    Input,
    Label,
    ListItem,
    ListView,
    RichLog,
    Static,
    Switch,
    TabbedContent,
    TabPane,
)

from .cooldown import calculate_scav_cooldown
from .grpc_client import GameEventClient, RaidType
from .screenshots import get_screenshots_path, parse_screenshot
from .sounds import SOUND_EVENTS, SOUND_LABELS, SoundManager
from .tarkov_dev import TarkovDevClient

log = logging.getLogger(__name__)

CONFIG_DIR = Path.home() / ".tarkovmonitor-tui"
SETTINGS_PATH = CONFIG_DIR / "settings.json"


def load_settings() -> dict:
    if SETTINGS_PATH.exists():
        try:
            return json.loads(SETTINGS_PATH.read_text())
        except Exception:
            pass
    return {}


def save_settings(settings: dict) -> None:
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    SETTINGS_PATH.write_text(json.dumps(settings, indent=2))


EVENT_CATEGORIES = {
    "RaidStarting": ("raid", "raid_starting"),
    "RaidStarted": ("raid", None),
    "RaidEnded": ("raid", None),
    "RaidExited": ("raid", None),
    "MapLoading": ("raid", None),
    "MatchFound": ("match", "match_found"),
    "TaskStarted": ("quest", None),
    "TaskFinished": ("quest", None),
    "TaskFailed": ("quest", None),
    "FleaSold": ("flea", None),
    "FleaOfferExpired": ("flea", None),
    "GroupInviteAccept": ("group", None),
    "GroupUserLeave": ("group", None),
    "DebugMessage": ("debug", None),
    "ExceptionThrown": ("error", None),
    "PlayerPosition": ("position", None),
    "ProfileChanged": ("profile", None),
    "InitialReadComplete": ("profile", None),
}

CATEGORY_STYLES = {
    "raid": "bold green",
    "match": "bold cyan",
    "quest": "bold yellow",
    "flea": "bold magenta",
    "group": "bold blue",
    "debug": "dim",
    "error": "bold red",
    "position": "cyan",
    "profile": "bold white",
    "info": "white",
    "system": "bold white",
}


class TarkovMonitorApp(App):
    """The main TUI application."""

    TITLE = "TarkovMonitor TUI"
    CSS = """
    Screen {
        background: $surface;
    }
    #status-bar {
        dock: top;
        height: 1;
        background: $primary-darken-2;
        color: $text;
        padding: 0 1;
    }
    #status-bar .connected {
        color: $success;
    }
    #status-bar .disconnected {
        color: $error;
    }
    #event-log {
        height: 1fr;
        border: solid $primary;
    }
    #settings-form {
        padding: 1 2;
    }
    .setting-row {
        height: 3;
        margin-bottom: 1;
    }
    .setting-label {
        width: 25;
        padding-top: 1;
    }
    .setting-input {
        width: 1fr;
    }
    #sound-list {
        height: 1fr;
        padding: 1;
    }
    .sound-row {
        height: 3;
        padding: 0 1;
    }
    .sound-name {
        width: 25;
        padding-top: 1;
    }
    .sound-toggle {
        width: 10;
    }
    .sound-test {
        width: 12;
    }
    #raid-info {
        dock: bottom;
        height: 3;
        background: $primary-darken-3;
        padding: 0 1;
        border-top: solid $primary;
    }
    """

    BINDINGS = [
        Binding("q", "quit", "Quit"),
        Binding("d", "show_tab('dashboard')", "Dashboard", show=True),
        Binding("s", "show_tab('settings')", "Settings", show=True),
        Binding("n", "show_tab('sounds')", "Sounds", show=True),
    ]

    connection_status = reactive("Disconnected")
    current_map = reactive("")
    in_raid = reactive(False)

    def __init__(self, server_address: str = "localhost:50051"):
        super().__init__()
        self._server_address = server_address
        self._client = GameEventClient(server_address)
        self._tarkov_dev = TarkovDevClient()
        self._sound_mgr = SoundManager()
        self._settings = load_settings()
        self._current_raid_map = ""
        self._profile_id = ""
        self._scav_available_at: float | None = None

    def compose(self) -> ComposeResult:
        yield Header()
        yield Static("", id="status-bar")
        with TabbedContent():
            with TabPane("Dashboard", id="dashboard"):
                yield RichLog(id="event-log", highlight=True, markup=True, wrap=True)
                yield Static("", id="raid-info")
            with TabPane("Settings", id="settings"):
                with VerticalScroll(id="settings-form"):
                    yield Label("Service Connection", classes="setting-label")
                    with Horizontal(classes="setting-row"):
                        yield Label("Server Address:", classes="setting-label")
                        yield Input(
                            value=self._settings.get("server_address", "localhost:50051"),
                            id="input-server",
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("Custom Logs Path:", classes="setting-label")
                        yield Input(
                            value=self._settings.get("custom_logs_path", ""),
                            placeholder="Leave empty for default",
                            id="input-logs-path",
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("Custom Map:", classes="setting-label")
                        yield Input(
                            value=self._settings.get("custom_map", ""),
                            placeholder="Fallback map for screenshots",
                            id="input-custom-map",
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("TarkovTracker Token:", classes="setting-label")
                        yield Input(
                            value=self._settings.get("tarkov_tracker_token", ""),
                            placeholder="Your TarkovTracker API token",
                            id="input-tt-token",
                            password=True,
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("TarkovTracker Domain:", classes="setting-label")
                        yield Input(
                            value=self._settings.get("tarkov_tracker_domain", "tarkovtracker.io"),
                            id="input-tt-domain",
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("Fence Karma:", classes="setting-label")
                        yield Input(
                            value=str(self._settings.get("scav_karma", "0.0")),
                            placeholder="Your Fence reputation (e.g. 0.45)",
                            id="input-scav-karma",
                            classes="setting-input",
                        )
                    with Horizontal(classes="setting-row"):
                        yield Label("Screenshots Path:", classes="setting-label")
                        yield Input(
                            value=self._settings.get("screenshots_path", str(get_screenshots_path())),
                            id="input-screenshots-path",
                            classes="setting-input",
                        )
                    yield Button("Save & Push to Service", id="btn-save-settings", variant="primary")
                    yield Button("Reconnect", id="btn-reconnect", variant="default")
            with TabPane("Sounds", id="sounds"):
                with VerticalScroll(id="sound-list"):
                    for key in SOUND_EVENTS:
                        with Horizontal(classes="sound-row"):
                            yield Label(SOUND_LABELS[key], classes="sound-name")
                            yield Switch(
                                value=self._sound_mgr.is_enabled(key),
                                id=f"sound-{key}",
                                classes="sound-toggle",
                            )
                            yield Button(
                                "Test",
                                id=f"test-{key}",
                                classes="sound-test",
                                variant="default",
                            )
        yield Footer()

    def on_mount(self) -> None:
        self._log_message("TarkovMonitor TUI v0.1.0", "system")
        self._log_message(f"Connecting to {self._server_address}...", "info")
        self._update_status_bar()
        self.connect_to_service()
        self.fetch_tarkov_dev_data()
        self.set_interval(1.0, self._tick_scav_timer)

    @work(exclusive=True, group="grpc")
    async def connect_to_service(self) -> None:
        self._wire_events()
        while True:
            try:
                await self._client.connect()
                self.connection_status = "Connected"
                self._update_status_bar()
                self._log_message("Connected to TarkovMonitor service", "system")
                await self._push_config()
                await self._client.subscribe()
            except Exception as e:
                self.connection_status = "Disconnected"
                self._update_status_bar()
                self._log_message(f"Connection failed: {e}. Retrying in 5s...", "error")
                await asyncio.sleep(5)

    @work(exclusive=True, group="tarkov_dev")
    async def fetch_tarkov_dev_data(self) -> None:
        await self._tarkov_dev.update()
        if self._tarkov_dev.maps:
            self._log_message(
                f"tarkov.dev: {len(self._tarkov_dev.maps)} maps, "
                f"{len(self._tarkov_dev.items)} items, "
                f"{len(self._tarkov_dev.tasks)} tasks",
                "info",
            )

    def _wire_events(self) -> None:
        self._client.on("RaidStarting", self._on_raid_starting)
        self._client.on("RaidStarted", self._on_raid_started)
        self._client.on("RaidEnded", self._on_raid_ended)
        self._client.on("RaidExited", self._on_raid_exited)
        self._client.on("MatchFound", self._on_match_found)
        self._client.on("MapLoading", self._on_map_loading)
        self._client.on("TaskStarted", self._on_task_event)
        self._client.on("TaskFinished", self._on_task_event)
        self._client.on("TaskFailed", self._on_task_event)
        self._client.on("FleaSold", self._on_flea_sold)
        self._client.on("FleaOfferExpired", self._on_flea_expired)
        self._client.on("GroupInviteAccept", self._on_group_invite)
        self._client.on("DebugMessage", self._on_debug)
        self._client.on("ExceptionThrown", self._on_exception)
        self._client.on("ProfileChanged", self._on_profile_changed)
        self._client.on("InitialReadComplete", self._on_initial_read)
        self._client.on("PlayerPosition", self._on_player_position)

    def _on_raid_starting(self, event_type: str, data: dict) -> None:
        self._sound_mgr.play("raid_starting")
        self._log_message("Raid starting...", "raid")

    def _on_raid_started(self, event_type: str, data: dict) -> None:
        self._tarkov_dev.record_activity()
        info = GameEventClient.parse_raid_info(data)
        self.in_raid = True
        self._current_raid_map = info.map
        map_name = self._resolve_map_name(info.map)
        if info.reconnected:
            self._log_message(f"Re-entering raid on {map_name}", "raid")
        else:
            self._log_message(
                f"Starting {info.raid_type.value} raid on {map_name}",
                "raid",
            )
        self._update_raid_info(info)

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

    def _on_match_found(self, event_type: str, data: dict) -> None:
        info = GameEventClient.parse_raid_info(data)
        self._sound_mgr.play("match_found")
        map_name = self._resolve_map_name(info.map)
        self._log_message(
            f"Match found on {map_name} after {info.queue_time:.0f}s",
            "match",
        )

    def _on_map_loading(self, event_type: str, data: dict) -> None:
        self._tarkov_dev.record_activity()
        info = GameEventClient.parse_raid_info(data)
        map_name = self._resolve_map_name(info.map)
        self._log_message(f"Loading map: {map_name}", "raid")

    def _on_task_event(self, event_type: str, data: dict) -> None:
        task_id = data.get("taskId", "")
        task = self._tarkov_dev.find_task(task_id)
        task_name = task.name if task else task_id
        action = {
            "TaskStarted": "Started",
            "TaskFinished": "Completed",
            "TaskFailed": "Failed",
        }.get(event_type, event_type)
        self._log_message(f"{action} task: {task_name}", "quest")

    def _on_flea_sold(self, event_type: str, data: dict) -> None:
        buyer = data.get("buyer", "Unknown")
        sold_id = data.get("soldItemId", "")
        count = data.get("soldItemCount", "1")
        item = self._tarkov_dev.find_item(sold_id)
        item_name = item.name if item else sold_id
        received = self._format_received(data.get("receivedItemsJson", "{}"))
        self._log_message(
            f"{buyer} purchased {count} {item_name} for {received}",
            "flea",
        )

    def _on_flea_expired(self, event_type: str, data: dict) -> None:
        item_id = data.get("itemId", "")
        count = data.get("itemCount", "1")
        item = self._tarkov_dev.find_item(item_id)
        item_name = item.name if item else item_id
        self._log_message(
            f"Offer expired: {item_name} (x{count})",
            "flea",
        )

    def _on_group_invite(self, event_type: str, data: dict) -> None:
        nick = data.get("nickname", "")
        side = data.get("side", "").upper()
        level = data.get("level", "?")
        self._log_message(f"{nick} ({side} {level}) accepted group invite", "group")

    def _on_debug(self, event_type: str, data: dict) -> None:
        self._log_message(data.get("message", ""), "debug")

    def _on_exception(self, event_type: str, data: dict) -> None:
        msg = data.get("message", "Unknown error")
        ctx = data.get("context", "")
        self._log_message(f"Error {ctx}: {msg}", "error")

    def _on_profile_changed(self, event_type: str, data: dict) -> None:
        self._profile_id = data.get("profileId", "")
        ptype = data.get("profileType", "Regular")
        self._log_message(f"Using {ptype} profile", "profile")

    def _on_initial_read(self, event_type: str, data: dict) -> None:
        self._profile_id = data.get("profileId", "")
        ptype = data.get("profileType", "Regular")
        self._log_message(f"Initial read complete — {ptype} profile", "profile")

    def _on_player_position(self, event_type: str, data: dict) -> None:
        x, y, z = data.get("x", "?"), data.get("y", "?"), data.get("z", "?")
        map_name = self._resolve_map_name(data.get("map", ""))
        self._log_message(f"Position on {map_name}: ({x}, {y}, {z})", "position")

    def _resolve_map_name(self, name_id: str) -> str:
        m = self._tarkov_dev.find_map(name_id)
        return m.name if m else (name_id or "Unknown")

    def _format_received(self, received_json: str) -> str:
        try:
            received = json.loads(received_json)
        except json.JSONDecodeError:
            return received_json
        parts = []
        for item_id, amount in received.items():
            if item_id == "5449016a4bdc2d6f028b456f":
                parts.append(f"{amount:,} RUB")
            elif item_id == "5696686a4bdc2da3298b456a":
                parts.append(f"${amount:,}")
            elif item_id == "569668774bdc2da2298b4568":
                parts.append(f"{amount:,} EUR")
            else:
                item = self._tarkov_dev.find_item(item_id)
                name = item.name if item else item_id
                parts.append(f"{amount:,} {name}")
        return ", ".join(parts) if parts else received_json

    def _log_message(self, text: str, category: str = "info") -> None:
        try:
            log_widget = self.query_one("#event-log", RichLog)
        except NoMatches:
            return
        ts = datetime.now().strftime("%H:%M:%S")
        style = CATEGORY_STYLES.get(category, "white")
        log_widget.write(f"[dim]{ts}[/dim] [{style}]{text}[/{style}]")

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

    def _tick_scav_timer(self) -> None:
        if self._scav_available_at is None:
            return
        remaining = self._scav_available_at - time.monotonic()
        if remaining <= 0:
            self._scav_available_at = None
            self._sound_mgr.play("scav_available")
            self._log_message("Scav available!", "info")
        self._update_status_bar()

    async def _push_config(self) -> None:
        try:
            await self._client.update_config(
                custom_logs_path=self._settings.get("custom_logs_path", ""),
                custom_map=self._settings.get("custom_map", ""),
                screenshots_path=self._settings.get("screenshots_path", str(get_screenshots_path())),
            )
        except Exception as e:
            self._log_message(f"Failed to push config: {e}", "error")

    @on(Button.Pressed, "#btn-save-settings")
    async def save_settings(self) -> None:
        self._settings["server_address"] = self.query_one("#input-server", Input).value
        self._settings["custom_logs_path"] = self.query_one("#input-logs-path", Input).value
        self._settings["custom_map"] = self.query_one("#input-custom-map", Input).value
        self._settings["tarkov_tracker_token"] = self.query_one("#input-tt-token", Input).value
        self._settings["tarkov_tracker_domain"] = self.query_one("#input-tt-domain", Input).value
        self._settings["scav_karma"] = self.query_one("#input-scav-karma", Input).value
        self._settings["screenshots_path"] = self.query_one("#input-screenshots-path", Input).value
        save_settings(self._settings)
        self._log_message("Settings saved", "system")

        try:
            tokens = {}
            domains = {}
            if self._profile_id and self._settings.get("tarkov_tracker_token"):
                tokens[self._profile_id] = self._settings["tarkov_tracker_token"]
            if self._profile_id and self._settings.get("tarkov_tracker_domain"):
                domains[self._profile_id] = self._settings["tarkov_tracker_domain"]

            await self._client.update_config(
                custom_logs_path=self._settings.get("custom_logs_path", ""),
                custom_map=self._settings.get("custom_map", ""),
                tarkov_tracker_tokens=tokens or None,
                tarkov_tracker_domains=domains or None,
                screenshots_path=self._settings.get("screenshots_path", ""),
            )
            self._log_message("Config pushed to service", "system")
        except Exception as e:
            self._log_message(f"Failed to push config: {e}", "error")

    @on(Button.Pressed, "#btn-reconnect")
    def reconnect(self) -> None:
        new_addr = self.query_one("#input-server", Input).value
        self._server_address = new_addr
        self._client = GameEventClient(new_addr)
        self._log_message(f"Reconnecting to {new_addr}...", "info")
        self.connect_to_service()

    @on(Switch.Changed)
    def on_sound_toggle(self, event: Switch.Changed) -> None:
        widget_id = event.switch.id or ""
        if widget_id.startswith("sound-"):
            key = widget_id[6:]
            self._sound_mgr.set_enabled(key, event.value)

    @on(Button.Pressed)
    def on_button_pressed(self, event: Button.Pressed) -> None:
        btn_id = event.button.id or ""
        if btn_id.startswith("test-"):
            key = btn_id[5:]
            self._sound_mgr.play(key)

    def action_show_tab(self, tab_id: str) -> None:
        tc = self.query_one(TabbedContent)
        tc.active = tab_id

    async def action_quit(self) -> None:
        await self._client.disconnect()
        await self._tarkov_dev.close()
        self.exit()


def main():
    import sys
    address = "localhost:50051"
    for i, arg in enumerate(sys.argv[1:]):
        if arg in ("--server", "-s") and i + 1 < len(sys.argv) - 1:
            address = sys.argv[i + 2]
        elif arg.startswith("--server="):
            address = arg.split("=", 1)[1]
    app = TarkovMonitorApp(server_address=address)
    app.run()


if __name__ == "__main__":
    main()
