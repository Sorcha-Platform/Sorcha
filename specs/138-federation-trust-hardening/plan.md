# Implementation Plan: Federation Trust Hardening

**Branch**: `138-federation-trust-hardening` | **Date**: 2026-05-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/138-federation-trust-hardening/spec.md`

## Summary

Close the soft network-edge trust gaps found by the 2026-05-24 red-team review, so that **every input crossing a node boundary is verified against a signature anchored in sealed register state** rather than trusted by sender or transport. Six independently-shippable slices: status-list signature verification (US1), authenticated peers (US2), sealed-roster vote authority (US3), verified blueprint recovery (US4), presentation replay hardening (US5), and open-participant key binding (US6). The cryptographic core (DID resolution, issuer-signature verification, chain integrity, double-vote *detection*) already exists and is sound — this feature wires those existing anchors into the surfaces that currently bypass them.

## Technical Context

**Language/Version**: C# / .NET 10 (per constitution Technology Stack)
**Primary Dependencies**: ASP.NET Core Minimal APIs, Grpc.Net 2.71, Microsoft.IdentityModel (JWT), `Sorcha.Cryptography` (ED25519 / P-256 / RSA / ML-DSA), JsonSchema.Net, OpenTelemetry 1.12, Scalar 2.10, `Sorcha.Verifier.Engine`
**Storage**: PostgreSQL (EF Core — Peer node identity, validator audit), MongoDB (register transactions / dockets / sealed roster), Redis (operational caches, presently the roster cache that US3 demotes)
**Testing**: xUnit + FluentAssertions + Moq; integration via WebApplicationFactory; adversarial negative tests mandatory per FR-021
**Target Platform**: Linux containers orchestrated by .NET Aspire 13 / docker-compose
**Project Type**: Microservices (multi-service) — touches Verifier engine, Peer, Validator, Blueprint, Wallet, Tenant services + shared Register.Models
**Performance Goals**: Not a throughput feature. Verification additions (signature checks, exp checks, roster lookups) MUST add bounded, sub-perceptible latency and MUST NOT regress consensus liveness.
**Constraints**: All new trust decisions fail **closed**. Configurable thresholds (clock skew, status-list freshness, KB-JWT expiry, peer-registration rate, validator liveness timeout) with secure defaults. No backward-compat migration burden (pre-release).
**Scale/Scope**: Existing federation scale (bounded validator roster ≤ 25 per `GOV-8`; small peer mesh). No open-membership scale assumptions (that is the backlogged `PERM-*` feature).

## Constitution Check

*GATE: evaluated against `.specify/memory/constitution.md` v1.1.0.*

| Principle | Assessment | Verdict |
|-----------|------------|---------|
| I. Microservices-First | Each US is contained to one service (or one shared model + its consumers). No new upward dependencies; verification logic stays in the engine/service that owns the boundary. Core/Register.Models changes (roster, blueprint hash) are pure model additions consumed downward. | ✅ Pass |
| II. Security First | This feature *is* the principle — zero-trust at every node boundary, fail-closed, input validation on external boundaries, no secrets committed. Node identity private keys encrypted at rest (AES-256-GCM via existing KPP). | ✅ Pass (core intent) |
| III. API Documentation | New/changed control-transaction action types and any config-surfaced endpoints documented; gRPC proto changes documented in `contracts/`. XML docs on all new public members; Scalar summaries where HTTP endpoints change. | ✅ Pass (commitment) |
| IV. Testing | >85% on new code. Every forged/unsigned/replayed/out-of-roster variant gets a deterministic negative test (FR-021). Integration tests per service boundary. | ✅ Pass (commitment) |
| V. Code Quality | async/await, DI, nullable enabled, no Release warnings. Reuse existing `IIssuerKeyResolver`, `CryptoModule`, `AddRateLimiting`, storage-registration-log patterns. | ✅ Pass |
| VI. Blueprint Standards | US4 adds a content-hash field to published-blueprint records; no blueprints authored in C#. | ✅ Pass |
| VII. Domain-Driven Design | Use ubiquitous terms: Validator, Docket, Register, Participant, Publish, Disclosure, Control transaction. New terms (Node Identity, Sealed Roster, Liveness-Timeout proof, Ejection record) are additive and consistent. | ✅ Pass |
| VIII. Observability | FR-022 mandates metrics for every security rejection on existing OTel meters (`sorcha_*`), plus health-degradation signals where a node falls back to a weaker mode. | ✅ Pass |

**No violations.** Complexity Tracking section intentionally empty. The one item worth flagging — mTLS + per-node key material (US2) introduces new certificate/key lifecycle — is squarely inside Principle II and is justified, not a deviation.

## Project Structure

### Documentation (this feature)

```text
specs/138-federation-trust-hardening/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — decisions per user story (grounded in current code)
├── data-model.md        # Phase 1 — entities, fields, state transitions
├── quickstart.md        # Phase 1 — adversarial validation walkthrough per US
├── contracts/           # Phase 1 — proto changes, control-tx action types, config keys, metrics, verifier contract
│   ├── peer-protocol.md
│   ├── validator-control-transactions.md
│   ├── verifier-statuslist-and-kbjwt.md
│   └── config-and-metrics.md
└── checklists/
    └── requirements.md  # Spec quality checklist (passing)
```

### Source Code (repository root) — surfaces touched, by user story

```text
# US1 — Status-list signature verification (verifier side)
src/Common/Sorcha.Verifier.Engine/
├── StatusListCache.cs                    # verify JWT sig in ParseJwt; fail-closed on fetch/verify failure
├── IIssuerKeyResolver.cs                 # reuse existing resolver contract (no change)
└── VerifiablePresentationValidator.cs    # (also US5) status check already at verify time
src/Apps/Sorcha.Verifier/Extensions/ServiceCollectionExtensions.cs  # inject resolver into StatusListCache; ClockSkew config
src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs  # add kid to header (resolver hygiene)

# US2 — Authenticated peers
src/Services/Sorcha.Peer.Service/
├── Program.cs                            # fail-closed transport outside Development; node-identity bootstrap
├── Interceptors/PeerAuthInterceptor.cs   # stop silent-anonymous; enforce in Prod/Staging
├── Interceptors/RateLimitInterceptor.cs  # NEW — gRPC RESOURCE_EXHAUSTED throttle
├── GrpcServices/PeerDiscoveryServiceImpl.cs   # challenge-response on RegisterPeer
├── GrpcServices/PeerHeartbeatGrpcService.cs   # validate sequence/timestamp; verify ad signatures
├── Services/RegisterAdvertisementService.cs   # sign/verify advertisements
├── Services/NodeIdentityService.cs       # NEW — per-node ED25519 key lifecycle
├── Models/PeerNode.cs                    # +PublicKey, +LastHeartbeatSequenceNumber
└── Protos/peer_communication.proto, peer_heartbeat.proto  # +signature/+public_key fields, challenge msgs

# US3 — Sealed-roster vote authority
src/Common/Sorcha.Register.Models/
├── RegisterPolicy.cs                     # CreateDefault(): RegistrationMode.Public -> Consent (line ~78)
├── ValidatorRoster.cs                    # ejection state on entries
└── RegisterControlRecord.cs              # sealed roster (exists)
src/Services/Sorcha.Validator.Service/Services/
├── ConsensusEngine.cs                    # vote authority from sealed roster, not Redis cache (~459-500)
├── ValidatorRegistry.cs                  # roster reconstruction from chain; cache becomes derived/non-authoritative
├── ControlDocketProcessor.cs             # process new ejection + liveness-violation control actions
└── BadActorDetector.cs                   # emit sealed ejection control-tx on detected equivocation

# US4 — Verified blueprint recovery
src/Common/Sorcha.ServiceClients/.../IRegisterServiceClient.cs   # PublishedBlueprintEntry +ContentHash
src/Services/Sorcha.Blueprint.Service/Services/Implementation/BlueprintRecoveryService.cs  # verify hash before store

# US5 — Presentation replay hardening
src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs  # mandatory KB-JWT exp + wall-clock+skew check

# US6 — Open-participant carried-key binding
src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs  # bind carried key to artifact
src/Services/Sorcha.Tenant.Service/Services/RegisterInvitationService.cs  # invitation nonce/commitment lookup

# Cross-cutting
src/Common/Sorcha.ServiceDefaults/  # rate-limit + mTLS helpers, OTel meters for new rejection metrics
```

**Structure Decision**: Multi-service. Each user story is a self-contained slice owned by one service (or one shared model plus its direct consumers), preserving Principle I. Build order follows spec priority and the dependency that US3 establishes the on-chain-roster primitives the backlogged `PERM-*` feature reuses.

## Build Sequence (independently shippable slices)

1. **US1** (P1, smallest blast radius — verifier-only): status-list signature verification + fail-closed. Ship first; no cross-service coordination. Each subsequent story is independently testable and deployable.
2. **US2** (P1): peer identity + signed advertisements + replay validation + mTLS-outside-dev + gRPC rate limiting.
3. **US3** (P1): sealed-roster vote authority + Consent default + automatic deterministic ejection + liveness-timeout proof. Largest slice; establishes `PERM-*` primitives.
4. **US4** (P2): published-blueprint content hash + recovery verification.
5. **US5** (P2): mandatory KB-JWT expiry + wall-clock/skew check (revocation re-check already at verify time once US1 lands).
6. **US6** (P3): bind open-participant carried delivery key to invitation/pre-registration artifact.

Phase 2 (`/speckit.tasks`) expands each slice into ordered, testable tasks.

## Complexity Tracking

*No constitution violations — section intentionally empty.*
