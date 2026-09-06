# Phase 0 Sequence Flows

<!-- markdownlint-disable MD022 MD031 -->

## 1. Host Register and Stay Online
```mermaid
sequenceDiagram
    participant H as Host App
    participant R as Rendezvous Server
    H->>R: register_host(host_id, max_clients, metadata)
    R-->>H: ack (implicit or status message)
    Note over H,R: Host remains connected via WebSocket
```

## 2. Client List Hosts and Connect Request
```mermaid
sequenceDiagram
    participant C as Client App
    participant R as Rendezvous Server
    C->>R: register_client(client_id)
    C->>R: list_hosts
    R-->>C: host_list(host summaries)
    C->>R: connect_request(client_id, host_id)
```

## 3. Punch Success Path
```mermaid
sequenceDiagram
    participant C as Client App
    participant H as Host App
    participant R as Rendezvous Server
    C->>R: connect_request
    R-->>H: incoming_client(client endpoint, session_id)
    R-->>C: host_endpoint(host endpoint, session_id)
    R-->>H: start_punch(session_id)
    R-->>C: start_punch(session_id)
    H->>C: TCP punch attempts/listen
    C->>H: TCP punch attempts/listen
    H->>R: punch_result(success=true, session_id)
    R-->>C: optional connected notification (future)
    Note over H,C: Direct transport established
```

## 4. Punch Timeout to Relay Fallback
```mermaid
sequenceDiagram
    participant C as Client App
    participant H as Host App
    participant R as Rendezvous Server
    C->>R: connect_request
    R-->>H: incoming_client + start_punch
    R-->>C: host_endpoint + start_punch
    Note over H,C: No direct TCP established within 2 seconds
    H->>R: punch_result(success=false, session_id)
    C->>R: punch_result(success=false, session_id)
    R-->>H: use_relay(relay_host, relay_port, session_id)
    R-->>C: use_relay(relay_host, relay_port, session_id)
    H->>Relay: SESSION <id> HOST
    C->>Relay: SESSION <id> CLIENT
    Note over H,C: Relay transport established
```

## 5. Error Flow for Invalid Message
```mermaid
sequenceDiagram
    participant X as Host/Client
    participant R as Rendezvous Server
    X->>R: invalid message
    R-->>X: error(code=invalid_payload or unsupported_message_type)
```
