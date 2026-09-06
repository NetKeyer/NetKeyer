from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any
from uuid import uuid4

from fastapi import WebSocket


UTC = timezone.utc


class DuplicateRegistrationError(ValueError):
    pass


@dataclass
class HostConnection:
    host_id: str
    ws: WebSocket
    public_ip: str
    public_port: int
    max_clients: int
    metadata: dict[str, Any] = field(default_factory=dict)
    current_clients: int = 0
    last_seen: datetime = field(default_factory=lambda: datetime.now(UTC))


@dataclass
class ClientConnection:
    client_id: str
    ws: WebSocket
    public_ip: str
    public_port: int
    connected_host: str | None = None
    last_seen: datetime = field(default_factory=lambda: datetime.now(UTC))


@dataclass
class SessionState:
    session_id: str
    host_id: str
    client_id: str
    created_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    updated_at: datetime = field(default_factory=lambda: datetime.now(UTC))
    state: str = "requested"
    host_punch_result: bool | None = None
    client_punch_result: bool | None = None
    map_requested: bool = False
    mapped_public_ip: str | None = None
    mapped_public_port: int | None = None
    timeout_task: asyncio.Task | None = None


class RendezvousState:
    def __init__(self) -> None:
        self._lock = asyncio.Lock()
        self.hosts: dict[str, HostConnection] = {}
        self.clients: dict[str, ClientConnection] = {}
        self.sessions: dict[str, SessionState] = {}
        self.security_metrics: dict[str, int] = {
            "auth_failures": 0,
            "handshake_failures": 0,
            "replay_rejects": 0,
            "decrypt_failures": 0,
        }

    async def increment_security_metric(self, name: str, count: int = 1) -> None:
        if not name or count <= 0:
            return

        async with self._lock:
            current = self.security_metrics.get(name, 0)
            self.security_metrics[name] = current + count

    async def record_security_failure(
        self,
        *,
        auth_failure: bool = False,
        handshake_failure: bool = False,
        detail: str = "",
    ) -> None:
        names: list[str] = []
        if auth_failure:
            names.append("auth_failures")
        if handshake_failure:
            names.append("handshake_failures")

        lower = (detail or "").strip().lower()
        if "replayed" in lower:
            names.append("replay_rejects")
        if "invalid access token" in lower or "invalid connection grant token" in lower:
            names.append("decrypt_failures")

        if not names:
            return

        async with self._lock:
            for name in names:
                current = self.security_metrics.get(name, 0)
                self.security_metrics[name] = current + 1

    async def register_host(
        self,
        host_id: str,
        ws: WebSocket,
        public_ip: str,
        public_port: int,
        max_clients: int,
        metadata: dict[str, Any],
    ) -> HostConnection:
        async with self._lock:
            existing = self.hosts.get(host_id)
            if existing and existing.ws is not ws:
                raise DuplicateRegistrationError(f"duplicate host_id: {host_id}")

            conn = HostConnection(
                host_id=host_id,
                ws=ws,
                public_ip=public_ip,
                public_port=public_port,
                max_clients=max_clients,
                metadata=metadata or {},
            )
            self.hosts[host_id] = conn
            return conn

    async def unregister_host(self, host_id: str) -> None:
        async with self._lock:
            self.hosts.pop(host_id, None)

    async def unregister_host_by_ws(self, ws: WebSocket) -> None:
        async with self._lock:
            ids = [h.host_id for h in self.hosts.values() if h.ws is ws]
            for host_id in ids:
                self.hosts.pop(host_id, None)

    async def register_client(
        self,
        client_id: str,
        ws: WebSocket,
        public_ip: str,
        public_port: int,
    ) -> ClientConnection:
        async with self._lock:
            existing = self.clients.get(client_id)
            if existing and existing.ws is not ws:
                raise DuplicateRegistrationError(f"duplicate client_id: {client_id}")

            conn = ClientConnection(
                client_id=client_id,
                ws=ws,
                public_ip=public_ip,
                public_port=public_port,
            )
            self.clients[client_id] = conn
            return conn

    async def unregister_client(self, client_id: str) -> None:
        async with self._lock:
            self.clients.pop(client_id, None)

    async def unregister_client_by_ws(self, ws: WebSocket) -> None:
        async with self._lock:
            ids = [c.client_id for c in self.clients.values() if c.ws is ws]
            for client_id in ids:
                self.clients.pop(client_id, None)

    async def list_hosts_for_client(self) -> list[HostConnection]:
        async with self._lock:
            return sorted(
                [h for h in self.hosts.values() if h.current_clients < h.max_clients],
                key=lambda h: h.host_id,
            )

    async def get_host(self, host_id: str) -> HostConnection | None:
        async with self._lock:
            return self.hosts.get(host_id)

    async def get_client(self, client_id: str) -> ClientConnection | None:
        async with self._lock:
            return self.clients.get(client_id)

    async def create_session(self, host_id: str, client_id: str, session_id: str | None = None) -> SessionState:
        async with self._lock:
            if session_id:
                if session_id in self.sessions:
                    raise ValueError("session_id already exists")
                next_session_id = session_id
            else:
                next_session_id = uuid4().hex
            session = SessionState(session_id=next_session_id, host_id=host_id, client_id=client_id, state="punch_signaled")
            self.sessions[next_session_id] = session

            host = self.hosts.get(host_id)
            client = self.clients.get(client_id)
            if host:
                host.current_clients = min(host.max_clients, host.current_clients + 1)
                host.last_seen = datetime.now(UTC)
            if client:
                client.connected_host = host_id
                client.last_seen = datetime.now(UTC)

            return session

    async def attach_timeout_task(self, session_id: str, task: asyncio.Task) -> None:
        async with self._lock:
            session = self.sessions.get(session_id)
            if not session:
                task.cancel()
                return
            session.timeout_task = task

    async def get_session(self, session_id: str) -> SessionState | None:
        async with self._lock:
            return self.sessions.get(session_id)

    async def update_punch_result(self, session_id: str, from_host: bool, success: bool) -> SessionState | None:
        async with self._lock:
            session = self.sessions.get(session_id)
            if not session:
                return None

            if from_host:
                session.host_punch_result = success
            else:
                session.client_punch_result = success

            if success:
                if session.state == "relay_requested":
                    session.state = "relay_connected"
                elif session.state in {"map_requested", "map_ready"} or session.map_requested:
                    session.state = "mapped_connected"
                else:
                    session.state = "direct_connected"
                if session.timeout_task:
                    session.timeout_task.cancel()
                    session.timeout_task = None
            elif session.map_requested:
                session.state = "relay_requested"
                if session.timeout_task:
                    session.timeout_task.cancel()
                    session.timeout_task = None

            session.updated_at = datetime.now(UTC)
            return session

    async def mark_map_requested(self, session_id: str) -> SessionState | None:
        async with self._lock:
            session = self.sessions.get(session_id)
            if not session:
                return None
            if session.state in {"direct_connected", "relay_connected", "closed"}:
                return session

            session.map_requested = True
            session.state = "map_requested"
            if session.timeout_task:
                session.timeout_task.cancel()
                session.timeout_task = None

            session.updated_at = datetime.now(UTC)
            return session

    async def set_mapped_endpoint(self, session_id: str, public_ip: str | None, public_port: int) -> SessionState | None:
        async with self._lock:
            session = self.sessions.get(session_id)
            if not session:
                return None
            if session.state in {"direct_connected", "relay_connected", "closed"}:
                return session

            session.map_requested = True
            session.mapped_public_ip = public_ip
            session.mapped_public_port = public_port
            session.state = "map_ready"
            session.updated_at = datetime.now(UTC)
            return session

    async def mark_relay_requested(self, session_id: str) -> SessionState | None:
        async with self._lock:
            session = self.sessions.get(session_id)
            if not session:
                return None
            if session.state in {"direct_connected", "relay_connected", "closed"}:
                return session
            session.state = "relay_requested"
            session.updated_at = datetime.now(UTC)
            return session

    async def close_session(self, session_id: str) -> None:
        async with self._lock:
            session = self.sessions.pop(session_id, None)
            if not session:
                return

            if session.timeout_task:
                session.timeout_task.cancel()

            host = self.hosts.get(session.host_id)
            client = self.clients.get(session.client_id)
            if host:
                host.current_clients = max(0, host.current_clients - 1)
                host.last_seen = datetime.now(UTC)
            if client and client.connected_host == session.host_id:
                client.connected_host = None
                client.last_seen = datetime.now(UTC)

    async def close_sessions_for_host(self, host_id: str) -> None:
        async with self._lock:
            session_ids = [s.session_id for s in self.sessions.values() if s.host_id == host_id]

        for session_id in session_ids:
            await self.close_session(session_id)

    async def close_sessions_for_client(self, client_id: str) -> None:
        async with self._lock:
            session_ids = [s.session_id for s in self.sessions.values() if s.client_id == client_id]

        for session_id in session_ids:
            await self.close_session(session_id)

    async def sweep_expired_sessions(self, ttl_seconds: int = 30) -> int:
        now = datetime.now(UTC)
        cutoff = now - timedelta(seconds=ttl_seconds)

        async with self._lock:
            expired = [
                s.session_id
                for s in self.sessions.values()
                if s.updated_at < cutoff
                and s.state not in {"direct_connected", "mapped_connected", "relay_connected"}
            ]

        for session_id in expired:
            await self.close_session(session_id)

        return len(expired)

    @staticmethod
    def _connection_type_for_session(session: SessionState) -> str:
        if session.state == "direct_connected":
            return "direct"
        if session.state in {"map_requested", "map_ready", "mapped_connected"}:
            return "mapped"
        if session.state in {"relay_requested", "relay_connected"}:
            return "relay"
        return "direct"

    async def get_statistics_snapshot(self) -> dict[str, Any]:
        async with self._lock:
            host_ids = sorted(self.hosts.keys())
            client_ids = sorted(self.clients.keys())
            sessions = sorted(self.sessions.values(), key=lambda s: s.created_at)

            host_entries: list[dict[str, Any]] = []
            for host_id in host_ids:
                host = self.hosts[host_id]
                host_sessions = [s for s in sessions if s.host_id == host_id]
                host_entries.append(
                    {
                        "host_id": host.host_id,
                        "public_ip": host.public_ip,
                        "public_port": host.public_port,
                        "current_clients": host.current_clients,
                        "max_clients": host.max_clients,
                        "active_sessions": [
                            {
                                "session_id": s.session_id,
                                "client_id": s.client_id,
                                "type": self._connection_type_for_session(s),
                                "state": s.state,
                            }
                            for s in host_sessions
                        ],
                    }
                )

            client_entries: list[dict[str, Any]] = []
            for client_id in client_ids:
                client = self.clients[client_id]
                client_sessions = [s for s in sessions if s.client_id == client_id]
                client_entries.append(
                    {
                        "client_id": client.client_id,
                        "public_ip": client.public_ip,
                        "public_port": client.public_port,
                        "connected_host": client.connected_host,
                        "active_sessions": [
                            {
                                "session_id": s.session_id,
                                "host_id": s.host_id,
                                "type": self._connection_type_for_session(s),
                                "state": s.state,
                            }
                            for s in client_sessions
                        ],
                    }
                )

            session_entries = [
                {
                    "session_id": s.session_id,
                    "host_id": s.host_id,
                    "client_id": s.client_id,
                    "state": s.state,
                    "type": self._connection_type_for_session(s),
                    "map_requested": s.map_requested,
                    "mapped_public_ip": s.mapped_public_ip,
                    "mapped_public_port": s.mapped_public_port,
                    "host_punch_result": s.host_punch_result,
                    "client_punch_result": s.client_punch_result,
                }
                for s in sessions
            ]

            counts = {
                "hosts": len(host_entries),
                "clients": len(client_entries),
                "sessions": len(session_entries),
            }

            type_counts = {
                "direct": sum(1 for s in session_entries if s["type"] == "direct"),
                "mapped": sum(1 for s in session_entries if s["type"] == "mapped"),
                "relay": sum(1 for s in session_entries if s["type"] == "relay"),
            }

            return {
                "counts": counts,
                "session_type_counts": type_counts,
                "security_metrics": dict(self.security_metrics),
                "hosts": host_entries,
                "clients": client_entries,
                "sessions": session_entries,
            }
