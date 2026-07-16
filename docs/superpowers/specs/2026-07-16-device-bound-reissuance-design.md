# Device-bound credential re-issuance — one assurance, many device bindings

**Date:** 2026-07-16
**Status:** Design (approved 2026-07-16)
**Tracking:** #1195 Phase 2 (extends the Phase 1 present-side conformance shipped in #1197)
**Surface:** AIAS / Assured-Identity blueprints, SD-JWT VC issuance (`cnf` binding), the citizen wallet PWA (ID-card "Bind to device" action + `IDeviceKeyService`), the credential issuance service (device-count policy), the Token Status List publisher/worker (F114), the F118 inbox.

---

## 1. Why

#1195 Phase 1 made the citizen *present* standards-cleanly (OID4VP `direct_post`, `cnf` = device key, no delegation). But it exposed a product-level contradiction it papered over with a demo shortcut:

- The **Assured Identity is created in the web app** (`/app`) — agent review, portrait capture, postcode/profanity checks. The web has **no device key** (the device key is a non-extractable P-256 WebCrypto key that exists only in the wallet PWA on a phone).
- A standards presentation from a phone requires `cnf` = the device key, and `cnf` is **frozen at mint time** inside the issuer's signature.

You cannot make one credential both "server-signable for web" and "device-signable for phone" under base SD-JWT VC — `cnf` is a single key. So the model must be **two credentials from one assurance**, and the device-bound one must be minted where the device key lives (the phone).

## 2. The model — one assurance, two bindings, select at presentation

| | Root credential | Device credential(s) |
|---|---|---|
| `cnf.jwk` | **holder key** (Ed25519, server-custodied) | **device key** (P-256, non-extractable, per device) |
| Created | web apply (`/app`), once, at AIAS approval | on demand, in the wallet PWA, per device |
| Presents | server-custody (wallet service signs the KB-JWT) — web/remote flows | device signs the KB-JWT — in-person/offline flows |
| Standard? | yes (a legitimate server-side wallet holds `cnf` and signs) | yes (textbook SD-JWT VC / OID4VP) |
| Count | one | capped at **3 per user** (evict oldest) |

**The assurance (AIAS's attestation of the verified attributes) is created once; the binding is a cheap, swappable layer re-minted per device.** The wallet chooses which credential to present based on what it can sign for on the current surface (§6). Verifiers stay standard and unaware of the distinction.

### 2.1 What the standards provide (grounding)

- **Bind-a-key-by-proof-of-possession is the standard OID4VCI issuance flow** — the wallet sends a `proof` JWT signed by the key to bind (carrying the issuer `c_nonce`); the issuer embeds it as `cnf`. "Submit a device key, receive a device-bound credential" is textbook, not a workaround.
- **Multiple device-bound copies is first-class** — OID4VCI's `proofs` array / Batch Credential Endpoint mints N copies of one credential each bound to a different key, designed for multi-device.
- **Revocation is a one-bit flip** — SD-JWT VCs carry a `status` claim → an IETF **Token Status List** (bitstring); flipping one bit revokes a single copy. Sorcha already runs the publisher + worker (F114).
- **The device-count *policy* is deliberately NOT in the standards** — "max 3, evict oldest" is pure issuer business logic. The standards give the primitives (bind-by-proof, batch, status-list revoke); the cap and eviction are ours to define.

## 3. The Phase-1 correction (ship first)

Phase 1 (#1197) put a `sorcha-device-key` field on the **apply** blueprint and assumed the citizen applies in the PWA. The real flow is a **web apply**. Therefore:

- **Revert the AIAS apply blueprint's starting-action field `sorcha-device-key` → `sorcha-holder-key`.** The root credential is holder-`cnf` again (today's working web path). `holderKeySourceField` (`/holderKeys/holderJwk`) unchanged.
- **Keep** Phase 1's `Present.razor` `direct_post` conformance (`670a2b0d`) — it serves *both* credential types' presentation.
- **Keep** the `sorcha-device-key` control in the codebase — it is now used by the **device-registration blueprint** (§4), not the apply blueprint.

## 4. The device-registration blueprint + the "Bind to device" flow

A **second AIAS blueprint** (AIAS org = issuer participant; `citizen` = open, late-bound participant). Its starting action runs **in the wallet PWA**, triggered by a **"Bind to device" button on the credential's ID card**.

### 4.1 Blueprint shape

- **Participants:** `citizen` (walletAddress omitted — open, late-bound), `aias-issuer` (pre-bound AIAS wallet).
- **Starting action (`isStartingAction: true`, sender `citizen`):**
  - `credentialRequirements`: must **present** an `AssuredIdentityCredential` (the root). This proves entitlement. The wallet presents the root server-custody.
  - `dataSchemas`: a single `deviceKey` object, `format: "sorcha-device-key"` (auto-captured device **public** JWK). **No human-entered fields.**
- **Issuance action (sender `aias-issuer`, `requiredPriorActions: [starting]`):**
  - `credentialIssuanceConfig`: `credentialType: AssuredIdentityCredential`, same `vct`/`displayName` as the root, `targetAudience: SorchaLocalWallet`, `recipientParticipantId: citizen`, `holderKeySourceField` → the captured device JWK (so `cnf` = device key).
  - **Claim source (security):** the copied claims come from the **verified root presentation itself** — the root is AIAS-signed, so the KB-verified presentation's disclosed claims are tamper-evident (a forged claim would break AIAS's signature). The bind flow requests **full disclosure** of the root (it is automated — the citizen is not hand-picking claims), so the device copy carries the complete assured claim set. This avoids any separate "look up the user's record" step and keeps the presented credential as the single source of truth. *Implementation dependency:* the engine must pipe the verified presentation's claims into the issuance step's source document (the credential-bootstrapped gate proves possession today; feeding its `VerifiedClaims` into `claimMappings` is the new wiring). Client-supplied payload data is NOT trusted for identity claims.

### 4.2 In-wallet flow (the button)

1. ID card renders **"Bind to device"** when the held credential is an `AssuredIdentityCredential` root (holder-`cnf`) and this device has no live device copy of it.
2. On tap: capture the device **public** JWK (`IDeviceKeyService.GetPublicJwkAsync`); the private key never leaves the device.
3. Present the root (server-custody) to satisfy the blueprint's `credentialRequirement`.
4. Submit the device-registration blueprint's starting action (device JWK payload).
5. Receive the device-`cnf` copy; cache it (`ICredentialCache`).
6. The ID card now offers in-person/offline presentation from this device.

No form engine is added to the wallet — the flow wires two existing primitives (present engine + `IDeviceKeyService`) behind one button.

## 5. The 3-device cap + eviction

A **`DeviceBoundCredentialPolicy`** in the credential issuance service (not the blueprint — the cap is cross-instance stateful). On each device-`cnf` AIAS issuance:

- **Identity of a "device"** = the device key's **JWK thumbprint** (RFC 7638).
- **Idempotent re-bind:** if the submitted device key thumbprint already holds a live device copy, **replace in place** (refresh) — no count increment.
- **Cap = 3 distinct device thumbprints** holding a live device-`cnf` AIAS copy per user.
- **Eviction (new key exceeding 3):** revoke the **oldest** copy (by issued-at) via a **Token Status List bit flip** (F114 publisher/worker) + write an **F118 inbox notification** to the evicted device. Silent LRU (per the approved "oldest gets revoked").
- **Ordering:** revoke-oldest must succeed **before** the new copy is issued — no orphaned 4th copy, no >3 live window. Treat as a single logical transaction; on revoke failure, fail the issuance.

A user-facing device-management list ("your devices", manual revoke) is a natural follow-up, **out of scope** for this spec.

## 6. Presentation selection (wallet-side)

At present time the wallet selects the credential it can actually sign for on this surface:

- **In-person / offline / device-mediated:** a device-`cnf` copy bound to *this* device → device signs the KB-JWT.
- **Web / remote / server-mediated:** the holder-`cnf` root → wallet service signs (server-custody).
- **No device copy on this device + offline present requested:** prompt "Bind this device first" (route to the §4 button).

Both branches are plain SD-JWT VC presentations; the verifier does nothing special. Delegation is absent from every presentation path.

## 7. Components & boundaries

**New:**
- `demos/AIAS/blueprints/aias-device-registration.template.json` — the §4 blueprint.
- PWA "Bind to device" ID-card action + the in-wallet flow (§4.2).
- `DeviceBoundCredentialPolicy` (count/evict, §5) in the issuance service.
- Wallet present-selection rule (§6).

**Changed:**
- AIAS apply blueprint: `sorcha-device-key` → `sorcha-holder-key` (§3).

**Reused:** Token Status List publisher/worker (F114) · F118 inbox · presentation engine · `IDeviceKeyService` · `sorcha-device-key` control · the Phase-1 `HandleDirectPost` verifier harness.

## 8. Error handling

| Condition | Behaviour |
|---|---|
| No root credential held | "Bind to device" hidden/disabled; prompt to get assured first |
| Root revoked / present fails | Starting-action `credentialRequirement` fails → no copy issued |
| Device key unavailable (non-PWA host) | Action unavailable (device binding is phone-only) |
| Eviction (revoke-oldest) fails | Abort issuance — never leave >3 live or orphan a copy |
| Offline present, no device copy | "Bind this device first" prompt |
| Same device re-binds | Idempotent replace-in-place (§5) |

## 9. Testing

- **Blueprint:** device-registration blueprint publishes + validates (open citizen participant, credentialRequirement present, device-key field).
- **In-wallet flow (integration):** capture device key + present root + submit → device-`cnf` copy cached; `cnf` = the device key, claims = the authoritative assurance.
- **Cap/eviction (unit):** 4th distinct device evicts the oldest (status bit flipped + F118 notified); re-binding an existing thumbprint is idempotent (no increment); revoke-failure aborts issuance.
- **Presentation selection:** device-side present picks the device copy (device-signs); web/remote picks the root (server-custody).
- **Verifier (reuse Phase-1 harness):** the device copy standard-verifies via `HandleDirectPost` + `HaipPresentationVerifier`; the root verifies server-custody.

## 10. Success criteria

- **SC-1** A citizen applies for Assured Identity on the **web** and receives a holder-`cnf` root that presents server-custody for remote verification.
- **SC-2** From the wallet, **"Bind to device"** mints a device-`cnf` copy (device key never leaves the phone) that presents in-person/offline and standard-verifies with no delegation.
- **SC-3** A user holds at most **3** live device-`cnf` AIAS copies; binding a 4th device revokes the oldest (status-list) and notifies it; re-binding an existing device is idempotent.
- **SC-4** The wallet presents the right credential per surface automatically (device copy in person, root on the web).

## 11. Resolved decisions

1. **Cap unit** = device-bound AIAS credentials (3 per user), AIAS-scoped. (Not platform-wide enrolled devices — that generalisation is a later concern.)
2. **Entitlement gate** = present the root credential (full disclosure); **claims** sourced from that verified, AIAS-signed presentation (tamper-evident), not from client-supplied payload data.
3. **Flow surface** = focused in-wallet "Bind to device" action (not a web pairing handoff, not a general application runner). Keeps it a blueprint for on-ledger AIAS provenance.
4. **Re-signer** = AIAS re-issues (the blueprint's issuer participant is AIAS), preserving "assured by AIAS" provenance on every copy.
5. **Eviction** = silent LRU (oldest revoked); device-management UI deferred.

## 12. Out of scope

- Platform-wide "enrolled devices" cap generalisation (this is AIAS-credential-scoped).
- A user-facing device-management list / manual device revoke.
- Applying an equivalent device-copy flow to non-AIAS credentials (driving licence, etc.).
- The optional holder→device delegation `/verify` extension method (delegation is simply absent here).
