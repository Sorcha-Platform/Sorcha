# Research: Federation Trust Hardening (Feature 138)

**Date**: 2026-05-24
**Method**: Three parallel read-only code-exploration agents over the verifier engine, peer service, and validator/blueprint services. All file:line references reflect current `master`/`138` state. No `NEEDS CLARIFICATION` remain (US6 scope resolved in-scope).

The guiding decision across every story: **reuse the existing sealed-state trust anchors rather than inventing new ones.** The red-team review confirmed DID resolution, issuer-signature verification, chain integrity, and double-vote *detection* already exist and are sound; the gaps are that several surfaces don't *consult* them.

---

## US1 — Status-list signature verification

**Decision**: Verify the status-list JWT signature inside `StatusListCache.ParseJwt()` using the existing `IIssuerKeyResolver`, pin the `iss` claim to the expected org DID, and make every failure path (fetch failure, signature failure, issuer mismatch, expiry) **fail closed** — the caller treats "cannot verify" as revoked/unverifiable.

**Rationale**: The trustworthy resolver already exists and is wired in the verifier (`DidResolverBackedIssuerKeyResolver` → register-anchored keys). The publisher already signs the list (`CitizenStatusListPublisher` with the `citizen-status-signing` derivation, `iss = did:sorcha:org:{orgId:N}`). The only missing half is verification. Smallest possible blast radius — confined to the verifier engine.

**Current shape**:
- `src/Common/Sorcha.Verifier.Engine/StatusListCache.cs:119-145` — `ParseJwt()` base64url-decodes the payload and reads `status_list.lst` + `exp` **without touching the signature**. On missing `exp` it defaults to +24h.
- `StatusListCache.cs:88-96` — `GetOrFetchAsync()` returns **stale cache on any fetch exception** (fail-open). This must become fail-closed.
- `IIssuerKeyResolver.cs:22-39` — `ResolveAsync(issuer, kid, ct) → Task<JsonElement?>` returns a public JWK. Composite tries DID-backed then JWK-registry.
- `CitizenStatusListPublisher.cs:197,225,244` — signs with `EdDSA`/`ES256`, `iss = did:sorcha:org:{orgId:N}`, **no `kid` in header**.
- Consumed at `VerifiablePresentationValidator.cs:400-413` via `IsRevokedAsync(uri, idx, ct)` — already at verify time.

**Decisions / sub-choices**:
- Add a `kid` to the publisher's JWT header (publisher-side hygiene) so the resolver can match a specific verification method; resolver must still fall back to "first published VM matching `alg`" when `kid` is absent (back-compat with already-issued lists during pre-release).
- Inject `IIssuerKeyResolver` into `StatusListCache` (currently it has none). The verifier DI (`src/Apps/Sorcha.Verifier/Extensions/ServiceCollectionExtensions.cs:30-87`) already constructs the composite resolver — pass it in.
- Expected-issuer pinning: the consuming credential's `status.status_list.uri` and the delegation/credential `iss` together determine the expected org DID; reject if the fetched list's `iss` ≠ expected.

**Alternatives considered**: (a) Verify at the presentation validator instead of in the cache — rejected: the cache is the single fetch/parse choke point, verifying there covers all callers. (b) Trust transport (TLS to a known publisher host) — rejected: violates the unifying criterion; any node can serve the list.

---

## US2 — Authenticated peers

**Decision**: Give every node a persistent **ED25519 node-identity key**; require a challenge-response signature on `RegisterPeer`; sign advertisements and heartbeats and reject unsigned ones; validate the already-transmitted `sequence_number` + `timestamp` for monotonicity/freshness; enforce mTLS + non-anonymous auth as **fail-closed outside Development**; add a gRPC rate-limit interceptor.

**Rationale**: The transport is currently forgeable end-to-end (no identity, cleartext, silent-anonymous auth, unsigned ads, unvalidated replay counters). `Sorcha.Cryptography`'s `CryptoModule` already supports ED25519, so node keys are net-new material but not net-new crypto. The anti-replay fields already exist on the wire — they just need to be checked.

**Current shape**:
- `PeerDiscoveryServiceImpl.cs:71-128` — `RegisterPeer` validates only non-empty `PeerId`/`Address`. `PeerNode.PeerId` is an arbitrary `string` (`PeerNode.cs:16-18`).
- `PeerAuthInterceptor.cs:40-108` — JWT validation is skipped entirely when `JwtSettings:SigningKey` is empty; unauthenticated peers pass through as anonymous.
- `Program.cs:113` — `Http2UnencryptedSupport` switch enables cleartext HTTP/2; Kestrel endpoints (54-78) carry no TLS. `PeerConnectionPool.cs:535-546` builds channels with no client cert.
- `peer_heartbeat.proto:115-137` — `RegisterAdvertisement` has no signature/ownership-proof field. Sent at `PeerHeartbeatService.cs:227`, received at `PeerHeartbeatGrpcService.cs:54-71`.
- `peer_heartbeat.proto:48` — `sequence_number` received at `PeerHeartbeatGrpcService.cs:46,115` but never validated. No per-peer last-sequence state on `PeerNode`.
- `Program.cs:42` — `AddRateLimiting()` is called but no policy is applied to the gRPC methods.

**Decisions / sub-choices**:
- **Node key source**: generate ED25519 on first startup via `CryptoModule`, persist the private key encrypted (existing Key Protection Provider) in `PeerDbContext`; export the public key in registration/heartbeat. Chosen over deriving from the shared JWT key (weaker isolation) — a new `NodeIdentityService` owns the lifecycle.
- **`PeerId` becomes the node public-key thumbprint** (or is bound to it), so identity is self-certifying and a node cannot register under another's id.
- **Replay**: add `LastHeartbeatSequenceNumber` to `PeerNode`; reject non-advancing sequence and stale timestamps (reuse a configured skew window, see US5).
- **Transport**: read an `EnableTls`/environment gate; in Production/Staging refuse cleartext and require mTLS (client-cert validation server-side, client cert in `PeerConnectionPool`). Dev keeps cleartext.
- **Rate limiting**: gRPC bypasses Minimal-API `.RequireRateLimiting`, so add a `RateLimitInterceptor` returning `RESOURCE_EXHAUSTED`, fed by the same `RateLimitSettings`.

**Alternatives considered**: (a) IP allowlist for admission — rejected: not cryptographic, defeated by spoofing/NAT. (b) Proof-of-work on registration — rejected: that's Sybil-economics, explicitly the backlogged `PERM-*` feature; federation gates participation by signed identity + roster, not cost.

---

## US3 — Sealed on-chain roster as sole vote authority

**Decision**: Derive consensus vote authority from the roster **sealed in `RegisterControlRecord.Validators`** (reconstructed from committed control transactions), not the Redis/Mongo `ValidatorRegistry` cache; flip the default admission mode to **Consent**; convert the existing in-memory double-vote detection into an **automatic, deterministic, sealed ejection** (a control transaction every honest node produces and applies identically); and produce a **sealed liveness-timeout proof** that ejects a withholding validator.

**Rationale**: This is the structural heart of federation hardening, and it deliberately builds the two primitives (`authoritative on-chain roster`, `deterministic equivocation handling`) that the backlogged permissionless feature (`PERM-1..PERM-5`) will extend by swapping roster-admission for stake-admission and roster-ejection for collateral-slashing. The roster already lives on-chain; the gap is that the vote check reads the cache.

**Current shape**:
- Sealed roster: `RegisterControlRecord.cs:84` (`Validators`), `ValidatorRoster.cs:50-97` (`ActiveValidators`/`VerifiableValidators`, `Validate()`), updated via `control.policy.update` in `ControlDocketProcessor.cs:32,200-293`.
- Vote authority **reads the cache**: `ConsensusEngine.cs:459-500` — double-vote detection (462), registered+Active check against `ValidatorRegistry.GetValidatorAsync` (477-487, Redis/Mongo), pubkey match (498-500).
- **No chain-reconstruction path yet** — `ValidatorRegistry.cs:358` comment: "Chain-based validator discovery can be added when register transactions include validator registration metadata."
- Admission default: `RegisterPolicy.CreateDefault():78` = `RegistrationMode.Public`. Consent logic exists at `ValidatorRegistry.cs:255-274`; new validators are `Active` immediately in Public, `Pending` in Consent (302-304).
- Ejection is manual + cache-only: `RevokeValidatorAsync` (`ValidatorRegistry.cs:687-756`) mutates Redis/Mongo, writes an audit row, **no control transaction**. `BadActorDetector.cs:108-141` logs incidents in memory only (purged at 286-325).
- Liveness: `DocketTimeoutSeconds` (`RegisterPolicy.cs:267-268`) drives local vote-collection deadlines only; no sealed proof.

**Decisions / sub-choices**:
- Reconstruct the effective roster from the latest sealed control record (and cache it as a *derived, non-authoritative* view — the cache may accelerate lookups but the sealed record is the source of truth; on divergence the seal wins, FR-014). Coordinate with `GOV-6` (roster reconstruction caching).
- New control-transaction action types: `control.validator.eject` (carries the equivocation proof: the two conflicting signed votes) and `control.validator.liveness-violation` (carries the timeout proof). Both are deterministic — any node observing the same evidence produces the identical control tx, so honest nodes converge.
- Flip `CreateDefault()` to `Consent`; accept the test-cascade cost (pre-release, no migration burden).
- Guard against ejection dropping the roster below workable quorum (surface the condition; ties to `GOV-5` deadlock detection).

**Alternatives considered**: (a) Keep the cache authoritative but sign it — rejected: the cache is per-node mutable state, not consensus-sealed; signing it doesn't make it agreed. (b) Operator-driven ejection with an alert — rejected: that's the current procedural control the spec explicitly replaces with a technical one (SC-005).

---

## US4 — Verified blueprint recovery

**Decision**: Add a `ContentHash` (SHA-256 of canonical blueprint JSON) to the published-blueprint record, sealed at publish time; on recovery, recompute and compare before storing — reject on mismatch or missing provenance.

**Rationale**: Closes the F137 US3 gap where recovery trusts the register's HTTP response over an unencrypted channel. The register already carries the canonical blueprint; it just doesn't expose a verifiable digest.

**Current shape**:
- `BlueprintRecoveryService.cs:262-323` — `RecoverFromRegisterAsync` fetches via `GetPublishedBlueprintsAsync` (268) and stores (294-310) with **no hash check**.
- `IRegisterServiceClient.cs:758-770` — `PublishedBlueprintEntry` has `BlueprintJson` but **no `ContentHash`/`Digest`**.

**Decisions / sub-choices**: compute the hash in the `control.blueprint.publish` path (sealed on-chain) so the digest itself is consensus-anchored; recovery verifies against it. Define a single canonical-JSON serialization so producer and consumer hash identically.

**Alternatives considered**: signing each blueprint with the publisher key — heavier and redundant given the register already seals the publish transaction; a sealed digest is sufficient and cheaper.

---

## US5 — Presentation replay hardening

**Decision**: Require an `exp` claim on the KB-JWT itself and validate it against wall-clock time within a configurable skew window, *before* delegation/status validation. Revocation is already re-checked at verify time, so US1's fail-closed change completes the mid-session-revocation requirement.

**Rationale**: The session nonce/aud/TTL leaves a multi-minute replay window for a captured proof. An independently-enforced short `exp` closes it without changing the session model.

**Current shape**:
- `VerifiablePresentationValidator.cs:196-206` — nonce + aud checked on KB-JWT; **no `exp` check**. The delegation credential has `exp` (384-391) but the KB-JWT does not. `_clock` is an injected `TimeProvider` (37,50); **no skew tolerance configured** (delegation check uses strict `exp <= now`).
- Status-list checked at verify time already (`:400-413`), so US1 fail-closed + verify-time check together satisfy FR-019.
- `VerifierSession.cs:34-38` — `CreatedAt`/`ExpiresAt`; no revocation state on session (good — checked fresh).

**Decisions / sub-choices**: add `Verifier:ClockSkewSeconds` (default 60) and apply the same tolerance to the KB-JWT `exp`, the delegation `exp`, and US2's heartbeat timestamp freshness for consistency. Reject KB-JWTs with no `exp` (FR-017).

**Alternatives considered**: shortening session TTL instead — rejected: doesn't bind the proof itself and still allows replay within the shorter window.

---

## US6 — Open-participant carried-key binding

**Decision**: For an **unpublished** open participant, bind the carried delivery key to a verifiable prior artifact — a `RegisterInvitationRecord` (which already carries a `Nonce`) or a sealed pre-registration — and reject unbound carried keys. Published participants are unaffected ("published wins" already protects them).

**Rationale**: Today the carried key for an open slot is accepted from a submission form field with no commitment, so an attacker racing into the slot can substitute their own delivery key. The Register Invitations feature already provides a per-invitation nonce that can commit the key.

**Current shape**:
- `ActionExecutionService.cs:616,2183-2209` — `ResolveCarriedHolderKeys` reads `holderJwk`/`encryptionPublicKey`/`algorithm` from a JSON-pointer form field.
- Precedence (619-648): published record wins (`ResolvePublicKeyAsync`), else carried key, else fail-closed `VAL_RUNTIME_CRED_004`. **No invitation binding today.**
- `RegisterInvitationRecord` (Tenant Service) carries `Nonce` and `Status` usable for freshness/commitment.

**Decisions / sub-choices**: when the participant is open + unpublished, require the carried key to match a commitment recorded against a valid, unconsumed invitation (or sealed pre-registration); otherwise reject. Keep the published-wins path untouched.

**Alternatives considered**: encrypting the carried key in transit — rejected: it's already inside a signed submission; the gap is *authorization of the key*, not its confidentiality.

---

## Cross-cutting decisions

- **Fail-closed everywhere**: every new verification returns reject on inability to verify; no silent fallback to a weaker mode.
- **Observability** (FR-022): each rejection class emits a counter on an existing `Sorcha.*` OTel meter (names in `contracts/config-and-metrics.md`); nodes that fall back to a weaker posture surface a health degradation.
- **Config defaults secure**: skew 60s, status-list freshness from list `exp`, KB-JWT exp short (e.g. 120s), peer-registration rate via `RateLimitSettings`, validator liveness timeout from policy `DocketTimeoutSeconds`. Values are tunable, documented in `contracts/config-and-metrics.md`.
- **Demo/dev bridges stay dev-only**: the demo-mint JWK-registry resolver path must be structurally excluded from production composition (not flag-gated), consistent with the spec assumption.
