# Feature Specification: Citizen Wallet PWA

**Feature Branch**: `114-citizen-wallet-pwa`
**Created**: 2026-04-26
**Status**: Draft
**Input**: User description: "Citizen Wallet PWA — installable wallet that holds Sorcha-issued credentials offline and presents them to verifiers via cross-device QR. Server-anchored holder identity with revocable on-device delegation. Recovery via existing platform identity. v1 scope is pure wallet (hold/view/present); persona offline, native shell, proximity transports (NFC/BLE), additional credential formats, and external verifier interop are explicitly future-tranche. Authoritative design rationale: docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md."

**Companion design doc**: [`docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md`](../../docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md) — captures architecture, cryptographic model, and roadmap. The spec below covers WHAT the citizen experiences and WHAT the system must do; HOW lives in the design doc and the upcoming plan.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Present a credential to a verifier with no network (Priority: P1)

A citizen arrives at a physical or online counter (a bar checking age, a verification analyst inspecting an identity, a clerk processing an application) and is asked to prove something using a credential they hold. The verifier shows a QR code on screen. The citizen opens their installed wallet app, scans the QR, sees exactly which attributes the verifier is asking for, approves the disclosure, and the verifier confirms acceptance — all without either party requiring a live connection to the platform during the exchange.

**Why this priority**: This is the entire reason the wallet exists. Every other story is in service of this one. If only this story shipped, the wallet would already be a useful product (provided credentials and devices were pre-loaded by some manual mechanism).

**Independent Test**: A pre-enrolled citizen device with a pre-loaded credential, plus a working verifier surface, plus both parties placed offline. Citizen scans the verifier's QR, approves disclosure, verifier confirms. No platform contact during the exchange.

**Acceptance Scenarios**:

1. **Given** the citizen has a valid credential cached on their device, **When** the verifier presents a QR code requesting attributes the citizen's credential carries, **Then** the wallet displays a consent screen showing which attributes will be shared and the citizen can approve to send a verifiable presentation that the verifier accepts.
2. **Given** the citizen and verifier are both fully offline, **When** the citizen completes the present flow, **Then** the verifier still validates the presentation cryptographically without needing to reach the platform.
3. **Given** the verifier requests one mandatory attribute and one optional attribute, **When** the consent screen is shown, **Then** the mandatory attribute is pre-selected and locked, the optional attribute is unselected by default, and the citizen can choose whether to include the optional attribute before approving.
4. **Given** the citizen does not hold a credential matching the verifier's request, **When** the QR is scanned, **Then** the wallet explains that no eligible credential is available and offers a way to cancel the exchange cleanly.
5. **Given** the verifier's request has an expired or invalid signature, **When** the QR is scanned, **Then** the wallet refuses to proceed and explains why.

---

### User Story 2 — Enrol a new device and load credentials onto it (Priority: P2)

A citizen who has previously been issued credentials through Sorcha installs the wallet app on a phone or laptop for the first time. They sign in with their existing platform account (using whichever sign-in method they normally use — email and password, social, or passkey). The wallet sets the device up as an authorised holder for their identity and pulls down all the credentials they currently hold so they are immediately available offline.

**Why this priority**: Without enrolment, no citizen can use the wallet at all. P2 because it is gating but only happens once per device, where presentation (P1) happens many times.

**Independent Test**: A citizen account already exists in the platform with at least one issued credential. Citizen opens the wallet for the first time, signs in, completes the enrolment flow, and can immediately see their credentials in the wallet list.

**Acceptance Scenarios**:

1. **Given** a citizen with an existing platform account and at least one issued credential, **When** they install the wallet, sign in, and confirm device enrolment, **Then** their credentials appear in the wallet within a minute and are usable offline thereafter.
2. **Given** the citizen completes enrolment, **When** the device is later disconnected from the network, **Then** all enrolled credentials remain available for viewing and presentation.
3. **Given** the citizen abandons enrolment partway, **When** they reopen the wallet, **Then** they can resume from where they left off without re-doing completed steps.
4. **Given** a citizen successfully enrols a device, **When** they sign in to the platform on any other surface (the existing web UI), **Then** the new device appears in their device list with a recognisable label and enrolment timestamp.

---

### User Story 3 — Recover after losing a device (Priority: P2)

A citizen loses their phone (or has it stolen, or wipes it). On any other device — a laptop, a friend's phone, the platform's main web UI — they sign in to their account, see the lost device in their device list, and revoke it. From that moment forward, no presentation made by the lost device is accepted by verifiers (within a known refresh window). The citizen then enrols a new device and immediately has all their credentials back, without needing to re-apply for any of them and without needing to remember any recovery phrase.

**Why this priority**: This is the *trust* story. If citizens don't believe they will be protected when their device is lost or stolen, they will not adopt the wallet, no matter how good the presentation experience is. P2 — same as enrolment because the two are joined: enrolment must be safe, and "safe" includes a believable recovery path.

**Independent Test**: A citizen with an enrolled device + cached credentials. They sign in on a second device, revoke the first, and confirm the first device's presentations are subsequently rejected by the verifier. Then they enrol a third device and confirm credentials are available without any re-issuance.

**Acceptance Scenarios**:

1. **Given** a citizen has an active enrolled device, **When** they sign in elsewhere and revoke the device, **Then** the device's authority to make presentations is removed and the revocation propagates to verifiers within the documented refresh window.
2. **Given** a citizen has revoked a lost device, **When** they enrol a new device, **Then** all credentials they previously held are available on the new device with no re-issuance required.
3. **Given** the citizen has never seen or stored any recovery phrase, **When** they go through recovery, **Then** the only secret they need is the platform login they already use elsewhere.
4. **Given** a verifier accepted a presentation from a device shortly before that device was revoked, **When** the verifier later refreshes its revocation information, **Then** the historical acceptance stands but any future presentation from that device fails.

---

### User Story 4 — Receive a newly-issued credential automatically (Priority: P3)

A citizen has the wallet installed and has previously enrolled. They go through one of the existing application flows in the main Sorcha web UI and are issued a new credential. The next time they open the wallet (or while the wallet is open and they have network), the new credential appears in their wallet without any explicit action required.

**Why this priority**: Without this, citizens have to manually pull every new credential, which is a poor experience. But the wallet still works (less smoothly) without it, so it is a polish-tier requirement rather than gating.

**Independent Test**: A pre-enrolled citizen who completes an issuance flow in the existing Sorcha web UI. The new credential appears in the wallet automatically on next open (or sooner if the wallet is already open).

**Acceptance Scenarios**:

1. **Given** an enrolled citizen receives a newly-issued credential, **When** they next open the wallet with network available, **Then** the new credential appears without any action on their part.
2. **Given** an enrolled citizen has the wallet open when a new credential is issued, **When** the citizen next looks at the wallet within a short delay, **Then** the new credential appears without requiring an explicit refresh.
3. **Given** a credential the citizen previously held is revoked or replaced upstream, **When** the wallet next syncs, **Then** the wallet's view reflects the revocation or replacement.

---

### User Story 5 — Review what was presented, when, to whom (Priority: P3)

A citizen wants to know the history of how their credentials have been used. The wallet shows a chronological list of every presentation: which credential was used, which attributes were disclosed, who the verifier identified themselves as, and the date/time. This works whether or not the citizen had network at the time of the presentation.

**Why this priority**: Audit and transparency build trust over time. P3 because it does not block the wallet being functional, but it is essential for citizen sovereignty and for resolving disputes ("did I really share my date of birth with that bar?").

**Independent Test**: A pre-enrolled citizen makes a small number of presentations both online and offline, then opens the activity view and confirms each entry shows the correct credential, attributes disclosed, verifier label, and timestamp.

**Acceptance Scenarios**:

1. **Given** a citizen has made one or more presentations, **When** they open the activity view, **Then** each presentation appears with credential reference, disclosed attributes, verifier label as supplied in the request, and timestamp.
2. **Given** a presentation was made offline, **When** the device later regains network, **Then** the presentation entry is reported back to the platform so the citizen sees a unified history across the wallet and the main Sorcha UI.
3. **Given** a citizen wants to delete a local activity entry, **When** they request deletion, **Then** the local copy is removed but the entry already reported to the platform remains in the platform-side history (the wallet does not promise to erase platform-side records).

---

### Edge Cases

- **Lost device while offline**: Citizen cannot reach the platform to revoke the device. Acceptable risk — verifiers continue accepting until their next status refresh; the design accepts a bounded window of vulnerability rather than requiring online revocation in every case.
- **Compromised device with intact lock screen**: Wallet must not leak credentials to an attacker who picks up an idle device with the wallet open in the background. Wallet locks itself after a short visibility timeout and requires re-unlock to read credential contents.
- **Stale revocation status at the verifier**: A verifier's cached revocation list is older than the most recent revocation event. The verifier may temporarily accept a presentation from a revoked device. This is bounded by the known refresh interval; documented as an inherent property of any offline-capable trust system.
- **Device clock skew**: If the device's clock is significantly wrong, presentations may appear to be expired or not yet valid. Wallet shows an explicit "your wallet thinks the time is wrong" message rather than silently failing or silently presenting an expired credential.
- **Verifier QR is malformed, expired, or replayed**: Wallet refuses to proceed and shows a citizen-readable explanation. Citizen can ask the verifier to regenerate.
- **No matching credential**: Citizen has no credential satisfying the verifier's request. Wallet says so plainly and offers a clean cancel path; does not appear to start a flow that cannot complete.
- **Multiple matching credentials**: Citizen holds more than one credential that could satisfy the request. Wallet lets the citizen choose which to use.
- **Storage near full**: Wallet warns the citizen but does not silently evict credentials. Citizen explicitly chooses what to remove.
- **Network appearing or disappearing mid-flow**: Presentation flow continues to work regardless. Sync operations defer until network returns.
- **Citizen revokes their only enrolled device while travelling**: Wallet can be reinstalled and re-enrolled on a borrowed device using only the citizen's platform login; no other secrets needed.
- **Credential issuer revokes a credential the citizen has cached**: Wallet learns of the revocation on next sync and reflects the new status. Past presentations of the credential remain visible in the activity log.
- **Large number of credentials**: Wallet remains responsive with at least dozens of credentials cached. (Hard upper limits are an implementation concern.)
- **Multiple devices for one citizen**: A citizen may enrol the wallet on more than one device. Each device is independently authorised and independently revocable.
- **Family member borrowing the device**: Out of scope — wallet assumes one device per citizen. Borrowing scenarios are not supported in v1.

## Requirements *(mandatory)*

### Functional Requirements

#### Wallet identity and enrolment

- **FR-001**: System MUST allow a citizen to install the wallet from a web URL on any modern browser-equipped device (mobile and desktop) and to add it to the device's home screen / app launcher.
- **FR-002**: Citizens MUST be able to authenticate to the wallet using their existing platform account, using any of the platform's supported sign-in methods (email and password, social login, passkey).
- **FR-003**: System MUST NOT show, generate, or require the citizen to record a recovery phrase, seed phrase, or any other secret beyond their platform login credentials.
- **FR-004**: System MUST treat each wallet installation on a separate device as a distinct enrolled device, with a citizen-visible label and enrolment timestamp.
- **FR-005**: System MUST allow a citizen to enrol the wallet on more than one device under the same platform account.
- **FR-006**: System MUST authorise a newly-enrolled device to act on behalf of the citizen for credential presentation, without granting the device the ability to present after revocation.

#### Credential receipt and storage

- **FR-007**: System MUST automatically pull all credentials the citizen currently holds onto a freshly enrolled device.
- **FR-008**: System MUST keep cached credentials available for viewing and presentation when the device has no network connectivity.
- **FR-009**: System MUST automatically receive newly-issued credentials onto enrolled devices that have network connectivity, without requiring an explicit refresh action by the citizen.
- **FR-010**: System MUST reflect upstream revocations and replacements of credentials in the wallet's view on the next sync.
- **FR-011**: System MUST encrypt credential contents at rest on the device such that a forensic dump of device storage does not reveal credential attributes without the device-bound key.
- **FR-012**: System MUST make cached credentials inaccessible after a short period of background inactivity, requiring the citizen to bring the wallet back to the foreground (and, where applicable, unlock it) before contents can be read again.

#### Presentation to verifiers

- **FR-013**: System MUST allow a citizen to scan a verifier's QR code and immediately see a citizen-readable summary of which attributes the verifier is requesting and which (if any) are optional.
- **FR-014**: System MUST default optional disclosed attributes to "off" so the citizen consciously opts in to sharing more than the verifier requires.
- **FR-015**: System MUST require an explicit citizen action (a deliberate confirmation, not a single tap that could be misclicked) before any attributes are released to a verifier.
- **FR-016**: System MUST be able to construct and deliver a verifiable presentation to a verifier without any contact with the platform during the exchange.
- **FR-017**: Verifiers MUST be able to validate a citizen's presentation cryptographically without contacting the platform during the exchange, given that they have refreshed their revocation status within the documented window.
- **FR-018**: System MUST cleanly handle the case where the citizen holds no credential satisfying the verifier's request, by telling the citizen so and offering a cancel action.
- **FR-019**: System MUST cleanly handle the case where the citizen holds multiple credentials satisfying the verifier's request, by letting the citizen choose which to use.
- **FR-020**: System MUST refuse to present an expired credential without first warning the citizen, and MUST refuse outright if the device's clock appears wrong enough that an expiry decision cannot be trusted.
- **FR-021**: System MUST reject a malformed, replayed, or signature-invalid verifier request and explain in citizen-readable terms why.

#### Recovery, revocation, and device management

- **FR-022**: Citizens MUST be able to view a list of all devices currently enrolled under their platform account, including device label, enrolment timestamp, and last-known activity timestamp.
- **FR-023**: Citizens MUST be able to revoke any one of their enrolled devices from any other authenticated surface (another device's wallet or the existing main Sorcha web UI).
- **FR-024**: System MUST propagate device revocation to verifiers' cached revocation status within a documented refresh window (default 24 hours, configurable downward by the platform operator).
- **FR-025**: System MUST allow a citizen who has lost a device to enrol a new device and recover access to all credentials they previously held, without re-issuance from the original issuer of any credential.
- **FR-026**: System MUST automatically renew the on-device authorisation grant when the device is online and the existing grant is approaching expiry, without requiring citizen interaction.
- **FR-027**: System MUST never accept a presentation from a device whose authorisation grant has expired and not been renewed.

#### Activity log and citizen visibility

- **FR-028**: System MUST record every presentation made by the device locally, including credential reference, disclosed attributes, the label the verifier supplied, and timestamp, regardless of whether the presentation was online or offline.
- **FR-029**: System MUST allow the citizen to browse the local presentation history.
- **FR-030**: System MUST report local presentation history entries back to the platform when the device next has network, so the citizen has a unified history across surfaces.
- **FR-031**: System MUST allow the citizen to delete local activity entries, with clear messaging that platform-side records are unaffected.

#### Lifecycle and platform integration

- **FR-032**: System MUST integrate offline presentations into the platform's existing presentation lifecycle, writing the standard initiated/outcome lifecycle records once the platform learns of an offline presentation, with the offline timestamps preserved.
- **FR-033**: System MUST distinguish, in the platform's lifecycle records, between presentations whose outcome was independently confirmed by a verifier reporting back to the platform and presentations that the wallet alone reported (where the platform cannot independently corroborate the verifier's acceptance).
- **FR-034**: System MUST allow the platform operator to configure a maximum age beyond which an offline presentation reported back to the platform is no longer treated as a fresh lifecycle event (default 600 seconds).
- **FR-035**: System MUST NOT modify, hide, or replace any existing flow in the main Sorcha web UI. Citizens continue to apply for credentials there exactly as they do today.

#### Reference verifier (required for v1 demoability)

- **FR-036**: System MUST provide a verifier-side reference application that any platform tenant can deep-link to in order to request specific attributes from a citizen.
- **FR-037**: The reference verifier MUST be capable of displaying the request as a QR code, accepting the citizen's response, validating the cryptographic chain, and reporting the outcome back to the platform.
- **FR-038**: The reference verifier MUST be capable of operating with stale revocation status (i.e., when the verifier itself was offline at the moment of presentation), within the documented refresh window.

### Key Entities

- **Wallet Installation**: A copy of the citizen wallet running on one specific device, paired one-to-one with a single platform account at a single point in time.
- **Holder Identity**: A citizen's stable identity used for credential binding, persistent across device losses; recoverable through the citizen's normal platform login.
- **Device Enrolment**: A record that links a wallet installation to a holder identity, including a label, enrolment timestamp, and current authorisation status (active or revoked).
- **Cached Credential**: A copy of an issued credential held locally on a wallet installation, encrypted at rest, presented to verifiers without platform contact.
- **Presentation Request**: A verifier's signed ask for specific attributes, conveyed to the wallet via QR scan; carries a verifier identifier, the requested attributes (with mandatory/optional flags), an audience, and a freshness nonce.
- **Verifiable Presentation**: The signed package the wallet returns to the verifier, containing only the disclosed attributes plus the cryptographic chain proving the citizen's authority to present them.
- **Presentation Log Entry**: A record of one presentation event, held locally on the wallet and (when sync occurs) reported to the platform.
- **Revocation Status Record**: A platform-published, regularly refreshed list of which credentials and which device enrolments are no longer valid, used by verifiers to make accept/reject decisions offline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A pre-enrolled citizen with a cached credential can complete an offline presentation to a verifier (from QR scan to verifier confirmation) in under 30 seconds, including the consent step.
- **SC-002**: A citizen new to the wallet can install it, sign in, enrol the device, and see their existing credentials available offline in under 5 minutes from first opening the install URL.
- **SC-003**: A citizen who has lost their device can revoke it and have a replacement device fully usable in under 10 minutes from signing in on any other surface.
- **SC-004**: Cached credentials remain available for viewing and presentation for at least 30 consecutive days of zero network connectivity, provided the on-device authorisation grant has not expired in that window.
- **SC-005**: 95% of offline presentations attempted between an enrolled wallet and the reference verifier succeed on first attempt, given a credential satisfying the request and current device authorisation.
- **SC-006**: 100% of presentations from a revoked device are rejected by a verifier whose revocation status is no older than the documented refresh interval (default 24 hours).
- **SC-007**: 0% of citizens are required to record, store, or recall any wallet-specific recovery secret at any point in any flow. The only secret a citizen needs is the one they already use to log in to the platform.
- **SC-008**: 100% of platform lifecycle records for offline presentations carry the original offline timestamp (not the platform's catch-up timestamp), so audit traces reflect when the citizen actually presented.
- **SC-009**: Wallet installs are usable on at least the latest two major versions of every browser engine that supports the relevant web standards (Chromium, WebKit, Gecko) on both mobile and desktop.
- **SC-010**: Existing flows in the main Sorcha web UI complete with identical behaviour after the wallet ships, with no regression in any existing application or issuance path.

## Assumptions

The following defaults were chosen during specification rather than left as open questions, in order to keep the spec actionable. Plan-phase may revisit any of these.

- **Recovery model**: The citizen's existing platform login is the sole recovery anchor. There is no separate wallet-specific recovery secret. Justification: explicit user direction during brainstorming ("we should be as protective as we can and assume the user wont work correctly").
- **Authorisation grant lifetime**: Each enrolled device's authorisation grant defaults to 12 months, silently renewed when the device is online and within 30 days of expiry. Justification: matches industry norms for similar wallet products and balances re-enrolment friction against the security benefit of bounded grants.
- **Revocation propagation window**: Verifiers refresh revocation status at least every 24 hours by default. Acceptable trade-off vs requiring online revocation checks for every presentation, which would defeat the offline goal.
- **Late-presentation freshness window**: Offline presentations reported back to the platform more than 600 seconds after the fact are not treated as lifecycle-fresh. Configurable.
- **Disclosure default**: Optional attributes default to OFF; mandatory attributes are pre-selected and locked. The wallet always shows the citizen exactly what will be shared before they confirm.
- **Per-citizen device count**: No fixed cap on number of devices per citizen in v1. Future revision may add a soft limit (e.g. 10) with an explicit citizen-visible cap.
- **Activity log retention**: Local activity log is retained until the citizen removes entries or uninstalls the wallet. Platform-side activity log retention follows existing platform policies for the existing presentation lifecycle.
- **Verifier surface for v1**: The reference verifier is the only verifier surface guaranteed to interoperate with the wallet in v1. External verifier interop is a future-tranche concern.
- **Offline issuance**: Out of scope. Citizens must be online to receive a new credential into the wallet. Once received, all subsequent use can be offline.
- **Multi-citizen-per-device**: Out of scope. One wallet installation maps to one citizen account.

## Dependencies

- **Existing platform identity (Feature 112)**: PlatformUser sign-in is the recovery anchor and the sole authentication primitive used by the wallet. Wallet enrolment requires a working platform login.
- **Existing presentation lifecycle (Feature 111)**: The wallet's offline presentations integrate as a new consumer of this lifecycle. No changes to the lifecycle architecture are made; only an additional consumer is added.
- **Existing credential issuance pipeline**: Credentials issued through any current Sorcha flow (Feature 103 Verified Citizen, Feature 107 Assured Identity, future credential types) flow into the wallet automatically once the citizen is enrolled.
- **Existing wallet-side cryptography (Feature 083 Org Key Derivation, Feature 086 Validator Roster derivation patterns)**: The holder identity uses the same derivation infrastructure the platform already operates, with a new derivation context dedicated to citizen holders.
- **Existing main Sorcha web UI**: Continues to be the surface where citizens apply for and receive credentials. The wallet is purely additive.

## Out of Scope (v1)

- Persona offline integration (Feature 092 attribute autofill while the wallet is the active surface) — planned for a later phase of the same tranche.
- A native mobile app (App Store / Play Store distribution with platform-bound key storage in Secure Enclave / Keystore) — planned for a later phase of the same tranche.
- Proximity transports (NFC tap-to-present, Bluetooth Low Energy device engagement) — planned for the second tranche.
- Additional credential formats beyond the platform's existing SD-JWT verifiable credential profile — planned for the second tranche.
- External (non-Sorcha-hosted) verifier interop beyond what falls out of standard cross-device flow conformance — broader interop planned for later phase of first tranche.
- Citizen application or issuance flows running inside the wallet itself — these stay in the existing main Sorcha web UI.
- Family/guardian/shared-device flows.
- Issuer-side wallet operations.
