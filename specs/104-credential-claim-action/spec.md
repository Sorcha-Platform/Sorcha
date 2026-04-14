# Feature Specification: Credential Claim Action (Feature 103 Wave 14)

**Feature Branch**: `104-credential-claim-action`
**Created**: 2026-04-14
**Status**: Draft
**Input**: Wave 14 of Feature 103 (Verified Citizen v2). Delivers HAIP credential offers to the citizen (recipient) instead of the assessor (action sender) by introducing a blueprint engine payload carry-forward primitive and a credential claim action renderer. Full design at `docs/superpowers/specs/2026-04-14-wave-14-credential-claim-action-design.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Citizen claims a verified-citizen credential from their action queue (Priority: P1)

A citizen submits an application for a verified citizen credential through the Sorcha UI. A government assessor reviews the application and approves it. Instead of the credential offer appearing in the assessor's browser session, the citizen sees a new pending item in their My Actions queue titled "Claim your Verified Citizen credential." When they open it, a credential claim card displays the credential type, issuer name, purpose, and expiry. The citizen clicks "Claim credential," the credential is received into their Sorcha wallet, appears in their My Credentials page, and the action queue item is marked complete. The register shows a full audit trail: application submitted, approved, claimed.

**Why this priority**: This is the whole point of the feature. Without it, wave 13's local-receive flow drops credentials into the wrong wallet (the assessor's) and the cryptographic binding is wrong. P1 because nothing else in wave 14 matters if the citizen cannot claim their own credential.

**Independent Test**: Run the HaipVerifiedCitizen walkthrough end-to-end. Submit application as citizen, approve as assessor, confirm a pending claim action appears in the citizen's My Actions (not the assessor's), click Claim, confirm the credential is in the citizen's My Credentials list, confirm the assessor's My Credentials does not contain the issued credential, confirm the register shows three sealed actions for the instance.

**Acceptance Scenarios**:

1. **Given** the Verified Citizen v2 blueprint published with the claim action, a citizen who has submitted an application, and an assessor who has just approved it, **When** the citizen opens their My Actions queue, **Then** a new pending action labelled as a credential claim appears with the credential type, issuer name, description, and expiry visible.
2. **Given** the citizen is viewing the credential claim card, **When** they click "Claim credential," **Then** the credential is stored in their Sorcha wallet, a success confirmation is shown, they are navigated to My Credentials where the new credential is visible, and the claim action is marked complete on the register.
3. **Given** the assessor has approved the application and a claim action is pending for the citizen, **When** the assessor views their own My Credentials page, **Then** the Verified Citizen credential is not present.
4. **Given** a claim action is pending, **When** the citizen declines it instead of claiming, **Then** the pending action is removed from their queue, the credential is not stored in any wallet, and the decline is recorded on the register.

---

### User Story 2 - Citizen loads the credential into an external HAIP wallet via QR (Priority: P2)

A citizen who prefers to use an external HAIP-compatible wallet (for example, an EUDI-compliant mobile wallet) opens the same credential claim card. Alongside the "Claim credential" button, they tap "Scan with external wallet," which reveals a QR code. Their external wallet scans the QR, runs the standard HAIP flow independently, and lands the credential in that wallet. The Sorcha UI detects the external claim and transitions the pending action to complete without storing the credential in the Sorcha wallet.

**Why this priority**: The external wallet path is important for interoperability and for users who want their credentials in their preferred wallet, but it is not the critical path for the core feature. The primary flow (user story 1) must work first. P2 because the internal claim path must be rock-solid before the external path matters.

**Independent Test**: Open the credential claim card, tap "Scan with external wallet," verify a QR code is displayed, scan it with a HAIP-compatible wallet simulator, confirm the external wallet successfully issues the credential (observable through the simulator), confirm the Sorcha UI transitions the pending action to complete after the external wallet reports success, confirm the Sorcha citizen wallet does not contain the credential.

**Acceptance Scenarios**:

1. **Given** the credential claim card is open, **When** the citizen taps "Scan with external wallet," **Then** a QR code encoding the credential offer URI is displayed.
2. **Given** the QR code is displayed and an external HAIP wallet has scanned it and completed the credential exchange, **When** the Sorcha UI detects the completion, **Then** the pending claim action is marked complete on the register and the citizen is shown a success confirmation.

---

### User Story 3 - Citizen retries a claim that failed due to a transient error (Priority: P2)

A citizen opens their credential claim card and clicks "Claim credential." The local wallet attempts to exchange the pre-authorized code with the issuer, but the network connection drops mid-exchange. The UI shows a clear error message, the action remains pending in their queue, and the Claim button re-enables. A few moments later, the citizen clicks Claim again; this time the exchange succeeds, the credential lands in their wallet, and the action completes normally.

**Why this priority**: Transient failures are common in real-world use (network blips, service bounces). If a single failure permanently invalidates a credential offer, users will be frustrated and support will suffer. P2 because the feature is still useful without retry (the citizen could start a new application), but substantially worse.

**Independent Test**: Simulate a network failure during the first claim attempt (for example, by blocking the issuer endpoint temporarily), confirm an error is shown and the action stays pending, unblock the endpoint, retry the claim, confirm it succeeds.

**Acceptance Scenarios**:

1. **Given** the citizen has clicked "Claim credential" and the issuer call fails with a transient error, **When** the error response is received, **Then** an error message is shown, the pending action remains in the queue, and the Claim button is enabled for another attempt.
2. **Given** a claim previously failed and the action is still pending, **When** the citizen clicks "Claim credential" again and the issuer is now reachable, **Then** the credential is successfully stored and the action is marked complete.

---

### User Story 4 - Citizen's credential offer expires before they claim it (Priority: P3)

A citizen submits an application, the assessor approves it, a claim action is created with an expiry timestamp (for example, 24 hours later), but the citizen does not open their Sorcha UI within that window. When they eventually return, the pending action shows the credential offer as expired; the Claim button is disabled and the card clearly explains the offer is no longer valid. The citizen can start a new application from scratch.

**Why this priority**: Expired offers are a rare edge case but must be handled gracefully; if the UI silently breaks or shows a misleading "still available" state, citizens will attempt failed claims and see confusing errors. P3 because it is a safety net, not a primary user goal.

**Independent Test**: Configure a short offer expiry (for example, 5 minutes), run the assessor approval, wait past the expiry without claiming, confirm the claim card shows expired state with Claim disabled, confirm the action is eventually marked failed.

**Acceptance Scenarios**:

1. **Given** a pending claim action whose offer expiry has passed, **When** the citizen opens the action, **Then** the credential claim card shows an expired state with the Claim button disabled and an explanation of why the offer is no longer valid.
2. **Given** an expired claim action, **When** the expiry is reached (whether the citizen is viewing it or not), **Then** the action transitions to a failed state on the register and no further claim attempts are possible on that action.

---

### User Story 5 - Blueprint author declares a credential claim action using the engine primitive (Priority: P2)

A blueprint author designs a new credential-issuing workflow. They add a third action to their blueprint whose schema contains a credential-offer object field marked with the claim extension. They declare on the approving action's route an output mapping that carries the minted credential offer data from the approving action's execution result into the claim action's prepopulated payload. When the workflow runs, the engine automatically seeds the claim action with the offer data without any custom code in the blueprint.

**Why this priority**: The engine primitive (wave 14a) is the foundation that user stories 1-4 sit on. It is also the first in a category of primitives (action-to-action data carry-forward) that future blueprints will reuse. P2 because blueprint authors are a secondary audience compared to citizens, but the primitive must be correct for the citizen-facing stories to work.

**Independent Test**: Write a minimal two-action blueprint with an output mapping, publish it, execute it, confirm that the second action's pending state contains the carried-forward data sourced from the first action's result, and confirm that the second action's renderer has access to the seeded payload at render time.

**Acceptance Scenarios**:

1. **Given** a published blueprint with a route declaring an output mapping from one action's result to another action's payload, **When** the source action is executed successfully, **Then** the next action's pending state contains the mapped data before any sender submits to it.
2. **Given** a pending action has prepopulated payload data, **When** the recipient opens it, **Then** the renderer has access to the prepopulated data for display.
3. **Given** a pending action has prepopulated payload data, **When** the recipient submits the action with their own confirmation data, **Then** both the prepopulated data and the submitted data are recorded in the sealed action result with submitted data taking precedence on any field conflicts.

---

### Edge Cases

- **The citizen closes the tab mid-claim.** The offer may already have been consumed by the issuer but the local wallet write or the action completion may not have happened. On their next session, the credential should be discoverable and the pending action should resolve to complete without double-claiming.
- **The citizen claims successfully locally but the action sealing fails.** The credential is already in the local wallet; the UI treats this as a success state and retries the action seal in the background. Worst case the action remains pending with the credential already claimed; ops recovery is acceptable for v1.
- **The assessor's approval transaction is sealed but the engine fails to write the pending claim action payload.** This should be detected at routing time, not after the fact. The assessor's approval should not be considered complete until the downstream claim action is fully seeded.
- **Two pending claim actions exist for the same citizen from different blueprints.** Each must be independent; claiming or declining one must not affect the other.
- **The blueprint author forgets to declare the output mapping.** The claim action renders with empty payload; the UI shows a clear "nothing to claim here" state rather than a broken form. Blueprint validation should warn about this configuration during publish.
- **The blueprint author maps source fields that are not present in the execution result.** The mapping silently skips absent source fields rather than failing the execution.
- **A non-Sorcha user receives an out-of-band offer link.** Out of scope for this feature — the offer assumes the citizen has a Sorcha wallet because they started the blueprint from Sorcha.
- **Multiple pending claim actions are visible in the My Actions queue at once.** They should be visually distinguishable from normal form-entry actions so the citizen understands these are "something to accept" rather than "something to fill in."

## Requirements *(mandatory)*

### Functional Requirements

**Engine primitive (Wave 14a)**

- **FR-001**: The blueprint routing system MUST support declaring, on a route definition, an optional mapping from paths in the current action's execution result to paths in the next action's initial payload.
- **FR-002**: When a route with such a mapping is evaluated during action execution, the system MUST resolve each source path against the execution result and write the resolved value to the corresponding target path in the next action's prepopulated payload state.
- **FR-003**: The system MUST persist prepopulated payload data per pending action so that the data survives page reloads, session loss, and service restarts.
- **FR-004**: When a source path referenced by the mapping is absent in the execution result, the system MUST skip that entry silently and continue evaluating remaining entries, rather than failing the execution.
- **FR-005**: The set of source data available to the mapping MUST include the submitted action payload, any calculated values produced by the engine's calculate step, and, when present, the HAIP credential offer output produced during the action's execution.
- **FR-006**: When a recipient opens a pending action that has prepopulated payload data, the system MUST make that data available to the action's renderer for display purposes.
- **FR-007**: When a recipient submits a pending action that has prepopulated payload data, the system MUST merge the prepopulated data with the submitted data such that the submitted data takes precedence on any field-level conflict, and the merged result MUST be what is sealed to the register.
- **FR-008**: When a pending action is completed, cancelled, or expired, its prepopulated payload data MUST be removed from the persisted pending state atomically with the action resolution.
- **FR-009**: Blueprints that do not use the output mapping feature MUST continue to execute unchanged, with no prepopulated payload data on their pending actions and no change to existing response shapes.

**Credential claim feature (Wave 14b)**

- **FR-010**: The system MUST allow a blueprint author to mark a payload field on a pending action's schema as a credential offer, such that the field's value will be rendered as a credential claim card rather than as a generic form input.
- **FR-011**: The credential claim card MUST display, from the payload data, the credential type, the credential purpose or description, the issuer name, and the offer expiry.
- **FR-012**: The credential claim card MUST offer the citizen a primary "Claim credential" action that stores the credential in their Sorcha wallet using the wave 13 local-receive mechanism.
- **FR-013**: The credential claim card MUST offer the citizen a secondary "Decline" action that cancels the pending action without consuming the credential offer.
- **FR-014**: The credential claim card MUST offer the citizen an alternative path to load the credential into an external HAIP-compatible wallet by displaying a QR code encoding the credential offer URI.
- **FR-015**: When the citizen successfully claims a credential locally, the system MUST automatically complete the pending action with a confirmation payload containing the claim timestamp and seal the completion to the register.
- **FR-016**: When the citizen's local claim attempt fails with a transient error (for example, network failure, issuer unavailable, transient server error), the system MUST leave the pending action in its pending state and enable the citizen to retry without requiring a new application.
- **FR-017**: When the credential offer's expiry has passed, the system MUST render the claim card in an expired state with the Claim action disabled and MUST mark the pending action as failed without consuming the offer.
- **FR-018**: When the citizen chooses the external wallet path and the external wallet successfully claims the credential, the system MUST detect the completion through the existing HAIP offer status mechanism and mark the pending action as complete.
- **FR-019**: The credential claim card MUST take the recipient wallet address from the authenticated session context and MUST NOT accept a wallet address from the payload, to prevent a malicious blueprint author from redirecting the credential to an unintended wallet.
- **FR-020**: The credential issued through this flow MUST be cryptographically bound to the recipient wallet's key. The action sender's key MUST NOT be used to bind the credential at any point in the flow.
- **FR-021**: The Verified Citizen v2 blueprint MUST be updated to include a third action using this mechanism, and the HAIP Driving Licence blueprint MUST receive the equivalent update.
- **FR-022**: The payload shape used for credential offers MUST align with the OpenID4VCI specification for the protocol-level offer data and MUST align with DIF Credential Manifest conventions for the display metadata, so that the same payload shape is interpretable by standards-conformant tooling outside Sorcha.

**Audit and integrity**

- **FR-023**: Every successful credential claim MUST appear on the register as a sealed action transaction attributable to the recipient's wallet.
- **FR-024**: Every declined claim MUST appear on the register as a recorded outcome so that the audit trail for the instance is complete.
- **FR-025**: Blueprints using this mechanism MUST produce a valid instance audit trail showing the application, approval, and claim actions in order.

### Key Entities *(include if feature involves data)*

- **Output Mapping**: A declaration on a route indicating which pieces of data from the current action's execution result should be carried forward into the next action's prepopulated payload. Owned by blueprint authors at design time, evaluated by the engine at execution time.
- **Prepopulated Action Payload**: Data attached to a pending action before its recipient sees it, seeded by a previous action's output mapping. Persisted on the blueprint instance, removed when the action resolves.
- **Credential Offer Payload**: The standards-aligned data structure carried as a prepopulated payload on a claim action, containing the credential offer URI, display metadata (title, subtitle, description, issuer name, issuer logo), and expiry. This is what the credential claim card renders.
- **Credential Claim Action**: A blueprint action whose payload schema includes a credential offer field marked for rendering as a claim card. Sender-locked to a participant that is guaranteed to represent the credential recipient (typically the starting action's open participant, already late-bound to the citizen's wallet).
- **Claim Confirmation**: The minimal payload submitted when a citizen successfully claims a credential, containing the claim timestamp. Written to the register as the sealed completion of the claim action.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the Verified Citizen v2 flow, the issued credential is present in the recipient citizen's wallet and absent from the issuing assessor's wallet, in 100% of successful flows.
- **SC-002**: A citizen whose first claim attempt fails due to a transient issuer error can complete the claim on a subsequent attempt within the same pending action, without starting a new application, in 95% or more of transient-failure scenarios.
- **SC-003**: The citizen can go from "assessor approved my application" to "credential is in my wallet" in under 60 seconds on a stable connection, measured from the moment the claim action appears in their queue to the moment the credential is visible in My Credentials.
- **SC-004**: The instance audit trail on the register shows a complete, ordered record of application → approval → claim for every successful flow, with 100% of claims attributable to the recipient's wallet (never the sender's).
- **SC-005**: Every pending claim action displays the credential type, issuer, description, and expiry to the recipient, with no missing metadata, in 100% of test runs where the blueprint author has populated the display data.
- **SC-006**: Expired claim actions are never successfully claimed; any attempt to claim past the expiry produces a clear user-facing message and transitions the action to failed in 100% of test runs.
- **SC-007**: External HAIP wallets can successfully claim the credential via the QR path with zero changes to their own implementation, proving that the payload and offer URI are standards-compliant.
- **SC-008**: The HaipVerifiedCitizen and HaipDrivingLicence walkthroughs pass end-to-end against both local Docker and n1.sorcha.dev after wave 14 ships, with no regressions in wave 13 or earlier functionality.
- **SC-009**: Blueprints published before wave 14 continue to execute identically, with zero observable behaviour change, confirming that the engine primitive is purely additive.
- **SC-010**: A blueprint author can add a credential claim action to a new blueprint using only the output mapping declaration and the credential-offer schema extension, without writing any custom code.

## Assumptions

- The citizen recipient is always a Sorcha user with a wallet at the moment the credential is issued, because the blueprint was started from the Sorcha UI. Out-of-band credential delivery to non-users is out of scope.
- Wave 13's local credential receive service is the correct mechanism for client-side credential receive and is production-ready. Wave 14 reuses it without modification.
- The existing participant late-binding behaviour (the open starting action binds a wallet to the applicant participant, and subsequent actions owned by the same participant inherit that wallet) is correct and will continue to work for the third action in a three-action blueprint.
- The pre-authorized code in a HAIP credential offer is acceptable to encrypt at rest using the same mechanism that existing action payload data uses. It is not a long-lived secret, but treating it like one is safer than the alternative.
- The offer expiry timestamp is populated by the HAIP issuer and is accurate. The UI trusts it without cross-validating against a server clock.
- Blueprint authors are trusted to configure their output mapping correctly; malformed mappings produce empty payloads (with a validation warning at publish time) rather than security failures.
- "Decline" on a credential claim is an intentional choice by the citizen and does not need to produce a retry path. If they later change their mind, they start a new application.

## Dependencies

- Wave 13 of Feature 103 merged (local credential receive service, credential offer QR card component, HAIP issuer Ed25519 verification support, local credential store write path).
- Existing HAIP infrastructure: HAIP service minting pre-authorized offers, wallet service signing endpoints, credential store on the wallet service.
- Existing participant late-binding: open starting action binds the citizen's wallet to the applicant participant; a subsequent action owned by the same participant inherits that wallet.
- Existing blueprint engine phases: validate, calculate, route, disclose — the output mapping primitive extends the routing phase without replacing any existing mechanism.

## Out of Scope

- Server-side claim provisioning (where the blueprint service mints and stores the credential on behalf of the citizen). Deferred; can be added later without breaking the client-side path.
- Expression-based or logic-based output mappings. Version one is pure path-to-path mapping.
- Retrofitting existing blueprints to use the output mapping primitive. It is additive; existing blueprints keep working unchanged.
- External-wallet claim telemetry beyond the existing HAIP offer status polling. We know the offer was exchanged, not who exchanged it or on which device.
- Real-time push notifications when a new pending claim action appears. Useful but cross-cutting, not specific to claim actions.
- Non-Sorcha citizen recipients. The flow assumes a Sorcha wallet exists.
- Renaming or changing wave 13's direct QR dialog flow for blueprints that do not use the claim action pattern. They keep working exactly as today.
- Credential revocation workflows (already covered by the trust hardening feature).
- Credential update or re-issuance flows. If a credential expires or needs refreshing, that is a new application.
