# Implementation Plan: PWA Dual-Tier / Org-Role Work

**Branch**: `153-dual-tier-org-role` | **Date**: 2026-06-14 | **Spec**: [spec.md](./spec.md)

**Source design**: `docs/superpowers/specs/2026-06-14-pwa-dual-tier-org-role-design.md`

**Depends on**: A (`151`, inbox) merged; reuses Feature 125 context-switch infrastructure.

## Summary

Let a citizen-and-org-member do their org-role workflow work in the PWA by lighting up the **existing**
context switcher. The org switch already re-mints + stores a platform/org token
(`ManagedUserContext.SetActiveContextAsync` → `/api/auth/switch-org`); the inbox (A) already lists the
member's actions (org-role actions bind to the member's own wallet, so they surface). The two real
gaps: (1) returning to **Personal** currently keeps the org token (consumer-gated surfaces would 403),
so we **cache the personal/home token and restore it**; (2) the inbox/badge need to **refresh + frame
"acting as <Org>"** on a capacity switch. No back-end change.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WASM PWA).
**Primary Dependencies**: `Sorcha.Wallet.Pwa` — `IUserContext`/`ManagedUserContext`,
`IAccessTokenStore`/`AccessTokenRecord`, `IActiveContextStore`, `MainLayout` (hosts
`ContextChipSwitcher` + `ActiveLabel`), A's `Actions.razor`/`IMyActionsClient`, `ApplicationInstance`,
`IUserOrgMembershipsClient`.
**Storage**: device-local IndexedDB (existing token + active-context stores; add a personal/home token slot).
**Testing**: xUnit + bUnit; mock `IUserContext`/token store/memberships. No backend tests (no backend change).
**Target Platform**: Blazor WASM PWA at `/wallet/` (consumer + platform tokens, by capacity).
**Project Type**: front-end feature reusing existing auth/context infra.
**Performance Goals**: capacity switch + inbox refresh feel instant.
**Constraints**: no backend change; **must not weaken the F136 entitlement/audience boundary**
(server 403 stands); base-relative nav; no `ISnackbar`; consumer-gated personal surfaces must keep
working after returning to Personal.
**Scale/Scope**: tier-aware token storage + Personal-restore + inbox capacity framing/refresh +
switcher entitlement polish. No new endpoints.

## Constitution Check

| Principle | Applies? | Status |
|-----------|----------|--------|
| I. Microservices-First | front-end only | ✅ PASS |
| II. Security First | **capacity/tier boundary** | ✅ PASS — relies on server entitlement/audience gates (403); never elevates client-side; personal-restore prevents wrong-tier use of consumer surfaces |
| III. API Documentation | no new APIs | ✅ N/A |
| IV. Testing (>85% new) | yes | ✅ PLANNED — token-store/restore + context-aware inbox bUnit + switcher entitlement |
| V. Code Quality | yes | ✅ PLANNED — nullable, async, DI, no warnings |
| VI. Blueprint Standards | no | ✅ N/A |
| VII. DDD | yes | ✅ PASS — "capacity" UI term over tier/org context; Action/Participant terms unchanged |
| VIII. Observability | front-end | ✅ PASS — structured logs on switch (existing) |

**Result**: PASS. No violations.

## Project Structure

```text
specs/153-dual-tier-org-role/
├── plan.md, research.md, data-model.md, quickstart.md, contracts/, checklists/, tasks.md

src/Apps/Sorcha.Wallet.Pwa/
├── Services/IAccessTokenStore.cs (+ AccessTokenRecord)   # MODIFY — capacity/tier marker + home-token slot
├── Services/Context/IUserContext.cs (ManagedUserContext) # MODIFY — restore home token on return to Personal
├── Services/ (sign-in/token capture)                     # MODIFY — capture the personal/home token at sign-in
├── MainLayout.razor                                      # MODIFY — capacity indicator + refresh inbox/badge on switch
├── Pages/Actions.razor                                   # MODIFY — "acting as <Org>" framing; refresh on context change
└── ContextChipSwitcher usage                             # REUSE — entitlement-aware (memberships only)
```

**Structure Decision**: PWA-contained, reusing the Feature-125 context infra + A's inbox. The new
mechanism is the **home-token capture + restore** so Personal works after an org excursion; the rest
is framing/refresh + entitlement polish.

## Open Decisions (resolved in research.md)

- How to restore the personal capacity on return to Personal (no `switch-org`-to-personal exists):
  cache the home consumer token at sign-in and restore it (refresh if expired).
- How the capacity is known (derive tier from the active JWT `aud` vs. an explicit marker on the record).
- How the inbox frames + refreshes per capacity.

## Complexity Tracking

> No Constitution violations. Section intentionally empty.
