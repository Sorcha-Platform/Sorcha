# Implementation Plan: Register Subscriptions & Private Register Invitations

**Branch**: `feature/register-subscriptions` | **Date**: 2026-03-23 | **Spec**: [design doc](../../docs/superpowers/specs/2026-03-23-register-subscriptions-design.md)
**Input**: Feature specification from `/specs/master/spec.md`
**Issue**: #113 (UX-001)

## Summary

Organisations subscribe to registers to scope what users see. Phase 1 adds org-level subscription management (CRUD endpoints, UI scoping, org wallet provisioning, System Register name fix). Phase 2 adds private register invitations using signed/encrypted tokens formalised as a governance blueprint on the System Register, with org DIDs (`did:sorcha:org:<address>`) and X25519 key agreement for encryption.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Entity Framework Core, Fido2NetLib, Sorcha.Cryptography, Sorcha.ServiceClients, MudBlazor
**Storage**: PostgreSQL (Tenant Service — subscriptions, nonces), MongoDB (Register Service — register metadata)
**Testing**: xUnit + FluentAssertions + Moq (unit), WebApplicationFactory (integration)
**Target Platform**: Linux containers (Docker), Azure Container Apps
**Project Type**: Microservices (Tenant Service, Register Service, Peer Service, API Gateway, Blazor WASM UI)
**Performance Goals**: Subscription CRUD < 100ms p95, Invitation create/accept < 500ms p95
**Constraints**: No new inter-service dependencies (Register→Tenant). Org wallet provisioning must be resilient to Wallet Service unavailability.
**Scale/Scope**: ~100 orgs, ~50 registers, ~500 subscriptions initially

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new cross-service dependencies. Auto-subscribe orchestrated at gateway level. |
| II. Security First | PASS | X25519 encryption, ED25519 signing, nonce replay protection, rate limiting. Input validation on all endpoints. |
| III. API Documentation | PASS | All endpoints will have XML docs, OpenAPI via Scalar, `.WithSummary()` and `.WithDescription()`. |
| IV. Testing Requirements | PASS | Unit tests for models/services, integration tests for endpoints, E2E for UI flows. Target >85%. |
| V. Code Quality | PASS | Async/await, DI, nullable enabled, no warnings. |
| VI. Blueprint Standards | PASS | Join Private Register blueprint as JSON. Published to System Register. |
| VII. Domain-Driven Design | PASS | Uses Sorcha ubiquitous language: Register, Blueprint, Participant, Disclosure. |
| VIII. Observability | PASS | Structured logging for subscription events, invitation lifecycle. Health checks unaffected. |
| Service Communication | EXCEPTION | Uses REST via ServiceClients (not gRPC) — consistent with existing codebase pragmatic pattern. Documented. |

## Project Structure

### Documentation (this feature)

```text
specs/master/
├── plan.md              # This file
├── spec.md              # Design spec (copied from brainstorming)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI fragments)
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Tenant.Service/
├── Models/
│   ├── Organization.cs                          # Add WalletAddress, PublicKey, EncryptionPublicKey, SigningAlgorithm
│   ├── OrganizationRegisterSubscription.cs      # NEW: subscription entity
│   ├── InvitationNonce.cs                       # NEW: Phase 2 nonce entity
│   └── Dtos/
│       ├── RegisterSubscriptionDtos.cs          # NEW: request/response DTOs
│       └── RegisterInvitationDtos.cs            # NEW: Phase 2 DTOs
├── Endpoints/
│   ├── RegisterSubscriptionEndpoints.cs         # NEW: CRUD endpoints
│   ├── RegisterInvitationEndpoints.cs           # NEW: Phase 2 invitation endpoints
│   └── BootstrapEndpoints.cs                    # MODIFY: org wallet provisioning
├── Services/
│   ├── IRegisterSubscriptionService.cs          # NEW: interface
│   ├── RegisterSubscriptionService.cs           # NEW: business logic
│   ├── IRegisterInvitationService.cs            # NEW: Phase 2 interface
│   ├── RegisterInvitationService.cs             # NEW: Phase 2 business logic
│   └── OrgWalletReconciliationService.cs        # NEW: background wallet provisioning
├── Data/
│   └── TenantDbContext.cs                       # MODIFY: add DbSets, config
└── Migrations/
    └── [timestamp]_AddRegisterSubscriptions.cs  # NEW: EF migration

src/Common/Sorcha.Register.Models/
└── SorchaDidIdentifier.cs                       # MODIFY: Phase 2 — add Organization type

src/Common/Sorcha.ServiceClients/Did/
└── SorchaDidResolver.cs                         # MODIFY: Phase 2 — add org method resolution

src/Services/Sorcha.Register.Service/
└── [bootstrap code]                             # MODIFY: System Register name in advertisement

src/Services/Sorcha.ApiGateway/
└── appsettings.json                             # MODIFY: new YARP routes

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
├── Pages/Registers/Index.razor                  # MODIFY: subscription-scoped view
└── Pages/MyWorkflows.razor                      # MODIFY: filter to subscribed registers

src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Services/RegisterSubscriptionService.cs      # NEW: HTTP client for subscription API
├── Components/Registers/SubscribeDialog.razor   # NEW: subscribe to public register
└── Components/Admin/PeerServiceAdmin.razor      # MODIFY: remove Available Registers tab

tests/
├── Sorcha.Tenant.Service.Tests/
│   ├── Services/RegisterSubscriptionServiceTests.cs  # NEW
│   ├── Endpoints/RegisterSubscriptionEndpointsTests.cs  # NEW
│   └── Services/RegisterInvitationServiceTests.cs  # NEW: Phase 2
├── Sorcha.Register.Models.Tests/
│   └── SorchaDidIdentifierTests.cs              # MODIFY: Phase 2 — org DID tests
└── Sorcha.ServiceClients.Tests/Did/
    └── SorchaDidResolverTests.cs                # MODIFY: Phase 2 — org resolution tests
```

## Phase 1 — Org Register Subscriptions

### Step 1a: System Register Name Fix
- Fix Register Service bootstrap to include `Name = "Sorcha System Register"` in peer advertisement
- Verify in Peer Service admin UI

### Step 1b: Subscription Data Model
- Add `OrganizationRegisterSubscription` entity to Tenant Service
- Add `SubscriptionType` and `SubscriptionStatus` enums
- Configure in `TenantDbContext` with unique constraint `(OrganizationId, RegisterId)`
- EF migration

### Step 1c: Org Wallet Fields
- Add `WalletAddress`, `PublicKey`, `EncryptionPublicKey`, `SigningAlgorithm` to `Organization`
- EF migration (combine with 1b if same PR)

### Step 1d: Org Wallet Provisioning
- Extend bootstrap endpoint to create org wallet
- Extend org creation to create org wallet
- `OrgWalletReconciliationService` background service for retry
- Unit tests for provisioning logic

### Step 1e: Subscription CRUD Endpoints
- `RegisterSubscriptionEndpoints.cs` with GET (list, single), POST, DELETE
- `RegisterSubscriptionService.cs` with business logic
- `RegisterSubscriptionDtos.cs` request/response types
- Pending → Active status transition with async retry
- Unit tests, integration tests

### Step 1f: Auto-Subscribe on Register Creation
- API Gateway / UI layer creates subscription after successful register creation
- Owner type, cannot be unsubscribed

### Step 1g: Bootstrap Auto-Subscribe
- Bootstrap endpoint creates Owner subscription to System Register for System Admin org

### Step 1h: YARP Gateway Routes
- Add routes for `/api/organizations/{orgId}/register-subscriptions/*`
- Add route for `/api/me/subscribed-registers`

### Step 1i: UI — Registers Page
- Consolidated view showing only subscribed registers
- Subscription type badges
- Subscribe dialog (public registers from peer network)
- Unsubscribe action

### Step 1j: UI — New Submission Scoping
- Filter register dropdown to subscribed registers

### Step 1k: UI — Remove Available Registers Tab
- Remove from Peer Admin, functionality now in Registers page

## Phase 2 — Private Register Invitations

### Step 2a: Org DID Method
- `SorchaDidType.Organization` enum value
- `SorchaDidIdentifier.FromOrganization()` factory
- `SorchaDidResolver` org method resolution
- Tests

### Step 2b: Join Private Register Blueprint
- JSON blueprint definition
- Publish to System Register during bootstrap

### Step 2c: Invitation Creation
- `RegisterInvitationEndpoints.cs` POST create
- X25519 encryption of payload
- ED25519 signing
- `RegisterInvitationService.cs`

### Step 2d: Invitation Acceptance
- POST accept endpoint
- Decrypt, verify, consume nonce
- Create subscription, trigger peer subscribe

### Step 2e: On-Ledger Record
- Blueprint instance creation for audit trail

### Step 2f: Invitation Revocation + Nonce Registry
- DELETE endpoint, `InvitationNonce` entity
- PostgreSQL fast-path + ledger audit

### Step 2g-2h: Invitation UI
- Invite Organisation dialog
- Accept Invitation flow
- Invitations panel

### Step 2i: Org Settings UI
- Wallet address, DID, public key display

### Step 2j: Security Hardening
- Rate limiting, max pending invitations
- Genesis hash verification on subscribe
