# Citizen wallet → standards-compliant OID4VP presentation (device-bound `cnf`)

**Date:** 2026-07-15
**Status:** Design (approved direction: standards-first, Option A — `cnf` = device key)
**Tracking:** GitHub #1195
**Surface:** SD-JWT VC issuance (`cnf` binding), the citizen wallet present flow (`Sorcha.Wallet.Pwa`), device enrolment, the HAIP verifier path, the AIAS/Assured-Identity blueprints.

---

## 1. Why

The full citizen loop works end-to-end **except the final presentation submission**. On n1 (2026-07-15): apply → agent approves → URI-vct credential delivered → accepted → **present → the credential MATCHES** the verifier's DCQL request → consent → **Hold to share → `415 UnsupportedMediaType`**.

Root cause (#1195): the desk verifier routes through HAIP's **OID4VP 1.0 / SD-JWT VC** endpoint, which is pure-standard — it verifies the presentation's **KB-JWT against the credential's `cnf.jwk`** and nothing else. But the Sorcha citizen wallet is **non-standard** at presentation:

- the credential's `cnf.jwk` is the **holder** key (Ed25519, per-citizen, server-custodied);
- the KB-JWT is signed by the **device** key (P-256, non-extractable WebCrypto) — `Present.razor` uses `DeviceKey.SignAsync`;
- a **separate `delegation` JWT** (holder→device, F114) bridges the two, and the wallet POSTs `{vpToken, delegation}` as JSON.

Only Sorcha's `VerifiablePresentationValidator` (F155, `Sorcha.Verifier.Engine`) understands that chain. HAIP correctly rejects it. **Product decision (2026-07-15): stay with the standards.** The default `/verify` path must be conformant OID4VP, verifiable by any standard wallet/verifier.

## 2. The decision — `cnf` = device key (Option A)

Make the presentation **textbook OID4VP + SD-JWT VC**: the credential's `cnf.jwk` is the **enrolled device's** public key, so the **device-signed KB-JWT verifies directly against `cnf`** — no delegation on the wire, no Sorcha-specific verifier logic.

| Concern | Today (holder→device) | Option A (device-bound) |
|---|---|---|
| `cnf.jwk` in the credential | holder key (Ed25519, slot 108) | **enrolled device key (P-256)** |
| KB-JWT signer | device key | device key (unchanged) |
| Verification | holder JWK → delegation → device JWK → KB-JWT (Sorcha extension) | **KB-JWT vs `cnf` (standard)** |
| Wire submission | JSON `{vpToken, delegation}` | **form-encoded `vp_token` envelope + `state`** (OID4VP `direct_post`) |
| Delegation on present | required | **removed** |

The holder→device delegation credential (F114) does **not** disappear from the platform — it stays as the enrolment authorization artefact — but it is no longer part of the **presentation** path.

## 3. The load-bearing tension — where does the device key come from at issuance?

`cnf` is set by the **issuer** at mint time and is inside the signed SD-JWT; the wallet cannot change it later. So the credential must be bound to **the device that will present it**. Two facts collide:

1. **The device key lives only in the wallet PWA** (mobile) — `IDeviceKeyService.GetPublicJwkAsync()` (non-extractable P-256). The **web app** (`/app`, where the AIAS application is filled) does **not** have it.
2. **One credential is bound to one device.** The holder→device model existed precisely to avoid per-device re-issuance; Option A re-couples issuance to a device.

This is the real design work. Two sub-decisions:

### 3a. Getting the device key into `cnf` (issuance surface)
Today F137 threads a **holder** JWK into `cnf` via a `sorcha-holder-key` form field (`HolderKeySourceField`, default `/holderKeys/holderJwk`), resolved server-side by owner. Option A must thread the **device** JWK instead. Options:

- **A-i (bind at issuance, device-key carried):** the application is submitted from a surface that has the device key. If the citizen applies **in the wallet PWA**, `sorcha-holder-key` becomes/gains a **`sorcha-device-key`** field sourced from `DeviceKey.GetPublicJwkAsync()`, carried into the payload, and `HolderKeySourceField` → `DeviceKeySourceField` binds `cnf` to it. Clean for a wallet-first apply; **does not** work for the current web-app apply (no device key there).
- **A-ii (bind at delivery/enrolment via re-issuance):** the credential is issued to the holder as today, then the wallet PWA (which has the device key) requests a **device-bound re-issue** — the issuer re-mints the same claims with `cnf` = the device key. Requires an issuer re-sign endpoint/authority and a re-bind trigger on `/devices/enrol`. Decouples the application surface from the device but adds a re-issuance mechanism + trust story (who re-signs, and is it still the AIAS org).

**Recommendation:** Phase 1 uses **A-i with a wallet-PWA apply** for the AIAS demo (the citizen fills the application *in the wallet*, which has the device key) — the smallest path to a real standards-conformant present. Phase 2 designs **A-ii** for the general case (web-app apply, multi-device) as a device-bound re-issuance flow.

### 3b. Multi-device
`cnf` = device ⇒ a credential presents **only on the device it's bound to**. Production needs one device-bound credential **per enrolled device**. That is the A-ii re-issuance flow triggered on each `POST /devices/enrol`: the newly-enrolled device requests device-bound copies of the citizen's held credentials. **Out of scope for Phase 1** (single-device demo); it is the crux of Phase 2 and must not be hand-waved when it lands.

## 4. Change surface

**Issuance (`Sorcha.Blueprint.Engine` / `Sorcha.Wallet.Service` / models):**
- `CredentialIssuanceConfig` gains a device-key binding source (new `DeviceKeySourceField`, or generalise `HolderKeySourceField` semantics + a `bindingKeyKind: holder | device`). The value flows into `ISdJwtService.CreateTokenAsync(holderJwk:)` **unchanged** — it already embeds whatever JWK it's handed as `cnf.jwk`; only *which* JWK we pass changes.
- The AIAS/Assured-Identity blueprint switches its issuance config to device binding.

**Application surface (client):**
- Phase 1: a `sorcha-device-key` form control (mirrors the F137 `sorcha-holder-key` renderer, `HolderKeyRenderer`) that writes the device JWK from `IDeviceKeyService.GetPublicJwkAsync()`. The AIAS application is presented **in the wallet PWA** for the demo.

**Present (`Sorcha.Wallet.Pwa/Pages/Present.razor` + `PresentationEngine`):**
- Drop `{vpToken, delegation}` JSON; POST **`application/x-www-form-urlencoded`** with `vp_token` = the object-keyed envelope + `state` = requestId (OID4VP `direct_post`, matching HAIP `HandleDirectPost`'s `[FromForm]` binding). Single-credential path must also emit the **object-keyed envelope** (not a bare compact string — F181 rejects that).
- KB-JWT signing is **unchanged** (already device-signed, `aud`/`nonce`/`exp` already set) — it just now verifies against `cnf` because `cnf` is the device key.
- The `delegation` fetch/attach is removed from the present path.

**Verifier (no change):**
- HAIP's standard `HaipPresentationVerifier` (KB-JWT vs `cnf`) now verifies the citizen presentation with **no new code**. The desk verifier keeps its HAIP transport. `VerifiablePresentationValidator` (F155) stays for the *extension* method (see §5) but is off the default path.

**Enrolment (Phase 2):** `POST /devices/enrol` triggers device-bound credential (re-)issuance for the enrolling device.

## 5. Method selection on `/verify` (the operator control)

Per the standards-first steer, `/verify` supports the standard path by default and keeps the Sorcha extension as an explicit, non-default option:

- **Default — "Standard OID4VP" (SD-JWT VC / `dc+sd-jwt`):** the device-bound path above; any conformant wallet works.
- **Optional — "Sorcha holder→device":** the existing `VerifiablePresentationValidator` path, for credentials still bound to a holder key with a delegation (backwards-compat / multi-device-before-Phase-2). Selectable in the Ask screen; **not** the default.

The control is a request-profile toggle on the Ask screen (`Sorcha.Verifier` Index + the shared verify control), carried into how the request-object/response_uri is built. Phase 1 may ship the standard path only and add the toggle when the extension path is retained.

## 6. Standards conformance (the whole point)

| Rule | Spec | This design |
|---|---|---|
| Holder binding proven by KB-JWT signed with the `cnf` key | SD-JWT VC §KB-JWT; OID4VP | ✓ device key IS `cnf`; device-signed KB-JWT verifies against it |
| No out-of-band holder-binding artefacts | OID4VP | ✓ `delegation` removed from the presentation |
| `direct_post` = `application/x-www-form-urlencoded`, `vp_token` + `state` | OID4VP 1.0 §6.2 | ✓ wallet posts form-encoded; `state` = requestId (already CSRF-checked by HAIP) |
| `vp_token` object-keyed envelope (DCQL), no `presentation_submission` | OID4VP 1.0 / F181 | ✓ envelope for single + multi; PE dialect stays retired |
| KB-JWT `nonce`/`aud`/`exp` bound to the session | SD-JWT VC / F138 | ✓ unchanged |

No standard is broken; the Sorcha-specific holder→device delegation moves off the presentation path and becomes a selectable extension.

## 7. Phasing

- **Phase 1 (demo + standards proof):** device-bound `cnf` via a wallet-PWA apply (`sorcha-device-key` field + `DeviceKeySourceField`), standard OID4VP `direct_post` from the wallet, HAIP verifies unchanged. Success = the AIAS credential presents and verifies through the standard desk-verifier path on n1, with the wallet using only its device key + a plain KB-JWT.
- **Phase 2 (general + multi-device):** A-ii device-bound re-issuance on enrolment (web-app apply supported; N devices each get a device-bound credential); the `/verify` method selector + retention of the holder→device extension path.

## 8. Tests (Phase 1)

- **Issuance unit:** an issuance config with device binding sets `cnf.jwk` = the supplied device JWK (not the holder JWK).
- **Present unit:** `Present.razor`/`PresentationEngine` submits `application/x-www-form-urlencoded` with `vp_token` (object-keyed envelope) + `state`, no `delegation`; single-credential emits the envelope shape.
- **Verifier integration:** a device-`cnf` SD-JWT VC with a device-signed KB-JWT passes HAIP `HandleDirectPost` + `HaipPresentationVerifier` (KB-JWT vs `cnf`), 200 + verified claims — no delegation.
- **E2E (n1):** the AIAS credential (device-bound) presents to the desk verifier's "Confirm identity" request and verifies. The reported #1195 flow, green.

## 9. Success criteria

- **SC-1** A citizen presents a device-bound Assured Identity credential to the standard desk-verifier path and it **verifies** — no `delegation`, no `415`, no Sorcha-specific verifier code on the path.
- **SC-2** The presentation is byte-for-byte conformant OID4VP `direct_post` (form-encoded `vp_token` envelope + `state`) with a standard SD-JWT VC KB-JWT.
- **SC-3** The holder→device delegation is absent from the presentation; it remains only as the enrolment artefact (and the optional extension verify method).
- **SC-4 (Phase 2)** Multi-device: each enrolled device holds a device-bound credential; revoking a device revokes only its credential's presentability.

## 10. Open questions to resolve at planning

1. **§3a A-i vs A-ii ordering** — confirm Phase 1 = wallet-PWA apply (A-i). The AIAS demo currently applies in the **web** app; Phase 1 either moves the AIAS apply into the wallet PWA or accepts A-ii earlier.
2. **Binding-key config shape** — new `DeviceKeySourceField` vs generalising `HolderKeySourceField` + a `bindingKeyKind` enum. Prefer the enum (one field, explicit intent).
3. **Phase 2 re-issuance authority** — who re-signs the device-bound copy (the original issuer org re-mint vs a platform re-binding service), and how trust/provenance is preserved.
4. **Extension retention** — do we keep the holder→device verify method at all after Phase 2, or fully retire it once every credential is device-bound? (Keeping it eases migration; retiring it simplifies the verifier.)
