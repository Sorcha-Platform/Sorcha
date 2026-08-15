# Authentication Setup Guide

## Overview

The Sorcha platform uses **JWT (JSON Web Token) Bearer authentication** for securing all API endpoints. The **Tenant Service** acts as the authentication authority, issuing tokens that are validated by all other services.

## Architecture

```
┌─────────────────┐
│  Tenant Service │ ──► Issues JWT tokens
└────────┬────────┘
         │
         │ JWT Token
         ▼
┌────────────────────────────────────┐
│  Protected Services                │
│  ├─ Blueprint Service (validates) │
│  ├─ Wallet Service (validates)    │
│  ├─ Register Service (validates)  │
│  └─ Peer Service (validates)     │
└────────────────────────────────────┘
```

## Platform Identity Layer (Feature 058)

Feature 058 introduces a two-tier identity model:

| Layer | Schema | Purpose | Entity |
|-------|--------|---------|--------|
| Platform | public | Authentication, cross-org anchor | `PlatformUser` |
| Organisation | org_{id} | Authorisation, org-scoped role | `UserIdentity` |

### How It Works

1. **Authentication** always resolves to a `PlatformUser` (by email, social login, or passkey)
2. The `PlatformUser` has one or more `PlatformUserOrgMembership` records linking them to organisations
3. A JWT is issued scoped to one organisation, containing both `platform_user_id` and org-scoped `sub` claims
4. **Authorisation** is checked against the `UserIdentity` in the current org's schema

### JWT Claims (Updated)

| Claim | Value | Purpose |
|-------|-------|---------|
| `sub` | UserIdentity.Id | Org-scoped user ID |
| `platform_user_id` | PlatformUser.Id | Cross-org identity anchor |
| `org_id` | Organization.Id | Current org scope |
| `org_name` | Organization.Name | Display name |
| `roles` | UserRole[] | Org-scoped permissions |

---

## Organisation Switching

Users who belong to multiple organisations can switch their active context without re-authenticating.

### Flow

1. User calls `GET /api/auth/me/organizations` to list their org memberships
2. User calls `POST /api/auth/switch-org` with target `organizationId`
3. Server verifies membership, issues a new JWT scoped to the target org
4. Client stores the new tokens and reloads the application context

### Security

- The switch endpoint verifies active membership in the target org
- A completely new JWT is issued (not a token modification)
- The previous token remains valid until expiry but is scoped to the old org
- Organisation suspension prevents switching into that org

---

## Social Login (Feature 058)

Social login uses OAuth2/OIDC with PKCE for all providers.

### Supported Providers

| Provider | OIDC Discovery | Scopes |
|----------|---------------|--------|
| Google | `https://accounts.google.com/.well-known/openid-configuration` | openid, email, profile |
| GitHub | Custom (non-standard OIDC) | user:email |
| Microsoft | `https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration` | openid, email, profile |
| Apple | `https://appleid.apple.com/.well-known/openid-configuration` | openid, email, name |

### Flow

1. Client calls `POST /api/auth/social/initiate` with provider name and return URL
2. Server generates PKCE challenge, stores state, returns authorization URL
3. User completes OAuth dance with provider
4. Client sends authorization code to `POST /api/auth/social/callback`
5. Server exchanges code for tokens **and verifies the ID token's JWS signature against the provider's published JWKS** (review M3a — `IOidcSigningKeyResolver` fetches + caches the keys from the IdP's `jwks_uri`/discovery, rotation-tolerant; fail-closed if the signature can't be verified), in addition to the existing issuer / audience / expiry / nonce checks, then resolves/creates `PlatformUser` + `PlatformSocialLogin`
6. JWT issued for the user's default organisation

### Configuration

Social login providers are configured per-organisation via the Identity Provider Configuration API (`/api/organizations/{orgId}/idp`). The public org typically has social providers enabled. Maximum 4 simultaneous providers per org.

---

## Services Configured (AUTH-002 Complete)

### ✅ Tenant Service
- **Role**: Authentication Authority
- **Functionality**: Issues JWT tokens via `/api/auth/login` and `/api/service-auth/token`
- **Token Types**:
  - User tokens (email/password login)
  - Service tokens (client credentials OAuth2)
  - Delegated tokens (service acting on behalf of user)

### ✅ Blueprint Service
- **Authentication**: JWT Bearer validation
- **Authorization Policies**:
  - `CanManageBlueprints` - Create, update, delete blueprints
  - `CanExecuteBlueprints` - Execute actions and workflows
  - `CanPublishBlueprints` - Publish blueprints
  - `RequireService` - Service-to-service operations

### ✅ Wallet Service
- **Authentication**: JWT Bearer validation
- **Authorization Policies**:
  - `CanManageWallets` - Create wallets, list wallets
  - `CanUseWallet` - Sign, encrypt, decrypt operations
  - `RequireService` - Service-to-service operations

### ✅ Register Service
- **Authentication**: JWT Bearer validation (register creation endpoints no longer allow anonymous access)
- **Authorization Policies**:
  - `CanManageRegisters` - Create and configure registers (requires `org_id` claim + Administrator or SystemAdmin role)
  - `CanCreateSystemRegisters` - Set register purpose to "System" (requires SystemAdmin org `00000000-0000-0000-0000-000000000001` + SystemAdmin role)
  - `CanSubmitTransactions` - Submit transactions
  - `CanReadTransactions` - Query transactions
  - `RequireService` - Service-to-service notifications

### ✅ Peer Service
- **Authentication**: JWT Bearer validation
- **Authorization Policies**:
  - `RequireAuthenticated` - Subscribe/unsubscribe/purge register replication
  - `CanManagePeers` - Ban, unban, reset peer failure counts
  - `RequireService` - Service-to-service operations
- **Unauthenticated Endpoints**: Read-only monitoring (peer list, health, stats, cache stats)

## Configuration

### JWT Settings (Required for ALL Services)

Add to `appsettings.json` or `appsettings.Development.json`:

```json
{
  "JwtSettings": {
    "InstallationName": "localhost",
    "SigningKey": "your-secret-key-min-32-characters-REPLACE-THIS-IN-PRODUCTION",
    "AccessTokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeHours": 24,
    "ServiceTokenLifetimeHours": 8,
    "ClockSkewMinutes": 5
  }
}
```

> **Issuer and audiences are derived, not hand-set (Feature 136).** `InstallationName` drives both:
> the issuer resolves to `urn:sorcha:{InstallationName}` (fail-closed in Production/Staging) and the
> audiences are the four tier strings `{InstallationName}:consumer|platform|service|enrol-session`.
> Do **not** set `JwtSettings:Issuer`/`Audience` here (the audience config is ignored). See
> [JWT Configuration](JWT-CONFIGURATION.md) for the full tiered-audience model.

### Environment Variables (Recommended for Production)

```bash
# JWT Configuration (Feature 136 — InstallationName drives issuer and all tier audiences)
export JwtSettings__InstallationName="your-deployment-name"
export JwtSettings__SigningKey="<strong-random-key-from-azure-key-vault>"
# JwtSettings__Audience is intentionally absent — audiences are derived from InstallationName.
# See docs/guides/JWT-CONFIGURATION.md for the full tiered-audience model.
```

### Azure Key Vault (Production)

For production deployments, store the signing key in Azure Key Vault:

```bash
# Store signing key
az keyvault secret set \
  --vault-name sorcha-keyvault \
  --name JwtSigningKey \
  --value "<your-strong-random-key>"

# Configure app to use Key Vault
export AZURE_KEY_VAULT_ENDPOINT="https://sorcha-keyvault.vault.azure.net/"
```

## Authentication Flow

### 1. User Authentication (Email/Password)

```http
POST https://tenant.sorcha.io/api/auth/login
Content-Type: application/json

{
  "email": "user@organization.com",
  "password": "SecurePassword123!"
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "refresh_token_here",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

### 2. Using the Token

Include the access token in the `Authorization` header for all API requests:

```http
GET https://blueprint.sorcha.io/api/blueprints
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 3. Service-to-Service Authentication (OAuth2 Client Credentials)

```http
POST https://tenant.sorcha.io/api/service-auth/token
Content-Type: application/json

{
  "grantType": "client_credentials",
  "clientId": "blueprint-service",
  "clientSecret": "service-secret",
  "scope": "blueprints:write registers:read"
}
```

## Token Claims

The `aud` claim is the **trust tier** (`{installation}:consumer|platform|service|enrol-session`) and
`iss` is `urn:sorcha:{installation}` — see [JWT Configuration](JWT-CONFIGURATION.md). Examples use the
`localhost` installation.

### User Tokens (Platform tier — admin / designer / org operator)
```json
{
  "sub": "user-id-guid",
  "email": "user@organization.com",
  "platform_user_id": "platform-user-guid",
  "org_id": "organization-id-guid",
  "org_name": "Example Org",
  "roles": ["Administrator"],
  "token_type": "user",
  "iss": "urn:sorcha:localhost",
  "aud": "localhost:platform"
}
```

> A **Consumer**-tier token (citizen / wallet holder) has the same shape **minus `roles` and
> `wallet_address`**, with `aud": "localhost:consumer"`.

### Service Tokens
```json
{
  "sub": "service-principal-id",
  "client_id": "service-blueprint",
  "service_name": "Blueprint Service",
  "token_type": "service",
  "scope": ["blueprints:write", "registers:read"],
  "iss": "urn:sorcha:localhost",
  "aud": "localhost:service"
}
```

## Testing Authentication

### 1. Start Tenant Service

```bash
cd src/Apps/Sorcha.AppHost
dotnet run
```

The Tenant Service will be available at: `https://localhost:7080` (check Aspire dashboard)

### 2. Create a Test User

```bash
# Register a test organization and user
curl -X POST https://localhost:7080/api/organizations \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Test Organization",
    "subdomain": "test-org"
  }'

# Add a user to the organization
curl -X POST https://localhost:7080/api/organizations/{org-id}/users \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@test-org.com",
    "displayName": "Admin User",
    "externalIdpUserId": "test-123",
    "roles": ["Administrator"]
  }'
```

### 3. Login and Get Token

```bash
curl -X POST https://localhost:7080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@test-org.com",
    "password": "password123"
  }'
```

Save the `accessToken` from the response.

### 4. Test Protected Endpoints

```bash
# Test Blueprint Service
curl https://localhost:7081/api/blueprints \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"

# Test Wallet Service
curl https://localhost:7082/api/v1/wallets \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"

# Test Register Service
curl https://localhost:7083/api/registers \
  -H "Authorization: Bearer YOUR_ACCESS_TOKEN"
```

## Authorization Policies

### Blueprint Service

| Policy | Description | Required Claims |
|--------|-------------|-----------------|
| `CanManageBlueprints` | Create, update, delete blueprints | (`token_type=service` AND `:service` aud) OR (`org_id` AND `:platform` aud) — Feature 147 |
| `CanExecuteBlueprints` | Execute actions | Authenticated user |
| `CanPublishBlueprints` | Publish blueprints | `can_publish_blueprint=true` OR `role=Administrator` |
| `RequireService` | Service operations | `token_type=service` |

### Wallet Service

| Policy | Description | Required Claims |
|--------|-------------|-----------------|
| `CanManageWallets` | Create, list wallets | `org_id` OR `token_type=service` |
| `CanUseWallet` | Sign, encrypt, decrypt | Authenticated user |
| `RequireService` | Service operations | `token_type=service` |

### Register Service

| Policy | Description | Required Claims |
|--------|-------------|-----------------|
| `CanManageRegisters` | Create and manage registers | `org_id` + (`role=Administrator` OR `role=SystemAdmin`) |
| `CanCreateSystemRegisters` | Set register purpose to System | `org_id=00000000-0000-0000-0000-000000000001` + `role=SystemAdmin` |
| `CanSubmitTransactions` | Submit transactions | Authenticated user |
| `CanReadTransactions` | Query transactions | Authenticated user |
| `RequireService` | Notifications | `token_type=service` |

> **Note:** Register creation endpoints (`/api/registers/initiate` and `/api/registers/finalize`) no longer allow anonymous access. The `CanManageRegisters` policy was tightened from requiring only `org_id` presence to requiring `org_id` plus an Administrator or SystemAdmin role.

### Peer Service

| Policy | Description | Required Claims |
|--------|-------------|-----------------|
| `RequireAuthenticated` | Subscribe/unsubscribe/purge registers | Authenticated user |
| `CanManagePeers` | Ban, unban, reset peers | `org_id` OR `token_type=service` |
| `RequireService` | Service operations | `token_type=service` |

## Security Best Practices

### Development
- ✅ Use a development signing key (min 32 characters)
- ✅ Store keys in `appsettings.Development.json` (gitignored)
- ✅ Use HTTPS for local development
- ✅ Test with both user and service tokens

### Production
- ✅ **NEVER** commit signing keys to source control
- ✅ Use Azure Key Vault or AWS Secrets Manager
- ✅ Rotate signing keys regularly (every 90 days recommended)
- ✅ Use strong random keys (256+ bits)
- ✅ Enable HTTPS everywhere
- ✅ Set appropriate token lifetimes
- ✅ Monitor failed authentication attempts
- ✅ Implement token revocation for compromised tokens

## Troubleshooting

### 401 Unauthorized Errors

**Symptom**: API returns 401 Unauthorized

**Common Causes:**
1. **Missing or invalid token** - Check Authorization header format
2. **Expired token** - Request a new token
3. **Wrong signing key** - Ensure all services use the same SigningKey
4. **Wrong issuer/audience** - Check JwtSettings match across services

**Solution:**
```bash
# Check token expiration
echo "YOUR_TOKEN" | base64 -d | jq .exp

# Verify signing key matches
grep SigningKey appsettings.*.json
```

### 403 Forbidden Errors

**Symptom**: Token validates but operation denied

**Common Causes:**
1. **Missing required claims** - Check token has needed claims (org_id, role, etc.)
2. **Insufficient permissions** - User lacks required role
3. **Wrong token type** - Using user token for service operation or vice versa

**Solution:**
```bash
# Decode and inspect token claims
echo "YOUR_TOKEN" | jwt decode -

# Check authorization policy requirements
```

### Token Not Validating

**Symptom**: Services cannot validate tokens from Tenant Service

**Checklist:**
- [ ] All services have same `JwtSettings__SigningKey`
- [ ] All services have same `JwtSettings__InstallationName` (drives both issuer and audiences — `JwtSettings:Audience` is derived and must NOT be set separately)
- [ ] JWT Bearer package installed on all services
- [ ] `app.UseAuthentication()` called before `app.UseAuthorization()`

---

## OIDC Identity Provider Configuration

The Tenant Service supports external identity provider (IDP) integration using OpenID Connect (OIDC). Organizations can connect their existing corporate identity system so users sign in with their existing credentials. The platform performs a full token exchange: external OIDC tokens are exchanged for Sorcha-native JWTs, and downstream services never see external tokens.

### Discovery-First Approach

Configuration follows a discovery-first workflow. The administrator provides an issuer URL and the system automatically fetches the provider's `.well-known/openid-configuration` document to populate endpoints.

**Configuration Flow:**

1. **Discover** — Enter the issuer URL (or select a provider preset). The system fetches the discovery document and auto-populates endpoints.
2. **Create** — Provide the Client ID and Client Secret obtained from the IDP's developer console.
3. **Test** — Click "Test Connection" to validate credentials against the provider.
4. **Enable** — Activate the configuration so it appears as a sign-in option on the organization's login page.

### Provider Presets

The following well-known providers have pre-configured issuer URL templates:

| Provider | Issuer URL Template |
|----------|-------------------|
| Microsoft Entra ID | `https://login.microsoftonline.com/{tenant-id}/v2.0` |
| Google | `https://accounts.google.com` |
| Okta | `https://{domain}.okta.com` |
| Apple | `https://appleid.apple.com` |
| Amazon Cognito | `https://cognito-idp.{region}.amazonaws.com/{user-pool-id}` |
| Custom | Any OIDC-compliant issuer URL |

### Required and Auto-Discovered Fields

**Required (admin must provide):**

| Field | Description |
|-------|-------------|
| `ClientId` | Application/client ID from the IDP's developer console |
| `ClientSecret` | Client secret from the IDP's developer console (encrypted at rest) |
| `Issuer` | The IDP's issuer URL |
| `Scopes` | Requested scopes (default: `openid profile email`) |

**Auto-discovered from `.well-known/openid-configuration`:**

| Field | Description |
|-------|-------------|
| `AuthorizationEndpoint` | URL for the authorization request |
| `TokenEndpoint` | URL for token exchange |
| `UserInfoEndpoint` | URL for user info retrieval |
| `JwksUri` | URL for JSON Web Key Set (signature verification) |

Discovery documents are cached and refreshed every 24 hours.

### Token Exchange Flow

When a user authenticates through an external IDP, the following exchange occurs:

```
┌──────────┐     ┌─────────────────┐     ┌──────────────┐     ┌──────────────┐
│  Browser  │     │  Tenant Service  │     │  External    │     │  Downstream  │
│           │     │                  │     │  IDP         │     │  Services    │
└─────┬─────┘     └────────┬─────────┘     └──────┬───────┘     └──────┬───────┘
      │                    │                       │                    │
      │ 1. Click           │                       │                    │
      │    "Sign in with   │                       │                    │
      │     [Provider]"    │                       │                    │
      │───────────────────▶│                       │                    │
      │                    │                       │                    │
      │ 2. Redirect to IDP │                       │                    │
      │◀───────────────────│                       │                    │
      │                    │                       │                    │
      │ 3. Authenticate    │                       │                    │
      │    at IDP          │                       │                    │
      │───────────────────────────────────────────▶│                    │
      │                    │                       │                    │
      │ 4. Redirect back   │                       │                    │
      │    with auth code  │                       │                    │
      │◀──────────────────────────────────────────│                    │
      │                    │                       │                    │
      │ 5. Auth code       │                       │                    │
      │───────────────────▶│                       │                    │
      │                    │ 6. Exchange code       │                    │
      │                    │    for tokens          │                    │
      │                    │    (server-side)       │                    │
      │                    │──────────────────────▶│                    │
      │                    │                       │                    │
      │                    │ 7. External ID token   │                    │
      │                    │◀──────────────────────│                    │
      │                    │                       │                    │
      │                    │ 8. Validate external   │                    │
      │                    │    token (sig, iss,    │                    │
      │                    │    aud, exp, nonce)    │                    │
      │                    │                       │                    │
      │ 9. Issue Sorcha    │                       │                    │
      │    JWT (native)    │                       │                    │
      │◀───────────────────│                       │                    │
      │                    │                       │                    │
      │ 10. Call API with  │                       │                    │
      │     Sorcha JWT     │                       │                    │
      │─────────────────────────────────────────────────────────────▶│
      │                    │                       │                    │
```

Key points:
- Authorization codes are exchanged server-side (step 6) — tokens are never exposed to the browser.
- The external ID token is validated (signature via JWKS, issuer, audience, expiry, nonce) before a Sorcha JWT is issued.
- Downstream services only ever see Sorcha-native JWTs. They do not need to know about external IDPs.
- Users are matched by the IDP's `sub` (subject) claim, not by email address.

### IDP Configuration via API

```http
POST https://tenant.sorcha.io/api/organizations/{orgId}/idp-config
Content-Type: application/json
Authorization: Bearer <admin-token>

{
  "providerType": "MicrosoftEntra",
  "issuerUrl": "https://login.microsoftonline.com/{tenant-id}/v2.0",
  "clientId": "your-app-client-id",
  "clientSecret": "your-app-client-secret",
  "scopes": "openid profile email"
}
```

### IDP Configuration via Admin UI

Navigate to **Identity > Identity Providers** in the admin console. Select a provider preset or enter a custom issuer URL, fill in the client credentials, test the connection, and enable it.

---

## Auto-Provisioning

When an external IDP is configured and active, user accounts are created automatically on first OIDC login. No administrator action is required for day-to-day user onboarding.

### How It Works

1. A user authenticates through the organization's configured IDP.
2. The Tenant Service checks whether a user record exists for the IDP's `sub` claim.
3. If no record exists, a new `UserIdentity` is created with:
   - **Role**: `Member` (default for all auto-provisioned users)
   - **Email**: Extracted from `email`, `preferred_username`, or `upn` claims
   - **Display name**: Extracted from `name` or `given_name` + `family_name` claims
   - **Status**: `Active` (if email is verified by the IDP) or `PendingVerification`
4. A Sorcha JWT is issued for the new or existing user.

### Domain Restrictions

By default, organizations have no domain restrictions and any email address can auto-provision. Administrators can restrict auto-provisioning to specific email domains.

| Scenario | Behavior |
|----------|----------|
| No restrictions configured | Any user who authenticates via the IDP is auto-provisioned |
| Restrictions active, email matches | User is auto-provisioned normally |
| Restrictions active, email does not match | User is denied with a message to contact the org administrator |
| User has an explicit invitation | User can join regardless of domain restrictions |

Configure domain restrictions via the admin console under **Identity > Domain Restrictions**, or via the API:

```http
POST https://tenant.sorcha.io/api/organizations/{orgId}/domain-restrictions
Content-Type: application/json
Authorization: Bearer <admin-token>

{
  "domain": "contoso.com"
}
```

---

## Email Verification

All users must have a verified email address before accessing the platform.

### Verification Paths

| Authentication Method | Verification Approach |
|-----------------------|----------------------|
| OIDC (IDP returns `email_verified: true`) | Email is trusted and marked as verified immediately — no additional verification required |
| OIDC (no `email_verified` claim, or `false`) | User is redirected to a "Complete your profile" page and must verify their email via a token-based flow |
| Local email/password account | A verification email is sent on registration with a time-limited token (24 hours) |
| Passkey (WebAuthn) signup with a real email | A verification email is sent on registration completion (`register/verify`), identical to the email/password path — a passkey does not establish email ownership. Skipped for passkey-only signups that use a synthetic `@placeholder.local` address, and for users who are already verified. |

### Token-Based Verification Flow

1. User registers or is prompted to verify their email.
2. The system sends an email containing a verification link with a unique token.
3. The user clicks the link within 24 hours.
4. The email is marked as verified and the account is fully activated.
5. If the token expires, the user can request a new verification email.

Users with unverified emails cannot access platform features. They will be redirected to the verification prompt on each login attempt.

---

## Password Policy (NIST SP 800-63B)

Local email/password accounts follow a modern password policy aligned with NIST SP 800-63B recommendations.

### Rules

| Rule | Value |
|------|-------|
| Minimum length | 12 characters |
| Maximum length | No limit (practical cap at 256 characters) |
| Complexity rules | None — no mandatory uppercase, lowercase, number, or special character requirements |
| Breach list check | Passwords are checked against known breached password lists and rejected if found |
| Password history | Not enforced |

### Progressive Account Lockout

Failed login attempts trigger progressive lockout to protect against brute-force attacks:

| Failed Attempts | Lockout Duration |
|-----------------|-----------------|
| 5 | 5 minutes |
| 10 | 30 minutes |
| 15 | 24 hours |
| 25 | Locked until admin unlock |

- Failed attempt counters reset after a successful login.
- Lockout events are recorded in the organization's audit log.
- Administrators can manually unlock accounts from the admin console under **Identity > Users**.

---

## TOTP Two-Factor Authentication

Organizations can enable TOTP (Time-based One-Time Password) two-factor authentication. When enabled, users must complete a TOTP challenge after primary authentication (whether local login or OIDC) before receiving their Sorcha JWT.

### Setup Flow

1. **Generate secret** — The user navigates to their security settings and initiates 2FA setup. The system generates a TOTP secret.
2. **Scan QR code** — The user scans the QR code with an authenticator app (e.g., Google Authenticator, Microsoft Authenticator, Authy).
3. **Verify** — The user enters the current TOTP code from their authenticator app to confirm setup.
4. **Backup codes** — The system generates a set of one-time backup codes for account recovery. The user must store these securely.

### Authentication with 2FA

When 2FA is enabled:

1. User completes primary authentication (password or OIDC).
2. The system returns a partial authentication response requiring a TOTP challenge.
3. User enters the current TOTP code from their authenticator app.
4. If the code is valid, the Sorcha JWT is issued.
5. If the user has lost their authenticator, they can use a one-time backup code instead.

### Configuration

2FA is configured at the organization level. Administrators can:
- Enable or disable 2FA requirement for all users in the organization
- View which users have completed 2FA setup
- Reset a user's 2FA configuration (e.g., if they lose their device)

---

## Passkey (WebAuthn/FIDO2) Authentication

The platform supports FIDO2/WebAuthn passkey authentication for both organizational and public users, powered by Fido2NetLib.

### Fido2 Configuration

Add to `appsettings.json`:

```json
{
  "Fido2": {
    "ServerDomain": "localhost",
    "ServerName": "Sorcha Tenant Service",
    "Origins": ["https://localhost:7080"],
    "TimestampDriftTolerance": 300000
  }
}
```

For production:

```bash
Fido2__ServerDomain="your-domain.com"
Fido2__ServerName="Sorcha Platform"
Fido2__Origins__0="https://your-domain.com"
```

### Org User Passkeys (2FA)

Organizational users can register passkeys as a second factor alongside TOTP:

1. **Register** — Authenticated user calls `POST /api/passkey/register/options` to get Fido2 creation options, then `POST /api/passkey/register/verify` with the attestation response.
2. **Login with 2FA** — After email/password login returns a `loginToken` with `available_methods: ["totp", "passkey"]`, the UI presents a method selector. For passkey: call `POST /api/auth/verify-passkey/options` with the loginToken, perform WebAuthn ceremony, then `POST /api/auth/verify-passkey`.
3. **Manage** — `GET /api/passkey/credentials` lists passkeys; `DELETE /api/passkey/credentials/{id}` revokes one.

### Public User Passkeys (Primary Auth)

Public users can use passkeys as their primary authentication method:

1. **Signup** — New user provides display name + optional email, calls `POST /api/auth/public/passkey/register/options`, completes WebAuthn ceremony, then `POST /api/auth/public/passkey/register/verify`. A PublicIdentity is created and tokens are issued.
2. **Sign-in** — Discoverable credentials flow: `POST /api/auth/passkey/assertion/options` (no email needed), WebAuthn ceremony, `POST /api/auth/passkey/assertion/verify`.
3. **Add passkey** — Authenticated user calls `POST /api/auth/public/passkey/add/options` then `POST /api/auth/public/passkey/add/verify`.

### Public User Social Login

Public users can also authenticate via social providers (Google, Microsoft, GitHub, Apple):

1. **Initiate** — `POST /api/auth/public/social/initiate` with provider name and redirect URI.
2. **Callback** — After OAuth redirect, `POST /api/auth/public/social/callback` exchanges the code for tokens.
3. **Link account** — Authenticated user can link additional social accounts via `POST /api/auth/public/social/link`.
4. **Unlink** — `DELETE /api/auth/public/social/{linkId}` (enforces last-method guard — cannot remove the only auth method).

### Auth Method Management

Authenticated public users can view and manage their auth methods:

- `GET /api/auth/public/methods` — Lists all passkeys and social links
- Last-method guard prevents removing the only remaining authentication method

### Credential Preferences

- **Discoverable credentials** (resident keys) are preferred for passwordless sign-in
- **Non-discoverable credentials** are supported as fallback
- Credential exclusion lists prevent duplicate registrations on the same device

### Security Considerations

- Passkey registration and assertion use transaction IDs to prevent replay attacks
- Signature counters are tracked and validated on each assertion
- Social login state tokens have a 10-minute lifetime
- The last-method guard ensures users always have at least one way to authenticate

---

## Next Steps

After authentication is configured:

1. **API Gateway Integration** - Configure YARP gateway for centralized auth
2. **Token Refresh** - Implement automatic token refresh on client side
3. **Multi-tenancy** - Enforce org_id isolation in data queries
4. **Audit Logging** - Log all authentication and authorization events
5. **Rate Limiting** - Implement rate limiting per user/organization

## References

- **JWT Specification**: https://jwt.io/
- **ASP.NET Core Authentication**: https://learn.microsoft.com/aspnet/core/security/authentication/
- **Azure Key Vault**: https://learn.microsoft.com/azure/key-vault/
- **OAuth 2.0 Client Credentials**: https://oauth.net/2/grant-types/client-credentials/

---

## Service Auth Configuration

All services authenticate to the Tenant Service using OAuth2 client credentials. The table below lists the complete configuration for each service. **This is the legacy/coexistence-default credential.** A deployment can additionally (or eventually instead) authenticate services with a workload certificate over mutual TLS — see [Service-to-service auth: workload certificates (mTLS, F191)](#service-to-service-auth-workload-certificates-mtls-f191) below. Until a deployment explicitly disables shared secrets, both paths work side by side.

| Service | ClientId | ClientSecret | Scopes |
|---------|----------|--------------|--------|
| Blueprint | `service-blueprint` | `blueprint-service-secret` | `wallets:sign registers:write blueprints:manage` |
| Wallet | `service-wallet` | `wallet-service-secret` | `registers:write registers:read` |
| Register | `register-service` | `register-service-secret` | `wallets:sign validator:write` |
| Validator | `validator-service` | `validator-service-secret` | `registers:write registers:read blueprints:read` |
| Peer | `service-peer` | `peer-service-secret` | `registers:write registers:read` |

These values are configured in each service's `appsettings.json` or via environment variables in `docker-compose.yml`:

```json
{
  "ServiceAuth": {
    "ClientId": "service-blueprint",
    "ClientSecret": "blueprint-service-secret",
    "Scopes": "wallets:sign registers:write",
    "TokenEndpoint": "http://tenant-service/api/service-auth/token"
  }
}
```

> **Production Note:** Replace all default secrets with strong, randomly generated values stored in Azure Key Vault or an equivalent secrets manager. Never use the default secrets shown above in production.

---

## Service-to-service auth: workload certificates (mTLS, F191)

Issue [#1420](https://github.com/Sorcha-Platform/Sorcha/issues/1420) retires the shared OAuth2
client secret as the *only* way a service proves its identity at the token mint. A per-installation
X.509 **workload certificate**, presented over mutual TLS, is now an equally valid credential.
Nothing downstream of the mint changes — same service JWT shape, same `RequireService` policy, same
scopes and tier audiences. Full design: `specs/191-mtls-workload-identity/spec.md`.

### How it's configured

**Server side (Tenant Service).** An additive Kestrel listener on port `8443` (internal-only, never
published to the host) activates only when both keys below are set:

| Key | Purpose |
|-----|---------|
| `ServiceAuth:Mtls:ServerCertificate` | Tenant's mTLS listener server certificate (PFX path or base64 PKCS#12) |
| `ServiceAuth:Mtls:ServerCertificatePassword` | PFX password |
| `ServiceAuth:Mtls:TrustBundle` | Workload CA trust bundle (path, inline PEM, or base64 PEM) |
| `ServiceAuth:Mtls:Port` | Listener port (default `8443`) |
| `ServiceAuth:DisableSharedSecrets` | Retire step only — see below (default `false`) |

**Client side (every service).** No shared-library changes are needed to call the Tenant Service;
configure a certificate instead of (or alongside) `ServiceAuth:ClientSecret`:

| Key | Purpose |
|-----|---------|
| `ServiceAuth:ClientCertificate` | This service's workload leaf certificate (PFX path or base64 PKCS#12) |
| `ServiceAuth:ClientCertificatePassword` | PFX password |
| `ServiceAuth:TrustBundle` | Workload CA trust bundle (path, inline PEM, or base64 PEM), used to authenticate the Tenant mTLS listener's server certificate |
| `ServiceAuth:MtlsTokenAddress` | Mint address to call in certificate mode (default `https://tenant-service:8443`) |

If no certificate is configured, behaviour is byte-for-byte unchanged from before this feature — the
legacy secret path, zero setup, exactly as dev/Aspire has always worked. If a certificate **is**
configured but the file is missing or unreadable, the service **fails fast at startup** — it never
silently falls back to the secret. If both a certificate and a secret are configured, the certificate
path is used and the secret is ignored for token acquisition (logged once at startup).

### Coexistence, by default

Both credential paths work side by side until an operator explicitly disables the secret path per
deployment:

- `ServiceAuth:DisableSharedSecrets=false` (default) — services authenticate by secret or by
  certificate; both succeed.
- `ServiceAuth:DisableSharedSecrets=true` (set on the **Tenant Service** only) — secret-based
  `client_credentials` requests are refused with an explicit "shared secrets disabled" error;
  certificate-based requests are unaffected. The condition is logged prominently at Tenant startup so
  a mis-flipped deployment is diagnosable from the logs, not from a wave of failed mints.

### How `sorcha-setup.sh` provisions it

A fresh install needs no extra operator action. `./scripts/sorcha-setup.sh`:

1. Generates `WORKLOAD_CERT_PASSWORD` into `.env` (same generator chain as the per-deploy service
   secrets from #1412).
2. Runs `sorcha workload-ca init --dir ./config/workload-certs --installation "$INSTALLATION_NAME"`
   — `sorcha` on `PATH` first, else the `sorchadev/cli` Docker image — creating the Workload CA, one
   leaf per service principal, and the Tenant server certificate.
3. Appends a marker-delimited, base64-encoded block of the cert env vars above to `.env` (idempotent
   — replaced on re-run). `docker compose up` then brings services up minting via certificate.

Provisioning failure degrades loudly to the shared-secret path (warn + skip) rather than
half-configuring certificate mode. `config/workload-certs/` is gitignored, joining `.env` and
`docker/certs` precedent — certificate material is never committed.

### Lifecycle commands

Certificate lifecycle is owned entirely by the CLI's `sorcha workload-ca` command group (full
reference in `src/Apps/Sorcha.Cli/README.md`):

```bash
sorcha workload-ca status                     # expiry table; exit 2 when anything is inside 30 days
sorcha workload-ca renew                      # re-issue expiring leaves under the current CA
sorcha workload-ca rotate-ca                  # new CA, bundle=[new,old] overlap, all leaves re-issued
sorcha workload-ca rotate-ca --complete       # drop the old root once every service is on the new CA
```

After `renew` or `rotate-ca`, re-run `./scripts/sorcha-setup.sh` (its keep-existing-`.env` path) to
re-encode the refreshed material into `.env`, then recreate the affected containers — services load
certificates at startup only.

### Health check

Every service exposes workload-certificate expiry via the standard health-check and metrics
surfaces: health check name **`workload-certificate`** — `Healthy` when no certificate is configured
(legacy secret mode) or when expiry is comfortably outside the warning window; `Degraded` inside
`WorkloadIdentity:ExpiryWarningDays` (default 30); `Unhealthy` once expired or unreadable. Metric:
`sorcha_workload_cert_days_to_expiry{subject}` on the `Sorcha.WorkloadIdentity` meter.

### Retiring shared secrets (per deployment)

Only after live verification — never as a big-bang cutover:

1. **Verify every service mints via certificate.** Check each service's startup/token-acquisition
   logs for certificate-mode minting, and confirm the `workload-certificate` health check is
   `Healthy` platform-wide.
2. **Set `ServiceAuth:DisableSharedSecrets=true` on the Tenant Service only**
   (`ServiceAuth__DisableSharedSecrets=true` in its environment).
3. **Recreate the Tenant Service** (`docker compose up -d --force-recreate --no-deps
   tenant-service`).
4. **Verify**: the Tenant startup log states secret-based service auth is disabled; a
   secret-presenting `client_credentials` request is refused; certificate-based minting and the
   platform's golden-path walkthrough are unaffected.
5. **Optionally remove** the 8 `*_SERVICE_SECRET` client wirings from the deployment configuration
   at leisure — they are inert once the switch is on.

**Put the flag in the deployment's own compose override, not a separate extra file.** A retire flag
carried by an additional `-f` file drops silently the moment anyone runs that deployment's documented
compose command — the Tenant Service simply comes back in coexistence mode, with no error and no log
to distinguish it. n1 hit exactly this and now carries `ServiceAuth__DisableSharedSecrets: "true"` in
`docker-compose.n1.yml` itself, so its standard three-file command cannot lose the posture.

Troubleshooting: a service failing at startup naming its client certificate means the mounted
material is missing/unreadable — check the mount and `WORKLOAD_CERT_PASSWORD` (this is deliberate
fail-fast, not a bug). A mint refused on identity mismatch means the PFX belongs to a different
service or installation — compare against `sorcha workload-ca status`. TLS handshake failures after
a CA rotation usually mean containers weren't recreated between `rotate-ca` and `--complete`, or
`--complete` ran before all services picked up new-CA leaves.

---

## Delegation Token Flow

When a service needs to act **on behalf of a user** (e.g., Blueprint Service calling Wallet Service to sign a transaction for a specific user), the platform uses a **delegation token flow**. This preserves both the service identity and the originating user identity in a single JWT.

### Flow Diagram

```
┌──────────┐         ┌───────────────────┐         ┌─────────────────┐
│  Client   │         │  Blueprint Service │         │  Tenant Service  │
│  (User)   │         │                   │         │  (Auth Authority)│
└─────┬─────┘         └────────┬──────────┘         └────────┬─────────┘
      │                        │                              │
      │  1. Request + User     │                              │
      │     Access Token       │                              │
      │───────────────────────▶│                              │
      │                        │                              │
      │                        │  2. Acquire service token    │
      │                        │     via ServiceAuthClient    │
      │                        │─────────────────────────────▶│
      │                        │                              │
      │                        │  3. Service token returned   │
      │                        │◀─────────────────────────────│
      │                        │                              │
      │                        │  4. POST /api/service-auth/  │
      │                        │     token/delegated          │
      │                        │     { serviceToken,          │
      │                        │       userAccessToken }      │
      │                        │─────────────────────────────▶│
      │                        │                              │
      │                        │  5. Validate both tokens,    │
      │                        │     issue delegation JWT     │
      │                        │◀─────────────────────────────│
      │                        │                              │
      │                        │                              │
      ┌────────────────────────┴──────────────────────────────┘
      │
      │  Delegation JWT claims include:
      │    token_type = "service"
      │    client_id  = "service-blueprint"
      │    delegated_user_id = "<original-user-id>"
      │    delegated_user_email = "<original-user-email>"
      │    org_id = "<user's-org-id>"
      │    scope  = "<service's scopes>"
      └──────────────────────────────────────────────────────

      ┌──────────────────────┐         ┌──────────────────┐
      │  Blueprint Service   │         │  Target Service   │
      │                      │         │ (Wallet/Register) │
      └──────────┬───────────┘         └────────┬──────────┘
                 │                               │
                 │  6. Call with delegation       │
                 │     token in Authorization     │
                 │     header                     │
                 │──────────────────────────────▶│
                 │                               │
                 │  7. Target validates token:    │
                 │     - token_type=service ✓     │
                 │     - delegated_user_id ✓      │
                 │     - RequireDelegatedAuthority│
                 │       policy satisfied         │
                 │                               │
                 │  8. Response                   │
                 │◀──────────────────────────────│
```

### Step-by-Step

1. **User sends request** to Blueprint Service with their user access token in the `Authorization` header.
2. **Blueprint acquires a service token** by calling `ServiceAuthClient` with its own client credentials (`service-blueprint` / `blueprint-service-secret`).
3. **Tenant Service returns** a service token to Blueprint.
4. **Blueprint POSTs both tokens** (the service token and the user's access token) to `POST /api/service-auth/token/delegated` on the Tenant Service.
5. **Tenant Service validates both tokens**, confirms they are not expired or revoked, and issues a **delegation JWT** that carries both the service identity (`token_type=service`, `client_id`) and the user identity (`delegated_user_id`, `delegated_user_email`, `org_id`).
6. **Blueprint calls the target service** (Wallet or Register) using the delegation token in the `Authorization` header.
7. **Target service validates** the delegation token against the `RequireDelegatedAuthority` policy, which requires both `token_type=service` AND a `delegated_user_id` claim to be present.
8. **Target service processes the request**, knowing both which service is calling and on whose behalf.

### Example: Delegation Token Request

```http
POST https://tenant.sorcha.io/api/service-auth/token/delegated
Content-Type: application/json
Authorization: Bearer <service-token>

{
  "userAccessToken": "<user-access-token>"
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenType": "Bearer",
  "expiresIn": 3600
}
```

### Delegation Token Claims

```json
{
  "sub": "service-principal-id",
  "client_id": "service-blueprint",
  "token_type": "service",
  "delegated_user_id": "user-guid-here",
  "delegated_user_email": "user@organization.com",
  "org_id": "organization-id-guid",
  "scope": ["wallets:sign", "registers:write"],
  "iss": "urn:sorcha:localhost",
  "aud": "localhost:service",
  "exp": 1735891200,
  "iat": 1735887600
}
```

---

## Token Revocation

The platform supports token revocation through the `ITokenRevocationStore` interface, allowing services to invalidate tokens before their natural expiry (e.g., user logout, compromised credentials, permission changes).

### Redis-Backed Revocation

Services register Redis-backed revocation checking during startup:

```csharp
// In Program.cs or service registration
builder.Services.AddTokenRevocation(options =>
{
    options.UseRedis(builder.Configuration.GetConnectionString("Redis"));
});
```

This registers an implementation of `ITokenRevocationStore` backed by Redis, where revoked token IDs (`jti` claims) are stored with a TTL matching the token's remaining lifetime. The JWT Bearer authentication middleware checks the revocation store on every request, rejecting tokens whose `jti` appears in the store.

### Revoking a Token

```csharp
// Inject ITokenRevocationStore
await tokenRevocationStore.RevokeAsync(tokenId, expiration);
```

### Key Points

- Revocation entries automatically expire from Redis when the original token would have expired, keeping storage bounded.
- The revocation check adds minimal latency (~1ms) since it is a single Redis `EXISTS` call.
- For high-availability deployments, the Redis instance used for revocation should be replicated.

---

## Authorization Policies (Consolidated)

The following table consolidates all authorization policies used across the platform. Each policy defines the claims or conditions required for access.

| Policy | Required Claims / Conditions | Description |
|--------|------------------------------|-------------|
| `RequireAuthenticated` | Any valid JWT | Any authenticated user, regardless of role or token type |
| `RequireService` | `token_type=service` | Service-to-service operations only; rejects user tokens |
| `RequireOrganizationMember` | `org_id` claim present | User must belong to an organization |
| `RequireAdministrator` | `role=Administrator` | User must have the Administrator role |
| `CanManageWallets` | `org_id` OR `token_type=service` | Create, list, and configure wallets (org members or services) |
| `CanManageBlueprints` | (`token_type=service` AND `:service` aud) OR (`org_id` AND `:platform` aud) | Authoring; consumer-tier tokens (which carry `org_id` under F136) are refused — Feature 147 |
| `CanRecoverSystemWallet` | (`token_type=service` AND `:service` aud) OR (`role=Administrator`/`SystemAdmin` AND `:platform` aud) | Wallet Service system-wallet BIP39 import (genesis ceremony / service automation) — Feature 147 |
| `CanManageRegisters` | `org_id` + (`role=Administrator` OR `role=SystemAdmin`) | Create and manage registers (tightened from org_id-only) |
| `CanCreateSystemRegisters` | `org_id=00000000-0000-0000-0000-000000000001` + `role=SystemAdmin` | Set register purpose to System (SystemAdmin org only) |
| `RequireDelegatedAuthority` | `token_type=service` AND `delegated_user_id` present | Service acting on behalf of a user; both identities must be present |
| `CanWriteRegisters` | `registers:write` in `scope` claim | Write access to register ledgers (submit transactions, publish) |

### Policy Usage by Service

| Service | Policies Used |
|---------|---------------|
| Blueprint | `CanManageBlueprints`, `CanExecuteBlueprints`, `CanPublishBlueprints`, `RequireService` |
| Wallet | `CanManageWallets`, `CanUseWallet`, `RequireService`, `RequireDelegatedAuthority` |
| Register | `CanManageRegisters`, `CanCreateSystemRegisters`, `CanSubmitTransactions`, `CanReadTransactions`, `RequireService`, `CanWriteRegisters` |
| Validator | `RequireService`, `CanWriteRegisters` |
| Peer | `RequireAuthenticated`, `CanManagePeers`, `RequireService` |

### Applying Policies to Endpoints

```csharp
// Minimal API example
app.MapPost("/api/registers/{id}/transactions", SubmitTransaction)
    .RequireAuthorization("CanSubmitTransactions");

// Delegation-protected endpoint
app.MapPost("/api/wallets/{id}/sign", SignWithWallet)
    .RequireAuthorization("RequireDelegatedAuthority");
```

---

**Status**: ✅ AUTH-002 Complete | OIDC (054) | PassKey & Social Login (055) | Platform Identity (058) documented
**Last Updated**: 2026-03-16
**Version**: 1.5

---

## Feature 150 — Unified Account Security (2FA channels, floor rule, SMS config)

The **Security** home (`/app/security`, `/wallet/security`) consolidates sign-in methods, two-factor channels, and recovery. Server-authoritative model in `AssurancePolicy` (Tenant Service).

**Assurance tiers & floor rule.** `AuthAssuranceTier` = `Basic < Strong < Strongest`. A step-up proof authorises a destructive/downgrade op **iff `proofTier >= RequiredProofTier(operation, target)`** plus the last-sign-in-method floor. Tiers: Passkey=Strongest; TOTP/Re-OAuth=Strong; **Password/Email-OTP/SMS-OTP=Basic** (T061: a password is a phishable knowledge factor). A Basic proof can never disable TOTP or remove a passkey. Enforced in `AuthChallengeService` (initiate offers only floor-eligible proofs; verify → `403 proof_tier_insufficient`). The ambiguous `RemoveAuthMethod` carries a `TargetMethodKind` (null → fail-safe Strongest).

**Always-notify.** Every security mutation writes an F118 inbox entry + a Sorcha-branded `security-change` email (both best-effort).

**2FA channels.**
- **Authenticator (TOTP)** — Strong; org-scoped `TotpConfiguration`.
- **Email OTP (US2)** — Basic; account-wide `PlatformUserTwoFactor.EmailOtpEnabled`. `POST /api/me/2fa/email/{enable,verify}`, `DELETE /api/me/2fa/email`. Codes via the F112 `twofactor-code` template; single-use, 10-min, 5-attempt, send-cooldown (`ServerSentOtpService` over Redis GETDEL).
- **SMS OTP (US3) — config-gated.** Enabled only when `Sms:AcsConnectionString` is set (registers `ISmsSender` + the SMS channel). `POST /api/me/2fa/sms/{phone,phone/verify,enable}`, `DELETE /api/me/2fa/sms` — **404 when unconfigured**. Capturing a new number clears verification + disables SMS. The concrete provider HTTP send in `AcsSmsSender` is an operator integration point.

**Login 2FA.** After the first factor, `LoginService` requires 2FA if any of TOTP / passkey / email-OTP / SMS-OTP is enrolled, offering methods strongest-first; `POST /api/auth/verify-2fa` accepts `method=email|sms`; `POST /api/auth/login/2fa/send-email` is the "use another method" resend.

**Config.** `Sms:AcsConnectionString`, `Sms:FromNumber` to enable SMS. No SMS config ⇒ the option is entirely absent.

**Metrics** (`Sorcha.Tenant.Auth` meter): `sorcha_auth_otp_send_total{channel,outcome}`, `sorcha_auth_otp_verify_total{channel,outcome}`, `sorcha_auth_floor_rejected_total{method,scope}`.
