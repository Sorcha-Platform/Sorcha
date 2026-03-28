# Distributed Register Walkthrough

**Purpose:** Tests cross-machine register creation, peer discovery, subscription, and transaction replication between two Sorcha nodes.
**Date Created:** 2026-02-09
**Status:** ✅ Complete
**Prerequisites:** Two networked machines, Docker, PowerShell 7+

---

## Overview

Demonstrates the full distributed ledger flow: create a register on one machine, advertise it to the peer network, have a second machine discover and subscribe, then execute a ping-pong blueprint and verify transactions replicate across nodes. Includes cross-machine service authentication via temporary service principals.

## Files in This Walkthrough

- **setup.ps1** — Bootstrap org and wallet on LOCAL machine
- **run.ps1** — Create register locally, verify basic replication to remote
- **sync-test.ps1** — **Full P2P sync integration test** (Feature 071): streaming relay, heartbeat advertisements, subscription sync, docket finalization, live streaming, resilience
- **README.md** — This file (setup guide + reference)

### sync-test.ps1 — P2P Register Sync Test

Tests the full end-to-end flow introduced in Feature 071:

| Phase | Tests | What's Validated |
|-------|-------|------------------|
| 1. Connectivity | PeerRouter reachable, both peers registered, reverse streams active | US1: Streaming Relay |
| 2. Discovery | Register created, advertised, discovered on remote via heartbeat | US2: Heartbeat Advertisements |
| 3. Subscription | Remote subscribes, sync completes (FullyReplicated) | US3: Full Register History |
| 4. Finalization | Transactions queryable on remote, register height matches | US4+US6: Docket Finalization + Signature Verification |
| 5. Live Streaming | New transaction on local appears on remote within 30s | US5: Live Transaction Streaming |
| 6. Resilience | Data survives restart, peers reconnect after Router restart | Resilience |

```bash
# Run all phases
pwsh walkthroughs/DistributedRegister/sync-test.ps1 -RemoteHost 192.168.51.9

# Start from a specific phase (e.g., skip connectivity checks)
pwsh walkthroughs/DistributedRegister/sync-test.ps1 -RemoteHost 192.168.51.9 -Phase 3
```

---

## Environment Setup (from scratch)

This section documents how to recreate the two-machine setup. Assumes:
- **Local machine** (Windows): e.g. `192.168.51.116`
- **Remote machine** (Linux): e.g. `192.168.51.9` (hostname: `tiny`)

Both machines need the Sorcha repo cloned at the same relative path.

### 1. SSH Key Authentication

Set up passwordless SSH from local to remote so scripts and file copies work without prompts.

```powershell
# Check if you have a key pair
ls ~/.ssh/id_rsa.pub   # or id_ed25519.pub

# If not, generate one
ssh-keygen -t ed25519 -C "your@email.com"

# Copy public key to remote
ssh-copy-id stuart@192.168.51.9
```

**Windows SSH agent issue:** Git Bash ships its own `ssh` binary which cannot talk to the Windows OpenSSH agent. If you're prompted for passwords despite having keys:

```powershell
# Verify the Windows SSH agent is running and has your key
Get-Service ssh-agent                          # Should be Running
C:\Windows\System32\OpenSSH\ssh-add.exe -l     # Should list your key

# Configure Git to use Windows OpenSSH (one-time)
git config --global core.sshCommand "C:/Windows/System32/OpenSSH/ssh.exe"

# For non-Git SSH commands, use the Windows binary explicitly:
/c/Windows/System32/OpenSSH/ssh.exe stuart@192.168.51.9 "echo works"
```

### 2. SSL Certificates (cross-machine HTTPS)

The default dev certs only have SANs for `localhost`. To access `https://192.168.51.9` or `https://192.168.51.116` without cert errors, regenerate with LAN SANs.

```powershell
# Regenerate certs with LAN IPs and hostnames
pwsh scripts/generate-dev-cert.ps1 -Force

# This creates docker/certs/aspnetapp.pfx and sorcha-ui-web.pfx with SANs:
# localhost, 127.0.0.1, ::1, sorcha-ui-web, api-gateway,
# 192.168.51.9, 192.168.51.116, tiny, tiny.local
```

To add different IPs/hostnames, edit the `$dnsNames` array in `scripts/generate-dev-cert.ps1`.

**Copy certs to remote:**
```powershell
scp docker/certs/aspnetapp.pfx stuart@192.168.51.9:~/projects/Sorcha/docker/certs/
scp docker/certs/sorcha-ui-web.pfx stuart@192.168.51.9:~/projects/Sorcha/docker/certs/
```

### 3. Peer Seeding

Each machine's peer service needs a seed node pointing to the other machine. Configuration is via `.env` variables consumed by `docker-compose.yml`.

**Local `.env`** (this machine seeds tiny):
```ini
PEER_NODE_ID=local-peer.sorcha.dev
SEED_PEER_NODE_ID=tiny-peer.sorcha.dev
SEED_PEER_HOST=192.168.51.9
SEED_PEER_PORT=50051
```

**Remote `.env`** (tiny seeds local):
```ini
PEER_NODE_ID=tiny-peer.sorcha.dev
SEED_PEER_NODE_ID=local-peer.sorcha.dev
SEED_PEER_HOST=192.168.51.116
SEED_PEER_PORT=50051
```

Port 50051 is the default host-mapped gRPC port (`PEER_GRPC_PORT` in docker-compose). The peer service listens on container port 5000 (cleartext HTTP/2, no TLS).

### 4. Sync docker-compose.yml and YARP config

The `docker-compose.yml` and API Gateway `appsettings.json` must match on both machines (they contain peer seeding env vars and YARP routes for P2P endpoints).

```powershell
scp docker-compose.yml stuart@192.168.51.9:~/projects/Sorcha/
scp src/Services/Sorcha.ApiGateway/appsettings.json stuart@192.168.51.9:~/projects/Sorcha/src/Services/Sorcha.ApiGateway/
```

### 5. Build and Start Services

**Local:**
```powershell
docker-compose up -d
```

**Remote:**
```bash
ssh stuart@192.168.51.9 "cd ~/projects/Sorcha && docker compose build api-gateway && docker compose up -d"
```

### 6. Verify Connectivity

```powershell
# HTTPS both directions
curl -sk https://192.168.51.9/health     # Should return "Healthy"
curl -sk https://192.168.51.116/health   # Should return "Healthy"

# Peer discovery
curl -s http://localhost/api/peers           # Should list tiny-peer
curl -s http://192.168.51.9/api/peers        # Should list local-peer
curl -s http://localhost/api/peers/connected  # {"connectedPeerCount":1}
```

---

## Architecture

```
  LOCAL (192.168.51.116)                  REMOTE (192.168.51.9)
  ┌─────────────────────┐                ┌─────────────────────┐
  │  Tenant Service     │◄── JWT req ────│  Peer Service       │
  │  (JWT Authority)    │                │  (Subscriber)       │
  ├─────────────────────┤                ├─────────────────────┤
  │  Register Service   │                │  Peer Service       │
  │  (Source of truth)  │── gRPC sync ──▶│  (Replica)          │
  ├─────────────────────┤                ├─────────────────────┤
  │  Peer Service       │◄── heartbeat ─▶│  Peer Service       │
  │  (Advertiser)       │                │  (Discoverer)       │
  └─────────────────────┘                └─────────────────────┘
```

### Network Ports

| Port | Protocol | Purpose |
|------|----------|---------|
| 80 | HTTP | API Gateway (main entry point) |
| 443 | HTTPS | API Gateway (TLS) |
| 50051 | HTTP/2 | Peer gRPC (cleartext, no TLS) |

## Authentication Flow

1. **Peer discovery** is unauthenticated (gRPC heartbeats, register advertisements)
2. Once peers are connected, the remote peer **registers as a service principal** on the local tenant service
3. The remote peer requests a **service JWT** from the local tenant using `client_credentials` grant
4. This JWT authorises the remote peer to query register data, subscribe, and participate in validation
5. On disconnect, the service principal is **revoked** (deleted)

Service principal naming convention: `{peerNodeId}_{connectTimestamp}` (e.g. `tiny-peer_20260209-212551`)

---

## Usage

```powershell
# Default (local=localhost, remote=192.168.51.9)
./walkthroughs/DistributedRegister/test-distributed-register.ps1

# Custom hosts
./walkthroughs/DistributedRegister/test-distributed-register.ps1 -RemoteHost 192.168.51.9 -RoundTrips 5

# Verbose output
./walkthroughs/DistributedRegister/test-distributed-register.ps1 -ShowJson

# Keep service principal after test
./walkthroughs/DistributedRegister/test-distributed-register.ps1 -SkipCleanup
```

## Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-LocalHost` | `localhost` | Local machine gateway address |
| `-RemoteHost` | `192.168.51.9` | Remote machine gateway address |
| `-AdminEmail` | `admin@sorcha.local` | Admin account email (both machines) |
| `-AdminPassword` | `Dev_Pass_2025!` | Admin account password |
| `-RoundTrips` | `3` | Number of ping-pong round-trips |
| `-ShowJson` | `false` | Show full JSON responses |
| `-SkipCleanup` | `false` | Don't revoke temporary service principal |

## Walkthrough Steps

| # | Phase | Auth | Description |
|---|-------|------|-------------|
| 1 | Peer Network | None | Verify both peers see each other |
| 2 | Local Auth | Local JWT | Admin login on local tenant service |
| 3 | Wallets | Local JWT | Create ED25519 wallets for ping/pong |
| 4 | Register | Local JWT | Two-phase register creation (initiate/sign/finalize) |
| 5 | Advertise | Local JWT | Set register as public on peer network |
| 6 | Service Principal | Local JWT | Register remote peer as temporary service principal |
| 7 | Cross-Machine JWT | Client Credentials | Remote peer obtains service JWT from local tenant |
| 8 | Discovery | None | Remote peer discovers advertised register |
| 9 | Subscribe | Remote JWT | Remote peer subscribes in full-replica mode |
| 10-11 | Blueprint | Local JWT | Create and publish ping-pong blueprint + instance |
| 12 | Ping-Pong | Local JWT | Execute N round-trips of transactions |
| 13 | Verification | Both | Check replication status on remote |
| 14 | Cleanup | Local JWT | Revoke temporary service principal |

## Troubleshooting

### Peers show 0 connected
- Check `SEED_PEER_*` variables in `.env` on both machines
- Verify port 50051 is reachable: `curl http://192.168.51.9:50051` (expect connection reset, not timeout)
- Check peer logs: `docker logs sorcha-peer-service --tail 30`

### 401 on subscribe endpoint
- The peer service needs `*jwt-env` in docker-compose (added in this branch)
- Verify with: `docker exec sorcha-peer-service env | grep Jwt`

### Register not visible on remote
- Advertisement propagates via heartbeat cycle (10-15s); the script retries up to 4 times
- Check local peer advertised registers: `curl http://localhost/api/peers` (look at `advertisedRegisters`)

### HTTPS cert errors
- Regenerate certs: `pwsh scripts/generate-dev-cert.ps1 -Force`
- Copy to remote and restart API gateways on both machines

### SSH password prompts
- See "SSH Key Authentication" section above
- Key issue is Git Bash `ssh` vs Windows `ssh.exe` — configure `core.sshCommand`

---

## YARP Routes Added

These routes were added to `src/Services/Sorcha.ApiGateway/appsettings.json` to support P2P operations:

| Route | Cluster | Purpose |
|-------|---------|---------|
| `/api/registers/available` | peer-cluster | List registers advertised on the network |
| `/api/registers/subscriptions` | peer-cluster | List active register subscriptions |
| `/api/registers/{id}/subscribe` | peer-cluster | Subscribe to a register |
| `/api/registers/{id}/advertise` | peer-cluster | Advertise/de-advertise a register |
| `/api/registers/{id}/cache` | peer-cluster | Purge cached replication data |
| `/api/service-principals` | tenant-cluster | Register/manage service principals |
| `/api/service-principals/{**}` | tenant-cluster | Service principal CRUD |

These use `Order: 1` to take priority over the register-service catch-all route (`Order: 10`).
