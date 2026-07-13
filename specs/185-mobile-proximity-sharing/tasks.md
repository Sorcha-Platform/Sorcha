# Tasks: Mobile proximity credential sharing (ISO 18013-5 over BLE)

**Feature**: 185 | **Branch**: `185-mobile-proximity-sharing`
**Input**: [spec.md](./spec.md), [plan.md](./plan.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)
**Design of record**: `docs/superpowers/specs/2026-07-13-mobile-proximity-sharing-design.md`

**Tests are REQUIRED for this feature** — not as a default, but because the spec demands it: FR-022 (the
exchange must be provable without phones) and FR-023 / SC-003 (byte-exactness against published reference
data) are *functional requirements*, and the golden-vector tests are the only mitigation for the fact that
our evidence bar is self-consistency rather than certified interop. Tests are written **before** the code
they check, throughout.

---

## Phase 1: Setup

- [ ] T001 Create `src/Common/Sorcha.Mdoc/Sorcha.Mdoc.csproj` (net10.0, pure-managed) with package refs `System.Formats.Cbor` 10.0.8, `System.Security.Cryptography.Cose` 10.0.8, `BouncyCastle.Cryptography` 2.6.2 — and **no** `Sodium.Core` / `Nethermind.MclBindings`. Add a csproj comment stating the WASM-safety constraint, mirroring `Sorcha.Cryptography.Secp256k1.csproj`.
- [ ] T002 [P] Create `src/Common/Sorcha.Proximity.Abstractions/Sorcha.Proximity.Abstractions.csproj` (net10.0, pure-managed) referencing `Sorcha.Mdoc`.
- [ ] T003 [P] Create `tests/Sorcha.Mdoc.Tests/Sorcha.Mdoc.Tests.csproj` (xUnit v3, FluentAssertions 8.x, Moq 4.20.x, net10.0) with the standard licence header and file-scoped namespaces.
- [ ] T004 [P] Create `tests/Sorcha.Proximity.Tests/Sorcha.Proximity.Tests.csproj` (same stack).
- [ ] T005 Add all four projects to `Sorcha.sln`.
- [ ] T006 [P] Add `src/Common/Sorcha.Mdoc/README.md` documenting the project's purpose and its **one hard rule**: pure-managed only, because the Blazor WASM wallet consumes it and cannot load native P/Invoke.

---

## Phase 2: Foundational — the mdoc extraction (BLOCKS every user story)

**Nothing else can start until this lands.** The wallet physically cannot reference the mdoc code today:
`Sorcha.Cryptography` P/Invokes libsodium and MCL and will not load in WASM (research R-001).

- [ ] T007 Move `MdocCbor.cs`, `CoseX5Chain.cs`, `MdocCodec.cs`, `MdocModels.cs`, `MdocService.cs`, `MdocIssuer.cs` from `src/Common/Sorcha.Cryptography/Mdoc/` to `src/Common/Sorcha.Mdoc/`, preserving the folder shape (`Cbor/`, `Cose/`). Change namespaces `Sorcha.Cryptography.Mdoc*` → `Sorcha.Mdoc*`. **No behavioural change in this task.**
- [ ] T008 Delete `src/Common/Sorcha.Cryptography/Mdoc/` and add a `ProjectReference` to `Sorcha.Mdoc` **only where still needed**. Clean break — no type-forwarding shim.
- [ ] T009 [P] Re-point `src/Core/Sorcha.Blueprint.Engine/Credentials/MdocFormatHandler.cs` at the `Sorcha.Mdoc` namespaces; add the `ProjectReference`.
- [ ] T010 [P] Re-point `src/Services/Sorcha.Haip.Service/Services/MdocPresentationVerifier.cs` at the `Sorcha.Mdoc` namespaces; add the `ProjectReference`.
- [ ] T011 Move the existing mdoc tests into `tests/Sorcha.Mdoc.Tests/` and confirm they pass **unchanged** — this is the proof the move was behaviour-preserving (protects SC-010).
- [ ] T012 Add `Sorcha.Mdoc` as a `ProjectReference` to `src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj` and **build the WASM app**. A successful WASM build is the actual acceptance criterion for this phase — it is what proves the extraction achieved its purpose.

**Checkpoint**: `dotnet build` green, all pre-existing mdoc tests green, the wallet PWA compiles with `Sorcha.Mdoc` referenced.

---

## Phase 3: User Story 1 — Prove the exchange works end to end, without phones (P1) 🎯 MVP

**Goal**: a complete proximity presentation — engagement, session, request, selective disclosure, response,
device auth, verdict — running holder↔reader in one process, in CI, with no Bluetooth and no devices.

**Independent test**: `dotnet test tests/Sorcha.Proximity.Tests` — full exchange, no phone, no radio.

**This phase retires most of the feature's risk and needs no mobile toolchain at all.**

### Golden vectors first (FR-023 / SC-003 — write these before the code they check)

- [ ] T013 [P] [US1] Add reference-data fixtures from the ISO 18013-5 standard text to `tests/Sorcha.Mdoc.Tests/Fixtures/Iso18013_5/` — `SessionTranscript`, `DeviceAuthentication`, `MAC_structure`, `Sig_structure`, and a tag-24 wrap/unwrap case. **Read these from the standard; do not generate them from our own code** — a vector produced by the implementation it is meant to check proves nothing.
- [ ] T014 [P] [US1] Write `tests/Sorcha.Mdoc.Tests/Cose/CoseSign1BuilderTests.cs` — assert the `Sig_structure` bytes and the assembled `COSE_Sign1` match the fixtures, given a **raw** (pre-computed) signature.
- [ ] T015 [P] [US1] Write `tests/Sorcha.Mdoc.Tests/Cose/CoseMac0Tests.cs` — assert `MAC_structure` bytes and the HMAC-SHA256 tag match the fixtures; assert verify rejects a tampered tag.
- [ ] T016 [P] [US1] Write `tests/Sorcha.Mdoc.Tests/Proximity/SessionTranscriptTests.cs` — assert the proximity transcript is **byte-identical** to the fixture, with both `DeviceEngagementBytes` and `EReaderKeyBytes` **tag-24 wrapped** (contract rule #1 — the single most likely way to lose a week).
- [ ] T017 [P] [US1] Write `tests/Sorcha.Mdoc.Tests/Proximity/MdocSessionCryptoTests.cs` — assert `SkDevice`, `SkReader` and `EMacKey` derive to the fixture values from a fixed ECDH pair.

### COSE + session crypto

- [ ] T018 [US1] Implement `src/Common/Sorcha.Mdoc/Cose/CoseSign1Builder.cs` + `CoseSign1Verifier.cs` — compute `Sig_structure`, emit `[protected, unprotected, payload, signature]`. **Must accept a raw signature** (`byte[]`), because the device key is a non-extractable WebCrypto key exposing only `SignAsync(byte[]) → byte[]` and `CoseSign1Message.SignDetached` cannot consume it (research R-004).
- [ ] T019 [US1] Implement `src/Common/Sorcha.Mdoc/Cose/CoseMac0.cs` — build + verify, HMAC-SHA256, COSE alg 5, over `MAC_structure`. The BCL has no such type; this is the documented reason `MdocService` refuses MAC device auth today (research R-005).
- [ ] T020 [US1] Implement `src/Common/Sorcha.Mdoc/Proximity/MdocSessionCrypto.cs` — ECDH P-256 → HKDF-SHA256 → `SkDevice`/`SkReader`/`EMacKey`; AES-256-GCM encrypt/decrypt with the message counter. **BouncyCastle only** (research R-003 — pure-managed, therefore certain to work in WASM). Zeroise keys on dispose.
- [ ] T021 [US1] Implement `src/Common/Sorcha.Mdoc/Proximity/MdocSessionTranscriptBuilder.cs` — `[DeviceEngagementBytes, EReaderKeyBytes, Handover]`, both tag-24 wrapped, `Handover = null` for QR engagement. Reuse `MdocCbor.WrapTag24` — **do not reimplement the tag-24 rule**.

### Wire model

- [ ] T022 [P] [US1] Implement `src/Common/Sorcha.Mdoc/Proximity/DeviceEngagement.cs` — model + CBOR encode/decode, incl. `BleRetrievalOptions` (peripheral-server mode, service UUID).
- [ ] T023 [P] [US1] Implement `src/Common/Sorcha.Mdoc/Proximity/SessionMessages.cs` — `SessionEstablishment` / `SessionData` + codecs.
- [ ] T024 [P] [US1] Implement `src/Common/Sorcha.Mdoc/Proximity/MdocDeviceRequest.cs` — `DeviceRequest`/`DocRequest`/`ItemsRequest` parse + build, carrying `intentToRetain` per element (it is shown to the citizen — FR-009).

### The missing holder side

- [ ] T025 [US1] Implement `src/Common/Sorcha.Mdoc/Proximity/MdocDeviceResponseBuilder.cs` — given selected credentials, approved elements, the transcript, and a signer/MAC delegate, emit a `DeviceResponse`. **Selective disclosure MUST splice the stored `TaggedBytes` verbatim** — re-encoding an `IssuerSignedItem` changes bytes, changes the digest, and invalidates the issuer's signature over data the issuer really did sign (contract rule #3).
- [ ] T026 [US1] Write `tests/Sorcha.Mdoc.Tests/Proximity/DeviceResponseBuilderTests.cs` — assert only approved elements appear (FR-008 / SC-005, asserted **on the encoded bytes**, not on a reader's rendering) and that spliced item bytes are byte-identical to the stored originals.

### Verification side

- [ ] T027 [US1] Widen `src/Common/Sorcha.Mdoc/MdocService.cs`: add `Verify(deviceResponse, byte[] sessionTranscript, MdocSessionKeys? keys)`; generalise the private `VerifyDeviceBinding` to take transcript **bytes** instead of rebuilding the OID4VP form internally; add the `deviceMac` branch, **removing** the "MAC-based device authentication is not supported in v1" rejection. The existing OID4VP overload delegates to the new one — refactor, don't rewrite (protects SC-010).
- [ ] T028 [US1] Confirm the existing HAIP/OID4VP mdoc verification tests still pass unchanged after T027 (SC-010 regression gate).

### The seam and the loopback harness

- [ ] T029 [US1] Implement `src/Common/Sorcha.Proximity.Abstractions/IProximityTransport.cs` + `ProximityCapability` / `ProximityAdvert` / `ProximityTarget` / `ProximityDisconnectReason`, exactly per [contracts/proximity-transport.md](./contracts/proximity-transport.md). **Opaque bytes only** — the seam must not know about CBOR, mdoc or credentials, or the loopback harness becomes impossible.
- [ ] T030 [US1] Implement `src/Common/Sorcha.Proximity.Abstractions/LoopbackProximityTransport.cs` — `CreatePair()` returns two transports whose `SendAsync` feeds the other's `Received`. **Include fault injection: drop, delay, and single-byte corruption** — this is how the tamper and replay scenarios are tested.
- [ ] T031 [US1] Implement `src/Common/Sorcha.Proximity.Abstractions/ProximityHolderSession.cs` — the holder state machine per [data-model.md](./data-model.md) §2. **`AwaitingConsent → Responding` must be the only edge on which credential data can be encoded** (FR-010 — make it structurally true, not merely intended). Zeroise keys on every terminal path.
- [ ] T032 [US1] Implement `src/Common/Sorcha.Proximity.Abstractions/ProximityReaderSession.cs` — the reader state machine, producing the existing four-layer `VerificationOutcome` with `RegisterAnchor = Unverified` when offline (FR-014).

### The story's acceptance tests

- [ ] T033 [US1] Write `tests/Sorcha.Proximity.Tests/FullExchangeTests.cs` — holder ↔ reader over loopback, complete exchange, accepting verdict, **and assert the reader received exactly the requested elements and no more** (US1 scenario 1).
- [ ] T034 [P] [US1] Write `tests/Sorcha.Proximity.Tests/TamperTests.cs` — single-byte corruption of the response ⇒ rejected (US1 scenario 2).
- [ ] T035 [P] [US1] Write `tests/Sorcha.Proximity.Tests/ReplayTests.cs` — a response captured from one session, replayed into a fresh session ⇒ rejected (US1 scenario 3).
- [ ] T036 [P] [US1] Write `tests/Sorcha.Proximity.Tests/NegativeVerificationTests.cs` — expired MSO, revoked credential, bad issuer signature, wrong transcript ⇒ each rejected, no false accepts (SC-004).
- [ ] T037 [US1] Add both new test projects to CI so the exchange is proven on every change (SC-002).

**🎯 Checkpoint — MVP.** The entire protocol is proven in CI with no phone, no BLE, no network. Everything
that follows is transport and UI.

---

## Phase 4: User Story 2 — A citizen shares a credential in person (P2)

**Goal**: a citizen, offline, shares a credential from the wallet's ordinary surfaces to a real reader.

**Independent test**: on a real phone in airplane mode, complete a share; confirm it lands in history.

**Note**: the native plugin is built here because US2 is the earliest story that needs it. **US3 depends on
these plugin tasks** (T038–T044).

### The native plugin (the repo's first)

- [ ] T038 [US2] Scaffold `mobile/plugins/sorcha-proximity/` (Capacitor 8 plugin: TS definitions, iOS Swift, Android Kotlin) per [contracts/capacitor-plugin.md](./contracts/capacitor-plugin.md).
- [ ] T039 [P] [US2] Implement the iOS plugin in `mobile/plugins/sorcha-proximity/ios/Plugin/` — `CBPeripheralManager` (holder) + `CBCentralManager` (reader), the 18013-5 mdoc peripheral-server GATT profile, MTU chunking with a leading continuation byte, **pairing-free** (18013-5 forbids bonding). Add `NSBluetoothAlwaysUsageDescription`.
- [ ] T040 [P] [US2] Implement the Android plugin in `mobile/plugins/sorcha-proximity/android/` — `BluetoothLeAdvertiser` + `BluetoothGattServer` (holder), `BluetoothLeScanner` + `BluetoothGatt` (reader). Manifest: `BLUETOOTH_ADVERTISE`, `BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN` (`neverForLocation`). `probe()` MUST distinguish `permissionNotYetRequested` from `permissionDenied` so the UI can ask rather than dead-end (FR-021).
- [ ] T041 [US2] Implement `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/proximity-bridge.js` — **base64 strings** across the boundary (never `byte[]`), push to C# via `DotNetObjectReference`, following the `SorchaConnectivity` pattern.
- [ ] T042 [US2] Implement `src/Apps/Sorcha.Wallet.Pwa/Services/Proximity/CapacitorProximityTransport.cs` (`IProximityTransport`) with a capability probe that returns `Supported: false` — never throws — when `globalThis.Capacitor?.Plugins?.SorchaProximity` is `undefined` (FR-020; the app really does run in a plain browser, since `capacitor.config.json` uses `server.url`).
- [ ] T043 [US2] Wire the plugin into `mobile/wallet/package.json` + `cap sync`; add BLE permissions to the wallet's `Info.plist` and `AndroidManifest.xml`.
- [ ] T044 [US2] Build a debug **byte-echo harness** and verify a >MTU payload arrives byte-identical device→device — the one thing the C# suite cannot reach, so it gets its own bar (contract: capacitor-plugin.md).

### The second device key (research R-002)

- [ ] T045 [US2] Extend `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/webcrypto-bridge.js` with `generateEcdhP256` / `deriveBits` / `getEcdhPublicJwk`, keyed separately from the existing ECDSA key.
- [ ] T046 [US2] Extend `IDeviceKeyService` / `WebCryptoDeviceKeyService` with the ECDH key. **Both keys remain non-extractable.** The existing ECDSA key and the SD-JWT KB-JWT path are **untouched** (SC-010).
- [ ] T047 [P] [US2] Write `tests/Sorcha.Wallet.Pwa.Tests/Services/DeviceKeyServiceTests.cs` — assert the two keys are distinct, that the ECDH key cannot sign and the ECDSA key cannot derive, and that neither is extractable.

### Wallet mdoc rail

- [ ] T048 [US2] Extend the IndexedDB credential cache with `Format` / `DocType` / `IssuerSignedCbor` (data-model §3a). **Preserve the evict-and-continue rule** on undecryptable rows — a bad row must never abort the listing (this has already broken sync once).
- [ ] T049 [US2] Implement `src/Apps/Sorcha.Wallet.Pwa/Services/Proximity/ProximityPresentationService.cs` — composes `ProximityHolderSession` + `CapacitorProximityTransport` + the credential cache + the device keys.

### UI (FR-017 — ordinary surfaces, not a developer route)

- [ ] T050 [US2] Implement `src/Apps/Sorcha.Wallet.Pwa/Pages/PresentProximity.razor` — engagement QR, honest connection state, and a completion beat.
- [ ] T051 [US2] Extend `ConsentSheet` to render mdoc namespace→element asks **and the `intentToRetain` flag** beside the existing SD-JWT claim asks. `intentToRetain` is a real disclosure the citizen is entitled to see (FR-009).
- [ ] T052 [P] [US2] Add a "Share in person" action to the credential detail view **and** the present surface, gated on `ProbeAsync().Supported` so it is hidden — not broken — where unsupported (FR-017 / FR-020).
- [ ] T053 [P] [US2] Record completed in-person shares in the existing presentation log with a channel discriminator so they are distinguishable from online presentations (FR-019). Disclosed claim **names** only, never values — the rule the log already follows.
- [ ] T054 [P] [US2] Write bUnit tests in `tests/Sorcha.Wallet.Pwa.Tests/` — the affordance is hidden when unsupported; declining discloses nothing (US2 scenario 3); every requested element and its retain flag is shown before approval (US2 scenario 2).

**Checkpoint**: a citizen can share in person, offline, from the wallet's ordinary surfaces.

---

## Phase 5: User Story 3 — A verifier reads a credential in person (P2)

**Goal**: a verifier reads a credential and gets an honest verdict, including what it could not check offline.

**Independent test**: on a real device, read from a holder and receive a verdict with supporting detail.

**Depends on**: US1 (protocol) and the US2 plugin tasks T038–T044.

- [ ] T055 [US3] Create `src/Apps/Sorcha.Verifier.Pwa/` — a Blazor **WASM** host referencing `Sorcha.Verifier.Engine` (already WASM-safe) + `Sorcha.Mdoc` + `Sorcha.Proximity.Abstractions`. **This is F155's deferred "path B"**, which this feature makes due.
- [ ] T056 [US3] Add mdoc verification to `Sorcha.Verifier.Engine` — dispatch by format so the engine answers both `mso_mdoc` and `dc+sd-jwt`. It is SD-JWT-only today.
- [ ] T057 [US3] Implement the reader-side `CapacitorProximityTransport` (central role) in `Sorcha.Verifier.Pwa`, reusing the same plugin.
- [ ] T058 [US3] Implement `Pages/Read.razor` — QR scan → engagement → connect → request → response, driven by `ProximityReaderSession`.
- [ ] T059 [US3] Implement `Pages/Verdict.razor` — the four-layer trail. **`RegisterAnchor` must render as "not checked" — never as passed, never as failed** (FR-014 / SC-007). Show disclosed values and make clear that others were withheld (FR-016).
- [ ] T060 [P] [US3] Implement the "what do you need to know?" first screen, reusing the F155 `QuestionPresets` shape (FR-018).
- [ ] T061 [US3] Create the `mobile/verifier` Capacitor target (new bundle id), wire in the plugin, add BLE permissions to both platform manifests.
- [ ] T062 [P] [US3] Write bUnit tests asserting a skipped layer is **never** rendered as passed (SC-007 — the specific dishonesty this feature must not commit).

**Checkpoint**: a verifier can read a credential in person and gets an honest verdict.

---

## Phase 6: User Story 4 — Both credential kinds travel in person (P3)

**Goal**: the in-person exchange carries Sorcha-native SD-JWT credentials as well as standard mdoc ones.

**Independent test**: run the exchange twice, once per format; both verify.

- [ ] T063 [US4] Fix `src/Apps/Sorcha.Wallet.Pwa/Services/Presentation/PresentationEngine.cs`: stop throwing when `vct_values` is absent, and dispatch matching on `meta.doctype_value` for `mso_mdoc` alongside `vct` for `dc+sd-jwt`. The DCQL *request* vocabulary already speaks mdoc (F181) — only the wallet cannot answer it (research R-008).
- [ ] T064 [US4] Implement `SorchaProximityEnvelope` in `src/Common/Sorcha.Mdoc/Proximity/` — the CBOR `{ Format, Payload }` map carrying an SD-JWT VP where mdoc carries a `DeviceResponse` (data-model §4).
- [ ] T065 [US4] Bind the SD-JWT **KB-JWT `aud`/`nonce` to the `SessionTranscript` hash** rather than an HTTPS `response_uri`. This is what makes FR-005's "replay resistance holds identically for both kinds" **true** rather than aspirational — without it the SD-JWT path has no session binding at all.
- [ ] T066 [US4] Teach `ProximityReaderSession` to dispatch on the envelope and verify either format.
- [ ] T067 [P] [US4] Write `tests/Sorcha.Proximity.Tests/BothFormatsTests.cs` — full loopback exchange for each format, **and replay/tamper rejection for each** (US4 scenario 3).

**Checkpoint**: both credential kinds work in person.

---

## Phase 7: Polish & cross-cutting

- [ ] T068 [P] Handle every abandonment path cleanly — walk-away, screen lock, second-reader connection attempt (refused), out-of-range. Neither app may be left stuck; **nothing is disclosed** on any of them (spec Edge Cases).
- [ ] T069 [P] Offline revocation honesty: use the most recent cached status list and **surface its age**; absent ⇒ `Unverified`. Never treat unknown as good (spec Edge Cases; FR-014).
- [ ] T070 [P] Permission and Bluetooth-off flows in both apps: tell the citizen what is needed, let them grant it and continue. Refusal is not a dead end and not a crash (FR-021).
- [ ] T071 [P] Handle "you don't hold what was asked for" with a plain explanation, not an empty approval screen (spec Edge Cases).
- [ ] T072 [P] XML doc comments on all public APIs of `Sorcha.Mdoc` and `Sorcha.Proximity.Abstractions` (constitution: mandatory, and it gates the build).
- [ ] T073 [P] `README.md` for `Sorcha.Proximity.Abstractions` and `mobile/plugins/sorcha-proximity/`.
- [ ] T074 Update `.claude/skills/sorcha-architecture/SKILL.md` with a Feature 185 section, and `CLAUDE.md` if any cross-cutting pattern changed (mandatory per the documentation sync policy — PRs without it are not approved).
- [ ] T075 Update `.specify/MASTER-TASKS.md` (📋 → 🚧 → ✅).
- [ ] T076 Two-device validation: iPhone ↔ Android, **both roles**, **both formats**, **both devices in airplane mode** (SC-008 / SC-009 / SC-001 — the ≤30 s budget measured on real hardware).
- [ ] T077 Run the full pre-existing test suite and confirm the online presentation path is untouched (SC-010).

---

## Dependencies

```
Setup (T001–T006)
   ↓
Foundational — mdoc extraction (T007–T012)   ← BLOCKS EVERYTHING
   ↓
US1 — protocol, loopback-provable (T013–T037) 🎯 MVP, no phone needed
   ↓
   ├─→ US2 — holder: plugin (T038–T044) + device key + wallet rail + UI (T045–T054)
   │        ↓ (plugin)
   │   US3 — reader: WASM host + reader UI (T055–T062)
   │        ↓
   └─→ US4 — both formats over the one session (T063–T067)
              ↓
        Polish (T068–T077)
```

**US3 depends on US2's plugin tasks (T038–T044)** — it reuses the same plugin in the central role. All other
story-to-story coupling is minimal.

## Parallel opportunities

- **Setup**: T002, T003, T004, T006 in parallel after T001.
- **Foundational**: T009 and T010 in parallel (different services).
- **US1 golden vectors**: T013–T017 all in parallel — and all **before** the code they check.
- **US1 wire model**: T022, T023, T024 in parallel (different files).
- **US1 acceptance**: T034, T035, T036 in parallel.
- **US2 native**: T039 (iOS) and T040 (Android) in parallel — the two platform implementations of one contract.
- **Polish**: T068–T073 largely parallel.

## Implementation strategy

**Ship Phase 3 (US1) first and stop to look at it.** It is a genuine MVP: it proves the entire ISO exchange
in CI with no phone, no BLE and no network, and it is where nearly all the risk lives. If the golden vectors
are green, the rest of the feature is transport and UI — work that fails *visibly*.

The reverse order is the trap: building BLE first gives the *appearance* of progress while the protocol —
which fails silently and gives no diagnostic when the transcript is one byte wrong — remains entirely
unproven.

**Total**: 77 tasks. US1: 25 · US2: 17 · US3: 8 · US4: 5 · Setup/Foundational: 12 · Polish: 10.
