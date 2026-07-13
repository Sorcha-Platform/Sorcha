# Sorcha.Mdoc

ISO/IEC 18013-5 `mso_mdoc` credentials: CBOR/COSE codec, issuance, verification, and the **proximity
(device-retrieval) session layer**.

Built on the BCL (`System.Formats.Cbor`, `System.Security.Cryptography.Cose`) plus BouncyCastle. No
third-party CBOR library.

## The one hard rule

**This project must stay pure-managed.** It is referenced by `Sorcha.Wallet.Pwa` (the holder) and the reader
app — both Blazor **WASM** hosts, where native P/Invoke is unavailable.

Do **not** add `Sodium.Core`, `Nethermind.MclBindings`, or any other native dependency. That constraint is
the entire reason this code was extracted out of `Sorcha.Cryptography` in feature 185: that project P/Invokes
libsodium and MCL, so the wallet cannot reference it — and the wallet needs the mdoc codec in order to
present a credential in person.

The precedent is `Sorcha.Cryptography.Secp256k1`, split out for exactly the same reason.

## Layout

| Path | What it is |
|---|---|
| `Cbor/MdocCbor.cs` | Tag-24 (`#6.24(bstr .cbor X)`) wrap/unwrap and deterministic encoding. |
| `Cose/CoseX5Chain.cs` | `x5chain` (COSE header label 33). |
| `Cose/CoseKey.cs` | EC2 P-256 `COSE_Key` encode/parse. |
| `Cose/CoseSign1Builder.cs` | `COSE_Sign1` from a **raw** signature (feature 185). |
| `Cose/CoseMac0.cs` | `COSE_Mac0` / HMAC-SHA256 (feature 185). |
| `MdocCodec.cs`, `MdocModels.cs` | Wire format. |
| `MdocIssuer.cs` | Issuance. |
| `MdocService.cs` | Verification — both the OpenID4VP and proximity transcripts, and both device-auth forms. |
| `Proximity/` | Feature 185: `DeviceEngagement`, session messages, `MdocSessionCrypto`, `ProximitySessionTranscript`, `MdocDeviceRequest`, `MdocDeviceResponseBuilder`. |

## Things that will cost you a day if you get them wrong

**Tag-24 is over the outer bytes.** Digests and signatures are computed over the *tag-24-wrapped* bytes,
never the inner CBOR. `MdocCbor.WrapTag24` implements this; use it rather than reimplementing it.

**Never re-encode an `IssuerSignedItem`.** Selective disclosure splices the stored `TaggedBytes` **verbatim**.
Re-encoding can change bytes for the same logical value, which changes the digest, which invalidates the
issuer's signature over data the issuer really did sign.

**The session transcript has two forms and the standard uses both.** `SessionTranscript` (the bare array) is
spliced into `DeviceAuthentication`; `SessionTranscriptBytes` (tag-24-wrapped) is what the HKDF salt hashes.
`ProximitySessionTranscript` exposes both, named, so you never have to choose.

**`CoseSign1Message.SignDetached` cannot sign for the device.** The citizen's device key is a non-extractable
WebCrypto key exposing only `SignAsync(byte[]) → byte[]`; `SignDetached` demands an `AsymmetricAlgorithm`. Use
`CoseSign1Builder`, which takes a raw signature.

**`deviceMac` needs an ECDH device key.** `EMacKey` is derived by ECDH between the mdoc's **static** device key
(from the MSO) and the reader's **ephemeral** key. A WebCrypto ECDSA key cannot do ECDH — usages are fixed at
generation — which is why the wallet carries a *second* device key. See feature 185 design §3.

**Do not reconcile against the DIS draft of 18013-5.** A full draft is freely downloadable and has *different*
crypto (empty HKDF info, `0x00`/`0x01` salts, a 2-element session transcript). Following it produces a
confidently wrong implementation. We target **:2021 final**.

## WASM-safety (CI-enforced)

`scripts/check-wasm-safe.ps1` (workflow `wasm-safe-gate.yml`) fails the build if this project — or
`Sorcha.Proximity.Abstractions` or `Sorcha.Verifier.Engine` — takes a dependency that cannot load in the
browser. That failure mode is the nasty one: it compiles, passes CI, ships, and *then* breaks on a phone.

Banned: native P/Invoke packages (`Sodium.Core`, `Nethermind.MclBindings`); `X509Certificate2` /
`X509CertificateLoader` / `GetECDsaPublicKey`; `ECDiffieHellman`; `AesGcm`. The verify path uses BouncyCastle
throughout instead (`Cose/X509Leaf.cs`, `CoseSign1Builder`, `MdocSessionCrypto`).

**Not banned — and this is the counter-intuitive bit: `ECDsa` verification works fine under browser-wasm.**
`Sorcha.Verifier.Engine` uses it for ES256 today, inside the shipping wallet, on every presentation. New code
here prefers BouncyCastle for consistency, but that is a preference, not a portability requirement.

`MdocIssuer` is exempt: issuance is **server-side only** — a browser never holds an issuer private key.

## Reference data

`tests/Sorcha.Mdoc.Tests/Fixtures/IsoAnnexDVectors.cs` carries ISO 18013-5:2021 **Annex D** reference data, and
`IsoAnnexDVectorTests` reproduces the standard's own `SessionEstablishment` / `SessionData` **ciphertexts** and
verifies its own `deviceMac`. Provenance — and its limits: the values come from an open-source reproduction,
not from the ISO document itself — is documented on the fixture. Read that before trusting the bytes.

## Known gaps

- **`MdocIssuer` uses a flat namespace equal to the docType.** A real mDL separates them
  (`org.iso.18013.5.1` vs `org.iso.18013.5.1.mDL`). This is an interop blocker for *issuance* — though not for
  the proximity protocol, which is namespace-agnostic.
- **`readerAuth` is parsed but not verified.** Reader authentication is a distinct trust decision; it is
  honestly skipped rather than stubbed as "verified".

Specs: `specs/135-eudi-credential-format-trust/` (format), `specs/185-mobile-proximity-sharing/` (proximity).
