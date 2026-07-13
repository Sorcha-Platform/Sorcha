# Phase 1 — Data model

**Feature**: 185 — Mobile proximity credential sharing
**Date**: 2026-07-13

**There is no database schema in this feature.** No table, no EF entity, no migration, no ledger write. The
exchange is offline and ephemeral by definition. What follows is (1) the **wire** model mandated by ISO
18013-5, (2) the **session** model held in memory for the life of one exchange, and (3) the two **persisted**
client-side additions.

---

## 1. Wire model (ISO 18013-5, CBOR)

Existing types in `Sorcha.Mdoc` (moved verbatim, unchanged): `IssuerSignedItem`, `IssuerSignedItemBytes`,
`IssuerSigned`, `ValidityInfo`, `MsoStatus`, `MobileSecurityObject`, `DeviceAuth`, `DeviceSigned`,
`Document`, `DeviceResponse`.

New types:

| Type | Shape | Notes |
|---|---|---|
| `DeviceEngagement` | `{ Version, Security{CipherSuite, EDeviceKeyBytes}, DeviceRetrievalMethods[] }` | What the holder encodes into the QR. Contains an **ephemeral** public key and the BLE service UUID. **Carries nothing about the citizen** — it is pre-identity. |
| `BleRetrievalOptions` | `{ SupportsPeripheralServer, SupportsCentralClient, PeripheralServerUuid, ... }` | The BLE `DeviceRetrievalMethod` option map. v1 advertises peripheral-server only. |
| `SessionEstablishment` | `{ EReaderKeyBytes, Data }` | Reader → holder, first message. `Data` is the encrypted `DeviceRequest`. |
| `SessionData` | `{ Data?, Status? }` | Either direction thereafter. `Data` is ciphertext; `Status` terminates the session. |
| `DeviceRequest` | `{ Version, DocRequests[] }` | Each `DocRequest` = `ItemsRequest{ DocType, NameSpaces{ ns → { element → intentToRetain:bool } } }`. |
| `MdocSessionKeys` | `{ SkDevice, SkReader, EMacKey }` | **In-memory only. Never persisted, never logged, never leaves the process.** Zeroised at session end. |
| `SorchaProximityEnvelope` | `{ Format, Payload }` | The Sorcha-native path (§4). CBOR map carrying an SD-JWT VP where mdoc would carry a `DeviceResponse`. |

**The tag-24 rule governs all of these.** Digests and signatures are computed over the **tagged outer bytes**
(`#6.24(bstr .cbor X)`), never over the inner CBOR. `MdocCbor.WrapTag24`/`UnwrapTag24` already implement this
correctly and are reused; `IssuerSignedItemBytes` already carries `TaggedBytes` alongside the decoded `Item`
for exactly this reason. **Selective disclosure splices the stored `TaggedBytes` verbatim** — it must never
re-encode an item, because re-encoding can change bytes and invalidate the issuer's digest.

---

## 2. Session model (in memory, one exchange)

Both engines are state machines over `IProximityTransport`. They hold no state between sessions.

### `ProximityHolderSession`

```text
Idle
  → Engaging          (ephemeral EDeviceKey generated; DeviceEngagement encoded to QR; advertising)
  → Connected         (reader connected over BLE)
  → SessionEstablished(EReaderKey received; SessionTranscript computed; SkDevice/SkReader/EMacKey derived)
  → RequestReceived   (DeviceRequest decrypted; matched against held credentials)
  → AwaitingConsent   (citizen shown every requested element + intentToRetain)
  → Responding        (approved elements only; DeviceResponse built; deviceMac computed)
  → Complete | Declined | Failed | Abandoned
```

**Invariants:**
- `AwaitingConsent → Responding` is the **only** edge on which credential data may be encoded. From any
  other state, a transition to `Declined`/`Abandoned`/`Failed` discloses nothing (FR-010).
- The ephemeral `EDeviceKey` is generated per session and **never reused** across sessions.
- Exactly one session at a time; a second connection attempt is refused (spec Edge Cases).
- On any terminal state, `MdocSessionKeys` is zeroised.

### `ProximityReaderSession`

```text
Idle
  → Engaged           (QR scanned; DeviceEngagement decoded)
  → Connected         (BLE central connected to the advertised service)
  → SessionEstablished(ephemeral EReaderKey generated; SessionTranscript computed; keys derived)
  → RequestSent
  → ResponseReceived
  → Verified          (four-layer VerificationOutcome produced)
  → Failed | Abandoned
```

---

## 3. Persisted additions (client-side only)

### 3a. mdoc credentials in the wallet cache

The wallet's IndexedDB credential cache is SD-JWT-only today. It gains **format discrimination** and the raw
CBOR:

| Field | Purpose |
|---|---|
| `Format` | `dc+sd-jwt` \| `mso_mdoc` — discriminates the rail. |
| `DocType` | mdoc only. The matching key (as `Vct` is for SD-JWT). |
| `IssuerSignedCbor` | The raw `IssuerSigned` bytes, **stored verbatim** so the tagged item bytes survive round-tripping. |

**Constraint (inherited, load-bearing):** the cache's **evict-and-continue** rule on undecryptable rows must
be preserved. A row the cache cannot decrypt is dropped and the listing continues — it must never abort the
whole listing. (This is a real regression the codebase has already suffered once, when a cipher change made
legacy rows throw and killed sync entirely.)

### 3b. The second device key

| Key | WebCrypto usage | Bound to | Purpose |
|---|---|---|---|
| existing device key | `sign` (ECDSA P-256) | SD-JWT `cnf` | KB-JWT signing. **Unchanged.** |
| **new device key** | `deriveBits` (ECDH P-256) | mdoc `MSO.DeviceKey` | Session ECDH + `deviceMac`. |

Both are **non-extractable**. Neither is persisted as key material — only the WebCrypto handle is retained,
exactly as today. The new key's public JWK/COSE_Key is what an issuer binds a proximity-capable mdoc to.

### 3c. Presentation history

An in-person share appends to the **existing** presentation log with a channel discriminator so it is
distinguishable from an online presentation (FR-019). Disclosed claim **names** only — never values — which
is the rule the existing log already follows.

---

## 4. Both formats over one session

The session layer (engagement, key agreement, encryption, transcript) is **shared and format-agnostic**. Only
the payload inside `SessionData.Data` differs:

| Path | Request | Response payload | Holder proof |
|---|---|---|---|
| **ISO** | `DeviceRequest` / `ItemsRequest` | `DeviceResponse` (CBOR) | `deviceMac` over `DeviceAuthentication` |
| **Sorcha-native** | DCQL | `SorchaProximityEnvelope{ Format:"dc+sd-jwt", Payload:<SD-JWT VP> }` | KB-JWT whose `aud`/`nonce` bind to the **`SessionTranscript` hash** |

Binding the KB-JWT to the session transcript rather than an HTTPS `response_uri` gives the SD-JWT path
**replay protection equivalent to the mdoc path** with no server in the middle — which is what makes FR-005's
"identically for both credential kinds" true rather than aspirational.
