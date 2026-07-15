"""Client for manager.tarkov.dev — queue time and goons sighting submission."""

from __future__ import annotations

import logging
from datetime import datetime, timezone

import httpx

log = logging.getLogger(__name__)

MANAGER_BASE = "https://manager.tarkov.dev/api"


class ManagerApiClient:
    def __init__(self) -> None:
        self._client = httpx.AsyncClient(
            base_url=MANAGER_BASE,
            timeout=10.0,
            headers={"User-Agent": "TarkovMonitor.TUI/0.1.0"},
        )

    async def post_queue_time(self, map_id: str, queue_time: float, raid_type: str) -> None:
        try:
            resp = await self._client.post(
                "/queue",
                json={"mapId": map_id, "queueTime": queue_time, "raidType": raid_type},
            )
            resp.raise_for_status()
        except Exception:
            log.debug("Failed to post queue time for %s", map_id)

    async def post_goons_sighting(self, map_id: str) -> None:
        try:
            ts = datetime.now(timezone.utc).isoformat()
            resp = await self._client.post(
                "/goons",
                json={"mapId": map_id, "timestamp": ts},
            )
            resp.raise_for_status()
        except Exception:
            log.debug("Failed to report goons on %s", map_id)

    async def close(self) -> None:
        await self._client.aclose()
