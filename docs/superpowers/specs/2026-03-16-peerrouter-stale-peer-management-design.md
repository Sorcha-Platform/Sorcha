# PeerRouter Stale Peer Management & Relay Enablement

**Date:** 2026-03-16
**Status:** Approved

## Problem

Container restarts generate new PeerIds (Docker container IDs via `Environment.MachineName`), creating ghost entries in the PeerRouter's routing table. The router marks stale peers unhealthy after 60s but never removes them, causing unbounded memory growth and a polluted `/peers` debug endpoint.

Additionally, NAT'd peers cannot reach each other directly, requiring the router's existing relay mode to be enabled on the Azure deployment.

## Design

### 1. Two-Tier Eviction in RoutingTable (Router)

- **Existing tier:** Mark peers unhealthy after 60s of no heartbeat (unchanged)
- **New tier:** Remove entries entirely after configurable eviction timeout (default 3600s / 60 min)
- New config: `PEERROUTER__EVICTION_TIMEOUT` / `--eviction-timeout`
- New event type: `PeerEvicted`
- `PeerTimeoutService` sweep handles both tiers in the same loop
- `RoutingTable.EvictStalePeers(TimeSpan)` removes entries from the `ConcurrentDictionary` and emits `PeerEvicted` events

### 2. Address-Based Dedup in RegisterPeer (Router)

- On new peer registration, scan for existing **unhealthy** entries with the same IP:port
- If found: silently remove the old entry, emit a `PeerReplaced` event with old/new PeerIds
- Only replaces unhealthy entries (healthy peers at same address could be legitimate NAT scenarios)
- New event type: `PeerReplaced`

### 3. Startup Warning for Missing NodeId (Peer Service)

- In `Program.cs`, if `PeerNetwork:NodeId` is null/empty, log a warning:
  `"NodeId not configured, using MachineName '{name}'. Set PeerNetwork:NodeId for stable peer identity."`
- No behaviour change — fallback to `Environment.MachineName` remains
- Nudges toward correct configuration without breaking dev workflows

### 4. Enable Relay on Azure Deployment

- Set `PEERROUTER__ENABLE_RELAY=true` on the `peer-router` Container App
- Relay implementation already exists in `RouterCommunicationService` — fully functional, just gated behind the config flag
- No code changes required

## Files to Modify

| File | Change |
|------|--------|
| `PeerRouter/Models/RouterConfiguration.cs` | Add `EvictionTimeoutSeconds` property (default 3600), CLI arg `--eviction-timeout`, env var `PEERROUTER__EVICTION_TIMEOUT` |
| `PeerRouter/Models/RouterEventType.cs` | Add `PeerReplaced` and `PeerEvicted` enum values |
| `PeerRouter/Services/RoutingTable.cs` | Add `EvictStalePeers(TimeSpan)` method; add address-based dedup logic in `RegisterPeer()` |
| `PeerRouter/Services/PeerTimeoutService.cs` | Inject eviction timeout; call `EvictStalePeers()` after `SweepUnhealthyPeers()` in the sweep loop |
| `Peer.Service/Program.cs` | Add startup warning when `NodeId` is not configured |

## What Doesn't Change

- Proto definitions (no wire format changes)
- Heartbeat processing logic
- Health endpoint response shape
- Existing 60s unhealthy marking behaviour
- Relay implementation code (already complete)

## Deployment Steps

1. Build and push updated PeerRouter image to ACR
2. Update Azure Container App with new image + `PEERROUTER__ENABLE_RELAY=true`
3. Verify via `/health` endpoint (`relayEnabled: true`) and `/peers` (stale entries evicted after 60 min)
