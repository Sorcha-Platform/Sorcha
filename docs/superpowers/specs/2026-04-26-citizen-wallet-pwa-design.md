# Citizen Wallet PWA — Design

**Date:** 2026-04-26
**Status:** Draft for review
**Owner:** Stuart Fraser
**Scope:** New citizen-facing wallet delivered as a Progressive Web App, offline credential cache, OID4VP-based offline presentation, with a long-road path to ISO 18013-5 / EUDIW conformance.

---

## 1. Summary

Sorcha today is a server-anchored, custodial platform — credentials are issued and stored against server-held keys, and presentations happen online via the existing HAIP flow (Feature 111). This spec adds a **citizen wallet** as a separate first-class surface, alongside the existing `Sorcha.UI.Web` application:

- A **Progressive Web App** (Blazor WebAssembly), installable to home screen on iOS, Android and desktop, that holds a citizen's credentials locally and presents them to verifiers offline using the OpenID for Verifiable Presentations (OID4VP) cross-device flow over QR.
- A **server-anchored holder key** model with revocable on-device delegation, aligned with the EUDIW Wallet Secure Cryptographic Application (WSCA) direction and accommodating real-world citizen behaviour (lost devices, forgotten passphrases) without sacrificing standards alignment.
- A **reference verifier app** that any Sorcha tenant can deep-link to as "verifier-as-a-service," and which doubles as the canonical example for the future verifier SDK.
- A staged roadmap from PWA (v1) to a full native shell with NFC/BLE proximity transports and dual SD-JWT VC + mdoc credential format, organised into two demo-driven tranches.

The wallet is **additive**. The existing Sorcha UI is unchanged; citizens continue to apply for and receive credentials through it. Once issued, credentials sync into the wallet and become available offline.

---

## 2. Decisions Locked During Brainstorming

| # | Decision | Why |
|---|---|---|
| 1 | **Two surfaces, one platform.** PWA = wallet only (hold/view/present). Existing `Sorcha.UI.Web` = application/issuance flows. | Clean separation of holder vs issuer/applicant concerns. The PWA's offline-first design fights with server-side prerendering; isolation removes the conflict. |
| 2 | **Server-anchored holder key + on-device delegation.** Issuers bind credentials to a holder key derived from the user's recoverable wallet root; each device holds a delegated key authorised by a Sorcha-issued delegation credential. | Matches EUDIW WSCA direction. Survives device loss without re-issuing credentials. Server retains revocation authority. |
| 3 | **Standards corridor: SD-JWT VC + OID4VP cross-device (QR).** mdoc / ISO 18013-5 / EUDIW-full conformance deferred to the native-shell tranche. | SD-JWT VC is Sorcha's existing credential profile; OID4VP cross-device is the most broadly interoperable transport that works in pure browser. |
| 4 | **Distribution v1 = pure PWA.** Native MAUI Blazor Hybrid shell on the roadmap, not in v1. | Fastest path to demoable end-to-end flow; no App Store review cycles in the critical path. |
| 5 | **Recovery via existing PlatformUser identity (Feature 112).** Email + password / social / passkey login. **No recovery phrases shown to citizens, ever.** | Citizen behaviour assumption — people forget phrases and get locked out. Recovery must use the same identity primitives the rest of Sorcha already supports. |
| 6 | **PWA v1 scope = pure wallet.** Persona offline, application flows, native distribution all explicitly out of v1. | Keep tranche 1's first phase tight enough to ship. Persona and broader scope arrive in later phases of tranche 1. |
| 7 | **Endgame = full ISO 18013-5 / EUDIW conformance** over time, with no rework of v1 architecture required to reach it. | Long-road commitment made explicitly during brainstorming. |

---

## 3. Architecture Overview

### 3.1 Topology

```
┌──────────────────────────────────────────────────────────────────────┐
│                        CITIZEN'S DEVICE                              │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │ Sorcha Citizen Wallet (PWA, installed)                         │  │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌────────────────┐  │  │
│  │  │ Credential      │  │ Device key      │  │ OID4VP         │  │  │
│  │  │ cache           │  │ (WebCrypto,     │  │ presentation   │  │  │
│  │  │ (IndexedDB,     │  │ non-extractable │  │ engine         │  │  │
│  │  │ encrypted)      │  │ EC P-256)       │  │ (offline)      │  │  │
│  │  └────────┬────────┘  └────────┬────────┘  └────────┬───────┘  │  │
│  │           └──────────┬─────────┴────────────────────┘          │  │
│  │           Service worker (precache + sync queue)               │  │
│  └────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────┬─────────────────────────────────┘
                                     │
                       ────  network when available  ────
                                     │
┌────────────────────────────────────▼─────────────────────────────────┐
│                       SORCHA PLATFORM (existing)                     │
│                                                                      │
│  ┌──────────────┐   ┌───────────────┐   ┌────────────────────────┐   │
│  │ Tenant       │   │ Wallet        │   │ Blueprint              │   │
│  │ Service      │   │ Service       │   │ Service                │   │
│  │              │   │               │   │                        │   │
│  │ • Login      │   │ • Holder key  │   │ • Feature 111          │   │
│  │ • Recovery   │   │   (custodial, │   │   PresentationLifecycle│   │
│  │ • Devices    │   │   recoverable)│   │ • New consumer:        │   │
│  │   (new)      │   │ • Delegation  │   │   OfflinePresentation  │   │
│  │              │   │   credential  │   │                        │   │
│  │              │   │   issuance    │   │                        │   │
│  └──────────────┘   └───────────────┘   └────────────────────────┘   │
│                                                                      │
│  ┌──────────────────────────────────────────────────────────────┐    │
│  │ Sorcha.UI.Web (existing) — citizen *applies* here, online    │    │
│  │ Open-participant flows (Feature 103), Verified Citizen, etc. │    │
│  └──────────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────────┘
```

### 3.2 Boundaries

In scope for v1:
- Citizen wallet PWA (hold, view, present credentials offline)
- Reference verifier web app (Sorcha-hosted, demoable verifier surface)
- Server-side holder key + device delegation issuance
- Device management endpoints in Tenant Service
- Offline presentation consumer in Blueprint Service (extending Feature 111)

Out of scope for v1:
- Issuance flows in the PWA (citizens apply via existing `Sorcha.UI.Web`)
- Persona offline autofill (Feature 092 — Phase 2)
- mdoc / CBOR credential format (Phase 6)
- NFC, BLE, Wi-Fi Aware proximity transports (Phase 5)
- External (non-Sorcha) verifier interop beyond OID4VP cross-device baseline (Phase 3)
- Offline credential issuance — must be online to receive a new credential

### 3.3 What is NOT changing

- Feature 111 PresentationLifecycle architecture — we add a new `IPresentationConsumer`, the lifecycle itself is unchanged.
- Existing Wallet Service custodial signing for non-citizen contexts (org keys, validator keys, persona vault, etc.).
- Tenant Service identity model — `PlatformUser` gets a new related entity (`PlatformUserDevice`); no changes to existing fields.
- `Sorcha.UI.Web` and `Sorcha.UI.Web.Client` — untouched.
- API Gateway authentication policies — JWT scoping gains a new audience claim (`sorcha:citizen-wallet`) but no new policy mechanism.

---

## 4. Holder Key + Device Delegation

The cryptographic core of the wallet: three keypairs, one delegation credential, one revocable trust chain.

### 4.1 Keypair model

| Keypair | Generated where | Lives where | Purpose |
|---|---|---|---|
| **User wallet root** | Wallet Service (existing) | Wallet Service, encrypted at rest | Custodial root, recoverable via PlatformUser login. Already exists today. |
| **Holder key** | Wallet Service, on first PWA enrolment | Wallet Service, encrypted at rest | Derived from the user wallet root under a new derivation context `sorcha:citizen-holder`. **Issuers bind credentials to this key.** Recoverable because it is deterministic from the recoverable root. |
| **Device key** | Citizen's device, on enrolment | Device IndexedDB, WebCrypto non-extractable EC P-256 | Signs OID4VP presentations offline. One per device. Authority is conferred by a delegation credential signed by the holder key. |

### 4.2 Derivation

Holder key derivation extends the existing Sorcha BIP32-style scheme (see Feature 083 Org Key Derivation, Feature 092 Persona Vault). New derivation context:

```
sorcha:citizen-holder
```

The slot index inside the derivation path is finalised at plan-phase to avoid clashing with persona-vault (currently slot 104). Holder keys are derived through the same `IWalletDerivation` pipeline as `sorcha:persona-vault` and `sorcha:docket-signing` — no new crypto primitives, only a new context.

### 4.3 Device delegation credential

When a device enrols, the Wallet Service issues a **device delegation credential** as a Sorcha-issued SD-JWT VC, signed by the user's holder key:

```jsonc
{
  "iss": "did:sorcha:holder:<holderKeyId>",
  "sub": "did:sorcha:device:<deviceKeyThumbprint>",
  "iat": 1735689600,
  "exp": 1767225600,                       // bounded, default 12 months
  "vct": "https://sorcha.dev/vc/device-delegation/v1",
  "delegated_capabilities": [
    "presentation.holder-key-binding"
  ],
  "device": {
    "label": "Stuart's iPhone 16",
    "platform": "iOS 19 / Safari 19",
    "enrolled_at": 1735689600
  },
  "cnf": {
    "jwk": { "kty": "EC", "crv": "P-256", "x": "...", "y": "..." }
  },
  "status": {
    "status_list": {
      "uri": "https://sorcha.dev/status/citizen-devices/<orgId>",
      "idx": 4711
    }
  }
}
```

This artefact carries everything an offline verifier needs to validate the chain. The cryptographic relationship `issuer → holder → device → presentation proof` is self-describing in the presentation envelope.

### 4.4 Verifier trust evaluation (offline)

When a citizen presents a credential offline, the verifier receives:

1. The **subject credential** (e.g. Verified Citizen) — issuer-signed, with `cnf.jwk` = holder public key.
2. The **device delegation credential** — signed by the holder key, with `cnf.jwk` = device public key.
3. A **fresh presentation proof** — signed by the device key, including the verifier's nonce + audience.

Verifier checks:
- Subject credential signature against issuer's published key (cached or pinned in verifier-side trust store).
- Delegation credential signature against the holder key referenced in the subject credential's `cnf`.
- Presentation proof signature against the device key referenced in the delegation credential's `cnf`.
- Status list bits for both credentials (cached locally, refreshed when the verifier is online).

### 4.5 Enrolment flow

```
1.  Citizen installs PWA, opens it for the first time.
2.  PWA prompts for sign-in to the Sorcha account (existing Tenant Service login —
    email+password / social / passkey). JWT returned, scoped with audience
    "sorcha:citizen-wallet".
3.  PWA generates a non-extractable P-256 keypair via WebCrypto, stores the key
    handle in IndexedDB, computes the JWK thumbprint as the device key ID.
4.  PWA calls POST /api/v1/wallet/devices/enrol  { deviceLabel, devicePublicJwk }.
    Server actions:
      a. Derive (or fetch existing) holder key for this user under
         sorcha:citizen-holder.
      b. Issue device delegation credential, exp = now + 12 months.
      c. Persist PlatformUserDevice row (id, userId, label, devicePublicJwk,
         status=Active, enrolledAt).
    Response: { deviceId, delegationCredential, holderPublicJwk, statusListUri }.
5.  PWA stores delegationCredential + holderPublicJwk in IndexedDB.
6.  PWA pulls all currently-issued credentials via GET /api/v1/wallet/credentials
    and caches them.
7.  Device is enrolled. All subsequent presentations are offline-capable.
```

### 4.6 Revocation flow ("I lost my phone")

```
1.  Citizen logs in on a new device or browser, or contacts support.
2.  Sees device list ("Stuart's iPhone 16 — Active").
3.  Hits Revoke.
    Server actions:
      a. Tenant Service marks PlatformUserDevice.Status = Revoked.
      b. Wallet Service flips the delegation credential's status-list bit.
      c. Tenant Service publishes a deviceRevoked SignalR event (best effort).
4.  Verifiers with cached status lists older than the revocation will still
    accept the device for up to the status-list refresh interval (default 24h).
    This trade-off is consistent with mDL practice today.
5.  Citizen enrols a new device (new device key → new delegation credential),
    using the same holder key. No upstream credentials need re-issuance.
```

### 4.7 Renewal

Device delegation credentials are bounded (default 12 months). On any online wallet session, if `exp - now < 30 days`, the PWA silently renews the delegation — same enrolment flow but reusing the existing device key. Citizen is not interrupted.

---

## 5. Credential Cache + Sync

### 5.1 IndexedDB layout

A single IndexedDB database `sorcha-wallet`, scoped to the PWA origin. Five object stores:

| Store | Key | Holds | Encrypted? |
|---|---|---|---|
| `device` | singleton `"self"` | Device key handle (CryptoKey reference, non-extractable), JWK thumbprint, enrolment metadata, wrapped content key | Key is non-extractable (browser-enforced); metadata plain |
| `delegation` | singleton `"self"` | Current device delegation credential (SD-JWT VC), holder public JWK, expiry, status-list pointer | Plain (it is a public artefact by design) |
| `credentials` | credential ID (UUID) | Issued SD-JWT VC, issuer DID, vct, `cnf` (holder pubkey), issuance/expiry timestamps, `display` metadata for UI | **Encrypted** with a content key wrapped by the device key |
| `statusLists` | status list URI | Cached status list bitstrings + `fetched_at` timestamp | Plain (public artefacts) |
| `syncQueue` | autoincrement | Pending operations (e.g. "renew delegation", "refresh status list", "ack credential receipt", "report offline presentation") | Plain |

### 5.2 At-rest encryption of credential payloads

Credentials are the high-value payload. Two threats motivate at-rest encryption:

1. **Forensic extraction** of IndexedDB by a malicious app or post-theft attacker who hasn't unlocked the device but has dumped browser storage.
2. **Defence-in-depth** against any future cross-origin escape paths.

Approach:

```
On device enrolment:
  1. Generate random 256-bit content key (CK_random).
  2. Wrap CK_random with device key → wrappedCK, store in `device` store.
  3. Hold CK_random in memory only (never persisted unwrapped).

On credential write:
  XChaCha20-Poly1305(CK_in_memory, nonce, credentialJwt) → ciphertext,
  store in `credentials`.

On wallet open (cold start):
  1. Read wrappedCK from `device`.
  2. Use device key to derive/unwrap CK_in_memory.
  3. Hold for the session, drop on visibilitychange="hidden" beyond 5 minutes.
```

WebCrypto note: P-256 keys cannot natively unwrap raw bytes. Practical implementation uses the device key to sign over a fixed challenge, then derives a deterministic AES content key via HKDF. Same effect, supported across all modern browsers. Final mechanism finalised at plan-phase.

### 5.3 Sync model

Three triggers, one logical flow.

| Trigger | When | Mechanism |
|---|---|---|
| **Pull on open** | Every PWA launch / focus regain | `GET /api/v1/wallet/sync?since=<lastSyncToken>` |
| **Live push** | New credential issued while online | SignalR `WalletHub.CredentialAvailable(credentialId)` |
| **Background sync** | Service worker `BackgroundSyncEvent` (where supported) | Periodic: status-list refresh, delegation renewal |

Server-side sync endpoint (Wallet Service):

```
GET /api/v1/wallet/sync?since={syncToken}
→ {
    syncToken: "opaque-cursor",
    credentials: {
      added:    [ { id, jwt, displayMeta, ... } ],
      revoked:  [ { id, reason, revokedAt } ],
      replaced: [ { oldId, newId, jwt, ... } ]
    },
    delegation: { renewed: false } | { renewed: true, jwt: "...", exp: ... },
    statusListsToRefresh: [ "uri1", "uri2" ]
  }
```

The `syncToken` is opaque, server-defined (likely a watermark on the user's credential-events stream). The wallet stores it after a successful merge and ships it back next time.

**SignalR push is an optimisation, not authoritative.** Pull-on-open is the source of truth. This avoids the entire class of "missed websocket message → desynced client" bugs.

**Background sync** uses `periodicSync` API where supported (Chromium-based browsers), falling back to "sync runs at app open" on Safari and others. Two scheduled jobs:

- **Status list refresh** — every 24h, fetch all `statusLists` URIs in the cache, update bitstrings.
- **Delegation renewal** — every 24h, check `delegation.exp`; if `exp - now < 30d`, silently renew.

### 5.4 Storage budget + eviction

Default cache scope: **everything**. SD-JWT VCs are typically 2–8 KB. Even an upper-bound 200 credentials per citizen is well under 2 MB.

Quota monitoring: at sync time, the wallet calls `navigator.storage.estimate()`. If used > 80% of quota, log a warning + show a UI banner. No silent eviction in v1 — citizens explicitly remove anything they don't want.

Manual removal is local-only. Server-side credentials are unaffected; a future sync can pull them back. The wallet UI does not allow citizens to permanently delete a server-side credential.

### 5.5 Content-key lifecycle in memory

The unwrapped content key (CK) lives in memory while the wallet is open.

Drop conditions:
- Tab closed → process dies, key gone.
- `visibilitychange === "hidden"` for more than 5 minutes → wipe in-memory CK; re-unwrap on next visibility.
- Explicit "lock" action in the wallet UI → wipe immediately.

Re-unwrapping costs one crypto operation (~ms). No re-auth prompt — the security gain is preventing a stolen-but-not-locked device from leaking credentials to an attacker who returns to an idle browser later.

---

## 6. Offline Presentation Flow

### 6.1 End-to-end

```
┌──────────────────┐                   ┌──────────────────┐                  ┌──────────────────┐
│  CITIZEN PWA     │                   │  VERIFIER APP    │                  │ SORCHA PLATFORM  │
│  (offline)       │                   │  (online OR      │                  │ (online,         │
│                  │                   │   offline-tolerant)                  │  catches up later)│
└────────┬─────────┘                   └────────┬─────────┘                  └────────┬─────────┘
         │                                      │                                     │
         │           1. Verifier shows QR (presentation_request_uri OR full request)  │
         │ ◀──────────────── (camera scan) ─────│                                     │
         │ 2. Parse request: presentation_definition, nonce, audience, response_uri   │
         │ 3. Match credentials against PD locally (DCQL or PEX query)                │
         │ 4. Show consent screen (selective disclosure)                              │
         │ 5. Build SD-JWT presentation:                                              │
         │      - Subject credential (issuer-signed, selective disclosure applied)    │
         │      - Device delegation credential (holder-signed)                        │
         │      - Key-binding JWT signed by device key (audience + nonce)             │
         │ 6a. POST signed VP → response_uri (verifier reachable on local network)    │
         │ ────────────────────────────────────▶│                                     │
         │ 6b. OR display QR back containing VP (verifier expects pull)               │
         │ ◀────────────── (verifier scans) ────│                                     │
         │                                      │ 7. Verify chain locally:            │
         │                                      │    - issuer sig                     │
         │                                      │    - holder→device delegation       │
         │                                      │    - key-binding proof              │
         │                                      │    - status list (cached)           │
         │                                      │ → ACCEPT / DECLINE                  │
         │ 8. Wallet shows confirmation; writes presentation log entry                │
         │ ─── (later, when both back online) ───                                     │
         │ 9. Wallet syncs presentation log entry to Sorcha                           │
         │ ──────────────────────────────────────────────────────────────────────────▶│
         │                                      │ 10. Verifier syncs its verification │
         │                                      │     event (Sorcha-registered only)  │
         │                                      │ ───────────────────────────────────▶│
         │                                      │ 11. Sorcha reconciles, writes       │
         │                                      │     PresentationInitiated +         │
         │                                      │     PresentationOutcome to the      │
         │                                      │     originating register            │
         │                                      │     (Feature 111 lifecycle, late    │
         │                                      │      writes)                        │
```

### 6.2 Verifier surfaces

| Verifier surface | What it is | When it ships |
|---|---|---|
| **Sorcha-hosted reference verifier** | A small Blazor Server web app at `verify.sorcha.dev/<verifierOrgId>/<purpose>`. Renders the request QR, accepts the response, validates the chain, displays the outcome. | v1 (Phase 1) — required for end-to-end demoability |
| **Verifier SDK** (`@sorcha/verifier-js`, `Sorcha.Verifier.Sdk`) | Library third parties drop into existing kiosk/web/mobile apps to verify Sorcha-issued credentials | Phase 3 |
| **OID4VP-conformant external verifier** | Any standards-conformant verifier (EUDIW reference, third-party tooling) | Long-tail outcome of Phases 3 and 6 |

### 6.3 Integration with Feature 111 PresentationLifecycle

The offline flow registers a new `IPresentationConsumer`:

```csharp
// Sorcha.Blueprint.Service / Services / Implementation /
//     OfflinePresentationConsumer.cs

public sealed class OfflinePresentationConsumer : IPresentationConsumer
{
    public string ConsumerName => "offline-oid4vp";

    public Task<PresentationOutcome> VerifyAsync(
        PresentationConsumerContext ctx,
        PresentationConsumerPayload payload,
        CancellationToken ct);
}
```

Lifecycle events (`PresentationInitiated`, `PresentationOutcome`) are written to the originating register **when the platform learns about the presentation**, not when it happens — they are *eventually consistent* in this consumer.

Two sync paths:

- **Verifier-callback path** (Sorcha-registered verifier reports back): Sorcha writes both events together, with the offline timestamps preserved in the payload.
- **Wallet-only path** (third-party verifier never reports back): The wallet itself reports the presentation on next sync. Outcome is `kind=success-unverified-by-platform` — Sorcha records *that* the citizen presented and *what* they presented, but cannot independently confirm the verifier accepted. Useful for citizen audit log; weaker for issuer analytics. Privacy-positive default.

A new `Blueprint.PresentationConfig.AcceptOfflinePresentationsWithinSeconds` setting (default 600) gates how late an offline presentation can be reported and still treated as fresh.

### 6.4 Selective disclosure UX

SD-JWT VC enables selective disclosure: the credential carries all attributes hashed-and-salted; the holder reveals only what was requested. The wallet exposes this in the consent screen:

```
┌──────────────────────────────────────────┐
│  Acme Bar wants to verify:               │
│                                          │
│  ✅  Over 18                              │
│  ✅  Photo                                │
│  ⬜  Date of birth         (optional)     │
│  ⬜  Full name             (optional)     │
│                                          │
│  [Hold to share]                         │
└──────────────────────────────────────────┘
```

Required claims are pre-checked and locked. Optional claims default to off (minimal disclosure). The wallet records exactly what was disclosed in the local presentation log.

### 6.5 Local presentation log

Every presentation writes a local row:

```jsonc
{
  "id": "uuid",
  "credentialId": "...",
  "verifierIdentifier": "did:sorcha:verifier:..." | "unknown",
  "verifierLabel": "Acme Bar",
  "disclosedClaims": ["over_18", "photo"],
  "presentedAt": "2026-04-26T19:23:00Z",
  "outcome": "presented" | "declined-by-citizen" | "verifier-rejected",
  "syncedToServer": false,
  "syncedAt": null
}
```

Visible in the wallet under "Recent activity." Synced opportunistically.

### 6.6 Failure modes

| Failure | Citizen sees | Recovery |
|---|---|---|
| Verifier QR malformed | "Couldn't read code, try again" | Retry scan |
| Credential doesn't match request | "You don't have a credential for this" | Manual cancel |
| Verifier nonce expired | "Verifier's request has expired" | Verifier regenerates QR |
| Status list bits stale and credential later revoked | Verifier accepted at the time → outcome stands | Sync brings new status list; future presentations fail correctly |
| Device clock skewed (delegation appears expired) | "Your wallet thinks the time is wrong" with explicit fix CTA | PWA does NOT silently accept; citizen must address |
| Network appears mid-flow | No effect — flow is fully offline regardless | n/a |

---

## 7. Codebase + Deployment

### 7.1 New project: `src/Apps/Sorcha.Citizen.Wallet/`

A standalone Blazor WebAssembly app — pure WASM, no server prerendering, statically hosted.

```
src/Apps/Sorcha.Citizen.Wallet/
├── Sorcha.Citizen.Wallet.csproj          ← Microsoft.NET.Sdk.BlazorWebAssembly
├── wwwroot/
│   ├── manifest.webmanifest
│   ├── service-worker.js
│   ├── service-worker.published.js
│   ├── icons/
│   └── index.html
├── Pages/
│   ├── Home.razor                        ← credential list
│   ├── CredentialDetail.razor            ← id-card view of one credential
│   ├── Present.razor                     ← QR scan + consent + present flow
│   ├── Devices.razor                     ← device manager (list, revoke)
│   ├── Activity.razor                    ← local presentation log
│   └── Settings.razor                    ← lock, sign-out, storage usage
├── Components/
│   ├── CredentialCard.razor              ← reuses x-review id-card styling (Feature 107)
│   ├── ConsentSheet.razor
│   └── EnrolmentWizard.razor
├── Services/
│   ├── ICredentialCache.cs               ← IndexedDB wrapper
│   ├── IDeviceKeyService.cs              ← WebCrypto wrapper
│   ├── ISyncService.cs                   ← pull/push reconciliation
│   ├── IPresentationEngine.cs            ← OID4VP request handling, VP construction
│   └── IStatusListService.cs             ← cached status-list checks
├── Auth/
│   └── (Tenant Service login integration)
└── Program.cs
```

**Why standalone, not a route inside `Sorcha.UI.Web.Client`:**
- Service worker scope cleanly covers `/wallet/*` only.
- Independent build pipeline; ships updates without rebuilding the whole UI.
- Smaller WASM payload — only the wallet's dependencies.
- PWA manifest scope is a single contiguous URL space.

**Reuses from `Sorcha.UI.Core`:**
- `ReviewSummaryRenderer` / `IdCardLayout` (Feature 107) for credential display.
- Theming primitives (`identity-navy`, `licence-pink`).
- `JsonDefaults.Api` serialisation.
- Auth client primitives (token storage, refresh interceptor).

### 7.2 New shared library: `src/Common/Sorcha.CitizenWallet.Abstractions/`

```
Sorcha.CitizenWallet.Abstractions/
├── Models/
│   ├── DeviceEnrolmentRequest.cs / Response.cs
│   ├── DeviceDelegationCredential.cs        ← typed wrapper around the SD-JWT VC
│   ├── WalletSyncResponse.cs
│   ├── PresentationLogEntry.cs
│   └── OfflinePresentationPayload.cs
├── Constants/
│   ├── DerivationContexts.cs                ← "sorcha:citizen-holder"
│   └── DelegatedCapabilities.cs             ← "presentation.holder-key-binding"
└── Schemas/
    └── device-delegation-credential.v1.json
```

Referenced by: the PWA, the Wallet Service, the Blueprint Service, and the reference verifier.

### 7.3 Extensions to existing services

| Service | Changes |
|---|---|
| `Sorcha.Wallet.Service` | New `IHolderKeyService` (derives + caches holder keys under `sorcha:citizen-holder`); new `IDeviceDelegationIssuer` (issues delegation credentials); new endpoints `/api/v1/wallet/devices/*`, `/api/v1/wallet/sync`, `/api/v1/wallet/credentials`; new `CitizenStatusListPublisher` exposing per-org device status list at `/api/v1/wallet/status/citizen-devices/{orgId}`. |
| `Sorcha.Tenant.Service` | New `PlatformUserDevice` entity (Id, PlatformUserId, Label, DevicePublicJwk, Platform, EnrolledAt, RevokedAt, Status); new endpoints `/api/v1/me/devices/*`; EF migration. |
| `Sorcha.Blueprint.Service` | New `OfflinePresentationConsumer : IPresentationConsumer` registered against Feature 111; new `PresentationConfig.AcceptOfflinePresentationsWithinSeconds` (default 600). |
| `Sorcha.ApiGateway` (YARP) | New routes: `/wallet/*` → Sorcha.Citizen.Wallet static host; `/api/v1/wallet/*` → Wallet Service; `/hubs/wallet` → Wallet SignalR hub. Existing `/api/*` routes unchanged. |
| `Sorcha.ServiceClients` | New `ICitizenWalletClient` for the PWA's HTTP calls. |

### 7.4 New project: `src/Apps/Sorcha.Citizen.Verifier/`

A small Blazor Server app — *not* a PWA. Routes at `/verify/{verifierOrgId}/{purpose}`. Renders OID4VP request QR, accepts response, validates chain, displays outcome. ~10 pages.

### 7.5 Hosting and Docker

```yaml
# docker-compose.yml additions
sorcha-citizen-wallet:
  image: sorcha-citizen-wallet:latest      # nginx serving WASM + service worker
  expose: ["80"]
  depends_on: [api-gateway]

sorcha-citizen-verifier:
  image: sorcha-citizen-verifier:latest
  expose: ["8080"]
  depends_on: [api-gateway]
```

Both go behind the API Gateway. PWA at `http://localhost/wallet/`, verifier at `http://localhost/verify/`.

### 7.6 Aspire integration

Both new apps register with `Sorcha.AppHost`. Suggested Aspire ports: 7400 (wallet), 7401 (verifier), fitting the existing port plan.

### 7.7 Build pipeline notes

- `Sorcha.Citizen.Wallet` builds with `dotnet publish -c Release` → static files in `wwwroot/` of an nginx-based image.
- Service worker generated by Blazor's PWA template (`service-worker.published.js`). Custom logic — sync queue, status-list refresh — added on top.
- AOT compilation **off** for v1. Reconsider only if startup time on low-end mobile becomes a real problem.

### 7.8 Testing surface

| Layer | Tests | Location |
|---|---|---|
| Unit (PWA services) | xUnit against `ICredentialCache`, `IDeviceKeyService`, `IPresentationEngine`, `ISyncService` | `tests/Sorcha.Citizen.Wallet.Tests/` |
| E2E (PWA UI) | Playwright via the existing `sorcha-ui` skill pattern — new `CitizenWalletDockerTestBase`, page objects per page | `tests/Sorcha.Citizen.Wallet.E2E.Tests/` |
| Integration (Wallet Service endpoints) | xUnit + WebApplicationFactory | `tests/Sorcha.Wallet.Service.Tests/` (extend) |
| Integration (Blueprint OfflinePresentationConsumer) | xUnit + lifecycle test fixtures | `tests/Sorcha.Blueprint.Service.Tests/` (extend) |
| End-to-end happy path | Playwright across PWA + Verifier reference app, simulating offline via `context.setOffline(true)` | `tests/Sorcha.Citizen.Wallet.E2E.Tests/Docker/PresentationFlowTests.cs` |

---

## 8. Roadmap (Two Tranches, Seven Phases)

### 8.1 Tranche 1 — Wallet Product (Phases 1–4)

Closes when Sorcha has a real native wallet on iOS + Android + web, with a verifier SDK any third party can integrate against.

| Phase | Adds | Demo moment |
|---|---|---|
| **1 — PWA v1 (foundation)** | Citizen wallet PWA + reference verifier; OID4VP cross-device QR; holder + device delegation; encrypted credential cache; eventual Feature 111 lifecycle integration. Everything in §3–§7. | "Scan QR, present credential offline, no signal" — first end-to-end demo |
| **2 — PWA polish + persona offline** | Feature 092 persona to the wallet (encrypted persona attributes mirrored to device; persona content-key delegation parallel to credentials; sync extension; same-origin form-fill bridge into `Sorcha.UI.Web` flows) | "Now it autofills forms with my profile" |
| **3 — Verifier SDK + external interop** | `@sorcha/verifier-js` (npm) and `Sorcha.Verifier.Sdk` (NuGet); harden wallet OID4VP request handling against external verifiers (full PEX + DCQL, broader trust framework configuration, conformance tests) | "Here's a third-party verifier built using our SDK in 30 lines" |
| **4 — Native shell via .NET MAUI Blazor Hybrid** | Wrap the same `Sorcha.Citizen.Wallet` WASM bundle in a MAUI shell. iOS + Android. Device key generation moves to Secure Enclave / Keystore + StrongBox; WebCrypto-backed `IDeviceKeyService` swapped for native backend; Razor pages and components reused as-is. App store distribution. | "Install from the App Store, fingerprint-unlock, present" |

**Phase 4 contingency:** if MAUI Blazor Hybrid proves unfit (Microsoft de-emphasises it, performance issues on iOS WebView, instability), the fallback is **Capacitor wrapping the PWA** as the native shell. Razor reuse is lost in that branch, but the PWA bundle ships natively with NFC/BLE bridges. Same demo outcome, different runtime under the hood.

### 8.2 Tranche 2 — Standards & Ecosystem (Phases 5–7)

Closes when Sorcha is a fully ISO 18013-5 / EUDIW-conformant wallet, legally recognised under eIDAS 2.0.

| Phase | Adds | Demo moment |
|---|---|---|
| **5 — Proximity transports (NFC + BLE)** | `IProximityTransport` abstraction (BLE / NFC / QR all implement it); `BleDeviceEngagement` and `NfcDeviceEngagement` MAUI implementations; verifier SDK extensions to consume from BLE/NFC sources. ISO 18013-5 device-engagement protocols. Presentation logic itself unchanged. | "Tap two phones, instant verification" |
| **6 — mdoc / CBOR credential format + ISO 18013-5 conformance** | Dual-format support: continue issuing SD-JWT VC, add mdoc for cross-ecosystem interop; `Sorcha.Cryptography.Mdoc`; mdoc issuance pipeline reusing the same holder key + delegation model; mdoc presentation; conformance test suite | "Cross-border presentation to a German EUDIW verifier" |
| **7 — Cross-ecosystem trust frameworks** | EUDIW Trust List integration; eIDAS 2.0 trust services hookup; W3C VC Trust List spec where applicable. Policy and compliance work as much as engineering. | "Legally recognised across the EU" |

### 8.3 Sequencing principle

Every phase ships an end-to-end usable artefact. Funding or attention can pause anywhere from Phase 2 onward and what's already shipped continues to deliver value. There is no "we built half of Phase 4 and now nothing works" failure mode.

### 8.4 Things explicitly NOT on the roadmap

- **Native UI rewrite** (e.g. SwiftUI / Compose) — would invalidate the Razor reuse principle. If MAUI Blazor Hybrid genuinely fails, fallback is Capacitor, not native rewrite.
- **Custodial signing for citizens** — explicitly rejected during brainstorming. Citizens always sign on-device once enrolled. Custodial path is for organisational keys, not citizen presentations.
- **Multi-user wallet** (one device, multiple citizens) — out of scope. One device → one PlatformUser. Family/guardian flows are a separate product question.
- **Issuer-side wallet UX** — out of scope. Sorcha tenants who *issue* credentials use the existing Sorcha UI for that. The wallet is for citizens who *hold* them.

---

## 9. Open Questions for Plan-Phase

These were intentionally deferred from brainstorming and need resolution before implementation:

1. **Holder key derivation slot.** Final BIP32 path index for `sorcha:citizen-holder` — must not collide with persona-vault (104) or other existing contexts. Audit `SorchaDerivationPaths` constants and pick the next free slot.
2. **Content key derivation mechanism.** Confirm the WebCrypto-compatible approach for deriving the AES content key from the device key (likely sign-fixed-challenge → HKDF). Validate cross-browser support including Safari iOS.
3. **Status list format and publication path.** SD-JWT VC supports several status mechanisms (status list 2021, token status list); pick one and confirm the per-org publication URL scheme works with the existing API Gateway routing.
4. **Delegation credential VCT URI.** Decide canonical VCT (`https://sorcha.dev/vc/device-delegation/v1` is a placeholder); coordinate with any existing Sorcha credential-type registry.
5. **Wallet SignalR hub authentication.** Confirm token scoping for `WalletHub` — the JWT audience `sorcha:citizen-wallet` needs to authorise the hub connection.
6. **PWA manifest icon set + theming.** Visual design / branding pass on icons, splash, theme colour. Reuse existing Sorcha brand assets where possible.
7. **CI test orchestration for Playwright cross-context tests.** PresentationFlowTests run two browser contexts (citizen + verifier). Confirm the existing Docker test infrastructure can host both within one test run, or split into two services and link via a test fixture.
8. **Phase 1 task decomposition and ordering.** Plan-phase will break Phase 1 into discrete implementable phases under Sorcha's GSD workflow, with dependency analysis and atomic-commit sequencing.

---

## 10. References

### 10.1 Sorcha features this builds on

- **Feature 092 (Consumer Persona)** — shape of encrypted-attribute storage, content-key derivation, pattern reused for credential cache
- **Feature 103 (Verified Citizen / Open Participants)** — citizens applying for credentials via the existing UI; the upstream of credential issuance into the wallet
- **Feature 107 (`x-review` credential id-cards)** — id-card visual layout reused for wallet credential display
- **Feature 111 (Presentation Lifecycle)** — extended with `OfflinePresentationConsumer`; lifecycle architecture unchanged
- **Feature 112 (Transactional Email + PlatformUser identity)** — recovery and account anchor for the wallet
- **Feature 083 (Org Key Derivation)** — derivation pattern (`sorcha:*` contexts) extended with `sorcha:citizen-holder`
- **Feature 086 (Validator Roster)** — derivation context pattern (`sorcha:docket-signing`) reference

### 10.2 External standards

- **OpenID for Verifiable Presentations (OID4VP)** — IETF / OpenID Foundation
- **SD-JWT VC** — IETF draft, Sorcha's existing credential profile
- **ISO/IEC 18013-5** — Mobile driving licence (mDL) reference for proximity presentation
- **ISO/IEC 23220** — Architecture of mobile eID systems
- **EUDIW Architecture Reference Framework (ARF)** — EU Digital Identity Wallet reference architecture
- **WebCrypto API** — browser crypto primitives used for device key
- **W3C Verifiable Credentials 2.0** — credential model

### 10.3 Sorcha skills relevant to implementation

- `sorcha-architecture` — feature-specific endpoint surfaces
- `verifiable-credentials` — current SD-JWT VC profile and DID handling
- `blazor` — Blazor render modes and PWA hosting
- `sorcha-ui` — Playwright test patterns
- `cryptography` — Sorcha cryptography primitives
- `entity-framework` — for `PlatformUserDevice` migration
- `signalr` — for `WalletHub`
- `yarp` — for gateway routing additions
- `aspire` — for AppHost registration
