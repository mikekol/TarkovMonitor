"""Tests for TarkovTracker API client."""

from __future__ import annotations

import pytest
from tarkovmonitor_tui.tarkov_tracker import TarkovTrackerClient


async def test_fetch_progress_returns_data(httpx_mock):
    httpx_mock.add_response(
        url="https://tarkovtracker.io/api/v2/progress",
        json={
            "data": {
                "displayName": "TestPlayer",
                "playerLevel": 42,
                "pmcFaction": "USEC",
                "hideoutModulesProgress": [
                    {"id": "module-1", "complete": True},
                    {"id": "module-2", "complete": False},
                ],
            }
        },
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="mytoken", domain="tarkovtracker.io")
    await client.close()

    assert result is not None
    assert result["displayName"] == "TestPlayer"
    assert result["playerLevel"] == 42
    assert result["pmcFaction"] == "USEC"
    assert len(result["hideoutModulesProgress"]) == 2
    assert result["hideoutModulesProgress"][0]["complete"] is True


async def test_fetch_progress_sends_bearer_token(httpx_mock):
    httpx_mock.add_response(
        url="https://tarkovtracker.io/api/v2/progress",
        json={"data": {"displayName": "X", "playerLevel": 1, "pmcFaction": "USEC", "hideoutModulesProgress": []}},
    )
    client = TarkovTrackerClient()
    await client.fetch_progress(token="secret-token", domain="tarkovtracker.io")
    await client.close()

    request = httpx_mock.get_requests()[0]
    assert request.headers["Authorization"] == "Bearer secret-token"


async def test_fetch_progress_returns_none_on_401(httpx_mock):
    httpx_mock.add_response(
        url="https://tarkovtracker.io/api/v2/progress",
        status_code=401,
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="bad-token", domain="tarkovtracker.io")
    await client.close()
    assert result is None


async def test_fetch_progress_uses_custom_domain(httpx_mock):
    httpx_mock.add_response(
        url="https://custom.tracker.io/api/v2/progress",
        json={"data": {"displayName": "X", "playerLevel": 1, "pmcFaction": "BEAR", "hideoutModulesProgress": []}},
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="tok", domain="custom.tracker.io")
    await client.close()
    assert result is not None
    assert result["pmcFaction"] == "BEAR"


async def test_fetch_progress_returns_none_on_network_error(httpx_mock):
    import httpx as _httpx
    httpx_mock.add_exception(
        _httpx.ConnectError("Network unreachable"),
        url="https://tarkovtracker.io/api/v2/progress",
    )
    client = TarkovTrackerClient()
    result = await client.fetch_progress(token="tok", domain="tarkovtracker.io")
    await client.close()
    assert result is None
