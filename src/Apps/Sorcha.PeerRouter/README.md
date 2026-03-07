# Sorcha Peer Router

A standalone, lightweight P2P network rendezvous and debugging tool for the Sorcha peer network. The Peer Router operates independently from the main Sorcha platform — no database, no Redis, no message broker, no Aspire orchestration. Just a single process that peers connect to for network bootstrapping and diagnostics.

## Overview

The Peer Router serves two primary purposes:

1. **Network Bootstrap** — Peers connect to the router as a seed node, register themselves, and discover other peers on the network. Once peers know about each other, they communicate directly.

2. **Network Debugging** — A real-time event stream (SSE) and browser-accessible debug page show every peer connection, disconnection, heartbeat, and relay event as it happens. Essential for diagnosing connectivity issues during development.

## Quick Start

```bash
# Run with defaults (gRPC: 5000, HTTP: 8080)
dotnet run --project src/Apps/Sorcha.PeerRouter

# Run with custom ports
dotnet run --project src/Apps/Sorcha.PeerRouter -- --port 5500 --http-port 8500

# Run with relay mode enabled
dotnet run --project src/Apps/Sorcha.PeerRouter -- --enable-relay

# Run via Docker
docker build -t sorcha-peer-router -f src/Apps/Sorcha.PeerRouter/Dockerfile .
docker run -p 5500:5000 -p 8500:8080 sorcha-peer-router

# Run via Docker with environment configuration
docker run -p 5500:5000 -p 8500:8080 \
  -e PEERROUTER__ENABLE_RELAY=true \
  -e PEERROUTER__NODE_NAME=production-router \
  -e PEERROUTER__PEER_TIMEOUT=120 \
  sorcha-peer-router
```

Once running, open `http://localhost:8080` (or your configured HTTP port) to view the debug page, or connect Peer Service instances with the router as their seed node.

## Configuration

All settings can be configured via **CLI arguments**, **environment variables**, or both. CLI arguments take precedence over environment variables.

### Configuration Reference

| Setting | CLI Argument | Environment Variable | Default | Description |
|---------|-------------|---------------------|---------|-------------|
| gRPC Port | `--port <port>` | `PEERROUTER__PORT` | `5000` | Port for gRPC services (peer discovery, heartbeat, relay) |
| HTTP Port | `--http-port <port>` | `PEERROUTER__HTTP_PORT` | `8080` | Port for HTTP endpoints (debug page, events, health, peers) |
| Relay Mode | `--enable-relay` | `PEERROUTER__ENABLE_RELAY` | `false` | Enable message relay between peers |
| Event Buffer | `--event-buffer <size>` | `PEERROUTER__EVENT_BUFFER` | `1000` | Maximum number of events kept in the circular buffer |
| Peer Timeout | `--peer-timeout <seconds>` | `PEERROUTER__PEER_TIMEOUT` | `60` | Seconds without heartbeat before a peer is marked unhealthy |
| Node Name | `--node-name <name>` | `PEERROUTER__NODE_NAME` | `peer-router` | Human-readable name for this router instance |

### Configuration Precedence

```
CLI arguments  →  Environment variables  →  Defaults
  (highest)          (middle)               (lowest)
```

### Examples

**Development (local):**
```bash
dotnet run --project src/Apps/Sorcha.PeerRouter -- \
  --port 5500 --http-port 8500 --node-name dev-router --enable-relay
```

**Docker (production-like):**
```bash
docker run -d --name peer-router \
  -p 5500:5000 \
  -p 8500:8080 \
  -e PEERROUTER__NODE_NAME=prod-router-eu-1 \
  -e PEERROUTER__PEER_TIMEOUT=120 \
  -e PEERROUTER__EVENT_BUFFER=5000 \
  sorcha-peer-router:latest
```

**Docker Compose (standalone):**
```yaml
# docker-compose.peer-router.yml
services:
  peer-router:
    build:
      context: .
      dockerfile: src/Apps/Sorcha.PeerRouter/Dockerfile
    image: sorchadev/peer-router:latest
    ports:
      - "5500:5000"   # gRPC
      - "8500:8080"   # HTTP (debug page)
    environment:
      PEERROUTER__NODE_NAME: my-router
      PEERROUTER__ENABLE_RELAY: "true"
      PEERROUTER__PEER_TIMEOUT: "90"
      PEERROUTER__EVENT_BUFFER: "2000"
```

## Architecture

```
┌──────────────────────────────────────────────────┐
│                  Peer Router                      │
│                                                   │
│  gRPC Services (port 5000)                        │
│  ├── RouterDiscoveryService   ← peer registration │
│  ├── RouterHeartbeatService   ← health tracking   │
│  └── RouterCommunicationService ← relay (opt-in)  │
│                                                   │
│  HTTP Endpoints (port 8080)                       │
│  ├── GET /health              ← health check      │
│  ├── GET /peers               ← routing table     │
│  ├── GET /events              ← event snapshot     │
│  ├── GET /events?follow=true  ← SSE live stream   │
│  └── GET /                    ← index.html page    │
│                                                   │
│  Core Services (in-memory, no external deps)      │
│  ├── RoutingTable   (ConcurrentDictionary)        │
│  ├── EventBuffer    (circular buffer + channels)  │
│  └── PeerTimeoutService (background sweep)        │
└──────────────────────────────────────────────────┘
         ▲              ▲              ▲
         │ gRPC         │ gRPC         │ gRPC
    ┌────┴────┐    ┌────┴────┐    ┌────┴────┐
    │ Peer A  │    │ Peer B  │    │ Peer C  │
    │ Service │◄──►│ Service │◄──►│ Service │
    └─────────┘    └─────────┘    └─────────┘
       direct P2P     direct P2P
```

**Key design principle:** The router is a rendezvous point, not a permanent intermediary. Peers discover each other through the router, then communicate directly. Relay mode is a development convenience for NAT/firewall edge cases, not a production architecture.

## HTTP Endpoints

### `GET /health`

Returns router health status, uptime, and peer counts.

```json
{
  "status": "Healthy",
  "uptime": "01:23:45.678",
  "totalPeers": 5,
  "healthyPeers": 4,
  "relayEnabled": false,
  "eventBufferSize": 1000
}
```

### `GET /peers`

Returns the full routing table (healthy and unhealthy peers).

```json
{
  "totalPeers": 3,
  "healthyPeers": 2,
  "peers": [
    {
      "peerId": "peer-abc-123",
      "nodeName": null,
      "address": "10.0.0.5",
      "port": 5000,
      "isHealthy": true,
      "lastSeen": "2026-03-07T14:30:00Z",
      "heartbeatCount": 42,
      "advertisedRegisters": [
        { "registerId": "reg-001", "hasFullReplica": true, "latestVersion": 150 }
      ]
    }
  ]
}
```

### `GET /events`

Returns a JSON array snapshot of the event buffer (most recent events, up to buffer size).

```json
[
  {
    "id": "018e3f2a1b00abcd1234",
    "type": "PeerConnected",
    "timestamp": "2026-03-07T14:30:00Z",
    "peerId": "peer-abc-123",
    "ipAddress": "10.0.0.5",
    "port": 5000,
    "detail": {}
  }
]
```

### `GET /events?follow=true`

Server-Sent Events (SSE) stream. Returns buffered events immediately, then streams new events in real time.

```
data: {"id":"018e3f2a1b00abcd1234","type":"PeerConnected","timestamp":"2026-03-07T14:30:00Z",...}

data: {"id":"018e3f2a1c00efgh5678","type":"PeerHeartbeat","timestamp":"2026-03-07T14:30:05Z",...}
```

**Usage with curl:**
```bash
curl -N http://localhost:8080/events?follow=true
```

### Debug Page (`/`)

A static HTML page that connects to the SSE event stream and displays:
- Live event feed with peer identification
- Connected peer count
- Routing table contents
- Auto-reconnect on connection loss

## gRPC Services

### PeerDiscovery (RouterDiscoveryService)

Implements the `PeerDiscovery` proto service for network bootstrap.

| RPC | Purpose |
|-----|---------|
| `RegisterPeer` | Register or update a peer in the routing table |
| `GetPeerList` | Get healthy peers (excludes the requester) |
| `Ping` | Update last-seen timestamp, check peer status |
| `ExchangePeers` | Gossip-style peer list exchange |
| `FindPeersForRegister` | Find peers holding a specific register |

### PeerHeartbeat (RouterHeartbeatService)

Implements the `PeerHeartbeat` proto service for health tracking.

| RPC | Purpose |
|-----|---------|
| `SendHeartbeat` | Unary heartbeat with metrics and register versions |
| `StreamHeartbeat` | Bidirectional streaming heartbeat |

### PeerCommunication (RouterCommunicationService) — Relay Mode Only

Only available when relay mode is enabled (`--enable-relay` or `PEERROUTER__ENABLE_RELAY=true`).

| RPC | Purpose |
|-----|---------|
| `SendMessage` | Forward a message from sender to recipient via the router |
| `Stream` | Not supported (returns `UNIMPLEMENTED`) |

**Relay behavior:**
- Looks up the recipient in the routing table
- Creates a gRPC client channel to the recipient's address
- Forwards the original message
- Emits a `RelayForwarded` event to the debug stream
- Returns `FAILED_PRECONDITION` if relay is disabled
- Returns `NOT_FOUND` if the recipient is unknown or unhealthy
- Returns `UNAVAILABLE` if the recipient cannot be reached

## Event Types

| Type | When | Detail Fields |
|------|------|---------------|
| `PeerConnected` | Peer registers for the first time | — |
| `PeerDisconnected` | Peer times out (no heartbeat within timeout) | `reason` |
| `PeerHeartbeat` | Peer sends a ping or heartbeat | — |
| `RegisterAdvertised` | Peer advertises registers during registration | `registers` |
| `PeerListRequested` | Peer requests the peer list | — |
| `PeerExchanged` | Gossip peer exchange completed | `received_count`, `returned_count` |
| `RelayForwarded` | Message relayed between peers (relay mode) | `recipient_peer_id`, `message_type`, `payload_size` |
| `Error` | An error occurred processing a request | `error` |

## Peer Timeout and Health

The router runs a background sweep at regular intervals (`max(timeout/3, 5 seconds)`). Any peer that hasn't sent a heartbeat or ping within the configured timeout is marked unhealthy:

- Unhealthy peers are excluded from `GetPeerList` and `FindPeersForRegister` responses
- Unhealthy peers remain in the routing table (visible via `GET /peers` and the debug page)
- If an unhealthy peer sends a heartbeat, it is automatically marked healthy again
- A `PeerDisconnected` event is emitted when a peer becomes unhealthy

## Connecting Peer Service Instances

Configure Peer Service instances to use the router as a seed node:

```json
{
  "PeerService": {
    "SeedNodes": ["http://peer-router-host:5500"],
    "NodeId": "peer-unique-id"
  }
}
```

Or via environment variable:
```bash
PeerService__SeedNodes__0=http://peer-router-host:5500
```

## Project Structure

```
src/Apps/Sorcha.PeerRouter/
├── GrpcServices/
│   ├── RouterDiscoveryService.cs      # Peer registration and discovery
│   ├── RouterHeartbeatService.cs      # Health tracking via heartbeats
│   └── RouterCommunicationService.cs  # Optional relay forwarding
├── Endpoints/
│   ├── EventStreamEndpoints.cs        # GET /events (snapshot + SSE)
│   ├── PeerEndpoints.cs               # GET /peers (routing table)
│   └── HealthEndpoints.cs             # GET /health
├── Models/
│   ├── RouterConfiguration.cs         # CLI + env var parsing
│   ├── RouterEvent.cs                 # Event record with time-sortable ID
│   ├── RouterEventType.cs             # Event type enum
│   └── RoutingEntry.cs                # Peer entry in routing table
├── Services/
│   ├── RoutingTable.cs                # Thread-safe peer registry
│   ├── EventBuffer.cs                 # Circular buffer with SSE fan-out
│   └── PeerTimeoutService.cs          # Background unhealthy peer sweep
├── wwwroot/
│   └── index.html                     # Browser debug page
├── Properties/
│   └── launchSettings.json            # VS/Rider launch profiles
├── Dockerfile                         # Multi-stage Docker build
└── Program.cs                         # Entry point
```

## Testing

```bash
# Run all PeerRouter tests (77 tests)
dotnet test tests/Sorcha.PeerRouter.Tests

# Run specific test class
dotnet test tests/Sorcha.PeerRouter.Tests --filter "FullyQualifiedName~RouterCommunicationService"
```

Test coverage includes:
- Routing table operations (register, touch, sweep, query)
- Event buffer (circular capacity, SSE fan-out, concurrent subscribers)
- Peer timeout service (sweep intervals, unhealthy marking)
- Discovery gRPC service (register, list, ping, exchange, find-by-register)
- Heartbeat gRPC service (unary and bidirectional streaming)
- Communication gRPC service (relay enabled/disabled, validation, forwarding)
- HTTP endpoints (health, peers, events snapshot and SSE)

## Deployment

The Peer Router is designed to be deployed independently from the Sorcha platform:

```bash
# Build the Docker image
docker build -t sorcha-peer-router -f src/Apps/Sorcha.PeerRouter/Dockerfile .

# Run standalone
docker run -d \
  --name peer-router \
  -p 5500:5000 \
  -p 8500:8080 \
  -e PEERROUTER__NODE_NAME=router-eu-west-1 \
  -e PEERROUTER__PEER_TIMEOUT=120 \
  sorcha-peer-router:latest

# Health check
curl http://localhost:8500/health
```

**No external dependencies required** — the router runs entirely in-memory with no database, cache, or message broker.

---

**Version:** 1.0.0 | **Updated:** 2026-03-07 | **License:** MIT
