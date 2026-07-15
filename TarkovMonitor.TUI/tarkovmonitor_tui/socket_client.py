"""WebSocket client for tarkov.dev browser remote — relays player position and map commands."""

from __future__ import annotations

import json
import logging
from typing import Any

import websockets

from .screenshots import PlayerPosition

log = logging.getLogger(__name__)

WS_BASE = "wss://socket.tarkov.dev"


class TarkovSocketClient:
    """Maintains one persistent WebSocket connection per remote ID."""

    def __init__(self, remote_ids: list[str]) -> None:
        self._remote_ids = remote_ids
        self._connections: dict[str, Any] = {}

    async def send_player_position(
        self,
        position: PlayerPosition,
        map_normalized_name: str,
        view_radius: int | None = None,
    ) -> None:
        data: dict[str, Any] = {
            "type": "playerPosition",
            "map": map_normalized_name,
            "position": {"x": position.x, "y": position.y, "z": position.z},
            "rotation": position.rotation,
        }
        if view_radius is not None:
            data["viewRadius"] = view_radius
        await self._broadcast(data)

    async def navigate_to_map(self, map_normalized_name: str) -> None:
        await self._broadcast({"type": "map", "value": map_normalized_name})

    async def close(self) -> None:
        for ws in list(self._connections.values()):
            try:
                await ws.close()
            except Exception:
                pass
        self._connections.clear()

    async def _get_ws(self, remote_id: str) -> Any:
        ws = self._connections.get(remote_id)
        if ws is None or ws.closed:
            url = f"{WS_BASE}?sessionid={remote_id}-tm"
            ws = await websockets.connect(url)
            self._connections[remote_id] = ws
        return ws

    async def _broadcast(self, data: dict) -> None:
        for remote_id in self._remote_ids:
            try:
                ws = await self._get_ws(remote_id)
                payload = json.dumps({
                    "type": "command",
                    "sessionID": remote_id,
                    "data": data,
                })
                await ws.send(payload)
            except Exception:
                log.debug("WebSocket send failed for remote %s", remote_id, exc_info=True)
                self._connections.pop(remote_id, None)
