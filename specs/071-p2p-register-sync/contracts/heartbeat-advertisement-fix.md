# Contract: Heartbeat Advertisement Fix

**Feature**: 071-p2p-register-sync | **Date**: 2026-03-28

## Problem

PeerRouter receives `advertised_registers` in heartbeat requests but:
1. Ignores them (doesn't update RoutingTable)
2. Never includes other peers' advertisements in heartbeat responses

Register discovery via heartbeats is broken at the Router layer.

## Changes Required

### 1. RouterHeartbeatService.ProcessHeartbeat — Extract advertisements

**Current**: Only processes `request.RegisterVersions`
**New**: Also process `request.AdvertisedRegisters` and call `RoutingTable.UpdateAdvertisedRegisters()`

### 2. RoutingTable — New method: UpdateAdvertisedRegisters

```
UpdateAdvertisedRegisters(peerId, advertisements) → bool
```

Updates the `RoutingEntry.AdvertisedRegisters` list for the specified peer. Replaces the full list (not merge — peer sends its complete set each heartbeat).

### 3. RouterHeartbeatService — Include other peers' ads in response

**Current**: Response only contains `Success`, `PeerId`, `Timestamp`, `Message`
**New**: Response also includes `AdvertisedRegisters` from other healthy peers

**Aggregation logic**:
- Collect `AdvertisedRegisters` from all healthy peers except the requesting peer
- Deduplicate by register ID (if multiple peers advertise the same register, include all — peer-side handles source selection)
- Cap at 100 advertisements per response to limit message size

### 4. Peer heartbeat response processing (already exists)

The Peer Service's `PeerHeartbeatService` already has code to process `response.AdvertisedRegisters` — it's just never populated. No changes needed on the peer side.

## Message Flow

```
Peer A (heartbeat)          PeerRouter                  Peer B (heartbeat)
  │                              │                           │
  │── HB Request ───────────────▶│                           │
  │   advertised_registers:      │                           │
  │   [{reg-1, v5, public}]     │                           │
  │                              │  Store: PeerA → [reg-1]  │
  │◀── HB Response ──────────────│                           │
  │   advertised_registers: []   │                           │
  │   (no other peers yet)       │                           │
  │                              │                           │
  │                              │◀── HB Request ────────────│
  │                              │   advertised_registers:   │
  │                              │   [{reg-2, v3, public}]  │
  │                              │  Store: PeerB → [reg-2]  │
  │                              │── HB Response ───────────▶│
  │                              │   advertised_registers:   │
  │                              │   [{reg-1, v5, PeerA}]   │
  │                              │                           │
  │── HB Request ───────────────▶│                           │
  │                              │── HB Response ───────────▶│
  │◀── HB Response ──────────────│                           │
  │   advertised_registers:      │                           │
  │   [{reg-2, v3, PeerB}]     │                           │
```

After two heartbeat cycles, both peers know about each other's registers.
