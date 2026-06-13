# Phase 0 Research: PWA Citizen Workflow Inbox

**Feature**: 151-citizen-workflow-inbox | **Date**: 2026-06-13

The source design (`docs/superpowers/specs/2026-06-13-pwa-citizen-workflow-inbox-design.md`) and a
read-only codebase investigation resolved the substantive unknowns before planning. This file
records the decisions; there are **no open NEEDS CLARIFICATION** items.

## Decision 1 — Backend surface to consume

**Decision**: Consume the existing `GET /api/actions/pending?page&pageSize` and
`GET /api/actions/pending/count` (Blueprint Service). No new or modified backend.

**Rationale**: Both endpoints already resolve a citizen's wallet(s) from a consumer-tier token via
`platform_user_id` (`ActionEndpoints.cs:166`, aligned with F136/#878) and return only actions the
citizen is the designated actor of (`EfCoreInstanceStore.cs:265` + `:658` `IsActionForWallet`) —
i.e. "my turn". This is exactly the inbox semantic (FR-001, FR-003, SC-002).

**Alternatives considered**:
- *Add a consumer-tier guard to `/api/actions/pending`* — **rejected**: the web `MyActions` page
  calls the same endpoint on the platform tier; it is a legitimately cross-tier "any-human"
  endpoint and per F136 correctly stays plain `RequireAuthorization()`. Tightening it would break
  the web app.
- *New consumer-only `my-instances` endpoint for a full tracker* — deferred to B/C (SCOPE; the full
  "all my workflows + state" tracker is not in A).

## Decision 2 — Open-action flow

**Decision**: Reuse `Pages/ApplicationInstance.razor` + `IApplicationActionClient` unchanged. The
inbox navigates to `applications/{instanceId}` (base-relative).

**Rationale**: The fill/submit loop (load instance + blueprint → `SorchaFormRenderer` →
`POST /…/execute`) already works (F125/F137). A only adds discovery around it (FR-004, FR-005).
Base-relative navigation is mandatory under the `/wallet/` path prefix (guards the PR #698
broken-nav regression class).

## Decision 3 — "In review" indication

**Decision**: Render the existing Feature-124 pending-application notice
(`IPendingApplicationClient` → `GET /api/v1/wallet/pending-applications`, consumer-tier) as a
lightweight banner below the "needs you" list.

**Rationale**: Reuses an existing, consumer-tier signal (FR-009). A complete instance-state tracker
is deferred (B/C). The notice is already posted on successful submit by `IApplicationActionClient`.

## Decision 4 — Navigation placement (was: nav host open decision)

**Decision**: Surface the inbox as a **new primary destination** reachable from the shared
`FloatingTabBar` (`Sorcha.UI.Components.User/Components/Wallet/FloatingTabBar.razor`) with a **count
badge**, route `actions`, plus a complementary entry point on Home.

**Rationale**: The PWA's primary navigation is the `FloatingTabBar` (currently Home / Cards /
Activity / Settings). A "Things to do" destination there is the discoverable home for the inbox and
the natural carrier for the live count badge (FR-006, FR-007).

**Risk / constraint (verified)**: `FloatingTabBar` lives in the shared `Sorcha.UI.Components.User`
library but is consumed by **exactly one** host — the PWA `MainLayout.razor` (verified:
`grep FloatingTabBar src --include=*.razor` returns only the PWA). A 5th tab is therefore safe with
no web-host impact, and no parameterisation is required. A five-item bottom bar is at the upper
bound of mobile IA guidance, so the exact visual treatment (5th tab vs. badge-on-Home + tab) is a
candidate for `/speckit.ui-phase` / the `frontend-design` skill, but it does not block planning.

## Decision 5 — Page identity

**Decision**: New `Pages/Actions.razor` (route `actions`). Do **not** repurpose `Applications.razor`.

**Rationale**: `Applications.razor` (route `/applications`) is the empty stub reserved for
sub-project B's catalogue ("start something new"). Keeping the inbox separate avoids a collision
with B and keeps the two discovery surfaces (do-existing vs. start-new) cleanly bounded.

## Decision 6 — Live refresh

**Decision**: Subscribe to the existing `CitizenWalletHubConnection` signal (the same channel that
drives silent credential sync) to re-fetch list + count; a short poll runs only while the page is
open as a fallback.

**Rationale**: Reuses the existing real-time plumbing (FR-007, SC-004). Background/closed-app push
is explicitly out of scope (companion roadmap P2 / sub-project C).

## Decision 7 — Test placement

**Decision**: New tests live in `tests/Sorcha.Wallet.Pwa.Tests` (the existing PWA test project):
`Actions/MyActionsClientTests.cs` (JSON mapping via stub `HttpMessageHandler`) and
`Pages/ActionsInboxTests.cs` (bUnit, `JSRuntimeMode.Loose`).

**Rationale**: Matches existing PWA test conventions (`sorcha-ui` skill). No backend test project is
touched (no backend change).

## Decision 8 — Feedback surface

**Decision**: Use `IInlineFeedback` for refresh-failure / stale-action messages; never `ISnackbar`.

**Rationale**: Critical Pattern #12 — the PWA has retired the snackbar surface; a CI gate enforces
it. Inline feedback renders via the existing `InlineFeedbackHost`.
