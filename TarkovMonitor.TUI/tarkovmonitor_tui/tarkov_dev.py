"""tarkov.dev API integration — fetches maps, items, and tasks for display names."""

from __future__ import annotations

import logging
from dataclasses import dataclass, field

import httpx

log = logging.getLogger(__name__)

API_BASE = "https://json.tarkov.dev"


@dataclass
class TarkovMap:
    id: str
    name: str
    name_id: str
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


class TarkovDevClient:
    """Fetches reference data from the tarkov.dev JSON API."""

    def __init__(self, game_mode: str = "regular", lang: str = "en") -> None:
        self.maps: list[TarkovMap] = []
        self.items: list[TarkovItem] = []
        self.tasks: list[TarkovTask] = []
        self._game_mode = game_mode
        self._lang = lang
        self._client = httpx.AsyncClient(
            base_url=API_BASE,
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

    async def update(self) -> None:
        try:
            await self._fetch_maps()
            await self._fetch_items()
            await self._fetch_tasks()
            log.info(
                "tarkov.dev: %d maps, %d items, %d tasks",
                len(self.maps), len(self.items), len(self.tasks),
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
                has_goons=bool(m.get("bosses", []) and any(
                    b.get("normalizedName") == "death-knight" for b in m.get("bosses", [])
                )),
            )
            for m in items
        ]

    async def _fetch_items(self) -> None:
        resp = await self._client.get(f"/{self._game_mode}/items")
        resp.raise_for_status()
        raw = resp.json()
        tr = await self._fetch_translations("items")
        data = raw.get("data", raw)
        if isinstance(data, dict) and "items" in data:
            items = data["items"]
            if isinstance(items, dict):
                items = list(items.values())
        elif isinstance(data, list):
            items = data
        else:
            items = []
        self.items = [
            TarkovItem(
                id=i.get("id", ""),
                name=self._translate(i.get("name", ""), tr),
                normalized_name=i.get("normalizedName", ""),
                link=i.get("link", ""),
            )
            for i in items
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
            )
            for t in items
        ]

    def find_map(self, name_id: str) -> TarkovMap | None:
        return next((m for m in self.maps if m.name_id == name_id), None)

    def find_item(self, item_id: str) -> TarkovItem | None:
        return next((i for i in self.items if i.id == item_id), None)

    def find_task(self, task_id: str) -> TarkovTask | None:
        return next((t for t in self.tasks if t.id == task_id), None)

    async def close(self) -> None:
        await self._client.aclose()
