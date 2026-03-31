# Sorcha Platform - Master Task List

> **Archived phases:** See [MASTER-TASKS-ARCHIVE.md](MASTER-TASKS-ARCHIVE.md) for all completed features and phases.
> **Deferred research:** See [tasks/deferred-tasks.md](tasks/deferred-tasks.md) for long-term research items (TRUST-1 to TRUST-10, governance enhancements, advanced features).

**Version:** 7.7
**Last Updated:** 2026-03-31
**Status:** MVD Complete — Preparing for First Release
**Related:** [MASTER-PLAN.md](MASTER-PLAN.md) | [development-status.md](../docs/reference/development-status.md)

> **Maintenance Rule:** This file MUST be updated as part of every PR. When a task is completed, mark it ✅ and update the summary counts. When new work is identified, add it to the appropriate theme. Completed tasks stay in place (marked ✅) until the next archive sweep. Do not let this file go stale — it is the single source of truth for remaining work.

---

## Overview

The Sorcha platform is **100% MVD feature-complete**. All core features (045-053), production packaging (Phases A-E), and code quality work have been completed and archived.

This document now tracks **remaining work for the first production release**, organized by development theme.

**Completed (archived):** 523 tasks across 13 features/phases + 82 tasks from Feature 054 + 51 tasks from Feature 055 + 81 tasks from Feature 058 + 38 tasks from Feature 060 + Feature 062 (Pending Action Notifications)
**Remaining:** 65 tasks across 7 themes (TRUST-3/4/5 completed in Feature 079)
**Deferred (post-release):** 43 research/future items in [deferred-tasks.md](tasks/deferred-tasks.md)

---

## Theme 1: Security Hardening — P0

> **Priority:** P0 (Release Blocker)
> **Estimated Effort:** 80-100h
> **Goal:** Production-grade security posture

| # | Task | Priority | Effort | Status | Notes |
|---|------|----------|--------|--------|-------|
| SEC-001 | HTTPS enforcement across all services (Kestrel TLS, cert management) | P0 | 12h | 📋 | Currently HTTP in Docker; HTTPS only in Aspire dev |
| SEC-002 | Azure Key Vault integration for Wallet Service key storage | P0 | 16h | 📋 | Keys currently stored in-memory or local EF Core |
| SEC-003 | Input validation hardening (request size limits, field validation) | P0 | 12h | 📋 | Partial — some endpoints lack validation |
| SEC-004 | Security audit (OWASP Top 10 review, penetration testing) | P0 | 24h | 📋 | Pre-release requirement |
| SEC-005 | Secret management review (connection strings, JWT keys, API keys) | P0 | 8h | 📋 | Ensure no hardcoded secrets in deployed configs |
| SEC-006 | CORS policy review and hardening | P0 | 4h | 📋 | Currently permissive for development |
| SEC-007 | Rate limiting tuning (current: 7 write routes via YARP) | P1 | 4h | 📋 | Review limits for production load |
| SEC-008 | Wallet sign/decrypt delegate access — extend ownership check with DelegationService | P1 | 4h | 📋 | PR #170 added owner-only check; needs DelegationService.HasAccessAsync for delegated wallets (SignOnly for /sign, ReadOnly for /decrypt, etc.). Pattern exists on wallet detail endpoint (line 518). |
| SEC-009 | Participant publishing wallet-link verification | P1 | 4h | 📋 | ParticipantPublishingService should verify signer wallet is linked to the participant being published. Currently accepts any wallet as signer. |
| SEC-010 | Peer replication participant record re-validation | P2 | 8h | 📋 | Re-validate participant records during peer sync (verify signatures, check conflicts with existing records). Currently accepted verbatim. |

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
| GAP-011 | AI Blueprint Builder Enhancement (063) | P2 | 40h | 🚧 | Schema library (26 schemas), 5 new AI tools, VC/DPP support, consultative prompt, chat UI fixes |
| GAP-012 | Invitation expiration background job — mark expired invitations | P2 | 4h | 📋 | Scheduled task to update Pending → Expired where expires_at < now |
| GAP-013 | Invitation org name resolution in list responses | P2 | 2h | 📋 | ListAsync currently returns null for target/source org names |
| GAP-014 | Register invitation integration tests (Tenant → Wallet → Register) | P2 | 8h | 📋 | End-to-end crypto verification against real Wallet Service |
| GAP-015 | Invitation ListAsync N+1 query — pre-load orgs or denormalize source DID | P2 | 2h | 📋 | GetSourceOrgDid fires synchronous query per record |
| GAP-016 | Invitation GetSourceOrgDid fallback format — use consistent DID or null | P3 | 1h | 📋 | Currently returns `org:{guid}` which is not a valid DID |
| GAP-017 | Invitation ListAsync direction parameter validation | P3 | 1h | 📋 | Unrecognised values silently default to "all" |
| GAP-018 | Auto-register participant and auto-link wallet during wallet creation | P1 | 12h | ✅ | Feature 077 — auto-link with VerificationMethod="self-created", fire-and-forget from Wallet Service |
| GAP-019 | Tenant org user admin — PlatformUser provisioning with admin overrides | P1 | 16h | ✅ | Feature 077 — POST /api/platform/users + PUT /api/platform/users/{id}/password, SystemAdmin only |
| GAP-020 | Multi-org ConstructionPermit walkthrough — complete run.ps1 | P2 | 8h | 🚧 | Setup passes (4 orgs, 5 users, register, subscriptions, blueprint). Run blocked on GAP-019 (per-org users can't login without PlatformUser). Branch: `feature/multi-org-construction-permit`. Also needs: rejection scenario fix, New Submission page visibility test. |

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
| AUTH-006 | Production SMTP configuration (replace MailKit stub) | P1 | 8h | 📋 | Feature 054 uses stub email sender; needs real SMTP/SendGrid for email verification |
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

---

## Summary

| Theme | Priority | Tasks | Effort | Focus |
|-------|----------|-------|--------|-------|
| 1. Security Hardening | P0 | 7 | 80-100h | Release blocker |
| 2. Production Infrastructure | P1 | 10 (1 ✅, 9 remaining) | 80-120h | Deployment readiness |
| 3. Deferred Feature Gaps | P1-P2 | 16 (3 ✅, 13 remaining) | 58-78h | Close MVD gaps — GAP-005/018/019 done (075, 077) |
| 4. Trust & Verification | P2 | 5 | 120-160h | Trust hardening |
| 5. Authentication & Identity | P1-P3 | 11 (3 ✅, 8 remaining) | 50-80h | Enterprise identity — OIDC, org admin, social login done (054); passkey/WebAuthn done (055); platform org topology done (058) |
| 6. P2P Network & Consensus | P3 | 9 (1 ✅, 8 remaining) | 120-200h | Decentralization — relay comms done (060) |
| 7. Public User Experience | P1 | 6 (1 ✅, 5 remaining) | 40-60h | Role model, register scoping, public UX |
| **Total** | | **64** (9 ✅, 55 remaining) | **538-778h** | |

### Completed Features (not in themes above)

| Feature | Status | Description |
|---------|--------|-------------|
| Feature 060 | 🚧 | **Wallet Recovery** — RecoveryKeyWrap, RecoveryAuditLog entities, RecoveryPathType enum, IRecoveryKeyService/RecoveryKeyService (AES-256-GCM key gen, asymmetric wrap/unwrap), PasskeyRecoveryService, OrgRecoveryService with delegation revocation, PasskeyServiceClient, OrgRecoveryConfig in Tenant Service with POST/GET endpoints, recovery endpoints (recover/passkey, recover/org, delegations/preserve, recovery-status), automatic recovery key generation on wallet creation, API Gateway routes for /api/organizations. 28 unit tests. |
| Feature 062 | ✅ | **Pending Action Notifications** — NotificationConfig on Action model, SummaryTemplateRenderer, UrgencyCalculator, EventsHubNotificationBridge enrichment with ActivityEvent persistence, PendingActionToast/PendingActionInbox UI components, GET /api/actions/pending + /count endpoints, PendingActionService HTTP client, TenantNotificationPreferenceProvider (Wallet Service), notification delivery preferences UI, notification history inbox. Unit tests for SummaryTemplateRenderer, UrgencyCalculator, TenantNotificationPreferenceProvider. |
| Feature 064 | ✅ | **Transaction Explorer UX Overhaul** — DAG visualization with lightweight graph endpoint (GET /api/registers/{registerId}/transactions/graph), TransactionGraphResponse model (nodes with TxId, PrevTxId, SenderWallet, TimeStamp, DocketNumber, BlueprintId, InstanceId, TransactionType), cursor-based pagination (limit/before), totalCount and hasMore support. |
| Feature 065 | ✅ | **Register Invitations** — Private register invitation system with cryptographic envelope (ED25519 sign + X25519 encrypt via Wallet Service). 4 Minimal API endpoints (create/accept/list/revoke), Organization DID support (`did:sorcha:org:{address}`), nonce replay protection with unique DB index, rate limiting (50 pending/10 per hour), InvitationNonce + RegisterInvitationRecord entities, EF Core migration, API Gateway YARP routes, `SubscriptionType.Invited`. 19 unit tests. |
| Feature 075 | ✅ | **FLE Completion & Crypto Progress UX** — Per-recipient SignalR events from encryption pipeline (RecipientEncryptionNotification), floating CryptoProgressPopover UI (expanded/minimised/dismissed), EncryptionOperationTracker scoped service, DevMode unit tests (initiation, toggle, plaintext path), FLE disclosure group tests (grouping, key resolution, atomic failure), actionable error feedback with retry, DisplayName resolution from blueprint participants. 35+ new tests. |
| Feature 077 | ✅ | **Auto-Register Participant & PlatformUser Provisioning** — Auto-link wallet during creation (ParticipantService.AutoLinkWalletAsync, VerificationMethod="self-created", fire-and-forget from Wallet Service), admin user provisioning (POST /api/platform/users creates PlatformUser + UserIdentity + OrgMembership atomically), admin password reset (PUT /api/platform/users/{id}/password with NIST policy). 20+ tests. |
| Feature 079 | ✅ | **Trust Hardening: Receipts, Proofs & Revocation (TRUST-3/4/5)** — Transaction receipts (signed finality proofs generated at docket sealing, stored in MongoDB, pushed via SignalR), Merkle inclusion proofs (on-demand generation, positional verification, portable in Validator.Core), revocation transactions (TransactionType.Revocation, authority check via original signer or governance roster, status endpoint), verification bundles (portable offline verification with 4-check pipeline), transaction lifecycle ticks (WhatsApp-style grey/blue/double-blue delivery indicators, WalletTransaction entity with outbound/inbound tracking, Redis event bridge). 97+ tests. |

### Release Gating

**First Release (v1.0)** requires:
- Theme 1 (Security Hardening) — all P0 items
- Theme 2 (Production Infrastructure) — P1 items (OPS-001 through OPS-005)
- Theme 3 (Deferred Feature Gaps) — assess and close or formally defer

**Post-Release (v1.1+):**
- Themes 4-6 and remaining P2/P3 items

---

**Version:** 7.7
**Last Updated:** 2026-03-31
**Document Owner:** Sorcha Architecture Team
