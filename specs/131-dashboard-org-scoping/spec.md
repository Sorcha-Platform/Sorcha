# Specification — Dashboard org-scoping (UX-005)

**Feature:** 131
**Status:** Draft
**Roadmap:** v1 / Milestone M4
**Design:** `docs/superpowers/specs/2026-05-18-ux-005-dashboard-org-scoping-design.md`

## Problem statement

The dashboard at `/` (Sorcha.UI.Web.Client/Pages/Home.razor) shows platform-wide totals to every signed-in admin/auditor. An Administrator of org A sees totals that include orgs B, C, D — wrong mental model and a soft data leak. The `GET /api/dashboard` endpoint that backs the cards is anonymous, so the same totals are readable by any unauthenticated HTTP caller — a hard data leak.

## User stories

### US1 · Org admin sees their own org's totals
**As** an Administrator of org A
**I want** the dashboard stats cards to count only my org's data
**So that** my mental model of platform usage matches what I actually manage.

**Acceptance:**
- ActiveBlueprints reflects only blueprints published by participants in my org.
- TotalWallets reflects only wallets with `Tenant = my-org-id`.
- ActiveRegisters reflects only my org's `OrganizationRegisterSubscriptions` with `Status=Active`.
- RecentTransactions reflects only transactions on registers I'm subscribed to.
- ConnectedPeers and TotalOrganizations cards are hidden.
- No platform-wide totals are visible anywhere on the page.

### US2 · Auditor sees same scope as Administrator
**As** an Auditor of org A
**I want** to see exactly the same scope as my Administrator
**So that** my read-only view is consistent with the actions I'm auditing.

**Acceptance:** identical to US1; role differs but stats scope does not.

### US3 · SystemAdmin defaults to org view, can toggle to platform
**As** a SystemAdmin
**I want** to default to the active context org and toggle to platform view explicitly
**So that** my dashboard matches the rest of the UI when I'm acting as an org admin, and I can still see platform-wide health on demand.

**Acceptance:**
- On first load, the stats grid is org-scoped to my active context org (the org-switcher selection).
- A `View: Org · Platform` toggle is present at top-right of the stats grid.
- Selecting Platform reveals six cards (org-view's four plus ConnectedPeers and TotalOrganizations).
- Toggle selection persists across reloads via localStorage keyed by my user id.

### US4 · Unauthenticated callers cannot read totals
**As** an outside HTTP caller
**I want** `GET /api/dashboard` to refuse unauthenticated requests
**So that** platform totals are not exfiltratable without an account.

**Acceptance:**
- `GET /api/dashboard` returns 401 without a valid bearer JWT.
- Backend `/api/stats` endpoints stay anonymous (security boundary is the gateway).

### US5 · SystemAdmin platform-view request is honoured only for SystemAdmin
**As** the platform
**I want** `?scope=platform` to be silently ignored for non-SystemAdmin callers
**So that** an org admin cannot surface platform totals by guessing the URL.

**Acceptance:**
- `GET /api/dashboard?scope=platform` from a non-SystemAdmin JWT returns org-scoped data, with `scope: "org"` in the response.
- Same request from SystemAdmin returns platform-scoped data with `scope: "platform"`.

## Functional requirements

| FR | Requirement |
|---|---|
| FR-001 | `GET /api/dashboard` MUST require a valid bearer JWT. |
| FR-002 | The endpoint MUST honour `?scope=platform` only when the caller's JWT carries the `SystemAdmin` role; otherwise the response MUST be org-scoped to `org_id` from the JWT. |
| FR-003 | Response MUST include `scope` ("org"\|"platform") and `orgId` (null when scope is platform) fields. |
| FR-004 | Org-scoped responses MUST NOT include `connectedPeers` or `totalOrganizations`. |
| FR-005 | Org-scoped `activeBlueprints` MUST be derived from blueprints whose publishing participant belongs to the caller's org. |
| FR-006 | Org-scoped `totalWallets` MUST be derived from `wallet.Wallets` rows with `Tenant = orgId`. |
| FR-007 | Org-scoped `activeRegisters` MUST equal the count of `OrganizationRegisterSubscriptions` rows for the org with `Status=Active`. |
| FR-008 | Org-scoped `recentTransactions` MUST equal the sum of transaction counts on the org's subscribed registers (Status=Active). |
| FR-009 | Platform-scoped responses retain the current six-card shape. |
| FR-010 | Backend `/api/stats` endpoints MUST accept an optional `?orgId={guid}` query parameter. When present, they MUST filter their counts to that org's data. When absent, they MUST return platform-wide counts (preserving today's wire shape). |
| FR-011 | The UI MUST hide the ConnectedPeers and TotalOrganizations cards when `scope == "org"`. |
| FR-012 | The UI MUST render a `MudButtonGroup` scope toggle visible only when the caller is in role `SystemAdmin`. |
| FR-013 | Toggle selection MUST persist in `localStorage` keyed by `platform_user_id` claim. |

## Success criteria

| SC | Criterion |
|---|---|
| SC-001 | `curl https://n1.sorcha.dev/api/dashboard` (no auth) returns 401. |
| SC-002 | `admin@sorcha.local` (SystemAdmin) sees the toggle; org view returns 4 cards; platform view returns 6 cards. |
| SC-003 | A non-SystemAdmin Administrator sees 4 cards and no toggle. |
| SC-004 | `?scope=platform` requested by a non-SystemAdmin returns `scope: "org"` in the response body and the four org-scoped fields only. |
| SC-005 | After a SystemAdmin selects Platform and reloads, the platform view is still rendered (localStorage persistence). |
| SC-006 | Numbers in org view match the corresponding lists: `totalWallets` = `/api/wallets?orgId=...` count, `activeRegisters` = Registers page count, etc. |
| SC-007 | No backend `/api/stats` endpoint has been moved off `AllowAnonymous`; gateway is the only auth gate. |
| SC-008 | Page load time of the dashboard is within 10% of the pre-change baseline (parallel-fan-out preserved). |

## Out of scope

- Caching strategy (current snapshot-per-request retained).
- Realtime updates / SignalR fan-out for stats.
- Historical/time-series view.
- Additional metric surfaces (e.g. presentation counts, credential issuances).
- Auditor's separate "audit log" page — UX-004 is a separate task.

## Dependencies

- **Reads** `OrganizationRegisterSubscriptions` (Tenant Service) — already in production for UX-001.
- **Reads** `wallet.Wallets.Tenant` column — already populated.
- **Reads** JWT `org_id`, `platform_user_id`, `role` claims — standard Sorcha JWT shape.
- **Touches** SystemAdmin's localStorage key shape — first time we use this; document the key.

## Open questions

None at design time. Three questions were resolved in the design session (Path A, org-by-default-toggle-to-platform, both-scoped).
