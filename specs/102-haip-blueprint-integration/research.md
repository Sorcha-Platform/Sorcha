# Research: HAIP Blueprint Integration

## Decision 1: ActionSubmissionResponse HAIP Property Names

**Decision**: Add `credentialOffer` and `presentationRequest` properties to `ActionSubmissionResponse` using nested record types that match the UI's `HaipCredentialOfferInfo` and `HaipPresentationRequestInfo` JSON shapes exactly.

**Rationale**: The UI's `WorkflowService` deserializes the Blueprint Service response directly into `ActionSubmissionResultViewModel` via `ReadFromJsonAsync` (no explicit mapping). JSON property names must match for `CredentialOffer` and `PresentationRequest` to deserialize correctly. Using camelCase JSON naming policy (already configured in the Blueprint Service) means the C# property `CredentialOffer` serializes as `credentialOffer`, matching the UI's expectation.

**Alternatives considered**:
- Adding a mapping layer in the UI's WorkflowService: rejected because it adds unnecessary complexity and the direct deserialization pattern is already established.
- Returning only the URI string: rejected because the UI needs the full offer/request data (ID, type, expiry) for the QR dialog.

## Decision 2: HAIP Presentation Request Creation in ActionExecutionService

**Decision**: When an action has `credentialRequirements` with `PresentationSource.HaipExternalWallet` and no credential presentations are submitted, create a presentation request via `IHaipServiceClient.CreatePresentationRequestAsync()` and return the QR data in the response. The action completes immediately and routes to the next action.

**Rationale**: This matches the credential issuance pattern (which also completes the action immediately and returns QR data). The verification happens asynchronously — the QR dialog polls for the result. The next action in the workflow can gate on the verification result if needed, but for the walkthrough the verification is completed by the sorcha-agent before the next action is executed.

**Alternatives considered**:
- Blocking action execution until verification completes: rejected because it requires long-polling or WebSocket coordination, and the QR display needs to happen before verification.
- Two-phase execution (action stays pending until verification): more architecturally correct but significantly more complex. Deferred to future work.

## Decision 3: Driving Licence Blueprint Participant Model

**Decision**: The "Verify Applicant Identity" action is a council action (participant: `council`), not an applicant action. The council creates the presentation request by executing the action, the citizen scans the QR from their external wallet.

**Rationale**: In the real-world flow, it's the verifier (council) who initiates the identity check, not the citizen. The citizen responds passively by scanning the QR code with their wallet. This matches the OID4VP same-device/cross-device flow.

**Alternatives considered**:
- Applicant-initiated: rejected because it inverts the verifier/holder relationship and would require the citizen to have a Sorcha account with action execution capability.

## Decision 4: Walkthrough Script Architecture

**Decision**: Walkthrough scripts follow the ConstructionPermit pattern: setup creates org/users/wallets/register/blueprint, run creates instances and executes actions via `Invoke-SorchaAction`. For HAIP interactions, the script extracts the QR URI from the action response and passes it to `sorcha-agent haip receive/present`.

**Rationale**: Reuses existing walkthrough infrastructure (SorchaWalkthrough module, `Invoke-SorchaAction`, `Publish-SorchaBlueprint`). The `sorcha-agent` already supports the HAIP wallet simulation commands.

**Alternatives considered**:
- Custom HTTP calls instead of Invoke-SorchaAction: rejected because it duplicates the module's auth/error handling logic.

## Decision 5: Identity Attestation Blueprint Structure

**Decision**: Single-action blueprint with one participant (`government-admin`). The action schema includes all citizen identity fields. The credential issuance config uses `targetAudience: HaipExternalWallet` with `VerifiedIdentityCredential` type. Uses the existing blueprint `sender` field (not `participant`) to match the Action model's property name.

**Rationale**: Simplest possible HAIP issuance blueprint. The government admin fills in citizen details and submits — there is no citizen-side confirmation step.

**Alternatives considered**:
- Two-action with citizen consent: rejected per user decision — single action is sufficient for the identity attestation use case.
