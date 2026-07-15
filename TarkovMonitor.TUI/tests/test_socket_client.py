"""Tests for socket_client.py."""
from __future__ import annotations

import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from tarkovmonitor_tui.screenshots import PlayerPosition
from tarkovmonitor_tui.socket_client import TarkovSocketClient

WS_URL = "wss://socket.tarkov.dev"


def _make_ws(sent: list[str]) -> MagicMock:
    ws = MagicMock()
    ws.closed = False
    ws.send = AsyncMock(side_effect=lambda m: sent.append(m))
    ws.close = AsyncMock()
    return ws


async def test_send_player_position_broadcasts_to_all_remotes():
    sent: list[str] = []
    ws = _make_ws(sent)

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["remote1", "remote2"])
        pos = PlayerPosition(x=100.0, y=5.5, z=-200.0, rotation=45.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "woods")
        await client.close()

    assert len(sent) == 2
    payload0 = json.loads(sent[0])
    assert payload0["type"] == "command"
    assert payload0["sessionID"] == "remote1"
    assert payload0["data"]["type"] == "playerPosition"
    assert payload0["data"]["map"] == "woods"
    assert payload0["data"]["position"] == {"x": 100.0, "y": 5.5, "z": -200.0}
    assert payload0["data"]["rotation"] == pytest.approx(45.0)
    assert "viewRadius" not in payload0["data"]


async def test_send_player_position_includes_view_radius_when_provided():
    sent: list[str] = []
    ws = _make_ws(sent)

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["r1"])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "customs", view_radius=300)
        await client.close()

    payload = json.loads(sent[0])
    assert payload["data"]["viewRadius"] == 300


async def test_navigate_to_map_sends_map_command():
    sent: list[str] = []
    ws = _make_ws(sent)

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["r1"])
        await client.navigate_to_map("interchange")
        await client.close()

    payload = json.loads(sent[0])
    assert payload["data"]["type"] == "map"
    assert payload["data"]["value"] == "interchange"


async def test_ws_url_includes_session_suffix():
    connect_calls: list[str] = []
    ws = _make_ws([])

    async def fake_connect(url, **kwargs):
        connect_calls.append(url)
        return ws

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", fake_connect):
        client = TarkovSocketClient(["myremote"])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "factory")
        await client.close()

    assert connect_calls[0] == f"{WS_URL}?sessionid=myremote-tm"


async def test_send_failure_clears_connection_and_does_not_raise():
    ws = MagicMock()
    ws.closed = False
    ws.send = AsyncMock(side_effect=Exception("connection reset"))
    ws.close = AsyncMock()

    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock, return_value=ws):
        client = TarkovSocketClient(["r1"])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        # must not raise
        await client.send_player_position(pos, "woods")
        assert "r1" not in client._connections
        await client.close()


async def test_empty_remote_ids_sends_nothing():
    with patch("tarkovmonitor_tui.socket_client.websockets.connect", new_callable=AsyncMock) as mock_connect:
        client = TarkovSocketClient([])
        pos = PlayerPosition(x=0.0, y=0.0, z=0.0, rotation=0.0, filename="f.png", map_name="")
        await client.send_player_position(pos, "woods")
        await client.close()
    mock_connect.assert_not_called()
