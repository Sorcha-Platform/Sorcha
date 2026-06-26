# Phase 0 Research: Onboarding Profile Capture

The spec's referenced design doc (`docs/superpowers/specs/2026-06-24-onboarding-profile-capture-design.md`)
was not present. This research resolves the open decisions by reading the existing onboarding, persona,
wallet-wizard, and auth surfaces. All file:line references were verified against the working tree.

---

## Decision 1 — Where the "Complete your profile" step lives

**Decision**: Implement the step as a reusable component in
`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Onboarding/CompleteProfileStep.razor`, consuming
the existing client `IPersonaService` (`Get`/`Update`). Sequence it into the web first-run journey at the
onboarding entry point (`Sorcha.UI.Web.Client/Pages/Home.razor`, which today routes a new user to
`wallets/create?wizard=true`).

**Rationale**:
- Persona CRUD already exists end-to-end: `GET/PUT/DELETE /api/me/persona`
  (`Sorcha.Tenant.Service/Endpoints/PersonaEndpoints.cs`), server `IPersonaService`/`PersonaService`,
  client `IPersonaService`/`PersonaHttpClient`
  (`Sorcha.UI.Components.User/Services/User/Persona/`), and the full edit form already exists at
  `Sorcha.UI.Web.Client/Pages/MyProfile.razor` (route `/profile`). The onboarding step is a focused
  subset of that form, not a new data path.
- Placing it in `Sorcha.UI.Components.User` follows the Feature 122 shared-component convention so the
  same step can be surfaced from the PWA (`Sorcha.Wallet.Pwa`) without duplication.

**Alternatives considered**:
- *Embed directly in `CreateWallet.razor`* — rejected: couples profile capture to wallet creation and
  isn't reusable by the PWA enrolment journey.
- *Reuse `MyProfile.razor` as-is inside onboarding* — rejected: that page is the full management surface
  (autofill toggle, address/phone/email list management, reset). Onboarding wants a minimal confirm step
  (FR-001, SC-001 "under 1 minute"), seeded from the same `IPersonaService`.

---

## Decision 2 — Persona write ordering vs. wallet provisioning

**Decision**: The profile step must run **after** the user has a provisioned wallet, OR must handle the
`409` returned by `PUT /api/me/persona` when no wallet exists, by surfacing a clear message and allowing
retry once the wallet is created. Default onboarding ordering: wallet creation → complete profile.

**Rationale**: `PUT /api/me/persona` encrypts the blob via the Wallet Service `IPersonaCryptoClient` keyed
on the user's primary wallet address (`WrappedKeyRef`). The endpoint returns **409 (wallet not
provisioned)** if there is no wallet yet (confirmed in `PersonaEndpoints.cs` handler contract). Capturing
profile values before a wallet exists would fail the save. This directly informs the onboarding step
order and the Edge-Case requirement that the flow must not silently advance on a failed save.

**Alternatives considered**:
- *Capture profile first, persist later* — rejected: requires holding plaintext PII client-side across
  steps and re-submitting; increases the window for the values to be lost (contradicts "retry without
  losing entered values" only if mishandled, and adds avoidable complexity).

---

## Decision 3 — Wallet defaults: 24-word default scoping

**Decision**: Do **not** change the global default on `CreateWalletRequest` (stays 12). Instead, the
onboarding entry point passes `words=24` (and a default `name=`) on the wizard URL
(`wallets/create?wizard=true&name=...&words=24`). `CreateWallet.razor` already reads `?name=` →
`DefaultName` and `?words=` → `DefaultWordCount` and applies them in `OnInitialized`, validating the word
count is one of `12|15|18|21|24`.

**Rationale**:
- Satisfies FR-006 (onboarding defaults to 24) **and** FR-009 (standalone wallet creation unchanged —
  no onboarding-specific default forced) with the least change, because the parameter plumbing already
  exists (`CreateWallet.razor` `[SupplyParameterFromQuery]` for `name`/`words`).
- Keeps the 12/15/18/21/24 selector and full user-editability (FR-008): defaults are seed values the user
  can override, and a back-navigation preserves the chosen value because it is bound to the request model.

**Alternatives considered**:
- *Flip `CreateWalletRequest.WordCount` default to 24 globally* — rejected: violates FR-009 (would change
  the standalone flow) and SC-005 (no regression to non-onboarding creation).
- *Add a dedicated `onboarding=true` flag that hard-codes 24* — rejected: redundant with the existing,
  more flexible `words=` parameter; the wizard already distinguishes wizard/first-login mode.

---

## Decision 4 — Source of `EmailVerified` on `/api/auth/me`

**Decision**: Mint an `email_verified` claim at token-issue time (sourced from
`PlatformUser.EmailVerified`) and read it in `GetCurrentUser` the same way every other field is read —
purely from `ClaimsPrincipal`. Add `EmailVerified` (a non-nullable `bool`, default `false`) to
`CurrentUserResponse`; absence of the claim ⇒ `false` (unambiguous "not verified / unknown", FR-011).
Refresh of the flag follows normal token lifecycle (re-issue on login / refresh), consistent with how
roles and org context already propagate.

**Rationale**:
- `GetCurrentUser` (`AuthEndpoints.cs:664`) is a pure claims projection — it injects only
  `ClaimsPrincipal`, no `DbContext` or service. Keeping it claims-only preserves that design and avoids a
  per-call DB read on a hot "who am I" endpoint.
- Email verification updates already re-touch the user (`EmailVerificationService.VerifyTokenAsync` sets
  `EmailVerified`/`EmailVerifiedAt`); surfacing the flag via the next token is consistent with how the
  platform already treats claim freshness. A `false` from a stale pre-verification token degrades safely
  (prompts re-verify) and never falsely implies "verified" (FR-011).

**Alternatives considered**:
- *Inject `DbContext`/user service into `GetCurrentUser` and look up live state* — rejected for the
  default path: adds a DB round-trip to every `/me` call and diverges from the claims-only handler design.
  Documented as the fallback if a future requirement demands real-time freshness (not required by this
  spec — "surfaces it rather than implementing verification").
- *Nullable `bool?`* — rejected: FR-011 wants an **unambiguous** representation. A non-nullable `bool`
  defaulting to `false` ("not known to be verified") is unambiguous; nullable invites "is null verified?"
  confusion at consumers.

**Open implementation note**: verify the token-mint path (`ITokenService` / claims builder in the Tenant
Service) is the single place that assembles user claims, and add `email_verified` there so both the web
and consumer tiers carry it. If multiple mint paths exist (login, refresh, social callback), all must add
the claim — capture as tasks in Phase 2.

---

## Decision 5 — Feedback + validation surfaces

**Decision**: The onboarding step uses `IInlineFeedback` for own-action success/error (Pattern #12); it
does **not** inject `ISnackbar`. Field-level validation reuses the existing `PersonaAttributesV1`
invariants enforced server-side (caps, E.164 phone, RFC-5322 email, ISO-3166 country) and mirrors the
client-side validation already present in `MyProfile.razor`. A failed `PUT` surfaces an inline error and
keeps the user on the step with entered values intact (Edge Cases; FR-005).

**Rationale**: Conforms to the snackbar-retirement ratchet (CI gate `scripts/check-no-snackbar.ps1`) and
reuses validation already proven on the persona path, so onboarding and `/profile` cannot diverge.

**Alternatives considered**: New bespoke validation — rejected (duplication, drift risk).

---

## Summary of resolved unknowns

| Unknown | Resolution |
|---------|------------|
| Where the profile step lives | New `CompleteProfileStep.razor` in `Sorcha.UI.Components.User/Components/Onboarding/`, consuming existing `IPersonaService`. |
| Profile vs wallet ordering | Wallet first (persona PUT needs a provisioned wallet; endpoint 409s otherwise) — step handles/retries on failure. |
| 24-word default scope | Onboarding entry passes `words=24` via existing query param; global default unchanged (FR-009/SC-005). |
| `EmailVerified` source | `email_verified` claim minted from `PlatformUser.EmailVerified`; read claims-only; non-nullable bool default false. |
| Feedback/validation | `IInlineFeedback` + reuse existing `PersonaAttributesV1` validation; no snackbar. |

No NEEDS CLARIFICATION markers remain.
