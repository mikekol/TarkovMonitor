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
                    c["task"]
                    for c in t.get("failConditions", [])
                    if isinstance(c, dict)
                    and "complete" in c.get("status", [])
                    and isinstance(c.get("task"), str)
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
            traders_raw = list(data["traders"].values()) if isinstance(data["traders"], dict) else data["traders"]
        elif isinstance(data, dict):
            traders_raw = list(data.values())
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
            stations_raw = list(data["stations"].values()) if isinstance(data["stations"], dict) else data["stations"]
        elif isinstance(data, dict):
            stations_raw = list(data.values())
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
