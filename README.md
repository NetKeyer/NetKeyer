# NetKeyer+Remote - FlexRadio CW Keyer

A cross-platform GUI application for CW (Morse code) keying with FlexRadio devices, supporting both serial port and MIDI input devices.

## Recent Changes

- **Revision 2.1.35 (2026-08-25)**
  - Added relay-only experiment mode for controlled latency testing:
    - Server flag: `RENDEZVOUS_FORCE_RELAY=true`
    - App client flag: `NETKEYER_FORCE_RELAY_TRANSPORT=true`
  - Added relay latency capture/report helpers under `rendezvous_services/scripts/`:
    - `capture-relay-latency-data.sh`
    - `summarize-relay-latency-runs.sh`
  - Completed nginx ingress hardening and observability baseline for rendezvous:
    - TLS-first ingress with redirect
    - request guard/rate-limit controls
    - restricted `/health` defaults with explicit deny/throttle log visibility
  - Validated nginx stream relay path latency is no worse than fallback relay path in current tests.
  - Release tags for this set:
    - Client: `v2.1.35`
    - Rendezvous services: `rs-0.1.2`

- **Revision 2.1.34 (2026-08-08)**
  - Swapped default port roles for remote transport vs rendezvous control plane:
    - Remote keying transport default is now `49923`.
    - Rendezvous HTTP/WebSocket control-plane default is now `49920`.
    - Relay service remains `49921`.
    - Optional nginx relay TCP stream proxy remains `49922`.
  - Updated Docker and nginx deployment defaults under `rendezvous_services` to match the new rendezvous control-plane port.

- **Revision 2.1.32 (2026-08-06)**
  - Completed setup-page UI enhancements:
    - Connection Mode now uses Standalone / Client / Host labels.
    - Added Network Connection grouping for remote networking controls.
    - Updated client labels to Select Host, Host IP, and Host Port.
    - Selecting a discovered host now fills Host IP and Host Port when endpoint metadata is available.
    - Radio Selection is hidden in Client mode.
    - Network Connection is hidden in Standalone mode.
  - Refined host-mode operating behavior:
    - CW Settings are hidden in host mode unless a local key input device is connected.
    - Local host keying enables sidetone, while remote-origin keying suppresses host sidetone.
    - Sidetone source switching is transition-based to prevent keying jitter/regression.

- **Revision 2.1.27 (2026-07-30)**
  - Fixed remote non-CW behavior so host transmit mode is communicated to clients and clients correctly follow CW vs non-CW operation.
  - Fixed host transmit-mode synchronization to include non-CW to non-CW changes so mode text follows radio mode changes (for example USB to LSB).
  - Added host transmit mode text to remote heartbeat telemetry so remote client bottom status mode labels mirror host mode naming.
  - Fixed remote client non-CW keying to send PTT intent while suppressing local CW keyer/sidetone behavior.
  - Fixed remote client bottom status identity/mode display so connected state shows host identity and active mode immediately.
  - Fixed remote client non-CW single LED logic to follow host PTT-closure behavior (responds to either paddle/PTT closure path).
  - Fixed host operating-page bottom keying indicator behavior in non-CW mode: left indicator now represents PTT assertion (green when active), and the right indicator is intentionally hidden.

## Features

- **Cross-Platform**: Runs on Linux, Windows, and macOS using Avalonia UI
- **Radio Discovery**: Automatic discovery of FlexRadio devices on the network
  - Local network discovery
  - SmartLink remote connection support
  - Sidetone-only practice mode (no radio required)
- **Multiple Input Device Types**:
  - Serial port (HaliKey v1)
  - MIDI devices (HaliKey MIDI, CTR2, and other MIDI controllers)
  - Configurable MIDI note mappings for paddles, straight key, and PTT
- **CW Controls**:
  - Speed adjustment (5-60 WPM)
  - Sidetone volume control (0-100)
  - Pitch control (300-1000 Hz)
  - Iambic Mode A/B selection
  - Straight Key mode
  - Paddle swap option
- **Local Sidetone Generation**:
  - Low latency audio using platform-optimized backends
  - PortAudio for cross-platform compatibility
  - WASAPI for Windows
- **PTT Support**:
  - Supports PTT keying for non-CW modes
- **Remote Keying Transport**:
  - Remote Client mode sends local paddle state over TCP
  - Remote Host mode accepts up to 5 TCP clients
  - Active-client ownership lock to prevent simultaneous multi-client keying contention
  - Configurable client ownership hold time from 0.5 to 30.0 seconds (default 1.0 second)
  - Stale-frame drop policy to reject delayed paddle frames before keying
  - Host and client telemetry summaries for handshake latency, last lag, keying p50/p95 lag, max lag (60s), accepted frames (60s), and stale drops
  - Telemetry lag values are normalized per client to remove static clock-skew bias while preserving observed network delay variation
  - Default TCP port is `49923`
  - Client keeps local sidetone active, host mutes local sidetone
- **Rendezvous + Relay Signaling and Fallback (Phase 3/4)**:
  - Optional rendezvous-assisted host discovery and connection setup for remote client mode
  - Automatic three-stage negotiation: direct -> mapped-direct (UPnP/NAT-PMP) -> relay fallback
  - Relay transport handshake support using `SESSION <session_id> <role>` (`HOST` / `CLIENT`)
  - Always-on connection outcome logging with transport labels: `direct`, `mapped-direct`, `relay`
  - Dedicated service ports aligned with remote transport defaults:
    - Rendezvous HTTP/WebSocket service: `49920`
    - Relay service: `49921`
    - Optional nginx relay TCP stream proxy: `49922`
    - Remote keying transport: `49923`

## Requirements

- .NET 8.0 Runtime
- FlexRadio device on the network (or use sidetone-only mode for practice)
- Input device:
  - Serial port device (e.g., HaliKey v1/v2), OR
  - MIDI controller (e.g., HaliKey MIDI, CTR2)
- SmartLink: you must be using a binary build from GitHub releases (or the builtin updater)
  to connect to SmartLink or see it in the UI. This is because FlexRadio requires us to keep
  the SmartLink client ID secret. Anyone wanting to develop a fork will have to negotiate a
  developer contract with FlexRadio if they want to use SmartLink. This is the best compromise
  we can manage for an open-source app.

## Building

### Requirements

- .NET 8.0 SDK
- To build the native MIDI shim (required for MIDI input):

  | Platform   | Tools required |
  |------------|----------------|
  | Linux      | `cmake`, `gcc`/`g++`, `libasound2-dev` (ALSA headers) |
  | Windows    | `cmake`, Visual Studio 2022 (includes MSVC, nmake, rc) |
  | macOS      | `cmake`, Xcode Command Line Tools (`xcode-select --install`) |

  CMake downloads libremidi automatically on first build (requires internet access).

### 1. Build the native MIDI shim

**Linux / macOS:**

```bash
cd native
./build.sh
```

**Windows (PowerShell):**

```powershell
cd native
.\build.ps1
```

The built binary is placed in the correct directory for your platform automatically
(e.g. `native/linux-x64/`, `native/osx-arm64/`, `native/windows-x64/`).

> **Not working on the native component?** You can skip the build above by copying
> the pre-built shim out of the [latest release](https://github.com/NetKeyer/NetKeyer/releases/latest)
> into the appropriate directory instead:
>
> | Platform    | File to copy                     | Destination              |
> |-------------|----------------------------------|--------------------------|
> | Linux x64   | `libnetkeyer_midi_shim.so`       | `native/linux-x64/`      |
> | Linux arm64 | `libnetkeyer_midi_shim.so`       | `native/linux-arm64/`    |
> | Windows     | `netkeyer_midi_shim.dll`         | `native/windows-x64/`    |
> | macOS x64   | `libnetkeyer_midi_shim.dylib`    | `native/osx-x64/`        |
> | macOS arm64 | `libnetkeyer_midi_shim.dylib`    | `native/osx-arm64/`      |

### 2. Build the application

```bash
dotnet build
```

## Running

```bash
dotnet run
```

## Usage

### Setup Page

1. **SmartLink (Optional)**: Click "Enable SmartLink" to connect to remote radios via FlexRadio SmartLink
2. **Select Radio**:
   - Click "Refresh" to discover FlexRadio devices
   - Select a radio and GUI client station from the dropdown, OR
   - Select "No radio (sidetone only)" for practice mode
3. **Select Input Device Type**: Choose between:
   - Serial Port (HaliKey v1) - uses CTS (left) and DSR (right) pins
   - MIDI (HaliKey MIDI, CTR2) - uses configurable MIDI note mappings
4. **Choose Input Device**:
   - For Serial: Select the serial port connected to your keyer/paddle
   - For MIDI: Select the MIDI device, then optionally click "Configure MIDI Notes..." to customize mappings
5. **Connect**: Click "Connect" to begin operating

### Operating Page

1. **Monitor Paddle Status**: Visual indicators show left/right paddle state in real-time
2. **Adjust CW Settings**:
   - Speed (WPM): Controls dit/dah timing
   - Sidetone: Volume of local audio feedback
   - Pitch: Frequency of sidetone tone
3. **Select Keyer Mode**:
   - Iambic: Automatic dit/dah generation with Mode A or Mode B
   - Straight Key: Direct on/off control
4. **Swap Paddles**: Reverse left/right paddle assignment if needed
5. **Disconnect**: Return to setup page to change settings

### Remote Mode

Use the **Connection Mode** section on the setup page to select one of these modes:

- **Standalone**: Existing behavior (local input keys local radio connection)
- **Client**:
  - Opens local input device and keeps local sidetone active
  - Sends paddle/straight/PTT state with timing ticks to a remote host via TCP
- **Host**:
  - Connects to a local/SmartLink radio and listens for remote paddle events
  - Mutes local sidetone while host mode is active
  - Accepts up to 5 simultaneous TCP client connections
  - Includes active-client ownership lock with configurable hold time
  - Drops stale remote frames before they reach keying

Rendezvous setup inputs:
- **Redezvous Server**: enter only host name or IP (for example, `netkeyer.ddns.net`).
- **Port**: default `49920`.
- The app generates the control URL in code as `http://<server>:<port>`.

Remote host setup options include:

- **Max Clients**: limit concurrent remote clients (1 to 5)
- **Client Hold Time**: ownership hold duration after last accepted key input (0.5s to 30.0s in 0.5s increments)

Operating-page telemetry:

- **Host Status** and **Client Status** blocks include: 

- Host and Client connection list and status
- compact two-line telemetry display.
- Telemetry fields:
  - Line 1: secure handshake duration, last lag, keying p50 lag, keying p95 lag
  - Line 2: accepted frames in last 60 seconds, stale drops, max lag (last 60 seconds)
- 60-second window metrics age out during idle periods (for example accepted 60s returns to 0 if no frames are received in the last 60 seconds).
- Telemetry text is rendered with high-contrast styling for readability in operating view.

Defaults:

- Port: `49923`
- Client target host: `127.0.0.1`
- Host bind address: `0.0.0.0`

Remote security defaults:

- Secure transport handshake is enabled by default.
- Secure transport is required by default (no plaintext fallback).
- Ciphertext frame validation is enabled by default for both direct and relay transports.
- Insecure overrides are debug-gated: set `NETKEYER_DEBUG_ALLOW_INSECURE_OVERRIDES=true` in a debug build.
- To opt out for local/lab debugging only, set one or more environment variables to `0`, `false`, `no`, or `off` (only honored when the debug gate above is active):
  - `NETKEYER_ENABLE_SECURE_REMOTE_TRANSPORT`
  - `NETKEYER_REQUIRE_SECURE_REMOTE_TRANSPORT`
  - `NETKEYER_VALIDATE_RELAY_CIPHERTEXT`
- Security policy failures are surfaced as actionable, non-sensitive UI status messages; detailed failure internals remain in debug logs.
- Security operations runbook (Phase 5): `docs/security/phase5-operations-runbook.md`

Rendezvous token options:

- Manual mode: set a pre-issued JWT under Settings -> Access Token.
- Local ID-key mode (RustDesk-style):
  - Enable `Generate JWT locally from ID key (RustDesk-style)` in Settings -> Access Token.
  - Configure `kid`, key secret, issuer, audience, and token TTL.
  - NetKeyer mints short-lived HS256 JWTs automatically for host registration, client host-discovery, and client connect requests.
  - For server-side key rotation/revocation, configure `RENDEZVOUS_JWT_KEYS_JSON` with per-user/per-device `kid` secrets.

## Rendezvous and Relay Services

NetKeyer now includes deployment artifacts for standalone rendezvous control-plane and relay data-plane services under [rendezvous_services](rendezvous_services).

The rendezvous server and relay server are Python applications requiring Python 3.11.
These Python apps are intended to run in Docker containers therfore Docker is required
to be installed on the system hosting these apps.  The repository contains the necessary Docker files for deployment.  It may be be necessary to open ports
49920-49922 on your router to allow the rendezvous server to be accessed over the WAN.

### Services Versioning Model

Rendezvous and relay services are versioned as a single suite using semantic versioning.

- Single source of truth: `rendezvous_services/pyproject.toml` `project.version`.
- Wire compatibility contract: `RENDEZVOUS_SERVICES_PROTOCOL_VERSION` (defaults to `1`).
- Build traceability metadata (optional):
  - `RENDEZVOUS_SERVICES_BUILD_TAG`
  - `RENDEZVOUS_SERVICES_BUILD_COMMIT`
  - `RENDEZVOUS_SERVICES_BUILD_DATE`

Runtime metadata is exposed via rendezvous `/health` (`version` block) and relay startup logs.

Compatibility matrix (maintain this table as releases evolve):

| NetKeyer Desktop Revision | Supported Services Version | Protocol Version |
|---|---|---|
| 2.1.35+ | 0.1.2+ | 1 |

Release tag conventions:

- Desktop client release tags use `vX.Y.Z` (example: `v2.1.35`).
- Rendezvous services release tags use `rs-X.Y.Z` (example: `rs-0.1.2`).

### Release Cut Order (v2.1.35 / rs-0.1.2)

Use this order to avoid cross-trigger confusion between desktop and services release flows.

1. Validate clean working tree and tests.
2. Create and push services tag first:
  - `git tag rs-0.1.2`
  - `git push origin rs-0.1.2`
3. Build/publish rendezvous services artifact from current commit:
  - `./build-rendezvous-release.ps1` (Windows PowerShell), or
  - `./build-rendezvous-release.sh` (Linux/macOS)
4. Publish services release notes/artifact labeled `rs-0.1.2`.
5. Create and push desktop client tag:
  - `git tag v2.1.35`
  - `git push origin v2.1.35`
6. Publish desktop client release notes/artifacts labeled `v2.1.35`.
7. Post-publish verification:
  - confirm desktop updater target/version is correct.
  - confirm rendezvous package metadata reports `services_version=0.1.2`.
  - confirm `/health` `version` block matches expected build tag/commit/date.

### Service Overview

- **Rendezvous server**: FastAPI + WebSocket signaling for host registration, host discovery, connect orchestration, and relay fallback signaling.
- **Relay server**: asyncio TCP byte pipe that pairs host/client sockets by session ID and forwards bytes bidirectionally.
- **Rendezvous health endpoint**: `/health` reports service status plus automatic router port-map attempt results when enabled.

### Connection Negotiation Summary

When rendezvous mode is enabled, NetKeyer negotiates transport in this order:

1. **Direct**: client attempts direct TCP to the host endpoint provided by rendezvous.
2. **Mapped-direct**: on direct timeout/failure, rendezvous requests host automatic TCP mapping (UPnP first, then NAT-PMP) and returns an updated mapped endpoint for a retry.
3. **Relay fallback**: if mapped endpoint is unavailable or retry fails, rendezvous signals both sides to switch to relay transport.

This keeps the keying data path as close to direct as possible while still providing deterministic fallback.

### Container Summary

| Container | Purpose | Internal Port | Host Port (default) |
|----------|---------|---------------|---------------------|
| `netkeyer-rendezvous` | HTTP/WebSocket control-plane (`/health`, `/ws/host`, `/ws/client`) | `49920` | `49920` |
| `netkeyer-relay` | Raw TCP relay service | `49921` | `49921` |
| `netkeyer-rendezvous-nginx` (optional) | Reverse proxy for rendezvous + optional TCP stream proxy for relay | `80` + `49922` | `8080` + `49922` |

## Docker Deployment (Rendezvous Services)

Compose files are split so nginx is optional:

- Base services (relay + rendezvous): [rendezvous_services/docker-compose.yml](rendezvous_services/docker-compose.yml)
- Optional nginx overlay: [rendezvous_services/docker-compose.nginx.yml](rendezvous_services/docker-compose.nginx.yml)

### Release Artifact Helper

Use the release helper to produce a stamped deployment bundle zip that works across Windows, Linux, and macOS:

From repository root (recommended one-command wrappers):

```powershell
./build-rendezvous-release.ps1
```

```bash
./build-rendezvous-release.sh
```

Direct helper invocation:

```bash
cd rendezvous_services
python release_helper.py
```

Output:

- `Releases/netkeyer-rendezvous-services-<version>.zip`

The artifact includes all files required to deploy rendezvous + relay with Docker Compose, and stamps these values into `docker-compose.yml`:

- `RENDEZVOUS_SERVICES_VERSION`
- `RENDEZVOUS_SERVICES_PROTOCOL_VERSION`
- `RENDEZVOUS_SERVICES_BUILD_TAG`
- `RENDEZVOUS_SERVICES_BUILD_COMMIT`
- `RENDEZVOUS_SERVICES_BUILD_DATE`

Note: the release bundle intentionally excludes the optional nginx overlay for this initial deployment track.

Advanced options:

- `--version <x.y.z>` override services version
- `--protocol-version <n>` override protocol version
- `--tag <tag>` override build tag
- `--commit <sha>` override commit hash
- `--build-date <utc-iso8601>` override build timestamp
- `--output-dir <path>` change artifact output directory
- `--keep-staging` keep expanded bundle directory in output

For upgrade deployments, extract release bundles with explicit overwrite flags (`unzip -o` on Linux/macOS, `Expand-Archive -Force` on Windows). See [rendezvous_services/README.md](rendezvous_services/README.md) for full upgrade steps.

Release checklist for rendezvous services packaging and deployment validation is documented in [rendezvous_services/README.md](rendezvous_services/README.md).

Clean upgrade (run from the `rendezvous_services` directory):

```bash
cd "$HOME/rendezvous_services"
docker compose -f docker-compose.yml down
docker compose -f docker-compose.yml build --no-cache
docker compose -f docker-compose.yml up -d --force-recreate
docker compose -f docker-compose.yml logs rendezvous --tail=50
```

### Start relay + rendezvous only

```bash
cd rendezvous_services
docker compose -f docker-compose.yml up -d
```

Manual-mode recommendation:

- The default compose preset is manual-mode with automatic router mapping disabled.
- Configure static router forwards for stable WAN behavior:
  - TCP `49920` -> rendezvous host
  - TCP `49921` -> relay host
- This mode is recommended for production deployments because many routers handle manual/static forwards more consistently than dynamic NAT-PMP mappings.

Rendezvous automatic port mapping controls (set on the `rendezvous` service in [rendezvous_services/docker-compose.yml](rendezvous_services/docker-compose.yml)):

- `RENDEZVOUS_ENABLE_PORT_MAP`: enable/disable startup port-map attempts (`false` by default in compose manual-mode preset).
- `RENDEZVOUS_CONTROL_PORT`: control-plane port to map/report (`49920` by default).
- `RENDEZVOUS_RELAY_PORT`: relay port to map/report (`49921` by default).
- `RENDEZVOUS_ENABLE_NGINX_PORT_MAP`: optionally include nginx relay proxy port in mapping attempts (`false` by default).
- `RENDEZVOUS_NGINX_PORT`: nginx relay proxy port when optional mapping is enabled (`49922` by default).
- `RENDEZVOUS_PORTMAP_INTERNAL_IP`: optional UPnP internal target override (for containerized deployments where mappings should target host LAN IP).
- `RENDEZVOUS_NATPMP_GATEWAY_IP`: optional NAT-PMP router IP override.
- `RENDEZVOUS_PORTMAP_HOST_IPS`: optional comma-separated host IP hints used when recognizing already-mapped ports.

When auto-mapping is enabled, the server attempts mappings in this order per port:

1. UPnP
2. NAT-PMP

Mapping results are included in `/health` under `port_mapping`, including per-port success/failure, protocol used, and a diagnostics block showing effective IP/gateway choices.

The `/health` response also includes a `version` block:

- `services_version`: suite version loaded from `pyproject.toml` or `RENDEZVOUS_SERVICES_VERSION` override.
- `protocol_version`: protocol contract value from `RENDEZVOUS_SERVICES_PROTOCOL_VERSION`.
- `component`: `rendezvous`.
- `build`: `{ tag, commit, built_at_utc }` from environment metadata.

### `/health` Statistics Block

The rendezvous `/health` endpoint also includes a `statistics` block for active runtime visibility:

- `counts`: totals for currently connected `hosts`, `clients`, and active `sessions`.
- `session_type_counts`: active session totals by negotiated type:
  - `direct`
  - `mapped`
  - `relay`
- `hosts`: list of connected hosts with endpoint/capacity and per-host `active_sessions`.
- `clients`: list of connected clients with endpoint/connected host and per-client `active_sessions`.
- `sessions`: list of active sessions with host/client IDs, state, type, and mapping/punch metadata.

Example `/health` payload (abridged):

```json
{
  "status": "ok",
  "relay_host": "relay",
  "relay_port": 49921,
  "control_port": 49920,
  "port_mapping": {
    "enabled": false,
    "attempted": false,
    "summary": {
      "successful": 0,
      "failed": 0
    }
  },
  "statistics": {
    "counts": {
      "hosts": 2,
      "clients": 3,
      "sessions": 3
    },
    "session_type_counts": {
      "direct": 1,
      "mapped": 1,
      "relay": 1
    },
    "hosts": [
      {
        "host_id": "host-a",
        "public_ip": "198.51.100.10",
        "public_port": 49923,
        "current_clients": 2,
        "max_clients": 5,
        "active_sessions": [
          {
            "session_id": "sess-1",
            "client_id": "client-a",
            "type": "direct",
            "state": "direct_connected"
          }
        ]
      }
    ],
    "clients": [
      {
        "client_id": "client-b",
        "public_ip": "203.0.113.21",
        "public_port": 53001,
        "connected_host": "host-a",
        "active_sessions": [
          {
            "session_id": "sess-2",
            "host_id": "host-a",
            "type": "mapped",
            "state": "map_ready"
          }
        ]
      }
    ],
    "sessions": [
      {
        "session_id": "sess-3",
        "host_id": "host-b",
        "client_id": "client-c",
        "state": "relay_requested",
        "type": "relay",
        "map_requested": true,
        "mapped_public_ip": null,
        "mapped_public_port": null,
        "host_punch_result": false,
        "client_punch_result": false
      }
    ]
  }
}
```

### Start relay + rendezvous + optional bundled nginx

```bash
cd rendezvous_services
docker compose -f docker-compose.yml -f docker-compose.nginx.yml up -d
```

### Included nginx snippets

- HTTP/WebSocket reverse proxy config: [rendezvous_services/nginx/rendezvous.conf](rendezvous_services/nginx/rendezvous.conf)
- TCP stream relay proxy config: [rendezvous_services/nginx/stream-relay.conf](rendezvous_services/nginx/stream-relay.conf)

## Using an Existing nginx Installation

If you already run your own nginx, do **not** start the optional nginx compose overlay. Run only relay + rendezvous and add equivalent nginx config to your existing deployment.

### 1. Run only core services

```bash
cd rendezvous_services
docker compose -f docker-compose.yml up -d
```

### 2. Configure HTTP/WebSocket proxy to rendezvous

Point nginx to `netkeyer-rendezvous:49920` (or the host where rendezvous is published).

```nginx
upstream rendezvous_backend {
  server 127.0.0.1:49920;
}

server {
  listen 80;
  server_name _;

  location /health {
    proxy_pass http://rendezvous_backend/health;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
  }

  location /ws/ {
    proxy_pass http://rendezvous_backend/ws/;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 3600;
  }
}
```

### 3. (Optional) Configure relay TCP stream proxy

If you need nginx TCP stream proxying, add a `stream` block that forwards to relay on `49921`.

```nginx
stream {
  upstream relay_backend {
    server 127.0.0.1:49921;
  }

  server {
    listen 49922;
    proxy_pass relay_backend;
  }
}
```

### 4. NetKeyer client/server endpoint expectations

- Rendezvous URL should target your nginx/public endpoint that serves `/ws/host` and `/ws/client`.
- Relay host/port is provided to clients by rendezvous via `use_relay` signaling.
- Ensure firewall/NAT rules allow inbound traffic for whichever public rendezvous/relay ports you publish.

## MIDI Configuration

The MIDI note configuration dialog allows you to assign any MIDI note (0-127) to one or more functions:

- **Left Paddle**: Generates dits in iambic mode
- **Right Paddle**: Generates dahs in iambic mode
- **Straight Key**: Direct key on/off control
- **PTT**: Push-to-talk for non-CW modes

Default mappings (compatible with HaliKey MIDI and CTR2):

- Note 20: Left Paddle + Straight Key + PTT
- Note 21: Right Paddle + Straight Key + PTT
- Note 30: Straight Key only
- Note 31: PTT only

## About and Updates

- Window and dialog titles use `NetKeyer+Remote` branding.
- The About dialog credits show:
  - `by Eric NR4O`
  - `forked from NetKeyer by Andrew KC2G and contributors`
- `Check for Updates` is enabled in the About dialog.
- Update checks target GitHub Releases for this repository: `https://github.com/NetKeyer/NetKeyer`.
- Update install/apply requires a Velopack-installed build. When running via `dotnet run`, update status will report development mode and skip install/apply.

## Troubleshooting

### Connection Issues

**Radio not found**:

- Ensure radio is on the same network
- Check firewall settings
- Try SmartLink if local discovery fails

**GUI client binding fails**:

- Radio needs SmartSDR or another GUI client running
- Wait a moment after connecting before binding

**Remote client connects then immediately disconnects**:

- Verify the shared token matches exactly between Remote Host and Remote Client.
- A token mismatch is refused by the host and now logs as connection refused due to shared token mismatch.
- On client, this commonly appears as a host error followed by a quick disconnect.

**Direct and mapped-direct never succeed (relay always used)**:

- Verify the host machine firewall allows inbound TCP on the remote host listen port (default 49923).
- On Windows, ensure the firewall rule applies to the active network profile, including Public when applicable.
- Confirm router/NAT forwarding and mapping are targeting the same host and port.

**Connected but host does not key radio (accepted=0, dropped_stale increases)**:

- Check host telemetry counters. If `accepted` stays `0` and `dropped_stale` rises, paddle frames are being rejected by stale-frame gating.
- NetKeyer now uses normalized lag by default for stale gating, which removes constant client/host wall-clock offset from stale decisions.
- Ensure both host and client clocks are synchronized (NTP/Windows Time), especially when testing across multiple machines.
- Optional staged mode: enable sender-tick stale gating in host settings by setting `RemoteHostUseSenderTickStaleGate` to `true` in `settings.json`.

### First-Time Remote Setup Checklist

Use this quick checklist before WAN testing:

1. Configure the same shared token on Remote Host and Remote Client.
2. Confirm Remote Host is listening on the expected port (default `49923`).
3. On host, allow inbound TCP on the listen port in the OS firewall.
4. On Windows host, ensure the firewall rule covers the active profile (Private/Public).
5. Optional: Port forwarding, confirm router/NAT forwards the same port to the host.
6. Start host first, then connect client.
7. Verify expected logs:
  - Host success: `Session <id> authenticated ...`
  - Host refusal: `Connection refused ... shared token mismatch` or `missing shared token`
  - Client refusal: `Host error payload: Connection refused: ...`
8. If direct fails but relay succeeds, treat this as a network path issue (firewall/NAT), not a protocol failure.

### Audio Issues

**No sidetone**:

- Check sidetone volume slider
- Verify system audio is not muted
- Check audio output device in your system mixer

**High latency**:

- Windows: Ensure WASAPI backend is being used
- Linux: Check PulseAudio/PipeWire configuration
- Adjust buffer size if needed

### Input Device Issues

**Serial port not found**:

- Check device permissions (Linux: add user to `dialout` group)
- Verify device is connected
- Click "Refresh" to rescan

**MIDI device not responding**:

- Verify MIDI device is connected and powered
- Check MIDI note mappings match your device
- Use "Configure MIDI Notes..." to adjust mappings

### Debug Logging

NetKeyer supports detailed debug logging controlled by the `NETKEYER_DEBUG` environment variable. This can help diagnose issues with specific subsystems.

Remote telemetry note:

- Core remote telemetry summaries are logged by default under category `remote-telemetry`, even when `NETKEYER_DEBUG` is not configured.
- Setting `NETKEYER_DEBUG=remote` still enables additional remote transport/session diagnostic logs.
- Stale-frame gate mode is logged on host startup as either `normalized-lag` (default) or `sender-tick`.

**Log File Location**:

Debug messages are automatically written to a log file in the NetKeyer application data folder:

- **Windows**: `%APPDATA%\NetKeyer\debug.log`
- **Linux**: `~/.config/NetKeyer/debug.log`
- **macOS**: `~/Library/Application Support/NetKeyer/debug.log`

You can easily access the log folder via **Help → View Debug Log...** in the application menu.

**Note**: On Windows, GUI applications don't show console output when run outside a debugger. Debug messages are always written to the log file, making them accessible even when the console isn't visible.

**Available Debug Categories**:

| Category | Description |
|----------|-------------|
| `keyer` | Iambic keyer state machine (paddle state, element timing, mode transitions) |
| `midi` | MIDI input parsing and raw event processing |
| `input` | Input abstraction layer (paddle state changes, indicator updates) |
| `slice` | Transmit slice mode monitoring (CW vs PTT mode detection) |
| `sidetone` | Audio sidetone provider (tone/silence state machine, timing) |
| `audio` | Audio device management (initialization, enumeration, selection) |
| `remote` | Remote TCP client/host transport, framing, and session status |
| `remote-telemetry` | Always-on remote telemetry summaries (raw/baseline/normalized lag, jitter, accepted/stale counters) |

**Usage Examples**:

**Linux/macOS**:

```bash
# Enable all debug output
NETKEYER_DEBUG=all dotnet run

# Enable specific categories
NETKEYER_DEBUG=keyer,midi dotnet run

# Enable all MIDI-related categories using wildcard
NETKEYER_DEBUG=midi* dotnet run
```

**Windows PowerShell**:

```powershell
# Enable all debug output
$env:NETKEYER_DEBUG="all"
dotnet run

# Enable specific categories
$env:NETKEYER_DEBUG="keyer,midi"
dotnet run
```

**Windows CMD**:

```cmd
# Enable all debug output
set NETKEYER_DEBUG=all
dotnet run

# Enable specific categories
set NETKEYER_DEBUG=keyer,midi
dotnet run
```

**Common Debugging Scenarios**:

- **Paddle not working**: Use `NETKEYER_DEBUG=input,keyer` to see paddle state changes and keyer logic
- **MIDI issues**: Use `NETKEYER_DEBUG=midi,input` to see raw MIDI events and parsed paddle states
- **Audio problems**: Use `NETKEYER_DEBUG=audio,sidetone` to see device initialization and tone generation
- **Radio connection issues**: Use `NETKEYER_DEBUG=slice` to see transmit mode detection

---

## Developer Information

### Project Structure

```
NetKeyer/
├── Views/                  # XAML UI layouts
├── ViewModels/             # Application logic and data binding
│   ├── MainWindowViewModel.cs
│   ├── MidiConfigDialogViewModel.cs
│   ├── AudioDeviceDialogViewModel.cs
│   └── AboutWindowViewModel.cs
├── Models/                 # Data models
│   ├── UserSettings.cs
│   ├── MidiNoteMapping.cs
│   └── AudioDeviceInfo.cs
├── Services/               # Core application services
│   ├── InputDeviceManager.cs
│   ├── KeyingController.cs
│   ├── RadioSettingsSynchronizer.cs
│   ├── SmartLinkManager.cs
│   └── TransmitSliceMonitor.cs
├── Audio/                  # Sidetone generation
│   ├── SidetoneGeneratorFactory.cs
│   └── ISidetoneGenerator.cs
│   ├── SidetoneGenerator.cs (PortAudio)
│   ├── WasapiSidetoneGenerator.cs (Windows WASAPI)
│   ├── SidetoneProvider.cs (waveform generation)
├── Midi/                   # MIDI input handling
│   ├── MidiPaddleInput.cs
│   └── LibreMidi/          # Native shim P/Invoke layer
│       ├── NativeMethods.cs
│       └── LibreMidiInput.cs
├── native/                 # Native MIDI shim source and pre-built binaries
│   ├── netkeyer_midi_shim.c
│   ├── CMakeLists.txt
│   ├── exports.map
│   ├── build.sh            # Linux/macOS build script
│   ├── build.ps1           # Windows build script
│   ├── linux-x64/          # Pre-built binaries (not in git; build or copy from release)
│   ├── linux-arm64/
│   ├── windows-x64/
│   ├── osx-x64/
│   └── osx-arm64/
├── Keying/                 # Iambic keyer logic
│   └── IambicKeyer.cs
├── SmartLink/              # SmartLink authentication
│   ├── SmartLinkAuthService.cs
│   ├── SmartLinkModels.cs
├── Helpers/                # Utility classes
│   ├── DebugLogger.cs
│   └── UrlHelper.cs
├── lib/                    # Compiled FlexRadio libraries
```

### Input Device Support

**Serial Port (HaliKey v1)**:

- HaliKey v1: CTS (left paddle) + DSR (right paddle)

**MIDI Devices**:

- Supports any MIDI controller with configurable note mappings
  - Tested with HaliKey MIDI and CTR2-MIDI
- Note On/Off events trigger paddle/key/PTT state changes

### Iambic Keyer Implementation

- Software-based iambic keyer with Mode A and Mode B support
- State machine is based on audio timings

### Audio Sidetone

**WASAPI Backend** (Windows preferred):
- Lowest latency

**PortAudio Backend**:
- Cross-platform compatibility for Linux and macOS
- Supports Windows DirectSound and ASIO in case WASAPI doesn't work for some reason

### Settings Persistence

User settings are stored in:

- Linux: `~/.config/NetKeyer/settings.json`
- Windows: `%APPDATA%\NetKeyer\settings.json`
- macOS: `~/Library/Application Support/NetKeyer/settings.json`

Stored settings include:

- Selected radio (serial number and GUI client station)
- Input device type and selection
- MIDI note mappings
- SmartLink credentials (encrypted)

## License

FlexLib components are Copyright © 2018-2024 FlexRadio Systems. All rights reserved.
