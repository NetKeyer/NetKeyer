from __future__ import annotations

import asyncio
import logging
from typing import Any
from uuid import uuid4

from fastapi import WebSocket, WebSocketDisconnect

from .auth import (
    AuthConfig,
    AuthError,
    issue_connection_grant_token,
    token_has_any_scope,
    token_has_role,
    validate_connection_grant_token,
)
from .models import (
    ConnectionGrantMessage,
    ClientRequestPortMapMessage,
    ConnectRequestMessage,
    ErrorMessage,
    HostEndpointMessage,
    HostPortMapResultMessage,
    HostListMessage,
    HostPunchResultMessage,
    HostSummary,
    IncomingClientMessage,
    ListHostsMessage,
    RequestConnectionGrantMessage,
    RequestPortMapMessage,
    RegisterClientMessage,
    RegisterHostMessage,
    StartPunchMessage,
    UseRelayMessage,
    is_validation_error,
    validate_client_inbound,
    validate_host_inbound,
)
from .state import DuplicateRegistrationError, RendezvousState


PUNCH_TIMEOUT_SECONDS = 2
PORT_MAP_TIMEOUT_SECONDS = 4
DEFAULT_RELAY_HOST = "relay"
DEFAULT_RELAY_PORT = 49921
LOGGER = logging.getLogger("netkeyer.rendezvous.ws")


def _claims_for_ws(ws: WebSocket) -> dict[str, Any]:
    state = getattr(ws, "state", None)
    claims = getattr(state, "auth_claims", None)
    if isinstance(claims, dict):
        return claims
    return {}


def _is_claims_admin(claims: dict[str, Any]) -> bool:
    return token_has_role(claims, "admin")


def _check_host_claims_for_host_id(claims: dict[str, Any], host_id: str) -> bool:
    if not claims:
        return True
    if _is_claims_admin(claims):
        return True

    token_sub = str(claims.get("sub", "")).strip()
    if token_sub != host_id:
        return False

    return token_has_any_scope(
        claims,
        [
            f"host:{host_id}",
            "host:*",
            "rendezvous:host",
            "rendezvous:*",
        ],
    )


def _check_client_claims_for_client_id(claims: dict[str, Any], client_id: str) -> bool:
    if not claims:
        return True
    if _is_claims_admin(claims):
        return True

    token_sub = str(claims.get("sub", "")).strip()
    if token_sub != client_id:
        return False

    return token_has_any_scope(
        claims,
        [
            f"client:{client_id}",
            "client:*",
            "rendezvous:client",
            "rendezvous:*",
        ],
    )


def _check_client_claims_for_target_host(claims: dict[str, Any], host_id: str) -> bool:
    if not claims:
        return True
    if _is_claims_admin(claims):
        return True

    return token_has_any_scope(
        claims,
        [
            f"target_host:{host_id}",
            f"host:{host_id}",
            "target_host:*",
            "host:*",
            "rendezvous:connect",
            "rendezvous:*",
        ],
    )


def _model_dump(model: Any) -> dict[str, Any]:
    if hasattr(model, "model_dump"):
        return model.model_dump(exclude_none=True)
    return model.dict(exclude_none=True)


async def _send_model(ws: WebSocket, model: Any) -> None:
    await ws.send_json(_model_dump(model))


async def _send_error(ws: WebSocket, code: str, message: str, session_id: str | None = None) -> None:
    err = ErrorMessage(type="error", code=code, message=message, session_id=session_id)
    await _send_model(ws, err)


def _peer_endpoint(ws: WebSocket) -> tuple[str, int]:
    client = ws.client
    if client is None:
        return "0.0.0.0", 0
    return client.host, client.port


async def _send_relay_to_both(state: RendezvousState, session_id: str, relay_host: str, relay_port: int) -> None:
    session = await state.get_session(session_id)
    if not session:
        return

    host = await state.get_host(session.host_id)
    client = await state.get_client(session.client_id)

    advertised_relay_host = _resolve_advertised_relay_host(relay_host, host.ws if host else None, client.ws if client else None)

    msg = UseRelayMessage(
        type="use_relay",
        relay_host=advertised_relay_host,
        relay_port=relay_port,
        session_id=session_id,
    )

    if host:
        await _send_model(host.ws, msg)
    if client:
        await _send_model(client.ws, msg)


def _resolve_advertised_relay_host(configured_relay_host: str, host_ws: WebSocket | None, client_ws: WebSocket | None) -> str:
    candidate = (configured_relay_host or "").strip()
    if candidate and candidate.lower() != "relay":
        return candidate

    # Fall back to the host header used by external app websocket clients.
    for ws in (host_ws, client_ws):
        if ws is None:
            continue

        host_header = ws.headers.get("x-forwarded-host") or ws.headers.get("host")
        parsed = _extract_host_name(host_header)
        if parsed:
            return parsed

    return candidate or DEFAULT_RELAY_HOST


def _extract_host_name(host_header: str | None) -> str:
    if not host_header:
        return ""

    # Header may be a CSV list; use the first host.
    first = host_header.split(",", 1)[0].strip()
    if not first:
        return ""

    # IPv6 host with brackets, possibly including :port.
    if first.startswith("["):
        end = first.find("]")
        if end > 1:
            return first[1:end]

    # Standard host:port form.
    if ":" in first:
        host, _ = first.rsplit(":", 1)
        return host.strip()

    return first


def _resolve_host_endpoint_port(metadata: dict[str, Any], fallback_port: int) -> int:
    if not metadata:
        return fallback_port

    value = metadata.get("listen_port")
    try:
        parsed = int(value)
    except (TypeError, ValueError):
        return fallback_port

    if 1 <= parsed <= 65535:
        return parsed

    return fallback_port


async def _punch_timeout_watchdog(state: RendezvousState, session_id: str, relay_host: str, relay_port: int) -> None:
    try:
        await asyncio.sleep(PUNCH_TIMEOUT_SECONDS)
        await _request_port_map_for_session(state, session_id, relay_host=relay_host, relay_port=relay_port)
    except asyncio.CancelledError:
        return


async def _port_map_timeout_watchdog(state: RendezvousState, session_id: str, relay_host: str, relay_port: int) -> None:
    try:
        await asyncio.sleep(PORT_MAP_TIMEOUT_SECONDS)
        session = await state.mark_relay_requested(session_id)
        if not session:
            return
        if session.state == "relay_requested":
            await _send_relay_to_both(state, session_id, relay_host, relay_port)
    except asyncio.CancelledError:
        return


async def _request_port_map_for_session(state: RendezvousState, session_id: str, relay_host: str, relay_port: int) -> None:
    session = await state.mark_map_requested(session_id)
    if not session:
        return

    if session.state != "map_requested":
        return

    host = await state.get_host(session.host_id)
    if not host:
        relay_state = await state.mark_relay_requested(session_id)
        if relay_state and relay_state.state == "relay_requested":
            await _send_relay_to_both(state, session_id, relay_host, relay_port)
        return

    listen_port = _resolve_host_endpoint_port(host.metadata, host.public_port)
    await _send_model(
        host.ws,
        RequestPortMapMessage(
            type="request_port_map",
            session_id=session_id,
            internal_port=listen_port,
        ),
    )

    timeout_task = asyncio.create_task(
        _port_map_timeout_watchdog(state, session_id, relay_host=relay_host, relay_port=relay_port)
    )
    await state.attach_timeout_task(session_id, timeout_task)


async def handle_host_ws(state: RendezvousState, websocket: WebSocket, relay_host: str, relay_port: int) -> None:
    await websocket.accept()
    host_id: str | None = None
    claims = _claims_for_ws(websocket)

    try:
        while True:
            payload = await websocket.receive_json()
            try:
                msg = validate_host_inbound(payload)
            except Exception as exc:
                if is_validation_error(exc):
                    await _send_error(websocket, "invalid_payload", "Invalid host message payload")
                else:
                    await _send_error(websocket, "unsupported_message_type", str(exc))
                continue

            if isinstance(msg, RegisterHostMessage):
                if not _check_host_claims_for_host_id(claims, msg.host_id):
                    await state.record_security_failure(auth_failure=True, detail="forbidden host registration claims")
                    await _send_error(websocket, "forbidden_claims", "Token claims do not allow this host registration")
                    continue

                ip, port = _peer_endpoint(websocket)
                try:
                    await state.register_host(
                        host_id=msg.host_id,
                        ws=websocket,
                        public_ip=ip,
                        public_port=port,
                        max_clients=msg.max_clients,
                        metadata=msg.metadata,
                    )
                except DuplicateRegistrationError:
                    await _send_error(websocket, "duplicate_host_id", f"Host ID '{msg.host_id}' is already registered")
                    LOGGER.warning("host_registration_rejected_duplicate host_id=%s peer=%s:%s", msg.host_id, ip, port)
                    await websocket.close(code=1008, reason="duplicate host_id")
                    return
                host_id = msg.host_id
                LOGGER.info("host_registered host_id=%s peer=%s:%s max_clients=%s", host_id, ip, port, msg.max_clients)
                continue

            if isinstance(msg, HostPunchResultMessage):
                if not host_id:
                    await _send_error(websocket, "not_registered", "Host must register before sending punch_result")
                    continue
                if msg.host_id != host_id:
                    await _send_error(websocket, "host_mismatch", "Host ID does not match registered host", msg.session_id)
                    continue

                session = await state.get_session(msg.session_id)
                if not session:
                    await _send_error(websocket, "not_found", "Unknown session", msg.session_id)
                    continue
                if session.host_id != host_id or session.client_id != msg.client_id:
                    await _send_error(websocket, "session_mismatch", "Session ownership mismatch", msg.session_id)
                    continue

                session = await state.update_punch_result(
                    session_id=msg.session_id,
                    from_host=True,
                    success=msg.success,
                )

                if session.state == "relay_requested":
                    await _send_relay_to_both(state, msg.session_id, relay_host, relay_port)
                continue

            if isinstance(msg, HostPortMapResultMessage):
                if not host_id:
                    await _send_error(websocket, "not_registered", "Host must register before sending port_map_result")
                    continue
                if msg.host_id != host_id:
                    await _send_error(websocket, "host_mismatch", "Host ID does not match registered host", msg.session_id)
                    continue

                session = await state.get_session(msg.session_id)
                if not session:
                    await _send_error(websocket, "not_found", "Unknown session", msg.session_id)
                    continue
                if session.host_id != host_id:
                    await _send_error(websocket, "session_mismatch", "Session ownership mismatch", msg.session_id)
                    continue

                if msg.success and msg.public_port:
                    updated = await state.set_mapped_endpoint(msg.session_id, msg.public_ip, msg.public_port)
                    if not updated:
                        continue

                    client = await state.get_client(updated.client_id)
                    host = await state.get_host(updated.host_id)
                    if not client or not host:
                        continue

                    mapped_ip = (msg.public_ip or "").strip() or host.public_ip
                    await _send_model(
                        client.ws,
                        HostEndpointMessage(
                            type="host_endpoint",
                            host_id=updated.host_id,
                            host_public_ip=mapped_ip,
                            host_public_port=msg.public_port,
                            session_id=updated.session_id,
                        ),
                    )
                    continue

                relay_state = await state.mark_relay_requested(msg.session_id)
                if relay_state and relay_state.state == "relay_requested":
                    await _send_relay_to_both(state, msg.session_id, relay_host, relay_port)

    except WebSocketDisconnect:
        pass
    finally:
        if host_id:
            host = await state.get_host(host_id)
            if host and host.ws is websocket:
                LOGGER.info("host_unregistering host_id=%s reason=socket_closed", host_id)
                await state.close_sessions_for_host(host_id)
                await state.unregister_host(host_id)
            else:
                LOGGER.info("host_unregister_skipped host_id=%s reason=socket_replaced", host_id)
        await state.unregister_host_by_ws(websocket)


async def handle_client_ws(
    state: RendezvousState,
    websocket: WebSocket,
    relay_host: str,
    relay_port: int,
    force_relay: bool = False,
    auth_config: AuthConfig | None = None,
) -> None:
    await websocket.accept()
    client_id: str | None = None
    claims = _claims_for_ws(websocket)

    try:
        while True:
            payload = await websocket.receive_json()
            try:
                msg = validate_client_inbound(payload)
            except Exception as exc:
                if is_validation_error(exc):
                    await _send_error(websocket, "invalid_payload", "Invalid client message payload")
                else:
                    await _send_error(websocket, "unsupported_message_type", str(exc))
                continue

            if isinstance(msg, RegisterClientMessage):
                if not _check_client_claims_for_client_id(claims, msg.client_id):
                    await state.record_security_failure(auth_failure=True, detail="forbidden client registration claims")
                    await _send_error(websocket, "forbidden_claims", "Token claims do not allow this client registration")
                    continue

                ip, port = _peer_endpoint(websocket)
                try:
                    await state.register_client(
                        client_id=msg.client_id,
                        ws=websocket,
                        public_ip=ip,
                        public_port=port,
                    )
                except DuplicateRegistrationError:
                    await _send_error(websocket, "duplicate_client_id", f"Client ID '{msg.client_id}' is already registered")
                    LOGGER.warning("client_registration_rejected_duplicate client_id=%s peer=%s:%s", msg.client_id, ip, port)
                    await websocket.close(code=1008, reason="duplicate client_id")
                    return
                client_id = msg.client_id
                LOGGER.info("client_registered client_id=%s peer=%s:%s", client_id, ip, port)
                continue

            if not client_id:
                await _send_error(websocket, "not_registered", "Client must register before this operation")
                continue

            if isinstance(msg, ListHostsMessage):
                hosts = await state.list_hosts_for_client()
                host_summaries = [
                    HostSummary(
                        host_id=h.host_id,
                        metadata=h.metadata,
                        current_clients=h.current_clients,
                        max_clients=h.max_clients,
                    )
                    for h in hosts
                ]
                await _send_model(websocket, HostListMessage(type="host_list", hosts=host_summaries))
                continue

            if isinstance(msg, RequestConnectionGrantMessage):
                if msg.client_id != client_id:
                    await _send_error(websocket, "client_mismatch", "Client ID does not match registered client")
                    continue
                if not _check_client_claims_for_client_id(claims, msg.client_id):
                    await state.record_security_failure(auth_failure=True, detail="forbidden client grant request claims")
                    await _send_error(websocket, "forbidden_claims", "Token claims do not allow this client grant request")
                    continue
                if not _check_client_claims_for_target_host(claims, msg.host_id):
                    await state.record_security_failure(auth_failure=True, detail="forbidden target host claims")
                    await _send_error(websocket, "forbidden_claims", "Token claims do not allow connecting to this host")
                    continue

                host = await state.get_host(msg.host_id)
                if not host:
                    await _send_error(websocket, "not_found", f"Host {msg.host_id} not found")
                    continue

                if auth_config is None:
                    await _send_error(websocket, "grant_unavailable", "Connection grant service is unavailable")
                    continue

                try:
                    grant_session_id = uuid4().hex
                    grant_token = issue_connection_grant_token(auth_config, msg.client_id, msg.host_id, grant_session_id)
                except AuthError as ex:
                    await state.record_security_failure(handshake_failure=True, detail=str(ex))
                    if not auth_config.require_connection_grant:
                        await _send_error(websocket, "grant_unavailable", str(ex))
                    else:
                        await _send_error(websocket, "grant_issue_failed", str(ex))
                    continue

                ttl = max(1, min(600, auth_config.connection_grant_ttl_seconds))
                await _send_model(
                    websocket,
                    ConnectionGrantMessage(
                        type="connection_grant",
                        client_id=msg.client_id,
                        host_id=msg.host_id,
                        grant_session_id=grant_session_id,
                        grant_token=grant_token,
                        expires_in_seconds=ttl,
                    ),
                )
                continue

            if isinstance(msg, ConnectRequestMessage):
                if msg.client_id != client_id:
                    await _send_error(websocket, "client_mismatch", "Client ID does not match registered client")
                    continue
                if not _check_client_claims_for_client_id(claims, msg.client_id):
                    await state.record_security_failure(auth_failure=True, detail="forbidden client connect claims")
                    await _send_error(websocket, "forbidden_claims", "Token claims do not allow this client connect request")
                    continue
                if not _check_client_claims_for_target_host(claims, msg.host_id):
                    await state.record_security_failure(auth_failure=True, detail="forbidden target host connect claims")
                    await _send_error(websocket, "forbidden_claims", "Token claims do not allow connecting to this host")
                    continue

                grant_session_id: str | None = None

                if auth_config and auth_config.require_connection_grant:
                    token = (msg.connection_grant_token or "").strip()
                    if not token:
                        await state.record_security_failure(handshake_failure=True, detail="missing connection grant token")
                        await _send_error(websocket, "missing_connection_grant", "Connection grant token is required")
                        continue

                    try:
                        grant_claims = validate_connection_grant_token(token, auth_config, msg.client_id, msg.host_id)
                        grant_session_id = str(grant_claims.get("session_id", "")).strip() or None
                    except AuthError as ex:
                        await state.record_security_failure(handshake_failure=True, detail=str(ex))
                        await _send_error(websocket, "invalid_connection_grant", str(ex))
                        continue

                host = await state.get_host(msg.host_id)
                client = await state.get_client(msg.client_id)

                if not host:
                    await _send_error(websocket, "not_found", f"Host {msg.host_id} not found")
                    continue
                if not client:
                    await _send_error(websocket, "not_found", f"Client {msg.client_id} not registered")
                    continue
                if host.current_clients >= host.max_clients:
                    await _send_error(websocket, "host_full", f"Host {msg.host_id} is at max capacity")
                    continue

                try:
                    session = await state.create_session(
                        host_id=msg.host_id,
                        client_id=msg.client_id,
                        session_id=grant_session_id,
                    )
                except ValueError:
                    await state.record_security_failure(handshake_failure=True, detail="connection grant session id already used")
                    await _send_error(websocket, "invalid_connection_grant", "connection grant session id already used")
                    continue

                LOGGER.info(
                    "connect_request_accepted client_id=%s host_id=%s session_id=%s grant_bound=%s force_relay=%s",
                    msg.client_id,
                    msg.host_id,
                    session.session_id,
                    bool(grant_session_id),
                    force_relay,
                )

                await _send_model(
                    host.ws,
                    IncomingClientMessage(
                        type="incoming_client",
                        client_id=msg.client_id,
                        client_public_ip=client.public_ip,
                        client_public_port=client.public_port,
                        session_id=session.session_id,
                    ),
                )

                await _send_model(
                    websocket,
                    HostEndpointMessage(
                        type="host_endpoint",
                        host_id=msg.host_id,
                        host_public_ip=host.public_ip,
                        host_public_port=_resolve_host_endpoint_port(host.metadata, host.public_port),
                        session_id=session.session_id,
                    ),
                )

                start_msg = StartPunchMessage(type="start_punch", session_id=session.session_id)
                await _send_model(host.ws, start_msg)
                await _send_model(websocket, start_msg)

                LOGGER.info(
                    "connect_request_signaled session_id=%s host_id=%s client_id=%s endpoint=%s:%s",
                    session.session_id,
                    session.host_id,
                    session.client_id,
                    host.public_ip,
                    _resolve_host_endpoint_port(host.metadata, host.public_port),
                )

                if force_relay:
                    relay_state = await state.mark_relay_requested(session.session_id)
                    if relay_state and relay_state.state == "relay_requested":
                        await _send_relay_to_both(state, session.session_id, relay_host, relay_port)
                    continue

                timeout_task = asyncio.create_task(
                    _punch_timeout_watchdog(state, session.session_id, relay_host=relay_host, relay_port=relay_port)
                )
                await state.attach_timeout_task(session.session_id, timeout_task)
                continue

            if isinstance(msg, ClientRequestPortMapMessage):
                if msg.client_id != client_id:
                    await _send_error(websocket, "client_mismatch", "Client ID does not match registered client", msg.session_id)
                    continue

                session = await state.get_session(msg.session_id)
                if not session:
                    await _send_error(websocket, "not_found", "Unknown session", msg.session_id)
                    continue
                if session.client_id != client_id or session.host_id != msg.host_id:
                    await _send_error(websocket, "session_mismatch", "Session ownership mismatch", msg.session_id)
                    continue

                await _request_port_map_for_session(state, msg.session_id, relay_host=relay_host, relay_port=relay_port)
                continue

            # Client punch result
            if msg.client_id != client_id:
                await _send_error(websocket, "client_mismatch", "Client ID does not match registered client", msg.session_id)
                continue

            session = await state.get_session(msg.session_id)
            if not session:
                await _send_error(websocket, "not_found", "Unknown session", msg.session_id)
                continue
            if session.client_id != client_id or session.host_id != msg.host_id:
                await _send_error(websocket, "session_mismatch", "Session ownership mismatch", msg.session_id)
                continue

            session = await state.update_punch_result(
                session_id=msg.session_id,
                from_host=False,
                success=msg.success,
            )

            if session.state == "relay_requested":
                await _send_relay_to_both(state, msg.session_id, relay_host, relay_port)

    except WebSocketDisconnect:
        pass
    finally:
        if client_id:
            client = await state.get_client(client_id)
            if client and client.ws is websocket:
                LOGGER.info("client_unregistering client_id=%s reason=socket_closed", client_id)
                await state.close_sessions_for_client(client_id)
                await state.unregister_client(client_id)
            else:
                LOGGER.info("client_unregister_skipped client_id=%s reason=socket_replaced", client_id)
        await state.unregister_client_by_ws(websocket)
