# Peer Router API Contract

**Feature**: 053-peer-router-completion
**Date**: 2026-03-07

## HTTP Endpoints

### GET /

Static HTML debug page. Serves embedded HTML that connects to `/events?follow=true` via EventSource and displays live network events plus current routing table.

**Response**: `text/html`, 200 OK

---

### GET /events?follow=true

Server-Sent Events stream of router events. When `follow=true`, the connection stays open and new events are pushed as they occur. Without `follow`, returns a JSON snapshot of the event buffer.

**SSE Response** (`follow=true`):
```
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive

data: {"id":"01HYXZ...","type":"PeerConnected","timestamp":"2026-03-07T10:15:30Z","peerId":"peer-abc","nodeName":"node-1.sorcha.dev","ipAddress":"192.168.1.10","port":5000,"detail":{"capabilities":{"supportsStreaming":true,"maxTransactionSize":10485760},"advertisedRegisters":["reg-001"]}}

data: {"id":"01HYXZ...","type":"PeerHeartbeat","timestamp":"2026-03-07T10:15:45Z","peerId":"peer-abc","nodeName":"node-1.sorcha.dev","ipAddress":"192.168.1.10","port":5000,"detail":{"sequenceNumber":1,"registerVersions":{"reg-001":42},"metrics":{"activeConnections":3,"cpuUsage":0.15}}}
```

**JSON Response** (no `follow`):
```json
[
  {
    "id": "01HYXZ...",
    "type": "PeerConnected",
    "timestamp": "2026-03-07T10:15:30Z",
    "peerId": "peer-abc",
    "nodeName": "node-1.sorcha.dev",
    "ipAddress": "192.168.1.10",
    "port": 5000,
    "detail": { ... }
  }
]
```

---

### GET /peers

Current routing table snapshot.

**Response**: 200 OK
```json
{
  "totalPeers": 3,
  "healthyPeers": 2,
  "peers": [
    {
      "peerId": "peer-abc",
      "nodeName": "node-1.sorcha.dev",
      "address": "192.168.1.10:5000",
      "ipAddress": "192.168.1.10",
      "port": 5000,
      "isHealthy": true,
      "firstSeen": "2026-03-07T10:15:30Z",
      "lastSeen": "2026-03-07T10:16:00Z",
      "heartbeatCount": 4,
      "advertisedRegisters": ["reg-001", "reg-002"],
      "capabilities": {
        "supportsStreaming": true,
        "supportsTransactionDistribution": true,
        "maxTransactionSize": 10485760
      }
    }
  ]
}
```

---

### GET /health

Simple health check.

**Response**: 200 OK
```json
{
  "status": "Healthy",
  "uptime": "00:45:30",
  "totalPeers": 3,
  "healthyPeers": 2,
  "relayEnabled": false,
  "eventBufferSize": 127
}
```

---

## gRPC Services

The Peer Router implements the same gRPC services defined in the Peer Service proto files. Peers connect to the router using the standard peer discovery protocol — no protocol changes needed.

### PeerDiscovery (peer_discovery.proto)

| RPC | Router Behavior |
|-----|----------------|
| RegisterPeer | Add/update peer in routing table, emit PeerConnected event |
| GetPeerList | Return healthy peers from routing table |
| Ping | Update peer LastSeen, emit PeerHeartbeat event, respond OK |
| ExchangePeers | Accept peer's known peers, return router's known peers, emit PeerExchanged |
| FindPeersForRegister | Filter routing table by register ID |

### PeerHeartbeat (peer_heartbeat.proto)

| RPC | Router Behavior |
|-----|----------------|
| SendHeartbeat | Update peer LastSeen and register versions, emit PeerHeartbeat event |
| StreamHeartbeat | Accept bidirectional stream, update peer on each heartbeat, respond with router state |

### PeerCommunication (peer_communication.proto) — Relay Mode Only

| RPC | Router Behavior |
|-----|----------------|
| SendMessage | Look up recipient in routing table, forward message, emit RelayForwarded event. Returns error if relay disabled or recipient unknown. |
| Stream | Not implemented (relay only supports unary forwarding) |

---

## Command-Line Interface

```
Sorcha.PeerRouter [options]

Options:
  --port <port>           gRPC listen port (default: 5000)
  --http-port <port>      HTTP port for debug page and events (default: 8080)
  --enable-relay          Enable message relay/forwarding between peers
  --event-buffer <size>   Max events to buffer (default: 1000)
  --peer-timeout <secs>   Seconds before marking peer unhealthy (default: 60)
  --help                  Show help
  --version               Show version
```
