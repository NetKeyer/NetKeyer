# NetKeyer Rendezvous Services

<!-- markdownlint-disable MD007 MD029 MD032 -->

This package provides the network services used by NetKeyer remote mode.

It includes two containers:

- `netkeyer-rendezvous`: control-plane service (HTTP + WebSocket) for host registration, host discovery, connection coordination, and relay fallback signaling.
- `netkeyer-relay`: data-plane TCP relay used when direct or mapped-direct connectivity is unavailable.

The rendezvous health endpoint is available at `/health` on port `49920`.

Phase 1 security work has started with an nginx TLS ingress overlay for controlled testing.

Security runbooks are documented in `../docs/security/phase5-operations-runbook.md`.

## Release Note

- Rendezvous release artifacts now include `scripts/manage-jwt-keyring.py` so operators can manage JWT keyring entries directly from the extracted deployment bundle.

## Prerequisites

- Python 3.11+
- Docker
- Docker Compose

## Network and Router Preparation

Map router ports to the system running these containers:

- TCP `49920` -> rendezvous service host
- TCP `49921` -> relay service host
- TCP `49922` reserved for a future release feature

Manual static forwarding is the recommended deployment mode.

For compatibility testing with desktop client `v2.1.34`, keep direct service exposure (`49920`/`49921`) enabled while validating the new TLS ingress path in parallel.

## Installation and Deployment

### 1. Unzip the distribution bundle

Extract the release zip to:

- Linux/macOS: `$HOME/rendezvous_services`
- Windows PowerShell: `$HOME\rendezvous_services`

Examples:

Linux/macOS:

```bash
mkdir -p "$HOME/rendezvous_services"
unzip -o netkeyer-rendezvous-services-<version>.zip -d "$HOME/rendezvous_services"
```

Windows PowerShell:

```powershell
New-Item -ItemType Directory -Force -Path "$HOME\rendezvous_services" | Out-Null
Expand-Archive -LiteralPath .\netkeyer-rendezvous-services-<version>.zip -DestinationPath "$HOME\rendezvous_services" -Force
```

Overwrite behavior notes:

- `Expand-Archive` only overwrites when `-Force` is used.
- `unzip` should use `-o` for non-interactive overwrite behavior.

### Upgrade Deployment (replace existing files)

For upgrades, always force overwrite during extraction before restarting containers.

Linux/macOS:

```bash
unzip -o netkeyer-rendezvous-services-<new-version>.zip -d "$HOME/rendezvous_services"
```

Windows PowerShell:

```powershell
Expand-Archive -LiteralPath .\netkeyer-rendezvous-services-<new-version>.zip -DestinationPath "$HOME\rendezvous_services" -Force
```

### 2. Change to the deployment directory

Linux/macOS:

```bash
cd "$HOME/rendezvous_services"
```

Windows PowerShell:

```powershell
Set-Location "$HOME\rendezvous_services"
```

### 3. Start services

```bash
docker compose -f docker-compose.yml up -d --build --force-recreate
```

### 3a. Optional: Start nginx TLS ingress overlay (Phase 1)

Use this during Phase 1 secure-ingress testing.

1. Place certificates in `nginx/certs`:
  - `nginx/certs/fullchain.pem`
  - `nginx/certs/privkey.pem`
2. Start base services plus nginx overlay:

```bash
docker compose -f docker-compose.yml -f docker-compose.nginx.yml up -d --build --force-recreate
```

External endpoints with overlay enabled:

- `https://<your-host>/health`
- `wss://<your-host>/ws/client`
- `wss://<your-host>/ws/host`

HTTP on port `80` is redirected to HTTPS. Plain websocket ingress should be treated as compatibility-only and disabled after migration.

### 3b. Ubuntu Let\'s Encrypt setup (zero-downtime renewal layout)

Use this when you have a public DNS name pointed at this host.

1. Install certbot on Ubuntu:

```bash
sudo apt update
sudo apt install -y certbot
```

2. Prepare ACME webroot directory used by nginx:

```bash
sudo mkdir -p /var/www/certbot/.well-known/acme-challenge
sudo chown -R $USER:$USER /var/www/certbot
```

3. Start services with nginx overlay so challenge files are served on port 80:

```bash
docker compose -f docker-compose.yml -f docker-compose.nginx.yml up -d --build --force-recreate
```

4. Request certificate using HTTP-01 webroot challenge:

```bash
sudo certbot certonly --webroot -w /var/www/certbot -d your.domain.example --email you@example.com --agree-tos --no-eff-email
```

5. Link live certbot files to nginx certificate paths expected by this stack:

```bash
ln -sf /etc/letsencrypt/live/your.domain.example/fullchain.pem ./nginx/certs/fullchain.pem
ln -sf /etc/letsencrypt/live/your.domain.example/privkey.pem ./nginx/certs/privkey.pem
```

6. Reload nginx in-place (no container restart required):

```bash
docker compose -f docker-compose.yml -f docker-compose.nginx.yml exec nginx nginx -s reload
```

7. Verify:

```bash
curl -I https://your.domain.example/health
```

Automatic renewal (zero downtime):

```bash
sudo certbot renew --deploy-hook 'cd /path/to/rendezvous_services && docker compose -f docker-compose.yml -f docker-compose.nginx.yml exec nginx nginx -s reload'
```

Optional dry run:

```bash
sudo certbot renew --dry-run --deploy-hook 'cd /path/to/rendezvous_services && docker compose -f docker-compose.yml -f docker-compose.nginx.yml exec nginx nginx -s reload'
```

### 3c. Automated certificate renewal with systemd (Ubuntu)

The repository includes ready-to-use automation assets:

- `scripts/renew-certs.sh`
- `scripts/reload-nginx-certs.sh`
- `scripts/manage-jwt-keyring.py`
- `systemd/netkeyer-certbot-renew.service`
- `systemd/netkeyer-certbot-renew.timer`

JWT keyring management helper:

- Interactive keyring editor with menu options to list, add, change, and delete key entries.
- Automatically generates a new 32-byte hex secret when adding or changing entries.
- Saves rotating backups before each write:
  - `jwt_keys.json.bak1` (most recent previous)
  - `jwt_keys.json.bak2`
  - `jwt_keys.json.bak3`

Run it from the `rendezvous_services` directory:

```bash
python3 scripts/manage-jwt-keyring.py
```

Optional custom keyring path:

```bash
python3 scripts/manage-jwt-keyring.py --file /path/to/jwt_keys.json
```

Install and enable automation:

1. Ensure scripts are executable:

```bash
cd /path/to/rendezvous_services
chmod +x scripts/renew-certs.sh scripts/reload-nginx-certs.sh
```

2. Copy systemd unit files:

```bash
sudo cp systemd/netkeyer-certbot-renew.service /etc/systemd/system/
sudo cp systemd/netkeyer-certbot-renew.timer /etc/systemd/system/
```

3. If your deployment path is not `/opt/rendezvous_services`, edit:

```bash
sudo systemctl edit --full netkeyer-certbot-renew.service
```

Update `WorkingDirectory`, `NETKEYER_RENDEZVOUS_DIR`, and `ExecStart` to your actual path.

4. Enable and start the timer:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now netkeyer-certbot-renew.timer
```

5. Verify timer and run a manual test:

```bash
systemctl list-timers netkeyer-certbot-renew.timer
sudo systemctl start netkeyer-certbot-renew.service
sudo journalctl -u netkeyer-certbot-renew.service -n 100 --no-pager
```

The renew service runs certbot renewal using webroot challenge and triggers an in-place nginx reload only when certificates are updated.

PR-2 defaults now apply when using nginx ingress:

- Request guards and rate limits are enabled for websocket and API traffic.
- `/health` is ACL-restricted to loopback/private source ranges by default.
- Security-oriented nginx access logs include explicit limit signals (`limit_req` and status `429`/`403`) for throttle/deny visibility.

Force relay experiment flags (latency testing):

- Server flag: `RENDEZVOUS_FORCE_RELAY=true`
  - Sends relay instructions immediately during rendezvous connect flow.
  - Reduces variability from punch and automatic port-map attempts.
- App flag: `NETKEYER_FORCE_RELAY_TRANSPORT=true`
  - Client skips direct and mapped-direct transport attempts.
  - Client waits for relay endpoint and connects relay path only.

Use both flags together when collecting controlled relay-only latency measurements.

Phase 2 auth controls (JWT rollout):

- `RENDEZVOUS_SECURITY_STAGE=compat`
  - Progressive enforcement profile. Stages:
    - `compat`: tokenless compatibility defaults (`require_signed=false`, `allow_legacy=true`, `require_connection_grant=false`, `require_jti=false`, `require_protocol_claim=false`)
    - `tokens`: signed-token defaults (`require_signed=true`, `allow_legacy=false`)
    - `grants`: token defaults plus connection-grant enforcement (`require_connection_grant=true`, `require_jti=true`)
    - `strict`: grants stage plus protocol claim enforcement (`require_protocol_claim=true`)
  - Any explicit per-flag env var below overrides stage defaults.

- `RENDEZVOUS_REQUIRE_SIGNED_TOKENS=true`
  - Enforces signed JWT authentication on websocket rendezvous endpoints.
- `RENDEZVOUS_AUTH_ALLOW_LEGACY_NO_TOKEN=true`
  - Compatibility toggle for staged rollout; tokenless clients can still connect when enforcement is off.
- `RENDEZVOUS_JWT_SECRET=<secret>`
  - Shared secret used for `HS256` signature validation.
- `RENDEZVOUS_JWT_KEYS_FILE=/app/jwt_keys.json` (recommended)
  - Preferred keyring source loaded at startup from a read-only mounted JSON file.
  - Default compose mapping mounts `./jwt_keys.json` from the `rendezvous_services` directory to `/app/jwt_keys.json` in the container.
  - File format must be a JSON object of `{ "kid": "secret" }` pairs.
- `RENDEZVOUS_JWT_KEYS_JSON={"kid-a":"secret-a","kid-b":"secret-b"}` (optional)
  - Fallback keyring source used only when `RENDEZVOUS_JWT_KEYS_FILE` is missing, unreadable, or invalid.
  - When keyring entries are loaded, access tokens must include a JWT header `kid` that maps to one of these secrets.
  - Allows per-user/per-device key distribution and selective key revocation without rotating every client at once.
- `RENDEZVOUS_JWT_ISSUER=<issuer>` (optional)
  - If set, token `iss` must match.
- `RENDEZVOUS_JWT_AUDIENCE=<audience>` (optional)
  - If set, token `aud` must match.
- `RENDEZVOUS_JWT_REQUIRED_SCOPE_HOST=<scope>` (optional)
  - If set, host websocket tokens must include this scope.
- `RENDEZVOUS_JWT_REQUIRED_SCOPE_CLIENT=<scope>` (optional)
  - If set, client websocket tokens must include this scope.
- `RENDEZVOUS_JWT_REQUIRE_JTI=true`
  - Requires `jti` in authenticated tokens.
- `RENDEZVOUS_JWT_REPLAY_TTL_SECONDS=600`
  - Time window for replay rejection of previously seen `jti` values.
- `RENDEZVOUS_JWT_REPLAY_CACHE_MAX_ENTRIES=50000`
  - Upper bound for in-memory replay cache.
- `RENDEZVOUS_JWT_REQUIRE_PROTOCOL_VERSION=false`
  - When enabled, access tokens must include compatible `protocol_version` (or `protocol_versions`) claims.
- `RENDEZVOUS_REQUIRE_CONNECTION_GRANT=false`
  - When `true`, `connect_request` must include a valid short-lived connection grant token.
- `RENDEZVOUS_CONNECTION_GRANT_TTL_SECONDS=30`
  - Lifetime for server-issued connection grant tokens.
- `RENDEZVOUS_CONNECTION_GRANT_SECRET=<secret>` (optional)
  - Signing secret for connection grant tokens. Falls back to `RENDEZVOUS_JWT_SECRET` when unset.

JWT requirements in this Phase 2 kickoff:

- Required claims: `sub`, `iat`, `exp`, `jti` (when `RENDEZVOUS_JWT_REQUIRE_JTI=true`)
- Role claim check:
  - `ws/host` requires `role`/`roles` including `host` (or `admin`)
  - `ws/client` requires `role`/`roles` including `client` (or `admin`)
- Scope claim check (when configured):
  - `ws/host` requires `RENDEZVOUS_JWT_REQUIRED_SCOPE_HOST`
  - `ws/client` requires `RENDEZVOUS_JWT_REQUIRED_SCOPE_CLIENT`
  - `rendezvous:*` is accepted as wildcard scope
- Anti-replay:
  - Tokens with previously seen `sub:jti` are rejected until replay TTL expires.

Identity and scope binding behavior:

- Host registration requires token identity to match `host_id` unless role includes `admin`.
- Client registration requires token identity to match `client_id` unless role includes `admin`.
- Client connect/grant requests require token identity to match `client_id` and include target-host scope (`target_host:<host_id>` or equivalent wildcard/admin scope).

Connection grant flow (when enabled):

1. Client registers as usual (`register_client`).
2. Client requests grant for target host (`request_connection_grant`).
3. Server returns signed short-lived `connection_grant` token and `grant_session_id`.
4. Client sends `connect_request` with `connection_grant_token`.
5. Server validates grant and binds resulting rendezvous session to grant `session_id`.

Grant validation checks:

- Signature and TTL (`exp`)
- Required purpose claim (`purpose=connect_grant`)
- Exact client/host binding (`sub` and `host_id`)
- Session binding (`session_id`)
- Single-use replay protection by grant `jti`

Token issuance and refresh model:

- Long-lived identity/access tokens are expected to be issued by your external identity provider.
- Rendezvous server validates those access tokens for websocket authorization and scoped operations.
- Short-lived connection grants are issued by rendezvous and are single-use; clients request a new grant whenever they initiate a new connect flow.
- If an access token expires, client should refresh/re-acquire it via the identity provider and reconnect.

Recommended staged rollout:

1. Compatibility start:
  - `RENDEZVOUS_SECURITY_STAGE=compat`
2. Distribute app tokens and monitor denied-auth logs.
3. Token enforcement:
  - `RENDEZVOUS_SECURITY_STAGE=tokens`
4. Grant enforcement:
  - `RENDEZVOUS_SECURITY_STAGE=grants`
5. Strict enforcement:
  - `RENDEZVOUS_SECURITY_STAGE=strict`

App token forwarding:

- `NETKEYER_RENDEZVOUS_ACCESS_TOKEN`
  - When set, NetKeyer app sends `Authorization: Bearer <token>` on websocket rendezvous requests.
- NetKeyer Settings -> Access Token supports local JWT generation from an ID key:
  - Enable `Generate JWT locally from ID key (RustDesk-style)`.
  - Configure `kid`, key secret, issuer, audience, and token TTL.
  - App mints short-lived role/scoped JWTs automatically for host register, client list-hosts, and client connect flows.

Rendezvous service health exposure defaults:

- `RENDEZVOUS_HEALTH_ACCESS_MODE=private` (default)
  - Allows loopback/private source addresses.
  - Denies public source addresses.
- `RENDEZVOUS_HEALTH_ACCESS_MODE=cidr`
  - Uses `RENDEZVOUS_HEALTH_ALLOWED_CIDRS` for explicit allow lists.
- `RENDEZVOUS_HEALTH_ACCESS_MODE=public`
  - Publicly accessible health endpoint (not recommended for production).
- `RENDEZVOUS_HEALTH_ACCESS_MODE=disabled`
  - Always returns restricted.

Security observability quick checks (nginx overlay enabled):

Linux/macOS:

```bash
docker compose -f docker-compose.yml -f docker-compose.nginx.yml logs nginx --tail=200 | rg 'status=403|status=429|limit_req="REJECTED"'
```

Windows PowerShell:

```powershell
docker compose -f docker-compose.yml -f docker-compose.nginx.yml logs nginx --tail=200 | Select-String -Pattern 'status=403|status=429|limit_req="REJECTED"'
```

Expected signals:

- `status=403`: source denied by health ACL policy.
- `status=429`: rate/connection guard triggered.
- `limit_req="REJECTED"`: request rejected by nginx request-rate controls.

### Clean Upgrade Procedure

Run these commands from the `rendezvous_services` deployment directory.

Linux/macOS:

```bash
cd "$HOME/rendezvous_services"
docker compose -f docker-compose.yml down
docker compose -f docker-compose.yml build --no-cache
docker compose -f docker-compose.yml up -d --force-recreate
docker compose -f docker-compose.yml logs rendezvous --tail=50
```

Windows PowerShell:

```powershell
Set-Location "$HOME\rendezvous_services"
docker compose -f docker-compose.yml down
docker compose -f docker-compose.yml build --no-cache
docker compose -f docker-compose.yml up -d --force-recreate
docker compose -f docker-compose.yml logs rendezvous --tail=50
```

After the upgrade, verify:

- `http://127.0.0.1:49920/health` includes `version.services_version`.
- If nginx overlay is enabled: `https://127.0.0.1/health` responds over TLS.

### 4. Verify deployment

Open this URL in a browser:

- `http://127.0.0.1:49920/health`

Expected result includes:

- `status: "ok"`
- `version.services_version`

If `version` is missing in `/health`, an old container image is likely still running. Re-run step 3 exactly with `--build --force-recreate`.

Optional startup log check:

```bash
docker compose -f docker-compose.yml logs rendezvous --tail=50
```

You should see a startup line with services version, protocol, tag, commit, and build timestamp.

## Included Files

The deployment artifact includes only the baseline rendezvous + relay stack for initial users.

- `docker-compose.yml`
- `server/`
- `relay/`
- version/build metadata files

The optional nginx overlay is intentionally not included in this release package.

## Release Checklist

Use this checklist when creating a new `rendezvous_services` release artifact.

1. Update service version in `pyproject.toml`:
  - `project.version = "<new-version>"`
2. From repository root, generate the stamped artifact:
  - Windows PowerShell:
    - `./build-rendezvous-release.ps1`
  - Linux/macOS:
    - `./build-rendezvous-release.sh`
3. Confirm artifact was created:
  - `Releases/netkeyer-rendezvous-services-<new-version>.zip`
4. (Optional but recommended) Inspect artifact metadata:
  - Extract and verify `RELEASE_METADATA.json` fields (`services_version`, `protocol_version`, `build_tag`, `commit`, `built_at_utc`).
5. Validate clean deployment from the new artifact:
  - Extract with overwrite into `$HOME/rendezvous_services`.
  - Run clean upgrade commands from that directory:
    - `docker compose -f docker-compose.yml down`
    - `docker compose -f docker-compose.yml build --no-cache`
    - `docker compose -f docker-compose.yml up -d --force-recreate`
6. Verify runtime identity and health:
  - `http://127.0.0.1:49920/health` includes `version.services_version` matching the release.
  - `docker compose -f docker-compose.yml logs rendezvous --tail=50` shows startup version/protocol/tag/commit/build timestamp.
