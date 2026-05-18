# Sorcha v1 release roadmap

**Date:** 2026-05-16
**Author:** Backlog audit (cold-start session)
**Audit basis:**
- `.specify/MASTER-TASKS.md` v7.14 (57 active tasks across 8 themes)
- `.specify/constitution.md` v1.3
- `docs/reference/development-status.md` v4.6 (100% MVD, ~30% production readiness)
- `docs/security/SECURITY-AUDIT-2026-03-19.md` (7 CRITICAL · 14 HIGH · 16 MEDIUM · 4 LOW)
- `docs/audits/2026-04-01-codebase-audit.md` (3 CRITICAL · 4 HIGH · 11 MEDIUM · 15 LOW; 5 closed)
- In-code `TODO/FIXME/HACK/DEFERRED/REVISIT` markers across `src/` and `tests/` (~109 raw, ~50 material)
- GitHub: 23 open issues, 1 open PR (#392 draft RFQ), 60 merged PRs in last 60 days
- `specs/` (open: F119 follow-up, deferred lists inside F124–F128 spec/tasks files)
- `~/.claude/projects/C--Projects-Sorcha/memory/` (`project_security_audit_features`, `project_signalr_pending_actions`, `project_wallet_derivation_chain`, `project_feature_108_followups`, `project_feature_113_storage_audit`, `project_recipients_wallets_pipeline_gap`, `project_p2p_replication_backlog`, `project_multi_node_audit`)
- Service READMEs (Validator, Register, Agent) and `.claude/skills/sorcha-architecture/SKILL.md`

**Bottom-line gap:** **17 release blockers · 18 v1-scope items · 14 v1-quality items · 30 explicitly post-v1 · 1 parked.** Critical-path estimate **~14–18 calendar weeks of focused work** at one engineer, or **~7–9 weeks with two engineers running independent tracks**. The longest single chain is consensus-vote verification → multi-validator atomic docket ordering → cross-node integration test pass, ~5 calendar weeks.

The platform is feature-complete for the MVD and the Strathcarron citizen arc (Specs 1–4 shipped, Spec 5 outstanding). The gap to v1 is dominated by **infrastructure hardening, consensus correctness, and operational maturity** — not new features. Two thirds of the blockers are well-scoped audit items; the remaining third are correctness gaps the team already knows about.

---

## Release blockers (must fix before v1)

Ordered by dependency-and-risk: things downstream work assumes, scariest unknowns first.

### B-01 · Consensus vote cryptographic verification — CRITICAL
- **Source:** `SECURITY-AUDIT-2026-03-19 §4.1`, `project_security_audit_features.md`
- **Files:** `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs:111-158`, `SignatureCollector.cs:50-200`
- **What:** Incoming consensus votes are counted on `response.Approved && response.Signature != null` without verifying signature ownership. An attacker who reaches a validator's gossip surface can impersonate N validators and push consensus over threshold.
- **Category:** BLOCKER · Area: security/correctness · Effort: M (2–3 days design + implementation) · Risk: high (touches validator hot path)
- **Depends on:** nothing
- **Tracked as:** new feature (operator preference, per memory). Treat as Feature `129-consensus-vote-verification`.

### B-02 · Transaction replay protection — CRITICAL
- **Source:** `SECURITY-AUDIT-2026-03-19 §4.2`, `project_security_audit_features.md`
- **Files:** `src/Services/Sorcha.Validator.Service/Models/Transaction.cs`, `ValidationEngine.cs:659-791`
- **What:** No per-sender monotonic sequence numbers. `PreviousTransactionId` is a routing hint, not a replay guard. Same transaction is theoretically resubmittable.
- **Category:** BLOCKER · security/correctness · Effort: M (3 days) · Risk: high (data-model change, migration impact)
- **Depends on:** B-01 (both touch the validator pipeline; sequence the design pass once).
- **Tracked as:** Feature `130-transaction-replay-protection`.

### B-03 · Atomic docket numbering — HIGH (gates B-01 closure on real network)
- **Source:** `SECURITY-AUDIT-2026-03-19 §4.3`
- **Files:** `Sorcha.Validator.Service/Services/DocketBuilder.cs:106-107`, `Sorcha.Register.Core/Storage/IRegisterRepository.cs:40-41`
- **What:** `DocketNumber = latestDocket.DocketNumber + 1` with no CAS. Two validators building docket N simultaneously produces conflicting dockets.
- **Category:** BLOCKER · multi-node correctness · Effort: S (1 day — unique compound index `(RegisterId, DocketNumber)` + atomic insert) · Risk: medium
- **Depends on:** nothing. Stands alone but **must land before** any multi-validator soak.

### B-04 · Participant role authorisation on action execution — HIGH
- **Source:** `SECURITY-AUDIT-2026-03-19 §3.1`, ties to `SEC-008`/`SEC-009` in MASTER-TASKS
- **Files:** `Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:152-156`
- **What:** Action execution validates "current action" but **does not verify the sender wallet maps to the action's designated participant role**. Any wallet linked to any participant in the org can execute any action. This is an auth-bypass at the heart of the workflow engine.
- **Category:** BLOCKER · security · Effort: S (1 day) · Risk: medium (need to check open-participants/late-bind interaction in F103)
- **Depends on:** awareness of F103 late-binding (handled by `IsStartingAction` flag — confirmed in skill).

### B-05 · Redis authentication enabled — CRITICAL (deploy-config)
- **Source:** `SECURITY-AUDIT-2026-03-19 §5.2`
- **Files:** `docker-compose.yml:30-31`, `.env:63`
- **What:** Redis published on host port 16379 with no password. Local apps can poison blueprints, manipulate pools, hijack SignalR.
- **Category:** BLOCKER · security · Effort: XS (2 hrs) · Risk: low
- **Depends on:** nothing. Land alongside B-06/B-07/B-09 as a single compose-hardening PR.

### B-06 · gRPC peer TLS enabled — CRITICAL
- **Source:** `SECURITY-AUDIT-2026-03-19 §5.3`
- **Files:** `docker-compose.yml:346` (`PeerService__EnableTls: "false"`)
- **What:** Register sync, votes, gossip — all cleartext on the wire.
- **Category:** BLOCKER · security · Effort: XS (4 hrs config + cert provisioning) · Risk: low
- **Depends on:** TLS cert story (covered by SEC-001/HTTPS enforcement — see B-09).

### B-07 · JWT signing key externalised — CRITICAL
- **Source:** `SECURITY-AUDIT-2026-03-19 §5.4`
- **Files:** `docker-compose.yml:19`
- **What:** Default `sorcha-docker-dev-key-2025-…` shared across installs. Any token from one install is valid on another.
- **Category:** BLOCKER · security · Effort: XS (2 hrs) · Risk: low

### B-08 · Service-to-service secrets + DB credentials to vault — CRITICAL
- **Source:** `SECURITY-AUDIT-2026-03-19 §5.5, §5.6`, MASTER-TASKS `SEC-005`
- **Files:** `docker-compose.yml:132,176,242,288,339,394` (service secrets); `:50,72,121,174,221,224,282,336` (DB)
- **What:** Six service client secrets + PostgreSQL/MongoDB credentials embedded in compose env. AKV adapter exists (Feature 082 `IKeyProtectionProvider`); generalise the pattern to all secrets.
- **Category:** BLOCKER · security · Effort: M (2 days) · Risk: medium (touches every service's startup)
- **Depends on:** Feature 082 AKV plumbing (already shipped — re-use the adapter).

### B-09 · HTTPS enforcement across all services — P0
- **Source:** MASTER-TASKS `SEC-001`
- **What:** Currently HTTP in Docker; HTTPS only in Aspire dev. Kestrel TLS + cert management.
- **Category:** BLOCKER · security · Effort: M (~12 h estimate) · Risk: medium (cert rotation story)
- **Depends on:** B-06 cert provisioning (share the same cert workflow).

### B-10 · CORS restricted to specific origins — CRITICAL (audit overlap)
- **Source:** `SECURITY-AUDIT-2026-03-19 §5.7`, `2026-04-01 codebase-audit SEC-001`, `gh#326`, MASTER-TASKS `SEC-006`
- **Files:** `src/Common/Sorcha.ServiceDefaults/CorsExtensions.cs:25-27`
- **What:** Every service uses `AllowAnyOrigin/Method/Header`. Audit `gh#326` frames as policy decision: (a) document the gateway-trust boundary, or (b) tighten to per-service allow-list.
- **Category:** BLOCKER · security · Effort: XS (2 hrs for option (a) + doc; ~30 LoC for option (b)) · Risk: low
- **Decision needed:** Recommend option (b) for defence-in-depth.

### B-11 · DTO validation pass — HIGH
- **Source:** `2026-04-01 codebase-audit VAL-001`, `gh#327`, MASTER-TASKS `SEC-003`
- **Files:** `src/Services/*/Models/{Dtos,Requests}/**`
- **What:** Request DTOs lack `[Required]`, `[StringLength]`, `[RegularExpression]`, `[Range]`. Global middleware catches attack-shaped input but not business-logic gaps; handlers fall back to ad-hoc null checks (or none).
- **Category:** BLOCKER · security/correctness · Effort: M (1 day per service × 7 services ≈ 1 week; one PR per service) · Risk: low
- **Depends on:** nothing.

### B-12 · Wallet sign/decrypt delegated-access check — P1
- **Source:** MASTER-TASKS `SEC-008`
- **What:** Owner-only check exists; needs DelegationService integration for SignOnly/ReadOnly grants. Pattern exists on wallet detail endpoint.
- **Category:** BLOCKER · security · Effort: S (~4 h) · Risk: low

### B-13 · Participant publishing wallet-link verification — P1
- **Source:** MASTER-TASKS `SEC-009`
- **What:** `ParticipantPublishingService` accepts any wallet as signer; should verify the signer wallet is linked to the participant being published.
- **Category:** BLOCKER · security · Effort: S (~4 h) · Risk: low

### B-14 · Bootstrap secret + fail-closed token revocation — MEDIUM-as-blocker
- **Source:** `SECURITY-AUDIT-2026-03-19 §1.1, §1.2`
- **What:** (a) Token revocation fails open when Redis is unavailable — revoked tokens accepted. (b) `AllowAnonymous` bootstrap endpoint relies on a one-shot DB guard; a DB restore can re-bootstrap the platform.
- **Category:** BLOCKER · security · Effort: S (~6 h combined: env-var bootstrap secret + fail-closed switch with circuit breaker) · Risk: low

### B-15 · Cross-user SignalR pending-action notifications — correctness
- **Source:** `project_signalr_pending_actions.md`
- **Files:** `Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- **What:** When User A submits an action, User B's Pending Actions page does not update in real time. Root cause: `EncryptionBackgroundService` notifies the submitting user's wallet, not the next participant's, and skips the `NotifyParticipantsAsync` call after advancing the instance. Inline path does both correctly.
- **Category:** BLOCKER · correctness/UX · Effort: S (~½ day — mirror the inline path) · Risk: low
- **Depends on:** nothing.

### B-16 · F108 forward-direction: InstanceMirrorReconstructor for action transactions
- **Source:** `project_p2p_replication_backlog.md`, `project_feature_108_followups.md`
- **Files:** `Sorcha.Blueprint.Service/Services/Implementation/InstanceMirrorReconstructor.cs:253`
- **What:** Replicated action transactions don't materialise instance mirrors on the receiving peer. Gates PingPongN1-style multi-node walkthroughs from reaching action-1 submission. Without this, the multi-node story is theoretical.
- **Category:** BLOCKER · multi-node correctness · Effort: M (3–5 days, role-routing carve-out needed) · Risk: high (scariest known unknown — could expose deeper design gap)
- **Depends on:** nothing structural, but **front-load it** so spec rework lands before downstream features pile on.

### B-17 · Pre-existing test failures cleared
- **Source:** `2026-04-01 codebase-audit TEST-001/002/003`, multi-source memory references to long-standing Validator.Service / Blueprint.Service / Integration.Tests failures
- **What:** Quickstart.InboxRoundTripTests + MultiNode.HubBackplaneCrossReplicaTests failing; Blueprint.Service.Tests 81 constructor failures (ConfigurationBinder NRE); Validator.Service.Tests ~30 runtime failures; 68 compile errors in Sorcha.Register.Core.Tests (TenantId removed); 30 flaky Playwright E2E.
- **Category:** BLOCKER · quality (you cannot trust the test suite if "all green" includes "all the ones we ignore") · Effort: M (3–5 days triage + fix) · Risk: medium (cleanup may surface real bugs)
- **Depends on:** nothing — but easier if B-01/B-02 are merged first, since some validator tests will need rewriting against the new contracts.

---

## V1 scope — feature build path

Sequenced as four overlapping milestones. Each milestone is independently demoable.

### Milestone M1 — Hardening Sprint (target: 1.5–2 weeks, 1 eng)
**Goal:** Close every release blocker except the two consensus features.
**Demoable outcome:** A clean `docker-compose up` produces an HTTPS-only deployment with no default secrets, vault-sourced credentials, restricted CORS, and Redis/gRPC authentication. The codebase audit and security audit Phase 1 sign-off.

- B-05 Redis auth, B-06 gRPC TLS, B-07 JWT key externalisation, B-09 HTTPS, B-10 CORS (one compose-hardening PR)
- B-08 Secrets to vault (Feature 082 adapter reuse)
- B-11 DTO validation pass (one PR per service; can parallelise with second engineer)
- B-12 Wallet delegation check, B-13 participant publishing wallet-link, B-14 bootstrap/revocation
- B-04 Participant role validation in action execution
- B-15 Cross-user SignalR fix
- **V1-quality interleaved:** `OPS-005` DB migration scripts and versioning (touches the same compose surface).

### Milestone M2 — Consensus Correctness (target: 2–3 weeks, 1 eng)
**Goal:** Close the two CRITICAL consensus findings as first-class features.
**Demoable outcome:** A multi-validator deployment refuses forged consensus votes (recorded as a deliberate forgery test in the walkthrough) and rejects replayed transactions with a clear sequence-number error.

- B-01 Feature `129-consensus-vote-verification` (spec → plan → execute via `/gsd:autonomous` or manual). Touches `ConsensusEngine`, `SignatureCollector`, validator key roster handling.
- B-02 Feature `130-transaction-replay-protection`. Data-model change on `Transaction`, migration story for in-flight registers (sequence-numbers reset at register-genesis or backfilled from existing chain).
- B-03 Atomic docket numbering (small PR, but bundle with M2 so the multi-validator soak is meaningful).
- B-17 Pre-existing test failures cleared (or at least quarantined with explicit `[Skip(reason)]`).
- **V1-quality interleaved:** `OPS-007` load testing the consensus path under fault injection.

### Milestone M3 — Multi-node correctness + ops readiness (target: 2–3 weeks, 1 eng)
**Goal:** Make multi-node deployment a thing you can demo, not just claim.
**Demoable outcome:** Two-node Sorcha deployment, second node joins, replicates registers + dockets + action transactions, citizen on node A receives credentials from issuer on node B end-to-end. Operational dashboard shows health, lag, sync state.

- B-16 `InstanceMirrorReconstructor` materialises instances from replicated action transactions
- F108 follow-ups from `project_feature_108_followups.md`: T037 validator sealing observation push (30 LoC), T039 OTel counters (`register_observations_ingested_total`, `register_sync_state_current`), T038 admin-UI enum display names
- `gh#461` federation sync (`RelayCommunication.Stream` unimplemented)
- `OPS-001` Production deployment runbook (Azure Container Apps)
- `OPS-002` Bicep/Terraform templates
- `OPS-003` Monitoring + alerting dashboards (build on existing Aspire instruments + the new F108 OTel counters)
- `OPS-004` Backup/DR procedures (PostgreSQL, MongoDB, Redis)
- **V1-quality interleaved:** Closure of the n1 deployment scars surfaced in `project_feature_124_pwa_assured_identity.md` (`IDistributedCache` DI gotcha, nginx cache-header regression guard already shipped, base-href nav fix already shipped).

### Milestone M4 — Public UX + final pre-release polish (target: 1.5–2 weeks, 1 eng)
**Goal:** Public users have correct mental model of their data, register access, and roles.
**Demoable outcome:** A new Consumer signs up, sees only registers they're subscribed to, with role nomenclature that matches the docs.

- `UX-004` Auditor read-only access scope decision
- `UX-005` Dashboard org-scoped stats
- `UX-007` Public-user page hitting admin-only endpoint (latent auth-boundary bug — fix and silence)
- `GAP-020` Multi-org `ConstructionPermit` walkthrough completion (the run path; setup already passes)
- `SEC-004` OWASP Top 10 review + light pentest pass against the hardened build
- **V1-quality interleaved:** `OPS-009` CI/CD release pipeline (tags, changelog, artifact signing).

> **Already shipped before this roadmap was finalised — kept here as audit trail:**
> - `UX-001` Register subscription scoping (`gh#113`, closed) — `Registers/Index.razor` + `NewSubmissions.razor` both filter by `IRegisterSubscriptionService.GetMySubscribedRegistersAsync()`; `SubscribeDialog` and `InvitationsPanel` ship the merged "Available Registers" + invite surfaces. Auto-subscribe on register creation + system-admin↔system-register bootstrap verified on n1 2026-05-18.
> - `UX-002` `UserRole.Member → UserRole.Consumer` rename (`gh#112`, closed) — landed in `aee79106`. Remaining `Member` symbols in `src/` belong to other domains (`ParticipantRole.Member`, `StandardMember` permission flag, `RosterMember`).
> - `GAP-019` admin user provisioning (Feature 077) — `POST /api/platform/users` + `PUT /api/platform/users/{id}/password` ship in `PlatformManagementEndpoints.cs`. The remaining walkthrough plumbing is `GAP-020`'s problem, listed above.

### Milestone M5 (parallel track if 2 engineers) — Strathcarron Spec 5
- Spec 5 is the natural close of the citizen arc. Lands `IIssuerKeyResolver` production hardening (DID-resolver-backed), verifier-DID resolution in `SorchaWalletPresentationConsumer` (currently emits `did:sorcha:org:UNKNOWN`), and the recovery UX entry point.
- **Treat as v1-stretch:** ships if M1–M4 complete on schedule, deferred otherwise. Cohesive arc but not gating.

---

## V1 quality bar

Interleaved into the milestones above. Listed here for the audit trail.

| ID | Item | Source | Milestone | Effort |
|----|------|--------|-----------|--------|
| Q-01 | DB migration scripts + versioning strategy | `OPS-005`, MASTER-TASKS | M1 | 8 h |
| Q-02 | Load testing the consensus path | `OPS-007` | M2 | 16 h |
| Q-03 | Cross-node integration test (real Postgres/Mongo/Redis via Testcontainers) | F113 storage-audit memory; F119 follow-up `gh#585` | M3 | 8 h |
| Q-04 | Production runbook (`docs/operations/runbook.md`) | `OPS-001` | M3 | 16 h |
| Q-05 | Bicep / Terraform deployment automation | `OPS-002` | M3 | 20 h |
| Q-06 | Monitoring + alerting dashboards (Grafana queries, alerts on the existing OTel meters) | `OPS-003` | M3 | 16 h |
| Q-07 | Backup + DR procedures (PostgreSQL, MongoDB, Redis) | `OPS-004` | M3 | 12 h |
| Q-08 | Doc propagation to `docs/reference/API-DOCUMENTATION.md` for F114/F124/F126/F127/F128 endpoints (`.claude/skills/sorcha-architecture/SKILL.md` is current; the published doc lags) | F128 deferred items | M4 | 4 h |
| Q-09 | Code quality: `DEAD-001/002/003` (13+ unused UI models, 2 unused interfaces) | `2026-04-01 codebase-audit` | M4 | 4 h |
| Q-10 | Code quality: `DUP-001` shared `SorchaJsonOptions` (40+ inline `new JsonSerializerOptions()`) | `2026-04-01 codebase-audit` | M4 | 4 h |
| Q-11 | `CODE-005` `async void` event handler in `RotatingLeaderElectionService.cs:465` | `2026-04-01 codebase-audit` | M2 (touches validator) | 1 h |
| Q-12 | `CODE-006` `ManualResetEventSlim.Wait()` inside `Task.Run()` in `RegisterSyncBackgroundService.cs:108` — burns a thread for up to 5 min | `2026-04-01 codebase-audit` | M3 | 2 h |
| Q-13 | `SEC-003` (codebase-audit) fire-and-forget Redis ops in `RegisterAdvertisementService.cs:368-374` | `2026-04-01 codebase-audit` | M3 | 2 h |
| Q-14 | CI/CD release workflow (tags, changelog, artifact signing) | `OPS-009` | M4 | 8 h |

---

## Post-v1 (explicitly deferred)

Items deliberately cut. Each was considered and rejected for v1 scope.

### Strathcarron citizen-arc follow-ups (low-risk polish)
- F124: bUnit component tests, Playwright E2E, telemetry dashboard
- F125: real QR/NFC scanners + `webcamera-bridge.js`, `VerifiablePresentationValidator` extraction to shared library, `FileRenderer` `x-file.capture` integration, `PersonaAutofillResolver` wiring into `ApplicationInstance`, real `IUserHistoryClient` HTTP source + SignalR `TransactionConfirmed`, MyDevices/MyAuthMethods/MyProfile migration from `Sorcha.UI.Web.Client` into the shared library, client-side OTel counters, scaffold sweeps, `IdCardLayout` body enhancement (claim disclosures + issuer branding), `[Demo("…")]` Playwright E2E for the three demo beats, walkthrough setup scripts
- F126: cold-start E2E Playwright, Tier-1 device-pairing setup-script automation, `EnrolPairingSignal` timer-bound tests
- F127: verifier-DID resolution (waits on F120 production `IIssuerKeyResolver`), `PresentationSealSubscriber` hub-publish for F119 deferred outcomes, integration tests (T041–T043), T072 Playwright `[Demo("blue-badge-second-service")]`
- F128: bUnit tests for `PairingTakeover`/`PairingHandoffSurface`/`PairingNagBanner`, Playwright E2E across four routes, auto-sign-in resumption variant, seamless `start_url`-baked token (FR-031), telemetry dashboard

**Rationale:** Citizen arc functionally complete and verified on n1 end-to-end. Test polish and UX micro-tuning are real follow-ups but none block ship. SC-006 measurement (FR-031 trigger) is itself a post-launch decision.

### Trust hardening Tier 2–3
- TRUST-1 verifiable calculations (Validator re-executes JSON Logic), TRUST-2 validator-enforced disclosure, TRUST-6 consensus finality, TRUST-7 cross-register references, TRUST-8 audit trails, TRUST-9 trusted timestamps, TRUST-10 key rotation. `deferred-tasks.md`.

**Rationale:** TRUST-3/4/5 (receipts, Merkle proofs, revocation) shipped in F079 — that's the v1 trust story. Tier 2-3 sharpens it further but is post-MVP enhancement.

### P2P consensus + multi-validator distributed signing
- `P2P-001..008` from MASTER-TASKS Theme 6, including BLS12-381 threshold coordination (P2P-004), fork detection (P2P-005), decentralised leader election (P2P-006), enclave support (P2P-007), multi-validator coordination (P2P-008).

**Rationale:** v1 ships with single-validator-per-register and quorum-of-one-by-default. B-01/B-02 above close the security gaps that block multi-validator; full distributed consensus is a v2 feature.

### Mobile app (Sorcha MAUI)
- `MOB-005` device input field types, `MOB-006` UI form controls, `MOB-007` org branding for runtime white-labelling, `MOB-008` VC exchange protocol (QR/BLE/NFC), `MOB-009` co-signed dual-key wallet (explicitly v2 in MASTER-TASKS).

**Rationale:** `MOB-002/003/004` infrastructure shipped (Feature 084). The actual MAUI app lives in a separate repo and isn't gating the v1 web platform.

### Identity & auth advanced features
- `AUTH-002` refresh-token rotation, `AUTH-003` cross-tab token sync, `AUTH-004` session expiry warning UI, `AUTH-007` HIBP breach-list integration, `AUTH-008` custom-domain DNS verification automation, `AUTH-009` real-credentials social-login tests, `AUTH-010` OIDC token-exchange load testing.

**Rationale:** Core auth path (F054/F055/F058/F112/F114/F126/F128) is shipping-grade. These are convenience and ops items that smooth the experience.

### Feature 113 storage-audit polish
- `PresentationRequestStore.MarkCompletedAsync` read-many+CAS migration (low-probability race window — `TODO(113-followup)` inline), `FakeHostEnvironment` test-helper dedupe across 3 files, `StorageRegistrationRecord` constructor validation, per-register validator mempool size gauge, Redis-impl unit tests via Testcontainers (MockRedisBuilder lacks Lua), no-TTL accumulation on Redis mempool keys for abandoned registers.

### F108 lower-priority follow-ups
- Mutable shared `RegisterSerializationOptions.Canonical`, log-level for transient Register Service failures (should be Error not Warning), N+1 HTTP calls in repository fallback, DB-layer `DateTimeKind.Utc` enforcement (EF value converter), `RegisterSyncGrpcService` test-construction complexity, hardcoded `ReceiverIsValidator`, double-compute races, proto/contract mismatch, audit-log noise, rate limiting on observation endpoints.

### TRUST research items
- TRUST-1 through TRUST-10 (the 10-item research backlog in `.specify/tasks/deferred-tasks.md`).

### Codebase-audit MEDIUM/LOW
- `DEAD-002` unused CLI models, `DUP-002` duplicate encryption implementations across 123 files (real refactor; not a v1 problem), `DUP-003/004/005` (CLI Refit clients, gRPC+REST overlap, legacy gateway routes), `CODE-008..010` (ConfigureAwait audit, commented-out code, CancellationToken.None), `DOC-001..004` (XML doc coverage on internal services).

---

## Parked (drafts, RFCs, bookmarks)

- `gh#392` — spec(113): credential-priced RFQ for invoice financing. Draft PR, 22 days stale at audit time. Per MEMORY index: "deliberate bookmark." Leave parked.
- `gh#588` — backlog cross-cutting Sorcha-testing skill. Pattern-analysis gate; not v1.
- `gh#587` — MTP0001 warning on `dotnet test`. One-line MSBuild suppression. Fold into M1 as a free chore.
- F099 system-register Phase-2 default blueprints — `SystemRegisterService.cs:122 TODO`. Not v1.

---

## Dependency graph (textual)

```
                        [B-01 consensus verify]──┐
                                                 ├──► [B-03 atomic docket]──► [Multi-validator soak (M3 gate)]
                        [B-02 replay protection]─┘
                                  │
                                  └─► (touches validator hot path; share design pass)

[B-05 Redis auth]──┐
[B-06 gRPC TLS]────┼──► [Compose-hardening PR]──► [B-09 HTTPS]──► (M1 demo: clean HTTPS deploy)
[B-07 JWT key]─────┤              │
[B-10 CORS]────────┘              └──► [B-08 Secrets to vault]──► (M1 demo)

[B-11 DTO validation] ── independent (parallelisable per-service)

[B-04 Participant role check] ── independent
[B-12 Wallet delegation]      ── independent
[B-13 Participant publish]    ── independent
[B-14 Bootstrap/revocation]   ── independent

[B-15 SignalR cross-user] ── independent (correctness fix)

[B-16 InstanceMirrorReconstructor]──► [F108 forward-direction]──► [M3 multi-node demo]
                                          │
                                          └──► [gh#461 federation Stream]──► [M3 multi-node demo]

[B-17 Test failures cleared] ── precondition for trustworthy CI through all milestones; do early in M1

[M1 hardening] ──► [M2 consensus] ──► [M3 multi-node + ops] ──► [M4 public UX + pre-release polish]
```

**Critical path:**
`B-17 tests cleared` → `B-01 consensus verify` → `B-02 replay protection` → `B-03 atomic docket` → `B-16 InstanceMirrorReconstructor` → multi-node integration test pass.

At 1 eng, that chain is ~5 calendar weeks. The rest of M1/M3/M4 work runs alongside but is largely off-critical.

---

## Critical-path estimate

| Track | Effort (1 eng) | Calendar |
|-------|----------------|----------|
| Critical path (B-17 → B-01 → B-02 → B-03 → B-16 → integration test) | ~13 dev-days | 4–5 weeks (factoring review/CI) |
| M1 (hardening, independent of critical path) | ~7 dev-days | 1.5–2 weeks |
| M3 ops + multi-node polish (post B-16) | ~10 dev-days | 2–3 weeks |
| M4 public UX + pre-release | ~8 dev-days | 1.5–2 weeks |
| **Total serialised** | **~38 dev-days** | **~14 calendar weeks (1 eng)** |
| **Total with 2 engineers** | | **~7–9 calendar weeks** |

**Ship-date floor:** end of August 2026 with 1 engineer at sustained pace, mid-July 2026 with 2 engineers and no surprises. The biggest risk to that floor is **B-16 expanding scope** — if `InstanceMirrorReconstructor` exposes a deeper design gap in the role-routing / receiver model, treat that as a feature in its own right and add 2–3 weeks.

---

## Open questions for the operator

1. **CORS policy decision (B-10 / `gh#326`):** Tighten to per-service allow-list (recommended) or keep gateway-only with explicit doc? The audit frames this as a deliberate policy call.
2. **Replay-protection rollout (B-02):** New registers only, or backfill existing chains? Backfill adds 2–3 days to the estimate and an operator-runbook step.
3. **Strathcarron Spec 5 — v1 scope or v1.1?** Listed as M5 stretch above. If Spec 5 is the canonical end of the citizen-arc demo, it should arguably be v1; if it's a refinement on a working demo, defer.
4. **Mobile app prerequisites (`MOB-005/006/007`):** Listed as post-v1 here. If SorchaMobile is dependent on these landing in the platform repo before its v1, they belong in M4.
5. **`gh#392` RFQ draft:** Confirm the "parked bookmark" status — if the RFQ has gone cold, close it; if it's a genuine v1.1 candidate, move it to Post-v1.
6. **Pre-existing test failures (B-17):** Fix or quarantine? Some (e.g. the `Sorcha.Register.Core.Tests` 68 compile errors from `TenantId` removal) are recoverable; others (e.g. `MultiNode.HubBackplaneCrossReplicaTests`) may have been correct tests revealing real F108/F118 drift. Triage first, then decide.
7. **Single-validator-auto-approve flag (`SECURITY-AUDIT §4.6`):** Audit recommends removing from production config entirely; v1 shipping decision is whether this is a deployment-mode flag or hard-removed.
8. **Validator approval workflow (`SECURITY-AUDIT §4.5`):** Currently in `SEC-011b` (P2). Audit suggests upgrade to v1-blocker. Operator call: ship v1 with open registration + post-v1 governance, or block on approval workflow?

---

## Appendix — Source counts

| Source | Items inventoried | Items surfaced |
|--------|-------------------|----------------|
| Security audit 2026-03-19 | 41 (7 CRIT, 14 HIGH, 16 MED, 4 LOW) | 14 in blockers, 8 in v1-scope notes, rest deferred |
| Codebase audit 2026-04-01 | 33 (3 CRIT, 4 HIGH, 11 MED, 15 LOW; 5 closed) | 4 in blockers, 4 in v1-quality, rest deferred |
| MASTER-TASKS v7.14 | 57 active across 8 themes | 17 in M1–M4, 14 quality, rest deferred |
| In-code TODO/FIXME | ~109 raw, ~50 material | 6 surfaced (security/correctness clustered in HAIP, Wallet, Blueprint), rest deferred |
| GitHub open issues | 23 | 4 referenced inline (`#326 #327 #344 #461 #585 #587`), 1 parked (`#392`) |
| Memory deferred items | ~60 across 10 files | 8 surfaced (B-15, B-16, F108/F113/F124 polish, multi-node, wallet-derivation coupling) |
| Spec/design-doc deferrals | ~25 (Strathcarron arc) | Catalogued in Post-v1 |

**Total roadmap items:** 17 blockers · 18 v1-scope (in milestones) · 14 v1-quality · 30 explicit post-v1 · 1 parked = **80 items**. Underneath that, ~120 finer-grained deferrals are grouped by feature and listed by source.

---

**End of roadmap.**
