# Phase 0 — Research: Citizen Wallet PWA

**Feature**: 114-citizen-wallet-pwa
**Date**: 2026-04-26
**Status**: Resolved — all open questions from the design doc have concrete answers below.

This document resolves the three open questions deferred from `docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md` §9, plus a small number of additional questions surfaced during plan-phase analysis. Every entry uses the format: **Decision · Rationale · Alternatives considered**.

---

## R-001 · Holder key derivation slot

**Question**: Which BIP44 index does `sorcha:citizen-holder` occupy in `SorchaDerivationPaths`?

**Decision**: Slot **108** — `m/44'/0'/0'/0/108`.

**Rationale**:
- Audit of `src/Core/Sorcha.Wallet.Portable/Constants/SorchaDerivationPaths.cs` shows slots 100..107 occupied (register-attestation, register-control, docket-signing, blueprint-publish, persona-vault, credential-holder-binding, haip-issuer-signing, tenant-ca-signing).
- 108 is the next contiguous free index. Sequential allocation matches the existing convention.
- New constant: `public const string CitizenHolder = "sorcha:citizen-holder";` with `CitizenHolderPath = "m/44'/0'/0'/0/108"`. Add to the `ResolvePath` switch.

**Distinction from existing `sorcha:credential-holder-binding` (slot 105)**:
- 105 is **per-wallet**, used to sign Key Binding JWTs for credentials whose `cnf` claim references the wallet's holder-binding pubkey. Lives entirely server-side today.
- 108 (new) is the **citizen wallet holder identity** — a platform-user-level key that issuers bind credentials to and which signs the device delegation credential. It is the citizen's stable identity across devices.

In the v1 architecture both keys exist independently:
- 105 still applies to credentials issued in classic-online HAIP flows.
- 108 is what citizen-wallet-issued credentials bind to, and what signs the device delegation credential consumed by offline verifiers.

A future cleanup may unify these once the wallet is the dominant holder pattern; v1 keeps them separate to avoid disturbing existing HAIP code paths.

**Alternatives considered**:
- *Re-use 105 (`credential-holder-binding`)*: Rejected. The existing key is per-wallet; the wallet model assumes a single device, which collides with the multi-device citizen-wallet model. Re-using would require redefining the existing key's semantics.
- *Allocate 200+ for "v2" derivation contexts*: Rejected. Sequential allocation is an established pattern; jumping the index would create an arbitrary gap with no functional meaning.

---

## R-002 · WebCrypto-compatible content-key derivation

**Question**: How does the wallet derive an AES content key from the non-extractable EC P-256 device key, given that WebCrypto's `wrapKey/unwrapKey` cannot wrap raw bytes with a P-256 key?

**Decision**: **HKDF-SHA256 over an ECDSA signature of a fixed deterministic challenge.**

```
ChallengeBytes = "sorcha-citizen-wallet/content-key/v1" UTF8 || deviceKeyJwkThumbprint
SignatureBytes = ECDSA-P256-SHA256(devicePrivateKey, ChallengeBytes)
ContentKey     = HKDF-SHA256(
    ikm  = SignatureBytes,
    salt = deviceKeyJwkThumbprint,
    info = "sorcha-citizen-wallet/content-encryption/v1",
    L    = 32   // 256-bit AES-256 key
)
```

**Why this works:**
- ECDSA signing with non-extractable keys is supported by `SubtleCrypto.sign()` in every modern browser including Safari iOS 14+.
- The signature output is deterministic only if a deterministic-k variant is used; ECDSA is *probabilistic* in WebCrypto, which would break determinism.

**ECDSA non-determinism mitigation:**
The signature is computed once at enrolment, the resulting `ContentKey` is wrapped with the device key (via deterministic re-derivation? — no, see below) and stored. On each cold open, the wallet re-signs the challenge to produce a *new* signature, which would yield a different `ContentKey`. To avoid this, the implementation actually:

1. Generates a random 256-bit `ContentKey` at enrolment (via `crypto.getRandomValues`).
2. Encrypts `ContentKey` with AES-GCM-256 using a *wrapping key* derived from the device key + a stored salt.
3. The *wrapping key* is `HKDF-SHA256(SignatureBytes, salt = storedRandomSalt, info = "sorcha-citizen-wallet/wrap/v1", 32)`.
4. ECDSA non-determinism is irrelevant for the *wrap* step because the wrapping key only needs to be derivable, not stable across sessions, *if* both the wrap and unwrap happen in the same flow. **It does NOT, so this scheme breaks.**

**Final corrected scheme — HMAC-SHA256 with WebCrypto:**

WebCrypto supports `HMAC` with non-extractable keys, and HMAC is deterministic. We use a per-device HMAC key as the device-bound primitive instead of an EC key for the encryption-at-rest path. The EC P-256 device key remains for OID4VP signing (presentation proofs).

```
On enrolment:
  1. Generate non-extractable ECDSA P-256 keypair (deviceSigningKey)        — signs OID4VP presentations
  2. Generate non-extractable HMAC-SHA256 256-bit key (deviceWrappingKey)   — encryption at rest
  3. Generate random 256-bit ContentKey (CK)                                 — bulk credential encryption
  4. Compute WrapKey = HKDF-SHA256(
        ikm  = HMAC(deviceWrappingKey, "sorcha-citizen-wallet/wrap/v1"),
        salt = randomPerEnrolmentSalt,
        info = "AES-256-GCM",
        L    = 32 )
  5. Encrypt CK with AES-GCM-256(WrapKey, randomNonce) → wrappedCK
  6. Store: deviceWrappingKey (non-extractable, browser-managed), randomPerEnrolmentSalt,
            randomNonce, wrappedCK in IndexedDB `device` store
  7. Hold CK in memory; encrypt all credentials with XChaCha20-Poly1305(CK, perCredentialNonce)
     ← XChaCha20 via libsodium-js (WebCrypto does not support XChaCha20)

On cold open:
  1. Read deviceWrappingKey handle, salt, nonce, wrappedCK from IndexedDB
  2. Re-derive WrapKey via the same HKDF call (deterministic, HMAC is deterministic)
  3. AES-GCM-256 decrypt wrappedCK → CK
  4. CK held in memory; drop on visibility-hidden timeout (5 min)
```

**Why HMAC instead of ECDSA for the wrap path:**
- `HMAC` in `SubtleCrypto.sign()` is deterministic by definition.
- Non-extractable HMAC keys are supported on iOS 14+ Safari and all Chromium/Gecko versions in scope.
- ECDSA stays for what it is uniquely required for — ECDSA-P-256 signatures on OID4VP presentation proofs (which verifiers expect; HMAC would not be acceptable there).

**Why XChaCha20-Poly1305 for the bulk path despite WebCrypto not supporting it:**
- 192-bit nonces eliminate the per-CK nonce-reuse risk if the same CK is reused across many credentials. AES-GCM has a 96-bit nonce and a hard 2^32 same-key/random-nonce limit before the birthday-bound concern bites.
- libsodium-js is mature, well-audited, ~100 KB (acceptable in a PWA bundle), and supplies the same primitive Sorcha already uses server-side (`Sorcha.Cryptography` aligned).

**Alternatives considered:**
- *Pure AES-GCM bulk encryption with WebCrypto*: viable for v1 — fewer JS dependencies, no libsodium load — but the nonce-management discipline is fiddler. Rejected for symmetry with the server-side pattern (Feature 092 persona uses XChaCha20-Poly1305).
- *Encrypt CK with PBKDF2 over a citizen passphrase*: rejected per FR-003 (no recovery phrases / wallet-specific secrets).
- *Re-derive CK from sign-fixed-challenge each session*: rejected because ECDSA in WebCrypto is probabilistic. HMAC sidesteps the determinism problem.
- *Store CK plaintext in IndexedDB*: rejected; defeats the at-rest encryption requirement (FR-011).

---

## R-003 · Status list format and per-org publication path

**Question**: Which status list format does Sorcha publish for citizen device delegations and how is it scoped per organisation?

**Decision**: **IETF Token Status List 2024 (`draft-ietf-oauth-status-list`)**, served as a status list JWT, scoped per-tenant-org with one list per credential-type-and-org pair.

**Format details:**
- Spec: `draft-ietf-oauth-status-list-09` or later (IETF OAuth WG, on standards track).
- Bitstring length: variable, default 2^15 (32 768) bits per list ≈ 4 KB compressed. Doubled when half-full.
- Bits per status entry: 1 (active=0, revoked=1) for v1. Schema reserves the second bit for future "suspended" semantics without re-keying.
- Wire format: signed JWT with `typ: "statuslist+jwt"`, `iss = did:sorcha:org:{wallet}`, signed with the org's holder key (TBD whether the org's wallet root or a derived `sorcha:status-list-signing` slot — see R-004).

**URL scheme:**
- Per-org per-list publication path:
  `GET /api/v1/wallet/status/{orgId}/citizen-devices/{listId}.statuslist+jwt`
- Per-org per-credential-type list (used for revoking credentials, separate from device delegation revocation):
  `GET /api/v1/wallet/status/{orgId}/credentials/{credentialType}/{listId}.statuslist+jwt`
- Status list URI embedded in delegation credentials' `status.status_list.uri` claim points to the device-delegations list; status list URI in citizen-issued credentials points to the credential-type list (issued by Wallet Service or other Sorcha services as today).

**Why this format:**
- IETF status list 2024 is the ratified successor to W3C StatusList2021. Adopted by EUDIW reference implementations, German Federal LISSI wallet, Sphereon. Cross-ecosystem interop benefit when the wallet ships.
- Bitstring + JWT is compact (4 KB for 32K entries), cacheable (immutable per `iat`), and verifier validation is one signature check.
- Scoping per-org keeps revocation scope narrow — a tenant's revocations don't appear in another tenant's status feed, and verifiers only fetch lists for orgs they trust.

**Refresh interval:**
- Default 24 h verifier-side cache TTL (per FR-024). Status list JWT carries `exp` claim set to `iat + 24h`; verifiers refuse stale lists past `exp` until refresh succeeds.
- Server-side, status lists are regenerated on every revocation event (incremental) plus a scheduled regeneration every 1 h to bound the staleness window if revocations are quiet.

**Alternatives considered:**
- *W3C StatusList2021*: predecessor, deprecated direction. Rejected.
- *RevocationList2020*: superseded; rejected.
- *Per-credential CRL (one revocation file per credential)*: rejected; explodes the number of files served and the verifier-side fetch load; not how the standard ecosystem operates.
- *Online verification (verifier hits Sorcha at presentation time)*: rejected; defeats the offline goal (FR-016, FR-017).
- *Single global status list for all orgs*: rejected; cross-tenant information leak (a verifier seeing list growth could infer revocation activity in unrelated tenants).

---

## R-004 · Status list signing key

**Question**: Which key signs the status list JWT?

**Decision**: A new derivation slot **`sorcha:citizen-status-signing`** at index **109** (m/44'/0'/0'/0/109), derived per tenant org from the org's system wallet.

**Rationale**:
- Status lists are tenant-scoped artefacts. The org's identity DID (`did:sorcha:org:{walletAddress}`) is the natural issuer.
- Using a *derived* signing key (not the root org wallet) preserves the principle of least privilege: status-list compromise rotates one purpose-derived key, not the org root.
- Sequential to slot 108 (citizen-holder); both new this feature.

Add to `SorchaDerivationPaths`:
```csharp
public const string CitizenStatusSigning = "sorcha:citizen-status-signing";
public const string CitizenStatusSigningPath = "m/44'/0'/0'/0/109";
```

**Alternatives considered**:
- *Sign with the existing org root wallet*: rejected — too broad a scope for a frequently-used signing key.
- *Sign with the validator key*: rejected — validators sign dockets, not tenant-scoped attestations.

---

## R-005 · Device labelling and platform fingerprinting

**Question**: How does the wallet propose a default device label, and what platform metadata is captured?

**Decision**: Default label format `"{citizenFirstName}'s {browserName} on {osName}"` with citizen-editable override at enrolment time. Platform metadata: `userAgent`, derived `platform` (e.g. "iOS 19 / Safari 19"), and `enrolledAt` UTC timestamp. No device fingerprinting beyond what the user agent provides — explicitly avoids fingerprinting techniques (canvas, WebGL, font enumeration) that would amount to citizen tracking.

**Rationale**:
- Recognisable label helps citizens identify devices in the device manager when revoking.
- Citizen-editable so they can name devices meaningfully ("home laptop", "kitchen iPad").
- No fingerprinting because the wallet's value proposition is citizen sovereignty; intrusive identification techniques would betray that.

**Alternatives considered**:
- *Hardware-attested device identity (WebAuthn attestation)*: out of v1 scope but a clean upgrade for the native shell tranche. Documented for future.

---

## R-006 · Sync token format

**Question**: What shape does the opaque `syncToken` take?

**Decision**: A signed compact-JWT with claims `{ sub: holderKeyId, lastEventSeq: long, iat: long }`, signed by the Wallet Service.

**Rationale**:
- The sync endpoint needs an authoritative, tamper-evident pointer to the citizen's last-applied event in their credential-events stream.
- JWT format is consistent with the rest of Sorcha's auth surface.
- Signed-by-Wallet-Service prevents the wallet from forging a "skip ahead" token to evade revocation events.

**Alternatives considered**:
- *Plain integer cursor*: rejected — wallet could send any value.
- *Encrypted blob*: rejected — tamper-evident is sufficient; encryption adds no value here.

---

## R-007 · Replay protection in OID4VP cross-device QR

**Question**: How does the wallet ensure each presentation proof is fresh and bound to the verifier's request?

**Decision**: Standard **OID4VP `nonce` + `aud` binding in the key-binding JWT**, with a wallet-side replay cache of `{nonce, aud, presentedAt}` tuples for 60 minutes.

- Verifier supplies `nonce` (fresh random) and `client_id` (their identifier as `aud`).
- Wallet's KB-JWT MUST include `nonce` and `aud` matching the request.
- Wallet rejects any presentation request whose `(nonce, aud)` it has seen within the last 60 minutes — bounds against attacker re-using a stolen QR before the verifier expires it.

**Rationale**: Standard OID4VP behaviour. Replay cache is local-only, opportunistically purged.

---

## R-008 · QR encoding for OID4VP cross-device

**Question**: What does the verifier's QR contain, and what is the response delivery mode?

**Decision**: QR contains a deep link of the form:
```
openid4vp://?request_uri=https://verify.sorcha.dev/r/{sessionId}
            &client_id=did:sorcha:verifier:{orgId}
            &response_mode=direct_post
            &nonce={base64url-random-128bit}
```

- `request_uri` is fetched by the wallet (when online) for the full PEX/DCQL request, OR is replaced by an inline `request` parameter (full base64url-encoded request JWT) when offline-friendly mode is required.
- v1 supports BOTH modes; reference verifier defaults to inline `request` for true offline (citizen + verifier both offline) and `request_uri` for citizen-online + verifier-online flows that prefer compact QRs.
- `response_mode=direct_post` lets the wallet POST the VP back to the verifier's response endpoint when network is available.
- For fully-offline both-parties scenarios, `response_mode=direct_post.qr` (wallet displays VP as a QR for verifier to scan) is supported.

**Rationale**: Two response modes cover the realistic offline scenarios. Inline request avoids the `request_uri` round-trip when truly offline; `request_uri` keeps QRs small in the common partially-online case.

**Alternatives considered**:
- *`response_mode=fragment`*: browser-based response, requires same-origin behaviour — not applicable for cross-device QR.
- *Custom Sorcha protocol*: rejected; standards alignment is the entire premise.

---

## R-009 · Sorcha tech stack alignment

**Question**: Which language, framework, runtime, and libraries does v1 commit to?

**Decision**:

| Concern | Choice | Source |
|---|---|---|
| Language / runtime | **C# 14 / .NET 10** | Constitution + repo-wide standard |
| Wallet UI | **Blazor WebAssembly** (standalone, no server prerender) | Design doc §7 |
| Verifier UI | **Blazor Server** | Design doc §7 |
| Browser crypto | **WebCrypto SubtleCrypto** (ECDSA P-256, HMAC, AES-GCM) + **libsodium-js** (XChaCha20-Poly1305) | R-002 |
| Local storage | **IndexedDB** via `Microsoft.JSInterop` JS module | Design doc §5 |
| HTTP client | Standard `HttpClient` with Sorcha.ServiceClients pattern | CLAUDE.md "Use Consolidated Service Clients" |
| Realtime | **SignalR** (`/hubs/wallet`) | Design doc §5.3 |
| Server crypto | **NBitcoin + Sorcha.Cryptography** | CLAUDE.md tech stack |
| EF Core | EF Core for `PlatformUserDevice` migration | Constitution + tenant-service convention |
| API style | **Minimal APIs + Scalar OpenAPI** (NOT Swagger) | Constitution III + CLAUDE.md |
| Validation | FluentValidation on request DTOs | Constitution Tech Stack |
| Telemetry | OpenTelemetry traces/metrics from the new endpoints | Constitution VIII |
| Test (unit) | xUnit + FluentAssertions + Moq | Constitution IV |
| Test (E2E) | Playwright (NUnit) — extending `sorcha-ui` patterns | sorcha-ui skill |
| Service discovery | Aspire AppHost ports 7400 (wallet), 7401 (verifier) | Design doc §7.6 |
| Reverse proxy | YARP — new clusters `/wallet/*`, `/verify/*`, `/api/v1/wallet/*`, `/hubs/wallet` | Design doc §7.3 |

**Rationale**: Every choice is the existing platform default. The wallet introduces zero new server-side technologies; client-side it adds libsodium-js (~100 KB) and a `qr-scanner` library for camera scanning. JS dependencies are deliberately minimal to keep the WASM bundle lean.

---

## R-010 · Browser support floor

**Question**: Which browsers must the v1 PWA support?

**Decision**: **Latest two major versions of Chromium (incl. Edge, Opera, Samsung Internet), WebKit (Safari iOS + macOS), and Gecko (Firefox)** on both mobile and desktop.

**Rationale**:
- Per SC-009.
- All target browsers support: Service Workers, IndexedDB, WebCrypto SubtleCrypto with ECDSA P-256, HMAC, AES-GCM, HKDF; Web App Manifest; `navigator.storage.estimate()`.
- `periodicSync` is Chromium-only; v1 uses the visibility-fallback for Safari/Firefox per design doc §5.3.
- Camera scanning via `getUserMedia` works on all targets.

---

## R-011 · Test isolation strategy for offline E2E

**Question**: How do Playwright tests prove offline behaviour deterministically in CI?

**Decision**: Playwright `BrowserContext.SetOffline(true)` between key flow steps. Two-context test scaffold for cross-device presentation: one context = citizen wallet, one context = verifier; both use the same Playwright test session so timing is controlled.

**Rationale**:
- Per design doc §7.8.
- No emulator, no mobile rig, no network shaping. Deterministic in CI.

---

## R-012 · Migration coordination

**Question**: Does this feature require any Sorcha-wide migration?

**Decision**: One EF migration (`AddPlatformUserDevice`) on the Tenant Service Postgres database. No other migrations.

**Rationale**:
- New table only — no changes to existing tables.
- Wallet Service holds new derivation contexts in code; no migration needed (the BIP44 path resolution is in-process).
- Blueprint Service adds a new consumer registration — DI wiring only.

---

## Open questions deferred BEYOND v1 plan

These are documented for the future-tranche phases and explicitly NOT in the v1 plan:

- **mdoc credential format support** — Phase 6.
- **NFC / BLE proximity transports** — Phase 5.
- **Native MAUI Blazor Hybrid shell** — Phase 4.
- **Persona offline integration (Feature 092)** — Phase 2.
- **External verifier conformance test suite** — Phase 3.
- **WebAuthn-backed device key (vs WebCrypto-backed)** — investigated for Phase 4 (Secure Enclave / StrongBox).

---

## Summary

All three originally-deferred design questions resolved (R-001, R-002, R-003), one closely-related question surfaced and answered (R-004), and seven tactical clarifications captured (R-005–R-011) plus one migration scope confirmation (R-012). No remaining `NEEDS CLARIFICATION` markers. Ready for Phase 1 design.
