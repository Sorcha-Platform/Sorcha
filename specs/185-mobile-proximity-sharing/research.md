# Phase 0 — Research

**Feature**: 185 — Mobile proximity credential sharing
**Date**: 2026-07-13

Every scope-level unknown was settled with the user *before* the design of record was written (protocol,
roles, formats, device-auth mode, BLE role, evidence bar). This document records the **technical** decisions
that follow from those, and — importantly — the things that must be **verified against the standard text
during implementation** rather than assumed from here.

---

## R-001 — The mdoc tree must be extracted before the wallet can touch it

**Decision**: Create `src/Common/Sorcha.Mdoc/`, a pure-managed project. Move `MdocCbor`, `CoseX5Chain`,
`MdocCodec`, `MdocModels`, `MdocService`, `MdocIssuer` into it verbatim. `Sorcha.Cryptography` drops the
folder; `Sorcha.Blueprint.Engine` and `Sorcha.Haip.Service` re-point at the new project. **Clean break, no
type-forwarding shim.**

**Rationale**: `Sorcha.Cryptography` references `Sodium.Core` (libsodium P/Invoke) and
`Nethermind.MclBindings` — both native. It is **not WASM-loadable**, and `Sorcha.Wallet.Pwa.csproj` does not
reference it and cannot. The `Mdoc/` subtree itself is already clean BCL (`System.Formats.Cbor`,
`System.Security.Cryptography.Cose`), so the extraction is a move, not a rewrite.

**Precedent**: `Sorcha.Cryptography.Secp256k1` exists for exactly this reason — its own csproj comment says
the verifier engine "is consumed by the Blazor WASM PWA, so it must stay native-dependency-free and cannot
reference `Sorcha.Cryptography`". Same constraint, same answer.

**Alternatives considered**: (a) Multi-target `Sorcha.Cryptography` with the native deps conditioned out for
`browser-wasm` — rejected: fragile, and it leaves a project whose capability silently varies by TFM.
(b) Duplicate the mdoc code into the wallet — rejected: two copies of the tag-24 rules is the exact failure
mode the design is built to avoid.

---

## R-002 — `deviceMac` requires an ECDH device key; the existing one is ECDSA-only

**Decision**: The wallet gains a **second** non-extractable P-256 device key, generated with `deriveBits`
usage. Sorcha-issued mdoc credentials bind `MSO.DeviceKey` to **that** key. The existing ECDSA key is
untouched and continues to sign SD-JWT KB-JWTs.

**Rationale**: ISO 18013-5 derives `EMacKey` by ECDH between the **static device key published in the MSO**
and the reader's **ephemeral** key. So the MSO's device key must be ECDH-capable. WebCrypto fixes a key's
usages at generation: a key **cannot be both ECDSA (`sign`) and ECDH (`deriveBits`)**. The wallet's existing
`WebCryptoDeviceKeyService` key is `sign`-only. Therefore it structurally cannot produce a `deviceMac`.

**Consequence**: an mdoc credential bound to the ECDH key can produce `deviceMac` but **not**
`deviceSignature` — which is correct and sufficient: 18013-5 requires exactly one of the two, and a
conformant reader must accept either. `MdocIssuer.holderDeviceKeyCose` must be fed the ECDH key's COSE_Key
for proximity-capable credentials.

**Alternatives considered**: (a) `deviceSignature`-only — would work between our own holder and reader, but
leaves our reader unable to read wallets that choose `deviceMac` (most real EUDI wallets do, because a MAC is
not transferable evidence whereas a signature is), and makes every citizen presentation non-repudiable.
Explicitly rejected by the user. (b) Do the ECDH natively in the plugin so the key can live in the Secure
Enclave / Keystore — rejected for now: it pushes crypto into the thin transport, which is the one thing the
architecture forbids. Worth revisiting if hardware-backed key storage becomes a requirement.

**⚠ To verify during implementation**: the exact salt/info inputs to the `EMacKey` HKDF, and whether the
static-vs-ephemeral pairing is as stated, must be read **from the standard text**, not from this note. This
is the single highest-consequence detail in the feature.

---

## R-003 — All session crypto via BouncyCastle

**Decision**: ECDH P-256, HKDF-SHA256, AES-256-GCM and HMAC-SHA256 all come from
`BouncyCastle.Cryptography` (already a dependency of both `Sorcha.Cryptography` and
`Sorcha.Verifier.Engine`).

**Rationale**: it is pure-managed and therefore certain to work in Blazor WASM. `Sorcha.Verifier.Engine`
already ships BouncyCastle into the WASM wallet for precisely this reason (its Ed25519 verification could not
use libsodium). Using the BCL's `System.Security.Cryptography` types instead would put us at the mercy of the
browser-crypto subset available under `browser-wasm`, and would be an avoidable, late-discovered risk.

**Alternatives considered**: a WebCrypto JS bridge for the session crypto (the wallet already has
`webcrypto-bridge.js`) — rejected: it would split the protocol across the JS boundary and make the loopback
harness (the whole de-risking strategy) impossible to run in a plain test process.

---

## R-004 — COSE_Sign1 must be hand-assembled from a raw signature

**Decision**: Add `CoseSign1Builder` / `CoseSign1Verifier` to `Sorcha.Mdoc`, computing the `Sig_structure`
and emitting the `[protected, unprotected, payload, signature]` array directly.

**Rationale**: the device key is reachable **only** as `SignAsync(byte[]) → byte[]` (a non-extractable
WebCrypto key). `CoseSign1Message.SignDetached` requires an `AsymmetricAlgorithm` instance and cannot consume
a raw-signature delegate. There is no public API on the BCL COSE types that accepts a pre-computed signature.

**Precedent**: F182 hand-rolled an RLP encoder rather than take Nethereum; F177 hand-rolled secp256k1
verification on BouncyCastle rather than fight `ECDsa`. Hand-rolling a *standard's byte structure* (as
opposed to a crypto primitive) is established practice in this repo.

---

## R-005 — `COSE_Mac0` does not exist in the BCL

**Decision**: Add `CoseMac0` (build + verify) to `Sorcha.Mdoc`. HMAC-SHA256, COSE alg 5, over the
`MAC_structure`.

**Rationale**: this is the *documented* reason `MdocService` refuses MAC device auth today — the model's own
XML doc says "the MAC path is preserved as raw bytes but not verified — the BCL has no COSE_Mac0 type". The
bytes already round-trip through `MdocCodec` as `DeviceAuth.DeviceMacRaw`; what is missing is only the
structure and the HMAC. This is small, self-contained, and fully golden-vector-testable.

---

## R-006 — Reuse `BuildDeviceAuthentication`; generalise `VerifyDeviceBinding`

**Decision**: `MdocCodec.BuildDeviceAuthentication(byte[] sessionTranscript, docType, deviceNameSpacesBytes)`
is **reused unchanged** — it already takes an *already-encoded* transcript and splices it verbatim, so it is
transport-agnostic by luck of its existing signature.

`MdocService` gains `Verify(deviceResponse, byte[] sessionTranscript, MdocSessionKeys? keys)`. The private
`VerifyDeviceBinding` is generalised to accept transcript **bytes** instead of internally calling
`BuildOpenId4VpSessionTranscript`, and gains the `deviceMac` branch. The existing OID4VP overload delegates
to the new one, so the online path is refactored-not-rewritten and SC-010 is protected by the existing tests.

**Rationale**: minimum surface change to a component that currently works. The one thing that must **not**
happen is a parallel mdoc verifier — two verifiers is two answers.

---

## R-007 — The proximity `SessionTranscript` fills two slots that are currently `null`

**Decision**: `MdocSessionTranscriptBuilder` emits `[DeviceEngagementBytes, EReaderKeyBytes, Handover]`.
For QR engagement the handover is `null`; NFC engagement carries an `NFCHandover`.

**Rationale**: `MdocCodec.BuildOpenId4VpSessionTranscript` already emits the three-element shape — with the
first two elements **hardcoded `w.WriteNull()`**. Those two nulls are *literally the proximity slots*. The
proximity builder is the same structure with them populated. Both must be **tag-24 wrapped** and spliced
verbatim; `MdocCbor.WrapTag24` already implements that rule correctly and is reused.

**⚠ To verify during implementation**: byte-exactness of the transcript against published reference data
(SC-003). This is the classic silent-failure point — a wrong transcript means every signature and MAC fails
with no useful diagnostic.

---

## R-008 — The wallet cannot currently parse or match an mdoc request

**Decision**: `PresentationEngine.ParseAsync` must stop throwing when `vct_values` is absent, and matching
must dispatch on `meta.doctype_value` for `mso_mdoc` alongside `vct` for `dc+sd-jwt`.

**Rationale**: `DcqlFormats.MsoMdoc` already exists and `DcqlCredentialQuery.Validate()` already accepts
`mso_mdoc` with a `doctype_value` — the *request* vocabulary (F181) already speaks mdoc. But the wallet's
`ParseAsync` throws `FormatException("Request object's credential query carries no vct_values.")` and
`MatchQuery` compares `Vct` strings only. So an mdoc query is currently unparseable **and** unmatchable in
the wallet. This is a gap in the wallet, not in the dialect.

---

## R-009 — The reader must be a new WASM host

**Decision**: New `src/Apps/Sorcha.Verifier.Pwa` (Blazor WASM), wrapped as `mobile/verifier`. Reuses
`Sorcha.Verifier.Engine` (already WASM-safe) + `Sorcha.Mdoc`.

**Rationale**: `Sorcha.Verifier` is Blazor **Server** — a browser cannot be a BLE central, and a server-hosted
app cannot reach a phone's Bluetooth radio at all. This is **F155's already-planned "path B (WASM/offline
verifier)"**, listed there as roadmap; this feature is what makes it due.

**The four-layer verdict works offline unchanged**: `LivePresentation`, `IssuerSignature` and `Revocation`
are all decidable from the presentation plus cached status lists. `RegisterAnchor` returns
`LayerStatus.Unverified` — which **never vetoes** an otherwise-passing verdict. The offline reader is the
exact case that enum value was designed for; no new verdict semantics are needed.

---

## R-010 — Transport contract: opaque bytes, capability-probed

**Decision**: `IProximityTransport` exposes `ProbeAsync / StartPeripheralAsync / ConnectCentralAsync /
SendAsync / Received / StopAsync` and knows nothing about CBOR, mdoc, or credentials.

Two implementations: `CapacitorProximityTransport` (JS interop) and `LoopbackProximityTransport` (in-process).

**Interop shape**: bytes cross the JS boundary as **base64 strings** (the established convention —
`WebCryptoDeviceKeyService` and `SorchaIndexedDb` both do this; `byte[]` is never marshalled). Push from JS
uses `DotNetObjectReference` + `[JSInvokable]`, the pattern `BrowserConnectivity` established and the only
push-from-JS pattern in the wallet. Capability probing with graceful degradation when
`globalThis.Capacitor?.Plugins?.SorchaProximity` is `undefined` (a plain browser), mirroring
`IPasskeyInterop.IsSupportedAsync()` and `SorchaQrScanner.isSupported()`.

**Rationale**: keeping the seam at opaque bytes is what makes `LoopbackProximityTransport` possible, and the
loopback harness is the entire de-risking strategy. If the seam knew about mdoc, the protocol could not be
tested without a phone.

---

## R-011 — Platform requirements for the plugin

**iOS**: `NSBluetoothAlwaysUsageDescription` in `Info.plist`. The peripheral role is restricted in the
**background** — **not applicable**: an in-person presentation is always foreground (the citizen is holding
the phone up). CoreBluetooth `CBPeripheralManager` (holder) and `CBCentralManager` (reader).

**Android**: `BLUETOOTH_ADVERTISE`, `BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN` with
`usesPermissionFlags="neverForLocation"`; runtime permission prompts required on Android 12+.
`BluetoothLeAdvertiser` + `BluetoothGattServer` (holder), `BluetoothLeScanner` + `BluetoothGatt` (reader).

**Both**: **pairing-free** — 18013-5 forbids bonding. MTU-aware chunking with a leading continuation byte.

**Note on the build pipeline**: this is the repo's **first native plugin**. `mobile/wallet` today has zero
plugins and its only hand-edited native file is `App.entitlements`. The Mac build node, fastlane lanes and
signing already exist and are proven (all four lanes shipped end-to-end), so the *pipeline* is not new risk;
the *native code* is.

**⚠ Also**: `capacitor.config.json` uses `server.url` (the app loads the remotely-hosted PWA rather than
bundling `www`). A plugin's JS shim is still injected into the WebView by the Capacitor bridge, so
`globalThis.Capacitor.Plugins.SorchaProximity` is reachable — but it will be `undefined` in a plain browser,
which is exactly what R-010's capability probe handles.

---

## Open items deliberately left to implementation

These are not unknowns of *approach* — they are details that must be read from the standard rather than
guessed, and getting them wrong fails loudly in the golden-vector tests (which is the point of having them):

1. Exact `EMacKey` / `SKDevice` / `SKReader` HKDF salt and info strings (R-002, R-003).
2. Exact `DeviceEngagement` CBOR structure and the BLE `DeviceRetrievalMethod` option map (R-007, R-011).
3. Exact GATT service/characteristic UUIDs and the state-machine bytes for peripheral server mode (R-011).
4. The session-encryption message counter's exact construction and its role in the AES-GCM nonce (R-003).

**Mitigation**: each is covered by a golden-vector test written *before* the implementation it checks
(FR-023 / SC-003). A wrong guess fails a test rather than shipping.
