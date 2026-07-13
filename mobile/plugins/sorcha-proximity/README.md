# sorcha-proximity

BLE transport for ISO 18013-5 proximity credential presentation (Feature 185). **The first native code in
this repository.**

## The one rule

**This plugin moves opaque bytes and knows nothing else.** No CBOR, no mdoc, no credentials, no session
state.

Every byte of ISO 18013-5 protocol lives in C# (`Sorcha.Mdoc`), written **once** and shared by the holder and
the reader. That is not an aesthetic preference — it is what allows `LoopbackProximityTransport` to stand in
for the radio so the **entire exchange is proven in CI with no Bluetooth and no phones**.

Protocol logic in here would be protocol logic the C# test suite cannot reach: implemented twice, in two
languages, with two chances to get the tag-24 rules wrong — and those get wrong *silently*.

**If you find yourself wanting to parse something in this plugin, you are in the wrong file.**

## Surface

Five methods, two events. Bytes cross as **base64 strings** (matching `SorchaWebCrypto` / `SorchaIndexedDb`).

| Method | Role | Purpose |
|---|---|---|
| `probe()` | — | What can this device do? **Never rejects.** |
| `startPeripheral({serviceUuid})` | holder | Advertise; accept one reader. |
| `connectCentral({serviceUuid})` | reader | Scan; connect. |
| `send({dataBase64})` | both | One whole message. |
| `stop()` | both | Idempotent teardown. |

Events: `received({dataBase64})` — one **whole** message, reassembled. `disconnected({reason})` — terminal.

## GATT (ISO 18013-5, mdoc peripheral server mode)

Base `-a123-48ce-896b-4c76973373e6`:

| Characteristic | UUID | Direction |
|---|---|---|
| State | `00000001…` | reader writes start/end |
| Client2Server | `00000002…` | reader → holder (write) |
| Server2Client | `00000003…` | holder → reader (notify) |

**The service UUID is not fixed.** It is random per session and arrives in the `DeviceEngagement`. A stable
UUID would let a passive observer correlate a citizen's presentations across time and place — exactly the
tracking property proximity presentation exists to avoid.

**Pairing-free.** 18013-5 forbids bonding, and a bond would leave a durable trace of the encounter on both
devices.

## Things that bite

**Chunking.** One byte of every packet is a continuation flag (`0x01` more / `0x00` last), so the payload
budget is `MTU - 1`. On Android the ATT header eats a further 3 bytes, so the usable MTU is `negotiated - 3`.
Assume the 23-byte default until `onMtuChanged` says otherwise; assuming larger silently truncates.

**Android drops queued notifications/writes.** Exactly one may be outstanding. Firing chunks in a loop
corrupts any message bigger than one packet — *intermittently*, which is the worst kind of bug. Both Kotlin
classes send one chunk and wait for `onNotificationSent` / `onCharacteristicWrite`.

**The CCC descriptor (`0x2902`) is mandatory.** Without it the central cannot subscribe and notifications
silently never arrive. Classic Android BLE omission.

**`neverForLocation` on `BLUETOOTH_SCAN`.** Without it Android treats a BLE scan as a location request and
demands `ACCESS_FINE_LOCATION` — meaning we would be asking a citizen for their location in order to show a
credential to the person standing in front of them. We do not derive location from these scans.

**iOS restricts the peripheral role in the background.** Not a constraint here: a proximity presentation is
always foreground — the citizen is holding the phone up.

## Testing

The chunker is the **only** logic in this plugin, and it has native tests on both platforms
(`ChunkingTests.swift`, `ChunkingTest.kt`) that assert the same properties — because a holder on one platform
will meet a reader on the other.

Everything else is proven in C# over `LoopbackProximityTransport`. What the radio owns is: a whole message
in, the same whole message out, across an MTU boundary.

```bash
npm run verify:ios       # xcodebuild
npm run verify:android   # gradlew test
```
