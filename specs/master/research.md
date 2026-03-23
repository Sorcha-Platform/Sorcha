# Research: Register Subscriptions & Private Register Invitations

## R1: Wallet Service API for Org Wallet Creation

**Decision:** Use existing `CreateWalletRequest` with conventions — no API contract changes.
**Rationale:** The wallet API already supports `Name`, `Algorithm`, and `Tags` fields. Using `owner: "org:{orgId}"` and `Tags: { ownerType: "Organization", ownerId: "{orgId}" }` avoids breaking changes while providing sufficient metadata.
**Alternatives considered:**
- Extend `CreateWalletRequest` with `OwnerType` discriminator — rejected: unnecessary contract change
- Create separate org wallet endpoint — rejected: over-engineering for same underlying HD wallet

## R2: ED25519 to X25519 Conversion for Encryption

**Decision:** Derive X25519 encryption key from ED25519 signing key. Store `EncryptionPublicKey` on Organisation record.
**Rationale:** ED25519 is signing-only. X25519 (Curve25519 ECDH) provides key agreement for encryption. The conversion is mathematically sound (both use Curve25519). `Sorcha.Cryptography` may need `ED25519ToX25519` utility if not already present.
**Alternatives considered:**
- Dual key pairs (ED25519 + RSA-4096) — rejected: unnecessary complexity, two keys to manage
- RSA-4096 for both signing and encryption — rejected: inconsistent with participant wallets (ED25519)
- NaCl/libsodium box (X25519 + XSalsa20-Poly1305) — considered: XChaCha20-Poly1305 preferred for larger nonce

## R3: Cross-Service Orchestration for Auto-Subscribe

**Decision:** API Gateway / UI layer orchestrates subscription creation after register creation.
**Rationale:** The JWT already contains org context. Creating a Register→Tenant service dependency adds coupling. The gateway pattern keeps services independent.
**Alternatives considered:**
- Register Service calls Tenant Service via gRPC — rejected: new cross-service dependency
- Event-driven (message bus) — rejected: no message bus in current architecture, over-engineering
- Register Service emits event, Tenant subscribes — rejected: same coupling, deferred

## R4: Nonce Replay Protection Strategy

**Decision:** Hybrid — PostgreSQL table for fast lookup, ledger record for audit.
**Rationale:** Checking the entire ledger for a nonce is O(n) and expensive. PostgreSQL with unique index on `Nonce` provides O(1) lookup. The ledger record provides immutable audit trail.
**Alternatives considered:**
- Ledger-only — rejected: too slow for real-time validation
- PostgreSQL-only — rejected: loses immutable audit trail
- Redis with TTL — rejected: nonces must persist beyond invitation expiry for replay protection

## R5: Subscription Status Model

**Decision:** `Pending` → `Active` with async retry for peer subscription failures.
**Rationale:** Peer Service subscription may fail (network issues, peer unavailable). Creating the Tenant record immediately provides user feedback. Background retry promotes to Active.
**Alternatives considered:**
- Synchronous only (fail entire request) — rejected: poor UX, transient failures block subscription
- No Pending state (always Active) — rejected: hides real subscription status from user
- Saga pattern with compensation — rejected: over-engineering for this use case

## R6: DID Method for Organisations

**Decision:** New `did:sorcha:org:<walletAddress>` method, separate from `did:sorcha:w:`.
**Rationale:** Audit clarity — ledger records distinguish org-level actions from individual wallet actions. Resolution is identical (wallet address → public key via Wallet Service).
**Alternatives considered:**
- Reuse `did:sorcha:w:` — rejected: ambiguous in audit trail, can't tell org from individual
- `did:sorcha:o:` (shorter) — rejected: less readable, `org` is clearer
- `did:web:` for orgs — rejected: requires web infrastructure, not self-sovereign

## R7: Register Name Cache Staleness

**Decision:** Accept eventual consistency. Register names rarely change. No active refresh mechanism.
**Rationale:** Adds complexity for minimal benefit. If a register name changes, subscriptions show stale name until re-subscribed or manually refreshed. A future enhancement could add a background refresh.
**Alternatives considered:**
- Active refresh via webhook — rejected: no webhook infrastructure
- TTL-based refresh — rejected: adds background service complexity for rare scenario
- Always fetch from Register Service — rejected: cross-service call on every page load
