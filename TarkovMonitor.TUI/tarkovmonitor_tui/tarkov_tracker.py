"""TarkovTracker API client — fetches progress data for the current profile."""

from __future__ import annotations

import logging

import httpx

log = logging.getLogger(__name__)


class TarkovTrackerClient:
    def __init__(self) -> None:
        self._client: httpx.AsyncClient | None = None

    def _get_client(self, domain: str) -> httpx.AsyncClient:
        base = f"https://{domain}"
        if self._client is None or str(self._client.base_url) != base + "/":
            if self._client is not None:
                # Will be garbage-collected; close lazily via close()
                pass
            self._client = httpx.AsyncClient(
                base_url=base,
                timeout=15.0,
                headers={"User-Agent": "TarkovMonitor.TUI/0.1.0"},
            )
        return self._client

    async def fetch_progress(self, token: str, domain: str = "tarkovtracker.io") -> dict | None:
        try:
            client = self._get_client(domain)
            resp = await client.get(
                "/api/v2/progress",
                headers={"Authorization": f"Bearer {token}"},
            )
            resp.raise_for_status()
            raw = resp.json()
            return raw.get("data")
        except Exception:
            log.debug("TarkovTracker fetch failed for domain %s", domain)
            return None

    async def close(self) -> None:
        if self._client is not None:
            await self._client.aclose()
            self._client = None
