# Sorcha Platform - Master Task List

> **Archived phases:** See [MASTER-TASKS-ARCHIVE.md](MASTER-TASKS-ARCHIVE.md) for all completed features and phases.
> **Deferred research:** See [tasks/deferred-tasks.md](tasks/deferred-tasks.md) for long-term research items (TRUST-1 to TRUST-10, governance enhancements, advanced features).

**Version:** 7.13
**Last Updated:** 2026-05-13
**Status:** MVD Complete — Preparing for First Release
**Related:** [MASTER-PLAN.md](MASTER-PLAN.md) | [development-status.md](../docs/reference/development-status.md)

> **2026-05-13 since last update:** Features 119 (seal-aware ordering), 120 (production issuer signature verification — 18 PRs incl. cross-device kid-swap), 122 (Sorcha.UI.Components.User extraction), 123 (UI.Core audience-folder split), and 124 (UI.Core type-coupling fixes) all shipped to master. Counts below are headline figures from earlier sweeps and will drift between full re-counts — treat as approximate.

> **Maintenance Rule:** This file MUST be updated as part of every PR. When a task is completed, mark it ✅ and update the summary counts. When new work is identified, add it to the appropriate theme. Completed tasks stay in place (marked ✅) until the next archive sweep. Do not let this file go stale — it is the single source of truth for remaining work.

---

## Overview

The Sorcha platform is **100% MVD feature-complete**. All core features (045-053), production packaging (Phases A-E), and code quality work have been completed and archived.

This document now tracks **remaining work for the first production release**, organized by development theme.

**Completed (archived):** 523 tasks across 13 features/phases + 82 tasks from Feature 054 + 51 tasks from Feature 055 + 81 tasks from Feature 058 + 38 tasks from Feature 060 + Feature 062 (Pending Action Notifications) + Feature 063 (AI Blueprint Builder)
**Remaining:** 57 tasks across 8 themes (TRUST-3/4/5 completed in Feature 079; GAP-011 AI Builder completed in Feature 063)
**Deferred (post-release):** 43 research/future items in [deferred-tasks.md](tasks/deferred-tasks.md)

---

## Theme 1: Security Hardening — P0

> **Priority:** P0 (Release Blocker)
> **Estimated Effort:** 80-100h
> **Goal:** Production-grade security posture

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| SEC-001 | HTTPS enforcement across all services (Kestrel TLS, cert management) | P0 | 12h | 📋 | Currently HTTP in Docker; HTTPS only in Aspire dev |
| SEC-002 | Azure Key Vault integration for Wallet Service key storage | P0 | 16h | ✅ | Feature 082: Multi-cloud KMS with Azure Key Vault (envelope encryption + KMS-resident signing). AWS/GCP deferred. |
| SEC-003 | Input validation hardening (request size limits, field validation) | P0 | 12h | 📋 | Partial — some endpoints lack validation |
| SEC-004 | Security audit (OWASP Top 10 review, penetration testing) | P0 | 24h | 📋 | Pre-release requirement |
| SEC-005 | Secret management review (connection strings, JWT keys, API keys) | P0 | 8h | 📋 | Ensure no hardcoded secrets in deployed configs |
| SEC-006 | CORS policy review and hardening | P0 | 4h | 📋 | Currently permissive for development |
| SEC-007 | Rate limiting tuning (current: 7 write routes via YARP) | P1 | 4h | 📋 | Review limits for production load |
| SEC-008 | Wallet sign/decrypt delegate access — extend ownership check with DelegationService | P1 | 4h | 📋 | PR #170 added owner-only check; needs DelegationService.HasAccessAsync for delegated wallets (SignOnly for /sign, ReadOnly for /decrypt, etc.). Pattern exists on wallet detail endpoint (line 518). |
| SEC-009 | Participant publishing wallet-link verification | P1 | 4h | 📋 | ParticipantPublishingService should verify signer wallet is linked to the participant being published. Currently accepts any wallet as signer. |
| SEC-010 | Peer replication participant record re-validation | P2 | 8h | 📋 | Re-validate participant records during peer sync (verify signatures, check conflicts with existing records). Currently accepted verbatim. |
| SEC-011 | Service-to-service authentication for internal endpoints | P1 | 8h | ✅ | Closed: RequireService policy on all 5 internal endpoints. Service clients attach JWT headers. |
| SEC-011b | Defence-in-depth: per-service identity policies | P2 | 8h | 📋 | Check `service_name` claim per endpoint, scope enforcement, API Gateway internal route blocking, audit logging. Deferred from SEC-011. |
| SEC-012 | CodeQL alert remediation — log injection, info exposure, resource leaks | P1 | 4h | ✅ | Fixed: AcsEmailSender private info exposure, HttpRequestMessage/FormUrlEncodedContent dispose, SystemWalletSigningService double-check-locking. Log injection alerts (19) confirmed already resolved in master. |
| SEC-013 | HAIP Service internal endpoints — replace AllowAnonymous with service-to-service JWT auth | P0 | 4h | ✅ | PR #378 (merged 2026-04-23) — 4 HAIP internal endpoints now use `RequireService` policy (same pattern as SEC-011); `HaipServiceClient` attaches service JWT via `ServiceClientAuthHelper.SetAuthHeaderAsync`. Wallet-facing endpoints (Request Object GET, direct_post POST) correctly remain AllowAnonymous. |
| SEC-015 | Platform-wide organisation name validation — length cap, character class, admin-name guard | P2 | 4h | 📋 | Spun out from PR #391 code review (Feature 112). `Organization.Name` is currently a free-form `required string` with no DTO validation — an admin could set a phishing-shaped name that flows into every invitation / welcome email subject, admin UI, audit log, and participant DID card. PR #391 added defensive 60-char subject-line truncation on the email path as a local mitigation; the structural fix is validation at `CreateOrganizationRequest` / `UpdateOrganizationRequest` DTO level (max length ~80, alphanum + punctuation + basic emoji allowlist, reject unicode control chars and RTL-override exploits). |
| SEC-014 | HAIP presentation-request action — two-phase execution to prevent premature ledger recording | P0 | 12h | ✅ | Superseded by Feature 111 (three-event Timebound Presentation Lifecycle primitive). Shipped via PRs #382 (Phases 1-4: abstractions, US1 attempt-always-recorded, US2 outcome-with-reason, HAIP consumer) and #383 (US3 retry-first-class gating + 409 precondition). Integration tests + remaining Polish (Prometheus counters, walkthrough verification, legacy path removal) tracked in `specs/111-presentation-lifecycle/tasks.md`. |

---

## Theme 2: Production Infrastructure — P1

> **Priority:** P1 (Release Important)
> **Estimated Effort:** 80-120h
> **Goal:** Reliable production deployment and operations

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| OPS-001 | Production deployment documentation (Azure Container Apps) | P1 | 16h | 📋 | Docker Compose works; production runbook missing |
| OPS-002 | Deployment automation (Bicep/Terraform templates) | P1 | 20h | 📋 | PeerRouter deployed manually; other services pending |
| OPS-003 | Monitoring and alerting dashboards (health, errors, latency) | P1 | 16h | 📋 | Aspire dashboard exists; production APM needed |
| OPS-004 | Backup and disaster recovery procedures | P1 | 12h | 📋 | PostgreSQL, MongoDB, Redis backup strategy |
| OPS-005 | Database migration scripts and versioning strategy | P1 | 8h | 📋 | EF Core migrations exist but no release process |
| OPS-006 | Production database tuning (connection pools, indexes, query plans) | P2 | 12h | 📋 | Default configs currently used |
| OPS-007 | Load testing at production scale | P2 | 16h | 📋 | NBomber tests exist but not at production volumes |
| OPS-008 | Log aggregation and structured logging review | P2 | 8h | 📋 | Serilog configured; central aggregation needed |
| OPS-009 | CI/CD pipeline hardening (release tags, changelog, artifact signing) | P2 | 8h | 📋 | PR/merge CI works; release workflow incomplete |
| OPS-010 | Documentation & API Portal — OpenAPI enrichment, admin/onboarding guides | P1 | 16h | ✅ | Config-gated `/openapi` route, `/admin/dashboard` proxy, 132 endpoints enriched with `.Produces<T>()`, 11 admin/onboarding docs |

---

## Theme 3: Deferred Feature Gaps — P1-P2

> **Priority:** P1-P2 (Small gaps from completed features)
> **Estimated Effort:** 40-60h
> **Goal:** Close minor gaps left from MVD feature work

### From Feature 051 (14 deferred tasks)

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| GAP-001 | CLI test: T019 wallet access delegation command tests | P2 | 4h | 📋 | |
| GAP-002 | CLI test: T026 schema provider command tests | P2 | 4h | 📋 | |
| GAP-003 | CLI test: T031 events admin command tests | P2 | 4h | 📋 | |
| GAP-004 | CLI test: T045 operation status command tests | P2 | 4h | 📋 | |
| GAP-005 | T051 EncryptionProgress SignalR integration test | P2 | 4h | ✅ | Completed in Feature 075 — per-recipient events + notification tests |
| GAP-006 | Remaining 051 deferred items (9 polish/test tasks) | P2 | 16h | 📋 | Review and close or drop |

### From Content-Type Payload (038, 5 deferred tasks)

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| GAP-007 | PayloadManager compression integration (T059) | P2 | 4h | 📋 | Brotli/Gzip for large payloads |
| GAP-008 | Blueprint/Validator wire-format identity encoding (T053/T054/T060/T061) | P2 | 8h | 📋 | Use identity encoding where applicable |

### Other Feature Gaps

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| GAP-009 | Client-side SignalR integration (BP-5.8) | P2 | 8h | 📋 | Blazor WASM SignalR client wiring |
| GAP-010 | RecoverKeySetAsync implementation (CRYPT-1) | P2 | 6h | 📋 | Currently stubbed — returns "not yet implemented" |
| GAP-011 | AI Blueprint Builder Enhancement (063) | P2 | 40h | ✅ | Feature 063 shipped (PR #85) — schema library (25+ schemas across 7 categories), 5 AI tools (`use_standard_schema`, `search_schemas`, `search_templates`, `require_credential`, `issue_credential`), consultative conversation flow, DPP via composable credential chain, chat UI fixed-bottom input + auto-scroll. 40/41 tasks done; only T041 US6 regression E2E deferred (see GAP-011b). |
| GAP-011b | AI chat designer US6 regression E2E — fixed-bottom input after 50+ messages | P3 | 3h | ✅ | Closed by PR on branch `109-designer-shell-redesign`, test T025 `DesignerShell_InputPinnedAtBottom_AfterManyMessages` in `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — injects 50 synthetic messages via the AI pane's test-only `[JSInvokable]` hook and asserts the input stays within 200px of the viewport bottom. |
| GAP-012 | Invitation expiration background job — mark expired invitations | P2 | 4h | 📋 | Scheduled task to update Pending → Expired where expires_at < now |
| GAP-013 | Invitation org name resolution in list responses | P2 | 2h | 📋 | ListAsync currently returns null for target/source org names |
| GAP-014 | Register invitation integration tests (Tenant → Wallet → Register) | P2 | 8h | 📋 | End-to-end crypto verification against real Wallet Service |
| GAP-015 | Invitation ListAsync N+1 query — pre-load orgs or denormalize source DID | P2 | 2h | 📋 | GetSourceOrgDid fires synchronous query per record |
| GAP-016 | Invitation GetSourceOrgDid fallback format — use consistent DID or null | P3 | 1h | 📋 | Currently returns `org:{guid}` which is not a valid DID |
| GAP-017 | Invitation ListAsync direction parameter validation | P3 | 1h | 📋 | Unrecognised values silently default to "all" |
| GAP-018 | Auto-register participant and auto-link wallet during wallet creation | P1 | 12h | ✅ | Feature 077 — auto-link with VerificationMethod="self-created", fire-and-forget from Wallet Service |
| GAP-019 | Tenant org user admin — PlatformUser provisioning with admin overrides | P1 | 16h | ✅ | Feature 077 — POST /api/platform/users + PUT /api/platform/users/{id}/password, SystemAdmin only |
| GAP-020 | Multi-org ConstructionPermit walkthrough — complete run.ps1 | P2 | 8h | 🚧 | Setup passes (4 orgs, 5 users, register, subscriptions, blueprint). Run blocked on GAP-019 (per-org users can't login without PlatformUser). Branch: `feature/multi-org-construction-permit`. Also needs: rejection scenario fix, New Submission page visibility test. |
| GAP-021 | Agent starting-action kickoff — persona mode | P2 | 10h | ✅ | Feature 110 US1–US4 + R-007 shipped (PRs #371, #372, #373, #374, #375) — `once` + `interval` triggers, JSONC-tolerant loader, coexistence + shutdown proven, ConstructionPermit persona parity, persona fires now land in the agent `*.jsonl` audit stream alongside reactive decisions (decision label `persona-fire`, synthetic ActionId `persona:{name}#{counter}`). 110/110 agent tests green. Remaining non-coding items: T029/T043/T050 live-run validation (operator-verified); T037 reactive-latency benchmark (deferred to walkthrough rather than unit suite); T042 `$schema` stable URL (awaits sorcha.dev publish). |

### From Feature 085 (Stored Data Transactions)

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| GAP-021 | Consolidate duplicate action submission endpoints in Blueprint Service Program.cs | P1 | 8h | 📋 | Two action submission paths: legacy `actionsGroup.MapPost("/")` (line ~849) and `instancesGroup.MapPost("/{instanceId}/actions/{actionId}/execute")` (line ~1724). The file key injection (Feature 085) had to be duplicated in the execute endpoint. Consolidate into a single service method and deprecate/remove the legacy endpoint. |
| GAP-022 | Blueprint disclosure rules for file recipient access | P2 | 8h | 📋 | Currently only the sender's wallet gets a wrapped key in the encrypted payload. Blueprint disclosure rules need to include all participants who should access file attachments. PayloadTests walkthrough downloads as sender only — receiver access requires disclosure config. |
| GAP-023 | Move file chunks from Blueprint Service DB to register transactions | P2 | 16h | 📋 | Chunks are currently stored in Blueprint Service PostgreSQL (IActionStore). Design intended chunks as register transactions flowing through the validator pipeline. Requires: chunk submission to validator, same-docket sealing logic, FileReassemblyService reads from register instead of Blueprint Service. |
| GAP-024 | EF Core migration for FileMetadata nullable TransactionHash + CreatedAt | P1 | 2h | ✅ | PR #201 — consolidated into single InitialCreate migration (20260406203435). |
| GAP-025 | PayloadTests walkthrough: multi-chunk pressure testing (4MB, 10MB, 40MB) | P2 | 4h | 📋 | 1KB smoke test passes. Larger file sizes need testing to verify chunking, multi-session continuity, and download reassembly at scale. |
| GAP-026 | FileReferenceField.razor: wire into actual blueprint action form renderer | P2 | 8h | 📋 | Component exists standalone but not integrated into the Sorcha.UI action form rendering pipeline (needs to detect `format: "file-reference"` fields and render FileReferenceField). |
| GAP-027 | Remove diagnostic logging from Program.cs file key injection | P3 | 1h | 📋 | `[085]` prefixed LogInformation calls added during debugging — clean up or reduce to LogDebug. |

### From Feature 091 (New Submissions Workspace)

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| GAP-028 | Backport idempotent register lookup to walkthroughs | P2 | 4h | 📋 | Apply `Get-SorchaRegisterByName` + explicit `New-SorchaRegisterSubscription` pattern to ConstructionPermit, TradeFinance, PayloadTests, SelfBuildHouse setup scripts so re-runs don't leak orphan registers. Helper already exists in `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1`. HealthDeclaration is the reference implementation. |
| GAP-029 | Required-boolean → yes/no enum pattern in demo blueprints | P3 | 2h | 📋 | Required boolean schema fields are ambiguous: `false` could mean "answered no" or "didn't answer yet". Recommended pattern is `enum: ["Yes", "No"]` for medical/legal yes-no questions. Reserve `boolean` for "I confirm" style fields where the only meaningful answer is true. Update HealthDeclaration demo to use enums for `hasMedicalCondition` / `takingMedication` / `hasAllergies`, with x-rule SHOW conditions adjusted to test against `const "Yes"` instead of `const true`. Document the pattern in the blueprint authoring guide. |
| GAP-030 | Wire FormContext.OnValidationChanged to all control renderers | P3 | 4h | 📋 | Field renderers (TextLine, Numeric, TextArea, Select, Checkbox, DateTime, Choice) currently refresh inline error UI only via OnParametersSet, which depends on the parent re-rendering. They should subscribe to `FormContext.OnValidationChanged` so external validation calls (e.g. wizard `HandleNext`, final `HandleSubmit`) update the UI directly. Works today via Blazor's auto re-render after click handlers, but is fragile — explicit event wiring is more robust. |
| GAP-031 | Validate `wallet` URL parameter on Blueprint Service action endpoints | P1 | 6h | 📋 | `GET /api/actions/{wallet}/{register}/blueprints` and `GET /api/actions/{wallet}/{register}/blueprints/{id}` accept the wallet path segment verbatim. Caller is authenticated via `CanExecuteBlueprints` but any user can supply any wallet address. Need to verify the wallet is one the authenticated user controls (via Wallet Service or org membership lookup) before returning blueprint data. Pre-existing TODO surfaced by PR #210 review. |
| GAP-032 | Targeted GetByBlueprintAndRegisterAsync on IPublishedBlueprintStore | P2 | 4h | 📋 | `GET /api/actions/{w}/{r}/blueprints/{id}` calls `GetByRegisterAsync(register)` which loads and deserialises every published blueprint for the entire register, then filters in memory. The 5-minute output cache mitigates this, but a cold hit on a large register is unnecessarily expensive. Add `Task<PublishedBlueprint?> GetByBlueprintAndRegisterAsync(string registerId, string blueprintId)` returning the latest version. |

---

## Theme 4: Trust & Verification — P2

> **Priority:** P2 (Post-release hardening)
> **Estimated Effort:** 120-160h (research + implementation)
> **Goal:** Strengthen decentralized trust guarantees
> **Reference:** [tasks/deferred-tasks.md](tasks/deferred-tasks.md) (TRUST-1 to TRUST-10)

These are the **Tier 1** trust improvements identified in the transaction architecture review. They close active trust gaps without architectural upheaval.

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| TRUST-001 | Verifiable calculations — Validator re-executes JSON Logic | P2 | 32h | 🔬 Research | Compromised Blueprint Service could submit incorrect values |
| TRUST-002 | Validator-enforced disclosure — verify disclosed fields match rules | P2 | 24h | 🔬 Research | Disclosure currently enforced at app layer only |
| TRUST-003 | Transaction receipts — signed finality proofs | P2 | 16h | ✅ | Feature 079 — receipts generated during docket sealing, signed by Validator, stored in MongoDB, pushed via SignalR |
| TRUST-004 | Merkle inclusion proofs — lightweight offline verification | P2 | 16h | ✅ | Feature 079 — on-demand proof generation, portable verification in Validator.Core, verification bundles |
| TRUST-005 | Revocation & amendment model — supersede/amend transactions | P2 | 24h | ✅ | Feature 079 — TransactionType.Revocation, per-tx revocation with authority check, status endpoint, irrevocable |

> **Tier 2-3** (TRUST-6 through TRUST-10: consensus finality, cross-register references, audit trails, timestamps, key rotation) remain in [deferred-tasks.md](tasks/deferred-tasks.md) for post-release.

---

## Theme 5: Authentication & Identity — P2

> **Priority:** P1-P3 (Production readiness + post-release enhancement)
> **Estimated Effort:** 50-80h
> **Goal:** Enterprise identity integration
> **Feature 054 Status:** Complete (82 tasks). Org admin, OIDC, roles, user mgmt, email verification, social login, admin UI all implemented.
> **Feature 055 Status:** Complete (51 tasks). Passkey/WebAuthn (Fido2NetLib) — org user 2FA registration + login, public user passkey signup + discoverable sign-in, social login (Google/Microsoft/GitHub/Apple), auth method management with last-method guard.
> **Feature 058 Status:** Complete (81 tasks). Platform Organisation Topology — three-tier org model (system admin, public, private), PlatformUser cross-org identity, social login, email/password signup, blueprint-driven org creation, org switching, platform governance (audit, suspension, settings), admin UI.

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| AUTH-001 | Azure AD B2C / OIDC integration for external identity | P2 | 24h | ✅ | Feature 054: Full OIDC with discovery, token exchange, 5 provider shortcuts (Entra, Google, Okta, Apple, Cognito) |
| AUTH-002 | Refresh token rotation (issue new on each refresh) | P2 | 8h | 📋 | Limits replay window |
| AUTH-003 | Cross-tab token synchronization (localStorage events) | P3 | 6h | 📋 | Multi-tab consistency |
| AUTH-004 | Session expiry warning UI (toast with "Extend" button) | P3 | 4h | 📋 | UX improvement |
| AUTH-005 | OIDC integration for participant authentication (PART-1) | P3 | 24h | ✅ | Feature 054: OIDC token exchange, social login (Microsoft, Google, Apple), auto-provisioning on first login |
| AUTH-006 | Transactional email sweep — unified templated pipeline, per-org branding, welcome emails | P1 | 8h | ✅ | Feature 112 — `ITransactionalEmailService` facade, Scriban templates (6 pairs) + snapshot fixtures, `WelcomeEmailDispatcher` one-shot per user, per-org branding on invitations, plaintext-token verification/invitation bugs fixed. SMTP (MailKit) and ACS backends unchanged other than multipart HTML+text. 12 new Tenant Service tests + 6 snapshot-fixture cases. See `specs/112-email-sweep/` and the Tenant Service README. |
| AUTH-007 | Breach password list integration (HaveIBeenPwned API) | P2 | 6h | 📋 | NIST policy implemented but breach list check needs external API integration |
| AUTH-008 | Custom domain DNS verification automation | P2 | 12h | 📋 | Feature 054 supports custom domains but DNS CNAME verification is manual |
| AUTH-009 | Social login provider testing with real credentials | P2 | 8h | 📋 | Feature 054 IdP config tested with mocks; needs real OAuth app credentials for each provider |
| AUTH-010 | Load testing for OIDC token exchange flow | P2 | 8h | 📋 | Token exchange is latency-sensitive; needs production-scale load testing |
| AUTH-011 | PassKey/WebAuthn authentication (Fido2NetLib) — org 2FA + public primary auth | P1 | 40h | ✅ | Feature 055: Org passkey 2FA, public passkey signup/sign-in, social login, method management |
| AUTH-012 | Server-rendered auth pages (Razor Pages in Tenant Service) | P1 | 30h | ✅ | Login, signup, logout, OAuth/OIDC callbacks, email verification, password reset — eliminates WASM download for unauth users |

---

## Theme 6: P2P Network & Consensus — P3

> **Priority:** P3 (Future release)
> **Estimated Effort:** 120-200h
> **Goal:** Decentralized multi-validator production network
>
> **Cross-node verification status (Feature 106 + 107):** subsumed by the
> AssuredIdentity cross-peer smoke. Baseline findings live at
> `walkthroughs/AssuredIdentity/multi-peer-findings.md`; regression checks
> rerun via `walkthroughs/AssuredIdentity/run-multi-peer.ps1`. Per FR-039
> the smoke is measurement, not a gate — first operator run on real
> hardware replaces the committed baseline.

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| P2P-001 | Transaction processing loop in Peer Service (PEER-1) | P3 | 12h | 📋 | Deferred from Sprint 4 |
| P2P-002 | Transaction distribution via gossip protocol (PEER-2) | P3 | 10h | 📋 | P2P gossip |
| P2P-003 | gRPC streaming communication (PEER-3) | P3 | 8h | 📋 | Bidirectional streaming |
| P2P-009 | Relay-aware communication for NAT'd peers (Feature 060) | P2 | 20h | ✅ | RelayCommunicationService, RelayMessageHandler, relay batch sync, periodic poll, semaphore guards — PR #65 |
| P2P-004 | BLS12-381 threshold coordination for distributed docket signing | P3 | 24h | 📋 | t-of-n validation |
| P2P-005 | Fork detection in Validator Service | P3 | 16h | 📋 | Chain fork handling |
| P2P-006 | Decentralized consensus / leader election | P3 | 32h | 📋 | Beyond simple quorum |
| P2P-007 | Enclave support for Validator (trusted execution) | P3 | 24h | 📋 | SGX/TDX integration |
| P2P-008 | Multi-validator coordination and synchronization | P3 | 20h | 📋 | Production consensus |

---

## Theme 7: Public User Experience & Role Model — P1

> **Priority:** P1 (Pre-release)
> **Estimated Effort:** 40-60h
> **Goal:** Correct public user experience, role model clarity, register scoping
> **Related:** Exploratory testing session 2026-03-23, PRs #111, Issues #112, #113

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| UX-001 | Register subscription scoping — org-based register access + UI consolidation | P1 | 24h | 📋 | #113 — New Submission shows only subscribed registers; merge Available Registers into Registers page |
| UX-002 | Rename UserRole.Member → UserRole.Consumer across codebase | P1 | 8h | 📋 | #112 — Enum, DB migration, JWT claims, docs, permission presets |
| UX-003 | Blueprint register filter + role-based nav/page auth | P1 | 8h | ✅ | #111 — Fixed register filter, nav gating, dashboard scoping, page-level [Authorize(Roles)] |
| UX-004 | Auditor role access review — determine read-only access scope | P2 | 4h | 📋 | Auditors currently have no nav items for Registers/Participants; decide if read-only access needed |
| UX-005 | Dashboard org-scoped stats for multi-tenant deployments | P2 | 8h | 📋 | Currently global counts; need per-org stats for Consumer/Auditor users |
| UX-006 | Public block explorer — unauthenticated register/transaction browsing | P2 | 16h | 📋 | Future feature — shares layout with authenticated app |
| UX-007 | Public user page load hits Administrator/SystemAdmin-only endpoint | P2 | 4h | 📋 | Observed 2026-04-14 on n1 — browser console shows `Authorization failed. RolesAuthorizationRequirement:User.IsInRole must be true for one of the following roles: (Administrator|SystemAdmin)` during a Consumer's first page load. Some page-load call in the Blazor client is reaching an admin-only endpoint that a public-org Consumer role should never touch. Identify the culprit (likely a shared layout service that fans out to admin-scoped APIs without a role guard), gate the call behind a role check, and silence the console warning. Not blocking but pollutes the log and points at a latent authorization boundary bug. |

---

## Theme 8: Mobile App Prerequisites — P1

> **Priority:** P1 (Enables SorchaMobile project)
> **Estimated Effort:** 60-80h
> **Goal:** Make Sorcha packages consumable by .NET MAUI mobile app, add device-aware capabilities
> **Related:** SorchaMobile project (C:\projects\SorchaMobile — separate repo)

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| MOB-001 | Multi-target Tier 1 packages to net10.0;net8.0 | P1 | 8h | ❌ | Eliminated -- SorchaMobile confirmed .NET 10, no multi-targeting needed. |
| MOB-002 | Extract Sorcha.Wallet.Portable from Wallet.Core | P1 | 16h | ✅ | Feature 084 -- Sorcha.Wallet.Portable extracted with entities, enums, derivation. |
| MOB-003 | NuGet packaging pipeline (GitHub Packages or private feed) | P1 | 8h | ✅ | Feature 084 -- GitHub Actions workflow for 9 NuGet packages. |
| MOB-004 | ServiceClients REST-only variant | P1 | 4h | ✅ | Feature 084 -- Sorcha.ServiceClients.Http with HTTP-only clients + SignalR. |
| MOB-005 | Blueprint schema: device input field types | P2 | 8h | 📋 | New field types in Blueprint.Models for camera/photo, GPS location, document scan. Shared between web and mobile — web renders file upload fallbacks, mobile bridges to native device APIs. |
| MOB-006 | Blazor UI form controls for device input fields | P2 | 12h | 📋 | MudBlazor components in Sorcha.UI.Core for MOB-005 field types. On web: file upload / manual entry. On Blazor Hybrid (mobile): bridge to MAUI native APIs (camera, GPS). |
| MOB-007 | Tenant Service: org branding configuration | P1 | 8h | 📋 | Brand colours, logo URL, font family, display name on Organization entity. GET endpoint to fetch branding by org ID. Supports runtime white-labelling in mobile app. |
| MOB-008 | VC exchange protocol definition (QR / BLE / NFC) | P2 | 12h | 📋 | Define shared models and protocol for credential presentation/handover between devices. QR code (MVP), BLE proximity, NFC tap-to-verify. Protocol defined in shared models, implemented per-platform. |
| MOB-009 | Co-signed dual-key (2-of-2 multisig) wallet for field collection | P3 (v2) | 60-100h | 📋 | **Deferred to v2 backlog 2026-05-10.** Collector holds Key A on-device (PWA self-custody), employing org holds Key B (Wallet Service custodial). Both signatures required to submit. Targets regulated evidence collection, two-person integrity, organisationally-bonded fieldwork. Slots into existing primitives: `CustodyMode.CoSigned` placeholder (Feature 083), validator `RequiredSignatures` precedent (Feature 086), `IAuthChallengeService` for org-side step-up (Feature 116), transaction receipts (Feature 079). Scope: naive 2-ED25519-attached crypto (vs. threshold FROST/MuSig2 later), org policy engine (roster + action allow-list + time window + geofence — the org's co-sign is itself a policy decision point), offline IndexedDB outbox + async org co-sign, Key A rebind recovery preserving past-signature verifiability, validator-side multi-sig aggregation check, pending-co-sign UX. Full design: `docs/superpowers/specs/2026-05-10-user-agent-unification-design.md` → "Co-signed data collection (v2 — backlog)". **Trigger to revisit:** when field data collection becomes a near-term product driver, OR after user-agent unification v1 (managed + self-custody) ships and the `IUserSigner` seam is stable. |

---

## Summary

| Theme | Priority | Tasks | Effort | Focus |
|-------|----------|-------|--------|-------|
| 1. Security Hardening | P0 | 10 (5 ✅, 5 remaining) | 80-100h | Release blocker — SEC-012 CodeQL fixes done; SEC-013 HAIP auth closed (PR #378); SEC-015 org-name validation spun out from Feature 112 review |
| 2. Production Infrastructure | P1 | 10 (1 ✅, 9 remaining) | 80-120h | Deployment readiness |
| 3. Deferred Feature Gaps | P1-P2 | 17 (4 ✅, 13 remaining) | 21-41h | Close MVD gaps — GAP-005/011/018/019 done (075, 063, 077); GAP-011b E2E spun out as 3h P3 |
| 4. Trust & Verification | P2 | 5 | 120-160h | Trust hardening |
| 5. Authentication & Identity | P1-P3 | 11 (4 ✅, 7 remaining) | 50-80h | Enterprise identity — OIDC/org admin/social login (054); passkey/WebAuthn (055); platform org topology (058); transactional email sweep (112) |
| 6. P2P Network & Consensus | P3 | 9 (1 ✅, 8 remaining) | 120-200h | Decentralization — relay comms done (060) |
| 7. Public User Experience | P1 | 6 (1 ✅, 5 remaining) | 40-60h | Role model, register scoping, public UX |
| 8. Mobile App Prerequisites | P1 | 8 (3 ✅, 1 ❌, 4 remaining) | 60-80h | Package portability, device inputs, white-label branding — Feature 084 done (MOB-002/003/004), MOB-001 eliminated |
| **Total** | | **75** (17 ✅, 1 ❌, 57 remaining) | **565-825h** | |

### Completed Features (not in themes above)

| Feature | Status | Description |
|---------|--------|-------------|
| Feature 085 | 🚧 | **Stored Data Transactions** — Chunked encrypted file upload via Blueprint Service (`POST /api/file-chunks`), decrypted file download via Wallet Service (`GET /api/v1/wallets/{address}/files/download`), FLE-compatible file field type in blueprint schemas, validator enforcement of file field encryption rules. E2E validation and some integration tests pending. |
| Feature 060 | 🚧 | **Wallet Recovery** — RecoveryKeyWrap, RecoveryAuditLog entities, RecoveryPathType enum, IRecoveryKeyService/RecoveryKeyService (AES-256-GCM key gen, asymmetric wrap/unwrap), PasskeyRecoveryService, OrgRecoveryService with delegation revocation, PasskeyServiceClient, OrgRecoveryConfig in Tenant Service with POST/GET endpoints, recovery endpoints (recover/passkey, recover/org, delegations/preserve, recovery-status), automatic recovery key generation on wallet creation, API Gateway routes for /api/organizations. 28 unit tests. |
| Feature 062 | ✅ | **Pending Action Notifications** — NotificationConfig on Action model, SummaryTemplateRenderer, UrgencyCalculator, EventsHubNotificationBridge enrichment with ActivityEvent persistence, PendingActionToast/PendingActionInbox UI components, GET /api/actions/pending + /count endpoints, PendingActionService HTTP client, TenantNotificationPreferenceProvider (Wallet Service), notification delivery preferences UI, notification history inbox. Unit tests for SummaryTemplateRenderer, UrgencyCalculator, TenantNotificationPreferenceProvider. |
| Feature 064 | ✅ | **Transaction Explorer UX Overhaul** — DAG visualization with lightweight graph endpoint (GET /api/registers/{registerId}/transactions/graph), TransactionGraphResponse model (nodes with TxId, PrevTxId, SenderWallet, TimeStamp, DocketNumber, BlueprintId, InstanceId, TransactionType), cursor-based pagination (limit/before), totalCount and hasMore support. |
| Feature 065 | ✅ | **Register Invitations** — Private register invitation system with cryptographic envelope (ED25519 sign + X25519 encrypt via Wallet Service). 4 Minimal API endpoints (create/accept/list/revoke), Organization DID support (`did:sorcha:org:{address}`), nonce replay protection with unique DB index, rate limiting (50 pending/10 per hour), InvitationNonce + RegisterInvitationRecord entities, EF Core migration, API Gateway YARP routes, `SubscriptionType.Invited`. 19 unit tests. |
| Feature 075 | ✅ | **FLE Completion & Crypto Progress UX** — Per-recipient SignalR events from encryption pipeline (RecipientEncryptionNotification), floating CryptoProgressPopover UI (expanded/minimised/dismissed), EncryptionOperationTracker scoped service, DevMode unit tests (initiation, toggle, plaintext path), FLE disclosure group tests (grouping, key resolution, atomic failure), actionable error feedback with retry, DisplayName resolution from blueprint participants. 35+ new tests. |
| Feature 077 | ✅ | **Auto-Register Participant & PlatformUser Provisioning** — Auto-link wallet during creation (ParticipantService.AutoLinkWalletAsync, VerificationMethod="self-created", fire-and-forget from Wallet Service), admin user provisioning (POST /api/platform/users creates PlatformUser + UserIdentity + OrgMembership atomically), admin password reset (PUT /api/platform/users/{id}/password with NIST policy). 20+ tests. |
| Feature 082 | ✅ | **Cloud KMS Key Management (SEC-002)** — Envelope encryption model: DEKs wrapped by `IKeyProtectionProvider`. `AzureKeyVaultKeyProtectionProvider` wraps/unwraps DEKs via AKV. `AzureSigningProvider` for KMS-resident keys (ED25519/P-256). `WalletKeyManagementOptions.SigningPolicy` controls default signing mode and migration. DEK cache with TTL + outage grace period. `Sorcha.Wallet.Providers.Azure` package. AWS/GCP providers deferred. |
| Feature 092 | 🚧 | **Consumer Persona & Nav Tidy** — `PlatformUserPersona` (ciphertext in Tenant DB, content key derived by Wallet Service under `sorcha:persona-vault`, XChaCha20-Poly1305 AEAD). `/me/persona` GET/PUT/DELETE endpoints. `PersonaAutofillResolver` (hybrid `x-persona` extension + conservative inference allowlist), `SorchaFormRenderer` cream-tint autofill with `self` provenance tick, `PersonaFillSummary` disclosure banner with Review/Clear all/Fill-from-profile. `MyProfile.razor` page for identity management (5-entry caps per list, default selection). Drawer "Navigation" header removed, Settings/Notifications side-nav entries removed and merged into Settings tabs + avatar-menu shortcut. 25 unit/component tests + 18 resolver tests. Deferred: Tenant-side endpoint unit/integration tests (T044/T045), Playwright E2E (T068/T073/T074). |
| Feature 079 | ✅ | **Trust Hardening: Receipts, Proofs & Revocation (TRUST-3/4/5)** — Transaction receipts (signed finality proofs generated at docket sealing, stored in MongoDB, pushed via SignalR), Merkle inclusion proofs (on-demand generation, positional verification, portable in Validator.Core), revocation transactions (TransactionType.Revocation, authority check via original signer or governance roster, status endpoint), verification bundles (portable offline verification with 4-check pipeline), transaction lifecycle ticks (WhatsApp-style grey/blue/double-blue delivery indicators, WalletTransaction entity with outbound/inbound tracking, Redis event bridge). 97+ tests. |

### Release Gating

**First Release (v1.0)** requires:
- Theme 1 (Security Hardening) — all P0 items
- Theme 2 (Production Infrastructure) — P1 items (OPS-001 through OPS-005)
- Theme 3 (Deferred Feature Gaps) — assess and close or formally defer

**Post-Release (v1.1+):**
- Themes 4-6 and remaining P2/P3 items

---

**Version:** 7.12
**Last Updated:** 2026-04-24
**Document Owner:** Sorcha Architecture Team
