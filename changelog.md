# Remote Keying Feature changes

<!-- markdownlint-disable MD022 MD024 MD032 -->

## 2026-09-06 (Revision 2.1.41, Services 0.1.7)

### Added
- App-side rendezvous auth configuration UX in Settings -> Access Token:
  - Manual JWT entry mode.
  - Local JWT generation mode (RustDesk-style) with configurable `kid`, key secret, issuer, audience, and token TTL.
  - In-app rendezvous auth test action with pass/fail status coloring for quick validation.
- JWT keyring operations support for deployment:
  - `rendezvous_services/scripts/manage-jwt-keyring.py` interactive keyring manager (list/add/change/delete).
  - Automatic 32-byte secret generation for add/change operations.
  - Rotating backups on write (`.bak1`, `.bak2`, `.bak3`).
- Release automation updates so rendezvous release bundles include the keyring manager script.

### Changed
- Completed Phase 5 security rollout baseline for rendezvous services and client integration.
- Added staged security/auth controls with `/health`-visible security telemetry counters:
  - `auth_failures`, `handshake_failures`, `replay_rejects`, `decrypt_failures`.
- Rendezvous JWT validation now supports keyring sources:
  - Preferred read-only file source: `RENDEZVOUS_JWT_KEYS_FILE`.
  - Fallback JSON source: `RENDEZVOUS_JWT_KEYS_JSON`.
  - When keyring entries are loaded, token `kid` must be present and map to a configured key.
- Docker Compose deployment defaults now include a read-only mounted keyring file (`jwt_keys.json`) and associated environment defaults.
- Client Status telemetry UI was simplified and reordered to:
  - `last`, `p50`, `p95`, `max`, `accepted 60s`, `stale`.
  - Handshake duration remains available in telemetry payloads and logs.

### Fixed
- Resolved compatibility-mode connection regression introduced during security hardening.
- Improved auth/connect traceability with additional always-on diagnostics for operator troubleshooting.

### Release Tags
- Client release tag: `v2.1.41`
- Rendezvous services release tag: `rs-v0.1.7`

## 2026-08-25 (Revision 2.1.35, Services 0.1.2)

### Added
- Relay-only latency experiment feature flags:
  - Server: `RENDEZVOUS_FORCE_RELAY=true` to advertise relay immediately and bypass punch/map negotiation for controlled testing.
  - App client: `NETKEYER_FORCE_RELAY_TRANSPORT=true` to skip direct and mapped-direct transport attempts and connect relay-only.
- Relay latency measurement helpers in `rendezvous_services/scripts/`:
  - `capture-relay-latency-data.sh` for per-run capture artifacts.
  - `summarize-relay-latency-runs.sh` for aggregate reporting and automatic p95 gate evaluation.

### Changed
- Phase 1 nginx ingress hardening and observability were completed for rendezvous deployment:
  - TLS-first ingress with HTTP-to-HTTPS redirect.
  - Request guards (rate/connection limits, timeout/body limits).
  - Restricted `/health` defaults and explicit deny/throttle visibility in nginx logs.
- Relay ingress decision is now Option A (nginx stream proxy), with fallback-to-Option B only if latency gate fails.

### Validated
- Relay path latency through nginx stream proxy was verified as no worse than direct relay fallback path for current test conditions.

### Release Tags
- Client release tag: `v2.1.35`
- Rendezvous services release tag: `rs-0.1.2`

## 2026-08-08 (Revision 2.1.34)

### Changed
- Swapped default port assignments between remote transport and rendezvous control-plane:
  - Remote host/client keying transport default changed from `49920` to `49923`.
  - Rendezvous HTTP/WebSocket control-plane default changed from `49923` to `49920`.
  - Relay service remains on `49921`.
  - Optional nginx relay TCP stream proxy remains on `49922`.
- Updated rendezvous deployment defaults to match the new control-plane port:
  - [rendezvous_services/server/Dockerfile](rendezvous_services/server/Dockerfile)
  - [rendezvous_services/docker-compose.yml](rendezvous_services/docker-compose.yml)
  - [rendezvous_services/nginx/rendezvous.conf](rendezvous_services/nginx/rendezvous.conf)
- Rendezvous server startup now attempts automatic router port mapping per configured port using UPnP first, then NAT-PMP fallback.
- Added rendezvous compose environment controls for optional nginx relay-proxy port mapping.
- Expanded `/health` endpoint output to include port-map attempt status, protocol used, and per-port success/failure diagnostics.
- Updated rendezvous compose defaults to a manual-mode recommendation preset (`RENDEZVOUS_ENABLE_PORT_MAP=false`) with explicit router forward guidance for TCP `49920` (rendezvous) and TCP `49921` (relay).
- Added optional advanced auto-mapping override environment settings for containerized deployments:
  - `RENDEZVOUS_PORTMAP_INTERNAL_IP`
  - `RENDEZVOUS_NATPMP_GATEWAY_IP`
  - `RENDEZVOUS_PORTMAP_HOST_IPS`
- Implemented shared services versioning for rendezvous + relay with a single source of truth (`rendezvous_services/pyproject.toml` project version) and optional environment overrides.
- Added `/health` `version` metadata block in rendezvous including:
  - `services_version`
  - `protocol_version`
  - `component`
  - build metadata (`tag`, `commit`, `built_at_utc`)
- Added relay startup version logging using the same shared services version/protocol/build metadata.
- Added compose environment placeholders for services version/protocol/build stamping:
  - `RENDEZVOUS_SERVICES_VERSION`
  - `RENDEZVOUS_SERVICES_PROTOCOL_VERSION`
  - `RENDEZVOUS_SERVICES_BUILD_TAG`
  - `RENDEZVOUS_SERVICES_BUILD_COMMIT`
  - `RENDEZVOUS_SERVICES_BUILD_DATE`
- Added `rendezvous_services/release_helper.py` release script to generate a cross-platform deployment artifact zip containing all required rendezvous/relay server files.
- Release helper now stamps compose metadata (`version`, `protocol`, `tag`, `commit`, `build date`) and emits `RELEASE_METADATA.json` for traceability.
- Added repository-root wrapper scripts for one-command, consistently stamped service release bundles:
  - `build-rendezvous-release.ps1`
  - `build-rendezvous-release.sh`

## 2026-08-06 (Revision 2.1.32)

### Added
- Completed Step 9 setup-page UI enhancement set:
  - Connection Mode labels now use Standalone / Client / Host.
  - New Network Connection group consolidates client/host network settings.
  - Client labels updated to Select Host, Host IP, and Host Port.
  - Selecting a discovered host now fills Host IP and Host Port when endpoint metadata is available.
  - Radio Selection is hidden in Client mode.
  - Network Connection is hidden in Standalone mode.

### Fixed
- Corrected host operating-page CW Settings visibility in host mode so CW Settings are hidden when no local key input device is connected.
- Corrected host-mode sidetone/keying source behavior:
  - local host keying from a connected local HaliKey enables sidetone,
  - remote keying never generates sidetone on the host instance,
  - source transitions no longer perform per-frame sidetone toggles (prevents delayed/erratic keying regression).

### Changed
- Program revision 2.1.32 publishes Step 9 setup UI completion and host-mode keying/sidetone source refinement updates.
- Re-enabled About dialog update checks and centralized the update repository URL used by both GitHub link navigation and Velopack update queries.

## 2026-07-30 (Revision 2.1.27)

### Fixed
- Corrected remote non-CW behavior so host transmit mode is communicated to connected clients via heartbeat telemetry, allowing clients to follow host CW vs non-CW state.
- Corrected remote client non-CW keying path to send PTT intent while suppressing local CW keyer/sidetone behavior when host is not in CW mode.
- Corrected host operating-page bottom status indicator behavior in non-CW mode:
  - Left indicator now represents PTT assertion state and turns green when asserted.
  - Right indicator is intentionally hidden in non-CW/PTT mode.
- Corrected transmit-mode synchronization to include non-CW to non-CW transitions (for example USB to LSB), so mode text updates immediately in the host operating status bar.
- Corrected remote client operating-page mode display to show connected host identity and active host mode on connect, instead of remaining on generic sidetone-only text.
- Corrected remote client non-CW LED behavior to follow host PTT-closure logic so the single non-CW indicator responds to either paddle/PTT closure path.

### Changed
- Program revision 2.1.27 implements the above PTT behavior corrections for host/client remote operation and status indication consistency.
- Program revision 2.1.27 also includes host-to-client transmit mode text propagation so the remote client bottom status bar mirrors host mode naming.

## 2026-07-29

### UI
- Completed Step 8 UI compaction and full-screen review updates across setup, operating, and dialog windows.
- Operating-page layout refinements in [Views/MainWindow.axaml](Views/MainWindow.axaml):
  - Host client table compacted to 3 rows.
  - Host IP column narrowed to IPv4-sized width.
  - Host and client status text moved from bottom bar into `Host Status` and `Client Status` title rows.
  - Host/client connected-state indicators shown in green in status fields.
  - `Disconnect` action moved into section headers for host/client modes.
  - Redundant operating-page `Exit` button removed.
- CW Settings compaction completed in [Views/MainWindow.axaml](Views/MainWindow.axaml):
  - Replaced speed/sidetone/pitch sliders with compact numeric controls.
  - Normalized control/label sizing and corrected radio-button clipping.
  - Reflowed controls into 3 dense rows:
    - Speed + Keyer Mode
    - Sidetone + Iambic Type
    - Pitch + Swap Paddles

### Behavior
- Exit paths are now unified to disconnect then shutdown for:
  - in-window Exit action,
  - File menu Exit,
  - window close (X) path,
  with close interception in [Views/MainWindow.axaml.cs](Views/MainWindow.axaml.cs).

### Sizing
- Main window sizing behavior updated for content-based autosize in setup and operating flows, including startup sizing correction in [Views/MainWindow.axaml.cs](Views/MainWindow.axaml.cs).
- Dialog sizing updates:
  - [Views/AboutWindow.axaml](Views/AboutWindow.axaml): dynamic content sizing.
  - [Views/AudioDeviceDialog.axaml](Views/AudioDeviceDialog.axaml): dynamic content sizing.
  - [Views/MidiConfigDialog.axaml](Views/MidiConfigDialog.axaml): dynamic content sizing with `MaxHeight` cap.

### Validation
- Repeated `dotnet build NetKeyer.csproj` validation completed successfully after each change set (no new blocking errors introduced).

## 2026-07-15

### Changed
- Remote client mapped-direct retry now uses a short timeout before relay fallback to avoid long OS TCP timeout delays in rendezvous negotiation.
- Relay pending-session timeout default increased from 10 seconds to 30 seconds in:
  - [rendezvous_services/relay/relay.py](rendezvous_services/relay/relay.py)
  - [rendezvous_services/docker-compose.yml](rendezvous_services/docker-compose.yml)
- Remote host stale-frame gating now uses normalized lag by default (instead of raw apparent age), eliminating false stale drops caused by constant client/host clock offset.
- Sender monotonic tick stale gating is staged behind host settings flag `RemoteHostUseSenderTickStaleGate` in `settings.json` (disabled by default).
- Exit command teardown in [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs) is now asynchronous and resilient:
  - avoids UI-thread blocking during remote/rendezvous shutdown,
  - suppresses setup-page SmartLink reconnect scheduling while exiting,
  - disconnects SmartLink WAN session during exit,
  - bounds `API.CloseSession()` with timeout so shutdown proceeds even if Flex session close stalls.

### Improved
- Host authentication failure logging now clearly indicates connection refusal due to shared token mismatch in [Services/Remote/RemoteClientSession.cs](Services/Remote/RemoteClientSession.cs).
- Client diagnostics are now clearer for rapid auth-refusal disconnects:
  - Always-on host error payload logging in [Services/Remote/RemoteClientService.cs](Services/Remote/RemoteClientService.cs).
  - Client status guard preserves explicit host error messages instead of immediately replacing them with generic EOF disconnect text.
- Host auth refusal messages now explicitly distinguish `shared token mismatch` vs `missing shared token` in [Services/Remote/RemoteClientSession.cs](Services/Remote/RemoteClientSession.cs).

### Troubleshooting
- Verified field behavior during WAN validation:
  - Shared token mismatch causes host-side connection refusal.
  - Host firewall policy (especially Windows Public profile inbound rules) can prevent direct and mapped-direct TCP success even when automatic mapping reports success.
  - Relay fallback remains functional when direct paths are blocked.
- Local/LAN validation confirmed clock skew can still impact stale-drop behavior when systems are significantly unsynchronized; normalized default gating and optional sender-tick gating reduce this failure mode.
- Added first-time remote setup checklist and expanded remote troubleshooting guidance in [README.md](README.md), including expected log signatures for token mismatch and firewall-related direct-path failures.

## 2026-07-08

### Added
- Three-stage rendezvous connection negotiation flow:
  - Initial direct transport attempt to rendezvous-provided host endpoint.
  - Automatic host port mapping request phase (`request_port_map` / `port_map_result`) using UPnP first, then NAT-PMP.
  - Deterministic relay fallback only when direct and mapped-direct attempts fail.
- Host-side automatic TCP mapping helper:
  - [Services/Remote/IHostPortMapper.cs](Services/Remote/IHostPortMapper.cs)
  - [Services/Remote/HostPortMapper.cs](Services/Remote/HostPortMapper.cs)

### Changed
- Rendezvous server protocol and orchestration now support mapping negotiation before relay fallback:
  - Added message handling/types in [rendezvous_services/server/models.py](rendezvous_services/server/models.py):
    - `request_port_map` (client->server and server->host)
    - `port_map_result` (host->server)
  - Added mapping-aware session state transitions in [rendezvous_services/server/state.py](rendezvous_services/server/state.py).
  - Updated orchestration in [rendezvous_services/server/websocket_handlers.py](rendezvous_services/server/websocket_handlers.py) to:
    - request host mapping after direct timeout/fail,
    - emit updated `host_endpoint` on mapping success,
    - fall back to `use_relay` when mapping is unavailable or fails.
- NetKeyer rendezvous client/host integration now includes map-request and mapped-endpoint retry handling:
  - [Services/Rendezvous/RendezvousControlModels.cs](Services/Rendezvous/RendezvousControlModels.cs)
  - [Services/Rendezvous/IRendezvousControlService.cs](Services/Rendezvous/IRendezvousControlService.cs)
  - [Services/Rendezvous/RendezvousControlService.cs](Services/Rendezvous/RendezvousControlService.cs)
  - [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs)
- Connection success logging is now explicit and always-on for operational visibility:
  - [Services/Remote/RemoteClientService.cs](Services/Remote/RemoteClientService.cs)
  - [Services/Remote/RemoteHostService.cs](Services/Remote/RemoteHostService.cs)
  - Added transport labels in connection success logs: `direct`, `mapped-direct`, `relay`.
- CW operating controls now persist in settings only for Remote Client mode:
  - `CwSpeed`, `SidetoneVolume`, and `CwPitch` are saved/restored for client mode sessions.
  - Host and standalone flows remain radio-driven and do not persist these values locally.
- Setup and operating UI updates in [Views/MainWindow.axaml](Views/MainWindow.axaml) + [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs):
  - Rendezvous input changed from a single URL field to separate `Redezvous Server` and `Port` fields.
  - Rendezvous URL is now generated in code as `http://<server>:<port>` (default port `49923`) and legacy saved URL values are parsed into the new fields.
  - Setup label copy refined:
    - `Redezvous Server` renamed to `ID Server`.
    - Client-side `Selected Host ID` renamed to `Host ID`.
  - Spinner arrows removed from both setup `Port` input controls for the ID server endpoint fields.
  - Remote mode selection labels simplified to `Remote Client` and `Remote Host` (removed `(Computer #1)` / `(Computer #2)`).
  - Operating page section labels renamed to `Host Status` and `Client Status`.
- UI branding updates across window titles and About dialog:
  - Main and dialog window titles now use `NetKeyer+Remote` branding.
  - About dialog title and app name updated to `NetKeyer+Remote`.
  - About credits updated to:
    - `by Eric NR4O`
    - `forked from NetKeyer by Andrew KC2G and contributors`
  - `Check for Updates` button was intentionally disabled in this revision pending update-location configuration.

### Reliability
- Rendezvous server test suite expanded and passing:
  - `uv run python -m unittest discover -s server/tests -v`
  - Result: 23 tests passed.
- WAN validation confirmed mapped-endpoint negotiation path is operational before relay fallback.

## 2026-07-07

### Added
- Rendezvous Phase 1 websocket handler integration tests in [rendezvous_services/server/tests/test_websocket_handlers.py](rendezvous_services/server/tests/test_websocket_handlers.py), including:
  - Connect orchestration signaling checks (`incoming_client`, `host_endpoint`, `start_punch`).
  - Host capacity rejection (`host_full`).
  - Not-registered and unknown-session rejection paths.
  - Timeout fallback signaling (`use_relay`) to both endpoints.
  - Disconnect cleanup validation for session removal and host capacity decrement.
  - Direct-success arbitration validation (no relay fallback after successful punch).
  - Duplicate host/client re-registration replacement semantics.
- Rendezvous runtime pinning/config files:
  - [rendezvous_services/.python-version](rendezvous_services/.python-version) (`3.11`).
  - [rendezvous_services/pyproject.toml](rendezvous_services/pyproject.toml) with pinned Python/dependency ranges.
- Phase 2 relay runtime scaffold in [rendezvous_services/relay/relay.py](rendezvous_services/relay/relay.py):
  - `SESSION <session_id> <role>` handshake parsing.
  - Session pairing and bidirectional byte forwarding.
  - Pending-session timeout garbage collection.
  - Disconnect/error cleanup for both peers.
- Relay test coverage in [rendezvous_services/relay/tests/test_relay.py](rendezvous_services/relay/tests/test_relay.py):
  - Invalid handshake rejection.
  - Pairing/forwarding and reversed connect order.
  - Timeout cleanup and disconnect propagation.
  - Duplicate-role rejection error-text assertion.
  - Sustained relay throughput/stability test.
- Initial Phase 3 NetKeyer rendezvous integration:
  - Added rendezvous signaling service in [Services/Rendezvous/RendezvousControlService.cs](Services/Rendezvous/RendezvousControlService.cs).
  - Added rendezvous control models/interface in [Services/Rendezvous/RendezvousControlModels.cs](Services/Rendezvous/RendezvousControlModels.cs) and [Services/Rendezvous/IRendezvousControlService.cs](Services/Rendezvous/IRendezvousControlService.cs).
  - Added persisted rendezvous settings in [Models/UserSettings.cs](Models/UserSettings.cs) (`RemoteUseRendezvous`, `RemoteRendezvousServerUrl`, `RemoteRendezvousHostId`).
  - Added rendezvous host discovery (`list_hosts`) support and client-side host selection wiring.
- Completed Phase 3 relay fallback runtime integration:
  - Added relay handshake support in [Services/Remote/RemoteClientService.cs](Services/Remote/RemoteClientService.cs) (`SESSION <session_id> CLIENT`).
  - Added host relay transport dial-out support in [Services/Remote/RemoteHostService.cs](Services/Remote/RemoteHostService.cs) and [Services/Remote/IRemoteHostService.cs](Services/Remote/IRemoteHostService.cs) (`SESSION <session_id> HOST`).
  - Added rendezvous host relay callback wiring in [Services/Rendezvous/RendezvousControlModels.cs](Services/Rendezvous/RendezvousControlModels.cs) and [Services/Rendezvous/RendezvousControlService.cs](Services/Rendezvous/RendezvousControlService.cs).
- Completed Phase 4 deployment packaging assets:
  - Added [rendezvous_services/server/Dockerfile](rendezvous_services/server/Dockerfile) for rendezvous container runtime.
  - Added [rendezvous_services/relay/Dockerfile](rendezvous_services/relay/Dockerfile) for relay container runtime.
  - Added [rendezvous_services/docker-compose.yml](rendezvous_services/docker-compose.yml) with `relay`, `rendezvous`, and optional `nginx` profile.
  - Added nginx snippets [rendezvous_services/nginx/rendezvous.conf](rendezvous_services/nginx/rendezvous.conf) (WebSocket proxy) and [rendezvous_services/nginx/stream-relay.conf](rendezvous_services/nginx/stream-relay.conf) (TCP stream proxy).

### Changed
- Rendezvous relay default port updated to `49921` in:
  - [rendezvous_services/server/main.py](rendezvous_services/server/main.py)
  - [rendezvous_services/server/websocket_handlers.py](rendezvous_services/server/websocket_handlers.py)
- Websocket hardening in [rendezvous_services/server/websocket_handlers.py](rendezvous_services/server/websocket_handlers.py):
  - Session ownership checks for host/client `punch_result` messages.
  - Explicit `session_mismatch` rejection for invalid host/client/session combinations.
  - Disconnect lifecycle cleanup now closes sessions owned by disconnecting host/client.
- [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs) now supports opt-in rendezvous-assisted remote startup:
  - Host registers with rendezvous on start.
  - Client resolves host endpoint through rendezvous before direct TCP connect.
  - Client sends initial punch result to rendezvous after connect attempt.
  - Client now defaults to discovered rendezvous host selection during connect, with manual host ID only as fallback.
- [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs) now performs direct-first remote client connect with automatic relay fallback when rendezvous emits `use_relay`.
- Setup UI in [Views/MainWindow.axaml](Views/MainWindow.axaml) now includes rendezvous URL/host settings and client host discovery refresh/select controls.
- Relay host advertisement for fallback signaling in [rendezvous_services/server/websocket_handlers.py](rendezvous_services/server/websocket_handlers.py) now derives an externally reachable host from websocket request headers when configured relay host is the internal Docker alias (`relay`), allowing host/client apps to use relay fallback without nginx stream proxy.
- Rendezvous runtime diagnostics and host/client status behavior improvements:
  - [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs) now emits always-on rendezvous startup/connect/fallback diagnostics via `DebugLogger.LogAlways`.
  - [Views/MainWindow.axaml](Views/MainWindow.axaml) + [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs) now show host waiting status and rendezvous status on the same operating-page line.
  - [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs) now normalizes IPv4-mapped IPv6 addresses in Host Client Status rows (removes `::ffff:` prefix from displayed IPv4 values).
- Rendezvous container runtime in [rendezvous_services/server/Dockerfile](rendezvous_services/server/Dockerfile) now installs `uvicorn[standard]` to ensure WebSocket upgrade support is present in Docker deployments.
- Relay fallback timing in [ViewModels/MainWindowViewModel.cs](ViewModels/MainWindowViewModel.cs) now caps rendezvous direct-connect attempts to 2 seconds before fallback to avoid relay pairing timeout churn and improve host/client relay session convergence.

### Reliability
- Rendezvous state cleanup improved in [rendezvous_services/server/state.py](rendezvous_services/server/state.py) via `close_sessions_for_host` and `close_sessions_for_client` helpers to prevent stale session/capacity leakage.
- Phase 1 server tests executed successfully with pinned runtime:
  - `uv run python -m unittest discover -s server/tests -v`
  - Result: 19 tests passed.
- Local route-level websocket simulation validated expected host-list and connect orchestration message flow through `/ws/host` and `/ws/client`.
- Relay validation completed:
  - `uv run python -m unittest discover -s relay/tests -v` (8 passed).
  - `uv run python -m unittest discover -v` (26 passed, including relay + server).
- NetKeyer relay integration validation completed:
  - `dotnet build NetKeyer.csproj` succeeded after relay fallback transport updates.
- Docker-hosted rendezvous/relay runtime validation:
  - Host registration and client host discovery verified against running Docker services.
  - Relay fallback path verified operational end-to-end (host/client keying data confirmed through relay transport).

## 2026-06-29

### Added
- Rendezvous Phase 0 contract artifacts under `rendezvous_services`:
  - Protocol contract freeze document and sequence-flow documentation.
  - Versioned JSON schemas for host, client, and server message sets.
  - Initial server-side Pydantic message models and endpoint-specific validators.
  - Schema-conformance tests for required fields, constraints, and endpoint message routing.

### Changed
- Remote telemetry lag measurement corrected for cross-system clock skew by keeping raw apparent-age values and normalizing to a per-client baseline before UI/log reporting.
- Telemetry labels clarified to distinguish normalized lag metrics in logs while keeping concise labels in UI.

### Improved
- Remote telemetry entries are now logged by default under `remote-telemetry` without requiring NETKEYER_DEBUG category configuration.
- Telemetry display upgraded to high-contrast bold magenta for improved visibility.
- Telemetry UI reformatted to two lines to reduce truncation:
  - Line 1: last lag, avg lag, max lag
  - Line 2: accepted 60s, stale
  - Second line aligned after the telemetry label colon.

### Reliability
- Telemetry values now track WAN delay variation more accurately (instead of appearing as persistent zeros under clock-offset conditions).

## 2026-06-24 (Phase 1)

### Added
- Initial Remote Client/Remote Host transport foundation over TCP with length-prefixed JSON frames.
- Remote protocol envelope and message set: `hello`, `auth`, `heartbeat`, `paddleState`, `disconnect`, `error`.
- Setup page remote-mode controls for Standalone, Remote Client, and Remote Host operation.
- Remote host settings for bind address, listen port, shared token, and maximum client count.

### Changed
- Input pipeline extended so client-mode paddle/straight/PTT events are transmitted remotely to host.
- Host-mode keying path integrated to consume remote paddle events and drive keying controller.

### Reliability
- Default remote port standardized to `49920`.
- Incremental build-and-test cycles used to validate remote integration after each change set.

## 2026-06-25 (Phase 1)

### Added
- Two-machine LAN smoke-test validation workflow and execution tracking.
- Host/client identity fields in remote setup and status views (callsign and host name).
- Host-side client status/history view with IP, callsign, connection status, and last active timestamp.

### Changed
- Host-mode sidetone behavior refined so local host mute does not force SmartSDR/Flex monitor gain mute path.
- Remote status UX expanded for clearer operating-state visibility in setup and operating pages.

### Improved
- Native shim and managed build validation completed successfully on this environment.
- LAN reachability and baseline remote keying path validated in host/client workflow.

## 2026-06-26

### Added
- Remote Host active-client ownership lock to prevent simultaneous multi-client keying contention.
- Configurable client ownership hold time in setup UI (`Client Hold Time`), range 0.5s to 30.0s in 0.5s increments.
- Client-mode operating `Client Host Status` block with host IP, host name, and compact status row.
- Host-mode and client-mode compact telemetry summary lines in operating view.
- Heartbeat telemetry path from host to client so client-side telemetry displays live host metrics.

### Changed
- Setup page button layout updated to place `Exit` on the same row as `Connect` and `Connect by IP...`.
- Main window dimensions reduced and unified across setup and operating windows.
- Host/client status display formatting refined (IP normalization and concise status text for client view).
- Operating status bar contrast updated for readability in both light and dark desktop themes.

### Improved
- Reconnect identity handling improved to reduce duplicate client rows in host history display.
- Host client history behavior improved with connected-first sorting and stale disconnected entry cleanup.
- Host telemetry now includes:
  - Last lag
  - Avg lag
  - Max lag over the last 60 seconds
  - Accepted frames in the last 60 seconds
  - Stale dropped frame count
- Rolling 60-second telemetry metrics now age out during idle periods (for example, `accepted 60s` returns to 0 when no frames are received in the window).

### Reliability
- Stale-frame drop policy added on host receive path to reject delayed paddle frames before keying.
- Ongoing successful `dotnet build` validation maintained after each remote-feature change set.
