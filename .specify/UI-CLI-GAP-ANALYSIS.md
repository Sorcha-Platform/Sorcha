# UI & CLI Gap Analysis — Backend Features Without Admin Tooling

**Created:** 2026-03-04
**Purpose:** Track backend API features that have no UI or CLI coverage, leaving administrators unable to configure or monitor them.
**Backend Endpoints:** ~135 REST + gRPC | **UI Pages:** ~35 routes | **CLI Commands:** 65+ subcommands

---

## Summary

| Category | Backend Endpoints | UI Coverage | CLI Coverage | Gap |
|----------|:-:|:-:|:-:|:-:|
| Register Policy (048) | 3 | ❌ → 🔧 049 | ❌ → 🔧 049 | **Feature 049** |
| System Register (048) | 4 | ❌ → 🔧 049 | ❌ → 🔧 049 | **Feature 049** |
| Validator Admin | 5 | ❌ → 🔧 049 | ❌ → 🔧 049 | **Feature 049** |
| Validator Metrics | 6 | ❌ → 🔧 049 | ❌ → 🔧 049 | **Feature 049** |
| Validator Consent (048) | 4 | ❌ → 🔧 049 | ❌ → 🔧 049 | **Feature 049** |
| Threshold Signing (BLS) | 3 | ❌ → 🔧 049 | ❌ → 🔧 049 | **Feature 049** |
| Wallet Delegation | 4 | ❌ | ❌ | **Full gap** |
| Credential Lifecycle | 4 | ❌ | partial | **UI gap** |
| Status List Management | 3 | ❌ | ❌ | **Full gap** |
| Presentation Requests (OID4VP) | 5 | placeholder | partial | **UI gap** |
| Schema Provider Admin | 2 | placeholder | ❌ | **Partial gap** |
| Service Principal CRUD | 8 | read-only → 🔧 049 | ✅ | **Feature 049** |
| Participant Publishing | 3 | ❌ | ❌ | **Full gap** |
| Participant Suspend/Reactivate | 2 | ❌ | ❌ | **Full gap** |
| Push Notifications | 3 | ❌ | ❌ | **Full gap** |
| Gateway Alerts & Stats | 3 | ❌ | partial | **UI gap** |
| Peer Network Management | gRPC | placeholder | ✅ | **UI gap** |
| Events Admin | 2 | ❌ | ❌ | **Full gap** |
| Encrypted Payload Operations | 1 | ❌ | ❌ | **Full gap** |

**Total: ~65 endpoints with no admin tooling**

---

## Tier 1 — Critical Admin Gaps (System cannot be configured)

These features are core to running the platform but have zero admin surface.

### 1. Register Policy Management (Feature 048)
**Backend:** Register Service
**Endpoints:**
- `GET /api/registers/{registerId}/policy` — Get effective policy
- `GET /api/registers/{registerId}/policy/history` — Policy version history
- `POST /api/registers/{registerId}/policy/update` — Propose policy update

**UI Need:** Policy viewer/editor on Register detail page (governance tab)
**CLI Need:** `register policy get|history|update` subcommands
**Why critical:** Administrators cannot view or modify consensus rules, replication policy, or TTL settings on any register.

---

### 2. System Register (Feature 048)
**Backend:** Register Service
**Endpoints:**
- `GET /api/system-register` — System register status
- `GET /api/system-register/blueprints` — List disseminated blueprints
- `GET /api/system-register/blueprints/{id}` — Get specific blueprint
- `GET /api/system-register/blueprints/{id}/versions/{v}` — Get blueprint version

**UI Need:** Dedicated System Register page under Admin showing status + blueprint catalog
**CLI Need:** `register system status|blueprints` subcommands
**Why critical:** The system register is the root of trust — admins need visibility into what's published and its health.

---

### 3. Validator Administration
**Backend:** Validator Service
**Endpoints:**
- `POST /api/admin/validators/start` — Start validator
- `POST /api/admin/validators/stop` — Stop validator
- `GET /api/admin/validators/{registerId}/status` — Validator status
- `POST /api/admin/validators/{registerId}/process` — Manual pipeline trigger
- `GET /api/admin/validators/monitoring` — Monitored registers

**UI Need:** Validator admin page (currently placeholder at `/admin/validator`)
**CLI Need:** `validator start|stop|status|process|monitoring` subcommands
**Why critical:** No way to start/stop validation, trigger manual processing, or see what registers are being monitored.

---

### 4. Validator Consent Mode (Feature 048)
**Backend:** Validator Service + Register Service
**Endpoints:**
- `POST /api/validators/register` — Register as validator
- `GET /api/validators/{registerId}/pending` — List pending validators
- `POST /api/validators/{registerId}/{validatorId}/approve` — Approve validator
- `POST /api/validators/{registerId}/{validatorId}/reject` — Reject validator
- `GET /api/registers/{registerId}/validators/approved` — Approved list
- `POST /api/validators/{registerId}/refresh` — Refresh from chain

**UI Need:** Validator approval queue on Register detail page or Admin
**CLI Need:** `validator pending|approve|reject|refresh` subcommands
**Why critical:** Consent-mode registers require admin approval of validators — currently impossible without raw API calls.

---

### 5. Service Principal Full CRUD
**Backend:** Tenant Service
**Endpoints:** 8 endpoints (create, list, get, update scopes, suspend, reactivate, delete, rotate secret)
**UI Current:** Read-only list of 5 hardcoded entries with "coming soon" banner
**CLI Current:** ✅ Full CRUD

**UI Need:** Replace placeholder with real CRUD — create, edit scopes, suspend/reactivate, rotate secret, delete
**Why critical:** Service-to-service auth is managed through service principals. Admin UI users cannot manage them.

---

## Tier 2 — Important Operational Gaps (System runs but can't be monitored)

### 6. Validator Metrics & Observability
**Backend:** Validator Service
**Endpoints:**
- `GET /api/metrics` — Aggregated metrics
- `GET /api/metrics/validation` — Validation engine metrics
- `GET /api/metrics/consensus` — Consensus metrics
- `GET /api/metrics/pools` — Memory pool metrics
- `GET /api/metrics/caches` — Cache metrics
- `GET /api/metrics/config` — Current configuration

**UI Need:** Metrics dashboard on Validator admin page (charts, gauges)
**CLI Need:** `validator metrics [validation|consensus|pools|caches|config]`
**Impact:** No visibility into validation performance, mempool pressure, or cache hit rates.

---

### 7. Wallet Delegation & Access Control
**Backend:** Wallet Service
**Endpoints:**
- `POST /api/v1/wallets/{address}/access` — Grant access
- `GET /api/v1/wallets/{address}/access` — List grants
- `DELETE /api/v1/wallets/{address}/access/{subject}` — Revoke access
- `GET /api/v1/wallets/{address}/access/{subject}/check` — Check access

**UI Need:** "Access" tab on Wallet detail page
**CLI Need:** `wallet access grant|list|revoke|check`
**Impact:** Multi-user wallet access is backend-only. No way for wallet owners to delegate or audit access.

---

### 8. Credential Lifecycle Management
**Backend:** Blueprint Service
**Endpoints:**
- `POST /api/v1/credentials/{id}/revoke` — Revoke
- `POST /api/v1/credentials/{id}/suspend` — Suspend
- `POST /api/v1/credentials/{id}/reinstate` — Reinstate
- `POST /api/v1/credentials/{id}/refresh` — Reissue expired

**UI Need:** Credential actions (revoke/suspend/reinstate) on credential detail view
**CLI Current:** `credential revoke` exists, others missing
**Impact:** Issuers cannot manage credential lifecycle through the UI.

---

### 9. Participant Publishing to Register
**Backend:** Tenant Service
**Endpoints:**
- `POST /api/organizations/{orgId}/participants/publish` — Publish to register
- `PUT /api/organizations/{orgId}/participants/publish/{id}` — Update published
- `DELETE /api/organizations/{orgId}/participants/publish/{id}` — Revoke published

**UI Need:** "Publish" action on Participant detail page
**CLI Need:** `participant publish|unpublish` subcommands
**Impact:** On-register participant identity publishing is API-only.

---

### 10. Participant Suspend/Reactivate
**Backend:** Tenant Service
**Endpoints:**
- `POST /api/organizations/{orgId}/participants/{id}/suspend`
- `POST /api/organizations/{orgId}/participants/{id}/reactivate`

**UI Need:** Suspend/Reactivate buttons on Participant management page
**CLI Need:** `participant suspend|reactivate` subcommands
**Impact:** Admins can only deactivate (delete) participants — no soft suspend/restore.

---

## Tier 3 — Feature Gaps (Features exist but aren't exposed)

### 11. Threshold Signing (BLS)
**Backend:** Validator Service
**Endpoints:**
- `POST /api/validators/threshold/setup` — Initialize BLS threshold
- `POST /api/validators/threshold/sign` — Submit partial signature
- `GET /api/validators/threshold/{registerId}/status` — Threshold status

**UI Need:** Threshold signing setup wizard + status on Validator page
**CLI Need:** `validator threshold setup|sign|status`

---

### 12. Status List Management (W3C Bitstring)
**Backend:** Blueprint Service
**Endpoints:**
- `GET /api/v1/credentials/status-lists/{listId}` — Get status list
- `POST /api/v1/credentials/status-lists/{listId}/allocate` — Allocate index
- `PUT /api/v1/credentials/status-lists/{listId}/bits/{index}` — Set/clear bit

**UI Need:** Status list viewer under Admin > Credentials
**CLI Need:** `credential status-list get|allocate|set-bit`

---

### 13. Verifiable Presentations (OID4VP)
**Backend:** Wallet Service
**Endpoints:**
- `POST /api/v1/presentations/request` — Create request
- `GET /api/v1/presentations/{id}` — Get request
- `POST /api/v1/presentations/{id}/submit` — Submit presentation
- `POST /api/v1/presentations/{id}/deny` — Deny request
- `GET /api/v1/presentations/{id}/result` — Poll result

**UI Current:** Placeholder in MyCredentials ("planned for future release")
**CLI Current:** `credential present` exists
**UI Need:** Complete the presentation flow in Credentials page

---

### 14. Push Notification Management
**Backend:** Tenant Service
**Endpoints:**
- `POST /api/push-subscriptions` — Subscribe
- `DELETE /api/push-subscriptions` — Unsubscribe
- `GET /api/push-subscriptions/status` — Check status

**UI Need:** Push notification toggle in Settings > Notifications
**CLI Need:** Not needed (user-facing only)

---

### 15. Schema Provider Admin
**Backend:** Blueprint Service
**Endpoints:**
- `GET /api/v1/schemas/providers` — List providers with health
- `POST /api/v1/schemas/providers/{name}/refresh` — Trigger refresh

**UI Current:** Placeholder at `/admin/schema-provider-health`
**CLI Need:** `schema providers list|refresh`

---

### 16. Gateway Alerts & Dashboard Stats
**Backend:** API Gateway
**Endpoints:**
- `GET /api/alerts` — Active alerts
- `GET /api/stats` — System-wide stats
- `GET /api/dashboard` — Dashboard stats

**UI Need:** Wire dashboard cards to real `/api/dashboard` endpoint; add alerts panel
**CLI Current:** `admin health` and `admin alerts` exist

---

### 17. Events Admin
**Backend:** Blueprint Service
**Endpoints:**
- `GET /api/events/admin` — Admin events
- `DELETE /api/events/{id}` — Delete event

**UI Need:** Admin event log viewer with delete capability
**CLI Need:** `admin events list|delete`

---

### 18. Encrypted Payload Operations (Feature 045)
**Backend:** Blueprint Service
**Endpoints:**
- `GET /api/operations/{operationId}` — Poll encryption operation status

**UI Need:** Progress indicator during payload encryption (SignalR partially covers this)
**CLI Need:** `operation status` subcommand

---

## Recommended Build Order

Based on admin impact and dependency chains:

| Priority | Items | Rationale |
|----------|-------|-----------|
| **P0** | 1, 2, 3, 4, 5 | System cannot be configured without these |
| **P1** | 6, 7, 8, 9, 10 | Operational monitoring and identity management |
| **P2** | 11-18 | Feature completeness |

### Suggested Feature Grouping

| Feature | Scope | Estimated Items |
|---------|-------|:-:|
| **049: Admin Tooling — Validator & Policy** | Items 1-4, 6, 11 | Register policy UI/CLI, Validator admin UI/CLI, Consent approval, Metrics dashboard |
| **050: Admin Tooling — Identity & Credentials** | Items 5, 8, 9, 10, 12, 13 | Service principal CRUD UI, Credential lifecycle, Participant publish, Presentations |
| **051: Admin Tooling — Operations & Monitoring** | Items 7, 14, 15, 16, 17, 18 | Wallet delegation, Push notifications, Schema providers, Gateway alerts, Events admin |

---

---

## Backlog — Deferred to Future Features

Items not included in Feature 049 (System Admin Tooling). Grouped into two planned features:

### Feature 050: Identity & Credentials Admin

| # | Item | UI Work | CLI Work |
|---|------|---------|----------|
| 1 | Credential Lifecycle (revoke/suspend/reinstate/refresh) | Credential detail actions | `credential suspend\|reinstate\|refresh` |
| 2 | Participant Publishing to Register | "Publish" action on Participant detail | `participant publish\|unpublish` |
| 3 | Participant Suspend/Reactivate | Suspend/Reactivate buttons on Participant page | `participant suspend\|reactivate` |
| 4 | Status List Management (W3C Bitstring) | Status list viewer under Admin | `credential status-list get\|allocate\|set-bit` |
| 5 | Verifiable Presentations (OID4VP) | Complete presentation flow in Credentials page | Already partial in CLI |

### Feature 051: Operations & Monitoring

| # | Item | UI Work | CLI Work |
|---|------|---------|----------|
| 1 | Wallet Delegation & Access Control | "Access" tab on Wallet detail page | `wallet access grant\|list\|revoke\|check` |
| 2 | Push Notification Management | Push toggle in Settings > Notifications | Not needed (user-facing) |
| 3 | Schema Provider Admin | Wire placeholder at `/admin/schema-providers` | `schema providers list\|refresh` |
| 4 | Gateway Alerts & Dashboard Stats | Wire dashboard to real `/api/dashboard`; add alerts panel | Already partial in CLI |
| 5 | Events Admin | Admin event log viewer with delete | `admin events list\|delete` |
| 6 | Encrypted Payload Operation Status | Progress indicator during encryption | `operation status` |

**Status:** Backlog — not yet specified or scheduled.

---

**Next Step:** Feature 049 spec is at `specs/049-system-admin-tooling/spec.md`. Proceed with `/speckit.plan`.
