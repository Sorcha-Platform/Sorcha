# Mobile proximity credential sharing — ISO 18013-5 over BLE

**Date:** 2026-07-13
**Status:** Design approved, not yet implemented
**Scope decisions taken by Stuart before design:** ISO 18013-5 mdoc proximity (BLE device retrieval);
holder **and** native reader; both mdoc **and** SD-JWT VC carried over proximity; COSE_Mac0 implemented
on both sides; holder takes the BLE **peripheral server** role; evidence bar is **our own two devices**
(iPhone ↔ Android), not a certified third-party reader.

---

## 1. What this is

Today the Sorcha Wallet presents credentials **online only** — OpenID4VP `direct_post` over HTTPS, reached
by QR or same-device link. Every presentation puts a server in the middle.

This feature makes the wallet able to present **in person, offline**: the holder's phone and a verifier's
device exchange a presentation directly over Bluetooth Low Energy, with no network and no server. That is
the ISO/IEC 18013-5 "device retrieval" story, and it is what the EUDI ARF expects of a wallet.

It also builds the other half of the ritual — a **native reader** — because a browser cannot be a BLE
central and `Sorcha.Verifier` is a Blazor *Server* app. Without a reader we could not demonstrate or test
the thing we built.

## 2. Why it is bigger than "add Bluetooth"

The kickoff analysis and a full codebase recon (2026-07-13) established the following. These are verified
facts, not estimates.

**The mdoc stack is online-only, and the holder half does not exist at all.**
`Sorcha.Cryptography/Mdoc` (Feature 135) has `MdocCodec`, `MdocCbor`, `MdocService` (verify), `MdocIssuer`,
`CoseX5Chain`. But:

- `MdocCodec.BuildOpenId4VpSessionTranscript` emits **only** the OID4VP hash-handover form. The two ISO
  proximity slots — `DeviceEngagementBytes` and `EReaderKeyBytes` — are literally hardcoded `WriteNull()`.
  There is no proximity `SessionTranscript`.
- There is **no `DeviceEngagement`**, no `SessionEstablishment`/`SessionData`, no session key derivation,
  no session encryption, and no BLE transport. Repo-wide grep for `DeviceEngagement` and
  `SessionEstablishment` returns **zero** hits.
- There is **no holder-side mdoc presentation builder anywhere**. `MdocCodec`'s `WriteDeviceAuth`,
  `WriteDocument` and `WriteIssuerSigned` are **private**. The only public write path is
  `EncodeDeviceResponse(DeviceResponse)`, which requires the caller to supply a fully-formed
  `CoseSign1Message`. `MdocIssuer` is issuer-side; `MdocService` is verifier-side. The wallet's
  presentation engine is **SD-JWT-only** — it has no CBOR/mdoc code path at all.
- **MAC-based device auth is explicitly refused** (`MdocService.cs:175`): *"MAC-based device authentication
  is not supported in v1 (use deviceSignature)."* `DeviceAuth.DeviceMacRaw` is round-tripped as opaque
  bytes but never constructed or verified. The BCL has no `COSE_Mac0` type.
- `MdocService.VerifyDeviceBinding` is **private and hardcodes the OID4VP transcript**, so offline
  verification cannot reuse it. The one genuinely transcript-agnostic seam is
  `MdocCodec.BuildDeviceAuthentication(byte[] sessionTranscript, …)`, which is reusable unchanged.

**Web Bluetooth is not a route.** It does not exist in iOS WKWebView, and is absent/unreliable in the
Android WebView Capacitor uses. This is the same class of constraint that already blocks Android passkeys
(the WebView has no `PublicKeyCredential`, so a native Credential Manager plugin is required). The answer
has the same shape: **native plugin + JS-interop seam**, not a web API.

**`mobile/wallet` has zero native plugins.** Dependencies are exactly `@capacitor/core`, `@capacitor/ios`,
`@capacitor/android`. Native code is `MainActivity extends BridgeActivity {}` and a stock `AppDelegate.swift`.
The only hand-edited native file is `App.entitlements` (associated domains, for passkeys). Any BLE/NFC
capability is net-new Swift + Kotlin.

**`Sorcha.Cryptography` is not WASM-loadable.** It pulls `Sodium.Core` (libsodium P/Invoke) and
`Nethermind.MclBindings` — both native. The wallet PWA does not reference it and cannot. The `Mdoc/`
subtree itself is clean BCL (`System.Formats.Cbor`, `System.Security.Cryptography.Cose`).

**The DCQL vocabulary already speaks mdoc, but the wallet chokes on it.** `DcqlFormats.MsoMdoc` exists and
`DcqlCredentialQuery.Validate()` accepts `mso_mdoc` with `meta.doctype_value`. But
`PresentationEngine.ParseAsync` **throws** when `vct_values` is absent, and `MatchQuery` matches on `Vct`
string equality only. An mdoc query is currently unparseable and unmatchable in the wallet.

## 3. The crypto finding that shapes the wallet's key model

**mdoc `deviceMac` requires an ECDH-capable device key, and ours cannot be one.**

In ISO 18013-5, `EMacKey` is derived by ECDH between the **static device key published in the MSO** and the
reader's **ephemeral** key. So `MSO.DeviceKey` must be ECDH-capable.

The wallet's device key is a non-extractable WebCrypto key generated for **ECDSA sign** usage
(`WebCryptoDeviceKeyService`, reachable only as `SignAsync(byte[]) → byte[]`). In WebCrypto a key's usages
are fixed at generation: a key **cannot be both ECDSA and ECDH**. Therefore the existing device key
structurally cannot produce a `deviceMac`.

**Resolution — two device keys, each fit for purpose:**

| Key | Usage | Purpose |
|---|---|---|
| Existing ECDSA P-256 device key | `sign` | SD-JWT KB-JWT signing. **Untouched.** |
| New ECDH P-256 device key | `deriveBits` | mdoc `MSO.DeviceKey` binding; session ECDH; `deviceMac`. |
| Per-session ephemeral `EDeviceKey` | `deriveBits` | Session encryption only. Freshly generated per session, never persisted. |

Sorcha-issued mdoc credentials bind `MSO.DeviceKey` to the **ECDH** key. A credential bound to the ECDH key
can produce `deviceMac` but not `deviceSignature`; that is correct and sufficient — 18013-5 requires exactly
one of the two, and a conformant reader must accept either.

**Consequence for `MdocIssuer`:** its `holderDeviceKeyCose` parameter must be fed the ECDH key's COSE_Key
for proximity-capable credentials. A credential already issued against the ECDSA key remains presentable
online with `deviceSignature`, unchanged.

## 4. Why this cut (and what was rejected)

**Chosen — thin native transport, fat C#.** The Capacitor plugin does *only* BLE and NFC: advertise, GATT
server/client, chunked read/write of **opaque bytes**. Every byte of ISO protocol lives in C#, written once,
shared by holder and reader.

Rejected alternatives:

- **Fat native, thin C#** (implement 18013-5 in Swift *and* Kotlin). This is how most wallet vendors do it.
  Rejected: two implementations of tag-24 verbatim splicing and the session transcript is two chances to
  get *"digests and signatures are over the tagged outer bytes"* wrong, in languages the existing test suite
  cannot reach.
- **Abandon Capacitor for .NET-native (MAUI)**. C# would run natively with no WASM constraint. Rejected:
  throws away the proven Capacitor + fastlane + TestFlight/Play pipeline for no protocol benefit.

The plugin's contract is deliberately small enough to be obviously correct in two languages, and dumb enough
that a bug in it cannot produce a subtly-wrong transcript.

## 5. Architecture

### 5.1 `Sorcha.Mdoc` — new pure-managed project

Extracted from `src/Common/Sorcha.Cryptography/Mdoc/`, following the **`Sorcha.Cryptography.Secp256k1`
precedent** (which exists for exactly this reason: the WASM verifier cannot take a native dependency).

- Deps: `System.Formats.Cbor`, `System.Security.Cryptography.Cose`, `BouncyCastle.Cryptography`. **No
  `Sodium.Core`, no `Nethermind.MclBindings`.** WASM-safe.
- Existing types move verbatim: `MdocCbor`, `CoseX5Chain`, `MdocCodec`, `MdocModels`, `MdocService`,
  `MdocIssuer`.
- **Clean break, no shim:** `Sorcha.Cryptography` drops the folder; `Sorcha.Blueprint.Engine` and
  `Sorcha.Haip.Service` re-point their `using`s at `Sorcha.Mdoc` directly.

New types in it:

| Type | Purpose |
|---|---|
| `CoseSign1Builder` / `CoseSign1Verifier` | Assemble and verify COSE_Sign1 from a **raw** signature over a hand-built `Sig_structure`. Required because the device key exposes only `SignAsync(byte[]) → byte[]` and `CoseSign1Message.SignDetached` demands an `AsymmetricAlgorithm`. Same "hand-roll rather than take the dependency" move as F182's RLP. |
| `CoseMac0` | Build + verify COSE_Mac0 (HMAC-SHA256, alg 5) over `MAC_structure`. The BCL has no such type. |
| `MdocSessionCrypto` | ECDH P-256 → HKDF-SHA256 (salt = `SHA-256(SessionTranscriptBytes)`; info `SKDevice` / `SKReader` / `EMacKey`) → AES-256-GCM with the 18013-5 message counter. BouncyCastle throughout so it runs in WASM. |
| `DeviceEngagement` + codec | Version, `Security[cipherSuite, EDeviceKeyBytes]`, `DeviceRetrievalMethods` (BLE, peripheral-server mode, service UUID). Encoded to the QR payload. |
| `SessionEstablishment` / `SessionData` + codec | The session wire messages. |
| `MdocSessionTranscriptBuilder` | The **proximity** transcript `[DeviceEngagementBytes, EReaderKeyBytes, Handover]` — filling the two slots currently hardcoded `WriteNull()`. QR engagement ⇒ `Handover = null`; NFC engagement ⇒ `NFCHandover`. |
| `MdocDeviceRequestParser` / `MdocDeviceRequestBuilder` | `DeviceRequest` / `ItemsRequest` (docType → namespace → element → `intentToRetain`). |
| `MdocDeviceResponseBuilder` | **The holder side that does not exist today.** Given selected credentials, requested elements, the transcript, and either a signer delegate or the MAC key, emits a `DeviceResponse`. Selective disclosure = include only the requested `IssuerSignedItemBytes`, spliced **verbatim** from storage. |

Widened, not duplicated:

- `IMdocService` gains `Verify(ReadOnlyMemory<byte> deviceResponse, byte[] sessionTranscript, MdocSessionKeys? keys)`,
  verifying **both** `deviceSignature` and `deviceMac`. The existing OID4VP overload delegates to it. The
  `VerifyDeviceBinding` private is generalised to take transcript bytes rather than rebuilding the OID4VP form.
- `MdocCodec.BuildDeviceAuthentication` is **reused unchanged** — it already takes an already-encoded
  transcript and splices it verbatim.

### 5.2 `IProximityTransport` — the seam

```csharp
public interface IProximityTransport : IAsyncDisposable
{
    Task<ProximityCapability> ProbeAsync(CancellationToken ct);
    Task StartPeripheralAsync(ProximityAdvert advert, CancellationToken ct);   // holder
    Task ConnectCentralAsync(ProximityTarget target, CancellationToken ct);    // reader
    Task SendAsync(byte[] payload, CancellationToken ct);
    event Action<byte[]> Received;
    Task StopAsync(CancellationToken ct);
}
```

Opaque bytes only — the transport knows nothing about CBOR, mdoc, or credentials.

Implementations:

- **`CapacitorProximityTransport`** — JS interop over the plugin. Uses the `DotNetObjectReference` push
  pattern established by `IConnectivity`/`BrowserConnectivity` (the only push-from-JS shape in the wallet).
  Bytes cross the boundary as **base64 strings**, matching the existing convention (`WebCryptoDeviceKeyService`,
  `SorchaIndexedDb`). Capability-probe with graceful degradation when `globalThis.Capacitor.Plugins` is
  `undefined` (plain browser), mirroring `IPasskeyInterop.IsSupportedAsync()` and `SorchaQrScanner.isSupported()`.
- **`LoopbackProximityTransport`** — in-process, wires a holder engine directly to a reader engine. See §8.

### 5.3 `sorcha-proximity` Capacitor plugin

New, at `mobile/plugins/sorcha-proximity/`. Swift (`CoreBluetooth`) + Kotlin (`android.bluetooth.le`) + a TS
shim. **The first native plugin in the project** — `mobile/wallet` currently has none.

- Methods: `probe()`, `startPeripheral({serviceUuid})`, `connectCentral({serviceUuid})`, `send({dataBase64})`,
  `stop()`. Events: `received({dataBase64})`, `stateChanged({state})`.
- Implements the 18013-5 **mdoc peripheral server mode** GATT profile (State, Client2Server, Server2Client,
  Ident characteristics) with MTU-aware chunking and the first-byte continuation flag.
- **Pairing-free** — 18013-5 forbids bonding.

Platform requirements:

- **iOS:** `NSBluetoothAlwaysUsageDescription` in `Info.plist`. The peripheral role is restricted in the
  **background** — irrelevant here: a proximity presentation is always foreground (the citizen is holding
  the phone up to a reader).
- **Android:** `BLUETOOTH_ADVERTISE`, `BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN` (with
  `usesPermissionFlags="neverForLocation"`), runtime permission prompts on Android 12+.

Both apps ship the same plugin; the holder uses the peripheral role, the reader the central role.

### 5.4 Holder — Sorcha Wallet PWA

New `/present/proximity` page plus a `ProximityPresentationService`. Also required (and currently absent):

- **mdoc credential storage** — extend `CachedCredential` + the IndexedDB cache with format discrimination
  and the raw CBOR `IssuerSigned`. The cache's existing **evict-and-continue** rule on undecryptable rows
  must be preserved.
- **mdoc DCQL** — `ParseAsync` must stop throwing when `vct_values` is absent, and matching must dispatch
  on `doctype_value` for `mso_mdoc` alongside `vct` for `dc+sd-jwt`.
- **Namespace/element consent** — extend `ConsentSheet` to render mdoc's namespace → element asks (and the
  `intentToRetain` flag, which is a real disclosure the citizen should see) beside the existing SD-JWT claim asks.

### 5.5 Reader — new Blazor WASM verifier + second Capacitor target

`Sorcha.Verifier` is Blazor **Server**; a browser cannot be a BLE central. The reader is a **new WASM host**
wrapped as a second Capacitor app (`mobile/verifier`).

This is precisely **F155's deferred "path B (WASM/offline verifier)"** — this feature is what makes it due.
It reuses `Sorcha.Verifier.Engine` (already WASM-safe, explicitly BouncyCastle-based for that reason) plus
`Sorcha.Mdoc`.

The F155 **four-layer verdict trail works offline unchanged**: `LivePresentation`, `IssuerSignature` and
`Revocation` are all decidable from the presentation and cached status lists; `RegisterAnchor` reports
`LayerStatus.Unverified`. That is exactly why `Unverified` exists and **never vetoes** an otherwise-passing
verdict — the offline reader is the case the enum was designed for.

### 5.6 Both formats over one session

The ISO session layer (engagement, key agreement, encryption, transcript) is **shared**. On top of it:

| Path | Request | Response | Binding |
|---|---|---|---|
| **ISO** (interop) | `DeviceRequest` / `ItemsRequest` | `DeviceResponse` (CBOR) | `deviceMac` (or `deviceSignature`) over `DeviceAuthentication` |
| **Sorcha-native** | DCQL | SD-JWT VP in a small CBOR envelope `{format, vp}` | KB-JWT whose `aud`/`nonce` bind to the **proximity session transcript hash** |

Binding the KB-JWT to the session transcript instead of an HTTPS `response_uri` gives the SD-JWT path replay
protection **equivalent** to the mdoc path, with no server in the middle. The reader dispatches on the
envelope. Because both ride the same encrypted session, session security is uniform and written once.

## 6. UI integration (first-class, not an afterthought)

The feature must be **reachable and exercisable from the UI of both apps** — a protocol library with a test
harness bolted on is not the deliverable.

- **Wallet:** a "Share in person" action on the credential detail view and on the main present surface,
  leading to `/present/proximity`. Shows the DeviceEngagement QR, an honest connection state, the consent
  sheet (with `intentToRetain` visible), and a completion beat. Presentations land in the existing
  presentation log / Activity history, marked as proximity — so a proximity share is auditable exactly like
  an online one.
- **Reader app:** scan-to-read as the primary action; the existing F155 Ask → session → **four-layer verdict**
  screens, with the register-anchor layer honestly reported `Unverified` when offline.
- **Graceful degradation:** on a device with no BLE, or in a plain browser, the proximity affordance is
  hidden (capability probe), not broken.

## 7. Risks and honest limits

| Risk | Position |
|---|---|
| **Self-consistency ≠ interop.** The evidence bar is our own two devices. Passing it proves our holder and our reader agree — it does **not** prove a certified reader accepts us. | Accepted deliberately. The design must not quietly assume otherwise. Byte-exact golden vectors (§8) are the mitigation that catches the errors self-consistency would hide. |
| **Tag-24 verbatim rule.** Digests and signatures are over the *tagged outer* bytes. Get it wrong and nothing verifies — and the failure is silent and total. | `MdocCbor.WrapTag24`/`UnwrapTag24` already implement it correctly and are reused. Golden vectors assert it. |
| **iOS background peripheral restriction.** | Not applicable — proximity presentation is always foreground. |
| **P-256 ECDH/HKDF/AES-GCM in Blazor WASM.** | BouncyCastle (pure managed), the same choice `Sorcha.Verifier.Engine` already made and ships with. |
| **mdoc is ES256/P-256-only at the format layer.** Sorcha's default wallets are frequently Ed25519. | Unchanged from F135 and out of scope to fix. The mdoc rail is P-256; the SD-JWT rail carries Ed25519 holders as it does today. |
| **First native plugin in the project.** Adds Swift + Kotlin to a codebase that has none, and both must be built on the Mac node. | The plugin surface is deliberately tiny (5 methods, opaque bytes). The Mac build node, fastlane lanes and signing already exist and are proven. |

## 8. Testing strategy

**The loopback transport is the centrepiece.** `LoopbackProximityTransport` wires the holder engine directly
to the reader engine in a single process — so the **entire protocol** (engagement, ECDH, HKDF, session
encryption, request, selective disclosure, DeviceResponse, `deviceMac`, verification, four-layer verdict) is
exercised in ordinary unit/integration tests with **no BLE and no phones**.

That leaves two-device testing responsible only for the transport — which is the one thing BLE is actually
responsible for.

1. **Golden vectors** — byte-exact fixtures for tag-24 splicing, the proximity `SessionTranscript`,
   `DeviceAuthentication`, and `MAC_structure`. These catch the class of bug that self-consistency hides:
   two implementations that agree with each other and disagree with the standard.
2. **Loopback integration** — full holder ↔ reader protocol, both formats, both device-auth modes.
3. **Negative tests** — tampered `DeviceResponse`, wrong transcript, replayed session, revoked credential,
   expired MSO, element not consented to. Each must fail closed.
4. **Two-device manual** — iPhone ↔ Android, both role directions, over real BLE. Proves the transport and
   the permission flows.

## 9. Delivery shape

The natural phasing, should it need to be split later (all of it is in scope now):

1. `Sorcha.Mdoc` extraction + COSE_Sign1/COSE_Mac0/session crypto + golden vectors. *No UI, no native code — fully testable.*
2. Session layer + `DeviceResponse` builder + loopback transport. *Whole protocol provable, still no phones.*
3. `sorcha-proximity` plugin (Swift + Kotlin) + `CapacitorProximityTransport`.
4. Wallet mdoc rail + `/present/proximity` UI.
5. WASM verifier host + reader Capacitor target + reader UI.
6. Two-device validation.

Phases 1–2 carry most of the risk and need no mobile toolchain at all.

## 10. Out of scope

- Wi-Fi Aware and NFC **data retrieval** (NFC **engagement** is in scope; NFC as the data channel is not).
- Certified-reader interop and formal 18013-5 conformance testing.
- Background/unattended presentation.
- mdoc issuance changes beyond binding `MSO.DeviceKey` to the new ECDH device key.
- The online OpenID4VP path, which is unchanged throughout.

---

## Related

- Feature 135 — mdoc/CBOR/COSE, unified trust. This feature is F135's explicit *"proximity deferred"* coming due.
- Feature 155 — Open Verifier PWA. Its deferred **path B (WASM/offline verifier)** is the reader host here.
- Feature 181 — DCQL dialect. Its `mso_mdoc` request vocabulary already exists; the wallet's inability to
  answer it is fixed here.
- Feature 114 — Citizen Wallet PWA. The device-key model extended in §3.
- `mobile-passkeys-associated-domains` — the WebView-capability-gap precedent that predicts the native-plugin answer.
- `mobile-build-pipeline` — the Mac build node, fastlane lanes and signing that the native code will need.
