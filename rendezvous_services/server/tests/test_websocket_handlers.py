import asyncio
import types
import unittest

from fastapi import WebSocketDisconnect

from server import websocket_handlers as handlers
from server.auth import AuthConfig
from server.state import RendezvousState


_DISCONNECT = object()


class FakeWebSocket:
    def __init__(self, host: str, port: int) -> None:
        self.client = types.SimpleNamespace(host=host, port=port)
        self.state = types.SimpleNamespace(auth_claims=None)
        self._incoming: asyncio.Queue = asyncio.Queue()
        self.sent: list[dict] = []
        self.accepted = False

    async def accept(self) -> None:
        self.accepted = True

    async def receive_json(self) -> dict:
        item = await self._incoming.get()
        if item is _DISCONNECT:
            raise WebSocketDisconnect(code=1000)
        return item

    async def send_json(self, payload: dict) -> None:
        self.sent.append(payload)

    async def push(self, payload: dict) -> None:
        await self._incoming.put(payload)

    async def disconnect(self) -> None:
        await self._incoming.put(_DISCONNECT)


class TestWebSocketHandlers(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self) -> None:
        self.state = RendezvousState()
        self.host_ws = FakeWebSocket("203.0.113.10", 51000)
        self.client_ws = FakeWebSocket("198.51.100.22", 52000)

    async def _start_handlers(self, force_relay: bool = False, auth_config: AuthConfig | None = None):
        host_task = asyncio.create_task(
            handlers.handle_host_ws(self.state, self.host_ws, relay_host="relay.test", relay_port=49921)
        )
        client_task = asyncio.create_task(
            handlers.handle_client_ws(
                self.state,
                self.client_ws,
                relay_host="relay.test",
                relay_port=49921,
                force_relay=force_relay,
                auth_config=auth_config,
            )
        )
        return host_task, client_task

    @staticmethod
    def _auth_config(require_connection_grant: bool) -> AuthConfig:
        return AuthConfig(
            require_signed_tokens=False,
            allow_legacy_no_token=True,
            jwt_secret="test-secret",
            jwt_issuer="",
            jwt_audience="",
            required_scope_host="",
            required_scope_client="",
            jti_replay_ttl_seconds=60,
            jti_replay_cache_max_entries=1000,
            require_jti=False,
            require_connection_grant=require_connection_grant,
            connection_grant_ttl_seconds=30,
            connection_grant_secret="",
            require_protocol_version_claim=False,
            expected_protocol_version=1,
            jwt_keyring={},
        )

    @staticmethod
    def _auth_config_without_grant_secret(require_connection_grant: bool) -> AuthConfig:
        return AuthConfig(
            require_signed_tokens=False,
            allow_legacy_no_token=True,
            jwt_secret="",
            jwt_issuer="",
            jwt_audience="",
            required_scope_host="",
            required_scope_client="",
            jti_replay_ttl_seconds=60,
            jti_replay_cache_max_entries=1000,
            require_jti=False,
            require_connection_grant=require_connection_grant,
            connection_grant_ttl_seconds=30,
            connection_grant_secret="",
            require_protocol_version_claim=False,
            expected_protocol_version=1,
            jwt_keyring={},
        )

    async def _stop_handlers(self, host_task: asyncio.Task, client_task: asyncio.Task) -> None:
        await self.host_ws.disconnect()
        await self.client_ws.disconnect()
        await asyncio.wait_for(asyncio.gather(host_task, client_task), timeout=2)

    def _sent_types(self, ws: FakeWebSocket) -> list[str]:
        return [m.get("type", "") for m in ws.sent]

    async def test_connect_request_emits_host_endpoint_and_start_punch(self) -> None:
        host_task, client_task = await self._start_handlers()

        await self.host_ws.push(
            {
                "type": "register_host",
                "host_id": "host-1",
                "max_clients": 5,
                "metadata": {"name": "Host1", "listen_port": 49920},
            }
        )
        await self.client_ws.push(
            {
                "type": "register_client",
                "client_id": "client-1",
            }
        )
        await self.client_ws.push(
            {
                "type": "connect_request",
                "client_id": "client-1",
                "host_id": "host-1",
            }
        )

        await asyncio.sleep(0.1)

        host_types = self._sent_types(self.host_ws)
        client_types = self._sent_types(self.client_ws)

        self.assertIn("incoming_client", host_types)
        self.assertIn("start_punch", host_types)
        self.assertIn("host_endpoint", client_types)
        self.assertIn("start_punch", client_types)

        endpoint_msgs = [m for m in self.client_ws.sent if m.get("type") == "host_endpoint"]
        self.assertTrue(endpoint_msgs)
        self.assertEqual(endpoint_msgs[-1].get("host_public_port"), 49920)

        await self._stop_handlers(host_task, client_task)

    async def test_connect_request_client_id_mismatch_rejected(self) -> None:
        host_task, client_task = await self._start_handlers()

        await self.host_ws.push(
            {
                "type": "register_host",
                "host_id": "host-1",
                "max_clients": 5,
                "metadata": {},
            }
        )
        await self.client_ws.push(
            {
                "type": "register_client",
                "client_id": "client-a",
            }
        )
        await self.client_ws.push(
            {
                "type": "connect_request",
                "client_id": "client-b",
                "host_id": "host-1",
            }
        )

        await asyncio.sleep(0.05)

        self.assertTrue(self.client_ws.sent)
        self.assertEqual(self.client_ws.sent[-1].get("type"), "error")
        self.assertEqual(self.client_ws.sent[-1].get("code"), "client_mismatch")

        await self._stop_handlers(host_task, client_task)

    async def test_connect_request_rejected_when_host_full(self) -> None:
        host_task, client_task = await self._start_handlers()

        await self.host_ws.push(
            {
                "type": "register_host",
                "host_id": "host-1",
                "max_clients": 1,
                "metadata": {},
            }
        )
        await self.client_ws.push(
            {
                "type": "register_client",
                "client_id": "client-1",
            }
        )

        await asyncio.sleep(0.05)
        host = await self.state.get_host("host-1")
        self.assertIsNotNone(host)
        assert host is not None
        host.current_clients = host.max_clients

        await self.client_ws.push(
            {
                "type": "connect_request",
                "client_id": "client-1",
                "host_id": "host-1",
            }
        )

        await asyncio.sleep(0.05)

        self.assertTrue(self.client_ws.sent)
        self.assertEqual(self.client_ws.sent[-1].get("type"), "error")
        self.assertEqual(self.client_ws.sent[-1].get("code"), "host_full")

        await self._stop_handlers(host_task, client_task)

    async def test_host_punch_result_requires_registration(self) -> None:
        host_task = asyncio.create_task(
            handlers.handle_host_ws(self.state, self.host_ws, relay_host="relay.test", relay_port=49921)
        )

        await self.host_ws.push(
            {
                "type": "punch_result",
                "success": False,
                "client_id": "client-1",
                "host_id": "host-1",
                "session_id": "sess-1",
            }
        )

        await asyncio.sleep(0.05)

        self.assertTrue(self.host_ws.sent)
        self.assertEqual(self.host_ws.sent[-1].get("type"), "error")
        self.assertEqual(self.host_ws.sent[-1].get("code"), "not_registered")

        await self.host_ws.disconnect()
        await asyncio.wait_for(host_task, timeout=2)

    async def test_client_punch_unknown_session_rejected(self) -> None:
        host_task, client_task = await self._start_handlers()

        await self.client_ws.push(
            {
                "type": "register_client",
                "client_id": "client-1",
            }
        )
        await self.client_ws.push(
            {
                "type": "punch_result",
                "success": False,
                "client_id": "client-1",
                "host_id": "host-1",
                "session_id": "missing-session",
            }
        )

        await asyncio.sleep(0.05)

        self.assertTrue(self.client_ws.sent)
        self.assertEqual(self.client_ws.sent[-1].get("type"), "error")
        self.assertEqual(self.client_ws.sent[-1].get("code"), "not_found")

        await self._stop_handlers(host_task, client_task)

    async def test_client_disconnect_cleans_session_and_host_capacity(self) -> None:
        host_task, client_task = await self._start_handlers()

        await self.host_ws.push(
            {
                "type": "register_host",
                "host_id": "host-1",
                "max_clients": 5,
                "metadata": {},
            }
        )
        await self.client_ws.push(
            {
                "type": "register_client",
                "client_id": "client-1",
            }
        )
        await self.client_ws.push(
            {
                "type": "connect_request",
                "client_id": "client-1",
                "host_id": "host-1",
            }
        )

        await asyncio.sleep(0.1)

        host = await self.state.get_host("host-1")
        self.assertIsNotNone(host)
        assert host is not None
        self.assertEqual(host.current_clients, 1)

        endpoint_msgs = [m for m in self.client_ws.sent if m.get("type") == "host_endpoint"]
        self.assertTrue(endpoint_msgs)
        session_id = endpoint_msgs[-1]["session_id"]
        self.assertIsNotNone(await self.state.get_session(session_id))

        await self.client_ws.disconnect()
        await asyncio.wait_for(client_task, timeout=2)

        host_after = await self.state.get_host("host-1")
        self.assertIsNotNone(host_after)
        assert host_after is not None
        self.assertEqual(host_after.current_clients, 0)
        self.assertIsNone(await self.state.get_session(session_id))

        await self.host_ws.disconnect()
        await asyncio.wait_for(host_task, timeout=2)

    async def test_timeout_triggers_use_relay(self) -> None:
        previous_timeout = handlers.PUNCH_TIMEOUT_SECONDS
        previous_map_timeout = handlers.PORT_MAP_TIMEOUT_SECONDS
        handlers.PUNCH_TIMEOUT_SECONDS = 0.05
        handlers.PORT_MAP_TIMEOUT_SECONDS = 0.05

        host_task, client_task = await self._start_handlers()
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )
            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.2)

            self.assertIn("request_port_map", self._sent_types(self.host_ws))
            self.assertIn("use_relay", self._sent_types(self.host_ws))
            self.assertIn("use_relay", self._sent_types(self.client_ws))
        finally:
            handlers.PUNCH_TIMEOUT_SECONDS = previous_timeout
            handlers.PORT_MAP_TIMEOUT_SECONDS = previous_map_timeout
            await self._stop_handlers(host_task, client_task)

    async def test_force_relay_emits_use_relay_without_port_map_request(self) -> None:
        host_task, client_task = await self._start_handlers(force_relay=True)
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {"listen_port": 49920},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )
            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.1)

            self.assertIn("use_relay", self._sent_types(self.host_ws))
            self.assertIn("use_relay", self._sent_types(self.client_ws))
            self.assertNotIn("request_port_map", self._sent_types(self.host_ws))
        finally:
            await self._stop_handlers(host_task, client_task)

    async def test_port_map_success_emits_retry_host_endpoint(self) -> None:
        host_task, client_task = await self._start_handlers()
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {"listen_port": 49920},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )
            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.05)

            endpoint_msgs = [m for m in self.client_ws.sent if m.get("type") == "host_endpoint"]
            self.assertTrue(endpoint_msgs)
            session_id = endpoint_msgs[-1]["session_id"]

            await self.client_ws.push(
                {
                    "type": "request_port_map",
                    "client_id": "client-1",
                    "host_id": "host-1",
                    "session_id": session_id,
                }
            )

            await asyncio.sleep(0.05)

            request_msgs = [m for m in self.host_ws.sent if m.get("type") == "request_port_map"]
            self.assertTrue(request_msgs)
            self.assertEqual(request_msgs[-1].get("internal_port"), 49920)

            await self.host_ws.push(
                {
                    "type": "port_map_result",
                    "host_id": "host-1",
                    "session_id": session_id,
                    "success": True,
                    "public_ip": "198.51.100.77",
                    "public_port": 62000,
                }
            )

            await asyncio.sleep(0.05)

            endpoint_msgs = [m for m in self.client_ws.sent if m.get("type") == "host_endpoint"]
            self.assertGreaterEqual(len(endpoint_msgs), 2)
            self.assertEqual(endpoint_msgs[-1].get("host_public_ip"), "198.51.100.77")
            self.assertEqual(endpoint_msgs[-1].get("host_public_port"), 62000)
        finally:
            await self._stop_handlers(host_task, client_task)

    async def test_connect_request_rejected_when_connection_grant_missing(self) -> None:
        auth_config = self._auth_config(require_connection_grant=True)
        host_task, client_task = await self._start_handlers(auth_config=auth_config)
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )
            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.05)

            self.assertTrue(self.client_ws.sent)
            self.assertEqual(self.client_ws.sent[-1].get("type"), "error")
            self.assertEqual(self.client_ws.sent[-1].get("code"), "missing_connection_grant")
        finally:
            await self._stop_handlers(host_task, client_task)

    async def test_request_connection_grant_returns_unavailable_when_optional_and_secret_missing(self) -> None:
        auth_config = self._auth_config_without_grant_secret(require_connection_grant=False)
        host_task, client_task = await self._start_handlers(auth_config=auth_config)
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )
            await self.client_ws.push(
                {
                    "type": "request_connection_grant",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.05)

            self.assertTrue(self.client_ws.sent)
            self.assertEqual(self.client_ws.sent[-1].get("type"), "error")
            self.assertEqual(self.client_ws.sent[-1].get("code"), "grant_unavailable")
        finally:
            await self._stop_handlers(host_task, client_task)

    async def test_connect_request_with_valid_connection_grant_succeeds(self) -> None:
        auth_config = self._auth_config(require_connection_grant=True)
        host_task, client_task = await self._start_handlers(auth_config=auth_config)
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )

            await self.client_ws.push(
                {
                    "type": "request_connection_grant",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.05)

            grant_messages = [m for m in self.client_ws.sent if m.get("type") == "connection_grant"]
            self.assertTrue(grant_messages)
            grant_token = grant_messages[-1].get("grant_token", "")
            self.assertTrue(grant_token)
            grant_session_id = grant_messages[-1].get("grant_session_id", "")
            self.assertTrue(grant_session_id)

            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                    "connection_grant_token": grant_token,
                }
            )

            await asyncio.sleep(0.1)

            host_types = self._sent_types(self.host_ws)
            client_types = self._sent_types(self.client_ws)
            self.assertIn("incoming_client", host_types)
            self.assertIn("start_punch", host_types)
            self.assertIn("host_endpoint", client_types)
            self.assertIn("start_punch", client_types)

            endpoint_messages = [m for m in self.client_ws.sent if m.get("type") == "host_endpoint"]
            self.assertTrue(endpoint_messages)
            self.assertEqual(endpoint_messages[-1].get("session_id"), grant_session_id)
        finally:
            await self._stop_handlers(host_task, client_task)

    async def test_replayed_connection_grant_increments_security_metrics(self) -> None:
        auth_config = self._auth_config(require_connection_grant=True)
        host_task, client_task = await self._start_handlers(auth_config=auth_config)
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )

            await self.client_ws.push(
                {
                    "type": "request_connection_grant",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.05)

            grant_messages = [m for m in self.client_ws.sent if m.get("type") == "connection_grant"]
            self.assertTrue(grant_messages)
            grant_token = grant_messages[-1].get("grant_token", "")
            self.assertTrue(grant_token)

            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                    "connection_grant_token": grant_token,
                }
            )
            await asyncio.sleep(0.05)

            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                    "connection_grant_token": grant_token,
                }
            )

            await asyncio.sleep(0.05)

            self.assertEqual(self.client_ws.sent[-1].get("type"), "error")
            self.assertEqual(self.client_ws.sent[-1].get("code"), "invalid_connection_grant")

            snapshot = await self.state.get_statistics_snapshot()
            self.assertGreaterEqual(snapshot["security_metrics"]["handshake_failures"], 1)
            self.assertGreaterEqual(snapshot["security_metrics"]["replay_rejects"], 1)
        finally:
            await self._stop_handlers(host_task, client_task)

    async def test_register_client_rejected_when_claim_subject_mismatch(self) -> None:
        self.client_ws.state.auth_claims = {
            "sub": "client-a",
            "role": "client",
            "scope": "client:* rendezvous:client",
        }
        host_task, client_task = await self._start_handlers()
        try:
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-b",
                }
            )

            await asyncio.sleep(0.05)

            self.assertTrue(self.client_ws.sent)
            self.assertEqual(self.client_ws.sent[-1].get("type"), "error")
            self.assertEqual(self.client_ws.sent[-1].get("code"), "forbidden_claims")

            snapshot = await self.state.get_statistics_snapshot()
            self.assertGreaterEqual(snapshot["security_metrics"]["auth_failures"], 1)
        finally:
            await self._stop_handlers(host_task, client_task)

    async def test_register_host_rejected_when_claim_subject_mismatch(self) -> None:
        self.host_ws.state.auth_claims = {
            "sub": "host-a",
            "role": "host",
            "scope": "host:* rendezvous:host",
        }
        host_task = asyncio.create_task(
            handlers.handle_host_ws(self.state, self.host_ws, relay_host="relay.test", relay_port=49921)
        )
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-b",
                    "max_clients": 5,
                    "metadata": {},
                }
            )

            await asyncio.sleep(0.05)

            self.assertTrue(self.host_ws.sent)
            self.assertEqual(self.host_ws.sent[-1].get("type"), "error")
            self.assertEqual(self.host_ws.sent[-1].get("code"), "forbidden_claims")

            snapshot = await self.state.get_statistics_snapshot()
            self.assertGreaterEqual(snapshot["security_metrics"]["auth_failures"], 1)
        finally:
            await self.host_ws.disconnect()
            await asyncio.wait_for(host_task, timeout=2)

    async def test_direct_success_cancels_timeout_and_avoids_relay(self) -> None:
        previous_timeout = handlers.PUNCH_TIMEOUT_SECONDS
        handlers.PUNCH_TIMEOUT_SECONDS = 0.2

        host_task, client_task = await self._start_handlers()
        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {},
                }
            )
            await self.client_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-1",
                }
            )
            await self.client_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-1",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.02)

            endpoint_msgs = [m for m in self.client_ws.sent if m.get("type") == "host_endpoint"]
            self.assertTrue(endpoint_msgs)
            session_id = endpoint_msgs[-1]["session_id"]

            await self.host_ws.push(
                {
                    "type": "punch_result",
                    "success": True,
                    "client_id": "client-1",
                    "host_id": "host-1",
                    "session_id": session_id,
                }
            )

            await asyncio.sleep(0.25)

            self.assertNotIn("use_relay", self._sent_types(self.host_ws))
            self.assertNotIn("use_relay", self._sent_types(self.client_ws))
        finally:
            handlers.PUNCH_TIMEOUT_SECONDS = previous_timeout
            await self._stop_handlers(host_task, client_task)

    async def test_duplicate_host_registration_replaces_connection(self) -> None:
        first_ws = FakeWebSocket("203.0.113.10", 51000)
        second_ws = FakeWebSocket("203.0.113.11", 51001)

        first_task = asyncio.create_task(
            handlers.handle_host_ws(self.state, first_ws, relay_host="relay.test", relay_port=49921)
        )
        second_task = asyncio.create_task(
            handlers.handle_host_ws(self.state, second_ws, relay_host="relay.test", relay_port=49921)
        )
        try:
            await first_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-dup",
                    "max_clients": 5,
                    "metadata": {"name": "first"},
                }
            )
            await second_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-dup",
                    "max_clients": 3,
                    "metadata": {"name": "second"},
                }
            )

            await asyncio.sleep(0.05)

            host = await self.state.get_host("host-dup")
            self.assertIsNotNone(host)
            assert host is not None
            self.assertIs(host.ws, second_ws)
            self.assertEqual(host.max_clients, 3)
            self.assertEqual(host.metadata.get("name"), "second")
        finally:
            await first_ws.disconnect()
            await second_ws.disconnect()
            await asyncio.wait_for(asyncio.gather(first_task, second_task), timeout=2)

    async def test_duplicate_client_registration_replaces_connection(self) -> None:
        first_ws = FakeWebSocket("198.51.100.22", 52000)
        second_ws = FakeWebSocket("198.51.100.23", 52001)

        first_task = asyncio.create_task(
            handlers.handle_client_ws(self.state, first_ws, relay_host="relay.test", relay_port=49921)
        )
        second_task = asyncio.create_task(
            handlers.handle_client_ws(self.state, second_ws, relay_host="relay.test", relay_port=49921)
        )
        try:
            await first_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-dup",
                }
            )
            await second_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-dup",
                }
            )

            await asyncio.sleep(0.05)

            client = await self.state.get_client("client-dup")
            self.assertIsNotNone(client)
            assert client is not None
            self.assertIs(client.ws, second_ws)
            self.assertEqual(client.public_ip, "198.51.100.23")
            self.assertEqual(client.public_port, 52001)
        finally:
            await first_ws.disconnect()
            await second_ws.disconnect()
            await asyncio.wait_for(asyncio.gather(first_task, second_task), timeout=2)

    async def test_stale_duplicate_client_disconnect_does_not_remove_active_client_or_session(self) -> None:
        host_task = asyncio.create_task(
            handlers.handle_host_ws(self.state, self.host_ws, relay_host="relay.test", relay_port=49921)
        )
        first_ws = FakeWebSocket("198.51.100.22", 52000)
        second_ws = FakeWebSocket("198.51.100.23", 52001)
        first_task = asyncio.create_task(
            handlers.handle_client_ws(self.state, first_ws, relay_host="relay.test", relay_port=49921)
        )
        second_task = asyncio.create_task(
            handlers.handle_client_ws(self.state, second_ws, relay_host="relay.test", relay_port=49921)
        )

        try:
            await self.host_ws.push(
                {
                    "type": "register_host",
                    "host_id": "host-1",
                    "max_clients": 5,
                    "metadata": {},
                }
            )

            await first_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-dup",
                }
            )
            await second_ws.push(
                {
                    "type": "register_client",
                    "client_id": "client-dup",
                }
            )

            await second_ws.push(
                {
                    "type": "connect_request",
                    "client_id": "client-dup",
                    "host_id": "host-1",
                }
            )

            await asyncio.sleep(0.1)

            endpoint_msgs = [m for m in second_ws.sent if m.get("type") == "host_endpoint"]
            self.assertTrue(endpoint_msgs)
            session_id = endpoint_msgs[-1]["session_id"]

            await first_ws.disconnect()
            await asyncio.wait_for(first_task, timeout=2)

            client = await self.state.get_client("client-dup")
            self.assertIsNotNone(client)
            assert client is not None
            self.assertIs(client.ws, second_ws)

            host = await self.state.get_host("host-1")
            self.assertIsNotNone(host)
            assert host is not None
            self.assertEqual(host.current_clients, 1)

            session = await self.state.get_session(session_id)
            self.assertIsNotNone(session)
        finally:
            await self.host_ws.disconnect()
            await second_ws.disconnect()
            await asyncio.wait_for(asyncio.gather(host_task, second_task), timeout=2)


if __name__ == "__main__":
    unittest.main()
