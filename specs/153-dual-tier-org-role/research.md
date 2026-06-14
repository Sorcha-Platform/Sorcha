# Phase 0 Research: PWA Dual-Tier / Org-Role Work

**Feature**: 153-dual-tier-org-role | **Date**: 2026-06-14

Grounded against the live PWA + Tenant auth. No open NEEDS CLARIFICATION.

## Decision 1 — Reuse the existing context switch (no new switch mechanism)

`ManagedUserContext.SetActiveContextAsync(orgId)` (`Services/Context/IUserContext.cs:104`) already
POSTs `/api/auth/switch-org`, stores the returned token, persists the active context, and fires
`OnContextChanged`. `/api/auth/switch-org` (Tenant `AuthEndpoints.cs:956`) re-mints at the tier
appropriate to the target org (`TierResolver.ResolvePreference`, downgrade-not-refuse), verifying
membership (403 if not a member). D reuses this unchanged.

## Decision 2 — Restore the personal capacity on return to Personal (the core gap)

Today, switching back to Personal (`orgId == null`) **keeps the org token** (`IUserContext.cs:143`
"keep the user's existing token in v1"). Because platform tokens carry `aud=:platform`, the citizen's
**consumer-gated** surfaces (e.g. `/api/v1/wallet/*` under `RequireConsumerAudience`) would 403 after
an org excursion. `/api/auth/switch-org` **cannot** mint a personal/consumer token (it requires an
org). So D **caches the personal/home token** and restores it:

- **Snapshot on leaving Personal**: when switching from Personal → org, capture the currently-active
  (consumer) token as the home token before it's overwritten.
- **Restore on return**: when switching org → Personal, set the home token active again.
- **Persistence**: add home-token slot to `IAccessTokenStore` (`GetHomeAsync/SetHomeAsync/
  ClearHomeAsync`, IndexedDB key `home-access-token`; cleared on sign-out).
- **Expiry edge**: if the home token expired during the excursion, restore yields a signed-out/refresh
  state via the existing expiry handling — acceptable v1 (a refresh-token renewal is a refinement).

No backend change; the F136 entitlement/audience gates are untouched.

## Decision 3 — Capacity is derived from the active context

The acting capacity = `IUserContext.ActiveContextOrgId` (null ⇒ Personal). The display label already
resolves in `MainLayout` (`ActiveLabel`, Feature 125) from `IUserOrgMembershipsClient`. No need to
parse the JWT for tier in the UI — the active context is the source of truth, and the token tier
follows it (org ⇒ platform/org, Personal ⇒ consumer home token).

## Decision 4 — Inbox surfaces org-role work + frames capacity

A's `Actions.razor` lists `/api/actions/pending` for the **active** token; in an org context that
returns the member's org-role actions (bound to their own wallet — verified: pre-baked participant
binding, `InstanceProjectionResolver`). D adds:
- an **"acting as <Org>"** banner when in an org context;
- a **refresh on context switch** (subscribe `IUserContext.OnContextChanged` → reload list + count).
`MainLayout` already exposes `OutstandingWorkChanged` + `RefreshTodoCountAsync` (A/C) — hook the
context-change there so the badge + mounted inbox refresh.

## Decision 5 — Execute org-role action in-context

`ApplicationInstance` → `/execute` uses the active token; in an org context that token carries
`org_id` + roles, which `ActionExecutionService` needs (issuance config + participant-ownership). No
submit-path change. **PRIMARY RISK — live-validate**: run the AssuredIdentity analyst Action 2 from
the PWA in the org context before trusting D (carried as SC-001/FR-006).

## Decision 6 — Entitlement-aware switcher (mostly existing)

The switcher lists only the user's memberships (`IUserOrgMembershipsClient`); `switch-org` 403s a
non-member and `SetActiveContextAsync` returns `false` (unchanged). D ensures the UI surfaces the
failure (FR-008) and never elevates client-side.

## Decision 7 — Testing seams

- Token-store home get/set/clear: InMemory store test.
- `ManagedUserContext` restore-on-Personal + snapshot-on-leave: unit test with a stub token store +
  stub HttpMessageHandler for switch-org.
- Inbox capacity banner + refresh-on-context-change: bUnit with a stub `IUserContext`.
- Switcher entitlement: covered by existing membership wiring; add a focused assertion if cheap.
No backend tests (no backend change). Live validation noted for execute-in-context.
