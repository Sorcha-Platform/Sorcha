# Feature Specification: Mobile proximity credential sharing (ISO 18013-5 over BLE)

**Feature Branch**: `185-mobile-proximity-sharing`

**Created**: 2026-07-13

**Status**: Draft

**Input**: User description: "Mobile proximity credential sharing — ISO 18013-5 mdoc device retrieval over BLE, for the Sorcha Wallet PWA (holder) and a new native reader app. Holder takes the BLE peripheral-server role; both mdoc and SD-JWT VC travel over one shared ISO session layer; COSE_Mac0 implemented on both sides; the mdoc tree is extracted from Sorcha.Cryptography into a new pure-managed project so the Blazor WASM holder can use it; the reader is a second Capacitor target wrapping a new WASM verifier host; a loopback transport proves the whole protocol with no phones; UI integration in BOTH apps is a first-class requirement; evidence bar is our own two devices."

**Design of record**: `docs/superpowers/specs/2026-07-13-mobile-proximity-sharing-design.md` — the architecture, the rejected alternatives, and the crypto finding about device keys are settled there and are not re-litigated here.

---

## Overview

A citizen holding a Sorcha credential can today only present it **online** — every presentation puts a
server in the middle. This feature lets a citizen present a credential **in person and offline**: their
phone and a verifier's device exchange the presentation directly over Bluetooth, with no network.

It delivers both halves of that exchange — the **holder** (the existing Sorcha Wallet app) and a **reader**
(a new app a verifier holds) — because without a reader there is no way to demonstrate, exercise or test
the capability at all.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prove the exchange works end to end, without phones (Priority: P1)

An engineer runs the test suite and sees a complete proximity presentation — engagement, secure session,
request, selective disclosure, response, device authentication, and a verification verdict — carried out
between a holder and a reader in a single process, with no Bluetooth and no mobile devices involved.

**Why this priority**: This is the only story that de-risks the feature. The offline exchange protocol is
the large, exacting, silently-failing part of the work (a single byte wrong in what gets hashed and nothing
verifies, with no useful error). Proving it in ordinary tests means the mobile work that follows is
responsible only for moving bytes. It is also the only story that can be built and validated with no mobile
toolchain, and it is a prerequisite for every other story.

**Independent Test**: Run the test suite on a developer machine or in CI. A holder and a reader complete a
full presentation over an in-process transport; the reader returns a verdict. No phone, no Bluetooth, no
network.

**Acceptance Scenarios**:

1. **Given** a holder with a credential and a reader requesting specific data elements, **When** the exchange runs over the in-process transport, **Then** the reader receives exactly the requested elements, no more, and returns an accepting verdict.
2. **Given** the same exchange, **When** the response is altered in transit by a single byte, **Then** the reader rejects it.
3. **Given** a completed exchange, **When** the same response is replayed into a fresh session, **Then** the reader rejects it.
4. **Given** published reference test data for the standard, **When** the system produces the values that get signed and hashed, **Then** they match the reference byte for byte.

---

### User Story 2 - A citizen shares a credential in person (Priority: P2)

A citizen is asked to prove something at a counter, a door, or a roadside. They open their wallet, choose
"Share in person", and hold up their phone. The verifier's device reads it. The citizen sees exactly what
is being asked for — including whether the verifier intends to **keep** it — approves, and the exchange
completes. It works with no signal.

**Why this priority**: This is the feature's reason for existing, and the first story that a person can
actually use. It depends on Story 1 and on the transport being real.

**Independent Test**: On a real phone, with the device in airplane mode, complete a share to a reader and
confirm the credential data arrives and the presentation is recorded in the citizen's history.

**Acceptance Scenarios**:

1. **Given** a citizen with a credential and no network connectivity, **When** they share it in person with a reader, **Then** the exchange completes successfully.
2. **Given** a verifier asking for several data elements, **When** the citizen reaches the approval step, **Then** they see each element asked for, and which of them the verifier intends to retain, before approving.
3. **Given** the citizen declines at the approval step, **When** the exchange ends, **Then** no credential data has left the device.
4. **Given** a completed in-person share, **When** the citizen later opens their activity history, **Then** the share is listed and is distinguishable from an online presentation.
5. **Given** a device without Bluetooth capability, **When** the citizen opens the wallet, **Then** the in-person sharing option is not offered (rather than offered and broken).

---

### User Story 3 - A verifier reads a credential in person (Priority: P2)

A verifier opens the reader app, chooses what they need to know, and reads the citizen's phone. They get a
plain verdict, and can expand it to see how it was reached — including an honest statement of what could
**not** be checked because they were offline.

**Why this priority**: Equal partner to Story 2 — neither is usable alone, and together they are the
demonstrable feature. Separated from Story 2 because it is a distinct application with a distinct user.

**Independent Test**: On a real device, read a credential from a holder and receive a verdict with its
supporting detail.

**Acceptance Scenarios**:

1. **Given** a verifier who needs to check one fact, **When** they read a citizen's credential, **Then** they receive a clear accept/reject verdict.
2. **Given** an accepted credential, **When** the verifier expands the verdict, **Then** they see which checks passed, and see that the ledger-anchoring check could not be performed offline — stated as *not checked*, not as *passed* and not as *failed*.
3. **Given** a credential whose issuer signature does not verify, **When** it is read, **Then** the verdict is a rejection and the reason is shown.
4. **Given** a credential the citizen chose to disclose only part of, **When** the verifier views the result, **Then** they see the disclosed values and can see that other values were withheld.

---

### User Story 4 - Both credential kinds travel in person (Priority: P3)

A citizen can share in person whether the credential they hold is the international standard kind (as a
driving licence or EU wallet credential would be) or Sorcha's own native kind. The verifier reads either.

**Why this priority**: Sorcha's native credentials are the ones citizens actually hold today; the standard
kind is what makes the feature interoperable in principle. Shipping only one would either make the feature
useless to current holders or dead-end it against the wider ecosystem. It is P3 only because it layers onto
the exchange proven in Story 1 rather than changing it.

**Independent Test**: Run the in-person exchange twice — once with each credential kind — and confirm both
verify.

**Acceptance Scenarios**:

1. **Given** a citizen holding a standard-format credential, **When** they share it in person, **Then** the reader verifies it.
2. **Given** a citizen holding a Sorcha-native credential, **When** they share it in person, **Then** the reader verifies it.
3. **Given** either kind, **When** the exchange is replayed or tampered with, **Then** it is rejected — the two kinds are equally protected against replay.

---

### Edge Cases

- **The citizen walks away mid-exchange** (connection drops after the request but before approval): the session ends, nothing is disclosed, and both sides return to a clean state. Neither app is left stuck.
- **A second reader tries to connect** while a session is in progress: it is refused. One session at a time.
- **The verifier asks for something the citizen does not hold**: the citizen is told plainly what was asked for and that they cannot satisfy it, rather than being shown an empty approval screen.
- **The verifier asks for more than they need**: the citizen sees every element asked for, and may decline entirely.
- **Bluetooth is switched off, or permission is refused**: the citizen is told what is needed and why, and can proceed once it is granted. Refusal is not a dead end and not a crash.
- **The credential is revoked, but the reader is offline** and cannot fetch fresh revocation data: the reader uses the most recent revocation data it holds, and says how old it is. It does not silently treat unknown as good.
- **The credential has expired**: rejected.
- **The two devices are too far apart, or the connection is unstable**: it fails with a clear "could not connect" state, distinguishable from "connected but the credential was rejected".
- **The phone's screen locks mid-exchange**: the session is abandoned safely rather than continuing invisibly.

---

## Requirements *(mandatory)*

### Functional Requirements

**The offline exchange**

- **FR-001**: The system MUST allow a holder and a verifier's device to complete a credential presentation with **no network connectivity on either device** and no server participating.
- **FR-002**: The exchange MUST be initiated by the verifier reading an engagement code displayed by the holder, establishing a direct connection between the two devices.
- **FR-003**: All data exchanged between the two devices MUST be encrypted such that a third party observing the radio traffic learns no credential content.
- **FR-004**: The devices MUST NOT need to be paired or bonded to each other, and MUST NOT retain a pairing after the exchange.
- **FR-005**: A presentation captured from one exchange MUST NOT be accepted in any other exchange (replay resistance), and this MUST hold identically for both credential kinds.
- **FR-006**: Any alteration to the presentation in transit MUST cause the verifier to reject it.
- **FR-007**: The holder MUST cryptographically prove possession of the credential's bound device key as part of the exchange, and the verifier MUST check that proof.

**What the holder discloses**

- **FR-008**: The holder MUST disclose **only** the data elements the verifier requested and the citizen approved — never the whole credential.
- **FR-009**: Before anything is disclosed, the citizen MUST be shown every data element being asked for, and MUST be shown which of those the verifier intends to **retain**.
- **FR-010**: If the citizen declines, **no** credential data MUST leave the device.
- **FR-011**: The holder MUST be able to present credentials of **both** the international standard format and Sorcha's native format over the same in-person exchange.

**What the verifier learns**

- **FR-012**: The verifier MUST receive a clear accept/reject verdict.
- **FR-013**: The verifier MUST be able to see the checks behind the verdict, including issuer authenticity, revocation status, and whether the credential's ledger anchoring was confirmed.
- **FR-014**: Checks that could not be performed (because the reader is offline) MUST be reported as **not checked** — distinct from both *passed* and *failed* — and MUST NOT by themselves cause a rejection.
- **FR-015**: Checks that **failed** MUST cause a rejection.
- **FR-016**: The verifier MUST be able to see which values were disclosed and that others were withheld.

**Using it (both apps)**

- **FR-017**: A citizen MUST be able to start an in-person share from the wallet's normal surfaces (credential view and the present surface) — not from a hidden or developer-only route.
- **FR-018**: A verifier MUST be able to state what they need to know and start a read from the reader app's primary surface.
- **FR-019**: Completed in-person shares MUST appear in the citizen's presentation history, distinguishable from online presentations.
- **FR-020**: Where a device cannot support in-person sharing (no Bluetooth, or running in a plain browser), the capability MUST be hidden rather than offered and broken.
- **FR-021**: Where permission is required and not yet granted, the citizen MUST be told what is needed and be able to grant it and continue.

**Provability**

- **FR-022**: The complete exchange MUST be executable in automated tests **without Bluetooth and without physical devices**, so that the protocol is provable in CI.
- **FR-023**: The values that the system signs and hashes MUST be asserted byte-for-byte against published reference data for the standard, not only against the system's own output.

### Key Entities

- **Engagement code**: What the holder displays and the verifier reads to start an exchange. Carries enough to establish a secure connection and nothing about the citizen.
- **Secure session**: The short-lived, encrypted, mutually-established channel between the two devices. Exists only for one exchange.
- **Data request**: What the verifier asks for — a set of named data elements, each flagged with whether the verifier intends to retain it.
- **Presentation**: What the holder returns — the approved subset of credential data, plus proof it came from this holder's device and belongs to this session.
- **Verdict**: What the verifier ends up with — accept or reject, plus the supporting checks, each of which passed, failed, or could not be checked.
- **Device binding key**: The key on the citizen's phone that a credential is bound to and that proves possession during an exchange. (The design of record establishes that the in-person standard-format path requires a *second* such key, distinct from the one used online.)

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A citizen can complete an in-person share, from opening the wallet to the verifier seeing a verdict, in **under 30 seconds**, with **both devices offline**.
- **SC-002**: The complete exchange — engagement, secure session, request, selective disclosure, response, device proof, verdict — runs in automated tests with **no Bluetooth and no physical devices**, and runs in CI on every change.
- **SC-003**: The values the system signs and hashes match **published reference data for the standard, byte for byte**.
- **SC-004**: A tampered presentation, a replayed presentation, an expired credential, and a revoked credential are each **rejected** — verified by automated test, with no false accepts.
- **SC-005**: A presentation discloses **only** the elements the citizen approved — verified by inspecting what crosses the wire, not only what the verifier chooses to display.
- **SC-006**: A citizen can reach in-person sharing from the wallet's ordinary surfaces **without instruction**, and a verifier can complete a read from the reader app's first screen.
- **SC-007**: An offline verifier's verdict states plainly which checks it could not perform. In a review of the verdict screen, **no check that was skipped is presented as having passed**.
- **SC-008**: The exchange succeeds on **both** an iPhone and an Android device, in **both** roles, over real Bluetooth.
- **SC-009**: Both credential kinds — international standard and Sorcha-native — complete an in-person exchange and verify.
- **SC-010**: The existing **online** presentation path is unaffected: all pre-existing presentation tests continue to pass.

---

## Assumptions

- **The evidence bar is self-consistency, not certified interop.** Success is our holder and our reader agreeing, on our own two devices. This does **not** establish that a certified third-party reader would accept us, and the feature must not claim that it does. The byte-for-byte reference-data check (SC-003) is the mitigation that catches errors two agreeing implementations would otherwise hide.
- **In-person presentation is always foreground** — the citizen is physically holding the phone up. Background or unattended presentation is out of scope, which removes the platform restriction that would otherwise bite on one of the two mobile platforms.
- **The verifier's device is a phone**, running an app we ship. Reading from fixed terminals or third-party readers is out of scope.
- **One exchange at a time per device.** Concurrent sessions are not supported and are refused.
- The citizen has already obtained a credential through the existing (online) issuance flow. This feature adds no issuance capability.
- The reader may hold **stale** revocation data when offline. It uses what it has and discloses the age; it does not treat unknown as good.
- The design of record (`docs/superpowers/specs/2026-07-13-mobile-proximity-sharing-design.md`) is authoritative for **how** this is built — including the mdoc project extraction, the two-device-key model, and the thin-native-transport cut. Those decisions are settled and are inputs to this spec, not open questions.

---

## Out of Scope

- Wi-Fi Aware, and NFC as the **data** channel (NFC as an **engagement** mechanism is in scope).
- Formal ISO 18013-5 conformance testing and certified-reader interoperability.
- Background or unattended presentation.
- Any change to the online presentation path.
- Issuance changes beyond binding credentials to the device key the in-person path requires.
