# Implementation Plan: Mobile proximity credential sharing (ISO 18013-5 over BLE)

**Branch**: `185-mobile-proximity-sharing` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/185-mobile-proximity-sharing/spec.md`

**Design of record**: `docs/superpowers/specs/2026-07-13-mobile-proximity-sharing-design.md`

## Summary

Give the Sorcha Wallet the ability to present a credential **in person and offline** over BLE, per ISO/IEC
18013-5 device retrieval, and build the **native reader** needed to receive it.

Technical approach: **thin native transport, one shared C# protocol.** A new Capacitor plugin does only BLE
and NFC engagement, moving opaque bytes. Every byte of ISO protocol — engagement, ECDH/HKDF session keys,
session encryption, the proximity `SessionTranscript`, `DeviceResponse` assembly, `COSE_Sign1` and
`COSE_Mac0` — lives in C#, written once, shared by holder and reader. This requires extracting the mdoc tree
out of `Sorcha.Cryptography` (which is not WASM-loadable — it P/Invokes libsodium and MCL) into a new
pure-managed `Sorcha.Mdoc` project, exactly as `Sorcha.Cryptography.Secp256k1` was extracted for the same
reason. A `LoopbackProximityTransport` runs the entire protocol holder↔reader in one process so it is
provable in CI with no phones.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (protocol, both apps). Swift 5.9+ and Kotlin (Capacitor plugin only).

**Primary Dependencies**:
- `System.Formats.Cbor` 10.0.8, `System.Security.Cryptography.Cose` 10.0.8 — already used by the mdoc tree.
- `BouncyCastle.Cryptography` 2.6.2 — **all** session crypto (ECDH P-256, HKDF-SHA256, AES-256-GCM,
  HMAC-SHA256). Chosen because it is pure-managed and therefore WASM-safe; `Sorcha.Verifier.Engine` already
  ships it in the WASM wallet for exactly this reason.
- Capacitor 8 (`@capacitor/core|ios|android`), CoreBluetooth (iOS), `android.bluetooth.le` (Android).
- **Explicitly NOT**: `Sodium.Core`, `Nethermind.MclBindings` (native P/Invoke — the reason for the extraction).

**Storage**: IndexedDB in both apps (existing `SorchaIndexedDb` bridge). mdoc credentials are cached as raw
CBOR alongside the existing SD-JWT rows. No server-side storage. No database migrations.

**Testing**: xUnit v3 + FluentAssertions 8.x + Moq (project standard). bUnit for Razor components. The
**loopback transport** is the primary integration harness. Golden-vector fixtures for byte-exactness.

**Target Platform**: Blazor WASM (both apps) hosted in a Capacitor WebView; iOS 15+ and Android 12+ (the
BLE permission model changed at 12).

**Project Type**: Mobile (two Capacitor targets) + shared .NET libraries. **No new backend service, no new
API endpoint, no ledger write** — the entire feature is client-side and offline by definition.

**Performance Goals**: Full exchange (engagement → verdict) in **under 30 seconds** wall-clock on real
hardware (SC-001). BLE MTU-chunked transfer of a typical `DeviceResponse` (a few KB) is well inside that;
the budget is dominated by the citizen reading the consent screen.

**Constraints**: **Offline by definition** — no network on either device at any point. Pairing-free (18013-5
forbids bonding). Foreground-only. One session at a time per device. Must not regress the online
presentation path (SC-010).

**Scale/Scope**: Two devices, one session, one credential exchange. "Scale" is not a dimension of this
feature; **correctness and byte-exactness are.**

## Constitution Check

*GATE: evaluated before Phase 0, re-evaluated after Phase 1 design. Result: **PASS**, with two conscious
deviations recorded below.*

| Principle | Assessment |
|---|---|
| **Microservices-first; independently deployable** | **N/A by design.** This feature adds no service. It is client-side and offline; a server in the middle is precisely what it removes. |
| **Internal comms MUST use gRPC; external MAY use REST** | **Not applicable.** There is no service-to-service communication. The BLE channel is device-to-device, and its wire format is mandated by ISO 18013-5 (CBOR/COSE over GATT). The constitution's transport rules govern Sorcha service comms, not a standards-defined proximity protocol. |
| **Cryptographic standards; industry-standard libraries** | **PASS, with a deviation** — see Complexity Tracking. `COSE_Sign1`-from-raw-signature and `COSE_Mac0` are hand-assembled because the BCL provides neither in a usable form. All *primitives* (ECDH, HKDF, AES-GCM, HMAC, ECDSA) come from BouncyCastle; only the standards-defined byte structures around them are ours. |
| **Never store mnemonics / secure key management** | **PASS.** Both device keys are non-extractable WebCrypto keys. No new key material is persisted, exported, or transmitted. |
| **Target .NET 10; DI throughout; async I/O** | **PASS.** |
| **≥80% unit coverage on core libraries** | **PASS** — `Sorcha.Mdoc` is a pure library with no I/O; the loopback harness makes even the session layer unit-testable. This is a feature that can genuinely reach high coverage. |
| **xUnit; separate test projects; mock external deps** | **PASS.** New `Sorcha.Mdoc.Tests`; the loopback transport is the mock for BLE. |
| **XML comments on public APIs; README per component** | **PASS** — required on `Sorcha.Mdoc` and the plugin. |
| **OpenAPI/Scalar for REST endpoints** | **N/A** — the feature adds no endpoints. |
| **Feature branch workflow** | **PASS** — `185-mobile-proximity-sharing`. |

## Project Structure

### Documentation (this feature)

```text
specs/185-mobile-proximity-sharing/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/           # Phase 1
│   ├── proximity-transport.md      # the C# ↔ native seam
│   ├── capacitor-plugin.md         # the JS/native plugin surface
│   └── session-protocol.md         # the on-the-wire ISO exchange
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks)
```

### Source Code (repository root)

```text
src/Common/
├── Sorcha.Mdoc/                          # NEW — pure-managed, WASM-safe
│   ├── Cbor/MdocCbor.cs                  #   moved from Sorcha.Cryptography
│   ├── Cose/CoseX5Chain.cs               #   moved
│   ├── Cose/CoseSign1Builder.cs          #   NEW — raw-signature COSE_Sign1
│   ├── Cose/CoseMac0.cs                  #   NEW — HMAC-SHA256 COSE_Mac0
│   ├── MdocCodec.cs                      #   moved + proximity transcript
│   ├── MdocModels.cs                     #   moved
│   ├── MdocService.cs                    #   moved + transcript overload + MAC verify
│   ├── MdocIssuer.cs                     #   moved
│   └── Proximity/
│       ├── DeviceEngagement.cs           #   NEW
│       ├── SessionMessages.cs            #   NEW — SessionEstablishment / SessionData
│       ├── MdocSessionCrypto.cs          #   NEW — ECDH → HKDF → AES-GCM
│       ├── MdocSessionTranscriptBuilder.cs  # NEW — the proximity transcript
│       ├── MdocDeviceRequest.cs          #   NEW — parse + build
│       └── MdocDeviceResponseBuilder.cs  #   NEW — the missing holder side
│
├── Sorcha.Proximity.Abstractions/        # NEW — IProximityTransport + the session engines
│   ├── IProximityTransport.cs
│   ├── ProximityHolderSession.cs         #   holder state machine
│   ├── ProximityReaderSession.cs         #   reader state machine
│   └── LoopbackProximityTransport.cs     #   the no-phones harness
│
├── Sorcha.Cryptography/                  # CHANGED — Mdoc/ folder removed (clean break)
└── Sorcha.Verifier.Engine/               # CHANGED — mdoc verification path added

src/Apps/
├── Sorcha.Wallet.Pwa/                    # holder
│   ├── Pages/PresentProximity.razor      #   NEW
│   ├── Services/Proximity/CapacitorProximityTransport.cs   # NEW
│   ├── Services/Proximity/ProximityPresentationService.cs  # NEW
│   ├── Services/WebCryptoDeviceKeyService.cs   # CHANGED — second (ECDH) device key
│   ├── Services/Presentation/PresentationEngine.cs  # CHANGED — mdoc DCQL + doctype match
│   └── wwwroot/js/proximity-bridge.js    #   NEW
│
└── Sorcha.Verifier.Pwa/                  # NEW — WASM verifier host (F155 "path B")
    ├── Pages/{Read,Verdict}.razor
    └── Services/Proximity/…              #   same transport, central role

mobile/
├── wallet/                               # CHANGED — plugin wired in, BLE permissions
├── verifier/                             # NEW — second Capacitor target
└── plugins/sorcha-proximity/             # NEW — the only native code in the repo
    ├── src/                              #   TS shim + definitions
    ├── ios/Plugin/                       #   Swift, CoreBluetooth
    └── android/src/main/java/…           #   Kotlin, android.bluetooth.le

tests/
├── Sorcha.Mdoc.Tests/                    # NEW — golden vectors + unit
├── Sorcha.Proximity.Tests/               # NEW — full loopback protocol + negative cases
└── Sorcha.Wallet.Pwa.Tests/              # CHANGED — bUnit for the new surfaces
```

**Structure Decision**: Two new `src/Common` libraries and two new test projects, following the existing
`Sorcha.Cryptography.Secp256k1` precedent for pure-managed/WASM-safe extraction. A second Capacitor target
and the repo's first native plugin. **No service, no endpoint, no migration.** The protocol libraries are
deliberately I/O-free so the whole exchange is testable without a phone — that property is what makes the
plan de-riskable, and it is why `IProximityTransport` is an interface in `Sorcha.Proximity.Abstractions`
rather than a class in the wallet.

## Phasing

Ordered so that **all the protocol risk is retired before any mobile toolchain is touched.**

| Phase | Delivers | Provable by |
|---|---|---|
| **1** | `Sorcha.Mdoc` extraction; `CoseSign1Builder`, `CoseMac0`, `MdocSessionCrypto`; proximity transcript; golden vectors | `dotnet test` — no phone, no BLE |
| **2** | Engagement, session messages, `DeviceRequest`/`DeviceResponse`, holder + reader session engines, `LoopbackProximityTransport` | `dotnet test` — **entire protocol**, still no phone |
| **3** | `sorcha-proximity` Capacitor plugin (Swift + Kotlin) + `CapacitorProximityTransport` | Two devices, byte echo |
| **4** | Wallet mdoc rail (storage, DCQL doctype matching, consent) + `/present/proximity` UI | bUnit + device |
| **5** | `Sorcha.Verifier.Pwa` WASM host + `mobile/verifier` target + reader UI | Device |
| **6** | Two-device validation, both roles, both formats | Manual, on iPhone ↔ Android |

Phases 1–2 carry most of the risk and need **no** Mac build node, **no** signing, and **no** device.

## Complexity Tracking

| Violation | Why needed | Simpler alternative rejected because |
|---|---|---|
| **Hand-assembled `COSE_Sign1` (from a raw signature) and `COSE_Mac0`** — rather than using a library type | The wallet's device key is a **non-extractable WebCrypto key**, reachable only as `SignAsync(byte[]) → byte[]`. `CoseSign1Message.SignDetached` requires an `AsymmetricAlgorithm` and **cannot consume it**. Separately, the BCL has **no `COSE_Mac0` type at all** (this is the documented reason `MdocService` refuses MAC device auth today). | Making the device key extractable would defeat the entire point of hardware-backed, non-exportable key storage. Doing the COSE assembly natively in Swift/Kotlin would duplicate the standard's byte structures in two more languages. The precedent for hand-rolling a standard's byte structure rather than taking a bad dependency is established in this repo by F182 (RLP encoder, rather than Nethereum). |
| **A second device key on the citizen's phone** | ISO 18013-5 derives `EMacKey` by ECDH between the **static device key in the MSO** and the reader's ephemeral key — so the MSO device key must be **ECDH-capable**. In WebCrypto a key's usages are fixed at generation and a key **cannot be both ECDSA and ECDH**. The existing device key is ECDSA-only. | There is no alternative: with one ECDSA key, `deviceMac` is structurally impossible. Restricting to `deviceSignature`-only would satisfy our own reader but leaves our reader unable to read any wallet that chooses `deviceMac` (which most real EUDI wallets do), and makes every presentation the citizen gives non-repudiable transferable evidence. Full reasoning in the design of record §3. |
| **A second Capacitor app (the reader)** | A browser cannot be a BLE central, and `Sorcha.Verifier` is Blazor **Server**. Without a reader, the feature cannot be demonstrated, exercised, or tested at all. | Reusing the existing verifier is impossible (wrong hosting model, no BLE). Depending on a third-party certified reader was explicitly rejected as the evidence bar. The cost is contained because the reader reuses `Sorcha.Verifier.Engine` (already WASM-safe) and the same plugin, and it discharges F155's already-planned "path B". |
