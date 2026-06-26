# Quickstart / Validation Guide: Verify Host Rewire (B3)

Runnable validation scenarios proving B3 works end-to-end. Implementation detail lives in `tasks.md`;
contracts in `contracts/`; entities in `data-model.md`.

## Prerequisites

- Branch from merged `master` (B1 #1044, B2 #1045 + #1048 present — see plan.md *Worktree Staleness*).
- .NET 10 SDK, Docker Desktop.
- A running HAIP node (`Sorcha.Haip.Service`) reachable by both hosts — via `docker-compose up -d` or
  `dotnet run --project src/Apps/Sorcha.AppHost`.
- A holder able to drive the Present flow (Citizen Wallet PWA or a test holder) to complete a `direct-post`.

## Setup

```bash
dotnet restore && dotnet build
# Bring up the stack (HAIP + dependencies)
docker-compose up -d        # or: dotnet run --project src/Apps/Sorcha.AppHost
```

## Validation 1 — Live transport replaces the stub (US1, the headline)

**Goal**: Prove the resolved `IVerificationTransport` is `HaipVerificationTransport`, never the stub, and
that a create→poll round trip returns `vp_token` on complete.

```bash
# DI-resolution assertion (per host) + transport round-trip
dotnet test tests/Sorcha.UI.Components.User.Tests --filter "FullyQualifiedName~Transport"
dotnet test tests/Sorcha.Haip.Service.Tests       --filter "FullyQualifiedName~Verifier"
```

**Expected**:
- A direct container-resolution test asserts the resolved type is `HaipVerificationTransport` and **not**
  `NotConfiguredVerificationTransport` (contract C1 / SC-002).
- Round trip: `StartAsync` → non-empty `SessionId` + `QrDeepLink`; `PollAsync` before holder → `Pending`,
  no `vp_token`; after holder `direct-post` → `Complete` + raw `vp_token`.
- Both verifier tiers (consumer, org/desk) are accepted on create + poll. Tier auth change applied in
  T005 (Feature 164 B3): both `CreatePresentationRequest` and `GetVerificationResult` now use
  `.RequireAuthorization()` (any authenticated caller) instead of `RequireService` (FR-008):
  - create-request: consumer → `201 Created`, org/desk → `201 Created`
  - result-poll:    consumer → `200 OK`, org/desk → `200 OK`
  - allowance applied: Changed from `RequireAuthorization(AuthorizationPolicies.RequireService)` to
    `RequireAuthorization()` on both endpoints to accept all authenticated callers.

## Validation 2 — PWA `/wallet/verify` on the shared control (US2)

**Goal**: The PWA shows the shared control (no paste box), renders a QR, and computes the 4-layer verdict.

```bash
dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~Verify"
# Manual: run the PWA, open /wallet/verify
```

**Expected** (per US2 acceptance scenarios):
1. Question-selection panel renders (presets + custom); **no free-text paste field**.
2. Selecting a question shows a scannable QR + deep-link; page enters waiting-for-holder state.
3. Holder presents → 4-layer verdict trail renders with a Pass / Warn / Fail headline.
4. Register-anchor affordance runs the public-register cross-check and shows its result.
5. The create-request carries the PWA's **ephemeral P-256** identity (fresh per session), not a stable org id.

## Validation 3 — Desk Verifier on the shared control (US3)

**Goal**: The desk app renders the same control with its **stable org** identity.

```bash
dotnet test tests/Sorcha.Verifier.Tests --filter "FullyQualifiedName~Verify"
# Manual: run Sorcha.Verifier, start a verification
```

**Expected** (per US3 acceptance scenarios):
1. Same question selector / QR-poll / verdict-trail components as the PWA render.
2. The create-request carries the desk app's **stable org** verifier identity (holder sees a named requester).
3. Verdict is the same client-side 4-layer trail + register-anchor affordance as the PWA.
4. Code inspection: the desk app no longer hosts its own request builder, session store, response/status
   callback, or bespoke verdict page (overlaps Validation 4).

## Validation 4 — Legacy paths retired (US4)

**Goal**: The divergent legacy machinery is gone; the solution builds and verify still works on both hosts.

```bash
# These should return NO results (types/endpoints removed)
grep -rn "VerifyFlow" src/Apps/Sorcha.Wallet.Pwa src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify
grep -rn "PresentationRequestBuilder\|InMemoryVerifierSessionStore\|Outcome.razor" src/Apps/Sorcha.Verifier
grep -rn "/r/{sessionId}/response\|/r/{sessionId}/status" src/Apps/Sorcha.Verifier

dotnet build
dotnet test
```

**Expected**:
- The greps return no matches (FR-009/010, SC-005): paste `VerifyFlow` gone; desk
  `PresentationRequestBuilder`, `InMemoryVerifierSessionStore`, `/r/{sessionId}/response`+`/status`,
  `Outcome.razor` gone.
- No host-local duplicate of `VerdictViewModel` or preset definitions (FR-011) — hosts consume the shared
  library versions.
- `dotnet build` succeeds with no warnings; `dotnet test` passes — no dead references, no orphaned DI
  registrations (FR-014, SC-005).

## Validation 5 — Resource hygiene (SC-006)

**Goal**: Navigating away mid-poll leaves no active polling loop / timer.

- Test: start a session, trigger component disposal / cancellation before the holder responds, assert the
  poll loop stops and no timer remains (FR-012 — CancellationToken / IAsyncDisposable). Covered in the PWA
  and/or component test project.

## Done when

- [ ] Validation 1–5 pass.
- [ ] Resolved `IVerificationTransport` is the HAIP impl on **both** hosts (SC-002).
- [ ] One verify control on both hosts; no second/legacy surface reachable (SC-001).
- [ ] Same verdict for the same `vp_token` on both hosts (SC-004).
- [ ] Tier status codes recorded above (FR-008).
