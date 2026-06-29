# Phase 0 Research: Fix "Verification Not Configured" False Error

This feature's spec was written against an **assumed** architecture. Phase 0 reconciled the
spec's prescriptive wording with the **actual** codebase. The headline finding: the spec's named
artifacts do not exist, and the spec's credential mechanism cannot satisfy the real endpoints.
Each decision below records what was chosen, why, and what was rejected.

---

## Finding 0 — Codebase ground truth (evidence)

| Spec term / claim | Evidence in repo | Status |
|---|---|---|
| `IHaipVerifierClient` | No match anywhere (`grep -r IHaipVerifierClient`). | **Absent** |
| `HaipVerificationTransport` | No match. | **Absent** |
| `NotConfigured` stub transport | No match in `src/Apps/Sorcha.UI`. | **Absent** |
| "Verification is not yet configured here" | No match in `.cs`/`.razor`. | **Absent** |
| `AddSorchaUserComponents` wires the client | `…/Sorcha.UI.Components.User/Extensions/ServiceCollectionExtensions.cs` — body is "Intentionally empty until Phase 5 (T037)"; called nowhere. | **Empty stub** |
| Verifier requests endpoint requires auth | `…/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs:35,76` — `RequireAuthorization(AuthorizationPolicies.RequireService)` // SEC-013. POST `/requests` + GET `/requests/{id}/result`. | **Service-token only** |
| Web verification surface | `…/Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrCard.razor` — renders QR, polls `IHaipOfferService.GetVerificationResultAsync` → `/api/v1/verifier/requests/{id}/result`. | **Real surface** |
| Web transport | `…/Sorcha.UI.Components.User/Services/User/Credentials/HaipOfferService.cs:58-62` — any non-success → `_logger.LogWarning(...)` → `return null`. | **Swallows failures** |
| Web transport already authenticated | `…/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs:300-306` — `IHaipOfferService` is built with `AuthenticatedHttpMessageHandler` (user JWT attached). | **Already user-auth'd** |
| Correct user-facing pattern exists | `PresentationAdminService.cs:32,55` posts to `/api/v1/presentations/request` and `/api/v1/presentations/{id}/result` (Blueprint BFF), not the verifier endpoints. | **Reference pattern** |
| BFF → verifier bridge | `…/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleService.cs:165` calls `IHaipServiceClient.CreatePresentationRequestAsync` with a **service** token. | **Correct path** |
| PWA "verify" surface | `…/Sorcha.Wallet.Pwa/Pages/Verify.razor` hosts `VerifyFlow` over the **local** `IVerifierEngine` (Feature 125 doorstep). It does **not** poll the verifier requests endpoint. | **Different surface** |

**Root cause (real):** `PresentationRequestQrCard` polls the **service-only** endpoint
`/api/v1/verifier/requests/{id}/result` carrying a **user** JWT. The `RequireService` policy
rejects it (401/403). `HaipOfferService` swallows the rejection to `null`, so the QR card's
polling loop `continue`s indefinitely — the surface never advances to Verified and never shows an
error. To the user this reads as "verification doesn't work / isn't here," matching the spec's
described symptom even though the literal "not configured" string is not what renders.

---

## Decision 1 — Reconcile spec intent with real code (don't build phantom artifacts)

**Decision**: Plan against the real surfaces (`PresentationRequestQrCard` + `IHaipOfferService`),
preserving the spec's intent (Stories 1/2/3, FR-001…FR-010, SC-001…SC-005). Do **not** introduce
new types merely to match spec names (`IHaipVerifierClient`, `HaipVerificationTransport`,
`NotConfigured` stub).

**Rationale**: Equivalent surfaces already exist and are wired into DI. Creating identically-purposed
parallel abstractions would duplicate behavior, fragment the verification path, and violate
"maintain consistency / use existing patterns" (Constitution AI guidelines #3).

**Alternatives considered**:
- *Build the spec's named artifacts verbatim* — rejected: produces duplicate/parallel abstractions
  for surfaces that already exist; higher risk, no user benefit.
- *Pause and rewrite spec.md first* — viable but heavier; the user declined the "revise spec"
  option when asked. The divergence is captured here and in plan.md instead, so spec intent is
  traceable without blocking implementation.

---

## Decision 2 — Keep verifier endpoints service-only; route UI through the `/presentations` BFF

**Decision**: Hold `/api/v1/verifier/requests*` at `RequireService`. Fix the UI so its
verification calls target the **user-authenticated** Blueprint surface `/api/v1/presentations/*`,
which already holds the service identity (`IHaipServiceClient` / `ServiceAuthClient`) to reach the
verifier downstream. Concretely, the QR card's result polling moves off
`IHaipOfferService.GetVerificationResultAsync` (verifier endpoint) onto a BFF-backed result/status
call (the `PresentationAdminService`/`/api/v1/presentations/{id}/result` pattern, or the Blueprint
`/api/presentations/{id}/status` endpoint that is `AllowAnonymous` for lifecycle polling).

**Rationale**:
- Honors spec **assumption #105**: authenticate the caller, do **not** relax endpoint authorization.
- Honors **SEC-013** and Constitution II: the service-trust boundary stays intact; no service
  credential is shipped to a browser/PWA.
- Satisfies **FR-002/FR-003** ("each host attaches its own user/holder credential") *to the BFF*,
  and **FR-004** ("service identity to the verifier") via the existing Blueprint→HAIP hop — which
  already works. The only thing missing was the UI calling the right tier.

**Alternatives considered**:
- *Relax verifier endpoint to accept consumer/platform audiences* — rejected: contradicts
  assumption #105, weakens SEC-013, broadens the attack surface of an internal endpoint.
- *Give the browser/PWA a service identity to call the verifier directly* — rejected: places a
  service credential in a public client; a zero-trust violation.

> The user was asked to confirm this fork (route-through-BFF vs relax-policy vs service-token-in-UI)
> and declined to override; the recommended default (route through BFF) therefore stands.

---

## Decision 3 — Distinguish three terminal-ish states; surface transport failure with retry

**Decision**: Replace the swallow-to-`null` behavior so the transport returns a discriminated
outcome the UI can render as **one of three** distinct states:
1. **Live/empty-but-configured** — request accepted, no result yet → keep polling (existing).
2. **Transport/auth/server error** — 401/403/5xx/network → **Error state with Retry** (FR-006,
   FR-007, FR-009; SC-003/SC-004), *distinct from* not-configured.
3. **Genuinely not configured** — the host did not wire up verification at all → the legitimate
   "not configured" state (FR-008, SC-005). In the real code this is an absence of registration,
   not a 401; keep it distinct so the fix does not make every host claim to be configured.

**Rationale**: The current `null` conflates (1), (2) and (3). FR-006/FR-007 require (2) to be
visible and recoverable; SC-005 requires (3) to be preserved with no regression. The QR card
already models terminal states (`HaipVerificationStates`) and a polling loop — add an `Error`
state + a retry affordance and route transport failures into it instead of `continue`.

**Alternatives considered**:
- *Surface a generic snackbar/toast* — rejected: Pattern #12 retired `ISnackbar` for user-facing
  code; use the inline component state already present in the card (and `IInlineFeedback` only at
  the page level if needed).
- *Throw and let an error boundary catch it* — rejected: loses the retry affordance and the
  polling context; the card already owns the loop and is the right place to show retry.

---

## Decision 4 — Bounded retry; preserve clock + refresh semantics

**Decision**: Reuse the existing polling bounds (`HaipPollingDefaults.PollInterval`,
`MaxPollTicks`) and gate retries so a persistent failure does not spam the backend or lock the UI
(Edge Case "Retry storms"). Keep the existing **web** `AuthenticatedHttpMessageHandler` 401-refresh
and the **PWA** `BearerTokenHandler` (transparent refresh) + `ServerClockHandler` (skew handling)
in force on whichever typed client the BFF calls flow through (FR-003, Edge Cases
"Expired/refreshable token", "Clock skew").

**Rationale**: These handlers already implement refresh-and-retry and server-clock capture; the fix
must keep them on the path, not bypass them. Retry is user-initiated (explicit Retry control) plus
the bounded background poll — no unbounded auto-retry.

**Alternatives considered**:
- *Add a new Polly retry policy* — rejected as out-of-scope; bounded polling + user-initiated retry
  already meets FR-009 and the retry-storm edge case without new infrastructure.

---

## Decision 5 — Testing & validation strategy

**Decision**: Cover three paths with xUnit + Moq/component tests and one manual/Playwright
walk-through (see quickstart):
- **Happy path (SC-001/SC-002)**: configured host + reachable backend → live session renders, no
  not-configured.
- **Failure path (SC-003/SC-004)**: backend returns 401/5xx or is unreachable → Error+Retry shown
  (no blank/empty, no false not-configured); on recovery, Retry reaches the live session.
- **Legitimate not-configured (SC-005)**: a host with verification not wired up still shows the
  not-configured state.

**Rationale**: Maps 1:1 to the spec's measurable outcomes; satisfies Constitution IV.

**Alternatives considered**: Integration-only testing — rejected: the state-discrimination logic
(Decision 3) is best pinned at the component/service level for determinism.

---

## Open items deferred to /speckit-tasks

- Exact BFF endpoint for QR-card result polling: `/api/v1/presentations/{id}/result`
  (`PresentationAdminService`, user-auth) vs the Blueprint `/api/presentations/{id}/status`
  (`AllowAnonymous`, lifecycle). Both avoid the `RequireService` endpoint; task generation should
  pick the one whose payload matches `HaipVerificationResult`/`HaipVerificationStates` with least
  mapping. Recorded in [contracts/verification-transport.md](./contracts/verification-transport.md).
- Whether to also harden the PWA path: `Verify.razor` uses the local `IVerifierEngine` and is not on
  the broken polling path, so it is **verify-only** here unless task analysis finds a second PWA
  surface that hits the verifier endpoint.
