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
- **Authentication**: JWT Bearer validation
- **Authorization Policies**:
  - `CanManageRegisters` - Create and configure registers
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
    "Issuer": "https://tenant.sorcha.io",
    "Audience": "https://api.sorcha.io",
    "SigningKey": "your-secret-key-min-32-characters-REPLACE-THIS-IN-PRODUCTION",
    "AccessTokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeHours": 24,
    "ServiceTokenLifetimeHours": 8,
    "ClockSkewMinutes": 5,
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ValidateIssuerSigningKey": true,
    "ValidateLifetime": true
  }
}
```

### Environment Variables (Recommended for Production)

```bash
# JWT Configuration
export JwtSettings__Issuer="https://tenant.your-domain.com"
export JwtSettings__Audience="https://api.your-domain.com"
export JwtSettings__SigningKey="<strong-random-key-from-azure-key-vault>"
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

### User Tokens
```json
{
  "sub": "user-id-guid",
  "email": "user@organization.com",
  "name": "User Name",
  "org_id": "organization-id-guid",
  "role": "Administrator",
  "token_type": "user",
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io",
  "exp": 1735891200,
  "iat": 1735887600
}
```

### Service Tokens
```json
{
  "sub": "service-principal-id",
  "client_id": "blueprint-service",
  "org_id": "organization-id-guid",
  "token_type": "service",
  "scope": "blueprints:write registers:read",
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io",
  "exp": 1735920000,
  "iat": 1735887600
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
| `CanManageBlueprints` | Create, update, delete blueprints | `org_id` OR `token_type=service` |
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
| `CanManageRegisters` | Create registers | `org_id` OR `token_type=service` |
| `CanSubmitTransactions` | Submit transactions | Authenticated user |
| `CanReadTransactions` | Query transactions | Authenticated user |
| `RequireService` | Notifications | `token_type=service` |

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
- [ ] All services have same `JwtSettings:SigningKey`
- [ ] All services have same `JwtSettings:Issuer`
- [ ] All services have same `JwtSettings:Audience`
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

All services authenticate to the Tenant Service using OAuth2 client credentials. The table below lists the complete configuration for each service.

| Service | ClientId | ClientSecret | Scopes |
|---------|----------|--------------|--------|
| Blueprint | `service-blueprint` | `blueprint-service-secret` | `wallets:sign registers:write` |
| Wallet | `service-wallet` | `wallet-service-secret` | `validators:notify` |
| Register | `service-register` | `register-service-secret` | `validators:notify` |
| Validator | `service-validator` | `validator-service-secret` | `registers:write registers:read` |
| Peer | `service-peer` | `peer-service-secret` | `registers:read` |

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
  "scope": "wallets:sign registers:write",
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io",
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
| `CanManageBlueprints` | `org_id` OR `token_type=service` | Create, update, and delete blueprints (org members or services) |
| `RequireDelegatedAuthority` | `token_type=service` AND `delegated_user_id` present | Service acting on behalf of a user; both identities must be present |
| `CanWriteRegisters` | `registers:write` in `scope` claim | Write access to register ledgers (submit transactions, publish) |

### Policy Usage by Service

| Service | Policies Used |
|---------|---------------|
| Blueprint | `CanManageBlueprints`, `CanExecuteBlueprints`, `CanPublishBlueprints`, `RequireService` |
| Wallet | `CanManageWallets`, `CanUseWallet`, `RequireService`, `RequireDelegatedAuthority` |
| Register | `CanManageRegisters`, `CanSubmitTransactions`, `CanReadTransactions`, `RequireService`, `CanWriteRegisters` |
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

**Status**: ✅ AUTH-002 Complete | OIDC (054) | PassKey & Social Login (055) documented
**Last Updated**: 2026-03-10
**Version**: 1.4
