# Verify unification — single HAIP transport, client-side verdict (PR B)

**Date:** 2026-06-25
**Status:** Approved design — ready for spec review → implementation (staged into PR B1/B2/B3)
**Supersedes:** `2026-06-25-verify-unification-decisions.md` (the earlier decisions stub)

---

## Goal
Collapse the three divergent "verify" surfaces into **one shared control** rendered by both the
Citizen Wallet PWA (`/wallet/verify`) and the standalone `Sorcha.Verifier` desk app, on **one
transport**, so the verify experience is upgraded once and flows through to both. The verifier picks
**what to verify** → shows a **QR** → the holder scans it with their Present flow → a **rich 4-layer
verdict** is shown.

## The key architectural finding (why this design)
Two concerns that looked coupled are actually separable:

- **HAIP's server-side verdict is lighter & structurally different** from `Sorcha.Verifier`'s. Its
  `direct-post` validates and returns flat fields (`IsValid`, `Errors`, `X5cChainValid`,
  `StatusCheckResult`, `TrustEvidence`) — **no** structured 4-layer trail, **no** register-anchor
  cross-check, **no** Warn state. Consuming it would lose the desk verifier's UX.
- **The rich verdict is transport-independent and already client-side-capable.**
  `Sorcha.Verifier.Engine`'s `IVerifiablePresentationValidator` (the 4-layer validator) is
  **WASM-safe and already runs in the PWA today**; the register-anchor endpoints are
  **public/anonymous**. So the full rich verdict can be computed **client-side, identically, on both
  hosts, from the vp_token alone**.

**Therefore:** unify the **transport** on HAIP; compute the **verdict client-side** in the shared
control. HAIP's own validation is untouched and keeps serving the blueprint-automation consumer
(`HaipPresentationConsumer`) — it is *not* the human-verifier verdict.

---

## Architecture

### 1. Shared verify control (`Sorcha.UI.Components.User/Components/Verify/`)
Replaces today's thin paste-based `VerifyFlow` with three composables, lifted from `Sorcha.Verifier`:
- **Question selector** — renders the **config-driven preset catalogue** (see §5) plus a custom/ad-hoc
  request; builds a presentation request (VCT + required/optional claims + purpose).
- **Request-QR + poll** — renders the `request_uri` QR and the awaiting-holder poll state.
- **Verdict trail** — the 4-layer trail (selective disclosure, live presentation/KB-JWT, issuer
  signature, revocation) + the register-anchor "verify against the register" affordance.

Both `/wallet/verify` and `Sorcha.Verifier` render this same control.

### 2. Transport seam — `IVerificationTransport`
One abstraction, one implementation (`HaipVerificationTransport`), used by both hosts:
- `CreateRequestAsync(request) → { requestId, requestUri, qrPayload }` → HAIP `POST /api/v1/verifier/requests`.
- `PollAsync(requestId) → { state, vpToken?, delegation? }` → HAIP result poll, **now returning the raw
  vp_token + delegation** once the holder has posted.

Retires `Sorcha.Verifier`'s inline `PresentationRequestBuilder`, `InMemoryVerifierSessionStore`, and the
local `/r/{sessionId}/response` callback.

### 3. Client-side verdict
On poll-complete the control runs `IVerifiablePresentationValidator` (shared engine) on the returned
vp_token → 3 crypto layers, then calls `IRegisterAnchorClient` (extracted to the shared lib) for layer
4. Identical verdict on both hosts. **HAIP's server-side validation is unchanged** (serves
`HaipPresentationConsumer` / blueprint automation).

### 4. HAIP change (small)
Extend the verifier result endpoint to return the raw vp_token (+ delegation) alongside its existing
result, so the client can validate locally.
**Verify during impl:** that `POST /verifier/requests` + the result poll accept the verifier's token
tier (consumer/desk) — may need a tier allowance. Related to the consumer-token thread in the mobile
feedback backlog; confirm the actual status codes on a live node.

### 5. Config-driven verification-request presets  ← (per review)
The "what can a verifier ask for" catalogue is **data, not code** (today it is hardcoded in
`Sorcha.Verifier/Services/QuestionPresets.cs`).
- Model: `VerificationPreset { Key, Label, Purpose, Vct, RequiredClaims[], OptionalClaims[] }`.
- Served **centrally** so both hosts show the same options and the set is editable **without an app
  rewrite/redeploy**: a backend catalogue endpoint (e.g. `GET /api/v1/verifier/presets`, HAIP or a
  config surface) backed by editable JSON config (appsettings section or a mounted
  `verifier-presets.json`), with a **bundled default** fallback for offline/first-run.
- The shared question selector loads it via `IVerificationPresetCatalogue` (HTTP-backed, cached),
  renders the presets, and still supports a custom request.
- Editing presets = edit config/JSON (or a later admin UI) — no code change.

### 6. Verifier identity (pluggable)
The control depends on an identity provider; each host supplies its own, preserving current behaviour:
- PWA → existing **ephemeral** P-256 identity (`IEphemeralVerifierIdentityService`) — peer/doorstep.
- Desk `Sorcha.Verifier` → its **stable org** identity (holder's wallet shows a known requester).

### 7. Retirement
Remove `PresentationRequestBuilder`, `InMemoryVerifierSessionStore`, the local response endpoint, the
PWA paste `VerifyFlow`; fold `Outcome.razor` / `VerdictViewModel` into the shared verdict component.

---

## Staging (3 PRs — prodexec each separately)

**PR B1 — HAIP returns the raw vp_token on poll (+ tier check).** Small backend change to the verifier
result endpoint; confirm/allow the verifier token tier on create-request + poll. Independently testable
(integration test: create → direct-post → poll returns vp_token). No UI change.

**PR B2 — Shared verify control + transport seam + client-side verdict + config presets.** Extract the
question selector / request-QR-poll / verdict-trail into `Sorcha.UI.Components.User` behind
`IVerificationTransport` (HAIP impl) and `IVerificationPresetCatalogue`; extract `IRegisterAnchorClient`
to the shared lib; wire client-side validation via the engine. **Not yet wired into either host's route**
(or behind a flag) — lands the machinery + tests without changing live behaviour.

**PR B3 — Rewire both hosts + retire old paths.** Point PWA `/wallet/verify` and desk `Sorcha.Verifier`
at the shared control; delete the PWA paste `VerifyFlow`, the desk `PresentationRequestBuilder` /
`InMemoryVerifierSessionStore` / local callback / `Outcome.razor` bespoke pieces. Playwright happy-path
for `/wallet/verify`.

---

## Testing
- bUnit for the shared control's state machine (question → request → poll → verdict).
- Reuse `Sorcha.Verifier.Engine`'s existing validator tests for the verdict computation.
- Integration test: HAIP create → direct-post → poll(vp_token) → client-side validate round trip (PR B1/B2).
- Playwright happy-path for PWA `/wallet/verify` (PR B3).
- A `VerificationPreset` catalogue test (loads config, falls back to bundled default).

## Out of scope
- Camera on the verify surface (verifier *shows* the QR; camera-first stays a Present concern).
- The PWA visual/wording refresh (deferred tidy-up).
- Changing HAIP's server-side validation behaviour (untouched; only additive vp_token return).
