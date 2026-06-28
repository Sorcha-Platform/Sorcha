# Phase 0 Research: Web Step-Up Social Account Linking (B-UI)

All Technical Context items resolved. No open `NEEDS CLARIFICATION` remain in this feature's own
scope. One **prerequisite/sequencing risk** (Feature 168 backend availability) is documented below.

---

## R1 — Feature 168 server contract (the endpoints this feature consumes)

**Decision**: Code the anonymous client service against the three F168 endpoints with the
**link-pending token as the principal** (no bearer session):

| Step | Method + Path | Principal | Returns |
|------|---------------|-----------|---------|
| Initiate | `POST /api/auth/social/link/challenge/initiate` | link-pending token (body) | `{ method, payload? }` — `payload` carries WebAuthn request options for Passkey, null for TOTP |
| Verify | `POST /api/auth/social/link/challenge/verify` | link-pending token (body) | `{ token: "ch_…", expiresIn: 300 }` single-use challenge token |
| Confirm | `POST /api/auth/social/link/confirm` | link-pending token (body) + `X-Auth-Challenge: ch_…` (header) | `{ accessToken, refreshToken?, expiresIn }` — same shape as normal social sign-in |

**Rationale**: Matches the spec's Key Entities (link-pending token, challenge token, session tokens)
and the established Sorcha challenge ladder shape already used by the authenticated
`/api/auth/challenge/*` flow (wire-compatible `ChallengeMethod`; `X-Auth-Challenge` header at the
mutation step). Status-code semantics the prompt must surface (server-enforced): `401`
invalid/expired token or rejected proof; `403 proof_tier_insufficient` / account mismatch; `409`
link conflict (already linked / email collision); `429` rate limited.

**Alternatives considered**: Reusing the authenticated `IAuthMethodsClientService.InitiateChallenge/
VerifyChallenge` — **rejected**: those assume a signed-in bearer principal and a `ScopedOperation`
on the caller's own account; this flow has no session, only the opaque link-pending token. Forcing
them together would violate FR-012 (separate surface) and FR-014’s reuse-without-duplication intent
by overloading authenticated methods with an anonymous code path.

**⚠ Prerequisite risk**: A repo-wide search on this branch finds **no** `LinkRequired`,
`linkPendingToken`, or `SocialLinkStepUpEndpoints` — F168 is **not present in this worktree**. The
spec explicitly lists F168 as an external dependency (Assumptions). Consequence for sequencing:
client unit tests (mocked HTTP) can be built and pass now; Playwright E2E and live verification
require F168 to be merged/deployed first. Captured as a plan-level risk, not a scope ambiguity.

---

## R2 — Fragment detection & token stripping (FR-001, FR-002, FR-003, SC-005)

**Decision**: Extend `Sorcha.UI.Web/wwwroot/app/js/fragment-handoff.js` to recognise an
`outcome=LinkRequired` fragment carrying `linkPendingToken`, stage it (window global +
`localStorage`), and `history.replaceState` the URL clean — mirroring the existing `token` path.

**Rationale**: The current script (verified) only stages when a `token` param is present and only
then clears the fragment. A `LinkRequired` fragment has **no** `token`, so today it is neither staged
nor stripped → the dead-end and a lingering token in the address bar. Extending the same eager,
pre-Blazor-boot script is the smallest change that satisfies both FR-002 (strip immediately) and
SC-005 (never persists on reload/back-nav), and it reuses the proven staging mechanism.

**Alternatives considered**: Reading `window.location.hash` from a Blazor component after boot —
**rejected**: races `AuthorizeRouteView` and leaves the token in the URL during WASM boot (the very
reason the existing eager script exists). Server-side interception — **rejected**: out of scope (web
UI feature; F168 owns the redirect).

**Absent/malformed fragment (FR-003)**: when no recognised outcome is staged, the gate is inert and
the standard signed-out home renders. Reload after the token has been cleared → nothing staged →
signed-out home (no crash, no partial link — edge case "Fragment refresh / deep-link").

---

## R3 — Where the prompt is hosted at boot (anonymous, pre-auth)

**Decision**: Add a `LinkRequiredGate.razor` mounted in `Routes.razor` **alongside**
`FragmentTokenHandler` (i.e. outside `AuthorizeRouteView`, so it runs with no session). On first
render it asks the JS for a staged link-pending outcome; if present it renders
`LinkExistingAccountPrompt` (full-screen takeover) instead of the signed-out home.

**Rationale**: `Routes.razor` already hosts `FragmentTokenHandler` at the top, outside the authorize
gate — the established seam for pre-auth boot logic. An anonymous prompt must render without a
`ClaimsPrincipal`, so it cannot live behind `[Authorize]`/`AuthorizeRouteView`.

**Alternatives considered**: A routable `@page "/app/link-account"` — rejected: would require
navigating (re-introducing URL state for an opaque token) and an extra redirect; the gate model
keeps the token purely in volatile staging.

---

## R4 — Passkey ceremony reuse (FR-008, FR-014)

**Decision**: Reuse `PasskeyInteropService.GetCredentialAsync(JsonElement options)` (verified) to
run `navigator.credentials.get()` over `webauthn.js`, feeding the initiate `payload` in and posting
the returned assertion `JsonElement` to verify as the `proof`.

**Rationale**: Exact capability already shipped for the authenticated step-up; handles base64url
codec per FIDO2. Reusing it satisfies FR-014 (no parallel duplicate of the ceremony) without
touching `AuthChallengeDialog`.

**Alternatives considered**: New WebAuthn interop — rejected (duplication, FR-014).

---

## R5 — TOTP code proof reuse (FR-009)

**Decision**: For the authenticator-code method, collect a 6-digit numeric code in the prompt and
post it as the verify `proof` (`{ "code": "123456" }`) through the **new anonymous client service**
— not through `ITotpClientService`.

**Rationale**: `ITotpClientService.ValidateCodeAsync` targets the signed-in user's own TOTP via
authenticated routes; this flow validates against the *target* account addressed by the link-pending
token on the anonymous verify endpoint. The reuse mandated by FR-014 is the *input collection +
proof shaping*, which is trivial and lives in the prompt; the transport differs by principal. The
wire enum `ChallengeMethod.Totp` and `ChallengeVerifyError` are reused as-is.

**Alternatives considered**: Routing TOTP through `ITotpClientService` — rejected: wrong principal
and wrong endpoint; would couple anonymous flow to authenticated code paths.

---

## R6 — Session establishment on confirm success (FR-011, SC-006)

**Decision**: On confirm, take the returned `{ accessToken, refreshToken }` and drive the **existing**
post-sign-in path: stage via the same handoff mechanism (or persist directly through `ITokenCache`)
then trigger `CustomAuthenticationStateProvider` to re-evaluate
(`NotifyAuthenticationStateChanged`), leaving the user signed in exactly as a normal social callback
would.

**Rationale**: The spec (Assumptions) calls for the same session-establishment path as the social
callback. `CustomAuthenticationStateProvider.TryConsumeFragmentTokenAsync` already validates expiry,
stores to `ITokenCache`, and clears staging; reusing it guarantees identical signed-in state and
avoids a second token-handling code path. SC-006 (permanent link) is a server property once the link
is committed.

**Alternatives considered**: Bespoke token persistence in the prompt — rejected: duplicates session
logic and risks divergent auth state.

---

## R7 — Proof-method selection & v1 set (FR-006, FR-007, FR-018, Edge: no reusable method)

**Decision**: Offer **only** Passkey + TOTP in v1; never offer ReOAuth or bare password. Present the
server-indicated method from `initiate`; when the account supports both v1 methods prefer Passkey
with a "use authenticator code instead" switch. If the server indicates a method outside the v1 set
(e.g. only password/ReOAuth viable), render the recovery path ("sign in with your existing method")
rather than a dead end.

**Rationale**: Directly encodes FR-006/FR-007/FR-018 and the "No reusable v1 method" edge case. The
server floors the proof tier; the prompt only renders what it can satisfy and routes the rest to
recovery.

**Alternatives considered**: Implementing ReOAuth now — rejected: explicitly deferred by the spec.

---

## R8 — Feedback surface (FR-019)

**Decision**: Use `IInlineFeedback` (Show{Success,Error,Info,Warning}) for all user-facing messages;
errors that must be acknowledged use `autoDismissMs: 0`. No `ISnackbar`.

**Rationale**: Mandated by FR-019 and CLAUDE.md Critical Pattern #12 (snackbar retired for
user-facing surfaces). `InlineFeedbackHost` is already mounted in `MainLayout`; for the pre-auth
takeover the prompt renders inline `MudAlert` for in-component errors where the layout host is not
guaranteed mounted, consistent with the dialog-content micro-rule.

---

## R9 — Isolation from Feature 150/116 (FR-012, FR-013, SC-007)

**Decision**: All new code is **net-new files** in a new `AccountLink` folder (component) and new
service/model files; **zero edits** to `AuthChallengeDialog.razor`, `SecurityHome.razor`,
`PasswordSection`, `PasskeysSection`, `SocialLinksSection`, `TwoFactorSection`, `AssuranceBadge`.
Reuse is by **consuming** `PasskeyInteropService`, `IInlineFeedback`, the challenge enums, and the
auth-state provider — not by editing them.

**Rationale**: SC-007 is verifiable by diff (no edits to those component files); FR-012/FR-013
satisfied structurally.

**Verification approach**: a diff-based check that none of the listed F150/116 component files are
modified by this branch.
