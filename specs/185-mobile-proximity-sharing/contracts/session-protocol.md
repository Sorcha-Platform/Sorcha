# Contract — the proximity exchange (on the wire)

**Normative source**: ISO/IEC 18013-5, device retrieval over BLE. **This document is a map, not the
territory** — every byte-level structure below MUST be implemented against the standard text and asserted
against published reference data (FR-023 / SC-003). Where this document and the standard disagree, the
standard wins and this document is wrong.

## The exchange

```
HOLDER (BLE peripheral)                              READER (BLE central)
────────────────────────                             ────────────────────
1. generate ephemeral EDeviceKey
   build DeviceEngagement
   { Version, Security{ CipherSuite, EDeviceKeyBytes },
     DeviceRetrievalMethods[ BLE: peripheral-server, serviceUuid ] }
   display as QR  ──────────── scanned ───────────▶  2. decode DeviceEngagement
   start advertising serviceUuid                        generate ephemeral EReaderKey

                        ◀────── BLE connect ──────     3. connect to serviceUuid

                                                     4. SessionTranscript =
                                                          [ DeviceEngagementBytes,   ← tag-24
                                                            EReaderKeyBytes,         ← tag-24
                                                            Handover ]               ← null for QR
                                                        ECDH(EReaderKey.priv, EDeviceKey.pub)
                                                        → HKDF → SkDevice, SkReader
                                                        encrypt DeviceRequest with SkReader

   5. ◀── SessionEstablishment{ EReaderKeyBytes, Data } ──

   6. compute the SAME SessionTranscript
      ECDH(EDeviceKey.priv, EReaderKey.pub) → same keys
      decrypt DeviceRequest with SkReader
      ── if the transcripts differ, everything below fails and nothing explains why ──

   7. match request → held credentials
      SHOW THE CITIZEN every requested element
      + which are intentToRetain
      ── nothing is encoded before approval ──

   8. on approval, build the response:
        ISO path:     DeviceResponse
                        IssuerSigned  = requested items only, TaggedBytes spliced VERBATIM
                        DeviceSigned  = deviceMac (COSE_Mac0, HMAC-SHA256, EMacKey)
                                          over DeviceAuthentication(SessionTranscript, docType, ns)
        Sorcha path:  SorchaProximityEnvelope{ "dc+sd-jwt", SD-JWT VP }
                        KB-JWT aud/nonce bound to the SessionTranscript hash
      encrypt with SkDevice

      ── SessionData{ Data } ────────────────────▶  9. decrypt with SkDevice
                                                        verify:
                                                          issuer signature (COSE_Sign1 / JWS)
                                                          value digests  (over TAGGED bytes)
                                                          device binding (deviceMac / KB-JWT)
                                                          validity window
                                                          revocation (cached status list)
                                                        → four-layer VerificationOutcome
                                                          RegisterAnchor = Unverified (offline)

  10. ◀────────── SessionData{ Status: terminate } ────────────
      both sides zeroise keys
```

## The rules that actually matter

| # | Rule | Consequence of breaking it |
|---|---|---|
| 1 | **The `SessionTranscript` must be byte-identical on both sides.** Both elements are **tag-24 wrapped**. | Every signature and MAC fails. There is **no useful diagnostic** — it just doesn't verify. This is the single most likely way to lose a week. |
| 2 | **Digests and signatures are over the *tagged outer* bytes** (`#6.24(bstr .cbor X)`), never the inner CBOR. | Nothing verifies. `MdocCbor.WrapTag24` already gets this right — use it, don't reimplement it. |
| 3 | **Selective disclosure splices stored `TaggedBytes` verbatim.** Never re-encode an `IssuerSignedItem`. | Re-encoding can change bytes, which changes the digest, which invalidates the issuer's signature over data the issuer *did* sign. |
| 4 | **Nothing is encoded before consent.** | FR-010. The state machine must make this structurally true, not merely intended. |
| 5 | **Only requested-and-approved elements appear in the response.** | FR-008 / SC-005 — verified by inspecting the wire, not the reader's display. |
| 6 | **Replay must fail for both formats.** mdoc binds via the transcript in `DeviceAuthentication`; SD-JWT binds via the KB-JWT's `aud`/`nonce` over the transcript hash. | FR-005 says "identically for both kinds". Without the SD-JWT transcript binding, that is false. |
| 7 | **Keys are zeroised at session end**, on every exit path including abandonment. | They are session-scoped secrets; the process outlives the session. |
| 8 | **Offline ≠ unknown-is-good.** A revocation check that could not be freshened reports as `Unverified` with the data's age, never as `Pass`. | FR-014 / SC-007. `LayerStatus.Unverified` exists for precisely this, and never vetoes. |

## Verdict semantics (unchanged from F155)

The reader produces the existing four-layer `VerificationOutcome`. **No new verdict semantics are needed** —
the offline reader is the case `LayerStatus.Unverified` was designed for:

| Layer | Offline behaviour |
|---|---|
| `LivePresentation` | Decidable — `Pass`/`Fail`. |
| `IssuerSignature` | Decidable from the credential's own chain — `Pass`/`Fail`. |
| `Revocation` | Decidable from the **cached** status list. Stale-but-present ⇒ `Pass`/`Fail` with the age surfaced. Absent ⇒ `Unverified`. |
| `RegisterAnchor` | **`Unverified`** — needs the register, which needs a network. **Never vetoes** an otherwise-passing verdict. |

`OverallPass = Accepted && no layer is Fail`. An `Unverified` layer reduces assurance and is shown as such;
it does not reject. A `Fail` rejects.
