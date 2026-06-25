# Onboarding Profile Capture (Feature 157) — design

**Date:** 2026-06-24
**Status:** Design — pending review
**Spec home:** `specs/157-onboarding-profile-capture/`
**Author:** brainstormed with Stuart

---

## Problem

A new citizen signs up (passkey, social, or email/password), runs the first-time
wallet setup wizard, and is then immediately asked — by AssuredIdentity or any
council form — to type their name, date of birth, email, and address from
scratch. The autofill machinery that should prevent this **already exists and is
fully wired**, but it never fires for a new citizen.

### Why it never fires (the root cause)

- The **persona** (Feature 092/125) is the platform's reusable, encrypted,
  self-asserted profile (`givenName`, `familyName`, `dateOfBirth`, `emails[]`,
  `phones[]`, `addresses[]`, `nationalities[]`). `SorchaFormRenderer` +
  `PersonaAutofillResolver` autofill any form field carrying an `x-persona` tag
  from it.
- **AssuredIdentity's form already carries explicit `x-persona` tags on all
  twelve citizen fields** (`givenName`, `familyName`, `dateOfBirth`,
  `defaultEmail`, every `address.*` component). There is nothing to mark up.
- The autofill produces **~0% fill** for one reason only: the **persona is empty**
  for every citizen until they manually visit MyProfile. Nothing seeds it.

The "claims weren't cached" symptom that prompted this work is therefore *not* a
caching bug and *not* missing markup. It is simply that **no step ever populates
the persona**, and AssuredIdentity issuance reads the *submitted form* (via
`BuildClaimsFromMappings`), not the persona — so an empty persona means the
citizen re-types everything.

### The chicken-and-egg

The persona cannot be seeded at sign-up: its content key is wallet-derived, and
the wallet does not exist until the citizen completes the first-time wallet setup
wizard. So the only correct place to seed the persona is **immediately after the
wallet exists** — i.e. as the final step of that wizard.

## Goal

Make the citizen enter their core identity data **once**, at the natural moment
the wallet is created, so every subsequent form autofills. Privacy-respecting
(only what the citizen gives or enters, stored encrypted), and on the "easy
route" rail (minimal friction, minimal required fields).

## Non-goals

- No change to how AssuredIdentity issuance sources its claims.
- No new credential→persona writeback (the "reverse arrow"; a separate future
  feature — `PersonaAttributeSource.VerifiedCredential` stays reserved/unused).
- No phone capture in this feature (AssuredIdentity does not use it; F150 is
  separately adding SMS OTP). Phone remains editable later in MyProfile.
- No schema change, no migration, no sign-up-callback change (see below).

## Scope decisions (locked with Stuart)

1. **Data scope: AssuredIdentity-shaped.** Name, date of birth, email, postal
   address — the fields the highest-value forms need. Nothing speculative.
2. **Gating: a mandatory final step of the wizard, with one required field.**
   It is not a dismissible page; it is the last step of first-time setup.
   **Only the name blocks "Finish."** DOB and address are optional. Email is
   captured but **never gates** (see #4).
3. **No database changes.** The original plan to persist social
   `given_name`/`family_name` in new `PlatformUser` columns is **dropped** — that
   would be new *plaintext* PII at rest, less private than the persona, for a
   marginal pre-fill gain. The name field pre-fills from the **`DisplayName`
   already stored**; since the citizen must review the name to finish, an
   imperfect split is corrected in place. The only durable new PII this feature
   creates lands **encrypted in the persona**.
4. **Email verification is informational, not a gate.** We show "✓ verified"
   when `EmailVerified` is true; an unverified email never blocks Finish and we
   do not run inline verification mid-wizard. The email is captured into the
   persona regardless. Its trustworthiness downstream is governed by the existing
   `PlatformUser.EmailVerified` flag — an unverified email is simply not *relied
   upon* by a later credential application, with no wizard gate required.

## Architecture & flow

The first-time wallet wizard (`CreateWallet.razor`, when `first-login=true` or
`wizard=true`) gains a third step. The wallet exists by step 3, which is what
unblocks the persona write.

```
Step 1  Create wallet      name defaults to "default"; word count 12 or 24 (default 24)
Step 2  Back up phrase      (unchanged)
        ── wallet set as default + JWT refreshed (wallet_address claim) ──
Step 3  Complete profile    NEW — seeds the persona
        ├─ Name (given/family)  pre-filled from DisplayName, editable   [REQUIRED]
        ├─ Email                pre-filled; "✓ verified" shown if so     (not gated)
        ├─ Date of birth                                                 (optional)
        └─ Postal address       (postcode lookup already wired)          (optional)
        └─ Finish → PUT /me/persona → /dashboard
```

After this, AssuredIdentity and every `x-persona`-tagged form autofills from the
seeded persona — no new markup.

**Ordering is load-bearing:** the persona content-key is wallet-derived, so the
default-wallet set + JWT refresh (the `wallet_address` claim) MUST complete
*before* Step 3 runs, not at the very end of the wizard as today. Step 3's
`PUT /me/persona` resolves the user's preferred wallet; without the default set
first it would fail `PersonaWalletNotProvisionedException`.

## Components & changes

### A — Wallet wizard simplification (also a latent bug fix)

`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/CreateWallet.razor` and
`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Wallet/CreateWalletRequest.cs`:

- `CreateWalletRequest.Name` default → `"default"` (still editable; still 1–100 chars).
- Word-count `MudSelect` keeps only **12** and **24**; default **24**.
- This removes a live correctness trap: `Mnemonic.Generate`
  (`src/Core/Sorcha.Wallet.Portable/Domain/ValueObjects/Mnemonic.cs`) maps any
  non-24 word count to `Twelve`, so selecting 15/18/21 today silently produces a
  12-word phrase. Removing those options eliminates the mismatch. (Leave
  `Bip39WordCountAttribute`'s accepted set as-is — it is a valid BIP-39 validator;
  we are only narrowing the UI choices.)

### C — "Complete your profile" wizard step (the main piece)

- New step in the wizard's `WizardStep` enum: `Form → MnemonicDisplay →
  CompleteProfile`. The step renders **only** in first-login/wizard mode (a
  returning user creating an additional wallet does not see it).
- New reusable component in **`Sorcha.UI.Components.User`** (so the PWA could
  adopt it later) — e.g. `Components/Onboarding/CompleteProfileStep.razor`,
  namespace `Sorcha.UI.Core.Components.Onboarding` per the RootNamespace
  convention.
- **Pre-fill source on load:**
  - Read the existing persona first (`IPersonaService.GetAsync()`); if already
    populated (mid-wizard refresh / returning), pre-fill from it — Step 3 is
    idempotent and never forces a redo.
  - Otherwise pre-fill name from `DisplayName` and email + verified status from
    the `/api/auth/me` response (see the one-field addition below).
- **Required to finish:** given name + family name. DOB and address optional.
- **On Finish:** build `PersonaAttributesV1`:
  - `givenName`, `familyName`, derived `fullName`,
  - `emails: [{ value: <email>, isDefault: true }]`,
  - `dateOfBirth` if entered,
  - `addresses: [{ ...lineN/town/region/postcode/country, isDefault: true }]` if entered,
  - all `Source = SelfAsserted` (v1).
  - Call the existing `PUT /me/persona` via `IPersonaService.UpdateAsync(...)`.
- **Feedback:** on success, `IInlineFeedback` is not appropriate inside the
  wizard chrome; navigate straight to `/dashboard` (matching today's
  `NavigateToWallet`). On persona write failure, render an inline
  `MudAlert Severity="Error"` in the step body and keep the citizen on Step 3
  with a retry (do **not** silently advance — a lost persona is the whole point
  of the feature).

### One-field read addition (no schema change)

`GET /api/auth/me` (`AuthEndpoints.cs`) currently returns
`CurrentUserResponse` from JWT claims and does **not** include `EmailVerified`.
Add `EmailVerified` to that response so Step 3 can show the "✓ verified" tick.
This is a **response-shape** addition only — no entity/table/migration change.
Source it from the `PlatformUser.EmailVerified` (read) or a verified claim if one
is present on the token; a DB read in the `/me` handler is acceptable for one
boolean.

## Error handling & edge cases

| Case | Behaviour |
|---|---|
| Persona write needs a wallet | Guaranteed present by ordering; ensure default-wallet set + JWT refresh happen *before* Step 3. |
| Email not verified | Show without the ✓ tick; never blocks Finish; captured into persona anyway; downstream usability keyed off `EmailVerified`. |
| No usable seed name (email/password, passkey-no-name) | Name fields render empty + required; citizen types once. |
| `DisplayName` is a full name needing a split | Best-effort split into given/family for pre-fill; citizen reviews (it's the required field) and corrects. |
| Refresh / re-entry mid-wizard | Step 3 reads current persona first; if populated, pre-fills from it; idempotent. |
| `PUT /me/persona` fails | Inline error + retry on Step 3; no silent advance. |

## Testing

- **Playwright E2E** (per the `sorcha-ui` skill, against the Docker stack): the
  first-time wizard is now three steps; assert Step 3 pre-fills name + email,
  blocks Finish with name cleared, writes the persona, and that a subsequent
  AssuredIdentity form renders with its `x-persona` fields autofilled. New
  `data-testid`s on every Step 3 field + the Finish button.
- **Unit / bUnit:**
  - Word-count select offers only 12/24 and defaults to 24; default name is
    `"default"`.
  - `CompleteProfileStep` maps inputs → `PersonaAttributesV1` correctly
    (name, fullName derivation, default email, optional DOB/address).
  - `CompleteProfileStep` pre-fills from an existing persona when present
    (idempotency).
  - `GET /api/auth/me` includes `EmailVerified`.

## What this feature explicitly does NOT touch

- AssuredIdentity blueprint / issuance path (already correct).
- Persona crypto, storage, or the `PUT /me/persona` contract (reused as-is).
- Sign-up callbacks, `PlatformUser`/`PlatformSocialLogin` schema, migrations.
- The reverse arrow (credential → persona writeback) — future feature.

## Open follow-ups (not in scope)

- Credential → persona writeback (`Source = VerifiedCredential`).
- Phone capture + verification (rides F150 SMS OTP when it lands).
- PWA adoption of `CompleteProfileStep` (wallet creation is web-only today; the
  component is placed in `Sorcha.UI.Components.User` to make this cheap later).
