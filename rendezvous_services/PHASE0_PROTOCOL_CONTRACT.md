# Phase 0 Protocol Contract

<!-- markdownlint-disable MD022 MD032 -->

## Purpose
This document freezes the control-plane contract for NetKeyer rendezvous signaling before Phase 1 implementation.

## Versioning
- protocol_version: 1
- Transport: WebSocket JSON messages
- Endpoint roles:
  - Host endpoint: /ws/host
  - Client endpoint: /ws/client

## Envelope
All messages must include:
- type: message discriminator string
- protocol_version: integer (default 1 if omitted during transition)

Example envelope shape:
- type: register_host
- protocol_version: 1
- ...message-specific fields

## Endpoint Message Permissions
- /ws/host accepts:
  - register_host
  - punch_result
- /ws/client accepts:
  - register_client
  - list_hosts
  - connect_request
  - punch_result

Messages sent server -> host:
- incoming_client
- start_punch
- use_relay
- error

Messages sent server -> client:
- host_list
- host_endpoint
- start_punch
- use_relay
- error

## Message Definitions
Host/client to server:
- register_host:
  - host_id: string, required, length 1-128
  - max_clients: integer, required, range 1-5
  - metadata: object, optional
- register_client:
  - client_id: string, required, length 1-128
- list_hosts:
  - no additional required fields
- connect_request:
  - client_id: string, required
  - host_id: string, required
- punch_result:
  - success: boolean, required
  - client_id: string, required
  - host_id: string, required
  - session_id: string, required

Server to host/client:
- host_list:
  - hosts: array of host summaries
- incoming_client:
  - client_id: string
  - client_public_ip: string (IPv4/IPv6 text)
  - client_public_port: integer 1-65535
  - session_id: string
- host_endpoint:
  - host_id: string
  - host_public_ip: string
  - host_public_port: integer 1-65535
  - session_id: string
- start_punch:
  - session_id: string
- use_relay:
  - relay_host: string
  - relay_port: integer 1-65535
  - session_id: string
- error:
  - code: string
  - message: string
  - session_id: optional string

## Validation and Error Semantics
- Unknown message type:
  - server responds error with code unsupported_message_type
- Message disallowed on endpoint role:
  - code invalid_endpoint_message
- Invalid payload/schema:
  - code invalid_payload
- Unknown host/client/session references:
  - code not_found
- Host at capacity:
  - code host_full
- Internal failures:
  - code internal_error

Error payload shape:
- type: error
- protocol_version: 1
- code: string
- message: string
- session_id: optional

## Session State Machine
Session states:
- requested
- punch_signaled
- direct_connected
- relay_requested
- relay_connected
- closed

Transitions:
- connect_request -> requested
- server emits incoming_client/host_endpoint/start_punch -> punch_signaled
- punch_result success from either side -> direct_connected
- punch timeout or dual failure -> relay_requested + use_relay
- relay established -> relay_connected
- disconnect/timeout -> closed

## Timeouts
- Punch timeout: 2 seconds from start_punch emission.
- Incomplete session TTL: 30 seconds.
- WebSocket idle heartbeat policy: to be finalized in Phase 1 implementation notes.

## Compatibility Note
Phase 1 implementation should accept missing protocol_version during transition and treat as version 1.
