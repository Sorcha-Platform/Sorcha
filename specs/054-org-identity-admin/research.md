# Research: Organization Admin & Identity Management

**Branch**: `054-org-identity-admin` | **Date**: 2026-03-08

## R1: OIDC Authorization Code Flow in ASP.NET Core

**Decision**: Use `Microsoft.AspNetCore.Authentication.OpenIdConnect` (already installed) for the server-side authorization code flow, but managed manually per-organization rather than via ASP.NET's built-in middleware.

**Rationale**: The built-in OIDC middleware is designed for single-tenant apps with one provider. Sorcha needs multi-tenant, per-org dynamic provider selection. We'll use `HttpClient` + OIDC discovery documents + manual code exchange to support N providers across N orgs.

**Alternatives considered**:
- **Built-in OIDC middleware** — Single-provider, doesn't support dynamic per-org provider selection. Rejected.
- **IdentityServer/Duende** — Full-featured but heavy dependency, commercial license. Rejected (overkill for our needs).
- **Manual HttpClient + discovery** — Full control, per-org dynamic providers, no new dependencies. **Chosen**.

**Implementation approach**:
1. `OidcDiscoveryService` — Fetches and caches `.well-known/openid-configuration` per issuer URL (24h cache with `IMemoryCache`).
2. `OidcExchangeService` — Handles authorization URL generation, code exchange, token validation.
3. `OidcProvisioningService` — Maps external claims to `UserIdentity`, handles auto-provisioning.
4. New endpoints under `/api/auth/oidc/` for initiate, callback, and profile completion.

## R2: OIDC Provider-Specific Quirks

**Decision**: Handle provider-specific behaviors via a `ProviderPreset` enum with per-provider claim mapping and configuration defaults.

**Key quirks by provider**:

| Provider | Quirk | Mitigation |
|----------|-------|------------|
| Microsoft Entra ID | Tenant-specific issuer URL template | Pre-fill `https://login.microsoftonline.com/{tenant}/v2.0` |
| Google | Always returns `email_verified` | Trust claim, skip verification |
| Okta | Supports custom authorization servers | Allow full issuer URL override |
| Apple | Email/name ONLY on first auth | Persist immediately; retry guidance if exchange fails |
| Amazon Cognito | Region-specific issuer URL | Pre-fill `https://cognito-idp.{region}.amazonaws.com/{poolId}` |

**ProviderPreset enum** (extends existing `IdentityProviderType`):
```
MicrosoftEntra, Google, Okta, Apple, AmazonCognito, GenericOidc
```

## R3: Email Verification Flow

**Decision**: Token-in-link approach (not code entry). Verification token stored on `UserIdentity` with 24h expiry.

**Rationale**: Maximum compatibility across email clients. Simple to implement. Consistent with industry standard.

**Flow**:
1. Generate cryptographic random token (32 bytes, URL-safe base64)
2. Store on `UserIdentity.VerificationToken` + `UserIdentity.VerificationTokenExpiresAt`
3. Send email with link: `/auth/verify-email?token={token}&orgSubdomain={sub}`
4. On click: validate token, mark `EmailVerified=true`, clear token
5. If expired: user requests new verification email

**Email sending**: Abstract via `IEmailSender` interface. Initial implementation: SMTP via `MailKit`. Configuration via `EmailSettings` section.

## R4: Multi-Tenant URL Resolution Strategy

**Decision**: Resolution handled at API Gateway level via YARP middleware, not in individual services.

**Rationale**: Centralized resolution ensures all services receive the same org context. Services don't need to know about URL tiers.

**Implementation**:
1. **Path-based** (`/org/{subdomain}/...`): YARP route transform extracts subdomain from path, adds `X-Org-Subdomain` header.
2. **Subdomain-based** (`{sub}.sorcha.io`): Middleware extracts subdomain from `Host` header.
3. **Custom domain** (`login.acme.com`): Lookup `CustomDomainMapping` table (cached in Redis, 5min TTL).

All three tiers add `X-Org-Subdomain` header to downstream requests. Tenant Service resolves subdomain to org ID.

**Note**: For Phase 1, implement path-based only. Subdomain and custom domain require infrastructure (wildcard DNS, cert management) that is deployment-specific.

## R5: Role Consolidation Migration

**Decision**: EF Core migration to remove `Developer`, `User`, `Consumer` from `UserRole` enum. Map all existing users with deprecated roles to `Member`.

**Rationale**: Spec requires exactly 5 roles. Existing users must not lose access.

**Migration steps**:
1. Add EF Core migration that updates all `UserIdentity.Roles` containing `Developer`, `User`, or `Consumer` to `Member`.
2. Update `UserRole` enum to remove deprecated values (keep numeric values stable for existing data).
3. Update authorization policies and token claims to use new role set.

**Risk**: Low — no existing production data. These roles were defined but minimally used.

## R6: Breached Password Check

**Decision**: Use the Have I Been Pwned (HIBP) k-Anonymity API for password breach checking.

**Rationale**: Free, privacy-preserving (only sends first 5 chars of SHA-1 hash), no local database needed.

**Implementation**:
- `IPasswordPolicyService` with `IsBreachedAsync(string password)` method
- SHA-1 hash → send first 5 chars to `https://api.pwnedpasswords.com/range/{prefix}`
- Check if full hash suffix appears in response
- Cache negative results (not breached) for 24h

## R7: Progressive Account Lockout

**Decision**: Extend existing `TokenRevocationService` which already implements lockout tracking via Redis.

**Rationale**: The lockout logic (5/10/15/25 thresholds) already exists in `TokenRevocationService.IsRateLimitedAsync()` and `IncrementFailedAuthAttemptsAsync()`. We need to:
1. Verify thresholds match spec (5=5min, 10=30min, 15=24h, 25=admin-unlock)
2. Add admin unlock endpoint
3. Surface lockout status in admin UI

## R8: Invitation System

**Decision**: Time-limited tokens stored in `OrgInvitation` entity. Invitations bypass domain restrictions.

**Implementation**:
- `OrgInvitation` entity with: Email, Role, Token, ExpiresAt (7 days), Status, InvitedByUserId
- Token: 32-byte cryptographic random, URL-safe base64
- Link: `/auth/accept-invitation?token={token}&orgSubdomain={sub}`
- Accepting: authenticate (OIDC or local), invitation consumed, user provisioned with specified role
- Admin can revoke (set status to Revoked)

## R9: Custom Domain SSL & Verification

**Decision**: Defer to hosting platform (Azure managed certificates). Sorcha tracks domain status only.

**Rationale**: SSL certificate provisioning is an infrastructure concern. Azure Container Apps, AWS ALB, and Cloudflare all handle this automatically once CNAME is verified.

**Implementation**:
- `CustomDomainMapping` entity: Domain, OrgId, Status (Pending/Verified/Failed), VerifiedAt
- Verification: DNS CNAME lookup via `System.Net.Dns.GetHostEntryAsync()`
- Periodic check: Background service, daily, updates status
- Phase 1: Manual verification endpoint. Phase 2: Automated background checks.

## R10: Audit Event Retention & Purge

**Decision**: Background hosted service with daily cleanup. Retention period stored per-org in `Organization.AuditRetentionMonths` (default 12).

**Implementation**:
- `AuditCleanupService : BackgroundService` — runs daily at 2 AM UTC
- Queries each org's retention setting
- Deletes audit events older than retention period
- Logs purge counts per org
