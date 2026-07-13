# Quickstart — Feature 185, mobile proximity credential sharing

## The one thing to understand first

**The hard part is not Bluetooth.** It is the ISO 18013-5 exchange, and it fails *silently*: get one byte
wrong in the `SessionTranscript` and every signature and MAC fails to verify, with no diagnostic that tells
you why. Bluetooth is the easy, visible part.

Everything in this plan is arranged around that fact:

- The whole protocol lives in **C#**, written once, shared by holder and reader (never twice, in Swift and
  Kotlin, where the test suite can't reach it).
- `LoopbackProximityTransport` runs the **entire exchange in one process**, so the protocol is provable in CI
  with **no phone and no BLE**.
- **Golden-vector tests** assert what we sign and hash against published reference data — because our holder
  and our reader agreeing with *each other* proves nothing about whether either agrees with the standard.

Phases 1–2 (the protocol) need no Mac, no signing, and no device.

## Start here

```bash
git checkout 185-mobile-proximity-sharing

# read in this order
docs/superpowers/specs/2026-07-13-mobile-proximity-sharing-design.md   # why it's shaped this way
specs/185-mobile-proximity-sharing/spec.md                             # what it must do
specs/185-mobile-proximity-sharing/contracts/session-protocol.md       # the 8 rules that actually matter
specs/185-mobile-proximity-sharing/research.md                         # what is settled vs what must be read from the standard
```

## Prove the protocol (no phone needed)

```bash
dotnet build
dotnet test tests/Sorcha.Mdoc.Tests/Sorcha.Mdoc.Tests.csproj          # golden vectors, COSE, transcript
dotnet test tests/Sorcha.Proximity.Tests/Sorcha.Proximity.Tests.csproj # full holder↔reader exchange, loopback
```

`Sorcha.Proximity.Tests` is the one that matters. It runs engagement → ECDH → session encryption → request →
consent → selective disclosure → `DeviceResponse` → `deviceMac` → verification → verdict, **end to end, in
one process.** If it is green, the remaining risk is the radio.

## Then the phones

```bash
# the plugin builds on the Mac node; the pipeline is already proven
ssh stuart@macmini 'cd ~/projects/Sorcha && git pull'
# Android via the runner; iOS MUST go via the SSH/user-session path (the runner daemon has no keychain)
```

Two devices, both roles, both formats. That's SC-008 and SC-009.

## Landmines (each has cost someone a day, somewhere)

| Landmine | The rule |
|---|---|
| Re-encoding an `IssuerSignedItem` during selective disclosure | **Splice the stored `TaggedBytes` verbatim.** Re-encoding changes bytes, which changes the digest, which invalidates the issuer's signature over data the issuer really did sign. |
| Hashing the inner CBOR instead of the tagged outer bytes | Digests and signatures are over `#6.24(bstr .cbor X)`. `MdocCbor.WrapTag24` already gets this right. Don't reimplement it. |
| Reaching for `Sorcha.Cryptography` from the wallet | It P/Invokes libsodium and MCL. **It will not load in WASM.** That's the entire reason `Sorcha.Mdoc` exists. |
| Reaching for `CoseSign1Message.SignDetached` | It needs an `AsymmetricAlgorithm`. The device key is a **non-extractable WebCrypto key** — you only get `SignAsync(byte[]) → byte[]`. Use `CoseSign1Builder`. |
| Trying to `deviceMac` with the existing device key | It's ECDSA-only. `deviceMac` needs **ECDH**, and WebCrypto fixes usages at generation. That's why there's a second device key. See design §3. |
| Marshalling `byte[]` across `IJSRuntime` | **Base64 strings.** Every existing bridge in the wallet does this. |
| Putting protocol logic in the plugin | The plugin moves opaque bytes. Protocol in the plugin is protocol the C# tests cannot reach — which defeats the whole strategy. |
| Treating "couldn't check" as "passed" | `LayerStatus.Unverified` ≠ `Pass`. An offline reader must say what it could not check. FR-014 / SC-007. |

## Definition of done

- The loopback suite is green in CI, including tamper, replay, expiry and revocation **rejections**.
- Golden vectors match published reference data byte for byte.
- A citizen can start an in-person share from the wallet's **ordinary** surfaces, and it appears in their
  history afterwards.
- A verifier can complete a read from the reader app's **first screen**, and the verdict says honestly what
  it could not check offline.
- It works iPhone ↔ Android, both roles, both credential formats, **with both devices in airplane mode**.
- The online presentation path still passes all its existing tests (SC-010).
