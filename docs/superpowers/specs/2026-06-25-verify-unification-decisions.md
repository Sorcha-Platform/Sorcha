# Verify unification — decisions captured (PR B, design pending)

**Date:** 2026-06-25
**Status:** Direction agreed; NOT yet a full implementable spec. Needs its own design session
before implementation/prodexec. Recorded here so the agreed decisions are not lost while PR A
(nav + Present camera-first) ships first.

## Agreed direction
- The richer, correct Verify experience already exists in **`Sorcha.Verifier`** (Blazor Server app
  at gateway route `/verify`): pick a preset question → build OID4VP request → show QR → poll
  session → render a **4-layer verdict trail** (selective disclosure, live presentation/KB-JWT,
  issuer signature, revocation, + optional register-anchor cross-check). The PWA's `VerifyFlow`
  (paste a JSON offer → pass/warn/fail) is the weaker one.
- **Lift the `Sorcha.Verifier` experience into a shared control** in
  `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/`, consumed by BOTH the PWA
  (`/wallet/verify`, reached from the home `VerifyHomeAction` tile) and `Sorcha.Verifier`. Upgrades
  then "flow through" to both surfaces.
- **Verify replaces paste.** The canonical model is verifier-initiated: pick what to verify → show
  QR → holder scans with their Present flow → result returns. The current paste-the-JSON
  `VerifyFlow` is retired (not kept as a hidden fallback).
- **Camera is Present-only.** In the verifier-initiated model the verifier *shows* the QR; it does
  not scan. No camera surface on Verify.
- **Single transport.** Move `Sorcha.Verifier` ALSO onto HAIP's existing
  `POST /api/v1/verifier/requests` + `request_uri` + `direct-post` + result-poll, so there is one
  verification transport across both hosts (replacing the desk verifier's current self-hosted
  in-memory session store + local `/verify/r/{sessionId}/response` callback + inline unsigned
  `presentation_definition`). The shared validation engine `Sorcha.Verifier.Engine` is already used
  by both, so verdicts stay identical.

## Open design questions to resolve before implementation
1. **Transport seam API.** Exact shape of the abstraction the shared control depends on
   ("create request → render QR → await result → return verdict model"). One interface, two host
   wirings (both pointing at HAIP).
2. **Desk verifier on HAIP.** How the server verifier's strengths survive the move: the 4-layer
   verdict trail, the on-demand **register-anchor cross-check** (`IRegisterAnchorClient`), issuer
   key resolution, and status-list caching — these must remain available when results come back via
   HAIP `direct-post` rather than the local callback.
3. **Ephemeral verifier identity** for the server host (the PWA uses
   `IEphemeralVerifierIdentityService`; the desk verifier mints a static org verifier identity) —
   reconcile into the shared control's contract.
4. **Component extraction.** Pull `Sorcha.Verifier`'s question-preset selector, QR/poll session UI,
   and verdict-trail into shared components; decide what stays host-specific.
5. **HAIP gaps.** Confirm HAIP's verifier endpoints expose everything both hosts need (e.g. the full
   verdict detail, anchor data) or extend them.
6. **Retirement.** Plan removal of `PresentationRequestBuilder` (inline), `InMemoryVerifierSessionStore`,
   the local response endpoint, and the PWA paste `VerifyFlow` once the shared control is live.

## Also deferred
PWA visual refresh + copy/wording (external design input) — folded into the post-refactor tidy-up.
