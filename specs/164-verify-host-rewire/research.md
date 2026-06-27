# Phase 0 Research: Verify Host Rewire + Live HAIP Transport (B3)

All NEEDS CLARIFICATION items from Technical Context are resolved below. Each entry records the decision, the
rationale, and the alternatives considered.

## R1 — Source of truth for the shared control (B1/B2 surfaces)

- **Decision**: Branch B3 from merged `master` where B1 (#1044), B2-foundation (#1045), and B2-components
  (#1048) are present. Treat the B2 seams/components and B1's `vp_token`-returning poll as existing inputs;
  do **not** re-implement them inside B3.
- **Rationale**: The spec's Assumptions state these are merged on `master`; the local worktree HEAD predates
  them (confirmed by exploration — the shared types and the `vp_token` poll are absent locally). Re-creating
  them would duplicate merged work and risk divergence from the shared versions B3 is meant to consume.
- **Alternatives considered**: (a) Re-author the missing types in B3 — rejected: contradicts the spec, would
  create the exact host-local duplicates FR-011 forbids. (b) Plan against the stale local HEAD — rejected:
  the plan would target a pre-B2 world that the merge has already moved past.

## R2 — Live transport target: which HAIP endpoints and does the poll return `vp_token`?

- **Decision**: The `HaipVerificationTransport` binds to the existing HAIP verifier endpoints in
  `Sorcha.Haip.Service` — create presentation request, serve request-object (for the QR deep-link), and the
  **result poll that returns the raw `vp_token`** (B1's additive change). See
  `contracts/haip-verifier-endpoints.md`.
- **Rationale**: FR-001/FR-006 require the transport to return the raw `vp_token` so the verdict is computed
  client-side by `IVerifiablePresentationValidator`. B1 (#1044) added exactly this return to the poll.
  Exploration of the *local* worktree showed the poll currently returns a flat `VerificationResult` **without**
  `vp_token` — that is the pre-B1 shape and confirms staleness; the merged poll returns `vp_token`.
- **Alternatives considered**: (a) Compute the verdict server-side and return it — rejected by FR-007 (HAIP's
  server validation is untouched; the human verdict is client-side, and HAIP's flat verdict has no warn
  state). (b) Add a new poll endpoint in B3 — rejected: B1 already added the additive return; B3 consumes it.

## R3 — One transport, per-host identity injection

- **Decision**: A single `HaipVerificationTransport` serves both hosts. The verifier identity is injected as
  a separate dependency: PWA supplies the ephemeral P-256 identity via `IEphemeralVerifierIdentityService`
  (WebCrypto, fresh per session); the desk app supplies its stable org identity
  (`did:sorcha:verifier:{orgId}`). The identity is embedded in the create-request so the holder's wallet
  shows the correct requester.
- **Rationale**: The spec's Assumptions name this as the only per-host variation. Keeping identity behind an
  abstraction the transport consumes means one transport, two registrations — matching FR-005.
- **Alternatives considered**: (a) Two transports (one per host) — rejected: duplicates the HAIP-call logic,
  the very divergence B3 removes. (b) Hard-code identity in the transport — rejected: breaks per-host
  pluggability (FR-005).

## R4 — The stub-leak failure mode (headline)

- **Decision**: Each host's DI override registers `HaipVerificationTransport` for `IVerificationTransport`
  *after* the library's default stub registration, and each host has an **explicit resolution assertion test**
  that the resolved `IVerificationTransport` is the HAIP implementation and **not**
  `NotConfiguredVerificationTransport`. This assertion is independent of any end-to-end test.
- **Rationale**: The spec names "host renders the control but never overrides the stub" as the single most
  common B3 failure and the documented reason the first B2 attempt parked. An end-to-end test can mask it
  (e.g. skipped/mocked); a direct container-resolution assertion cannot. FR-002 / SC-002.
- **Alternatives considered**: (a) Rely only on an e2e test — rejected: it can pass for the wrong reason or be
  skipped, letting the stub leak to production. (b) Remove the stub from the library — rejected: B2 keeps it
  as the safe library default for hosts that have not yet wired verification; B3 overrides, it doesn't delete.

## R5 — Verifier token tier acceptance at HAIP

- **Decision**: Confirm against a **live HAIP node** that create-request and result-poll accept the consumer
  tier (PWA) and the org/desk tier (desk app). If a tier is rejected, apply the minimal HAIP-side allowance
  (audience/policy) needed; record the observed status codes in `quickstart.md`.
- **Rationale**: FR-008 and the design's "verify during impl" note. The tier boundary is enforced per endpoint
  via the Feature 136 audience policies; the verify endpoints must permit both tiers or the rewire 401/403s at
  runtime with no UI signal. This is a live-node confirmation, not an assumption.
- **Alternatives considered**: (a) Assume both tiers already allowed — rejected: the spec explicitly flags
  this as unverified and to be confirmed live. (b) Mint a service token in the UI — rejected: violates the
  tier model (the verifier acts as its own tier, not a service principal).

## R6 — Polling lifecycle (cancellation + disposal)

- **Decision**: The shared `VerificationSessionQr` poll loop is driven by a `CancellationToken` tied to the
  component lifetime and `IAsyncDisposable`; navigating away or cancelling stops the loop and disposes timers.
  The transport's poll method accepts a `CancellationToken`. Bounded interval (default ≤2 s), explicit
  expiry/timeout state.
- **Rationale**: FR-012 / SC-006 require no leaked timers on navigate-away. Blazor component disposal is the
  correct seam; passing the token through the transport keeps cancellation honoured at the HTTP layer.
- **Alternatives considered**: (a) Fire-and-forget timer — rejected: leaks on navigation. (b) Server-push
  (SignalR) instead of polling — rejected: out of scope; the B2 control is poll-based and HAIP exposes a poll
  endpoint, not a hub.

## R7 — Preset catalogue source

- **Decision**: Continue using B2's `DefaultPresetCatalogue` (bundled default) via
  `IVerificationPresetCatalogue`, with the bundled fallback for PWA offline/cold-start. No central HTTP-backed
  preset endpoint in B3.
- **Rationale**: Spec Assumptions + Out of Scope explicitly defer the `GET /api/v1/verifier/presets` endpoint
  (design §5) unless already shipped by B2. The bundled default satisfies the offline edge case (PWA cold
  start still renders question selection).
- **Alternatives considered**: (a) Build the HTTP preset endpoint now — rejected: explicitly out of scope.

## R8 — Legacy retirement sequencing

- **Decision**: Retire legacy paths **only after** both host rewires are live (US4 follows US2/US3). Remove:
  PWA `VerifyFlow`; desk `PresentationRequestBuilder`, `InMemoryVerifierSessionStore`,
  `/r/{sessionId}/response` + `/status` endpoints, `Outcome.razor`; and any host-local duplicate of
  `VerdictViewModel` / preset definitions the shared library already owns.
- **Rationale**: FR-009/010/011 + the spec's priority ordering — retiring before the rewire would remove a
  path still in use. A post-rewire deletion with a green build proves no dead references / orphaned DI.
- **Alternatives considered**: (a) Delete legacy first — rejected: breaks the live verify surface mid-wave.
  (b) Leave legacy behind a flag — rejected: defeats the unification (SC-001/SC-005 require it gone).

## R9 — Where the new transport lives

- **Decision**: `HaipVerificationTransport` lives in `Sorcha.UI.Components.User` (shared library), alongside
  the B2 seam and stub, and must be WASM-safe (HTTP via the injected client, no server-only types).
- **Rationale**: Both hosts (one WASM, one desk Blazor) must share one implementation; placing it in the
  shared library is the only way to avoid a per-host copy. WASM-safety is mandatory because the PWA is WASM.
- **Alternatives considered**: (a) Put it in each host — rejected: duplication. (b) Put it in a server-only
  library — rejected: the PWA could not consume it.

## Resolved unknowns summary

| Unknown (Technical Context) | Resolution |
|---|---|
| B1/B2 surfaces present? | R1 — branch from merged `master`; consume, don't rebuild |
| Does HAIP poll return `vp_token`? | R2 — yes (B1 additive); transport returns it for client-side verdict |
| One transport or two? | R3 — one transport, per-host identity injection |
| How to prevent stub leak? | R4 — explicit container-resolution assertion per host |
| Tier acceptance unknown | R5 — confirm against live node, apply minimal allowance |
| Polling leak risk | R6 — CancellationToken + IAsyncDisposable, bounded interval |
| Preset source | R7 — B2 `DefaultPresetCatalogue`, bundled fallback |
| Retirement timing | R8 — after both rewires, green build proves clean |
| Transport placement | R9 — shared library, WASM-safe |
