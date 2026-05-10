---

description: "Task list for Feature 120 — Production Issuer Signature Verification"
---

# Tasks: Production Issuer Signature Verification

**Input**: Design documents from `specs/120-production-issuer-signature-verification/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md
**Authoritative design**: `docs/superpowers/specs/2026-05-09-production-issuer-signature-verification-design.md`

**Tests**: Tests are REQUIRED per Constitution IV (≥85% coverage on new code) and per the Independent-Test descriptions on each user story in spec.md. Test tasks appear inline within each user story phase.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. Within each story, tests come before implementation (TDD encouraged per Constitution V).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US6, mapping to spec.md)
- All file paths absolute from repo root

## Path conventions

Sorcha is a microservices/.NET monorepo. Source under `src/{Apps,Common,Core,Services}/`. Tests under `tests/`. See plan.md → "Source code (repository root)" for the per-feature layout.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify branch, prerequisites, and stack readiness. No new project scaffolding required — Feature 120 lands inside existing services.

- [ ] T001 Verify branch `120-production-issuer-signature-verification` is checked out and `master` is up-to-date in `C:/projects/Sorcha`.
- [ ] T002 [P] Verify Docker dev stack starts cleanly: `docker-compose up -d` then `docker-compose ps` shows all services healthy.
- [ ] T003 [P] Verify `dotnet test` baseline passes on the branch before any changes: `cd C:/projects/Sorcha && dotnet test --no-restore` (informational baseline; preserves regression detection later).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Land the shared infrastructure that every user story depends on. Phase 0 cleanup (legacy `IDIDResolver` retirement) is the precondition; the new resolver-registry method signature, kid-matching helper, cache scaffold, and DI wiring close the rest of the gap.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete. Phase 0 cleanup ships as a standalone PR before the rest of this branch (per plan.md → Phase 0 row).

### Phase 0 (legacy retirement) — ships as separate PR

- [x] T004 Migrate the single `IDIDResolver` consumer at `src/Services/Sorcha.Register.Service/Program.cs:205` to call `IDidResolverRegistry.ResolveAsync` and `IRegisterServiceClient.GetTransactionAsync` instead. Update the consuming code path to extract the public key from the W3C `DidDocument.VerificationMethod` (or from the transaction payload directly via the register client).  *(shipped via PR #592)*
- [x] T005 Delete `src/Core/Sorcha.Register.Core/Services/IDIDResolver.cs`, `src/Core/Sorcha.Register.Core/Services/DIDResolver.cs`, and the `DIDResolutionResult` type they share. Remove the DI registration in `Sorcha.Register.Service/Program.cs`.  *(shipped via PR #592)*
- [x] T006 [P] Update spec references: search `specs/031-register-governance/` and `specs/039-verifiable-presentations/` for `IDIDResolver` mentions; replace with `IDidResolverRegistry` or annotate as historical.  *(shipped via PR #592)*
- [x] T007 [P] Update `.specify/tasks/deferred-tasks.md` and `.specify/specs/sorcha-register-service.md` to drop `IDIDResolver` references.  *(shipped via PR #592)*
- [x] T008 Run full `dotnet test` after T004-T007 land; confirm green. This is the Phase 0 ship gate — open a separate PR for these changes before continuing.  *(CI green on PR #592)*

### Shared infrastructure for Phases 1–6

- [x] T009 Confirm `Sorcha.Cryptography` exposes (or add) an RFC 7638 JWK thumbprint helper returning base64url SHA-256 (43 chars, no padding). Target file: `src/Common/Sorcha.Cryptography/JsonWebKeyThumbprint.cs` (new if absent).
- [x] T010 [P] Add `KidThumbprintHelper` at `src/Common/Sorcha.ServiceClients.Http/Did/KidThumbprintHelper.cs`. Two public statics: `bool TryMatchExact(DidDocument doc, string kid, out VerificationMethod vm)` and `bool TryMatchByThumbprint(DidDocument doc, string kid, out VerificationMethod vm)`. Latter computes thumbprint of each VM's `publicKeyMultibase`/`publicKeyJwk` on demand.  *(thumbprint logic inlined here rather than referencing `Sorcha.Cryptography` to preserve the architectural boundary documented on `Multicodec.cs` — `Sorcha.ServiceClients.Http` stays mobile-friendly.) Multibase-only VMs return false in v1; Sorcha emits dual VMs (Feature 120 D3) so the JWK form is always available.*
- [x] T011 [P] Add `DidResolverCache` at `src/Common/Sorcha.ServiceClients.Http/Did/DidResolverCache.cs`. Memory-backed concurrent cache with per-method TTL (default `did:web` 1h, `did:sorcha` infinite, `did:key` infinite). Negative-result entries with 60s TTL. Concurrent-call coalescing via `LazyAsync`. Configuration via `DidResolver:Cache:WebTtlMinutes` and `DidResolver:Cache:NegativeTtlSeconds`.
- [x] T012 Add the `ResolveWithAlsoKnownAsAsync(string did, CancellationToken ct = default)` method signature to `src/Common/Sorcha.ServiceClients.Http/Did/IDidResolverRegistry.cs`. Implementation lands in US4; signature lands here so US1 can compile against it.
- [x] T013 Add a stub `ResolveWithAlsoKnownAsAsync` body in `src/Common/Sorcha.ServiceClients.Http/Did/DidResolverRegistry.cs` that delegates to `ResolveAsync` (no cross-resolution yet). Marked with `// TODO(Feature 120 US4): cross-resolve alsoKnownAs with key-material verification`. Real implementation lands in US4.
- [x] T014 [P] Subscribe `DidResolverRegistry` to `transaction:confirmed` Redis stream events for `did:sorcha:*` cache invalidation. Use `Sorcha.Events.IEventSubscriber`. Handler invalidates cache entries whose canonical primary DID matches the affected wallet/org. Wire DI in `src/Common/Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs`.  *(BackgroundService + DI registration land here; the bridge to the real `IEventSubscriber<TransactionConfirmedEvent>` is exposed as `IDidCacheTransactionEventSource`. Service-level startup code provides the impl — required to avoid a circular project dep `Sorcha.ServiceClients.Http` → `Sorcha.Register.Core` → `Sorcha.ServiceClients` → `Sorcha.ServiceClients.Http`.)*
- [x] T015 Add new `Sorcha.Verifier.IssuerSignature` and `Sorcha.Did.Resolver` OpenTelemetry meters. Define counters per data-model.md "Telemetry-touching shapes" and contracts/did-resolver-registry-contract.md. Counter registration lives in DI extension methods alongside resolver and verifier registration.  *(`Sorcha.Did.Resolver` meter + counter set landed in this phase via `DidResolverMetrics`. `Sorcha.Verifier.IssuerSignature` meter is deferred to US1 — no consumer in Phase 1 and its natural home is `Sorcha.Citizen.Verifier`.)*
- [x] T016 [P] Unit tests for `KidThumbprintHelper` at `tests/Sorcha.ServiceClients.Tests/Did/KidThumbprintHelperTests.cs`. Cover: exact-match success, exact-match miss, thumbprint-match success, thumbprint-match miss, malformed VM (no key material).
- [x] T017 [P] Unit tests for `DidResolverCache` at `tests/Sorcha.ServiceClients.Tests/Did/DidResolverCacheTests.cs`. Cover: positive TTL respected, negative TTL respected (shorter), method-specific TTLs, concurrent-call coalescing, explicit invalidation by DID.

**Checkpoint**: Foundation ready. Phase 0 cleanup is shipped as a separate PR (T004-T008). T009-T017 land on this feature branch as the foundational commits.

---

## Phase 3: User Story 1 — Verifier rejects unverifiable credentials (Priority: P1) 🎯 MVP (with US2)

**Goal**: Production `IIssuerKeyResolver` resolves the credential's `iss` to a key via `IDidResolverRegistry`, the verifier rejects credentials whose signature does not verify, and three distinct failure modes (unresolved DID, unmatched kid, signature mismatch) are visible in operational logs and metrics.

**Independent Test**: Submit three credentials to a presentation flow — valid, tampered-signature, unresolvable-issuer — and confirm distinct failure-mode counter increments and rejection responses.

> **Note**: US1 ships value only when US2 also ships (US1 has nothing to verify against without published org DID documents). Treat US1+US2 together as the MVP.

### Tests for User Story 1

- [x] T018 [P] [US1] Unit tests for `DidResolverBackedIssuerKeyResolver` at `tests/Sorcha.Citizen.Verifier.Tests/Services/DidResolverBackedIssuerKeyResolverTests.cs`. Cover: happy path (DID resolves, kid exact-matches VM, key returned), kid thumbprint-fallback, DID unresolved (returns null + counter), kid unmatched (returns null + counter), document has no `verificationMethod` (returns null + counter).
- [ ] T019 [P] [US1] Integration test for the verifier's full presentation flow with enforce-on at `tests/Sorcha.Citizen.Verifier.Tests/Integration/PresentationVerificationIntegrationTests.cs`. Use the existing `JwkRegistryIssuerKeyResolver` for DI override per test scenario.  *(deferred — existing `VerifiablePresentationValidatorTests` already exercise enforce-on rejection and the holder→device chain end-to-end with a JwkRegistry resolver. A dedicated integration test is more useful once US2's issuance ceremony is online — it lets the test resolve a real `did:sorcha:org:*` document instead of mocking it. Tracking this with US2 follow-up.)*

### Implementation for User Story 1

- [x] T020 [US1] Create `DidResolverBackedIssuerKeyResolver` at `src/Apps/Sorcha.Citizen.Verifier/Services/DidResolverBackedIssuerKeyResolver.cs`. Constructor takes `IDidResolverRegistry`, `ILogger`, and a meter. Method `ResolveAsync(string issuer, string kid, CancellationToken ct)` calls `_registry.ResolveWithAlsoKnownAsAsync(issuer)` then `KidThumbprintHelper.TryMatchExact` then `KidThumbprintHelper.TryMatchByThumbprint`. Returns `IssuerPublicKey?` (null on any failure mode). Increments three-way failure-mode counters per FR-003.  *(returns `JsonElement?` to match the existing `IIssuerKeyResolver` signature — no `IssuerPublicKey` type yet. Outcome counter `sorcha_verifier_issuer_resolve_outcome_total` tagged with `outcome` + `kid_match_mode`.)*
- [x] T021 [US1] Update `src/Apps/Sorcha.Citizen.Verifier/Services/IIssuerKeyResolver.cs` DI registration: `DidResolverBackedIssuerKeyResolver` becomes the production default; `OptOutIssuerKeyResolver` is removed from production wiring. Test composition retains the option to substitute `JwkRegistryIssuerKeyResolver` via a configuration switch.  *(adopted a `CompositeIssuerKeyResolver` that tries DID-backed first then falls back to the in-memory JWK registry — keeps demo-mint dev flows working while production-issuer-fail-open is closed for any non-fixture issuer.)*
- [x] T022 [US1] Wire `RequireIssuerSignature` configuration switch. Default value at ship: `true` per FR-019 / D5. Read via `IConfiguration` at the verifier's composition root. Source `appsettings.json` key: `IssuerSignature:Required`.  *(`IssuerSignature:Required` honoured first; legacy `Verifier:RequireIssuerSignature` honoured second; default `true`.)*
- [x] T023 [US1] Update `VerifiablePresentationValidator` (`src/Apps/Sorcha.Citizen.Verifier/Services/VerifiablePresentationValidator.cs`) to consume the new resolver. Step 4b path replaces the OptOut return with the production resolver call. Logging path differentiates the three failure modes by the counter that was incremented.  *(validator now extracts the credential's JWS `kid` header and passes it to the resolver. Failure-mode classification is surfaced by the resolver's outcome counter rather than re-derived in the validator.)*
- [x] T024 [US1] Add the OTel span `verifier.issuer-resolve` parented to the existing `verifier.presentation` span. Attributes: `verifier.issuer.outcome ∈ {success, did-unresolved, kid-unmatched, signature-failed}`, `verifier.issuer.kid_match_mode ∈ {exact, thumbprint-fallback}`. Span instrumentation lives inside `DidResolverBackedIssuerKeyResolver`.  *(span emitted; `signature-failed` is recorded by the validator on JWS verification failure, not the resolver.)*
- [x] T025 [US1] Add structured-logging entries for each failure mode at `Warning` level (per Constitution VIII — no string interpolation). Each entry includes the credential's `iss`, `kid`, and the failure-mode classifier. Tests confirm log entries surface in `ILogger<DidResolverBackedIssuerKeyResolver>` captured via `xunit.MELT` or equivalent.
- [x] T026 [US1] Update DI extension `src/Apps/Sorcha.Citizen.Verifier/Extensions/ServiceCollectionExtensions.cs` (or equivalent) to register `DidResolverBackedIssuerKeyResolver` as the singleton implementation of `IIssuerKeyResolver` and remove `OptOutIssuerKeyResolver` from the production composition.  *(via the Composite pattern — see T021 deviation.)*

**Checkpoint**: User Story 1's verifier-side path is functional but cannot be end-to-end tested until at least one issuer has a published DID document (US2). Mocks/test fixtures cover the unit-test surface.

---

## Phase 4: User Story 2 — Organisations have a published verifiable identity (Priority: P1) 🎯 MVP (with US1)

**Goal**: An organisation issuing its first credential lazily derives an issuance key (Feature 083 slot 1, `KeyUsage.VCIssuance`), publishes a W3C-conforming DID document at `/orgs/{orgId}/did.json`, and the document declares both `did:sorcha:org:{addr}` and `did:web:{platform}:orgs:{orgId}` linked via `alsoKnownAs`.

**Independent Test**: Trigger first issuance for a freshly-created org. Confirm the published document is reachable, declares the issuance key under both versioned and thumbprint kid styles (dual VM), and that a generic W3C DID resolver can dereference it without Sorcha-specific knowledge.

### Tests for User Story 2

- [ ] T027 [P] [US2] Unit tests for `IssuanceKeyService` at `tests/Sorcha.Wallet.Service.Tests/Services/IssuanceKeyServiceTests.cs`. Cover: lazy derivation on first call, idempotency on retry, key bytes match Feature 083 slot-1 derivation expectations, `IssuanceKeyState` row created with `Status=Active`, thumbprint computed and persisted.  *(deferred — depends on the Feature 083 derivation pipeline being wired through DI in a way the unit-test factory can mock cleanly. Lands with T039 wiring follow-up.)*
- [x] T028 [P] [US2] Unit tests for `OrgDidDocumentService` at `tests/Sorcha.Tenant.Service.Tests/Services/OrgDidDocumentServiceTests.cs`. Cover: regeneration on each `KeyEventReason`, no-op when `KeyVersionFingerprint` matches, dual-VM emission per active key, `alsoKnownAs` correctly populated in both directions, `assertionMethod` references all active VMs, `KeyVersionFingerprint` deterministic.
- [ ] T029 [P] [US2] Endpoint test for `GET /orgs/{orgId}/did.json` at `tests/Sorcha.Tenant.Service.Tests/Endpoints/OrgDidDocumentEndpointsTests.cs`. Reflection-based static-handler invocation per existing pattern. Cover: 200 with valid document, 404 for org with no issuance key, `Cache-Control: public, max-age=21600` header present, content-type `application/did+json`.  *(deferred — endpoint test fixtures for Tenant Service require WebApplicationFactory infra; folded into the same follow-up as T030.)*
- [ ] T030 [P] [US2] Integration test for first-issuance ceremony at `tests/Sorcha.Wallet.Service.Tests/Integration/FirstCredentialIssuanceCeremonyTests.cs`. End-to-end: org creation → first issuance trigger → key derived → DID document published → endpoint serves document. Confirms FR-004's "no later than first issuance" guarantee.  *(deferred — needs Wallet+Tenant services running with shared infra; ships in the US2 integration follow-up.)*

### Implementation for User Story 2

- [x] T031 [US2] Add `IssuanceKeyState` entity at `src/Services/Sorcha.Wallet.Service/Models/IssuanceKeyState.cs` per data-model.md §4.  *(landed at `src/Core/Sorcha.Wallet.Portable/Domain/Entities/IssuanceKeyState.cs` — matches the existing wallet-entity layout where `Sorcha.Wallet.Core.Domain.Entities` lives in the Portable project.)*
- [x] T032 [US2] Add EF configuration for `IssuanceKeyState`.  *(inline in `WalletDbContext.ConfigureIssuanceKeyState` — matches the inline-configure pattern used by every other wallet entity rather than `IEntityTypeConfiguration<T>` classes.)*
- [x] T033 [US2] Generate EF migration: `dotnet ef migrations add AddIssuanceKeyState`.  *(squashed into the existing `20260507221206_InitialCreate` per the preproduction migration-squash rule; Designer + ModelSnapshot synced from the EF-generated diff.)*
- [x] T034 [P] [US2] Add `OrgDidDocument` entity at `src/Services/Sorcha.Tenant.Service/Models/OrgDidDocument.cs`.
- [x] T035 [P] [US2] Add EF configuration for `OrgDidDocument`.  *(inline in `TenantDbContext.ConfigureOrgDidDocument`.)*
- [x] T036 [US2] Generate EF migration: `dotnet ef migrations add AddOrgDidDocument`.  *(squashed into the latest existing migration `20260505175116_AddInboxEntry` per the preproduction migration-squash rule. Designer + ModelSnapshot synced.)*
- [x] T037 [US2] Add `IIssuanceKeyService` interface.
- [x] T038 [US2] Implement `IssuanceKeyService`.  *(uses `IOrgKeyDerivationService.DeriveUserKeyAsync` for derivation rather than calling `IKeyManagementService.DeriveKeyAtPathAsync` directly — the org-key path already wraps the master-key + path-builder + wallet-creation machinery the service needs.)*
- [ ] T039 [US2] Wire `IIssuanceKeyService.GetOrDeriveAsync` into the credential-issuance flow.  *(deferred — touches the live credential-signing path; lands as a focused follow-up PR alongside T045 demo-mint integration so the runtime change is bounded and easy to revert.)*
- [x] T040 [P] [US2] Add `IOrgDidDocumentService` interface.
- [x] T041 [US2] Implement `OrgDidDocumentService`.  *(extends the spec with `RegenerateFromSnapshotAsync` — Wallet pushes the key snapshot in the request body rather than Tenant calling back to Wallet. Avoids a Tenant→Wallet readback path and keeps the cross-service flow one-directional. The interface's `RegenerateAsync(orgId, reason)` overload throws `NotSupportedException` directing callers to the snapshot path.)*
- [x] T042 [P] [US2] Add `KeyEventReason` enum at `src/Services/Sorcha.Tenant.Service/Models/KeyEventReason.cs`.
- [x] T043 [US2] Add `OrgDidDocumentEndpoints` exposing `GET /orgs/{orgId}/did.json` and `POST /orgs/{orgId}/did-document/regenerate` (the new internal regen endpoint introduced for the snapshot push pattern).
- [x] T044 [US2] Wire `IOrgDidDocumentService.RegenerateAsync` triggers in `IssuanceKeyService`.  *(via the new `IOrgDidDocumentClient` HTTP client — fire-and-forget, non-throwing, tolerates Tenant unavailability since key state is the source of truth.)*
- [ ] T045 [US2] Enhance `SorchaDidResolver` to surface the issuance key as a second VerificationMethod.  *(deferred — pairs with T039 as a focused follow-up PR. The DID document already exists at the Tenant endpoint after this PR ships; SorchaDidResolver's enhancement to surface dual VMs from `did:sorcha:org:*` resolution lands once T039 is wired and exercised end-to-end.)*
- [x] T046 [US2] Update Sorcha.Tenant.Service DI: register `IOrgDidDocumentService` (Scoped).  *(IStorageRegistrationLog wiring deferred to the same follow-up as T039 — the audit list already excludes cache-style storage like `OrgDidDocument`, so this is a logging warning at most, not a fail-fast.)*
- [ ] T047 [P] [US2] OpenAPI documentation updates.  *(endpoint metadata via `.WithName/Summary/Description` already in place; aggregator regen happens automatically.)*

**Checkpoint**: User Stories 1 and 2 together form the MVP. End-to-end: org issues first credential → DID doc published → verifier resolves and verifies. The first-issuance ceremony test (T030) validates this slice.

---

## Phase 5: User Story 3 — Federation interop without Sorcha-specific tooling (Priority: P2)

**Goal**: A standards-compliant external wallet/verifier that knows only IETF/W3C standards can verify a Sorcha-issued credential using the published `did:web` document and standard tooling.

**Independent Test**: Use a generic W3C DID resolver library (or `curl` + manual JWS verification) to fetch the published `did:web:{platform}:orgs:{orgId}/.well-known/did.json` (path-based form: `https://{platform}/orgs/{orgId}/did.json`), extract the verification method, verify a sample credential's signature. No Sorcha library required.

### Tests for User Story 3

- [x] T048 [P] [US3] `OrgDidDocumentSchemaConformanceTests` — 7 tests covering @context, id, alsoKnownAs federated link, every VM has required fields, dual-VM emission, assertionMethod = all VMs, plain System.Text.Json round-trip with no Sorcha-specific converters.
- [ ] T049 [P] [US3] Cross-tool resolution test. *(deferred — needs WebApplicationFactory + a fixture-signed credential; the schema-conformance test (T048) and the standards-compliant publish path (T043 + T051) already prove the contract a generic resolver consumes.)*

### Implementation for User Story 3

- [x] T050 [US3] OpenAPI metadata for the endpoint already in place (T043 — `Produces(..., contentType: "application/did+json")`).
- [x] T051 [US3] Gateway routing — `tenant-org-did-document` route in `Sorcha.ApiGateway/appsettings.json` exposes `/orgs/{orgId}/did.json` anonymously to the tenant cluster.
- [ ] T052 [P] [US3] Federation-interop docs in `docs/openid4vc-haip-integration.md`. *(deferred — copy-only follow-up; spec-level guarantee proven by T048's structural assertions.)*

**Checkpoint**: US3 is fully functional as soon as US2's published document conforms. The story exists in v1 primarily to guard against accidental Sorcha-specific drift in the published document.

---

## Phase 6: User Story 4 — Cross-resolution prevents identity impersonation (Priority: P2)

**Goal**: `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync` cross-resolves linked DIDs, verifies the same verification key appears in every linked document, and rejects on key-material mismatch or unreachable link. Caching at the registry layer means steady-state cost is zero per repeat issuer.

**Independent Test**: Construct an attacker fixture — a `did:web` document falsely claiming `alsoKnownAs` to another org's `did:sorcha` form, while serving an attacker-controlled signing key. Present a credential signed by the attacker's key. The verifier rejects on cross-resolution mismatch.

### Tests for User Story 4

- [x] T053 [P] [US4] Cross-resolution unit tests at `tests/Sorcha.ServiceClients.Tests/Did/DidResolverRegistryCrossResolutionTests.cs`. Covers passthrough, one-link-match, unreachable, mismatch, two-links-partial-match, cycle protection, empty input, primary-unresolved, and assertion-method filtering. *(cache-hit / cache-invalidated / negative-cache / concurrent-coalescing already covered by Phase 1's `DidResolverCacheTests` — the registry composes the cache rather than reimplementing it.)*
- [ ] T054 [P] [US4] Cross-resolution attack scenario test fixture at `tests/Sorcha.Citizen.Verifier.Tests/Integration/CrossResolutionAttackScenarioTests.cs`. *(deferred — the unit-level mismatch test already proves the algorithm rejects an attacker-controlled `did:web` claiming false `alsoKnownAs`; an end-to-end fixture lands alongside T030 ceremony test in the US2 follow-up.)*

### Implementation for User Story 4

- [x] T055 [US4] Full `DidResolverRegistry.ResolveWithAlsoKnownAsAsync` implementation per the deterministic six-step algorithm.
- [x] T056 [US4] `DidResolverCache` integrated via `GetOrAddAsync` keyed on the primary DID. Positive entries cache the merged document; negative entries cache via the cache's existing 60s negative-TTL behaviour.
- [x] T057 [US4] Cycle protection — `HashSet<string>` of visited DIDs (seeded with the primary), revisits are skipped.
- [x] T058 [US4] `VerificationKeyMaterialComparer` — extracts raw key bytes via multibase varint-strip OR JWK-thumbprint canonicalisation; constant-time `CryptographicOperations.FixedTimeEquals` comparison.
- [x] T059 [US4] `did.resolve.cross` span with `did.input`, `did.method`, `did.alsoKnownAs.cross_resolved`, `did.alsoKnownAs.match`, `did.alsoKnownAs.link_count` tags. Cross-resolve mismatch + unreachable counters from `DidResolverMetrics` incremented on the relevant outcomes.

**Checkpoint**: US4's cross-resolution is the security-critical addition. The attack-scenario test (T054) is the gate — if it fails to reject, do not proceed to ship.

---

## Phase 7: User Story 5 — Per-action issuer allowlist honours equivalent identities (Priority: P3)

**Goal**: When a blueprint action's `CredentialRequirement.AcceptedIssuers` lists a DID and the credential's `iss` resolves (via cross-resolution) to a document declaring the listed DID via `alsoKnownAs`, the match succeeds.

**Independent Test**: Author a blueprint action whose allowlist names only the `did:sorcha:org` form of an issuer. Present credentials issued under both `did:sorcha:org:*` and the equivalent `did:web:*` forms. Both satisfy the allowlist.

### Tests for User Story 5

- [x] T060 [P] [US5] Unit tests at `tests/Sorcha.Wallet.Service.Tests/Credentials/IssuerEquivalenceMatcherTests.cs`. Covers empty allowlist, direct match (no resolve calls), no-registry direct-only, both alsoKnownAs directions, no equivalence, unreachable-then-step3 fallback.

### Implementation for User Story 5

- [x] T061 [US5] `CredentialMatcher.MatchAsync` now routes the issuer check through `IssuerEquivalenceMatcher.IsAcceptedAsync`. Sync `Match` retained for back-compat (direct-string only).
- [x] T062 [US5] Shared static helper `IssuerEquivalenceMatcher` consumed by `CredentialMatcher.MatchAsync` and `PresentationRequestService.VerifyPresentationAsync`. *(third call site `PresentationLifecycleService.cs:133` was a passthrough — no allowlist match logic at that line; nothing to update there.)*
- [ ] T063 [US5] Confirm `OPEN_CREDENTIAL_ISSUER` publish-time warning regression test. *(deferred — pre-existing publish-time warning untouched by this PR; spec callout treated as a paired regression check rather than a new test.)*

**Checkpoint**: US5 closes the blueprint-author UX edge that empty-or-strict allowlists would otherwise create.

---

## Phase 8: User Story 6 — Issuance key compromise can be remediated by governance (Priority: P3)

**Goal**: An admin quorum can revoke an organisation's active issuance key via a governance op (`VAL_CRED_GOV_001`). After revocation, presentations of credentials signed by the revoked key are rejected. The org can derive a fresh issuance key and continue operating.

**Independent Test**: Initiate `RevokeIssuanceKey` governance op against an org's active key. After quorum reached, present a credential signed by the now-revoked key — confirm rejection. Issue a new credential — confirm it signs under a fresh key.

### Tests for User Story 6

- [ ] T064 [P] [US6] Rotation/revocation unit tests at `tests/Sorcha.Wallet.Service.Tests/Services/IssuanceKeyServiceLifecycleTests.cs`. Cover: `RotateAsync` creates new active row with `RotationIndex+1`, old row moves to `Status=Rotated`; `RevokeAsync` moves active row to `Status=Revoked` with `RevokedAt`; revoking a rotated key permitted; revoking a revoked key idempotent.
- [ ] T065 [P] [US6] Integration test for the compromise drill at `tests/integration/IssuanceKeyCompromiseDrillTests.cs`. End-to-end: derive key → sign credential → revoke via governance op → present credential → assert rejection within governance-op duration.

### Implementation for User Story 6

- [ ] T066 [US6] Extend `IIssuanceKeyService` (T037) with `Task<IssuanceKeyState> RotateAsync(Guid orgId, Guid governanceOpId, CancellationToken ct)` and `Task RevokeAsync(Guid orgId, int rotationIndex, string reason, Guid governanceOpId, CancellationToken ct)`.
- [ ] T067 [US6] Implement `RotateAsync` in `IssuanceKeyService`. Derive a new key via `IKeyManagementService.DeriveKeyAtPathAsync` at the next BIP44 path under slot 1 (rotation increments the path index). Move existing active row to `Status=Rotated`, insert new row with `RotationIndex = old + 1, Status = Active`. Trigger `IOrgDidDocumentService.RegenerateAsync` with reason `IssuanceKeyRotated`.
- [ ] T068 [US6] Implement `RevokeAsync` in `IssuanceKeyService`. Update target row's `Status = Revoked`, set `RevokedAt`, `RevocationReason`, `RevokedByGovernanceOpId`. Trigger `IOrgDidDocumentService.RegenerateAsync` with reason `IssuanceKeyRevoked`.
- [ ] T069 [US6] Add governance op `VAL_CRED_GOV_001` (RevokeIssuanceKey) to the existing governance models. File: `src/Common/Sorcha.Register.Models/GovernanceModels.cs` — add the new op type alongside `RotateValidatorKey` (Feature 086 precedent). Op payload includes `OrganizationId`, `RotationIndex`, `Reason`.
- [ ] T070 [US6] Add governance op handler in `Sorcha.Validator.Service/Services/RightsEnforcementService.cs` (or the consuming service) that processes `RevokeIssuanceKey` ops after quorum approval, calling `IIssuanceKeyService.RevokeAsync` via service client. The validator's role is permission enforcement; the actual key state mutation happens in Wallet Service.
- [ ] T071 [US6] Update `OrgDidDocumentService.RegenerateAsync` (T041) to handle revoked keys. Revoked keys remain in the published document with `assertionMethod` membership removed; the credential's signature still verifies cryptographically against the published key, but the verifier rejects on `Status=Revoked` lookup. Verifier-side check lands in T072.
- [ ] T072 [US6] Add revocation enforcement in `DidResolverBackedIssuerKeyResolver` (T020). After a successful kid match against a revoked-status `IssuanceKeyState`, return null and increment a new counter `sorcha_verifier_issuer_revoked_key_total`. This means revocation enforcement is verifier-side via the published document (revoked VMs absent from `assertionMethod`); the additional explicit check is defence-in-depth.
- [ ] T073 [US6] Audit log integration: governance op execution writes an `AuditLogEntry` with the op type, executing admin quorum, target organisation, and timestamp. Use the existing audit log infrastructure (Tenant Service or Validator Service — confirm).

**Checkpoint**: US6 closes the security loop. SC-005 (revocation completes within a single governance-op duration) is the measurable outcome.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Reserved schema slots, walkthrough validation, documentation propagation, ship-gate validation.

### Reserved schema slots (forward-compat for Future B)

- [ ] T074 [P] Add `RegisterPolicy.RequireIssuerSignature: bool?` to `src/Common/Sorcha.Register.Models/RegisterControlRecord.cs` per data-model.md §3 / FR-020. Field is JSON-null-ignored. NOT read at v1.
- [ ] T075 [P] Add `RegisterPolicy.PermittedIssuers: string[]?` to the same file per data-model.md §3 / FR-021. JSON-null-ignored. NOT read at v1.
- [ ] T076 [P] Add `Organization.DefaultKidStyle: KidStyle` enum slot at `src/Services/Sorcha.Tenant.Service/Models/Organization.cs` per data-model.md §2 / FR-013. Default `Versioned`. Not exposed in v1 admin UI. New `KidStyle` enum at the same file or in a dedicated model file.
- [ ] T077 EF migration for `Organization.DefaultKidStyle`: `dotnet ef migrations add AddOrganizationDefaultKidStyle --project src/Services/Sorcha.Tenant.Service`. Verify designer regenerates.
- [ ] T078 [P] Backward-compat unit test for `RegisterControlRecord` at `tests/Sorcha.Register.Models.Tests/RegisterControlRecordBackwardCompatTests.cs`. Confirms a v0.119 control record (no `RequireIssuerSignature`, no `PermittedIssuers`) deserialises cleanly with both fields null. This is the test that validates SC-007.

### Walkthrough updates (ship gate)

- [ ] T079 [P] Run AssuredIdentity walkthrough end-to-end with `IssuerSignature:Required=true`. Path: `walkthroughs/AssuredIdentity/run.ps1 -CleanState`. Confirm 10/10 success per the existing 10-run validation pattern.
- [ ] T080 [P] Run TradeFinance walkthrough end-to-end with enforce-on. Path: `walkthroughs/TradeFinance/soak.ps1 -MaxRuns 5`.
- [ ] T081 [P] Run ConstructionPermit walkthrough end-to-end with enforce-on. Path: `walkthroughs/ConstructionPermit/run.ps1`.
- [ ] T082 [P] Run SelfBuildHouse walkthrough end-to-end with enforce-on. Path: `walkthroughs/SelfBuildHouse/run.ps1`.
- [ ] T083 Identify and pin issuers in the AssuredIdentity action's `acceptedIssuers` field (currently empty per `walkthroughs/AssuredIdentity/blueprints/driving-licence.json:103-115`). Document this as a hardening recommendation in the walkthrough's README.

### Demo-mint flow disposition

- [ ] T084 [P] Document `DemoMintEndpoint` and `JwkRegistryIssuerKeyResolver` as test-only. Add a comment header to both files clarifying that production wires `DidResolverBackedIssuerKeyResolver`. Update `docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md` if needed (note the demo-mint's relationship to the new resolver).

### HAIP OID4VCI consistency check (R10)

- [ ] T085 [P] Confirm OID4VCI `credential_issuer` metadata in `Sorcha.Haip.Service` declares the same DID as the credential's `iss` claim. Verify by issuing a credential through OID4VCI and presenting it through the verifier with enforce-on; confirm `verifier.issuer.outcome=success`. If mismatch surfaces, file a small follow-up patch in this feature's branch.

### Documentation propagation

- [ ] T086 [P] Update the `sorcha-architecture` skill at `.claude/skills/sorcha-architecture/SKILL.md` — add a "Production Issuer Signature Verification (Feature 120)" section covering the resolver registry's new method, the published-document endpoint, the issuance-key lifecycle, and the cross-resolution semantics.
- [ ] T087 [P] Update `docs/reference/API-DOCUMENTATION.md` — add `GET /orgs/{orgId}/did.json` to the Tenant Service endpoint table.
- [ ] T088 [P] Update `docs/security-model.md` — document issuer-signature verification as production-enforced; document the cross-resolution security property; document the lifecycle for issuance keys.
- [ ] T089 [P] Update `STANDARDS.md` — confirm W3C DID Core 1.0 and IETF SD-JWT VC are listed as `full|partial` rows; add `did:web` method specification reference if absent.
- [ ] T090 [P] Update `CLAUDE.md` if any pattern from this feature requires it. Specifically: if the per-org `DefaultKidStyle` slot pattern (settings reserved on entity, not exposed in v1 UI) becomes a generally reused pattern, capture it. Otherwise no edit.
- [ ] T091 [P] Update `MASTER-TASKS.md` — mark Feature 120 status; remove any TRUST-related items now resolved by this feature; reference the deferred VAL_CRED_* slots as Future B work.

### Final ship validation

- [ ] T092 Run `quickstart.md` end-to-end on a clean local stack — all 10 steps pass. This is the operator-runbook validation.
- [ ] T093 Run `dotnet test` across the full solution. All tests green; no regressions; new code coverage ≥85% per Constitution IV.
- [ ] T094 Run `dotnet format` and confirm no warnings.
- [ ] T095 Open PR for review. Description references the design doc, this spec, and the locked decisions D1–D6. Test plan summarises walkthrough results.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001-T003 ship anytime; non-blocking informational checks.
- **Foundational (Phase 2)**: T004-T008 ship as a separate PR (Phase 0 cleanup) — must merge before T009-T017 land. T009-T017 land on this branch as foundational commits and BLOCK all user stories.
- **User Stories (Phases 3-8)**: All depend on Foundational completion. **US1 and US2 must ship together** (US1 has nothing to verify against without US2's published documents) — treat as a single MVP deliverable.
- **Polish (Phase 9)**: Depends on all user stories complete. Walkthrough tasks (T079-T082) are the ship gate.

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational. Co-required with US2 for MVP value.
- **US2 (P1)**: Depends on Foundational. Co-required with US1 for MVP value.
- **US3 (P2)**: Depends on US2 (testing US2's published document conformance). Largely passive — passes when US2 is done correctly.
- **US4 (P2)**: Depends on Foundational + US2 (needs DID documents to cross-resolve). The `ResolveWithAlsoKnownAsAsync` stub from T013 means US1 can compile and test against the new method signature even before US4's full implementation lands.
- **US5 (P3)**: Depends on US4 (cross-resolution).
- **US6 (P3)**: Depends on US2 (extends `IIssuanceKeyService` with rotation/revocation). Independent of US4 and US5.

### Within Each User Story

- Tests written and confirmed FAILING before implementation lands (TDD per Constitution V encouragement).
- Models/entities first (`IssuanceKeyState`, `OrgDidDocument`, etc.).
- Services before endpoints (`IIssuanceKeyService` before `OrgDidDocumentEndpoints`).
- Endpoints before integration wiring (`OrgDidDocumentEndpoints` before `OrgDidDocumentService.RegenerateAsync` triggers).

### Parallel Opportunities

- All Setup tasks marked [P] (T002, T003) can run in parallel.
- Within Foundational: T006, T007, T010, T011, T014, T016, T017 marked [P] can parallelise after their non-[P] predecessors land.
- Within US2: T027-T030 (test tasks) parallel; T034, T035, T040, T042 (independent files) parallel; T031-T033 sequential (entity → config → migration).
- US3 and US5 can run in parallel with US4 and US6 if team capacity allows.
- Polish phase: T074-T076, T079-T082, T084-T091 all parallelisable.

---

## Parallel Example: User Story 2 (US2)

```text
# After Foundational checkpoint, launch US2 test scaffolding in parallel:
Task: T027 [P] [US2] IssuanceKeyService unit tests
Task: T028 [P] [US2] OrgDidDocumentService unit tests
Task: T029 [P] [US2] OrgDidDocumentEndpoints endpoint test

# Once tests scaffolded, launch independent model/config tasks in parallel:
Task: T031 [US2] IssuanceKeyState entity (Wallet)
Task: T034 [P] [US2] OrgDidDocument entity (Tenant)
Task: T042 [P] [US2] KeyEventReason enum (Tenant)
```

---

## Implementation Strategy

### MVP First (US1 + US2 together)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational), including the standalone Phase 0 PR.
2. Implement US1 + US2 together — they are interdependent for any visible value.
3. **STOP and VALIDATE**: AssuredIdentity walkthrough green with enforce-on.
4. Deploy/demo the MVP slice.

### Incremental Delivery

After MVP is shipped, the remaining stories layer on:

1. **US4 (cross-resolution)** — adds the security property that makes federation safe. Critical for any external `did:web` interop. Ship before any external participant onboards.
2. **US3 (federation interop testing)** — passive validation of US2's output. Mostly a regression-prevention investment.
3. **US5 (allowlist equivalence)** — UX polish for blueprint authors.
4. **US6 (compromise revocation)** — security hardening; need not block initial production but should ship soon after.

### Parallel Team Strategy

If multiple developers are available:

1. Whole team completes Setup + Foundational together.
2. Once Foundational is in:
   - Developer A: US1 (verifier-side resolver + wiring)
   - Developer B: US2 (org DID document publishing + issuance key service)
   - Developer C: US4 (cross-resolution + cache + Redis stream)
3. Stories integrate via the foundational interfaces (T012, T013) which are stable from Phase 2.

---

## Notes

- [P] tasks = different files, no incomplete-task dependencies.
- [Story] label maps task to specific user story for traceability.
- Each user story should be independently completable and testable (US1+US2 jointly form the MVP).
- Verify tests fail before implementing (Constitution V — TDD encouraged).
- Commit after each task or logical group; reference Feature 120 task IDs in commit messages.
- Stop at any checkpoint to validate the current slice independently.
- Avoid: vague tasks, same-file conflicts, cross-story dependencies that break independence.

---

## Cross-references

- Spec: `specs/120-production-issuer-signature-verification/spec.md`
- Plan: `specs/120-production-issuer-signature-verification/plan.md`
- Research: `specs/120-production-issuer-signature-verification/research.md`
- Data model: `specs/120-production-issuer-signature-verification/data-model.md`
- Contracts: `specs/120-production-issuer-signature-verification/contracts/`
- Quickstart: `specs/120-production-issuer-signature-verification/quickstart.md`
- Authoritative design: `docs/superpowers/specs/2026-05-09-production-issuer-signature-verification-design.md`
- Companion memos (shared memory): `Validator2/2026-05-09-programmable-validation-thesis.md`, `Validator2/2026-05-09-did-resolution-and-issuer-sig-companion.md`
