# Local credential presentation on `/app` — design

**Date:** 2026-07-28
**Tracking issue:** [#1330](https://github.com/Sorcha-Platform/Sorcha/issues/1330)
**Brief:** `docs/superpowers/briefs/2026-07-28-local-credential-presentation.md`
**Status:** approved (route + UX decided with Stuart, 2026-07-28)

## Problem

A citizen signed in on `/app`, whose own (server-custody) wallet holds a credential matching a
`presentationSource: SorchaWallet` gate, is shown a QR to scan with a phone. The gate cannot be
satisfied on the device the citizen is already using. The cross-device QR path must keep working
for phone-only credentials (do not regress #1327).

## Established facts (file:line evidence, read 2026-07-28)

1. **The wallet-service "presentation" endpoints do not build presentations.**
   `POST /api/v1/presentations/request` / `/{id}/submit` / `/{id}/result`
   (`PresentationEndpoints.cs:21-65`) are a self-contained legacy OID4VP mini-flow: in-memory
   `ConcurrentDictionary` store (`PresentationRequestService.cs:81`), `CanManageWallets`-gated, and
   `/submit` **verifies a caller-supplied vpToken** (`PresentationRequestService.cs:207-252`).
   Nothing in that flow calls `ISdJwtService.PresentAsync`/`CreatePresentationAsync`. (The
   `verifiable-credentials` skill's claim that `PresentationRequestService` "builds SD-JWT
   presentations with selective disclosure" is false; corrected as part of this work.)
2. **Server-custody KB-JWT signing exists.** `POST /api/v1/wallet/presentations/sign-kb`
   (`CitizenWalletEndpoints.cs:164-311`) signs a client-built KB-JWT signing input with the
   authenticated citizen's slot-108 holder key. `typ` must be `kb+jwt`; header `alg` must match the
   holder key.
3. **The F127 verifier accepts a holder-cnf presentation with no device binding.**
   `SorchaWalletPresentationConsumer.VerifyAsync` (`SorchaWalletPresentationConsumer.cs:144-166`)
   rebuilds the `VerifierSession` (nonce, client_id, vct, required claims) and validates through
   the shared `VerifiablePresentationValidator`, which carries the #1200 server-custody branch
   (holder-signed KB-JWT vs the credential's own `cnf.jwk`).
4. **Key binding is checked only on the async path.** The synchronous internal branch
   (`ActionExecutionService.cs:392` → `CredentialVerifier.cs:104-108`) builds `PresentedCredential`
   with no `ExpectedAudience`/`ExpectedNonce`, so `SdJwtVcFormatHandler.cs:116-128` runs
   issuer-only verification — an inline presentation is a replayable bearer artefact and writes no
   F111 lifecycle record.
5. **The whole local flow is already implemented and live-proven — just not in any UI.**
   `demos/AIAS/rehearse.ps1:476-625` (`Complete-SorchaWalletPresentation`) completes the F127
   SorchaWallet lifecycle as the holder with no device and no QR, using only the citizen's consumer
   bearer: GET request-object JWT → decode `nonce`/`client_id`/`response_uri`/`dcql_query` →
   `GET /api/v1/wallets/{addr}/credentials/{id}/export` → keep consented disclosure segments →
   `sd_hash` (SHA-256 over the hashable prefix) → sign-kb → DCQL object-keyed `vp_token` envelope →
   form-encoded direct_post to `response_uri`. Passed all 4 AIAS Cyber rehearsal paths on n1,
   2026-07-28.
6. **The pre-submit gate panel blocks submission on a selection nothing consumes.**
   `SorchaFormRenderer.razor:1153-1158` refuses submit until `CredentialGateSatisfied`;
   `CredentialGatePanel` sets it only after a Select dialog whose output is deliberately not
   forwarded (post-#1329 revert, `CredentialGatePanel.razor:208-228`).
7. **Client match surface**: `POST /api/v1/wallets/{addr}/credentials/match` returns
   `CredentialMatchResult { requirementType, matched, credentialId, issuerDid, expiresAt }`
   (`CredentialMatchResult.cs:11-42`) — no raw token, no claims. The raw token comes from the
   export endpoint. `GET /api/v1/wallet/holder-keys` returns the wallet address, holder JWK and
   algorithm from the bearer alone.

## Decision

**Route A — local-complete the async lifecycle.** Zero server change. The web client does what the
rehearsal script does. Rejected alternatives:

- *Re-apply `5c0ce81e` (inline sync threading)*: weaker gate (no key binding, no nonce — fact 4),
  skips the F111 register lifecycle record, and adds a second verification path to keep honest.
  The brief's §7.5 presumed this route before facts 4–5 were established; the brief is corrected.
- *Both routes*: scope without a consumer — no UI needs inline threading; walkthroughs post the
  API directly.

**UX — side-by-side, local primary.** When the probe finds a match: "Use this device" consent panel
primary, QR collapsed under "or scan with your phone". No match, or probe failure: QR primary,
exactly as today (probe failure silently degrades to QR — never a dead end). The citizen keeps the
choice; phone-only citizens see no change. With Route A the local route is cryptographically
identical to the phone route (nonce-bound KB-JWT, RFC 9901 anchoring, same verifier), so this is
product choice, not a security trade-off.

## Components

### `ISorchaWalletLocalPresenter` — new, `Sorcha.UI.Components.User/Services/User/Presentation/`

Registered in `AddSorchaPresentationGate` alongside `SorchaWalletGateTransport`, on a typed
HttpClient the host wires with `AuthenticatedHttpMessageHandler`.

- `ProbeAsync(presentationRequestUri, ct)` → `LocalPresentationCandidate?`
  1. Parse `request_uri` out of the `openid4vp://` authorization URI.
  2. GET the request-object JWT (anonymous, content type `application/oauth-authz-req+jwt`);
     base64url-decode the payload; read `nonce`, `client_id`, `response_uri`, `dcql_query`
     (query id, vct, required + optional claim names). Pure string/JSON — no crypto.
  3. `GET /api/v1/wallet/holder-keys` → wallet address (+ JWK, algorithm). 401/404 → no candidate.
  4. `POST /api/v1/wallets/{addr}/credentials/match` with a requirement built from the request
     object (vct + required claims). Not matched → no candidate.
  Returns candidate = { credentialId, walletAddress, requiredClaims, optionalClaims, nonce,
  clientId, responseUri, queryId, holderJwk, algorithm }.
- `PresentAsync(candidate, consentedClaims, ct)` → `LocalPresentResult` (Success / Declined / Error+reason)
  1. `GET /api/v1/wallets/{addr}/credentials/{id}/export` → raw SD-JWT.
  2. Split on `~`; keep only disclosure segments whose decoded claim name is consented
     (decode = base64url JSON `[salt, name, value]`).
  3. `sd_hash` = base64url(SHA-256(ASCII(`jwt~sel1~…~selN~`))) — `SHA256.HashData`, WASM-safe
     (the PWA `PresentationEngine` already does this in-browser).
  4. KB-JWT header `{ alg, typ: "kb+jwt", kid: thumbprint }`, payload
     `{ iat, exp: iat+120, aud: clientId, nonce, sd_hash }`;
     `POST /api/v1/wallet/presentations/sign-kb`; refuse on algorithm mismatch.
  5. `vp_token` = hashable prefix + KB-JWT; envelope `{ "<queryId>": ["<vp_token>"] }`;
     POST form-encoded `vp_token` + `state={requestId}` to `response_uri`.
  **Bearer rule:** the bearer is attached only when `response_uri` is same-origin — the request
  object is server-served (not a scanned QR), but the presenter keeps the #1310 discipline anyway.

### `PresentationRequestCard` — local route added to the card, not the transport seam

`IPresentationGateTransport` implementations are *waiting* strategies; presenting is an action the
card initiates. On mount with `Source == SorchaWallet`, fire `ProbeAsync` (resolved via
`IServiceProvider.GetService` so hosts without the presenter — or a probe throw — degrade to
QR-only). Match → consent panel primary (credential display name, required claims locked on,
optional claims toggleable **default on** — the Cyber agent hard-rejects portrait-less
presentations, so default-off would convert the happy path into a rejection), QR collapsed beneath.
"Share & continue" → `PresentAsync`; on Success the card does nothing further — the existing
`IPresentationSignal` (hub + `/status` poll) observes the outcome, so success/decline/expiry
handling stays one code path for both routes. A `PresentAsync` error surfaces inline on the panel
with the QR still available.

### `CredentialGatePanel` + `SorchaFormRenderer` — stop lying, stop blocking

For requirements whose `presentationSource` routes to the async lifecycle (SorchaWallet /
HaipExternalWallet), the panel is informational only: match status text, no Select dialog, no
contribution to `CredentialGateSatisfied`. The renderer's submit block
(`SorchaFormRenderer.razor:1153`) applies only to requirements with no presentation source (the
inline path no UI uses). The dead `FormContext.CredentialPresentations` mapping is left untouched.

## Testing & verification

- **Unit (presenter):** each step against mocked typed clients; a disclosure-strip round-trip test
  pinned against a real exported SD-JWT fixture; sd_hash pinned against a known-answer value
  computed from the same fixture the rehearsal path exercises. Every new guard mutation-tested:
  perturb, observe RED, restore.
- **bUnit (card):** local-primary on match; QR-only on no match; QR-only on probe throw; error on
  `PresentAsync` failure leaves QR usable.
- **Live gate on n1 (the evidence #1330 closes with):** submit the AIAS Cyber questionnaire on
  `/app` as a citizen holding an Assured Identity. Expect: no QR interaction, DevTools shows the
  request-object GET, sign-kb, direct_post, and `/api/presentations/{id}/status` polling (never
  `/api/v1/verifier/requests/`), submission completes, Cyber Level credential delivered. Then one
  cross-device QR run (phone) to prove #1327 is not regressed.

## Corrections shipped with this work (code wins)

- `verifiable-credentials` skill: `PresentationRequestService` builds nothing — fix the component
  table row and the `PresentationRequestService` prose.
- `CredentialGatePanel.razor` comment: the `/presentations/request → /{id}/submit` pointer is wrong
  (that flow verifies a caller-built token; separate legacy in-memory surface). Point at the real
  recipe (request object → export → sign-kb → direct_post).
- Brief §7.5: "re-apply `5c0ce81e`" presumed the sync route; corrected to record this decision.
- `seam-bugs-nothing-verifies-the-join` memory: add the new instance — a UI blocking gate
  (`CredentialGateSatisfied`) wired to a selection nothing consumes.

## Out of scope

- Any server-side change (endpoints, lifecycle, verifier, DTO threading).
- Multi-credential / `credential_sets` local consent (F181 US2 gap in the SorchaWallet consumer is
  pre-existing; the local panel handles the single-ask shape the consumer supports).
- Removing the inline sync verifier branch or the legacy `PresentationEndpoints` mini-flow.
