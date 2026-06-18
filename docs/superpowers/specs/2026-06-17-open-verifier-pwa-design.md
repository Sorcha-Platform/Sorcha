# Open Verifier PWA — present-then-cross-check-the-register-anchor

**Date:** 2026-06-17
**Status:** Design approved (brainstorm), ready for Spec Kit
**Scope:** Demo-focused. Eventually product-grade + open-register + offline, phased.

---

## 1. Summary

Evolve the existing reference verifier (`src/Apps/Sorcha.Verifier`, Blazor Server) into an
**installable PWA** that performs a **present-then-cross-check** verification: a citizen presents a
credential over OpenID4VP (the existing QR / `direct_post` flow), and the verifier then **cross-checks
that credential against its anchor on the public register**, rendering the result as a **verdict with
progressive drill-down**.

The headline capability is an **open verifier**: no pre-shared issuer allowlist. The verifier
resolves and verifies everything reachable *from the credential itself*, surfaces the **issuer's
identity** prominently, and leaves the "do I trust this issuer for this purpose" judgement to the
human/policy. It proves **authenticity, integrity, non-revocation, and on-ledger anchoring** — it
does not assert issuer reputation.

The motivating demo: an **Assured Identity** credential (name, address, age, portrait) issued by a
council. The operator asks **"Age over 18?"**; the wallet discloses only `age_over_18 = true` + the
portrait; the verifier shows a clean **Over 18 ✓** verdict and an expandable trail proving the four
validation layers, ending with a "verify inclusion proof against the register" beat.

---

## 2. The four-layer standards stack

The cross-check is a layered stack of open standards. Each layer answers a *different* question; a
credential can pass any subset (e.g. signature-valid but revoked, or status-clean but never anchored).

| Layer | Question | Standard | Status in Sorcha |
|---|---|---|---|
| 1 — Live presentation | Is the live holder actually presenting it? | OpenID4VP + KB-JWT (nonce + audience) | exists (engine) |
| 2 — Issuer signature | Who signed it, and is the signature valid? | W3C DID Core resolution + JWS verify | exists (engine, F135 trust evaluator) |
| 3 — Revocation | Is it still valid right now? | IETF Token Status List 2024 (+ W3C Bitstring) | exists (engine) |
| 4 — Register anchor | Was it genuinely recorded on the public register? | F079 receipts + Merkle inclusion proof (SCITT-aligned) | **new UI + lookup** |

Layers 1–3 already run inside `Sorcha.Verifier.Engine` (the unified `ITrustEvaluator` from
Feature 135). The verifier UI currently collapses all of this into a single outcome; this work
**surfaces** each layer's detail. Layer 4 is the new build.

**Trust posture:** fully open — resolve-and-verify, `requireIssuerSignature: true` (the signature is
genuinely checked), **no allowlist**. The verdict names the issuer org (display name + DID).

### "Age over 18?" expressed interoperably

The credential is **issued** with selectively-disclosable boolean age claims — `age_over_18: true`
(ISO 18013-5 mdoc defines `age_over_NN` data elements for exactly this; the SD-JWT VC analogue is a
disclosable boolean claim). On an over-18 check the wallet discloses **only** `age_over_18` (+ the
portrait for a human face-match); DOB, name, and address never leave the wallet. This is real
selective disclosure on the genuine OID4VP/SD-JWT path.

Out of scope: disclosing DOB and computing age (worse privacy, fallback only); true zero-knowledge
predicate proofs (BBS / AnonCreds — not in SD-JWT VC or mdoc, not in Sorcha).

---

## 3. UI / flow

Three screens (the existing shape, redesigned). Visual language: **MudBlazor + the existing
`IdCardLayout` + Sorcha colour themes** — a sibling of the wallet, not a separate tool.

1. **Ask screen** — operator picks **question presets** ("Age over 18?", "Confirm identity",
   "Custom…") that map to a `vct` + required-claims set under the hood. "Age over 18?" requests
   `age_over_18` + `picture` only. Builds the OID4VP request (existing `IPresentationRequestBuilder`
   plumbing, unchanged).
2. **QR session screen** — renders the `openid4vp://` QR (cross-device, `direct_post`), polls the
   session store. Transport unchanged from today.
3. **Verdict screen** — **Direction B (ID-card + validation trail), wallet look**: the
   `IdCardLayout` card up top (portrait + "Over 18 ✓" + `age_over_18=true` chip + issuer org name &
   DID), then the **four-layer validation trail** as a timeline. Each step: label on the left;
   status text + tick + expand `▾` grouped on the right. Detail panels collapsed by default
   (progressive disclosure).

### Verdict trail — per-step expanded detail

- **Live presentation** — protocol (OpenID4VP · direct_post), nonce matches request, audience = this
  verifier, KB-JWT EdDSA holder-bound + freshness.
- **Selective disclosure** — **disclosed (2):** `age_over_18`, `picture`; **withheld (n):** the
  held-back claims, struck through, labelled "never left the wallet". (This is the layer that makes
  minimal disclosure *visible* — without it an over-18 check looks identical to handing over full ID.)
- **Issuer signature** — `iss` DID, `kid`, `alg`, "resolved via DID document · assertionMethod match".
- **Not revoked** — IETF Token Status List 2024, status-list URI, index, `status=0 (valid)`,
  list-fresh.
- **On the public register** — the anchor (from the credential), docket #N + sealed timestamp,
  "Merkle inclusion proof verified ✓", and an **export verification bundle (offline-checkable)**
  affordance. Rendered as an explicit "tap to verify inclusion proof" beat.

Mockups (approved): `.superpowers/brainstorm/.../verdict-trail-expanded-v3.html`.

---

## 4. Layer-4 design — register anchor

**A credential cannot embed its own issuance `txId`** (the SD-JWT is built before the issuance
transaction is sealed). So "self-anchoring" means the credential carries the **`registerId` + its own
`credentialId`/`jti`** (both known at issuance); the verifier resolves these to the sealed tx +
Merkle inclusion proof in one public read. Everything still starts from the credential.

Three pieces:

1. **Issuance tweak** — the Assured Identity credential includes an anchor claim
   (`registerId` + `credentialId`). No new crypto.
2. **One public read path** — given `(registerId, credentialId)`, return the issuance tx + F079
   inclusion proof. F079 already exposes `/inclusion-proof` and `/verification-bundle` *by txId*; the
   new bit is a **public find-issuance-by-credentialId** lookup. (Alternative considered and rejected
   for the demo: post-seal re-anchoring so the delivered credential carries the real txId — bigger,
   two-pass issuance.)
3. **Verifier client + UI** — calls the read, verifies the proof in-engine, renders the layer-4 trail
   step and the exportable verification bundle.

---

## 5. PWA shell (delivery path A)

Keep Blazor **Server**; add installability. (Path B — convert to Blazor WASM for on-device/offline
verification — is documented roadmap, not in scope. A verifier inherently consults public data
online, so "installable but online" is the right trade for a verifier; offline pairs with the
same-device W3C Digital Credentials API, a B-era concern.)

- Web manifest + icons (192 / 512 / maskable) + meta tags in the host page.
- Hand-written service worker (no Blazor Server SW template): cache the static shell + an
  offline-fallback page; the circuit itself can't be cached.
- `beforeinstallprompt` capture + an install button.
- **Service-worker scope must cover the `/verify/` gateway mount** (path-prefix gotcha class — same
  family that bit the wallet PWA; manifest + SW must be served and scoped correctly behind YARP).

---

## 6. Out of scope (roadmap)

- **Path B** — WASM / on-device / offline verifier (+ same-device W3C Digital Credentials API).
- **Hard trusted-issuer allowlist** — fully-open only for now; pinned-issuer mode is a later option.
- **True ZK age predicates** — BBS / AnonCreds; not in SD-JWT VC or mdoc.
- **External X.509 / EUDI rail** — and anything requiring Ed25519 certs (known-flaky 25519 path;
  also `X509CertificateBuilder` is P-256-only). The open verifier uses the register/DID-native trust
  rail exclusively.
- **mdoc presentation** — F135 supports `mso_mdoc` behind OID4VP; the demo is SD-JWT VC. Noted as
  "also possible".
- Product-grade hardening (multi-tenant verifier config, audit, rate-limit tuning).

---

## 7. Demo script

1. Open the installed **Verifier** PWA → tap **"Age over 18?"**.
2. QR appears → citizen scans with the wallet PWA, approves disclosing **`age_over_18` + portrait**.
3. Verdict: **Over 18 ✓**, portrait, **issued by Strathcarron Council**.
4. Operator expands the trail; the **withheld** list proves minimal disclosure; taps **"verify
   inclusion proof"** → **anchored ✓** in docket #N.
5. (Flourish) export the offline-checkable verification bundle.

---

## 8. Setup prerequisites (call-outs, not debugging tasks)

- The issuing org **must have an org master key** (`Set-SorchaOrgMasterKey`) so the issuer signature
  actually verifies — otherwise it falls to the bare-wallet-`iss` path with an unresolvable key.
  AssuredIdentity historically provisioned HAIP enrolment only and skipped this.
- The credential must be issued with the **`age_over_18` boolean + `picture` + the anchor claim**
  (`registerId` + `credentialId`).
- The verifier must reach the **public** register + status-list endpoints from inside Docker (the
  issue-#808 networking class: server-side fetch to the public gateway URL can be unreachable from
  inside the container — resolve in planning).

---

## 9. Testing

- **Playwright E2E against Docker** (per the `sorcha-ui` skill) — the three screens + verdict trail,
  console/network/CSS health, screenshots on failure.
- **Unit tests** — anchor-claim parsing, the find-issuance-by-credentialId client, inclusion-proof
  verification wiring.
- Layers 1–3 lean on existing `Sorcha.Verifier.Engine` tests.

---

## 10. Files (indicative)

- `src/Apps/Sorcha.Verifier/Components/Pages/Index.razor` — Ask screen (question presets).
- `src/Apps/Sorcha.Verifier/Components/Pages/Outcome.razor` — Verdict screen + validation trail.
- `src/Apps/Sorcha.Verifier/` — manifest, icons, service worker, install prompt JS.
- `src/Apps/Sorcha.Verifier/Services/` — register-anchor client + inclusion-proof verifier wiring.
- Register Service — public find-issuance-by-credentialId lookup endpoint.
- Issuance / blueprint — Assured Identity credential gains `age_over_18` + anchor claim.
- `walkthroughs/AssuredIdentity/` — over-18 demo wiring + master-key setup.
- `tests/Sorcha.UI.E2E.Tests/` + `tests/Sorcha.Verifier.Tests/` — E2E + unit coverage.
