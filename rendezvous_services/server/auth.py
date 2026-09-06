from __future__ import annotations

from dataclasses import dataclass
import threading
import time
from typing import Any
from uuid import uuid4

import jwt


class AuthError(Exception):
    pass


@dataclass(frozen=True)
class AuthConfig:
    require_signed_tokens: bool
    allow_legacy_no_token: bool
    jwt_secret: str
    jwt_issuer: str
    jwt_audience: str
    required_scope_host: str
    required_scope_client: str
    jti_replay_ttl_seconds: int
    jti_replay_cache_max_entries: int
    require_jti: bool
    require_connection_grant: bool
    connection_grant_ttl_seconds: int
    connection_grant_secret: str
    require_protocol_version_claim: bool
    expected_protocol_version: int
    jwt_keyring: dict[str, str]


class _ReplayCache:
    def __init__(self) -> None:
        self._items: dict[str, float] = {}
        self._lock = threading.Lock()

    def _evict_expired(self, now: float) -> None:
        expired = [key for key, expires_at in self._items.items() if expires_at <= now]
        for key in expired:
            self._items.pop(key, None)

    def check_and_store(self, key: str, ttl_seconds: int, max_entries: int) -> bool:
        if ttl_seconds <= 0:
            return True

        now = time.time()
        with self._lock:
            self._evict_expired(now)

            if key in self._items:
                return False

            # Defensive bound to avoid unbounded growth under attack.
            if max_entries > 0 and len(self._items) >= max_entries:
                oldest_key = min(self._items, key=self._items.get)
                self._items.pop(oldest_key, None)

            self._items[key] = now + ttl_seconds
            return True


REPLAY_CACHE = _ReplayCache()


def parse_bearer_token(auth_header: str | None) -> str:
    if not auth_header:
        return ""

    value = auth_header.strip()
    if not value:
        return ""

    prefix = "bearer "
    if value.lower().startswith(prefix):
        return value[len(prefix) :].strip()

    return ""


def extract_websocket_token(websocket: Any) -> str:
    query_token = (websocket.query_params.get("access_token") or "").strip()
    if query_token:
        return query_token

    auth_header = websocket.headers.get("authorization")
    return parse_bearer_token(auth_header)


def _extract_roles(claims: dict[str, Any]) -> set[str]:
    roles: set[str] = set()

    role_value = claims.get("role")
    if isinstance(role_value, str) and role_value.strip():
        roles.add(role_value.strip().lower())

    roles_value = claims.get("roles")
    if isinstance(roles_value, str) and roles_value.strip():
        roles.add(roles_value.strip().lower())
    elif isinstance(roles_value, list):
        for item in roles_value:
            if isinstance(item, str) and item.strip():
                roles.add(item.strip().lower())

    return roles


def token_has_role(claims: dict[str, Any], role: str) -> bool:
    required = role.strip().lower()
    if not required:
        return True
    roles = _extract_roles(claims)
    return required in roles or "admin" in roles


def _extract_scopes(claims: dict[str, Any]) -> set[str]:
    scopes: set[str] = set()

    scope_value = claims.get("scope")
    if isinstance(scope_value, str):
        for item in scope_value.split():
            value = item.strip().lower()
            if value:
                scopes.add(value)
    elif isinstance(scope_value, list):
        for item in scope_value:
            if isinstance(item, str):
                value = item.strip().lower()
                if value:
                    scopes.add(value)

    scopes_value = claims.get("scopes")
    if isinstance(scopes_value, str):
        for item in scopes_value.split():
            value = item.strip().lower()
            if value:
                scopes.add(value)
    elif isinstance(scopes_value, list):
        for item in scopes_value:
            if isinstance(item, str):
                value = item.strip().lower()
                if value:
                    scopes.add(value)

    return scopes


def token_has_scope(claims: dict[str, Any], scope: str) -> bool:
    required = scope.strip().lower()
    if not required:
        return True
    scopes = _extract_scopes(claims)
    return required in scopes


def token_has_any_scope(claims: dict[str, Any], scopes: list[str]) -> bool:
    for scope in scopes:
        if token_has_scope(claims, scope):
            return True
    return False


def _claim_has_expected_protocol_version(claims: dict[str, Any], expected_version: int) -> bool:
    single = claims.get("protocol_version")
    if isinstance(single, int):
        return single == expected_version
    if isinstance(single, str):
        try:
            return int(single.strip()) == expected_version
        except ValueError:
            return False

    many = claims.get("protocol_versions")
    if isinstance(many, list):
        for item in many:
            if isinstance(item, int) and item == expected_version:
                return True
            if isinstance(item, str):
                try:
                    if int(item.strip()) == expected_version:
                        return True
                except ValueError:
                    continue

    return False


def _required_scope_for_role(config: AuthConfig, required_role: str | None) -> str:
    role = (required_role or "").strip().lower()
    if role == "host":
        return config.required_scope_host.strip().lower()
    if role == "client":
        return config.required_scope_client.strip().lower()
    return ""


def validate_access_token(token: str, config: AuthConfig, required_role: str | None = None) -> dict[str, Any]:
    if not token:
        raise AuthError("missing access token")

    secret = (config.jwt_secret or "").strip()
    if config.jwt_keyring:
        try:
            unverified_header = jwt.get_unverified_header(token)
        except jwt.PyJWTError as ex:
            raise AuthError(f"invalid access token header: {ex}") from ex

        kid = str(unverified_header.get("kid", "")).strip()
        if not kid:
            raise AuthError("access token missing required 'kid' header")

        mapped_secret = (config.jwt_keyring.get(kid) or "").strip()
        if not mapped_secret:
            raise AuthError(f"unknown access token kid '{kid}'")

        secret = mapped_secret

    if not secret:
        raise AuthError("jwt secret is not configured")

    decode_kwargs: dict[str, Any] = {
        "algorithms": ["HS256"],
        "options": {"require": ["exp", "iat", "sub"]},
    }

    if config.jwt_issuer:
        decode_kwargs["issuer"] = config.jwt_issuer

    if config.jwt_audience:
        decode_kwargs["audience"] = config.jwt_audience

    try:
        claims = jwt.decode(token, secret, **decode_kwargs)
    except jwt.PyJWTError as ex:
        raise AuthError(f"invalid access token: {ex}") from ex

    if required_role:
        if not token_has_role(claims, required_role):
            raise AuthError(f"required role '{required_role}' not present")

    required_scope = _required_scope_for_role(config, required_role)
    if required_scope:
        if not token_has_any_scope(claims, [required_scope, "rendezvous:*"]):
            raise AuthError(f"required scope '{required_scope}' not present")

    if config.require_protocol_version_claim:
        if not _claim_has_expected_protocol_version(claims, config.expected_protocol_version):
            raise AuthError(
                f"required protocol_version '{config.expected_protocol_version}' not present in token claims"
            )

    jti_value = claims.get("jti")
    jti = jti_value.strip() if isinstance(jti_value, str) else ""
    if config.require_jti and not jti:
        raise AuthError("required claim 'jti' not present")

    if jti:
        replay_key = f"{claims.get('sub', '')}:{jti}"
        is_new = REPLAY_CACHE.check_and_store(
            replay_key,
            ttl_seconds=config.jti_replay_ttl_seconds,
            max_entries=config.jti_replay_cache_max_entries,
        )
        if not is_new:
            raise AuthError("replayed token rejected")

    return claims


def issue_connection_grant_token(config: AuthConfig, client_id: str, host_id: str, grant_session_id: str) -> str:
    secret = (config.connection_grant_secret or config.jwt_secret).strip()
    if not secret:
        raise AuthError("connection grant secret is not configured")

    ttl = max(1, min(600, config.connection_grant_ttl_seconds))
    now = int(time.time())
    payload: dict[str, Any] = {
        "sub": client_id,
        "host_id": host_id,
        "session_id": grant_session_id,
        "purpose": "connect_grant",
        "iat": now,
        "nbf": now,
        "exp": now + ttl,
        "jti": f"grant-{uuid4().hex}",
    }
    if config.jwt_issuer:
        payload["iss"] = config.jwt_issuer
    if config.jwt_audience:
        payload["aud"] = config.jwt_audience

    return jwt.encode(payload, secret, algorithm="HS256")


def validate_connection_grant_token(
    grant_token: str,
    config: AuthConfig,
    client_id: str,
    host_id: str,
) -> dict[str, Any]:
    if not grant_token:
        raise AuthError("missing connection grant token")

    secret = (config.connection_grant_secret or config.jwt_secret).strip()
    if not secret:
        raise AuthError("connection grant secret is not configured")

    decode_kwargs: dict[str, Any] = {
        "algorithms": ["HS256"],
        "options": {"require": ["exp", "iat", "sub", "jti", "purpose", "session_id"]},
    }

    if config.jwt_issuer:
        decode_kwargs["issuer"] = config.jwt_issuer

    if config.jwt_audience:
        decode_kwargs["audience"] = config.jwt_audience

    try:
        claims = jwt.decode(grant_token, secret, **decode_kwargs)
    except jwt.PyJWTError as ex:
        raise AuthError(f"invalid connection grant token: {ex}") from ex

    if str(claims.get("purpose", "")).strip().lower() != "connect_grant":
        raise AuthError("invalid connection grant purpose")

    if str(claims.get("sub", "")).strip() != client_id:
        raise AuthError("connection grant client mismatch")

    if str(claims.get("host_id", "")).strip() != host_id:
        raise AuthError("connection grant host mismatch")

    grant_session_id = str(claims.get("session_id", "")).strip()
    if not grant_session_id:
        raise AuthError("connection grant session_id is missing")

    jti = str(claims.get("jti", "")).strip()
    if not jti:
        raise AuthError("required claim 'jti' not present")

    is_new = REPLAY_CACHE.check_and_store(
        f"grant:{client_id}:{host_id}:{jti}",
        ttl_seconds=max(1, config.connection_grant_ttl_seconds),
        max_entries=config.jti_replay_cache_max_entries,
    )
    if not is_new:
        raise AuthError("replayed connection grant rejected")

    return claims


def authorize_websocket(websocket: Any, config: AuthConfig, required_role: str) -> tuple[bool, int, str, dict[str, Any] | None]:
    token = extract_websocket_token(websocket)

    if not token:
        if config.require_signed_tokens:
            return False, 4401, "missing access token", None
        if not config.allow_legacy_no_token:
            return False, 4401, "token required by policy", None
        return True, 1000, "ok", None

    try:
        claims = validate_access_token(token, config, required_role=required_role)
    except AuthError as ex:
        return False, 4403, str(ex), None

    return True, 1000, "ok", claims
