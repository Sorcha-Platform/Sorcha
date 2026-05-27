# Specification — Dashboard org-scoping (UX-005)

**Feature:** 131
**Status:** Draft (v2 — pivoted after Wallet/Blueprint schema review)
**Roadmap:** v1 / Milestone M4
**Design:** `docs/superpowers/specs/2026-05-18-ux-005-dashboard-org-scoping-design.md`

## Problem statement

`Sorcha.UI.Web.Client/Pages/Home.razor` shows platform-wide totals on stats cards to every signed-in admin/auditor. An Administrator of org A sees totals that include orgs B, C, D — wrong mental model. The `GET /api/dashboard` endpoint backing the cards is anonymous, so the totals leak to any unauthenticated HTTP caller — a soft data exposure.

## v2 pivot

v1 of this spec proposed pushing `?orgId=` through Wallet and Blueprint Service `/api/stats` endpoints. Schema review showed `wallet.Wallets` and blueprint storage carry no direct org id (only user/owner-wallet linkage). v2 redefines the org-view card set to use only Tenant Service's existing org-scoped data plus a Register Service tx-count filter.

## User stories

### US1 · Org admin sees their org's own scope
**As** an Administrator of org A
**I want** the dashboard cards to reflect data about my org
**So that** my mental model matches what I manage.

**Acceptance:**
- Card grid shows: Active users in org · Pending invitations · Subscribed registers · Recent transactions across subscribed registers. Four cards.
- No platform-wide totals (Wallets, Blueprints, ConnectedPeers, TotalOrganizations) anywhere on the page.

### US2 · Auditor sees same scope as Administrator
**As** an Auditor of org A
**I want** the same scope as my Administrator
**So that** my read-only view is consistent.

**Acceptance:** identical to US1.

### US3 · SystemAdmin defaults to org view, can toggle to platform
**As** a SystemAdmin
**I want** the dashboard to default to my active context org and let me explicitly switch to platform view
**So that** my normal usage matches the rest of the UI but I can still see platform health.

**Acceptance:**
- First load: org-scoped to my active context org (the org-switcher selection).
- A `View: Org · Platform` toggle is present at top-right of the stats grid.
- Selecting Platform reveals six cards: Active Blueprints, Total Wallets, Recent Transactions, Active Registers, Connected Peers, Total Organizations.
- Toggle selection persists across reloads via `localStorage["dashboard-scope-{platform_user_id}"]`.

### US4 · Unauthenticated callers cannot read totals
**As** an outside HTTP caller
**I want** `GET /api/dashboard` to refuse unauthenticated requests
**So that** platform totals are not exfiltratable without an account.

**Acceptance:**
- `GET /api/dashboard` returns 401 without a valid bearer JWT.
- Backend `/api/stats` endpoints stay anonymous (security boundary is the gateway).

### US5 · `?scope=platform` honoured only for SystemAdmin
**As** the platform
**I want** `?scope=platform` ignored for non-SystemAdmin callers
**So that** an org admin cannot surface platform totals by guessing the URL.

**Acceptance:**
- `GET /api/dashboard?scope=platform` with non-SystemAdmin JWT returns org-scoped data; `scope` field in response is `"org"`.
- Same request with SystemAdmin JWT returns platform-scoped data; `scope` is `"platform"`.

## Functional requirements

| FR | Requirement |
|---|---|
| FR-001 | `GET /api/dashboard` MUST require a valid bearer JWT (`.RequireAuthorization()`). |
| FR-002 | The endpoint MUST honour `?scope=platform` only when the caller's role includes `SystemAdmin`; otherwise the response MUST be org-scoped. |
| FR-003 | The response MUST include `scope` (`"org"`\|`"platform"`) and `orgId` (Guid when org-scoped, null when platform-scoped). |
| FR-004 | Org-scoped responses MUST include `activeUsers`, `pendingInvitations`, `subscribedRegisters`, `recentTransactions`. They MUST NOT include platform-only fields. |
| FR-005 | Platform-scoped responses MUST preserve today's six fields (`totalBlueprints`, `totalBlueprintInstances`, `activeBlueprintInstances`, `totalWallets`, `totalRegisters`, `totalTransactions`, `totalTenants`, `connectedPeers`). |
| FR-006 | `activeUsers` = count of `UserIdentity` rows with `OrganizationId = orgId` and `Status = Active`. |
| FR-007 | `pendingInvitations` = count of `OrgInvitations` with `OrganizationId = orgId`, `Status = Pending`, `ExpiresAt > now`. |
| FR-008 | `subscribedRegisters` = count of `OrganizationRegisterSubscriptions` with `OrganizationId = orgId`, `Status = Active`. |
| FR-009 | `recentTransactions` = sum of `transactionCount` across the org's subscribed-active register ids, obtained by Tenant Service calling Register Service `/api/stats?registerIds=…`. |
| FR-010 | Register Service `/api/stats` MUST accept an optional `?registerIds=a,b,c` (comma-separated). When set, `transactionCount` is the sum across those registers; `registerCount` is the count of registers in the param. When unset, behaviour is unchanged. |
| FR-011 | The UI MUST render an org-view card grid (4 cards) when `scope == "org"` and a platform-view grid (6 cards) when `scope == "platform"`. |
| FR-012 | The UI MUST render a `MudButtonGroup` scope toggle visible only when the caller's role includes `SystemAdmin`. |
| FR-013 | Toggle selection MUST persist in `localStorage` keyed by `platform_user_id` claim. |

## Success criteria

| SC | Criterion |
|---|---|
| SC-001 | `curl https://n1.sorcha.dev/api/dashboard` (no auth) returns 401. |
| SC-002 | `admin@sorcha.local` (SystemAdmin) sees the toggle. Org view renders 4 cards; Platform view renders 6 cards. |
| SC-003 | A non-SystemAdmin Administrator sees 4 cards and no toggle. |
| SC-004 | `?scope=platform` requested by a non-SystemAdmin returns `scope: "org"` and the four org-scoped fields. |
| SC-005 | After a SystemAdmin selects Platform and reloads, Platform view is still rendered (localStorage persistence). |
| SC-006 | Org-view `subscribedRegisters` matches the count visible on `/registers`. |
| SC-007 | Org-view `activeUsers` matches the count on the `/admin/identity/org/{orgId}/dashboard` admin panel. |
| SC-008 | No backend `/api/stats` endpoint has been moved off `AllowAnonymous`; gateway is the only auth gate. |

## Out of scope

- Caching (snapshot-per-request retained).
- Realtime updates.
- Historical/time-series view.
- Wallet/Blueprint org-scoping. Deferred until schema decision or cross-service join is worth the surface cost.
- Auditor's separate audit-log page (UX-004 is separate).

## Dependencies

- `OrganizationRegisterSubscriptions` (Tenant) — already used by `/registers`.
- `Register Service /api/stats` — existing endpoint, extended with `?registerIds=`.
- JWT `org_id`, `platform_user_id`, `role` claims — standard.
- Existing `IDashboardService` in Tenant — extend with sibling method or extend response.

## Open questions

None at this revision.
