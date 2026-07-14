"""gRPC client for TarkovMonitor.Service — subscribes to game events and exposes config RPCs."""

from __future__ import annotations

import asyncio
import json
import logging
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Callable

import grpc

from . import game_events_pb2 as pb
from . import game_events_pb2_grpc as rpc

log = logging.getLogger(__name__)


class RaidType(Enum):
    UNKNOWN = "Unknown"
    PMC = "PMC"
    SCAV = "Scav"
    PVE = "PVE"


@dataclass
class Profile:
    id: str = ""
    account_id: str = ""
    type: str = "Regular"


@dataclass
class RaidInfo:
    map: str = ""
    raid_id: str = ""
    raid_type: RaidType = RaidType.UNKNOWN
    reconnected: bool = False
    queue_time: float = 0.0
    profile: Profile = field(default_factory=Profile)
    screenshots: list[str] = field(default_factory=list)


@dataclass
class GameEvent:
    event_type: str
    timestamp_ms: int
    data: dict[str, str]


class GameEventClient:
    """Async gRPC client that mirrors the C# GameEventClient's event dispatch."""

    def __init__(self, address: str = "localhost:50051"):
        self._address = address
        self._channel: grpc.aio.Channel | None = None
        self._stub: rpc.TarkovMonitorServiceStub | None = None
        self._listeners: dict[str, list[Callable]] = {}
        self._connected = False
        self._cancel_event = asyncio.Event()

    @property
    def connected(self) -> bool:
        return self._connected

    def on(self, event_type: str, callback: Callable) -> None:
        self._listeners.setdefault(event_type, []).append(callback)

    def _emit(self, event_type: str, data: dict[str, str]) -> None:
        for cb in self._listeners.get(event_type, []):
            try:
                cb(event_type, data)
            except Exception:
                log.exception("Error in event callback for %s", event_type)

    async def connect(self) -> None:
        self._channel = grpc.aio.insecure_channel(self._address)
        self._stub = rpc.TarkovMonitorServiceStub(self._channel)
        status = await self._stub.GetStatus(pb.GetStatusRequest())
        self._connected = True
        self._emit("ConnectionStateChanged", {"connected": "true"})
        log.info("Connected to service (uptime=%ds)", status.uptime_seconds)

    async def subscribe(self) -> None:
        if not self._stub:
            raise RuntimeError("Not connected")
        request = pb.SubscriptionRequest(client_agent="TarkovMonitor.TUI/0.1.0")
        try:
            stream = self._stub.SubscribeToGameEvents(request)
            async for event in stream:
                self._dispatch(event)
        except grpc.aio.AioRpcError as e:
            if e.code() != grpc.StatusCode.CANCELLED:
                self._connected = False
                self._emit("ConnectionStateChanged", {"connected": "false", "error": str(e)})
                raise

    def _dispatch(self, event: pb.GameEvent) -> None:
        data = dict(event.data)
        self._emit(event.event_type, data)

    async def get_config(self) -> pb.ServiceConfig:
        if not self._stub:
            raise RuntimeError("Not connected")
        return await self._stub.GetConfig(pb.GetConfigRequest())

    async def update_config(
        self,
        custom_logs_path: str = "",
        custom_map: str = "",
        tarkov_tracker_tokens: dict[str, str] | None = None,
        tarkov_tracker_domains: dict[str, str] | None = None,
        screenshots_path: str = "",
    ) -> pb.UpdateConfigResponse:
        if not self._stub:
            raise RuntimeError("Not connected")
        req = pb.UpdateConfigRequest(
            custom_logs_path=custom_logs_path,
            custom_map=custom_map,
            screenshots_path=screenshots_path,
        )
        if tarkov_tracker_tokens:
            for k, v in tarkov_tracker_tokens.items():
                req.tarkov_tracker_tokens[k] = v
        if tarkov_tracker_domains:
            for k, v in tarkov_tracker_domains.items():
                req.tarkov_tracker_domains[k] = v
        return await self._stub.UpdateConfig(req)

    async def get_status(self) -> pb.ServiceStatus:
        if not self._stub:
            raise RuntimeError("Not connected")
        return await self._stub.GetStatus(pb.GetStatusRequest())

    async def disconnect(self) -> None:
        self._connected = False
        if self._channel:
            await self._channel.close()

    @staticmethod
    def parse_raid_info(data: dict[str, str]) -> RaidInfo:
        raid_type_str = data.get("raidType", "Unknown")
        try:
            raid_type = RaidType(raid_type_str)
        except ValueError:
            raid_type = RaidType.UNKNOWN

        screenshots: list[str] = []
        try:
            screenshots = json.loads(data.get("screenshotsJson", "[]"))
        except json.JSONDecodeError:
            pass

        return RaidInfo(
            map=data.get("map", ""),
            raid_id=data.get("raidId", ""),
            raid_type=raid_type,
            reconnected=data.get("reconnected", "false").lower() == "true",
            queue_time=float(data.get("queueTime", "0")),
            profile=Profile(
                id=data.get("profileId", ""),
                account_id=data.get("profileAccountId", ""),
                type=data.get("profileType", "Regular"),
            ),
            screenshots=screenshots,
        )
