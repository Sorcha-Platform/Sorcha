# Tasks: HAIP Blueprint Integration

**Input**: Design documents from `/specs/102-haip-blueprint-integration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup

**Purpose**: No new project setup needed. All projects exist. This phase ensures the branch is ready.

- [x] T001 Verify solution builds cleanly with `dotnet build`
- [x] T002 Verify existing Blueprint Service tests pass with `dotnet test tests/Sorcha.Blueprint.Service.Tests/` (81 pre-existing failures, 432 passing — baseline noted)

---

## Phase 2: Foundational — Blueprint Service Response Pipeline Fix

**Purpose**: Add HAIP data to ActionSubmissionResponse and map it in ActionExecutionService. This MUST complete before walkthrough scripts can extract HAIP data from action responses.

- [x] T003 [P] Add `HaipCredentialOfferResponse` record to `src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs` with properties: OfferId (Guid), CredentialOfferUri (string), CredentialType (string), IssuerName (string?), ExpiresAt (DateTimeOffset). Add XML documentation.
- [x] T004 [P] Add `HaipPresentationRequestResponse` record to `src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs` with properties: RequestId (Guid), PresentationRequestUri (string), CredentialType (string), RequestedClaims (List<string>?), ExpiresAt (DateTimeOffset). Add XML documentation.
- [x] T005 Add nullable `CredentialOffer` (HaipCredentialOfferResponse?) and `PresentationRequest` (HaipPresentationRequestResponse?) properties to `ActionSubmissionResponse` in `src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs`. Add XML documentation.
- [x] T006 In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` around line 544: capture the full `offerResult` from `_haipClient.CreateCredentialOfferAsync()` (not just the URI string). Replace `credentialOfferUri = offerResult.CredentialOfferUri` with storing the full `CreateOfferResult` in a local variable.
- [x] T007 In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` around line 704 (response builder): map the captured `CreateOfferResult` to `response.CredentialOffer` as a new `HaipCredentialOfferResponse` with OfferId, CredentialOfferUri, CredentialType (from `actionDef.CredentialIssuanceConfig.CredentialType`), and ExpiresAt. IssuerName can be null for now.
- [x] T008 In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` after the credential verification block (around line 238): add HAIP presentation request creation. If `actionDef.CredentialRequirements` contains any requirement with `PresentationSource.HaipExternalWallet` AND `request.CredentialPresentations` is null/empty AND `_haipClient != null`, call `_haipClient.CreatePresentationRequestAsync()` with the credential type and required claim names. Store the `CreatePresentationRequestResult` for response mapping. Skip the existing presentation verification (it would throw since no presentations were submitted).
- [x] T009 In the response builder (line ~704): map the captured `CreatePresentationRequestResult` to `response.PresentationRequest` as a new `HaipPresentationRequestResponse` with RequestId, PresentationRequestUri (from AuthorizationRequestUri), CredentialType, RequestedClaims, and ExpiresAt.
- [ ] T010 Add unit test in `tests/Sorcha.Blueprint.Service.Tests/` verifying that when ActionExecutionService processes an action with `CredentialIssuanceConfig.TargetAudience == HaipExternalWallet`, the response includes a non-null `CredentialOffer` with valid OfferId, CredentialOfferUri, CredentialType, and ExpiresAt.
- [ ] T011 Add unit test verifying that when ActionExecutionService processes an action with `CredentialRequirements` containing `PresentationSource.HaipExternalWallet` and no presentations submitted, the response includes a non-null `PresentationRequest` with valid RequestId, PresentationRequestUri, CredentialType, and RequestedClaims.
- [ ] T012 Add unit test verifying that standard (non-HAIP) action execution returns null for both `CredentialOffer` and `PresentationRequest`.
- [x] T013 Verify solution builds and all existing tests pass with `dotnet build && dotnet test`

**Checkpoint**: Blueprint Service returns HAIP data in action responses. UI QR dialogs will fire when receiving this data.

---

## Phase 3: User Story 1 — Government Admin Issues Identity Credential via Blueprint (Priority: P1)

**Goal**: Government admin creates a workflow instance, executes the identity credential issuance action, and the external wallet collects the credential via QR code.

**Independent Test**: Run `pwsh walkthroughs/HaipIdentityAttestation/setup.ps1 -Force && pwsh walkthroughs/HaipIdentityAttestation/run.ps1` and verify the credential is exchanged. Check the UI shows the workflow instance in My Workflows.

### Implementation

- [x] T014 [P] [US1] Create identity attestation blueprint template at `walkthroughs/HaipIdentityAttestation/blueprints/identity-attestation.json`. Single participant `government-admin`. Single starting action "Issue Identity Credential" with schema for givenName, familyName, fullName, dateOfBirth (format: date), email (format: email), address (nested object: street, locality, region, postcode, country). credentialIssuance config: VerifiedIdentityCredential, targetAudience: HaipExternalWallet, disclosable for all fields, route to null (workflow complete).
- [x] T015 [US1] Rewrite `walkthroughs/HaipIdentityAttestation/setup.ps1` to add register and blueprint creation. After existing org/user/wallet/participant setup and trust anchor provisioning: call `New-SorchaRegister` to create a register, call `Publish-SorchaBlueprint` with the identity-attestation.json template, save registerId and blueprintId to state.json. Keep all existing setup steps (org creation, user registration, wallet creation, participant linking, trust anchor, HAIP issuer enrolment).
- [x] T016 [US1] Rewrite `walkthroughs/HaipIdentityAttestation/run.ps1` to use Blueprint instance flow. Login as gov-admin with org token. Create instance via POST `/instances/` with blueprintId and registerId. Execute the starting action via `Invoke-SorchaAction` with citizen persona data as payload. Extract `credentialOffer.credentialOfferUri` from the action response. Pass the URI to `sorcha-agent haip receive`. Verify credential stored in wallet. Save instanceId to state.json.
- [x] T017 [US1] Test: run `pwsh walkthroughs/HaipIdentityAttestation/setup.ps1 -Force` against fresh Docker stack and verify state.json contains registerId and blueprintId
- [x] T018 [US1] Test: run `pwsh walkthroughs/HaipIdentityAttestation/run.ps1` and verify credential is exchanged and state.json contains instanceId

**Checkpoint**: Identity attestation walkthrough runs end-to-end through Blueprint flows. Government admin's actions appear in the UI.

---

## Phase 4: User Story 2 — Council Admin Verifies Identity Then Issues Driving Licence (Priority: P1)

**Goal**: Council admin creates a workflow instance, verifies citizen identity via HAIP presentation request QR, then issues a driving licence via HAIP credential offer QR.

**Independent Test**: Run `pwsh walkthroughs/HaipDrivingLicence/setup.ps1 -Force && pwsh walkthroughs/HaipDrivingLicence/run.ps1` and verify both credentials are in the wallet. Check the UI shows the workflow instance.

### Implementation

- [x] T019 [P] [US2] Update driving licence blueprint at `walkthroughs/HaipDrivingLicence/blueprints/driving-licence.json`. Change action "verify-identity" participant from `applicant` to `council`. Ensure action 1 is marked `isStartingAction: true`. Ensure action 1 has credentialRequirements with presentationSource: HaipExternalWallet. Add route from action 1 to action 2 (issue-licence). Ensure action 2 has credentialIssuance with targetAudience: HaipExternalWallet and route to null.
- [x] T020 [US2] Rewrite `walkthroughs/HaipDrivingLicence/setup.ps1` to add register and blueprint creation. After existing org/user/wallet/participant setup and HAIP issuer enrolment: call `New-SorchaRegister`, call `Publish-SorchaBlueprint` with driving-licence.json, save registerId and blueprintId to state.json. Keep all existing setup steps.
- [x] T021 [US2] Rewrite `walkthroughs/HaipDrivingLicence/run.ps1` to use Blueprint instance flow. Login as council-admin. Create instance via POST `/instances/`. Execute "Verify Applicant Identity" action (action 1) via `Invoke-SorchaAction`. Extract `presentationRequest.presentationRequestUri` from response. Run `sorcha-agent haip present` with the request URI. Poll instance for action 2 to become current (check currentActionIds). Execute "Issue Driving Licence" action (action 2) with licence data. Extract `credentialOffer.credentialOfferUri` from response. Run `sorcha-agent haip receive`. Verify both credentials in wallet.
- [x] T022 [US2] Test: run `pwsh walkthroughs/HaipDrivingLicence/setup.ps1 -Force` and verify state.json contains registerId and blueprintId
- [x] T023 [US2] Test: run `pwsh walkthroughs/HaipDrivingLicence/run.ps1` and verify both credentials are exchanged

**Checkpoint**: Driving licence walkthrough runs end-to-end through Blueprint flows. Council admin's workflows and actions appear in the UI.

---

## Phase 5: User Story 3 — Screenshots with Real Data (Priority: P2)

**Goal**: Updated UI screenshots showing real HAIP workflow data for documentation.

**Independent Test**: Run HaipWalkthroughScreenshotTests and verify previously-empty pages show data.

### Implementation

- [x] T024 [US3] Rebuild Docker images with `docker compose build --parallel`
- [x] T025 [US3] Reset Docker stack: `docker compose down -v && docker compose up -d`
- [x] T026 [US3] Run both HAIP walkthrough setup and run scripts against fresh Docker
- [x] T027 [US3] Run screenshot tests: `dotnet test tests/Sorcha.UI.E2E.Tests/ --filter "TestCategory=HaipScreenshots" -- NUnit.NumberOfTestWorkers=1`
- [x] T028 [US3] Verify that at least 3 previously-empty screenshots now show real workflow data (My Workflows, Pending Actions, credentials views)
- [x] T029 [US3] Copy updated screenshots to `docs/screenshots/haip-walkthrough/` and update `docs/screenshots/haip-walkthrough/README.md` with revised captions

**Checkpoint**: All screenshots capture real HAIP workflow data for documentation.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T030 [P] Verify all existing unit tests pass: `dotnet test` (full solution)
- [ ] T031 [P] Run `dotnet format` on modified files
- [ ] T032 Commit all changes with task references and create PR

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — verify current state
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1)**: Depends on Phase 2 — can run in parallel with Phase 4
- **Phase 4 (US2)**: Depends on Phase 2 — can run in parallel with Phase 3. Also depends on US1's identity credential for the driving licence flow (citizen needs VerifiedIdentityCredential first)
- **Phase 5 (US3)**: Depends on Phases 3 and 4 (needs walkthrough data in Docker)
- **Phase 6 (Polish)**: Depends on all phases

### User Story Dependencies

- **US1 (Identity Attestation)**: Independent after Phase 2
- **US2 (Driving Licence)**: Depends on US1 at runtime (needs the VerifiedIdentityCredential in the wallet), but can be implemented in parallel
- **US3 (Screenshots)**: Depends on both US1 and US2 completing against Docker

### Within Each User Story

- Blueprint template before walkthrough scripts
- Setup script before run script
- Run script tests last

### Parallel Opportunities

- T003 and T004 (response records) can run in parallel
- T010, T011, T012 (unit tests) can run in parallel
- T014 and T019 (blueprint templates) can run in parallel
- T030 and T031 (polish tasks) can run in parallel

---

## Parallel Example: Phase 2 Foundation

```text
# Launch response record creation in parallel:
Task: T003 "Add HaipCredentialOfferResponse record"
Task: T004 "Add HaipPresentationRequestResponse record"

# Then add properties (depends on T003, T004):
Task: T005 "Add CredentialOffer and PresentationRequest to ActionSubmissionResponse"

# Then ActionExecutionService changes (depends on T005):
Task: T006 "Capture full offer result"
Task: T007 "Map offer to response"
Task: T008 "Add presentation request creation"
Task: T009 "Map presentation request to response"

# Then unit tests (depends on T006-T009):
Task: T010 "Test credential offer response mapping"
Task: T011 "Test presentation request response mapping"
Task: T012 "Test non-HAIP action returns null"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup verification
2. Complete Phase 2: Response pipeline fix (CRITICAL)
3. Complete Phase 3: Identity attestation walkthrough
4. **STOP and VALIDATE**: Run the walkthrough, check UI shows data
5. Deploy/demo if ready

### Incremental Delivery

1. Phase 2 → Response pipeline works
2. Phase 3 (US1) → Identity attestation E2E
3. Phase 4 (US2) → Driving licence E2E
4. Phase 5 (US3) → Documentation screenshots
5. Each phase adds visible value

---

## Notes

- Total tasks: 32
- Phase 2 (Foundational): 11 tasks
- US1 (Identity Attestation): 5 tasks
- US2 (Driving Licence): 5 tasks
- US3 (Screenshots): 6 tasks
- Polish: 3 tasks
- Parallel opportunities: T003/T004, T010/T011/T012, T014/T019, T030/T031
- MVP scope: Phase 1 + Phase 2 + Phase 3 (US1)
