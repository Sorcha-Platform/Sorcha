# Feature 061: Edge Device Integration

**Status:** Future | **Priority:** P2-P3 | **Effort:** ~60h

## Problem

Real-world blueprint workflows often happen at physical locations: warehouse docks, manufacturing floors, healthcare wards, field inspections. Users need to trigger and complete actions via QR codes, NFC taps, RFID badges, or Bluetooth proximity, often on shared devices or with intermittent connectivity.

## User Scenarios

### Supply Chain Checkpoint
- Truck arrives at warehouse, driver scans QR code on dock door
- QR contains action reference (register + instance + action + challenge nonce)
- Driver's phone app presents the pending action, driver signs "delivery confirmed"

### Healthcare Ward
- Patient taps NFC wristband on nurse's tablet
- Tablet resolves pending actions for that patient-nurse pair
- Nurse reviews vitals, signs "assessment complete"

### Manufacturing Floor
- Worker badges into QC station via RFID
- Station presents pending quality inspection for current production order
- Worker reviews, signs via biometric or PIN on station screen

### Field Inspection (Offline)
- Inspector photographs defect, records measurements on tablet
- Signs action locally with cached derived key (no connectivity)
- Transaction queued, syncs automatically when back online

## Three Edge Models

### Model A: Smart Edge (device has wallet)

The device (phone/tablet) has a full wallet with signing keys. QR/NFC/RFID provides context only (which action to perform).

```
User's Device → Scans QR/NFC trigger
             → Fetches pending action from Blueprint Service API
             → User reviews action details
             → Signs locally with wallet key
             → Submits signed transaction
```

- Best UX, most secure (keys never leave device)
- Requires wallet app installed
- **Effort:** 8h (QR protocol + deep links)

### Model B: Thin Edge (shared device, user authenticates)

Shared terminals (kiosks, stations) where user identifies via badge/NFC and authenticates via PIN/biometric. Server signs on their behalf using delegation.

```
Shared Terminal → Scans user badge/NFC → resolves wallet address
               → Fetches pending actions from API
               → User authenticates (PIN, biometric, passkey)
               → Server creates delegation token
               → Server signs transaction on behalf of user
               → Submits signed transaction
```

- Works on any shared device
- Requires delegation infrastructure (time-limited, scope-limited tokens)
- **Effort:** 16h (delegation signing + thin client)

### Model C: Offline Edge (no connectivity)

Device operates without network, caches blueprint definitions and derived keys, queues signed transactions for later sync.

```
Offline Device → Pre-caches blueprint definition + derived key
              → Collects data (photos, measurements, scans)
              → User signs locally with cached key
              → Transaction queued in local storage
              → On reconnect: sync queue → Blueprint Service
              → Engine processes queued actions in order
```

- Essential for field work, rural areas, underground/maritime
- Requires: pre-cached blueprints, derived key provisioning, conflict resolution
- **Effort:** 24h (offline queue, sync protocol, conflict handling)

## Protocols

### QR Code (sorcha:// deep link)

```
sorcha://action?r={registerId}&i={instanceId}&a={actionId}&n={nonce}
sorcha://pending?r={registerId}&loc={locationId}
sorcha://wallet?addr={walletAddress}
```

- Standard URI scheme for app deep linking
- Nonce prevents replay of scanned QR codes
- Location-based variant resolves "what's pending for me here"

### NFC / NDEF

```
NDEF Record:
  TNF:     External Type
  Type:    sorcha.io:action
  Payload: {registerId}:{instanceId}:{actionId}:{challengeNonce}
```

- Tap-to-act: receiving device parses, fetches action, presents for signing
- Write-once NFC tags at physical locations (dock doors, stations, patient beds)

### Bluetooth LE (BLE Beacon)

```
BLE Advertisement:
  Service UUID: sorcha-proximity
  Payload: {registerId}:{locationId}
```

- Passive: device detects proximity, resolves pending actions for location
- No tap required, auto-surfaces relevant actions when user is nearby
- Privacy consideration: only resolves actions for authenticated user

## Key Components to Build

| Component | Model | Effort | Priority |
|-----------|-------|--------|----------|
| `sorcha://` URI scheme + QR generation endpoint | A | 4h | P2 |
| QR scanner in Sorcha UI mobile view | A | 4h | P2 |
| Delegation-scoped signing (time-limited, action-limited) | B | 16h | P2 |
| Thin edge client SDK (web component) | B | 8h | P3 |
| Offline blueprint cache + sync queue | C | 16h | P3 |
| Conflict resolution for offline actions | C | 8h | P3 |
| NFC/NDEF payload standard + reader integration | A/B | 4h | P3 |

## Dependencies

- Wallet Service: delegation-scoped signing tokens
- Blueprint Service: action resolution by location, QR generation endpoint
- API Gateway: `sorcha://` redirect handling for web fallback
- Mobile: PWA or native app with QR/NFC reader capabilities

## Open Questions

- Should QR codes be single-use (nonce-bound) or reusable (location-bound)?
- How to handle conflict when offline actions arrive out-of-order?
- BLE beacons: who manages beacon infrastructure? Platform or customer?
- Thin edge: how long should delegation tokens live? Per-action or per-session?
- Offline: maximum offline duration before keys must be re-provisioned?
