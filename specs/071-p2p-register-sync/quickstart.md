# Quickstart: P2P Register Sync

**Feature**: 071-p2p-register-sync | **Date**: 2026-03-28

## Prerequisites

- Docker Desktop running
- Two machines on the same local network (or two Docker Compose stacks)
- PeerRouter deployed at n0.sorcha.dev with `PEERROUTER__ENABLE_RELAY=true`
- Both peers configured with n0.sorcha.dev as seed node

## Setup: Peer A (Source)

```bash
# Start full Sorcha stack on Machine A
docker-compose up -d

# Create a register
curl -X POST http://localhost/api/registers \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"name": "Test Register", "description": "P2P sync test"}'

# Publish a blueprint and execute some actions to generate transactions
# (use existing walkthrough scripts or CLI)

# Verify peer service is connected to Router
curl http://localhost:50051/api/peers/connected
# Should show n0.sorcha.dev as connected seed node

# Verify register is advertised
curl http://localhost:50051/api/registers/subscriptions
# Should show local register with sync state
```

## Setup: Peer B (Subscriber)

```bash
# Start full Sorcha stack on Machine B
# Ensure PeerService__NodeId is different from Machine A
docker-compose up -d

# Verify peer service is connected to Router
curl http://localhost:50051/api/peers/connected
# Should show n0.sorcha.dev as connected seed node

# Check available registers (discovered via heartbeat advertisements)
curl http://localhost:50051/api/registers/available
# Should list Peer A's register with name, version, public flag

# Subscribe to Peer A's register
curl -X POST http://localhost:50051/api/registers/{registerId}/subscribe \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"mode": "FullReplica"}'

# Monitor sync progress
curl http://localhost:50051/api/registers/subscriptions
# State should progress: Subscribing → Syncing → FullyReplicated

# Verify finalized data on local Register Service
curl http://localhost/api/registers/{registerId}/transactions
# Should show all transactions that were sealed by dockets on Peer A
```

## Verification

1. **Relay working**: Both peers show as connected in Router's `/peers` endpoint
2. **Advertisements flowing**: Peer B sees Peer A's register in `/api/registers/available`
3. **Sync complete**: Subscription state is `FullyReplicated`
4. **Data finalized**: Transactions queryable on Peer B's Register Service
5. **Live updates**: New transaction on Peer A appears on Peer B within 15 seconds

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Peers not discovering each other | Verify both peers heartbeat to Router; check Router `/peers` endpoint |
| No register advertisements | Check Router logs for heartbeat processing; verify fix for advertisement relay |
| Sync stuck at Subscribing | Check Peer Service logs for relay connection errors; verify Router has `EnableRelay=true` |
| Finalization failing | Check Peer Service logs for signature verification errors; verify Validator Service is running on Peer A |
| Data not queryable on Peer B | Check Register Service logs; verify MongoDB is accessible from Peer Service |
