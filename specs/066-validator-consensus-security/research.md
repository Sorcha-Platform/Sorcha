# Research: Validator Consensus Security

**Feature**: 066-validator-consensus-security
**Date**: 2026-03-22

## R1: Existing Validator Registry Infrastructure

**Decision**: Extend existing ValidatorRegistry rather than replace it.

**Rationale**: The Validator Service already has substantial infrastructure:
- `ValidatorStatus` enum: Pending, Active, Suspended, Removed
- `ValidatorInfo` record with PublicKey, Status, GrpcEndpoint, Metadata
- `ValidatorRegistration` record with public key
- Registration endpoints with approve/reject (consent mode)
- `ValidatorListChanged` event with change types (Added, Removed, Suspended, Reactivated, Approved, Rejected)
- L1 local cache + L2 Redis architecture in `ValidatorRegistry`

**Gaps to fill**:
1. No `Revoked` terminal state (only `Removed`) — rename or add
2. No suspend/unsuspend endpoints — only approve/reject
3. Storage is Redis-only — need MongoDB persistence layer
4. No admin UI pages exist (Sorcha.Admin.Client/Pages is empty)
5. No last-active-validator guard
6. No audit logging of state transitions

**Alternatives considered**: Building from scratch — rejected because 80% of the domain model and API surface already exists.

## R2: Consensus Vote Verification Approach

**Decision**: Use canonical signing contract `SHA256("{DocketId}:{DocketHash}:{Approved}:{ValidatorId}")` with the existing `ICryptoModule.VerifySignatureAsync`.

**Rationale**:
- `Sorcha.Cryptography.ICryptoModule` already supports ED25519, P-256, and RSA-4096 signature verification
- The existing `Signature` model in the Validator Service has `PublicKey`, `SignatureValue`, and `Algorithm`
- `ConsensusEngine` already receives vote responses from peers via gRPC — signatures just need to be added to the response and verified
- `SignatureCollector` aggregates votes — the natural place to add verification

**Performance**: Signature verification is CPU-bound (~0.1ms for ED25519). With 10 validators, total verification takes ~1ms — well within the 30s consensus timeout. Can parallelize with `Task.WhenAll` if needed.

**Alternatives considered**:
- HMAC-based vote authentication — rejected (requires shared secrets, doesn't provide non-repudiation)
- TLS mutual authentication only — rejected (doesn't prove vote content was from that validator)

## R3: Transaction Replay Protection Design

**Decision**: Per-wallet, per-register monotonic sequence numbers stored in MongoDB.

**Rationale**:
- MongoDB already used by Register Service for transactions/dockets
- Sequence numbers are small documents (wallet + register + counter)
- Atomic `findOneAndUpdate` with `$inc` provides concurrency safety
- The `Transaction` model needs a `SequenceNumber` field added
- Blueprint Service's existing idempotency key provides application-level protection; this adds chain-level defense

**Sequence number query endpoint**: Add `GET /api/validators/{registerId}/sequence/{walletAddress}` so clients can determine their next number.

**Alternatives considered**:
- Redis-based counters — rejected (not durable enough for security-critical data)
- Nonce-based (random) — rejected (harder for clients to manage, can't detect gaps)
- Timestamp-based ordering — rejected (clock skew issues across distributed nodes)

## R4: Admin UI Technology

**Decision**: Build Blazor WASM pages in `Sorcha.Admin.Client` using MudBlazor components, consistent with the existing UI pattern.

**Rationale**:
- `Sorcha.Admin` project exists with host infrastructure
- `Sorcha.Admin.Client` exists for WASM components (currently empty)
- MudBlazor is the standard component library used across all Sorcha UIs
- API Gateway already proxies to Validator Service

**Pages needed**:
1. `ValidatorManagement.razor` — list + actions (approve/suspend/revoke)
2. `ValidatorDetail.razor` — full validator info with audit history

## R5: MongoDB Persistence for Validator Registry

**Decision**: Add a MongoDB collection for validator registrations alongside the Redis cache.

**Rationale**:
- Redis is volatile — validator state must survive Redis restarts
- MongoDB is already used by the Validator Service (via Register Service client)
- Write-through pattern: write to MongoDB first, then update Redis cache
- On startup, hydrate Redis from MongoDB

**Collection**: `validators` in the Register database
- Document: `{ _id: "{registerId}:{validatorId}", registerId, validatorId, publicKey, status, grpcEndpoint, registeredAt, approvedAt, approvedBy, lastStateChange, metadata }`
- Index: `{ registerId: 1, status: 1 }` for efficient per-register status queries
