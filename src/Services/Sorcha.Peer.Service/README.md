# Sorcha Peer Service

**Version**: 1.1.0
**Status**: Complete (100% MVD)
**Framework**: .NET 10.0
**Architecture**: Microservice (gRPC + REST)
**Last Updated**: 2026-03-01

---

## Overview

The **Peer Service** enables distributed system register replication across the Sorcha platform through a hub node architecture. Peer nodes connect to hub nodes (n0, n1, n2.sorcha.dev) to replicate the system register containing published blueprints, with automatic failover, heartbeat monitoring, and push notifications.

This service provides:
- **Central node connection** with priority-based failover (n0→n1→n2)
- **System register replication** (full sync + incremental sync)
- **Heartbeat monitoring** (30s interval, 60s timeout triggers failover)
- **Push notifications** for blueprint publication events
- **Isolated mode** for graceful degradation when hub nodes are unreachable
- **Comprehensive observability** (7 OpenTelemetry metrics, 6 distributed traces, structured logging)

### Key Features

- ✅ **Hub Node Detection**: Hybrid detection using config flags + optional hostname validation
- ✅ **Priority-Based Connection**: Connects to n0 (priority 0) → n1 (priority 1) → n2 (priority 2) with automatic failover
- ✅ **Exponential Backoff**: Polly v8 resilience pipeline with jitter (1s, 2s, 4s, 8s, 16s, 32s, 60s max)
- ✅ **Full Sync**: Initial system register synchronization via gRPC server streaming
- ✅ **Incremental Sync**: Periodic sync (5 minutes) fetching only new blueprints since last version
- ✅ **Push Notifications**: Real-time notifications when blueprints are published (80% delivery target)
- ✅ **Heartbeat Monitoring**: 30-second heartbeat interval, failover after 2 missed heartbeats (60s)
- ✅ **Isolated Mode**: Continues serving cached blueprints when all hub nodes are unreachable
- ✅ **MongoDB Repository**: System register storage with auto-increment versioning
- ✅ **Thread-Safe Caching**: ConcurrentDictionary for in-memory blueprint cache
- ✅ **OpenTelemetry**: Full observability with metrics, traces, and structured logging

---

## Architecture

### Components

```
Peer Service
├── gRPC Layer (Port 5000)
│   ├── CentralNodeConnectionService (peer connections)
│   ├── SystemRegisterSyncService (full/incremental sync)
│   ├── HeartbeatService (heartbeat monitoring)
│   └── PeerDiscoveryService (legacy peer-to-peer)
├── REST Layer (Port 5001)
│   ├── GET /health - Health checks
│   ├── GET /api/peers - List active peers
│   ├── GET /api/peers/{id} - Get peer details
│   └── GET /api/central-connection - Central node connection status
├── Business Logic
│   ├── CentralNodeDiscoveryService - Detects if node is central or peer
│   ├── CentralNodeConnectionManager - Manages connection with failover
│   ├── SystemRegisterReplicationService - Orchestrates sync operations
│   ├── SystemRegisterCache - Thread-safe in-memory cache
│   ├── PeriodicSyncService - Background service for 5-minute sync
│   ├── PushNotificationHandler - Manages push notification subscribers
│   ├── HeartbeatMonitorService - Sends heartbeats every 30s
│   ├── PeerListManager - Tracks local peer status
│   └── SystemRegisterService - Initializes system register (hub nodes)
├── Data Layer
│   └── MongoSystemRegisterRepository - MongoDB storage with auto-increment versioning
└── Observability
    ├── PeerServiceMetrics - 7 OpenTelemetry metrics
    ├── PeerServiceActivitySource - 6 distributed traces
    └── Structured Logging - Correlation IDs and semantic properties
```

### Data Flow

**Peer Node Startup Flow:**
```
Node Startup
    ↓
CentralNodeDiscoveryService.DetectIfCentralNode() → IsCentralNode = false
    ↓
CentralNodeConnectionManager.ConnectToCentralNodeAsync()
    ↓
Try n0.sorcha.dev:5000 (priority 0)
    ↓
[Success] → CentralNodeConnectionService.ConnectToCentralNode (gRPC)
    ↓
Response: { SessionId, SystemRegisterVersion }
    ↓
SystemRegisterReplicationService.FullSyncAsync()
    ↓
SystemRegisterSyncService.FullSync (gRPC server streaming)
    ↓
Receive all blueprints → SystemRegisterCache
    ↓
PeriodicSyncService starts (5-minute interval)
    ↓
HeartbeatMonitorService starts (30-second interval)
    ↓
PushNotificationHandler.SubscribeToPushNotifications (gRPC streaming)
    ↓
[Operational] Peer receives blueprint publications in real-time
```

**Heartbeat Failover Flow:**
```
HeartbeatMonitorService sends heartbeat every 30s
    ↓
[Failure] No response from n0 (30s timeout)
    ↓
Increment MissedHeartbeats (1/2)
    ↓
[Failure] Second heartbeat fails
    ↓
MissedHeartbeats >= 2 → Trigger failover
    ↓
CentralNodeConnectionManager.FailoverToNextNodeAsync()
    ↓
Disconnect from n0 → Call DisconnectFromCentralNode (gRPC)
    ↓
Try n1.sorcha.dev:5000 (priority 1)
    ↓
[Success] → Connect to n1
    ↓
Full sync from n1 (reset SyncCheckpoint)
    ↓
Resume heartbeat monitoring (connected to n1)
```

**Isolated Mode Flow:**
```
All hub nodes (n0, n1, n2) unreachable
    ↓
CentralNodeConnectionManager.HandleIsolatedModeAsync()
    ↓
PeerListManager.UpdateLocalPeerStatus(null, Isolated)
    ↓
[Isolated Mode Active]
    ↓
Serve cached blueprints from SystemRegisterCache
    ↓
Background reconnection attempts every 60s
    ↓
[Central node returns] → Auto-reconnect
    ↓
Full sync to catch up on missed blueprints
    ↓
Resume normal operation
```

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** or later
- **MongoDB 8.0+** (for hub nodes)
- **Git**

### 1. Clone and Navigate

```bash
git clone https://github.com/yourusername/Sorcha.git
cd Sorcha/src/Services/Sorcha.Peer.Service
```

### 2. Configure Node Type

#### For Peer Nodes (Default)

Edit `appsettings.json`:

```json
{
  "CentralNode": {
    "IsCentralNode": false,
    "ValidateHostname": false,
    "CentralNodes": [
      { "Hostname": "n0.sorcha.dev", "Port": 5000, "Priority": 0 },
      { "Hostname": "n1.sorcha.dev", "Port": 5000, "Priority": 1 },
      { "Hostname": "n2.sorcha.dev", "Port": 5000, "Priority": 2 }
    ]
  }
}
```

#### For Hub Nodes

Edit `appsettings.json`:

```json
{
  "CentralNode": {
    "IsCentralNode": true,
    "ExpectedHostnamePattern": "n[0-2].sorcha.dev",
    "ValidateHostname": true
  },
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "sorcha_system_register"
  }
}
```

**Note**: Central nodes require MongoDB for system register storage.

### 3. Run the Service

#### Peer Node

```bash
dotnet run
```

Service will start at:
- **gRPC**: `http://localhost:5000` (CentralNodeConnection, SystemRegisterSync, Heartbeat)
- **REST**: `https://localhost:5001` (health checks, monitoring)
- **Scalar API Docs**: `https://localhost:5001/scalar/v1`

#### Hub Node (with MongoDB)

```bash
# Start MongoDB first
docker run -d -p 27017:27017 --name sorcha-mongo mongo:8.0

# Run service
dotnet run
```

### 4. Verify Connection

```bash
# Check connection status (peer node)
curl https://localhost:5001/api/central-connection

# Check health
curl https://localhost:5001/health

# List active peers (hub node)
curl https://localhost:5001/api/peers
```

### 5. Test gRPC Endpoints (Optional)

Using `grpcurl` (install from https://github.com/fullstorydev/grpcurl):

```bash
# List available gRPC services
grpcurl -plaintext localhost:5000 list

# Connect to hub node (peer node)
grpcurl -plaintext -d '{
  "peer_id": "test-peer",
  "peer_info": {
    "address": "localhost",
    "port": 5000,
    "node_type": "Peer",
    "supported_protocols": ["v1"]
  },
  "last_known_version": 0,
  "connection_time": 1702800000
}' localhost:5000 sorcha.peer.v1.CentralNodeConnection/ConnectToCentralNode

# Send heartbeat
grpcurl -plaintext -d '{
  "peer_id": "test-peer",
  "timestamp": 1702800000,
  "sequence_number": 1,
  "last_sync_version": 5
}' localhost:5000 sorcha.peer.v1.Heartbeat/SendHeartbeat
```

---

## Configuration

### appsettings.json Structure

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Sorcha.Peer.Service": "Debug",
      "Grpc": "Information"
    }
  },
  "AllowedHosts": "*",

  "CentralNode": {
    "IsCentralNode": false,
    "ExpectedHostnamePattern": "*.sorcha.dev",
    "ValidateHostname": false,
    "CentralNodes": [
      { "Hostname": "n0.sorcha.dev", "Port": 5000, "Priority": 0 },
      { "Hostname": "n1.sorcha.dev", "Port": 5000, "Priority": 1 },
      { "Hostname": "n2.sorcha.dev", "Port": 5000, "Priority": 2 }
    ]
  },

  "SystemRegister": {
    "PeriodicSyncIntervalMinutes": 5,
    "HeartbeatIntervalSeconds": 30,
    "HeartbeatTimeoutSeconds": 30,
    "MaxRetryAttempts": 10
  },

  "PeerService": {
    "Enabled": true,
    "NodeId": "peer-node-001",
    "ListenPort": 5001,
    "PeerDiscovery": {
      "BootstrapNodes": [],
      "RefreshIntervalMinutes": 15,
      "MaxPeersInList": 1000,
      "MinHealthyPeers": 5,
      "PeerTimeoutSeconds": 30
    }
  },

  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "sorcha_system_register",
    "CollectionName": "sorcha_system_register_blueprints"
  },

  "OpenTelemetry": {
    "ServiceName": "Sorcha.Peer.Service",
    "ZipkinEndpoint": "http://localhost:9411"
  }
}
```

### Environment Variables (Production)

```bash
# Node type
CENTRALNODE__ISCENTRALNODE=false
CENTRALNODE__VALIDATEHOSTNAME=false

# Central nodes (for peer nodes)
CENTRALNODE__CENTRALNODES__0__HOSTNAME=n0.sorcha.dev
CENTRALNODE__CENTRALNODES__0__PORT=5000
CENTRALNODE__CENTRALNODES__0__PRIORITY=0
CENTRALNODE__CENTRALNODES__1__HOSTNAME=n1.sorcha.dev
CENTRALNODE__CENTRALNODES__1__PORT=5000
CENTRALNODE__CENTRALNODES__1__PRIORITY=1
CENTRALNODE__CENTRALNODES__2__HOSTNAME=n2.sorcha.dev
CENTRALNODE__CENTRALNODES__2__PORT=5000
CENTRALNODE__CENTRALNODES__2__PRIORITY=2

# Sync configuration
SYSTEMREGISTER__PERIODICSYNCINTERVALMINUTES=5
SYSTEMREGISTER__HEARTBEATINTERVALSECONDS=30
SYSTEMREGISTER__HEARTBEATTIMEOUTSECONDS=30
SYSTEMREGISTER__MAXRETRYATTEMPTS=10

# MongoDB (for hub nodes)
MONGODB__CONNECTIONSTRING=mongodb://sorcha-mongo:27017
MONGODB__DATABASENAME=sorcha_system_register

# Observability
OPENTELEMETRY__ZIPKINENDPOINT=https://zipkin.yourcompany.com
```

### Configuration Reference

| Setting | Description | Default | Required |
|---------|-------------|---------|----------|
| `CentralNode:IsCentralNode` | Whether this node is a hub node | `false` | Yes |
| `CentralNode:ValidateHostname` | Validate hostname matches pattern | `false` | No |
| `CentralNode:ExpectedHostnamePattern` | Hostname regex pattern for hub nodes | `*.sorcha.dev` | No |
| `CentralNode:CentralNodes` | Array of hub node endpoints | `[]` | Yes (peer nodes) |
| `SystemRegister:PeriodicSyncIntervalMinutes` | Incremental sync interval | `5` | No |
| `SystemRegister:HeartbeatIntervalSeconds` | Heartbeat send interval | `30` | No |
| `SystemRegister:HeartbeatTimeoutSeconds` | Heartbeat timeout threshold | `30` | No |
| `SystemRegister:MaxRetryAttempts` | Max connection retry attempts | `10` | No |
| `MongoDB:ConnectionString` | MongoDB connection string | - | Yes (hub nodes) |
| `MongoDB:DatabaseName` | MongoDB database name | `sorcha_system_register` | Yes (hub nodes) |

---

## gRPC Services

### CentralNodeConnection Service

**Proto Definition**: `Protos/CentralNodeConnection.proto`

| Method | Description | Type | Request | Response |
|--------|-------------|------|---------|----------|
| `ConnectToCentralNode` | Initiate peer-to-central connection | Unary | `ConnectRequest` | `ConnectionResponse` |
| `DisconnectFromCentralNode` | Graceful disconnect | Unary | `DisconnectRequest` | `DisconnectionResponse` |
| `GetCentralNodeStatus` | Get hub node health | Unary | `StatusRequest` | `CentralNodeStatus` |

**ConnectRequest:**
```protobuf
message ConnectRequest {
  string peer_id = 1;                     // Unique peer identifier
  CentralNodePeerInfo peer_info = 2;      // Peer connection info
  int64 last_known_version = 3;           // Last sync version (0 if first)
  int64 connection_time = 4;              // Unix milliseconds UTC
}
```

**ConnectionResponse:**
```protobuf
message ConnectionResponse {
  bool success = 1;                       // Connection successful
  string message = 2;                     // Status message
  string session_id = 3;                  // Session identifier
  string central_node_id = 4;             // Central node ID (e.g., n0.sorcha.dev)
  int64 current_system_register_version = 5;  // Current version
  int64 connected_at = 6;                 // Unix milliseconds UTC
  int32 heartbeat_interval_seconds = 7;   // Recommended interval (30s)
  ConnectionConfig config = 8;            // Connection configuration
}
```

### SystemRegisterSync Service

**Proto Definition**: `Protos/SystemRegisterSync.proto`

| Method | Description | Type | Request | Response Stream |
|--------|-------------|------|---------|-----------------|
| `FullSync` | Initial full synchronization | Server Streaming | `FullSyncRequest` | `SystemRegisterEntry` |
| `IncrementalSync` | Incremental sync since version | Server Streaming | `IncrementalSyncRequest` | `SystemRegisterEntry` |
| `SubscribeToPushNotifications` | Real-time blueprint notifications | Server Streaming | `PushSubscriptionRequest` | `BlueprintNotification` |

**FullSyncRequest:**
```protobuf
message FullSyncRequest {
  string peer_id = 1;                     // Peer identifier
  string session_id = 2;                  // Session from connection
}
```

**SystemRegisterEntry:**
```protobuf
message SystemRegisterEntry {
  string blueprint_id = 1;                // Blueprint unique ID
  bytes blueprint_data = 2;               // Serialized BSON document
  int64 version = 3;                      // Auto-increment version
  int64 published_at = 4;                 // Unix milliseconds UTC
  string published_by = 5;                // Publisher wallet address
}
```

**IncrementalSyncRequest:**
```protobuf
message IncrementalSyncRequest {
  string peer_id = 1;                     // Peer identifier
  string session_id = 2;                  // Session from connection
  int64 last_known_version = 3;           // Version to sync from
}
```

### Heartbeat Service

**Proto Definition**: `Protos/Heartbeat.proto`

| Method | Description | Type | Request | Response |
|--------|-------------|------|---------|----------|
| `SendHeartbeat` | Send heartbeat to hub node | Unary | `HeartbeatMessage` | `HeartbeatAcknowledgement` |
| `MonitorHeartbeat` | Bidirectional heartbeat stream | Bidirectional Streaming | `HeartbeatMessage` | `HeartbeatAcknowledgement` |

**HeartbeatMessage:**
```protobuf
message HeartbeatMessage {
  string peer_id = 1;                     // Peer identifier
  int64 timestamp = 2;                    // Unix milliseconds UTC
  int32 sequence_number = 3;              // Monotonic sequence
  int64 last_sync_version = 4;            // Peer's last sync version
}
```

**HeartbeatAcknowledgement:**
```protobuf
message HeartbeatAcknowledgement {
  bool acknowledged = 1;                  // Heartbeat received
  int64 server_timestamp = 2;             // Server time (clock skew detection)
  RecommendedAction recommended_action = 3;  // Suggested action
}

enum RecommendedAction {
  RECOMMENDED_ACTION_NONE = 0;            // No action needed
  RECOMMENDED_ACTION_SYNC = 1;            // Perform incremental sync
  RECOMMENDED_ACTION_FAILOVER = 2;        // Failover to another node
  RECOMMENDED_ACTION_RECONNECT = 3;       // Reconnect (stale session)
}
```

---

## REST API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Health check endpoint |
| GET | `/api/peers` | List active peers (hub nodes) |
| GET | `/api/peers/{id}` | Get peer details by ID |
| GET | `/api/peers/connected` | Get count of connected peers (anonymous), full list if authenticated |
| GET | `/api/peers/health` | Get peer network health status |
| GET | `/api/peers/stats` | Get aggregated peer network statistics |
| GET | `/api/central-connection` | Central node connection status (peer nodes) |

### Connected Peers Endpoint

The `/api/peers/connected` endpoint provides different responses based on authentication:

**Anonymous Access:**
```json
{
  "connectedPeerCount": 5
}
```

**Authenticated Access:**
```json
{
  "connectedPeerCount": 5,
  "peers": [
    {
      "peerId": "peer-001",
      "address": "192.168.1.100",
      "port": 5000,
      "supportedProtocols": ["v1"],
      "lastSeen": "2025-12-16T10:30:00Z",
      "averageLatencyMs": 45,
      "isBootstrapNode": false
    }
  ]
}
```

---

## Development

### Project Structure

```
Sorcha.Peer.Service/
├── Program.cs                      # Service entry point, DI configuration
├── PeerService.cs                  # Background service orchestrating operations
├── Core/
│   ├── PeerServiceConfiguration.cs  # Configuration models
│   ├── CentralNodeConfiguration.cs
│   ├── SystemRegisterConfiguration.cs
│   ├── PeerServiceConstants.cs
│   ├── CentralNodeInfo.cs          # Central node state tracking
│   ├── SystemRegisterEntry.cs      # System register entry model
│   ├── HeartbeatMessage.cs         # Heartbeat protocol model
│   ├── ActivePeerInfo.cs           # Local peer status
│   ├── SyncCheckpoint.cs           # Sync progress tracking
│   ├── BlueprintNotification.cs    # Push notification model
│   └── Validators (5 classes)      # Business rule validators
├── Discovery/
│   ├── CentralNodeDiscoveryService.cs  # Central/peer detection
│   └── PeerListManager.cs          # Peer registry management
├── Connection/
│   └── CentralNodeConnectionManager.cs  # Connection + failover logic
├── Replication/
│   ├── SystemRegisterReplicationService.cs  # Sync orchestration
│   ├── SystemRegisterCache.cs      # Thread-safe in-memory cache
│   ├── PeriodicSyncService.cs      # Background periodic sync
│   └── PushNotificationHandler.cs  # Push notification management
├── Services/ (gRPC Implementations)
│   ├── CentralNodeConnectionService.cs  # CentralNodeConnection gRPC
│   ├── SystemRegisterSyncService.cs     # SystemRegisterSync gRPC
│   └── HeartbeatService.cs         # Heartbeat gRPC
├── Monitoring/
│   └── HeartbeatMonitorService.cs  # Heartbeat sender (peer nodes)
├── Resilience/
│   └── ConnectionResiliencePipeline.cs  # Polly v8 retry pipeline
├── Observability/
│   ├── PeerServiceMetrics.cs       # 7 OpenTelemetry metrics
│   └── PeerServiceActivitySource.cs  # 6 distributed traces
└── Protos/
    ├── CentralNodeConnection.proto
    ├── SystemRegisterSync.proto
    ├── Heartbeat.proto
    ├── peer_discovery.proto        # Legacy P2P
    ├── transaction_distribution.proto  # Legacy P2P
    └── peer_communication.proto    # Legacy P2P
```

### Register Service Integration

**MongoSystemRegisterRepository** (in Register Service):

Location: `src/Services/Sorcha.Register.Service/Repositories/MongoSystemRegisterRepository.cs`

| Method | Description |
|--------|-------------|
| `GetAllBlueprintsAsync()` | Full sync - retrieve all blueprints |
| `GetBlueprintsSinceVersionAsync(long version)` | Incremental sync - retrieve blueprints since version |
| `PublishBlueprintAsync(SystemRegisterEntry entry)` | Publish new blueprint (auto-increment version) |
| `GetLatestVersionAsync()` | Get current system register version |
| `IsSystemRegisterInitializedAsync()` | Check if system register exists |

**SystemRegisterService** (in Register Service):

Location: `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs`

| Method | Description |
|--------|-------------|
| `InitializeSystemRegisterAsync()` | Initialize system register with Guid.Empty ID |
| `SeedDefaultBlueprintsAsync()` | Seed default blueprints (register-creation-v1) |
| `PublishBlueprintAsync()` | Publish blueprint to system register |
| `ValidateSystemRegisterIntegrityAsync()` | Validate system register consistency |

### Running Tests

```bash
# Run all Peer Service tests
dotnet test tests/Sorcha.Peer.Service.Tests

# Run Register Service tests (includes MongoDB repository tests)
dotnet test tests/Sorcha.Register.Service.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Watch mode (auto-rerun on changes)
dotnet watch test --project tests/Sorcha.Peer.Service.Tests
```

**Test Coverage**:
- **Unit Tests**: Core service tests implemented
- **Integration Tests**: gRPC and replication scenarios
- **Performance Tests**: Pending benchmarking

**Key MVD Completions**: 7 gRPC RPCs (RegisterSync 4 + TransactionDistribution 3), register replication, live subscriptions, EF Core database

---

## Observability

### Metrics (OpenTelemetry)

**PeerServiceMetrics** exposes 7 metrics:

| Metric | Type | Description |
|--------|------|-------------|
| `peer.connection.status` | Gauge | Current connection status (0=Disconnected, 1=Connected, 2=Isolated) |
| `peer.heartbeat.latency` | Histogram | Heartbeat round-trip time (milliseconds) |
| `peer.sync.duration` | Histogram | Sync operation duration (seconds) |
| `peer.sync.blueprints.count` | Counter | Total blueprints synchronized |
| `peer.push.notifications.delivered` | Counter | Successful push notification deliveries |
| `peer.push.notifications.failed` | Counter | Failed push notification deliveries |
| `peer.failover.count` | Counter | Number of failover events |

**Prometheus Endpoint**: `/metrics` (via ServiceDefaults OpenTelemetry configuration)

### Distributed Tracing (OpenTelemetry)

**PeerServiceActivitySource** creates 6 trace activities:

| Activity | Kind | Tags |
|----------|------|------|
| `peer.connection.connect` | Client | central_node_id, priority |
| `peer.connection.failover` | Client | from_node, to_node, reason |
| `peer.sync.full` | Client | peer_id, blueprint_count |
| `peer.sync.incremental` | Client | peer_id, last_known_version, new_blueprints |
| `peer.heartbeat.send` | Client | peer_id, sequence_number |
| `peer.notification.receive` | Server | blueprint_id, version, type |

**Zipkin Endpoint**: Configured via `OpenTelemetry:ZipkinEndpoint` in appsettings.json

### Structured Logging (Serilog)

**Correlation IDs**: All logs include `SessionId` for request tracing

**Semantic Properties**:
- Connection events: NodeId, Priority, Duration, ConsecutiveFailures
- Heartbeat events: SequenceNumber, LatencyMs, MissedCount
- Sync events: SyncType, Duration, BlueprintCount, VersionFrom, VersionTo

**Example Logs**:
```
[INF] Attempting to connect to hub node n0.sorcha.dev with priority 0
[INF] Successfully connected to hub node n0.sorcha.dev (session: abc123, version: 42)
[WRN] Heartbeat timeout for hub node n0.sorcha.dev (missed: 2/2)
[INF] Failover initiated from n0.sorcha.dev to n1.sorcha.dev
[INF] Full sync completed: 150 blueprints in 12.5 seconds
[INF] Incremental sync completed: 3 new blueprints (version 42 → 45)
```

---

## Deployment

### .NET Aspire (Development)

The Peer Service is registered in the Aspire AppHost:

```csharp
var peerService = builder.AddProject<Projects.Sorcha_Peer_Service>("peer-service");
```

Start the entire platform:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

Access Aspire Dashboard: `http://localhost:15888`

### Docker Compose (Recommended)

The Peer Service is part of the full Sorcha Docker Compose stack with two nodes:

**Local Hub Node (`peer-hub-local`)**:
```bash
# Start all services (includes hub and peer)
docker-compose up -d peer-hub-local

# Hub gRPC accessible at: localhost:50051
```

**Peer Node (`peer-service`)**:
```bash
# Start peer node (connects to local hub via Docker DNS)
docker-compose up -d peer-service

# Peer gRPC accessible at: localhost:50052
```

**Network Architecture:**
- Bridge network (`sorcha-network`) for all services
- Hub node: `peer-hub-local` (Docker DNS hostname)
- Peer connects via Docker DNS: `http://peer-hub-local:5000`
- Published ports: 50051 (hub), 50052 (peer)
- TLS disabled for local development (`EnableTls: false`)
- Configuration: `docker/appsettings.Bridge.json`
- See [docs/DOCKER-BRIDGE-NETWORKING.md](../../../docs/DOCKER-BRIDGE-NETWORKING.md) for complete details

**Verify Connection:**
```bash
# Check peer connected to hub
docker logs sorcha-peer-service | grep "Successfully connected"

# Expected output:
# Successfully connected to hub node hub-local.sorcha.dev at http://peer-hub-local:5000
```

### Docker (Standalone)

#### Peer Node

```bash
# Build Docker image
docker build -t sorcha-peer-service:latest -f src/Services/Sorcha.Peer.Service/Dockerfile .

# Run container
docker run -d \
  -p 5000:5000 \
  -p 5001:5001 \
  -e PeerService__HubNode__HubNodes__0__NodeId=hub-local \
  -e PeerService__HubNode__HubNodes__0__Hostname=peer-hub-local \
  -e PeerService__HubNode__HubNodes__0__Port=5000 \
  -e PeerService__HubNode__HubNodes__0__Priority=0 \
  -e PeerService__HubNode__HubNodes__0__EnableTls=false \
  --name peer-service \
  sorcha-peer-service:latest
```

#### Hub Node

```bash
# Start MongoDB first
docker run -d \
  -p 27017:27017 \
  --name sorcha-mongo \
  mongo:8.0

# Run hub node
docker run -d \
  -p 5000:5000 \
  -p 5001:5001 \
  -e PeerService__HubNode__IsHubNode=true \
  -e PeerService__HubNode__ValidateHostname=false \
  -e MongoDB__ConnectionString=mongodb://sorcha-mongo:27017 \
  --link sorcha-mongo \
  --name peer-hub-local \
  sorcha-peer-service:latest
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sorcha-central-node
spec:
  replicas: 3
  selector:
    matchLabels:
      app: sorcha-central-node
  template:
    metadata:
      labels:
        app: sorcha-central-node
    spec:
      containers:
      - name: peer-service
        image: sorcha-peer-service:latest
        ports:
        - containerPort: 5000
          name: grpc
        - containerPort: 5001
          name: http
        env:
        - name: CentralNode__IsCentralNode
          value: "true"
        - name: CentralNode__ValidateHostname
          value: "true"
        - name: CentralNode__ExpectedHostnamePattern
          value: "n[0-2].sorcha.dev"
        - name: MongoDB__ConnectionString
          value: "mongodb://sorcha-mongo:27017"
        - name: MongoDB__DatabaseName
          value: "sorcha_system_register"
---
apiVersion: v1
kind: Service
metadata:
  name: sorcha-central-node
spec:
  type: LoadBalancer
  ports:
  - port: 5000
    name: grpc
  - port: 5001
    name: http
  selector:
    app: sorcha-central-node
```

---

## Troubleshooting

### Common Issues

**Issue**: Peer cannot connect to hub nodes
**Solution**: Verify hub node hostnames and network connectivity.

```bash
# Test gRPC connectivity
grpcurl -plaintext n0.sorcha.dev:5000 list

# Check DNS resolution
nslookup n0.sorcha.dev
```

**Issue**: Heartbeat timeouts causing frequent failovers
**Solution**: Increase heartbeat timeout or check network latency.

```json
{
  "SystemRegister": {
    "HeartbeatTimeoutSeconds": 60
  }
}
```

**Issue**: Incremental sync not fetching new blueprints
**Solution**: Check SyncCheckpoint version matches hub node version.

```bash
# Get hub node status
grpcurl -plaintext -d '{"peer_id": "test"}' n0.sorcha.dev:5000 \
  sorcha.peer.v1.CentralNodeConnection/GetCentralNodeStatus
```

**Issue**: Node incorrectly detected as hub node
**Solution**: Verify hostname or disable hostname validation.

```json
{
  "CentralNode": {
    "IsCentralNode": false,
    "ValidateHostname": false
  }
}
```

**Issue**: MongoDB connection failed on hub node startup
**Solution**: Verify MongoDB is running and connection string is correct.

```bash
# Test MongoDB connectivity
docker ps | grep mongo
mongosh mongodb://localhost:27017
```

### Debug Mode

Enable detailed logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Sorcha.Peer.Service": "Trace",
      "Grpc": "Debug"
    }
  }
}
```

---

## Performance Benchmarks

**Success Criteria** (from spec.md):

| Metric | Target | Status |
|--------|--------|--------|
| SC-009: System register initialization | 100% success | ✅ Implemented |
| SC-010: Full sync duration | <60s for 100 blueprints | 🚧 Pending tests |
| SC-012: System register integrity check | <2s | ✅ Implemented |
| SC-013: Central node detection | 100% accuracy | ✅ Implemented |
| SC-014: Connection establishment | <30s per node | ✅ Implemented |
| SC-015: Central node uptime | 100% (3 nodes for redundancy) | ✅ Implemented |
| SC-016: Push notification delivery | 80% delivered in 30s | ✅ Implemented |
| FR-036: Heartbeat timeout | 30s (2 missed = 60s) | ✅ Implemented |

---

## Security Considerations

### Authentication (Production)

- **Current**: Development mode (no authentication required for gRPC)
- **Production**: Mutual TLS (mTLS) with client certificates
- **JWT Tokens**: Service-to-service authentication via Tenant Service

### Authorization

- **Hub Nodes**: Only hub nodes can accept peer connections
- **Peer Verification**: Validate peer signatures before accepting sync requests
- **Session Management**: Use session IDs to track connection state

### Data Protection

- **TLS 1.3**: All gRPC and REST communications encrypted
- **Blueprint Signatures**: Validate cryptographic signatures on blueprints
- **No Sensitive Logging**: Never log blueprint content or private keys

### Secrets Management

- **MongoDB Credentials**: Store in environment variables or Azure Key Vault
- **TLS Certificates**: Rotate certificates every 90 days
- **Session Tokens**: Generate cryptographically secure session IDs

---

## Resources

- **Architecture**: [docs/architecture.md](../../docs/reference/architecture.md)
- **Development Status**: [docs/development-status.md](../../docs/reference/development-status.md)
- **gRPC Documentation**: https://grpc.io/docs/languages/csharp/
- **MongoDB .NET Driver**: https://www.mongodb.com/docs/drivers/csharp/
- **Polly Resilience**: https://www.pollydocs.org/
- **OpenTelemetry**: https://opentelemetry.io/docs/instrumentation/net/

---

## Technology Stack

**Runtime:**
- .NET 10.0 (10.0.100)
- C# 13
- ASP.NET Core 10

**Frameworks:**
- gRPC for .NET (Grpc.AspNetCore 2.71.0)
- MongoDB.Driver 3.5.2
- Polly 8.5.0 (resilience pipeline)
- .NET Aspire 13.0+ for orchestration

**Networking:**
- HTTP/2 (gRPC primary protocol)
- HTTP/1.1 (REST endpoints)

**Observability:**
- OpenTelemetry 1.10.0 for distributed tracing and metrics
- Serilog for structured logging
- Scalar.AspNetCore 2.11.2 for API docs

**Testing:**
- xUnit for test framework
- FluentAssertions for assertions
- Moq for mocking
- Testcontainers for MongoDB integration tests

---

## Contributing

### Development Workflow

1. **Create a feature branch**: `git checkout -b feature/peer-service-enhancement`
2. **Make changes**: Follow C# coding conventions
3. **Write tests**: Maintain >85% coverage (constitution requirement)
4. **Run tests**: `dotnet test`
5. **Format code**: `dotnet format`
6. **Commit**: `git commit -m "feat: add incremental sync optimization"`
7. **Push**: `git push origin feature/peer-service-enhancement`
8. **Create PR**: Reference issue number

### Code Standards

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use async/await for I/O operations
- Add XML documentation for public APIs
- Include unit tests for all business logic (>85% coverage)
- Use dependency injection for testability
- Follow Sorcha project constitution principles

---

## Status and Roadmap

### Completed (70% - Phase 1-3)

✅ **Phase 1: Setup (6 tasks)**
- gRPC proto compilation
- Test directory structure
- Fixed proto naming conflicts

✅ **Phase 2: Foundational (23 tasks)**
- Core entities and configuration (17 classes, 3 enums)
- Validation utilities (5 validators)
- Polly resilience pipeline
- MongoDB system register repository
- Extended PeerListManager

✅ **Phase 3: Core Implementation (34 tasks)**
- Central node detection with hostname validation
- Priority-based connection manager with failover
- System register replication (full + incremental sync)
- Heartbeat monitoring with timeout handling
- Push notifications for blueprint publication
- Isolated mode for graceful degradation
- Comprehensive observability (7 metrics, 6 traces, structured logs)

### Deferred (Post-MVD)

- Performance optimization (MongoDB query benchmarking)
- Security hardening (mTLS, certificate rotation)
- End-to-end validation with 3 hub nodes + 2 peer nodes
- Additional integration and performance tests (SC-010, SC-016 benchmarking)
- Quickstart documentation

---

## License

Apache License 2.0 - See [LICENSE](../../LICENSE) for details.

---

**Version**: 1.1.0
**Last Updated**: 2026-03-01
**Maintained By**: Sorcha Contributors
**Status**: ✅ Complete (100% MVD)
