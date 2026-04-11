# Implementation Plan: HAIP Blueprint Integration

**Branch**: `102-haip-blueprint-integration` | **Date**: 2026-04-11 | **Spec**: [spec.md](spec.md)

## Summary

Fix the Blueprint Service response pipeline to carry HAIP credential offer and presentation request data back to the UI, create Blueprint templates for both HAIP walkthroughs, rewrite walkthrough scripts to use Blueprint instance flows, and recapture screenshots showing real workflow data.

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: Sorcha.Blueprint.Service, Sorcha.ServiceClients.Http (IHaipServiceClient), Sorcha.UI.Core  
**Storage**: In-memory (HAIP offers/requests), PostgreSQL (Blueprint instances), MongoDB (transactions)  
**Testing**: xUnit + FluentAssertions + Moq, Playwright E2E  
**Target Platform**: Docker containers, Linux  
**Project Type**: Microservices (existing codebase)  
**Constraints**: Must not break existing action execution or walkthrough flows

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes are within existing service boundaries. Blueprint Service calls HAIP Service via established IHaipServiceClient. |
| II. Security First | PASS | No new external boundaries. HAIP data flows through existing authenticated channels. |
| III. API Documentation | PASS | New response properties will have XML documentation. |
| IV. Testing Requirements | PASS | Unit tests for response mapping, E2E screenshot tests. |
| V. Code Quality | PASS | Async/await, DI, nullable types all used correctly. |
| VI. Blueprint Creation Standards | PASS | Blueprint templates are JSON files. |
| VII. Domain-Driven Design | PASS | Uses correct domain terms: Blueprint, Action, Participant. |
| VIII. Observability | PASS | Existing logging in ActionExecutionService covers HAIP paths. |

## Project Structure

### Source Code (affected files)

```text
src/Services/Sorcha.Blueprint.Service/
├── Models/Responses/
│   └── ActionSubmissionResponse.cs        # Add HAIP response properties
└── Services/Implementation/
    └── ActionExecutionService.cs          # Map HAIP results to response

walkthroughs/
├── HaipIdentityAttestation/
│   ├── blueprints/
│   │   └── identity-attestation.json      # NEW blueprint template
│   ├── setup.ps1                          # Rewrite: register + blueprint
│   └── run.ps1                            # Rewrite: instance + actions
└── HaipDrivingLicence/
    ├── blueprints/
    │   └── driving-licence.json           # Update: council participant
    ├── setup.ps1                          # Rewrite: register + blueprint
    └── run.ps1                            # Rewrite: instance + actions

tests/Sorcha.UI.E2E.Tests/Docker/
└── HaipWalkthroughScreenshotTests.cs      # Re-run for updated screenshots

docs/screenshots/haip-walkthrough/         # Updated screenshots
```

## Implementation Phases

### Phase 1: Blueprint Service Response Pipeline Fix (FR-001, FR-002, FR-003, FR-010, FR-011)

**Goal**: HAIP credential offer and presentation request data flows from ActionExecutionService through to the UI.

**Tasks**:

1.1. **Add HAIP response types to ActionSubmissionResponse**
   - File: `src/Services/Sorcha.Blueprint.Service/Models/Responses/ActionSubmissionResponse.cs`
   - Add `HaipCredentialOfferResponse` record: OfferId, CredentialOfferUri, CredentialType, IssuerName, ExpiresAt
   - Add `HaipPresentationRequestResponse` record: RequestId, PresentationRequestUri, CredentialType, RequestedClaims, ExpiresAt
   - Add nullable properties `CredentialOffer` and `PresentationRequest` to `ActionSubmissionResponse`
   - XML documentation on all new types and properties

1.2. **Map HAIP offer data in ActionExecutionService**
   - File: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
   - At line ~544: capture full `offerResult` (not just URI string)
   - At line ~704 (response builder): map `offerResult` to `response.CredentialOffer`
   - Include credential type from `actionDef.CredentialIssuanceConfig.CredentialType`
   - Include issuer name (derive from instance or org context if available, else null)

1.3. **Add HAIP presentation request creation in ActionExecutionService**
   - File: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
   - After credential verification block (line ~238): detect HAIP presentation requirements
   - If action has `credentialRequirements` with `PresentationSource.HaipExternalWallet` and no presentations submitted and `_haipClient != null`:
     - Call `_haipClient.CreatePresentationRequestAsync()` with credential type and required claims
     - Store result for response mapping
   - At response builder: map to `response.PresentationRequest`

1.4. **Unit tests for response mapping**
   - File: new test file in `tests/Sorcha.Blueprint.Service.Tests/`
   - Test that credential offer data maps correctly to response
   - Test that presentation request data maps correctly to response
   - Test that both are null for standard (non-HAIP) actions

**Verification**: `dotnet build` succeeds, unit tests pass, existing Blueprint Service tests pass.

### Phase 2: Blueprint Templates (FR-004, FR-005)

**Goal**: Blueprint JSON templates exist for both HAIP walkthroughs.

**Tasks**:

2.1. **Create Identity Attestation blueprint template**
   - File: `walkthroughs/HaipIdentityAttestation/blueprints/identity-attestation.json`
   - Single participant: `government-admin`
   - Single action (starting): "Issue Identity Credential"
   - Schema: givenName, familyName, fullName, dateOfBirth, email, address (nested object with street, locality, region, postcode, country)
   - credentialIssuance: VerifiedIdentityCredential, targetAudience: HaipExternalWallet
   - disclosable: all fields
   - Route: action completes workflow (target: null)

2.2. **Update Driving Licence blueprint template**
   - File: `walkthroughs/HaipDrivingLicence/blueprints/driving-licence.json`
   - Change action 1 participant from `applicant` to `council`
   - Ensure action 1 has credentialRequirements with presentationSource: HaipExternalWallet
   - Ensure action 2 has credentialIssuance with targetAudience: HaipExternalWallet
   - Add route from action 1 to action 2
   - Verify action 2 route terminates workflow

**Verification**: Blueprint templates are valid JSON. Manual inspection of structure.

### Phase 3: Walkthrough Script Rewrite (FR-006, FR-007, FR-008, FR-009)

**Goal**: Both HAIP walkthroughs execute through Blueprint instance flows.

**Tasks**:

3.1. **Rewrite HaipIdentityAttestation/setup.ps1**
   - Keep: org creation, user registration, wallet creation, participant setup, trust anchor provisioning, HAIP issuer enrolment
   - Add: `New-SorchaRegister` to create a register
   - Add: `Publish-SorchaBlueprint` to publish identity-attestation.json
   - Save: registerId, blueprintId to state.json

3.2. **Rewrite HaipIdentityAttestation/run.ps1**
   - Login as gov-admin (with org selection)
   - Create Blueprint instance: POST /instances/ with blueprintId and registerId
   - Execute "Issue Identity Credential" action via `Invoke-SorchaAction` with citizen persona data
   - Extract credentialOfferUri from action response
   - Run `sorcha-agent haip receive` with the offer URI
   - Verify credential stored in wallet

3.3. **Rewrite HaipDrivingLicence/setup.ps1**
   - Keep: org creation, user registration, wallet creation, participant setup, HAIP issuer enrolment
   - Add: `New-SorchaRegister` to create a register
   - Add: `Publish-SorchaBlueprint` to publish driving-licence.json
   - Save: registerId, blueprintId to state.json

3.4. **Rewrite HaipDrivingLicence/run.ps1**
   - Login as council-admin
   - Create Blueprint instance: POST /instances/
   - Execute "Verify Applicant Identity" action → extract presentationRequestUri from response
   - Run `sorcha-agent haip present` with the request URI
   - Wait for action 2 to become current (poll instance currentActionIds)
   - Execute "Issue Driving Licence" action with licence data → extract credentialOfferUri
   - Run `sorcha-agent haip receive` with the offer URI
   - Verify both credentials in wallet

**Verification**: Both walkthroughs complete end-to-end. State.json contains blueprintId, registerId, instanceId.

### Phase 4: Docker Rebuild, Screenshots, and Documentation

**Goal**: Updated screenshots showing real HAIP workflow data in the UI.

**Tasks**:

4.1. **Rebuild Docker and run walkthroughs**
   - `docker compose down -v && docker compose build --parallel && docker compose up -d`
   - Run initialize-secrets, then both HAIP walkthrough setup and run scripts

4.2. **Run screenshot tests**
   - Execute HaipWalkthroughScreenshotTests (21+ tests)
   - Verify previously-empty pages now show workflow data

4.3. **Copy screenshots and update documentation**
   - Copy to docs/screenshots/haip-walkthrough/
   - Update README.md with new captions

**Verification**: All screenshot tests pass. At least 3 previously-empty pages show real data.

## Complexity Tracking

No constitution violations. All changes use existing patterns and service boundaries.
