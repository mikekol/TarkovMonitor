"""Tests for manager.tarkov.dev API client."""

from __future__ import annotations

import pytest
from tarkovmonitor_tui.manager_api import ManagerApiClient


async def test_post_queue_time_sends_correct_payload(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/queue",
        method="POST",
        status_code=200,
        json={"status": "ok"},
    )
    client = ManagerApiClient()
    await client.post_queue_time(map_id="bigmap", queue_time=47.3, raid_type="pmc")
    await client.close()

    request = httpx_mock.get_requests()[0]
    import json
    body = json.loads(request.content)
    assert body["mapId"] == "bigmap"
    assert body["queueTime"] == pytest.approx(47.3)
    assert body["raidType"] == "pmc"


async def test_post_queue_time_swallows_http_errors(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/queue",
        method="POST",
        status_code=500,
    )
    client = ManagerApiClient()
    # Should not raise
    await client.post_queue_time(map_id="bigmap", queue_time=10.0, raid_type="scav")
    await client.close()


async def test_post_goons_sighting_sends_map_id(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/goons",
        method="POST",
        status_code=200,
        json={"status": "ok"},
    )
    client = ManagerApiClient()
    await client.post_goons_sighting(map_id="customs")
    await client.close()

    request = httpx_mock.get_requests()[0]
    import json
    body = json.loads(request.content)
    assert body["mapId"] == "customs"
    assert "timestamp" in body


async def test_post_goons_swallows_errors(httpx_mock):
    httpx_mock.add_response(
        url="https://manager.tarkov.dev/api/goons",
        method="POST",
        status_code=503,
    )
    client = ManagerApiClient()
    await client.post_goons_sighting(map_id="customs")
    await client.close()
