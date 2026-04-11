# Feature Specification: HAIP Blueprint Integration

**Feature Branch**: `102-haip-blueprint-integration`  
**Created**: 2026-04-11  
**Status**: Draft  
**Input**: Fix the Blueprint Service response pipeline to carry HAIP credential offer and presentation request data back to the UI, create Blueprint templates for both HAIP walkthroughs, rewrite walkthrough scripts to go through Blueprint instance flows, and recapture screenshots showing real data in the UI.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Government Admin Issues Identity Credential via Blueprint (Priority: P1)

A government admin logs into the Sorcha UI, creates a new workflow instance from the "HAIP Identity Attestation" blueprint, fills in the citizen's personal details (name, date of birth, address), and submits the action. The system creates a HAIP credential offer and displays a QR code dialog. The citizen scans the QR code with their external HAIP wallet to collect the VerifiedIdentityCredential. The QR dialog updates from "Waiting for wallet to scan..." to "Credential Collected" with a green checkmark.

**Why this priority**: This is the foundational HAIP flow. Without the response pipeline fix, no HAIP data reaches the UI and neither QR component ever renders. This story validates the end-to-end pipeline from Blueprint action execution through HAIP offer creation to QR display.

**Independent Test**: Execute the identity attestation walkthrough through Blueprint flows. Verify the action appears in Pending Actions, the QR dialog shows after submission, and the credential is exchanged via the external wallet agent.

**Acceptance Scenarios**:

1. **Given** a published identity attestation blueprint and active register, **When** the government admin creates an instance and executes the "Issue Identity Credential" action, **Then** the Blueprint Service response includes the credential offer URI, offer ID, credential type, and expiry.
2. **Given** the action execution returns a credential offer, **When** the UI receives the response, **Then** the CredentialOfferQrDialog opens with a scannable QR code, issuer name, credential type, and expiry.
3. **Given** the QR dialog is displayed with a pending offer, **When** the external wallet exchanges the pre-authorized code for a credential, **Then** the dialog polls the offer status and transitions to "Credential Collected" with a green confirmation.
4. **Given** the workflow instance has been created and the action executed, **When** the government admin views "My Workflows", **Then** the identity attestation instance is listed.

---

### User Story 2 - Council Admin Verifies Identity Then Issues Driving Licence (Priority: P1)

A council admin creates a new workflow instance from the "Driving Licence Application" blueprint. The first action ("Verify Applicant Identity") creates a presentation request QR code. The citizen scans the QR with their external HAIP wallet and presents their VerifiedIdentityCredential, disclosing only givenName, familyName, and dateOfBirth. Once verified, the workflow routes to the second action ("Issue Driving Licence"). The council admin fills in licence details and submits. A credential offer QR appears. The citizen scans it to collect the DrivingLicenceCredential.

**Why this priority**: This is the most complex HAIP flow, combining both presentation verification (OID4VP) and credential issuance (OID4VCI) in a single Blueprint workflow. It validates the full round-trip and routing between actions.

**Independent Test**: Execute the driving licence walkthrough through Blueprint flows. Verify the presentation request QR appears for action 1, credential presentation succeeds, workflow routes to action 2, and the credential offer QR appears for the licence issuance.

**Acceptance Scenarios**:

1. **Given** a published driving licence blueprint, **When** the council admin creates an instance and executes "Verify Applicant Identity", **Then** the response includes a presentation request URI, request ID, required credential type, and requested claims.
2. **Given** the PresentationRequestQrDialog is displayed, **When** the external wallet submits a VP token via direct_post, **Then** the dialog transitions through "Verifying..." to "Credential Verified" with disclosed claims summary.
3. **Given** identity verification succeeded, **When** the council admin views Pending Actions, **Then** "Issue Driving Licence" appears as the next action.
4. **Given** the council admin executes "Issue Driving Licence" with licence details, **When** the action completes, **Then** the CredentialOfferQrDialog appears for the DrivingLicenceCredential.

---

### User Story 3 - Walkthrough Data Visible in UI Screenshots (Priority: P2)

After both HAIP walkthroughs run through Blueprint flows, the UI pages show real data: Pending Actions lists actions awaiting execution, My Workflows shows active/completed instances, My Credentials shows issued credentials (for internal Sorcha wallets), and the admin views show organisation and wallet details. Screenshot tests capture all views for documentation.

**Why this priority**: Documentation screenshots with real data demonstrate the platform's HAIP capabilities. This depends on Stories 1 and 2 being complete.

**Independent Test**: Run the extended HaipWalkthroughScreenshotTests (21+ tests). Verify that previously-empty pages now show real workflow data.

**Acceptance Scenarios**:

1. **Given** both HAIP walkthroughs have completed via Blueprint flows, **When** the government admin views "My Workflows", **Then** at least one identity attestation instance is listed.
2. **Given** both walkthroughs have completed, **When** the council admin views "Pending Actions", **Then** any remaining pending actions from the driving licence workflow are visible (or completed actions show in workflow history).
3. **Given** screenshots are captured, **When** saved to docs/screenshots/haip-walkthrough/, **Then** the README.md includes captioned tables for all screenshots.

---

### Edge Cases

- What happens when the HAIP Service is unavailable during action execution? The Blueprint Service should return a clear error rather than silently discarding the HAIP interaction.
- What happens when a credential offer expires before the wallet scans? The QR dialog should show "This offer has expired" and the action can be retried.
- What happens when the external wallet denies a presentation request? The PresentationRequestQrCard should show the denial reason and the workflow should not proceed to the next action.
- What happens when the Blueprint action's credentialIssuance config references an unknown credential type? The HAIP Service should return a clear error that propagates to the user.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Blueprint Service action execution response MUST include HAIP credential offer data (offer ID, credential offer URI, credential type, issuer name, expiry) when the action has a credentialIssuance with targetAudience HaipExternalWallet.
- **FR-002**: Blueprint Service action execution response MUST include HAIP presentation request data (request ID, presentation request URI, credential type, requested claims, expiry) when the action has credentialRequirements with presentationSource HaipExternalWallet.
- **FR-003**: The response property names MUST match the UI's ActionSubmissionResultViewModel structure for direct JSON deserialization (credentialOffer, presentationRequest).
- **FR-004**: An identity attestation Blueprint template MUST exist with a single starting action that issues a VerifiedIdentityCredential to HaipExternalWallet with disclosable fields for givenName, familyName, fullName, dateOfBirth, email, and address.
- **FR-005**: The driving licence Blueprint template MUST have two actions: (1) a council-initiated "Verify Applicant Identity" action requiring a VerifiedIdentityCredential via HaipExternalWallet presentation, and (2) a council-initiated "Issue Driving Licence" action issuing a DrivingLicenceCredential to HaipExternalWallet.
- **FR-006**: The HaipIdentityAttestation walkthrough MUST create a register, publish the blueprint, create an instance, and execute the action through the Blueprint Service (not via direct HAIP API calls).
- **FR-007**: The HaipDrivingLicence walkthrough MUST create a register, publish the blueprint, create an instance, and execute both actions through the Blueprint Service.
- **FR-008**: Walkthrough scripts MUST use org admin users for setup (register creation, blueprint publication) and participant users for action execution.
- **FR-009**: Walkthrough scripts MUST use sorcha-agent to simulate external HAIP wallet interactions (haip receive for credential offers, haip present for presentation requests).
- **FR-010**: The ActionExecutionService MUST capture the full CreateOfferResult from IHaipServiceClient (not just the URI string) and map it to the response.
- **FR-011**: The ActionExecutionService MUST capture the full CreatePresentationRequestResult from IHaipServiceClient and map it to the response when processing presentation requirements with HaipExternalWallet source.

### Key Entities

- **ActionSubmissionResponse**: Blueprint Service response model that currently lacks HAIP properties. Needs CredentialOffer and PresentationRequest fields matching the UI's expected structure.
- **Blueprint Template (Identity Attestation)**: Single-action blueprint with one participant (government-admin). Action issues VerifiedIdentityCredential to external HAIP wallet.
- **Blueprint Template (Driving Licence)**: Two-action blueprint with two participants (council, applicant). Action 1 verifies identity via HAIP presentation. Action 2 issues DrivingLicenceCredential via HAIP offer.
- **Walkthrough State (state.json)**: Extended to include blueprintId, registerId, and instanceId alongside existing user/wallet/org data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After executing a Blueprint action that issues a credential to HaipExternalWallet, the UI displays the CredentialOfferQrDialog with a valid QR code within 2 seconds of submission.
- **SC-002**: After executing a Blueprint action that requires a credential presentation from HaipExternalWallet, the UI displays the PresentationRequestQrDialog with a valid QR code within 2 seconds of submission.
- **SC-003**: Both HAIP walkthroughs complete end-to-end through Blueprint instance flows (no direct HAIP API calls) and produce exchanged credentials in the external wallet.
- **SC-004**: After walkthroughs complete, the UI "My Workflows" page shows at least one workflow instance for each logged-in admin user.
- **SC-005**: All 21+ screenshot tests pass and at least 3 previously-empty pages (Pending Actions, My Workflows, or credentials views) now show real walkthrough data.
- **SC-006**: Existing unit tests in the Blueprint Service continue to pass after the ActionSubmissionResponse changes.

## Assumptions

- The HAIP Service's in-memory storage is sufficient for walkthrough purposes; persistent HAIP offer/request storage is out of scope.
- The sorcha-agent CLI already supports `haip receive` and `haip present` commands for simulating external wallet interactions.
- The SorchaWalkthrough PowerShell module provides `New-SorchaRegister` and `Publish-SorchaBlueprint` helper functions (used by ConstructionPermit and SelfBuildHouse walkthroughs).
- The existing QR card Blazor components (CredentialOfferQrCard, PresentationRequestQrCard) and their dialog wrappers are complete and tested.
- Blueprint action routing (determining the next action after completion) already works for multi-action blueprints.
- The credential presentation verification path in ActionExecutionService (lines 218-238) already works for internal Sorcha presentations; only the HAIP external wallet path needs the response data surfaced.
