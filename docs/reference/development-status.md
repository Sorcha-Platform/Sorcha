# Sorcha Platform - Development Status Report

**Overall Completion:** 100% MVD

> **Currency note.** This snapshot was last comprehensively updated for ~Feature 109/145. Work has
> continued since — notably tiered JWT audiences + issuer hardening (**Feature 136**), the
> June 2026 security-hardening initiative (**Features 146/147/148** — Tenant at-rest secret
> protection, authorization-gap closure, verification correctness), and the 2026-06-02 architecture
> review. The current per-service completion figures live in the [CLAUDE.md](../../CLAUDE.md) service
> table. The previous per-service `reference/status/*` "% MVD" pages have been **retired** (they were
> perpetually-stale point-in-time snapshots) — this document plus the service READMEs are the live
> status source.

---

## Executive Summary

This document provides an accurate, evidence-based assessment of the Sorcha platform's development status.

**Key Findings:**
- Blueprint-Action Service is 100% complete with full orchestration and JWT authentication (123 tests)
- Wallet Service is 98% complete with full API implementation, JWT authentication, EF Core persistence, and org key derivation
- Register Service is 100% complete with comprehensive testing, JWT authentication, and decentralized governance (234 tests)
- **Peer Service 95%**: P2P topology, JWT auth, EF Core, 7 gRPC RPCs, register replication, live subscriptions, circuit breaking, PostgreSQL queue
- **Validator Service 100% MVD**: Memory pool, docket building, consensus, gRPC, duplicate detection cross-check (620+ tests)
- **Tenant Service 97%**: Auth, orgs, OIDC integration, identity management, role consolidation, admin console, platform org topology
- **AUTH-002 complete**: All services now have JWT Bearer authentication with authorization policies
- **Quantum-Safe Cryptography 100% complete**: ML-DSA-65, ML-KEM-768, SLH-DSA-128s, BLS12-381 threshold signatures, ZK proofs (Pedersen commitments, range proofs), per-register crypto policy, ws2 Bech32m addresses (270+ new tests)
- **System Admin Tooling (Feature 049)**: Service principal CRUD, register policy management, validator consent/metrics/threshold, system register page, 17 CLI subcommands (29 new components)
- **UI Modernization 100% complete**: Comprehensive overhaul — admin panels, workflow management, cloud persistence, dashboard stats, wallet/transaction integration, template library, explorer enhancements, consistent ID truncation
- **UI Register Management 100% complete**: Wallet selection wizard, search/filter, transaction query
- **Verifiable Credentials 100% complete**: SD-JWT VC format, credential gating on actions, blueprint-as-issuer, cross-blueprint composability, selective disclosure, revocation (53 engine + 6 crypto + 4 endpoint tests)
- **CLI Register Commands 100% complete**: Two-phase creation, dockets, queries with System.CommandLine 2.0.2
- **Peer Router & Peer Service Completion (Feature 053) 100% complete**: PeerRouter standalone app, circuit breaking, SQLite removed, PostgreSQL queue migration, Phases 1-8 delivered *(the PeerRouter standalone app was later **retired in Feature 143** — capability folded into Sorcha.Peer.Service)*
- **Organization Admin & Identity Management (Feature 054) complete**: OIDC discovery-first integration, multi-tenant URL resolution, 5-role model, 7 Blazor admin pages, social login, TOTP 2FA, invitation system, progressive lockout
- **Encryption Integration (Feature 052) 100% complete**: End-to-end async encryption pipeline wired to UI and CLI — CryptoProgressPopover with per-recipient progress (replacing EncryptionProgressIndicator), SignalR push + polling fallback, retry on failure, cross-page toast notifications via EventsHub, operations history page with pagination, CLI `action execute` with blocking spinner and `--no-wait` mode (63+ new tests)
- **FLE Completion & Crypto Progress UX (Feature 075) complete**: Per-recipient SignalR events from encryption pipeline, floating popover UI (expanded/minimised/dismissed), DevMode per-register unit tests, FLE disclosure group unit tests, actionable error feedback with retry (35+ new tests)
- **Verified Citizen v2 (Feature 103) complete**: Open starting actions with Redis-backed `InstanceBindingCache` + publish-time guardrail (`VAL_BP_010`), Sorcha core identity primitive library (`PersonName/v1`, `DateOfBirth/v1`, `EmailAddress/v1`, `PostalAddress/v1`) with `SchemaRefResolver` flattening at publish time, address lookup library with Postcodes.io (validate-only) + OS Places (full-address) providers behind `IAddressLookupProvider`, `PostcodeLookupRenderer` with three runtime modes (NoProvider/ValidateOnly/FullAddress) dispatched on `x-address-lookup: true`, JSON Pointer claim-mapping walker that resolves nested primitive payloads correctly on both internal-issuance and HAIP external-wallet paths. Shipped across 7 PRs (#268-#274). Subsumed + superseded by Feature 107 — the former `HaipVerifiedCitizen` + `HaipDrivingLicence` verification walkthroughs were retired; see `walkthroughs/AssuredIdentity/` for the canonical replacement.
- **AI Designer Unified Shell (Feature 109) complete**: Collapses the former split designer pages (`/designer/chat` and `/designer`) into a single shell at `/designer/blueprint/{BlueprintId?}?tab={ai|diagram|preview}` with three tabs sharing live state via a scoped `DesignerContext` (Blueprint, Validation, ChatSessionId, ActiveActionId, IsManualCursor, IsDirty). AI tab is full-width chat with input pinned at the viewport bottom via CSS Grid `1fr auto` (closes GAP-011b regression — driven in E2E via `page.evaluate` → `[JSInvokable]` synthetic SignalR injection, no real Anthropic round-trip). Diagram tab hosts the existing Blazor Diagrams canvas reading/writing the shared context. Preview tab is new — renders one action at a time through `SorchaFormRenderer` in a new additive `PreviewMode` parameter, with pager (Prev/Next/jump/`[`/`]`) and Follow AI toggle for manual override. Shared toolbar owns Save / Load / Export / validation popover across all tabs. Legacy routes (`/designer/chat`, `/designer/chat/{id}`, `/designer`) become client-side redirect shims for one release cycle. `MudTabs KeepPanelsAlive="true"` preserves SignalR and canvas state across tab switches. 40 new unit tests (`TabRouteParser`, `PreviewPagerLogic`, `AutoScrollController`, `DesignerContext`); 10 new E2E tests (`DesignerShellTests`). No API, schema, or validator changes — UI-only refactor in `Sorcha.UI.Web.Client` + helpers in `Sorcha.UI.Core.Services.Designer`. Shipped as PR #369.
- **Assured Identity v1 (Feature 107) complete**: single canonical `AssuredIdentityCredential` replaces the prior split citizen-identity credentials. 5-page GDS wizard (name + DOB, address, contact, optional portrait, id-card review), new `x-review` schema extension with parameterised id-card layout (identity-navy / licence-pink themes), `x-file.capture` + `x-file.embedAs` drive mobile camera capture + client-side 240×320 JPEG token resize, server-side portrait-size gate in `BuildClaimsFromMappings` (`WARN_CRED_PORTRAIT_OVERSIZE_001`). Driving Licence credential-chain Phase 2 proves full HAIP OID4VP → OID4VCI round-trip with holder identity carry-forward. Unattended reviewer agents (`verification-analyst`, `licensing-officer`) in rules mode. Cross-peer smoke harness (`docker-compose.federation.yml` + `run-multi-peer.ps1` + findings format) — measurement, non-blocking per FR-039. Legacy `HaipVerifiedCitizen` + `HaipDrivingLicence` walkthroughs retired. Shipped across 5 PRs (#335, #343, #348, #350, PR 5).
- **Transactional Email Sweep (Feature 112) complete**: unified templated pipeline for every Tenant Service transactional email — `ITransactionalEmailService` facade, `ScribanEmailTemplateRenderer` (embedded-resource templates, pre-parsed at startup), `EmailBrandingResolver` (per-field fallback to Sorcha defaults; org name always wins), `WelcomeEmailDispatcher` (one-shot per user via `PlatformUser.WelcomeSentAt`; non-throwing; public vs invited variant by earliest-joined standard-org membership). Six template pairs (`base`/`verify`/`invite`/`reset`/`welcome-public`/`welcome-invited`) with committed snapshot fixtures. Fixes two latent plaintext-token bugs in verify + invite; adds welcome emails on verify success + first login + social callback paths. SMTP (MailKit) and ACS backends unchanged other than multipart HTML + plaintext. Per-org branding on invitations. 12 new Tenant Service tests (renderer, branding resolver, facade, dispatcher, verification service, login service) + 6 snapshot-fixture cases. Closes MASTER-TASKS AUTH-006. See `specs/112-email-sweep/` and the Tenant Service README.
- Total actual completion: 100% MVD (all feature gaps closed, auth policies on all API routes)

---

## Detailed Status by Service

The former per-service `docs/reference/status/*` snapshots have been removed — they were perpetually-stale point-in-time "% MVD" pages (see the currency note above). This document plus the CLAUDE.md service table are the current source. Current status is summarised inline below.

| Service | Status | Details |
|---------|--------|---------|
| Blueprint-Action Service | 100% | Full orchestration, SignalR, JWT auth |
| Wallet Service | 98% | EF Core, API complete, HD wallets, Azure Key Vault KMS (Feature 082), Org Key Derivation (Feature 083) |
| Register Service | 100% | 20 REST endpoints, OData, SignalR |
| Peer Service | 95% | P2P, 7 gRPC RPCs, replication, circuit breaking, PostgreSQL queue |
| **Sorcha.PeerRouter** | Retired (F143) | Standalone app removed — capability folded into Sorcha.Peer.Service (reverse-stream rendezvous) |
| Validator Service | 100% MVD | Consensus, mempool, dedup cross-check |
| Tenant Service | 97% | Auth, orgs, OIDC, identity mgmt, admin console, platform org topology |
| Authentication (AUTH-002) | 100% | JWT Bearer for all services |
| Core Libraries & Infrastructure | 95% | Engine, Crypto, Gateway |
| **Sorcha.UI (Unified)** | 100% | Register management, designer, consumer pages |
| Issues & Actions | - | Resolved issues, next steps |

---

## Completion Metrics

### By Component

| Component | Completion | Status | Blocker |
|-----------|-----------|--------|---------|
| **Blueprint.Engine** | 100% | Complete | None |
| **Blueprint.Service** | 100% | Complete | None |
| **Wallet.Service** | 98% | Nearly Complete | AWS/GCP KMS (deferred), threshold signing (deferred) |
| **Register.Service** | 100% | Complete | None |
| **Peer.Service** | 95% | Complete | None (deferred: BLS threshold) |
| **Sorcha.PeerRouter** | Retired (F143) | Removed | Folded into Sorcha.Peer.Service |
| **Validator.Service** | 100% MVD | Complete | None (deferred: enclave, fork detection) |
| **Tenant.Service** | 97% | Complete | None (deferred: Phase 10 polish) |
| **Authentication (AUTH-002)** | 100% | Complete | None |
| **Sorcha.Cryptography (PQC)** | 100% | Complete | None |
| **ApiGateway** | 100% MVD | Complete | None |
| **Sorcha.UI (Unified)** | 100% | Complete | None |
| **Sorcha.CLI** | 100% | Complete | None |
| **CI/CD** | 95% | Complete | Prod validation |

### By Phase (MASTER-PLAN.md)

| Phase | Completion | Status |
|-------|-----------|--------|
| **Phase 1: Blueprint-Action Service** | 100% | Complete (Sprint 10 Orchestration) |
| **Phase 2: Wallet Service** | 95% | Nearly Complete |
| **Phase 3: Register Service** | 100% | Complete |
| **Authentication Integration (AUTH-002)** | 100% | Complete |
| **Overall Platform** | **100% MVD** | **Feature Complete** |

### Test Coverage

| Component | Unit Tests | Integration Tests | Coverage |
|-----------|-----------|------------------|----------|
| Blueprint.Engine | 102 tests | Extensive | >90% |
| Blueprint.Service | 123 tests | Comprehensive | >90% |
| Wallet.Service | 60+ tests | 20+ tests | >85% |
| Register.Service | 112 tests | Comprehensive | >85% |
| Validator.Service | 16 test files | Comprehensive | ~80% |
| Tenant.Service | N/A | 479+ tests | ~85% |

---

## Recent Completions

### 2026-08-11
- **189-Org-Signed-Governance — US1, US2 and US3 complete** (PRs #1377, #1383, #1386–#1391). A register's governance changes are now proposed, approved and enacted as **ledger transactions signed by the participating organisations**, not by the node. The three-transaction model — proposal (carries no roster, so it changes nothing) → sibling approvals chained off it → a separate enactment naming `enactsProposalId` — is raised by nothing but the `docket:confirmed` reaction, and every approval is re-counted from sealed content on **every** node rather than trusted from its point of entry. Approvals are produced **outside** the platform's automatic signing path (R-014), bind the operation's whole canonical serialisation rather than a hand-picked field list (so a property added later is covered the moment it exists), and each must carry an accountability block naming a responsible individual, verified by one shared implementation in `Sorcha.Validator.Core` so the Register Service and the Validator cannot disagree. Proposal status is **derived** from the ledger, never stored.
  - **Live-proven on n1 2026-08-11.** A three-organisation register under `Unanimous` did not enact at 2 of 3, enacted and sealed at 3 of 3, and replicated to tiny **byte-identically** (5 dockets, same hashes and order; the same 6 transaction ids). **SC-010** — the feature's sharpest property — also holds live: with one approver outstanding, removing that organisation **invalidates** the proposal instead of enacting it, and the attack was genuinely available at that moment (2 approvals collected against a pool that had just fallen to 2, so unanimity equalled what was already in hand). A late approval is refused `409 roster-changed` rather than silently uncounted.
  - ⚠ **Known limitation, not solved:** an organisation's governance key is platform-custodied, so a sufficiently-privileged service principal can produce **any** organisation's approval. A sealed approval proves the node was asked to use the key, not that the organisation decided — worst under `Unanimous`, where one principal satisfies a whole consortium. Stated in [`security-model.md`](../security-model.md) → Honest Gaps; issue #1380.
  - **Remaining:** US4 (system-register ownership transfer — the feature's acceptance test, needing a coordinated re-genesis), the PWA signing surface (blocked on the same key-custody question), and the delegation *granting* path, which does not exist yet.

### 2026-07-28
- **Presentation gate: one control, two transports** (branch `feature/presentation-gate-transport`). Submitting the AIAS Cyber questionnaire on `/app` opened a QR dialog that polled HAIP's verifier for the full five-minute window and then reported "Expired" — because the gate was `presentationSource: SorchaWallet`, so its request lived in Blueprint's F127 lifecycle and HAIP had never heard of it. Root cause was a dropped mapping: `PresentationInitiationResult` already carried a `ClaimsFetchToken`, but the response DTO had no field for it and no source discriminator either, so `ActionExecutionService` discarded both and the client had nothing to branch on. Fixed by carrying `Source` + `ClaimsFetchToken` on `PresentationRequestResponse`/`PresentationRequestInfo` (renamed from `Haip*`, neither was ever HAIP-specific), guarded by `Sorcha.UI.ContractTests`. New `IPresentationGateTransport` seam with `SorchaWalletGateTransport` (over `IPresentationSignal`) and `HaipGateTransport` (over `IHaipOfferService`); one `PresentationRequestCard` in `Sorcha.UI.Components.User` replaces **both** `CredentialGateComponent` and the web app's HAIP-only `PresentationRequestQrCard`, which are deleted. `Sorcha.UI.Web.Client` now calls `AddSorchaPresentationGate` (previously only the sample portal did). `IPresentationSignal` gained `OnRequestUnreachable` — three consecutive 404s from `/status` are permanent and now say so instead of timing out as an expiry (the F127-side counterpart of #1325). Sample portal migrated. Design `docs/superpowers/specs/2026-07-28-presentation-gate-transport-design.md`, plan `docs/superpowers/plans/2026-07-28-presentation-gate-transport.md`.

### 2026-06-29
- **174-AIAS-Assured-Identity** (M1, branch `174-aias-assured-identity`, commit `ec63765b`) complete. Anonymous→assured journey end to end: sign up → apply (name, valid UK postcode, photo) → Assure-ID autonomous agent approves or rejects with on-brand reason → Assured Identity credential carrying the applicant's photo lands in the wallet via HAIP/OID4VCI. One genuine new code surface: **external-check hook** on `RulesDecisionEngine` (`src/Apps/Sorcha.Agent/Decision/Checks/`) with `IExternalCheck` contract, `ExternalCheckRunner` (concurrent, fault-contained), `ExternalCheckFactory`, `ChecksConfig`, `PayloadPointer` and four concrete checks — `EmailVerifiedCheck`, `FieldPresentCheck`, `PostcodeExistsCheck` (postcodes.io + bundled offline fixture), `ProfanityCheck` (local wordlist). Non-AIAS agents unaffected (`checksFile` required in actor config). Reboot-proof demo provisioning under `demos/AIAS/` (`AiasDemo.psm1` idempotent, `run-demo.ps1` Docker-first / `-Target n1`, `rehearse.ps1` approval + rejection e2e, blueprint template with AIAS branding + reject route, `assure-id.rules.json` + `assure-id.checks.json`, offline postcode fixture). 147/147 Sorcha.Agent.Tests pass. Spec-Kit artifacts in `specs/174-aias-assured-identity/`.

### 2026-06-26
- **163-Verify-Shared-Components** (PR B2-components) complete. Three shared Blazor components — `QuestionSelectionPanel`, `VerificationSessionQr`, `VerdictTrailPanel` — shipped in `Sorcha.UI.Components.User/Components/Verify/`. `VerdictViewModel` relocated to `Sorcha.UI.Components.User/Models/Verification/`; `IRegisterAnchorClient`/`RegisterAnchorClient`/`RegisterAnchorResult` relocated to `Sorcha.Verifier.Engine`. `NotConfiguredVerificationTransport` default stub registered via extended `AddSorchaUserComponents` (single call wires all three seams with `TryAdd*` override semantics). `Outcome.razor` in `Sorcha.Verifier` updated to synthesize `VerificationPreset` from session — desk verifier fully operationally intact (61 existing tests pass unchanged). Components activate under shared DI from a single `AddSorchaUserComponents` call; `VerificationSessionQr` implements `IAsyncDisposable` with deterministic poll-cancel proven in bUnit. 21 new tests across `SharedVerifyRegistrationTests`, `QuestionSelectionPanelTests`, `VerificationSessionQrTests`, `VerdictTrailPanelTests`. No host pages rewired (scope boundary B3). Prerequisite: PR #1045 (B2-foundation) merged to master and brought in via `origin/master` merge.

### 2026-05-31
- **145-Ledger-Derived-Workflow-Instances** — US1 cutover landed on branch `145-ledger-derived-cutover` (PR #891); **live-validated 2026-06-01 — local single-node AssuredIdentity PASS + cross-node tiny+n1 PASS (SC-001 identical projection on both nodes, no mirror; SC-002 cross-node credential delivery).** A workflow instance is now a **deterministic projection of the sealed register**: the single `InstanceProjector : BackgroundService` folds each sealed action transaction (carried `RoutingDecision` in the clear, never decrypting payload) on **every** node and is the sole instance writer + post-fold notifier. `ActionExecutionService.ExecuteAsync` is one **async** path — always `202` (`isAsync`, empty `nextActions`), no owner/subscriber branch, no imperative advance; a bounded seal-wait preserves chain ordering. `EncryptionBackgroundService` no longer advances. The origin/read-only-**mirror** model is removed entirely (`InstanceMirrorReconstructor`, `IsReadOnlyMirror`, `Create|UpdateMirrorAsync` — column squashed out of `InitialCreate`, clean break / `down -v`), with a 3-pattern CI clean-break gate (`ledger-derived-clean-break-gate.yml`). `instanceReference` is generated once pre-submit (the only point with plaintext for DevMode + encrypted). Presentation completion still advances imperatively by design (US6). Blueprint.Service.Tests 832 pass. Deferred follow-ups: T017 roster sealer (peer transport, F108 follow-up #1), T024 validator carries the decision through the seal (singular `NextActionId` stays a projector fallback), US2 ReactionDispatcher, US4 RebuildAsync/parity, US6 presentation-onto-projection. Canonical reference: `sorcha-architecture` skill → "Ledger-Derived Workflow Instances (Feature 145)".

### 2026-05-20
- **135-EUDI-Credential-Format-Trust** complete (PRs #806 US1, #807 US2, #809 US3 — squash-merged to master). (1) **Unified trust**: one `ITrustEvaluator` (`Sorcha.Blueprint.Engine.Credentials`, WASM-friendly) consulted by both the engine `CredentialVerifier` and HAIP's verifier — pluggable `register`/`x509-tenant`/`did-allowlist`/`trustlist` sources, `AnyOf`/`AllOf` combinators, ordered assurance levels, unified `IStatusListChecker` (W3C + IETF), pinnable `TrustEvidence` on spec-079 receipts, `Sorcha.Trust` OTel meter. The engine's hard-coded `SignatureValid=false` defer-to-service-layer defect is removed — signatures verify for real, fail-closed. (2) **`mso_mdoc` format** (ISO 18013-5, CBOR/COSE on the BCL — `Sorcha.Mdoc` since F185, no third-party lib): verify (US2) via `MdocService` + `MdocPresentationVerifier` over the existing OpenID4VP `direct_post`; issue (US3) via `MdocIssuer` + `MdocFormatHandler.IssueAsync` + the OpenID4VCI `/credential` dispatch with a selectable trust anchor. Online/OpenID4VP only; ES256/P-256-only (additive — Sorcha-native + PQC signing unchanged, SC-009). Clean break: `CredentialRequirement.AcceptedIssuers`→`TrustPolicy`, `HaipPresentationVerifier._trustedRoots`/`AddTrustedRoot` removed (CI gate `scripts/check-trust-clean-break.ps1`). Operator trust-list admin at Tenant `/api/v1/trust/trustlists`. Suites: Cryptography 396, Blueprint.Engine 509/+1 skip, Haip.Service 70 (+ cross-path parity SC-001, signature truthfulness SC-002, offline pinned re-eval SC-005). Deferred: HAIP trustlist-source consumption. (The external known-answer vector and MAC-based device auth were **closed by Feature 185** — see below.) Canonical reference: `sorcha-architecture` skill → "EUDI credential format & unified trust (Feature 135)".
- **185-Mobile-Proximity-Sharing US1 complete** (PR #1169). In-person, **offline** ISO 18013-5 credential presentation. `Sorcha.Mdoc` extracted pure-managed from `Sorcha.Cryptography` (which P/Invokes libsodium + MCL and cannot load in Blazor WASM, so the wallet could never reference the mdoc codec). Adds the **holder side of mdoc, which did not previously exist anywhere** (`MdocDeviceResponseBuilder`), plus `CoseSign1Builder` (COSE_Sign1 from a *raw* signature — the device key is a non-extractable WebCrypto key, so `SignDetached` cannot be used), **`CoseMac0`** (the BCL has no such type), `MdocSessionCrypto`, `ProximitySessionTranscript`, `DeviceEngagement`, session messages. `Sorcha.Proximity.Abstractions` carries `IProximityTransport` (opaque bytes) + holder/reader state machines + **`LoopbackProximityTransport`, which runs the ENTIRE exchange in CI with no Bluetooth and no phones**. Validated against **ISO Annex D reference data** — we decrypt the standard's own ciphertexts, re-encrypt byte-for-byte, and verify its own `deviceMac`. **Not yet shipped:** the BLE transport (so no over-the-air exchange has run), the wallet UI, and the reader app. **Not an interop claim** — the evidence bar is our own two devices. Canonical reference: `sorcha-architecture` skill → "Proximity credential sharing (Feature 185)".

### 2026-05-16
- **127-Credential-Gated-Second-Service** (Spec 4 of the Strathcarron citizen arc) — in flight on branch `127-credential-gated-service`. Four sub-PRs:
  - **PR-A #719** (merged): `samples/strathcarron-portal/` structural extract per the platform-vs-consumer boundary contract. F126 driving-licence page relocated out of `Sorcha.UI.Web.Client`. CI grep gate (`scripts/check-samples-references.ps1` + `.github/workflows/samples-boundary-check.yml`) enforces "no `ProjectReference` into `src/Apps/Sorcha.UI/` from samples except `Sorcha.UI.Components.User`".
  - **PR-B #720** (merged): F111 reconciliation — F127 adopts F111's Timebound Presentation Lifecycle as substrate. New `SorchaWalletPresentationConsumer` (first non-HAIP `IPresentationConsumer`) wraps `Sorcha.Verifier.Engine` server-side. New `BuildInitiationAsync` extension on `IPresentationConsumer` (lands the F111 deferred contract). New `GET /api/presentations/{id}/disclosed-claims?token=…` endpoint with single-use `ClaimsFetchToken`. New `BlueprintHubGroups.PresentationNonce` + `IBlueprintHubClient.PresentationOutcomeReady` thin-signal event. Library: `PresentationHubConnection`, `IPresentationSignal`, `CredentialGateComponent`. 25 new tests (11 consumer unit, 8 bunit, 6 FakeTimeProvider) — all pass.
  - **PR-C #721** (merged): Blue Badge content — three-action blueprint chain (`verify-identity` → `submit-blue-badge` → `issue-blue-badge`), `BlueBadgeForm.razor` + `BlueBadge.razor` in the sample, `setup-blue-badge-demo.ps1` walkthrough seed chaining off Spec 3 state.json.
  - **PR-D**: doc propagation + structured-logging audit + Redis store unit tests + (deferred) integration tests + final regression.
- **F127 ↔ F111 reconciliation** captured in `docs/superpowers/specs/2026-05-15-f127-f111-reconciliation.md` and the Spec 4 design doc §14. The pre-amendment Spec 4 shape (greenfield endpoints + new blueprint syntax + nonce stash) was discarded in favour of extending F111's shipped lifecycle.
- **Platform-vs-consumer boundary** locked via `docs/superpowers/specs/2026-05-15-platform-consumer-boundary-design.md` (PR #718). Application-specific demo code lives in `samples/`, builds to its own container, no `ProjectReference` into `src/Apps/Sorcha.UI/` beyond `Sorcha.UI.Components.User`. Driver: deployment realism.

### 2026-04-22
- **110-Agent-Persona-Mode** (Feature 110 User Stories 1-4 shipped via PRs #371, #372, #373, #374)
  - Adds an optional "persona" loop to `Sorcha.Agent` that fires workflow-starting actions on agent launch, unblocking the TradeFinance walkthrough (which hung because action 1 had no prior transaction to populate any inbox).
  - `Persona/` namespace: `PersonaDefinition`, `OnceTriggerLoop`, `IntervalTriggerLoop`, `PayloadTokenResolver` (typed-vs-interpolated `${now|uuid|counter|random.*}`), `PersonaSchemaValidator` (embedded JSON Schema), `PersonaDefinitionLoader` (JSONC-tolerant — comments + trailing commas), `PersonaSubmitter` (thin wrapper over the same `/api/instances/{id}/actions/{i}/execute` path `ActionExecutor` uses).
  - `ActorDefinition` gains an optional `PersonaFile` field; when null, behaviour is byte-identical to pre-feature (regression test enforced).
  - `RunCommand` launches `PersonaHost` as a peer task alongside the existing inbox loop; shared `CancellationToken` gives clean shutdown within ≤1 s (SC-004, regression test covers it).
  - `interval` trigger supports `everySeconds`/`everyMinutes`, `maxIterations`, `until`; transient failures keep the counter pinned; 3 consecutive hard failures trip an exit.
  - Coexistence proven: persona loop and reactive inbox loop share one `HttpClient` + `CancellationToken` without blocking each other (both orderings covered).
  - Ships three persona files: `procurement-mgr-kickoff.persona.json` (TradeFinance kickoff), `invoice-generator.persona.json` (scenario data generation, 20 × 30 s), `contractor-kickoff.persona.json` (ConstructionPermit parity — SC-002).
  - 106/106 tests passing in `Sorcha.Agent.Tests`; zero warnings in feature code.
  - Persona fires land in the agent `*.jsonl` audit stream alongside reactive decisions (research R-007) — decision label `persona-fire`, synthetic `ActionId` `persona:{name}#{counter}`. Operators have a single greppable audit file capturing both agent behaviours.
  - Deferred (operator-run only): T029/T043/T050 live walkthrough validation, T037 reactive-latency benchmark, T042 `$schema` stable URL.

### 2026-04-04
- **084-Mobile-Package-Infrastructure** (Feature 084 complete)
  - Extracted `Sorcha.Wallet.Portable` from Wallet.Core: entities, enums, service interfaces, derivation path builder -- zero EF Core/Npgsql dependencies
  - Extracted `Sorcha.ServiceClients.Http` from ServiceClients: all HTTP REST clients + SignalR helper -- zero gRPC/Protobuf dependencies
  - GitHub Actions workflow (`publish-nuget.yml`) for automated NuGet publishing of 9 packages on merge/tag
  - `SorchaHubConnectionBuilder` for mobile-friendly SignalR with JWT auth and exponential backoff reconnection
  - `HttpServiceCollectionExtensions` for registering HTTP-only service clients
  - MOB-001 eliminated (SorchaMobile confirmed .NET 10), MOB-002/003/004 complete
  - Full backward compatibility: 6254 unit tests pass, all Docker images build, zero breaking changes
- **083-Wallet-Key-Derivation** (Feature 083 complete)
  - Organisation-level HD key derivation using Sorcha-specific BIP32 paths (`m/0x534F52'/org'/dept'/user'/usage/index`)
  - `OrgMasterKey`: Per-org root seed encrypted at rest via Key Protection Provider
  - `DerivedKeyRecord`: User keys derived from org master with path/usage/index/status tracking
  - Key rotation (index increment, old key decrypt-only) and revocation (wallet lock + DID event)
  - `KeyUsage` enum: Identity, VCIssuance, Governance, Communications, ServiceAuth
  - `CustodyMode` model: Custodial (implemented), CoSigned and SelfCustody (schema only)
  - 4 new REST endpoints for master key provisioning, key derivation, rotation, and revocation
  - Implements WALLET-R1 through R4 from deferred research items

### 2026-04-02
- **082-Cloud-KMS-Key-Management** (Feature 082 complete)
  - Envelope encryption model: DEKs generated locally, wrapped by Key Protection Provider (KPP)
  - `IKeyProtectionProvider` — new interface replacing legacy `IEncryptionProvider` for production use
  - `AzureKeyVaultKeyProtectionProvider`: full AES-256-GCM envelope encryption backed by Azure Key Vault wrap/unwrap
  - `AzureSigningProvider`: KMS-resident signing — keys generated in AKV and never exported; supports ED25519, P-256
  - `Sorcha.Wallet.Providers.Azure` NuGet package registered via `AddAzureKeyProtectionProvider()`
  - `WalletKeyManagementOptions.SigningPolicy`: `DefaultMode` (Local/KmsResident), `AllowLocalToKmsMigration`, `AllowedKmsAlgorithms`
  - DEK cache: TTL + grace period for outage tolerance, configurable max entries
  - Health check: `EncryptionProviderHealthCheck` validates KPP connectivity on startup
  - AWS/GCP KMS deferred to post-release

### 2026-03-16
- **058-Platform-Organisation-Topology** (Phases 1-10 complete, 81 tasks)
  - Three-tier org topology: system admin, public, private organisations
  - PlatformUser cross-org identity with social login (Google, GitHub, Microsoft, Apple)
  - Email/password signup with BCrypt + NIST SP 800-63B password policy
  - Blueprint-driven self-service org creation
  - Organisation switching with JWT re-issuance
  - Platform governance: org audit, suspension/activation, settings management
  - Admin-initiated org creation with invitation
  - Platform Organisations management UI page
  - YARP gateway routes for all new endpoints

### 2026-03-09
- **054-Org-Identity-Admin** (Phases 1-9 complete, T001-T082)
  - **Tenant Service upgraded to 95%**: Full organization admin and identity management
  - **OIDC integration**: Discovery-first configuration with top-5 provider shortcuts (Microsoft Entra, Google, Okta, Apple, Cognito)
  - **Multi-tenant URL resolution**: 3-tier scheme — path (`/org/{sub}`), subdomain (`{sub}.sorcha.io`), custom domain (CNAME)
  - **Role consolidation**: 8 roles reduced to 5 (SystemAdmin, Administrator, Designer, Auditor, Member)
  - **Admin console**: 7 Blazor WASM pages for identity management (dashboard, users, IDP config, invitations, domains, audit, org settings)
  - **Auto-provisioning**: Automatic user provisioning on first OIDC login with configurable domain restrictions
  - **Email verification**: Required for all users; trusts IDP `email_verified` claim
  - **Social login support**: Microsoft, Google, Apple + optional email/password registration
  - **TOTP 2FA**: Time-based one-time password with setup/verify/disable flow
  - **Invitation system**: Email-based invitations with role assignment and expiration
  - **Progressive account lockout**: 5 fails=5min, 10=30min, 15=24h, 25=admin unlock
  - **NIST password policy**: Min 12 chars, no complexity rules, breach list check
  - **Audit retention**: 12 months default, admin-configurable
  - Test results: 479 Tenant tests pass, 27 Gateway tests pass

### 2026-03-07
- **053-Peer-Router-App-and-Peer-Service-Completion** (Phases 1-8 complete)
  - **Peer Service upgraded to 95%**: Circuit breaking wired, SQLite removed, PostgreSQL queue migration complete
  - **PeerRouter application (100%)**: Standalone P2P network bootstrap and debug tool for peer network diagnostics *(later retired in Feature 143 — folded into Sorcha.Peer.Service)*
  - All 8 phases delivered (T001-T056)

### 2026-03-06
- **051-Operations-Monitoring-Admin** (65 tasks, 10 phases — operational admin UI & CLI)
  - **Dashboard auto-refresh**: 30-second timer on Home.razor with refresh indicator, graceful "data unavailable" states
  - **Alert dismissal**: Per-user alert dismissal via localStorage-based AlertDismissalService
  - **Wallet access delegation**: WalletAccessTab component (grant/list/revoke/check), CLI `wallet access` subcommands
  - **Schema provider CLI**: `schema providers list|refresh` commands via IBlueprintServiceClient
  - **Events admin**: EventsAdmin.razor with server-side pagination, severity filters, delete with confirmation; CLI `admin events list|delete`
  - **Push notifications**: NotificationSettings.razor with MudSwitch toggle, browser permission request via JS interop, status chips
  - **Encryption progress**: EncryptionProgressIndicator.razor with auto-poll timer, stage labels; CLI `operation status`
  - **Presentation admin UX**: Replaced comma-separated text inputs with chip-based tag inputs for AcceptedIssuers and RequiredClaims; 5-second auto-refresh for pending presentation requests
  - **Credential lifecycle errors**: Typed CredentialOperationResult with specific messages for 403/404/409/500; CredentialStatus constants replacing magic strings
  - **Code quality**: Shared JsonSerializerOptions, CredentialStatus constants, CredentialOperationResult typed errors
  - Test results: 38+ new unit tests (services, error handling, auto-refresh polling), 4 E2E test suites

### 2026-03-04
- **048-Register-Policy-Model** (54 tasks, 9 phases — unified register policy & system register)
  - **RegisterPolicy model**: Governance (quorum formula), Validators (registration mode, approved list, operational TTL), Consensus (thresholds, docket limits), LeaderElection (mechanism, heartbeat, term duration)
  - **Policy on genesis**: RegisterCreationOrchestrator embeds RegisterPolicy in control record; GenesisConfigService reads policy with 3-tier fallback (policy → legacy → defaults)
  - **System Register**: Deterministic singleton register bootstrapped on startup with governance blueprints; SystemRegisterBootstrapper + SystemRegisterEndpoints
  - **Approved validators**: Consent-mode registration with on-chain approved validator list; ValidatorRegistry checks DID/PublicKey against policy
  - **Policy updates**: control.policy.update Control transactions; ControlDocketProcessor validates version, transition rules, min/max constraints
  - **Governance quorum**: Parameterized quorum calculation (strict-majority, supermajority, unanimous) on RegisterControlRecord
  - **Operational presence**: Policy-driven TTL for validator heartbeat (OperationalTtlSeconds); DocketBuildTriggerService enforces minValidators before building
  - **FluentValidation**: RegisterPolicyValidator with nested validators for all policy sections
  - **YARP routes**: 9 new routes for policy, system register, and validator query endpoints
  - Test results: 25+ new tests across Register and Validator test projects

### 2026-03-02
- **045-Encrypted-Payload-Integration** (67 tasks, 10 phases — envelope encryption for action transactions)
  - **Envelope encryption**: XChaCha20-Poly1305 symmetric + per-recipient asymmetric key wrapping (ED25519, P-256, RSA-4096, ML-KEM-768)
  - **Disclosure grouping**: DisclosureGroupBuilder optimizes M groups (by unique field set) instead of N per-recipient ciphertexts
  - **Async pipeline**: Channel&lt;T&gt; + BackgroundService with 4-step progress (ResolvingKeys → Encrypting → BuildingTransaction → Submitting)
  - **SignalR notifications**: EncryptionProgress, EncryptionComplete, EncryptionFailed events to wallet groups
  - **Operations polling**: GET /api/operations/{operationId} fallback for clients without SignalR
  - **Public key resolution**: Batch register lookup with external key override, revoked → hard fail, not-found → skip with warning
  - **Pre-flight size estimation**: CheckSizeLimit with 4MB default, hot-reloadable via IOptionsMonitor
  - **Recipient decryption**: TransactionRetrievalService unwraps key → decrypts → verifies SHA-256 integrity hash
  - **Backward compatibility**: Legacy unencrypted transactions detected and returned as-is
  - **OpenTelemetry**: ActivitySource traces on EncryptionPipelineService and EncryptionBackgroundService
  - Test results: 44+ new tests across Blueprint Service and TransactionHandler

### 2026-02-26
- **043-UI-CLI-Modernization** (66 tasks, 11 phases — UI polish and CLI expansion)
  - **Activity Log** *(legacy — removed in F170)*: EventEntity + EventService + ActivityLogPanel bell icon; superseded by the Inbox spine (F169)
  - **Sidebar**: Consolidated ADMINISTRATION section, mini drawer with OpenMiniOnHover
  - **StatusFooter**: 30s health polling, connectivity indicator, pending action count, 9 bunit tests
  - **Wallet Management**: List/grid view toggle, default wallet star, QR address dialog, PQC algorithm support in CreateWallet, WalletPreferenceService migrated to server-side with localStorage fallback (14 tests)
  - **Dashboard Wizard**: Conditional wizard/KPI display on Home.razor, auto-set default wallet on creation
  - **Validator Dashboard**: ValidatorPanel with 3s polling, throughput sparkline visualization, register health table (16 tests)
  - **Settings**: 6-tab Settings.razor (Appearance, Language, Security, Notifications, Connections, About), TOTP 2FA backend (TotpConfiguration encrypted entity, ITotpService, setup/verify/disable/status endpoints with rate limiting, loginToken 2FA flow), ThemeService dark mode with OS detection, LocalizationService JSON i18n (fr/de/es), TimeFormatService, push notification service worker + subscription endpoints
  - **CLI Commands**: BlueprintCommands, ParticipantCommands, CredentialCommands, ValidatorCommands, AdminCommands — all using Refit HTTP clients + Spectre.Console rich output
  - **UserPreferences API**: 5 CRUD endpoints with DTOs and UI client integration (17 endpoint tests)
  - Test results: TOTP endpoint tests + CLI command tests committed

### 2026-02-25
- **040-Quantum-Safe-Crypto** (74 tasks, 10 phases — post-quantum cryptography with CNSA 2.0 compliance)
  - **Algorithm Support Matrix:**
    | Algorithm | Type | Security Level | Key Size | Signature Size |
    |-----------|------|---------------|----------|----------------|
    | ML-DSA-65 | Lattice signature | NIST Level 3 | 1,952 bytes | 3,309 bytes |
    | ML-KEM-768 | Lattice KEM | NIST Level 3 | 1,184 bytes | 1,088 bytes |
    | SLH-DSA-128s | Hash-based sig | NIST Level 1 | 32 bytes | 7,856 bytes |
    | BLS12-381 | Pairing signature | 128-bit | 48 bytes | 96 bytes |
  - Hybrid signing: classical (ED25519/P-256) + PQC (ML-DSA-65) dual signatures for quantum resistance
  - Per-register crypto policy: governance-controlled algorithm acceptance, required algorithms, and migration deadlines
  - SLH-DSA-128s: stateless hash-based signatures as conservative fallback for lattice cryptanalysis concerns
  - ws2-prefixed Bech32m addresses: quantum-safe wallet addresses with error-correcting encoding
  - ML-KEM-768 payload encryption: quantum-safe KEM + AES-256-GCM hybrid for confidential payloads
  - BLS12-381 threshold signatures: t-of-n distributed docket validation with Shamir secret sharing
  - Zero-knowledge proofs: Pedersen commitments on secp256k1 with Schnorr proofs (inclusion), OR proofs (range)
  - Register Service ZK proof API: generate/verify inclusion proofs, YARP gateway routes
  - Test results: 270+ new tests across Cryptography, Wallet, Validator, Register test projects

### 2026-02-21
- **039-Verifiable-Presentations** (82 tasks, 11 phases — verifiable credential lifecycle & presentations)
  - W3C Bitstring Status List: GZip+Base64 compressed bitstrings, MSB-first bit ordering, public GET endpoint for verifiers
  - Credential lifecycle state machine: Active/Suspended/Revoked/Expired/Consumed with valid transition enforcement
  - OID4VP presentation flow: request creation, credential matching, selective claim disclosure, verification result polling
  - QR code presentation: openid4vp:// deep link generation for physical credential presentation
  - DID resolution registry: pluggable resolvers (did:sorcha, did:web, did:key) with ActivitySource tracing
  - Cross-blueprint credential issuance: usage policy (SingleUse/LimitedUse/Reusable), display config, status list allocation
  - Credential wallet UI: card list with issuer-styled cards, status filter, search, detail dialog, export
  - Presentation inbox UI: tabbed interface with badge count, credential selection, claim disclosure checkboxes
  - YARP routes for presentation endpoints → wallet-cluster
  - Structured logging and ActivitySource tracing for credential and DID operations
  - Test results: Engine 323+ pass, Wallet Service 251+ pass, Blueprint Service 300+ pass, UI Core 517+ pass

- **038-Content-Type-Payload** (69 tasks, 9 phases — content-type aware payload encoding)
  - Added `ContentType` and `ContentEncoding` metadata fields to `PayloadModel` and `PayloadInfo`
  - Created `PayloadEncodingService`: centralized Base64url/identity/Brotli/Gzip encode/decode with configurable compression threshold (4KB default)
  - Migrated ~128 call sites from legacy Base64 (`Convert.ToBase64String`/`FromBase64String`) to `Base64Url` (RFC 4648 §5)
  - MongoDB BSON Binary storage: `MongoTransactionDocument` stores Signature/Data/Hash as BSON Binary subtype 0x00 (~33% storage reduction). Dual-format reads via `BinaryAwareStringSerializer` (handles both BsonBinary and BsonString)
  - `JsonTransactionSerializer` emits native JSON objects for identity-encoded payloads (ContentEncoding: "identity")
  - Cryptographic conformance: Base64url encoding produces correct bytes, legacy Base64 auto-detected and normalized
  - 23 MongoDocumentMapper tests, 12 JsonTransactionSerializer tests, 40 PayloadEncodingService tests, 6 CryptoEncodingConformance tests
  - Test results: TransactionHandler 183 pass, Register Core 234 pass, Cryptography 122 pass, Validator 639 pass

### 2026-02-18
- **037-New-Submission-Page** (31 tasks, 8 phases — user activity: new submission service directory)
  - Redesigned MyWorkflows.razor from workflow instance list into service directory grouped by register
  - Created WalletPreferenceService: localStorage-backed smart default wallet selection
  - New components: WalletSelector.razor (inline, auto-hides for single wallet), NewSubmissionDialog.razor (create instance + execute Action 0)
  - Added GetAvailableBlueprintsAsync (IBlueprintApiService), CreateInstanceAsync + SubmitActionExecuteAsync (IWorkflowService) with X-Delegation-Token
  - Fixed Pending Actions page: wired wallet into ActionForm, actual backend submission after dialog
  - Swapped nav order: New Submission before Pending Actions
  - 10 new WalletPreferenceService tests, all UI projects build 0w/0e

- **036-Unified-Transaction-Submission** (26 tasks, 7 phases — single transaction submission path)
  - Created ISystemWalletSigningService: singleton with wallet caching, derivation path whitelist, sliding-window rate limiting, structured audit logging
  - Unified all transaction types (genesis, control, action) through `POST /api/v1/transactions/validate`
  - Removed signature verification skip for genesis/control transactions — all types now require valid signatures
  - Migrated register creation (RegisterCreationOrchestrator) from legacy genesis endpoint to signing service + generic endpoint
  - Migrated blueprint publish (Register Service Program.cs) from legacy genesis endpoint to signing service + generic endpoint
  - Removed legacy genesis endpoint (`POST /api/validator/genesis`), GenesisTransactionSubmission, GenesisSignature, SubmitGenesisTransactionAsync
  - Renamed ActionTransactionSubmission → TransactionSubmission (single unified model)
  - Documented direct-write tech debt: Blueprint Service paths 6-7, Validator self-registration paths 8-9
  - 15 new signing service tests, 4 new blueprint publish tests, 28 TransactionBuilder tests updated
  - Test results: ServiceClients 24 pass, Validator 627 pass (1 pre-existing), Blueprint Service 28 pass

### 2026-02-11
- **031-Register-Governance** (80 tasks, 9 phases — decentralized register governance)
  - TransactionType.Genesis renamed to Control (value 0 preserved), System=3 removed
  - Governance models: GovernanceOperation, ApprovalSignature, ControlTransactionPayload, AdminRoster
  - DID scheme: `did:sorcha:w:{walletAddress}` (wallet) + `did:sorcha:r:{registerId}:t:{txId}` (register)
  - GovernanceRosterService: roster reconstruction, quorum validation (floor(m/2)+1), proposal validation
  - DIDResolver: wallet + register DID resolution with cross-instance support
  - RightsEnforcementService: validator pipeline stage 4b (governance rights check)
  - Governance REST endpoints: roster + history (paginated)
  - 56 new tests: Register Core 234 pass, Validator Service 620 pass

### 2026-02-08
- **PR #110 P2P Review Fixes** (12 issues resolved — 3 critical, 4 high, 5 medium)
  - CRITICAL: Race condition fix (Dictionary → ConcurrentDictionary), EF Core migration, hardcoded password removal
  - HIGH: JWT authentication added, gRPC channel idle timeout, RegisterCache eviction limits, replication timeouts, batched docket pulls
  - MEDIUM: Magic numbers replaced with named constants, seed node reconnection, gRPC message size limits, idle connection cleanup wired into heartbeat
  - 504 tests passing (4 new eviction tests)
- **UI Modernization 100% complete** (92/94 tasks, 13 phases — 2 Docker E2E tasks remain)
  - Admin: Organization management, validator admin panel, service principal management, flattened navigation
  - Core Workflows: Real workflow instance management and action execution pages
  - Blueprint Cloud Persistence: Designer and Blueprints pages backed by Blueprint Service API with publishing flow
  - Dashboard: Live stat cards wired to gateway /api/dashboard endpoint
  - User Pages: Real Wallet Service integration (CRUD, addresses) and Register Service transaction queries
  - Template Library: Backend template API integration (CRUD, evaluate, validate)
  - Explorer: Docket/chain inspection, advanced OData query builder for cross-register searches
  - UX: Consistent TruncatedId component for all long identifiers (first 6 + last 6 with ellipsis)
  - Shared: EmptyState, ServiceUnavailable reusable components
  - E2E: 7 new Docker test files, 4 new page objects, 0 warnings
  - LoadBlueprintDialog: Self-loading via IBlueprintApiService (no longer requires caller to pass data)

### 2026-03-16
- **Designer & Blueprint Instructions Upgrade** (Feature 059)
  - Blueprint Instructions Model: overview, per-action, per-participant, per-field guidance with schema description fallback
  - Semantic Versioning: v{major}.{minor} with structural diff detection (SHA-256 hash comparison)
  - Unified BlueprintDiagram component (Edit/Preview/Compact modes) with swimlane layout
  - Blueprint Publishing Workflow template: Author→Reviewer→Publisher governance with structural/doc review paths
  - Catalogue Dual-Source: auto-seeded templates + published blueprints from system register
  - Designer Context Handoff: round-trip between visual designer and AI chat via query params
  - Instructions Editor: InstructionsTab, InstructionsPreview, export/import for translation, stale detection
  - Stub Fixes: AI chat export download, clipboard copy, schema-aware field resolution, RouteEditor, DisclosureEditor
  - 2 new API endpoints: /versions (history), /classify-change (structural diff)
  - 69 tasks completed, ~100 new tests

### 2026-01-28
- **UI Register Management 100% complete** (70/70 tasks)
  - Enhanced CreateRegisterWizard with 4-step flow including wallet selection
  - Added RegisterSearchBar component for client-side filtering by name and status
  - Created TransactionQueryForm and Query page for cross-register wallet search
  - Added clipboard.js interop with snackbar confirmation for copy actions
  - Enhanced TransactionDetail with copy buttons for IDs, addresses, signatures
  - Added data-testid attributes across components for E2E testing
- **CLI Register Commands 100% complete**
  - `sorcha register create` - Two-phase register creation with signing
  - `sorcha register list` - List registers with filtering
  - `sorcha register dockets` - View dockets for a register
  - `sorcha register query` - Query transactions by wallet address
  - All commands use System.CommandLine 2.0.2 with proper option naming

### 2026-01-21
- **UI Consolidation 100% complete** (35/35 tasks)
- All Designer components migrated (ParticipantEditor, ConditionEditor, CalculationEditor)
- Export/Import dialogs with JSON/YAML support
- Offline sync service and components (OfflineSyncIndicator, SyncQueueDialog, ConflictResolutionDialog)
- Consumer pages: MyActions, MyWorkflows, MyTransactions, MyWallet, Templates
- Settings page with profile management
- Help page with documentation
- Configuration service tests (59 tests)
- Fixed Docker profile to use relative URLs for same-origin requests

### 2025-12-22
- Validator Service documentation completed (95% MVP, ~3,090 LOC)

### 2025-12-14
- Peer Service Phase 1-3 completed (63/91 tasks, 70%)
- Central node connection with automatic failover
- System register replication and heartbeat monitoring
- ~5,700 lines of production code

### 2025-12-12-13
- AUTH-002: JWT Bearer authentication for all services
- AUTH-003: PostgreSQL + Redis infrastructure deployment
- AUTH-004: Bootstrap seed scripts
- WS-008/009: Wallet Service EF Core repository

### 2025-12-07
- Tenant Service integration tests (67 tests, 91% pass rate)

### 2025-12-04
- Blueprint Service Sprint 10 orchestration (25 new tests)

---

## Deferred from MVD (Future Work)

These items are explicitly **out of scope for MVD** and deferred to production readiness phases:

1. **Azure AD B2C** — Dedicated Azure AD B2C provider (generic OIDC integration now available via Feature 054)
2. **Azure Key Vault** — ~~Production key management for Wallet Service~~ **Complete (Feature 082)**. AWS/GCP KMS deferred.
3. **Fork Detection** — Validator Service chain fork handling
4. **Enclave Support** — Trusted execution environment for Validator
5. **BLS Threshold Coordination** — Peer Service distributed docket signing
6. **Decentralized Consensus** — Leader election, multi-validator coordination

---

## Production Readiness (~30%)

The platform is feature-complete for MVD but requires the following for production deployment. See [MASTER-TASKS.md](../../.specify/MASTER-TASKS.md) v7.0 for the full release task list (44 tasks across 6 themes).

| Area | Status | Priority | MASTER-TASKS Theme |
|------|--------|----------|-------------------|
| Security hardening (HTTPS, validation, audit) | Pending | P0 | Theme 1 |
| Azure Key Vault for key storage | Complete (Feature 082) | P0 | Theme 1 |
| Deployment documentation & automation | Pending | P1 | Theme 2 |
| Backup and disaster recovery | Pending | P1 | Theme 2 |
| Production database tuning | Pending | P1 | Theme 2 |
| Monitoring and alerting | Pending | P2 | Theme 2 |
| Load testing at scale | Pending | P2 | Theme 2 |
| Azure AD B2C integration | Partially addressed (OIDC) | P2 | Theme 5 |

---

**Document Version:** 4.4
**Last Updated:** 2026-04-04
**Owner:** Sorcha Architecture Team

**See Also:**
- [MASTER-PLAN.md](../../.specify/MASTER-PLAN.md) - Implementation phases
- [MASTER-TASKS.md](../../.specify/MASTER-TASKS.md) - Task tracking
- [architecture.md](../architecture.md) - System architecture

## Feature 150 — Unified Account Security Surface (2026-06-11)

Status: **US1–US4 + T061 implemented** (PR #1001, branch `150-account-security`). Consolidated Security home (web `/app/security` + PWA `/wallet/security`), assurance-aware floor rule, always-notify, finished Passkey step-up proof, Email OTP (US2) and config-gated SMS OTP (US3) second factors. Full Tenant suite green (1313+). Runtime verification (email delivery, login-with-email-code, real SMS provider) pending the Docker stack / operator config; Playwright E2E + Re-OAuth in-browser redirect UX deferred. See `specs/150-account-security/` and the `sorcha-architecture` skill (Feature 150).
