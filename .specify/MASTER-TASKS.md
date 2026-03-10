# Sorcha Platform - Master Task List

> **Archived phases:** See [MASTER-TASKS-ARCHIVE.md](MASTER-TASKS-ARCHIVE.md) for all completed features and phases.
> **Deferred research:** See [tasks/deferred-tasks.md](tasks/deferred-tasks.md) for long-term research items (TRUST-1 to TRUST-10, governance enhancements, advanced features).

**Version:** 7.2
**Last Updated:** 2026-03-10
**Status:** MVD Complete — Preparing for First Release
**Related:** [MASTER-PLAN.md](MASTER-PLAN.md) | [development-status.md](../docs/reference/development-status.md)

> **Maintenance Rule:** This file MUST be updated as part of every PR. When a task is completed, mark it ✅ and update the summary counts. When new work is identified, add it to the appropriate theme. Completed tasks stay in place (marked ✅) until the next archive sweep. Do not let this file go stale — it is the single source of truth for remaining work.

---

## Overview

The Sorcha platform is **100% MVD feature-complete**. All core features (045-053), production packaging (Phases A-E), and code quality work have been completed and archived.

This document now tracks **remaining work for the first production release**, organized by development theme.

**Completed (archived):** 523 tasks across 13 features/phases + 82 tasks from Feature 054 + 51 tasks from Feature 055
**Remaining:** 62 tasks across 6 themes
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
| GAP-005 | T051 EncryptionProgress SignalR integration test | P2 | 4h | 📋 | |
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
| TRUST-003 | Transaction receipts — signed finality proofs | P2 | 16h | 🔬 Research | Submitter has no cryptographic proof of inclusion |
| TRUST-004 | Merkle inclusion proofs — lightweight offline verification | P2 | 16h | 🔬 Research | Currently requires fetching entire docket |
| TRUST-005 | Revocation & amendment model — supersede/amend transactions | P2 | 24h | 🔬 Research | No structural mechanism for on-chain correction |

> **Tier 2-3** (TRUST-6 through TRUST-10: consensus finality, cross-register references, audit trails, timestamps, key rotation) remain in [deferred-tasks.md](tasks/deferred-tasks.md) for post-release.

---

## Theme 5: Authentication & Identity — P2

> **Priority:** P1-P3 (Production readiness + post-release enhancement)
> **Estimated Effort:** 50-80h
> **Goal:** Enterprise identity integration
> **Feature 054 Status:** Complete (82 tasks). Org admin, OIDC, roles, user mgmt, email verification, social login, admin UI all implemented.
> **Feature 055 Status:** Complete (51 tasks). Passkey/WebAuthn (Fido2NetLib) — org user 2FA registration + login, public user passkey signup + discoverable sign-in, social login (Google/Microsoft/GitHub/Apple), auth method management with last-method guard.

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
| P2P-004 | BLS12-381 threshold coordination for distributed docket signing | P3 | 24h | 📋 | t-of-n validation |
| P2P-005 | Fork detection in Validator Service | P3 | 16h | 📋 | Chain fork handling |
| P2P-006 | Decentralized consensus / leader election | P3 | 32h | 📋 | Beyond simple quorum |
| P2P-007 | Enclave support for Validator (trusted execution) | P3 | 24h | 📋 | SGX/TDX integration |
| P2P-008 | Multi-validator coordination and synchronization | P3 | 20h | 📋 | Production consensus |

---

## Summary

| Theme | Priority | Tasks | Effort | Focus |
|-------|----------|-------|--------|-------|
| 1. Security Hardening | P0 | 7 | 80-100h | Release blocker |
| 2. Production Infrastructure | P1 | 9 | 80-120h | Deployment readiness |
| 3. Deferred Feature Gaps | P1-P2 | 10 | 40-60h | Close MVD gaps |
| 4. Trust & Verification | P2 | 5 | 120-160h | Trust hardening |
| 5. Authentication & Identity | P1-P3 | 11 (3 ✅, 8 remaining) | 50-80h | Enterprise identity — OIDC, org admin, social login done (054); passkey/WebAuthn done (055) |
| 6. P2P Network & Consensus | P3 | 8 | 120-200h | Decentralization |
| **Total** | | **50** (3 ✅, 47 remaining) | **500-720h** | |

### Release Gating

**First Release (v1.0)** requires:
- Theme 1 (Security Hardening) — all P0 items
- Theme 2 (Production Infrastructure) — P1 items (OPS-001 through OPS-005)
- Theme 3 (Deferred Feature Gaps) — assess and close or formally defer

**Post-Release (v1.1+):**
- Themes 4-6 and remaining P2/P3 items

---

**Version:** 7.2
**Last Updated:** 2026-03-10
**Document Owner:** Sorcha Architecture Team
