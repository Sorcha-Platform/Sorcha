# Verifier verdict screen — show what was verified

**Date:** 2026-07-18
**Status:** Design (approved 2026-07-18 — open to revision after in-person testing)
**Mockup:** https://claude.ai/code/artifact/6991c341-c22b-4ebb-abda-3fdfccee29fd
**Surface:** `Sorcha.Verifier` (web `/verify`), the wallet-PWA doorstep verifier, the shared `VerdictTrailPanel` / `VerdictViewModel` in `Sorcha.UI.Components.User`, and AIAS credential issuance.

---

## 1. Why

Today the web verifier shows a hardcoded **"Verification Complete / The credential was presented and verified successfully"** (`Sorcha.Verifier/Components/Pages/Index.razor:40`). It discards everything the credential actually carries — the portrait, the name, the disclosed claims, the issuer identity, and the four-layer trust trail. For an identity or age check the entire value is the human *seeing who/what was verified*; a bare "success" throws that away.

Separately, the rich verdict UI **already exists but is wired into nothing**: `VerdictViewModel` (captures portrait, disclosed name→value pairs, withheld list, issuer, `age_over_18`, register-anchor id, the layer results) and `VerdictTrailPanel.razor` (renders all of it) were built for F155 and have **zero consumers** today — neither the web verifier nor the PWA renders them.

## 2. What

**One shared component, wired into both surfaces.** `VerdictTrailPanel` becomes the single verdict surface, rendered by both the web verifier and the PWA doorstep verifier, so any future polish is one place, no divergence. The layout leads with the portrait + verdict for the 2-second in-person read; the trust detail sits one tap below.

### 2.1 Two treatments, one component (preset-adaptive)

The preset that was asked drives the **header block** and the **disclosure set**; everything below (issuer line, trust trail) is identical.

- **Confirm identity** — green "Identity verified" banner; **large portrait** ("Compare to the person present"); the **name**; disclosed values (full name, photo); a **Withheld** line (date of birth); issuer line; trust trail (collapsed).
- **Age over 18?** — the **answer is the hero**: a large "**18+ ✓ / Over 18 — confirmed**"; a **smaller portrait** ("Confirm it's the same person" — the age preset requests the photo so staff can bind the answer to the person); an explicit **minimal-disclosure statement** ("You learned only that they're over 18… you did not learn their name, birth date, or exact age"); disclosed (age over 18: yes, photo); Withheld (name · DOB · exact age); issuer; trust trail.

### 2.2 The four-layer trust trail (progressive, collapsed by default)

Live presentation · Issuer signature · Not revoked · Register-anchored, each with a Pass / Fail / Unverified status pill. The first three come from `VerificationOutcome.Layers`; the **register-anchor** check stays **on-demand** ("Tap to check") — it is the only layer that touches the network.

### 2.3 Fail / warn states

Reuse the same layout with semantic colour (semantic ≠ the indigo accent):
- **Fail** — red banner, the failing layer named, no disclosed identity presented as trusted.
- **Warn (reduced assurance)** — amber banner ("verified with reduced assurance"), the reason named. This is the documented offline-PWA path where the issuer signature could not be checked (`RealVerifierEngine` maps `Accepted + NotVerified` → `Warn`); never render that as a plain pass.

## 3. Companion fix (hard dependency): `age_over_18` at issuance

The "Age over 18?" preset requires an `age_over_18` claim, but the AIAS credential only carries `dateOfBirth`, so matching finds nothing ("none of your credentials match https://sorcha.dev/vc/assured-identity/v1"). **AIAS issuance must derive and include an `age_over_18` boolean** (computed from date of birth at issue time) — the EUDI / ISO 18013-5 mDL `age_over_NN` pattern, and better privacy than disclosing DOB. The verify age-screen has no claim to disclose without it.

Structured so more thresholds (`age_over_21`, …) are a trivial addition later; only `age_over_18` is in scope now.

## 4. Scope

- Design/polish the shared `VerdictTrailPanel` per the mockup (identity + age treatments, fail/warn).
- **Wire it into `Sorcha.Verifier`** (web `/verify`) — replace the bare `Index.razor` success message with the panel driven by the real `VerificationOutcome`.
- **Wire it into the PWA doorstep verifier** (the `RealVerifierEngine` result surface).
- **AIAS issuance:** add `age_over_18`.

## 5. Out of scope

- ZK age predicates (issuer-derived booleans instead).
- Any change to the verify transport / presentation flow — this is the **result screen** only.
- `age_over_21` and other thresholds (trivial follow-on once `age_over_18` lands).

## 6. Open questions (deferred to post-IRL revision)

The design is approved to build; these are refinements the user will settle after using it in person: portrait size; issuer logo vs name-only; "18+" numeral vs "Over 18 ✓" in words; a photo-free age variant for pure online age gates. Decision on record: **keep the photo on the age screen** (in-person venues).

## 7. Success criteria

- **SC-1** A successful identity verification shows the portrait, name, disclosed values, issuer, and the collapsible trust trail — not a bare "Verification Complete".
- **SC-2** An "Age over 18?" verification (once `age_over_18` is issued) matches, and the result leads with "Over 18 — confirmed" + photo while withholding name/DOB/exact age.
- **SC-3** Both the web verifier and the PWA doorstep verifier render the same shared `VerdictTrailPanel`.
- **SC-4** Fail and reduced-assurance (warn) outcomes are visually distinct and name the reason; a warn never reads as a plain pass.
