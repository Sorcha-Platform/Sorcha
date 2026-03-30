# Research: Auto-Register Participant & PlatformUser Provisioning

## R1: Auto-Link Bypass Strategy

**Decision**: Create `LinkedWalletAddress` directly in the Tenant Service during wallet creation, bypassing the challenge/verify flow.

**Rationale**: The challenge/verify flow proves wallet ownership via cryptographic signature. When a user creates a wallet with their own mnemonic, ownership is already proven — they just generated the keys. Requiring a round-trip signature verification for a wallet the user created moments ago adds friction with no security benefit.

**Implementation**: After wallet creation succeeds, the Wallet Service (or a post-creation hook) calls the Tenant Service internally to:
1. Ensure participant exists (self-register if not)
2. Create `LinkedWalletAddress` with `VerificationMethod = "self-created"` to distinguish from challenge-verified links

**Alternatives considered**:
- Calling the existing challenge/verify endpoints automatically: Rejected — adds network round-trips and complexity for no security gain
- Linking from the UI after wallet creation: Rejected — user-facing friction, exactly the problem we're solving
- Making wallet creation a Tenant Service concern: Rejected — violates microservice boundaries

## R2: Where to Place the Auto-Link Logic

**Decision**: In the Wallet Service's wallet creation endpoint, as a post-creation fire-and-forget call to the Tenant Service via `IParticipantServiceClient`.

**Rationale**: The Wallet Service already has the wallet address and the authenticated user's JWT (with `sub` and `org_id` claims). It can call the Tenant Service to register the participant and link the wallet. Failures don't block wallet creation (FR-004).

**Alternatives considered**:
- UI-side orchestration (call participant + wallet-link endpoints from Blazor): Rejected — fragile, adds UI complexity, race conditions with token refresh
- Tenant Service event subscription: Rejected — no event bus exists; adds infrastructure complexity
- Middleware/interceptor: Rejected — too implicit, harder to test

## R3: PlatformUser Admin Endpoint Design

**Decision**: Single `POST /api/platform/users` endpoint in Tenant Service that creates PlatformUser + UserIdentity + PlatformUserOrgMembership atomically.

**Rationale**: The existing `RegistrationService.RegisterAsync()` already does this for public self-registration. The admin endpoint follows the same pattern but adds: role selection, `skipEmailVerification`, and bypasses org self-registration checks.

**Implementation**: New `PlatformUserProvisioningService` (or extend existing `RegistrationService`) with `ProvisionUserAsync()` method that:
1. Validates org exists
2. Creates or reuses PlatformUser by email
3. Creates UserIdentity with specified role
4. Creates PlatformUserOrgMembership
5. Optionally hashes password and marks email verified

## R4: Password Reset Endpoint

**Decision**: `PUT /api/platform/users/{id}/password` with SystemAdmin authorisation.

**Rationale**: Simple REST semantics. Reuses existing `IPasswordHasher` infrastructure from RegistrationService.

## R5: Auto-Link and Existing Wallet Links

**Decision**: Auto-link respects the existing platform-wide uniqueness constraint. If the wallet is already linked (shouldn't happen for a just-created wallet), the auto-link is skipped with a warning.

**Rationale**: The uniqueness constraint in `WalletVerificationService` prevents one wallet from being linked to multiple participants. Auto-link must honour this.

## R6: YARP Gateway Routes

**Decision**: New routes needed for `POST /api/platform/users` and `PUT /api/platform/users/{id}/password`. The auto-link is internal (Wallet → Tenant service-to-service) and doesn't need a gateway route.

**Rationale**: Admin endpoints must be accessible through the API Gateway. Internal service calls use direct service discovery.
