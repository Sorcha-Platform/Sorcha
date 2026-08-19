# Sorcha Tenant Service

**Version**: 2.0.0
**Status**: 97% Complete
**Framework**: .NET 10.0
**Architecture**: Microservice

---

## Overview

The **Sorcha Tenant Service** is a multi-tenant authentication, authorization, and organization management service that acts as a Secure Token Service (STS) for the Sorcha platform. It enables organizations to bring their own identity providers via OIDC federation, supports local email/password authentication with TOTP 2FA, and provides comprehensive organization administration capabilities.

### Key Features

- **Multi-Organization Support**: Each organization has its own identity provider configuration, subdomain, and user management
- **OIDC Identity Federation**: Integrate with Microsoft Entra ID, Google, Okta, Apple, Amazon Cognito, or any OIDC-compliant provider with automatic discovery
- **Full Token Exchange**: External IDP tokens are exchanged for Sorcha JWTs; downstream services never see external tokens
- **Local Authentication**: Email/password login with NIST-compliant password policy and HIBP breach list checking
- **TOTP Two-Factor Authentication**: Authenticator app-based 2FA with backup codes
- **Self-Registration**: Public organizations can allow users to self-register with email verification
- **PassKey Authentication**: FIDO2/WebAuthn passkey authentication — org user 2FA (register + verify as second factor) and public user primary auth (signup, sign-in, method management)
- **Server-Rendered Auth Pages**: Razor Pages for login, signup, logout, OAuth/OIDC callbacks, email verification, and password reset — eliminates ~15MB WASM download for unauthenticated users
- **Service-to-Service Authentication**: OAuth2 client credentials flow for microservice communication
- **JWT Token Issuance**: RS256-signed tokens with configurable lifetimes
- **Token Revocation**: Redis-backed token blacklist with automatic TTL cleanup
- **Multi-Tenant Data Isolation**: PostgreSQL schema-based tenant isolation
- **Organization Invitations**: Invite users by email with configurable roles and expiry
- **Domain Restrictions**: Restrict auto-provisioning to specific email domains
- **Custom Domain Support**: Organizations can configure custom domains with CNAME verification
- **Consolidated Roles**: 5 roles (SystemAdmin, Administrator, Designer, Auditor, Member)
- **User Lifecycle Management**: Unlock, suspend, reactivate, and role change operations
- **Admin Dashboard**: Aggregated KPIs including user counts, role distribution, and login statistics
- **Audit Logging**: Comprehensive audit trail with configurable retention (1-120 months)
- **Rate Limiting & Progressive Lockout**: 5 fails=5min, 10=30min, 15=24h, 25=admin unlock
- **Email Verification**: Required for all users; trusts IDP `email_verified` claim for OIDC users
- **Multi-Tenant URL Resolution**: 3-tier URL routing (path, subdomain, custom domain)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Sorcha Tenant Service                    │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌───────────────┐  ┌────────────────┐  │
│  │   Auth API   │  │   Admin API   │  │   Audit API    │  │
│  │              │  │               │  │                │  │
│  │ • OIDC SSO   │  │ • Org Mgmt    │  │ • Log Query    │  │
│  │ • Local Auth │  │ • IDP Config  │  │ • Retention    │  │
│  │ • TOTP 2FA   │  │ • User Mgmt   │  │ • Dashboard    │  │
│  │ • PassKey    │  │ • Invitations │  │                │  │
│  │ • Token Mgmt │  │ • Domains     │  │                │  │
│  └──────────────┘  └───────────────┘  └────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │           Server-Rendered Auth Pages (Razor)         │  │
│  │  /auth/login    /auth/signup    /auth/logout         │  │
│  │  /auth/social/callback   /auth/oidc/callback         │  │
│  │  /auth/verify-email  /auth/reset-password            │  │
│  │  /auth/error                                         │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │             Service Layer                            │  │
│  │  • OrganizationService  • TokenService               │  │
│  │  • OidcExchangeService  • OidcProvisioningService    │  │
│  │  • IdpConfigurationService • TotpService             │  │
│  │  • InvitationService    • CustomDomainService        │  │
│  │  • PasswordPolicyService • EmailVerificationService  │  │
│  │  • DashboardService     • PassKeyService             │  │
│  │  • PublicUserService    • LoginService               │  │
│  │  • RegistrationService  • PasswordResetService       │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │             Data Layer                               │  │
│  │  • EF Core (PostgreSQL)  • Redis Cache               │  │
│  └──────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
           │                    │                  │
           ▼                    ▼                  ▼
    ┌──────────────┐    ┌─────────────┐   ┌──────────────┐
    │  PostgreSQL  │    │    Redis    │   │ External IDP │
    │  (Multi-     │    │  (Revoke    │   │ (Azure/AWS/  │
    │   tenant)    │    │   List)     │   │  Google)     │
    └──────────────┘    └─────────────┘   └──────────────┘
```

### Platform Identity Layer (Feature 058)

Authentication happens at the **platform level** via `PlatformUser`, while authorisation is scoped per-org via `UserIdentity`.

| Layer | Schema | Purpose | Entity |
|-------|--------|---------|--------|
| Platform | public | Authentication, cross-org anchor | PlatformUser |
| Organisation | org_{id} | Authorisation, org-scoped role | UserIdentity |

**Key entities:**
- `PlatformUser` — Cross-org identity with email uniqueness, social logins, passkey credentials
- `PlatformSocialLogin` — OAuth provider links (Google, GitHub, Microsoft, Apple)
- `PlatformUserOrgMembership` — Maps platform users to org-scoped roles
- `PlatformSettings` — Platform governance (public org enable/disable, max orgs per user)

**Well-known organisations:**
- System Admin Org (`00000000-0000-0000-0000-000000000001`) — Platform governance
- Public Org (`00000000-0000-0000-0000-000000000002`) — Social login + email/password signup

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Docker Desktop** - For PostgreSQL and Redis
- **Git** - Version control

### 1. Clone and Navigate

```bash
cd C:\Projects\Sorcha
```

### 2. Set Up Local Secrets

**Option A: Automated Setup (Recommended)**

```bash
# Windows (PowerShell)
.\specs\001-tenant-auth\setup-local-secrets.ps1

# macOS/Linux (Bash)
chmod +x ./specs/001-tenant-auth/setup-local-secrets.sh
./specs/001-tenant-auth/setup-local-secrets.sh
```

**Option B: Manual Setup**

```bash
# Initialize User Secrets
dotnet user-secrets init --project src/Services/Sorcha.Tenant.Service

# Generate and set JWT signing key (see secrets-setup.md)
openssl genrsa -out jwt_private.pem 4096
dotnet user-secrets set "JwtSettings:SigningKey" "$(cat jwt_private.pem)" --project src/Services/Sorcha.Tenant.Service

# Set database password
dotnet user-secrets set "ConnectionStrings:Password" "dev_password123" --project src/Services/Sorcha.Tenant.Service
```

For detailed secrets management, see the Authentication Setup guide in `docs/guides/AUTHENTICATION-SETUP.md`.

### 3. Start Dependencies

```bash
# Start PostgreSQL and Redis
docker-compose up -d postgres redis
```

### 4. Run Database Migrations

```bash
cd src/Services/Sorcha.Tenant.Service
dotnet ef database update
```

### 5. Run the Service

```bash
dotnet run
```

Service will start at:
- **HTTPS**: https://localhost:7080
- **HTTP**: http://localhost:7081
- **Scalar API Docs**: https://localhost:7080/scalar

---

## Configuration

### appsettings.json Structure

```json
{
  "ConnectionStrings": {
    "TenantDatabase": "Host=localhost;Port=5432;Database=sorcha_tenant;Username=sorcha_user;Password=placeholder"
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "SorchaTenant:"
  },
  "JwtSettings": {
    "Issuer": "https://localhost:7080",
    "Audience": ["https://localhost:7081"],
    "AccessTokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeMinutes": 1440
  },
  "Fido2": {
    "ServerDomain": "localhost",
    "ServerName": "Sorcha Tenant Service"
  },
  "EmailSettings": {
    "SmtpHost": "localhost",
    "SmtpPort": 587,
    "SmtpUser": "",
    "SmtpPassword": "",
    "FromAddress": "noreply@sorcha.example.com",
    "FromName": "Sorcha Platform",
    "EnableSsl": true
  },
  "OidcSettings": {
    "CallbackBaseUrl": "https://localhost:7080",
    "StateTokenLifetimeMinutes": 10,
    "LoginTokenLifetimeMinutes": 5
  }
}
```

### New Configuration Settings (054)

| Section | Key | Default | Purpose |
|---------|-----|---------|---------|
| `EmailSettings:SmtpHost` | — | SMTP server hostname |
| `EmailSettings:SmtpPort` | 587 | SMTP server port |
| `EmailSettings:SmtpUser` | — | SMTP authentication username |
| `EmailSettings:SmtpPassword` | — | SMTP authentication password (use secrets) |
| `EmailSettings:FromAddress` | — | Sender email address |
| `EmailSettings:FromName` | Sorcha Platform | Sender display name |
| `EmailSettings:EnableSsl` | true | Enable TLS/SSL for SMTP |
| `OidcSettings:CallbackBaseUrl` | — | Base URL for OIDC callback redirects |
| `OidcSettings:StateTokenLifetimeMinutes` | 10 | OIDC state token expiry |
| `OidcSettings:LoginTokenLifetimeMinutes` | 5 | 2FA login token expiry |
| `Fido2:ServerDomain` | localhost | WebAuthn relying party domain |
| `Fido2:ServerName` | Sorcha Tenant Service | WebAuthn display name |

### Environment Variables

For production deployment, use environment variables:

```bash
ConnectionStrings__TenantDatabase="Host=prod-db;Port=5432;..."
Redis__ConnectionString="prod-redis:6379"
JwtSettings__Issuer="https://api.sorcha.example.com"
AzureKeyVault__Enabled="true"
AzureKeyVault__VaultUri="https://sorcha-kv.vault.azure.net/"
EmailSettings__SmtpHost="smtp.example.com"
EmailSettings__SmtpPassword="your-smtp-password"
EmailSettings__FromAddress="noreply@sorcha.example.com"
OidcSettings__CallbackBaseUrl="https://api.sorcha.example.com"
```

### Service Principal Secrets (Per-Deploy, issue #1412)

`DatabaseInitializer.SeedServicePrincipalsAsync` seeds the 8 in-platform service principals
(Blueprint, Wallet, Register, Peer, Validator, Tenant, HAIP, Verifier). Each principal's client
secret is resolved via `ServicePrincipalSecretResolver.Resolve` in this order:

1. `Seed:ServicePrincipals:{clientId}` (env `Seed__ServicePrincipals__{clientId}`) — the per-deploy
   secret generated by `scripts/sorcha-setup.sh` into `.env` and injected into both this key and the
   matching client's `ServiceAuth__ClientSecret` in `docker-compose.yml`, so client and server always
   agree. Wins in every environment. In every real deployment this is always present: `docker-compose.yml`
   guards both the client-side `ServiceAuth__ClientSecret` lines and these seed lines with
   `${VAR:?...}`, so a bare `docker compose up` without a generated `.env` refuses to start
   (run `scripts/sorcha-setup.sh` first).
2. **Production/Staging** with no configured secret **fails closed** (`InvalidOperationException` at
   startup) rather than seeding a random secret the client services can never learn — the prior
   silent-break bug this resolver replaces.
3. Any other environment (e.g. Testing) with no configured secret generates a fresh random secret —
   only reachable by unit/integration tests that seed without config, which are self-contained and
   need no cross-service agreement.

The resolved source (`Configured` / `Generated`) is logged; the secret value itself never is.

### Admin Password Fail-Closed (issue #1409)

`DatabaseInitializer.SeedAdminUserAsync` seeds the initial `admin@sorcha.local` `PlatformUser`.
Its password is resolved via `AdminPasswordResolver.Resolve` — the same shape as
`ServicePrincipalSecretResolver` above — in this order:

1. `Seed:AdminPassword` (env `Seed__AdminPassword`) — an operator-configured, per-deploy password.
   Wins in every environment.
2. **Production/Staging** with no configured password **fails closed** (`InvalidOperationException`
   at startup) rather than seeding the well-known `Dev_Pass_2025!` literal on an
   internet-reachable node — the known-credential gap this resolver closes.
3. Any other environment (Development, Testing, or unset) with no configured password uses the
   committed `DatabaseInitializer.DefaultAdminPassword` (`Dev_Pass_2025!`) — the unchanged local
   dev/test convenience. `docker-compose.yml` runs Tenant Service with
   `ASPNETCORE_ENVIRONMENT=Development`, so this remains the default for local/demo deployments.

The resolved source (`Configured` / `DevDefault`) is logged. The dev-default password is logged
alongside it (it is a published convenience literal); a configured, operator-set password never is.

### Workload-Identity Service Auth (Feature 191 / #1420)

Service-to-service authentication is moving from a shared OAuth2 client secret to a per-installation
X.509 workload certificate presented over mutual TLS. The certificate replaces the secret at the
token mint **only** — everything downstream (service JWT shape, `RequireService`, scopes, tier
audiences) is unchanged. Both credential paths coexist by default; a deployment retires the shared
secret only after live verification (runbook in `docs/guides/AUTHENTICATION-SETUP.md`).

**mTLS listener.** An additive Kestrel listener on port **8443** (internal-only — never published to
the host) activates when both `ServiceAuth:Mtls:ServerCertificate` and `ServiceAuth:Mtls:TrustBundle`
are configured. Client certificates are **required** and chain-validated against the installation's
Workload CA bundle at the TLS handshake; requests without a valid client cert never reach the
handler.

**Mint contract.** `POST /api/internal/service-auth/token` and
`POST /api/internal/service-auth/token/delegated` accept `client_secret` as **optional** when the
request arrives on the mTLS listener with a valid workload certificate — the certificate itself is
the credential. The handler additionally requires the certificate's SPIFFE URI SAN
(`spiffe://{installation}/service/{client_id}`, where `{installation}` is
`JwtSettings:InstallationName` — the same source as the JWT issuer/audiences) to exactly match the
requested `client_id`; a mismatch is refused and logged with both identities. On the plaintext
internal listener `client_secret` remains required — secretless requests are refused there.
`POST /api/internal/service-auth/rotate-secret` is unchanged and becomes inert once a deployment
disables shared secrets.

**Coexistence switch.** `ServiceAuth:DisableSharedSecrets=true` refuses secret-based
`client_credentials` requests platform-wide with an explicit "shared secrets disabled" error, while
certificate-based minting continues unaffected; off (the default) both paths succeed. The switch is
logged prominently at startup so a mis-flipped deployment is diagnosable from startup logs.

**Server-side config keys:**

| Key | Purpose | Default |
|-----|---------|---------|
| `ServiceAuth:Mtls:ServerCertificate` | Identity-service mTLS listener certificate (PFX path or base64 PKCS#12) | unset (listener inactive) |
| `ServiceAuth:Mtls:ServerCertificatePassword` | PFX password | unset |
| `ServiceAuth:Mtls:TrustBundle` | Workload CA trust bundle (path, inline PEM, or base64 PEM) | unset |
| `ServiceAuth:Mtls:Port` | mTLS listener port | `8443` |
| `ServiceAuth:DisableSharedSecrets` | Refuse secret-based service auth platform-wide (retire step only) | `false` |

Certificate lifecycle (init / status / renew / rotate-ca) is CLI-owned — see the "workload-ca"
section of `src/Apps/Sorcha.Cli/README.md`. Full config/delivery and mint contracts:
`specs/191-mtls-workload-identity/contracts/`.

---

## API Endpoints

### Authentication API (`/api/auth`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/login` | POST | Login with email and password (returns 2FA challenge if enabled) |
| `/api/auth/verify-2fa` | POST | Verify TOTP code or backup code to complete login. Accepts optional `tier` field (`"consumer"`) — forces Consumer-tier token for wallet sign-in (spec 136). |
| `/api/auth/register` | POST | Self-register with email/password (public orgs only) |
| `/api/auth/logout` | POST | Logout and revoke current token |
| `/api/auth/me` | GET | Get current authenticated user info |
| `/api/auth/token/refresh` | POST | Refresh access token |
| `/api/auth/token/revoke` | POST | Revoke a specific token |
| `/api/auth/token/introspect` | POST | Introspect a token (service-to-service) |
| `/api/auth/token/revoke-user` | POST | Revoke all tokens for a user (admin) |
| `/api/auth/token/revoke-organization` | POST | Revoke all tokens for an organization (admin) |

### OIDC Authentication API (`/api/auth`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/oidc/initiate` | POST | Initiate OIDC login flow (generates authorization URL) |
| `/api/auth/callback/{orgSubdomain}` | GET | OIDC callback - exchange authorization code for Sorcha JWT |
| `/api/auth/oidc/complete-profile` | POST | Complete user profile after OIDC provisioning |
| `/api/auth/verify-email` | POST | Verify email address with token |
| `/api/auth/resend-verification` | POST | Resend email verification (rate limited: 3/hour) |

### Organisation Switching

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | `/api/auth/me/organizations` | List user's org memberships | Authenticated |
| POST | `/api/auth/switch-org` | Switch active org (re-issues JWT) | Authenticated |

### Org User PassKey 2FA API (`/api/passkey`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/passkey/register/options` | POST | Get passkey registration options for org user (authenticated) |
| `/api/passkey/register/verify` | POST | Complete passkey registration for org user |
| `/api/passkey/credentials` | GET | List org user's passkey credentials |
| `/api/passkey/credentials/{id}` | DELETE | Delete/revoke an org user's passkey credential |

### Org User PassKey 2FA Login (`/api/auth`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/verify-passkey/options` | POST | Get passkey assertion options for 2FA verification during login |
| `/api/auth/verify-passkey` | POST | Verify passkey assertion to complete 2FA login |

### Public User Passkey API (`/api/auth/public/passkey`) — Anonymous, Rate-Limited

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/public/passkey/register/options` | POST | Create PlatformUser + generate FIDO2 registration options for public signup |
| `/api/auth/public/passkey/register/verify` | POST | Verify attestation, create UserIdentity in public org, issue JWT |

### Public User Passkey Sign-in (`/api/auth/passkey`) — Anonymous, Rate-Limited

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/passkey/assertion/options` | POST | Generate discoverable passkey assertion options (optional email filter) |
| `/api/auth/passkey/assertion/verify` | POST | Verify assertion, resolve user, issue JWT. Accepts optional `tier` field (`"consumer"`) — safe downgrade for wallet sign-in (spec 136). |

### Public User Social Login (`/api/auth/social` and `/api/auth/public/social`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/social/providers` | GET | List configured providers (anonymous). Returns `{"providers":["google",…]}`. Drives conditional "Continue with…" buttons on the citizen wallet PWA sign-in screen. Rate-limited (`platform-auth`). |
| `/api/auth/public/social/initiate` | POST | Initiate social login/signup with provider (Google, Microsoft, GitHub, Apple). Optional `surface` field: `"wallet"` routes the callback into the citizen wallet PWA (Consumer-tier mint + `/wallet/#…` redirect + login-only enforcement); null/`"app"` keeps the default web flow. |
| `/api/auth/public/social/callback` | POST | Handle OAuth callback and issue tokens |

**Browser callback URL:** providers redirect to the Razor page at
`/auth/social/callback` (single canonical path per environment, see
[`docs/guides/SOCIAL-LOGIN-SETUP.md`](../../../docs/guides/SOCIAL-LOGIN-SETUP.md)).
The page resolves the provider from the cached state — provider is NOT a
query parameter — and applies the strict link policy added in
feature 115 before issuing tokens.

**Wallet surface (`surface:"wallet"`) behaviour.** When `surface:"wallet"` is set on the initiate request, the `SocialCallback` Razor page: (1) mints a Consumer-tier token; (2) redirects to `/wallet/#token=…&refresh=…&expires_in=…` so the PWA's `auth-fragment.js` captures the tokens before Blazor boots; (3) enforces login-only — an unknown social identity is refused (`SocialLoginRefusal.NoExistingAccount`) and the user is redirected to `/wallet/signin?authError=no_account`. No `PlatformUser` is created.

**Strict link policy (feature 115).** Both signup and link flows refuse
when verification is missing on either side:

- New user creation requires the provider to assert
  `email_verified=true`. Otherwise → refusal with
  `provider_unverified` reason.
- Cross-method linking (existing Sorcha account, new social provider
  with the same email) requires *both* the provider's claim and the
  existing account's `EmailVerified` to be true. Otherwise → refusal
  with `existing_unverified` reason.
- Returning users (provider+sub already linked) are NOT re-checked
  against verification gates — trust is established at link time.

**Provider visibility.** Signup and login pages render a "Continue
with..." button only for providers configured with non-empty
`ClientId` and `ClientSecret`. Configuration shape:

```yaml
SocialProviders__0__Name: Google
SocialProviders__0__ClientId: ${GOOGLE_OAUTH_CLIENT_ID}
SocialProviders__0__ClientSecret: ${GOOGLE_OAUTH_CLIENT_SECRET}
```

Adding a new provider requires only configuration + service restart.
See [`docs/guides/SOCIAL-LOGIN-SETUP.md`](../../../docs/guides/SOCIAL-LOGIN-SETUP.md)
for the operator runbook.

**Telemetry.** Refusals emit
`sorcha_social_login_refusal_total{provider, reason}` on the
`Sorcha.Tenant` meter. PII is never tagged on these metrics; the
matching log line carries a hash-based redacted email tag.

**Step-up social account linking (Feature 168).**
When a social sign-in's verified email matches an existing verified account that isn't yet linked to
the incoming `(provider, subject)`, the callback returns `{"outcome":"LinkRequired","linkPendingToken":"…"}`
instead of issuing a session. No `PlatformSocialLogin` row is created. The client must complete
the three-step pre-session challenge flow below.

#### Step-Up Social Account Linking Endpoints (`/api/auth/social/link`)

All three endpoints are unauthenticated and rate-limited with `platform-auth`. The `linkPendingToken`
(HMAC-SHA256, 5-minute TTL, HKDF-derived key) acts as the principal.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/social/link/challenge/initiate` | POST | Begin a `LinkSocial` step-up challenge. Body: `{"linkPendingToken":"…","preferredMethod":null}`. Returns the offered challenge method and payload. |
| `/api/auth/social/link/challenge/verify` | POST | Submit the proof. Body: `{"linkPendingToken":"…","method":"totp","proof":{…}}`. Returns a single-use `X-Auth-Challenge` token on success. |
| `/api/auth/social/link/confirm` | POST | Redeem both tokens, link the social identity, and issue a session. Body: `{"linkPendingToken":"…"}`. Header: `X-Auth-Challenge: ch_…`. |

**Floor rule.** `ScopedOperation.LinkSocial` requires `AuthAssuranceTier.Strong` (Feature 168, T022).
TOTP, passkey, and linked-social re-auth satisfy the floor. Bare password (Basic) is always rejected.
Accounts with only a password and no TOTP/passkey enrolled receive `NoMethodAvailable` at initiate (400).

**Link-confirm error codes.** `401` — missing/invalid/expired link-pending token or challenge token.
`403` — challenge bound to a different account or wrong operation. `409` — social identity already linked
to a different account. On success, the same session token shape as a normal social sign-in is returned.

**Key material.** The token's signing key is HKDF-SHA256 derived from `JwtSettings:SigningKey` with
info label `sorcha:tenant:link-pending-hmac:v1`, isolated from all other key derivations.

**Public-organisation seed.** A fresh database can be seeded with
`PublicOrgEnabled=true` via
`PlatformSettings__SeedPublicOrgEnabled=true` (feature 115 FR-019).
Seed-time only — admin UI/API toggles win on subsequent boots.

### Public User Auth Method Management (`/api/auth/public`) — Authenticated

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/public/methods` | GET | List authenticated user's passkeys and social links |
| `/api/auth/public/social/link` | POST | Link a social account to existing user |
| `/api/auth/public/social/{linkId}` | DELETE | Unlink a social account (enforces last-method guard) |
| `/api/auth/public/passkey/add/options` | POST | Get options for adding a passkey to existing account |
| `/api/auth/public/passkey/add/verify` | POST | Complete adding a passkey to existing account |

### Organization API (`/api/organizations`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations` | POST | Create a new organization |
| `/api/organizations` | GET | List organizations (admin) |
| `/api/organizations/{id}` | GET | Get organization details |
| `/api/organizations/{id}` | PUT | Update organization (admin) |
| `/api/organizations/{id}` | DELETE | Deactivate organization (admin, soft delete) |
| `/api/organizations/by-subdomain/{subdomain}` | GET | Get organization by subdomain (public) |
| `/api/organizations/validate-subdomain/{subdomain}` | GET | Validate subdomain availability (public) |
| `/api/organizations/stats` | GET | Get organization statistics (public) |

### User Management API (`/api/organizations/{orgId}/users`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/users` | POST | Add user to organization (admin) |
| `/api/organizations/{orgId}/users` | GET | List organization users |
| `/api/organizations/{orgId}/users/{userId}` | GET | Get user details |
| `/api/organizations/{orgId}/users/{userId}` | PUT | Update user (admin) |
| `/api/organizations/{orgId}/users/{userId}` | DELETE | Remove user from organization (admin) |
| `/api/organizations/{orgId}/users/{userId}/unlock` | POST | Unlock a locked user account (admin) |
| `/api/organizations/{orgId}/users/{userId}/suspend` | POST | Suspend a user account (admin) |
| `/api/organizations/{orgId}/users/{userId}/reactivate` | POST | Reactivate a suspended account (admin) |
| `/api/organizations/{orgId}/users/{userId}/role` | PUT | Change a user's role (admin) |
| `/api/organizations/{orgId}/users/{userId}/verify-email` | POST | Admin override to mark email as verified (admin) |

**User List Query Parameters** (GET `/api/organizations/{orgId}/users`):
- `includeInactive` (bool) — Include suspended/deleted users
- `emailVerified` (bool?) — Filter by email verification status
- `provisionedVia` (string?) — Filter by provisioning method (Local, Oidc, Invitation, etc.)
- `includePending` (bool) — Include pending OrgInvitation records

**Enhanced UserResponse** now includes: `EmailVerified`, `EmailVerifiedAt`, `ProvisionedVia`, `InvitedByUserId`, `ProfileCompleted`, `InvitationStatus`.

### IDP Configuration API (`/api/organizations/{orgId}/idp`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/idp` | GET | Get IDP configuration |
| `/api/organizations/{orgId}/idp` | PUT | Create or update IDP configuration |
| `/api/organizations/{orgId}/idp` | DELETE | Delete IDP configuration |
| `/api/organizations/{orgId}/idp/discover` | POST | Discover OIDC endpoints from issuer URL |
| `/api/organizations/{orgId}/idp/test` | POST | Test IDP connection (client_credentials grant) |
| `/api/organizations/{orgId}/idp/toggle` | POST | Enable or disable IDP |

**Supported provider presets:** Microsoft Entra, Google, Okta, Apple, Amazon Cognito, Generic OIDC

### Invitation API (`/api/organizations/{orgId}/invitations`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/invitations` | POST | Send an organization invitation (admin) |
| `/api/organizations/{orgId}/invitations` | GET | List invitations (filter by status) |
| `/api/organizations/{orgId}/invitations/{id}/revoke` | POST | Revoke a pending invitation (admin) |

**Invitation details:** 32-byte cryptographic token, configurable expiry (1-30 days, default 7). Invited users bypass domain restrictions.

### Domain Restrictions API (`/api/organizations/{orgId}/domain-restrictions`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/domain-restrictions` | GET | Get allowed email domains for auto-provisioning |
| `/api/organizations/{orgId}/domain-restrictions` | PUT | Update allowed email domains (admin) |

**Note:** An empty array disables restrictions (all domains allowed).

### TOTP Two-Factor Authentication API (`/api/totp`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/totp/setup` | POST | Initiate TOTP setup (generates secret, QR URI, backup codes) |
| `/api/totp/verify` | POST | Verify initial TOTP code to complete enrollment |
| `/api/totp/validate` | POST | Validate TOTP code during login (uses loginToken) |
| `/api/totp/backup-validate` | POST | Validate and consume a one-time backup code |
| `/api/totp` | DELETE | Disable TOTP 2FA |
| `/api/totp/status` | GET | Get TOTP 2FA status |

**Rate limiting:** TOTP validation endpoints use the `totp-validate` policy. Limit configurable via `RateLimiting:TotpPermitLimit` in `appsettings.json` (production recommendation: 5/min per IP).

### Organization Settings API (`/api/organizations/{orgId}/settings`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/settings` | GET | Get org settings (type, self-registration, domains, audit retention) |
| `/api/organizations/{orgId}/settings` | PUT | Update settings (self-registration, audit retention 1-120 months) |

### Custom Domain API (`/api/organizations/{orgId}/custom-domain`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/custom-domain` | GET | Get custom domain configuration and verification status |
| `/api/organizations/{orgId}/custom-domain` | PUT | Configure custom domain (returns CNAME instructions) |
| `/api/organizations/{orgId}/custom-domain` | DELETE | Remove custom domain configuration |
| `/api/organizations/{orgId}/custom-domain/verify` | POST | Verify custom domain CNAME DNS resolution |

### Admin Dashboard API (`/api/organizations/{orgId}/dashboard`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/dashboard` | GET | Get admin dashboard KPIs (user counts, roles, logins, invitations, IDP status) |

### Audit API (`/api/organizations/{orgId}/audit`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/audit` | GET | Query audit events (paginated, filterable by date/type/user) |
| `/api/organizations/{orgId}/audit/retention` | GET | Get audit retention configuration |
| `/api/organizations/{orgId}/audit/retention` | PUT | Update audit retention period (1-120 months) |

**Max page size:** 200 events. Audit events older than the retention period are automatically purged daily.

### Platform Organisation Management

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | `/api/platform/organizations` | List all organisations (paginated, status filter) | SystemAdmin |
| PUT | `/api/platform/organizations/{orgId}/status` | Update org status (Active/Suspended) | SystemAdmin |
| GET | `/api/platform/organizations/{orgId}/users` | List org users (read-only audit) | SystemAdmin |
| POST | `/api/platform/organizations` | Create org with admin invite | SystemAdmin |
| GET | `/api/platform/settings` | Get platform settings | SystemAdmin |
| PUT | `/api/platform/settings/public-org` | Enable/disable public org | SystemAdmin |

### Organization Recovery Configuration API (`/api/organizations/{orgId}/recovery-config`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/organizations/{orgId}/recovery-config` | POST | Create or update organization recovery configuration (admin) |
| `/api/organizations/{orgId}/recovery-config` | GET | Get organization recovery configuration |

**OrgRecoveryConfig entity**: Stores the organization's recovery public key and policy settings for organization-delegated wallet recovery (Feature 060). Administrators configure this to enable org-level wallet recovery for their users.

The Tenant Service also exposes passkey public key data used by the Wallet Service's `PasskeyServiceClient` during passkey-based wallet recovery key wrapping.

### Internal API (`/api/internal`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/internal/resolve-domain/{domain}` | GET | Resolve custom domain to organization subdomain (API Gateway use only) |

**Note:** Internal endpoints are excluded from public API documentation.

### Caller-organisation binding (required on every org-scoped route)

`RequireAdministrator` is **not** an organisation check. It is literally
`RequireRole("SystemAdmin", "Administrator")` and never inspects `org_id`. Composing
`RequirePlatformAudience` on top adds a *tier* check, not an organisation one. Neither answers
"*whose* organisation may this administrator administer?"

Any route whose template names an organisation must therefore also apply
**`.RequireCallerOrganization()`** (`Authorization/CallerOrganizationGate.cs`):

```csharp
var group = app.MapGroup("/api/organizations/{organizationId:guid}/invitations")
    .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")  // is an admin at all
    .RequireCallerOrganization();                                             // of THIS org
```

The gate allows: **service tokens** (internal S2S, not org-scoped); a **platform SystemAdmin**
(`SystemAdmin` role **and** membership of `WellKnownIds.SystemAdminOrgId` — mirroring the
`RequireSystemAdmin` policy's own test, so a stray `SystemAdmin` role claim in another org buys
nothing); otherwise the caller's `org_id` **must equal** the route's organisation, else **403** with
a `SEC-AUDIT` log. Applied to a route with no organisation id in its template it **fails closed** —
a mis-wiring must not look like a working control. It reads both `{organizationId}` and `{orgId}`.

**Cross-org access by a genuine platform SystemAdmin is intended and preserved** — verified live on
n1, where the seeded `admin@sorcha.local` reads other organisations deliberately.

**Why this was needed (B2+, 2026-07-29 catch-up security review):** six org-scoped groups had *zero*
caller-org binding, and no handler compared the caller either. An `Administrator` of org A could
read org B's audit log, alter its custom domain and domain restrictions, read its dashboard, manage
its invitations (a resend **rotates the token and emails the invitee**), and — worst — **add users to
org B, change their roles, and suspend them**. Confirmed empirically before the fix: a plain
Administrator of one organisation reached four other organisations' routes with HTTP 200. That the
codebase knows how to bind org is not in doubt — `RequireSystemAdmin` does exactly this check — so
its absence here was a gap, not a decision.

**Gated (every org-scoped group):** `InvitationEndpoints`, `AuditEndpoints`,
`CustomDomainEndpoints`, `DomainRestrictionEndpoints`, `DashboardEndpoints` (both `/dashboard` and
`/dashboard-summary`), the 13 per-organisation routes in `OrganizationEndpoints` (user management +
recovery-config), and — wave 2 — `IdpConfigurationEndpoints`, `OrgSettingsEndpoints`,
`ParticipantEndpoints` (**both** of its groups — see below), `RegisterInvitationEndpoints`,
`RegisterSubscriptionEndpoints`.

> **`ParticipantEndpoints` maps TWO groups on the SAME prefix** (`orgGroup` and `serviceGroup`).
> Wave 2 gated only the first, leaving `GET .../participants/by-user/{userId}` on plain
> `.RequireAuthorization()` — no role check, no caller-org bind — so any signed-in principal,
> including a citizen, could read a participant record for any user in any organisation. Caught in
> review of #1346 and fixed. The wiring guard now enumerates **by route prefix**, not by group
> variable, because a per-group review cannot see a second group sharing a prefix.

Two wave-2 cases are worth calling out:

- **`IdpConfigurationEndpoints` is the most serious of the whole set.** It decides *how users
  authenticate* into an organisation. An administrator of org A repointing org B's identity provider
  at an IDP they control is **account takeover of org B**.
- **`RegisterSubscriptionEndpoints`' read routes carried plain `.RequireAuthorization()`** — not even
  a role check — so **any** signed-in principal, including a citizen, could enumerate any
  organisation's register subscriptions.

**Deliberately NOT gated — `PlatformOrgEndpoints`.** `/api/platform/organizations` is cross-org **by
design** (platform topology administration) and is *already* correctly scoped: both
`RequireSystemAdmin` and `RequirePlatformAuditor` assert membership of the system-admin org, not
merely a role.

The principal a caller-org bind would actually break is **not** the SystemAdmin — the gate exempts
SystemAdmin-in-system-admin-org unconditionally. It is a **platform auditor**:
`RequirePlatformAuditor` admits an `Auditor` (or `Administrator`) role in the system-admin org, and
that principal has **no exemption arm**, so a route-org comparison would refuse them for every
organisation except `…0001`. (An earlier version of this note gave the SystemAdmin as the affected
principal, which was wrong — corrected in review of #1346.)

`OrgScopedCallerBindingTests` pins this as a deliberate non-change **via route metadata**, not by
driving a request: a behavioural test using a SystemAdmin client passes whether or not the gate is
applied, because that caller is exempt either way — so it could never detect the regression it
claimed to pin. `InternalEndpoints` and the DID-document regenerate route are `RequireService`, out of scope.

**One legitimate cross-org flow depends on the SystemAdmin exemption:** cross-node public-org
subscription (`Add-SorchaPublicOrgSubscription` → `POST /organizations/{publicOrgId}/register-subscriptions`)
posts with **sysadmin** headers, so it passes via the platform-SystemAdmin arm rather than an org
match. Removing that exemption would break federated subscribe.

### Organisation DID Documents (`/orgs/...`) — Feature 120

Per-organisation W3C DID documents. Contract:
`specs/120-production-issuer-signature-verification/contracts/org-did-document-endpoint.openapi.yaml`.

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/orgs/{orgId}/did.json` | GET | Anonymous | Resolve an org's published DID document (`application/did+json`, cacheable 6h) |
| `/orgs/by-did/{did}/did.json` | GET | Anonymous | Resolve by canonical `did:sorcha:org:{addr}` — a verifier holds the DID, not the org GUID (Feature 149) |
| `/orgs/{orgId}/did-document/regenerate` | POST | **`RequireService`** | Internal Wallet → Tenant trigger after a key event; rebuilds and publishes the document from the pushed key snapshot. Idempotent. |

**The two GETs are deliberately anonymous** — public DID resolution is the point. **The POST is
privileged and must stay that way.** It writes the issuer key material every verifier trusts for
that organisation, and derives the canonical identifier verbatim from the request body's
`walletAddress`. It shipped with no authorization attribute, and because this service configures no
fallback policy that made it *anonymous* on the published Tenant port — an attacker posting a
victim's orgId and wallet address (both public values) with their own JWK would have had their key
served as the victim's issuer key, so attacker-signed credentials verified as that organisation's.
Fixed in the 2026-07-29 catch-up security review with two controls:

1. `RequireAuthorization(AuthorizationPolicies.RequireService)` — anonymous callers and human
   tokens of **any** role (Administrator included) are refused; this path is service-to-service only.
2. The supplied `walletAddress` must equal the organisation's recorded canonical wallet address.
   An org with no recorded address yet is allowed through with a warning, so first-time publication
   is not broken; tighten to a hard refusal once provisioning guarantees the address is set first.

The caller is `IOrgDidDocumentClient.RegenerateAsync` (Wallet Service `IssuanceKeyService`), which
attaches a service token via `IServiceAuthClient` and **fails closed before sending** if it cannot
get one. That matters because `RegenerateAsync` maps every failure to `false`, which callers largely
swallow — so an unauthenticated call would have looked like an intermittent Tenant fault while DID
publishing silently stopped. Token rejection (401/403) is logged at `Error` for the same reason.

For full API documentation, open **Scalar UI** at `https://localhost:7080/scalar`.

---

## Address Lookup (Feature 103)

The Tenant Service hosts the postcode → address autofill API used by the
`PostcodeLookupRenderer` form control. Providers are pluggable behind
`IAddressLookupProvider`:

| Provider | Capability | Country | Auth | Default |
|----------|------------|---------|------|---------|
| **Postcodes.io** | ValidateOnly (postcode → town / region / country / lat-long) | UK | None (free public API) | ✅ Always on |
| **OS Places**    | FullAddress (postcode → candidate list) | UK | API key | ❌ Opt-in |

### Endpoints (routed via API Gateway `/api/*`)

- `GET  /api/address-lookup/providers` — list configured providers and their live availability
- `POST /api/address-lookup/postcode`  — resolve a postcode (validate-only metadata or full-address candidate list)

Both require a Bearer JWT and apply the standard API rate-limit policy.
The renderer falls back to plain text when no provider is reachable, so
service downtime never blocks form submission.

### Configuration

```json
{
  "AddressLookup": {
    "Enabled": true,
    "Providers": {
      "PostcodesIo": { "Enabled": true },
      "OSPlaces":    { "Enabled": false, "ApiKey": "" }
    },
    "CacheTtlMinutes": 60
  }
}
```

To enable OS Places, set `Providers.OSPlaces.Enabled = true` and supply
an API key obtained from `os.uk/datahub`. Provider order in the config
sets preference: the first provider with capability `FullAddress` wins
over any `ValidateOnly` provider for the same postcode.

---

## Trust: Org Certificates & Trusted Lists (Feature 181)

The Tenant Service hosts the platform's X.509 trust rail under `/api/v1/trust`, alongside the
DID-native register rail. Two capabilities landed with the EUDI conformance feature (US3–US5):
an ETSI TS 119 612 **trusted-list** import surface, and per-org **certificate lifecycle** (CSR,
external cert import, internal-cert enrol/re-issue). Admin routes require `RequireAdministrator`
**and** `RequirePlatformAudience`; the imported-chain reader is public for x5c resolution.

Implementation: `Trust/OrgCertificateService.cs`, `Trust/X509CertificateBuilder.cs`,
`Trust/TrustedListImportService.cs`, `Endpoints/TrustEndpoints.cs`, storage in `Storage/CertificateStore.cs`
+ `Storage/TrustedListSnapshotStore.cs` (Postgres, `public` schema).

### Trusted-list snapshots (US3) — `/api/v1/trust/trustlists`

| Method | Path | Description |
|--------|------|-------------|
| POST | `/trustlists/import` | Import a signed ETSI TS 119 612 list (`multipart` or HTTPS fetch-once). Enveloped XMLDSig core verify + parse + granted CA/QC anchor extraction. Newest per `trustListId` is authoritative; import supersedes the prior Active version |
| GET | `/trustlists` | List loaded snapshots + freshness |
| GET | `/trustlists/{trustListId}` | Detail — anchors + extracted-vs-skipped summary |
| DELETE | `/trustlists/{trustListId}` | Remove all versions |
| GET | `/trustlists/{trustListId}/anchors` | **Service-tier** — DER roots + freshness for Blueprint/HAIP; 404 `TRUSTLIST_UNAVAILABLE` |

Typed import failures: `TRUSTLIST_MALFORMED` / `TRUSTLIST_SIGNATURE_INVALID` / `TRUSTLIST_SEQUENCE_REGRESSION`.
Snapshot identity flows into `TrustEvidence.TrustListId` as `{trustListId}#{sequenceNumber}`. Freshness is
warn-by-default; `Trust:TrustListStrictFreshness` fails closed `TRUSTLIST_STALE`. Metrics on the
`Sorcha.Trust` meter: `sorcha_trustlist_stale_evaluation_total`, `sorcha_trustlist_snapshot_info`. Live
LOTL / XAdES / pivot-chain refresh is deferred.

### Org certificates (US4/US5) — `/api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}`

| Method | Path | Description |
|--------|------|-------------|
| GET | `.../certificates` | List internal + imported certs + `eligibility` (`eligible`, `reason`, `boundKeySource` ∈ `Primary`\|`HaipCoKey`) |
| POST | `.../csr` | CSR bound to the server-resolved org P-256 key (optional `subjectDn`) |
| POST | `.../certificates/import` | Import leaf `certificatePem` + `chainPem[]`; supersedes the prior Active imported cert |
| DELETE | `.../certificates/{certificateId}` | Retire an imported cert (Status→Superseded); idempotent |
| GET | `.../imported-cert-chain` | **Public** — imported chain for x5c resolution |
| POST | `.../enrol` | Issue/re-issue the internal tenant-root cert; server resolves the key (no caller-supplied key); backfill for pre-existing orgs |

Typed failures (`422`, problem+json, `Trust/CertErrorCodes.cs`): `CERT_KEY_NOT_ELIGIBLE` (non-P-256 org
key — replaces the prior ASN.1 500), `CERT_KEY_MISMATCH`, `CERT_CHAIN_INVALID`, `CERT_EXPIRED`,
`CERT_UNSUITABLE`, `CERT_EXTERNAL_ANCHOR_UNAVAILABLE`. The org's P-256 key is its primary key when ES256,
else a derived HAIP co-key under `sorcha:haip-issuer-signing`; CSR/cert signing is remote pre-hashed ES256
via the Wallet Service seam `IOrgIssuerCertKeyService`, so the private key never leaves custody. **Auto-enrol**
runs best-effort as a ride-along on `POST /api/organizations/{id}/wallet`, the moment an organisation first
has a wallet (#1525) — a server-side hook, not an API; failure never fails the link. It previously rode on
server-side wallet provisioning at org creation and on the `OrgWalletReconciliationService` sweep, both of
which are gone. CA keys are AES-256-GCM encrypted at rest
(`TenantRootCaRecord`); `InternalCaTrustProvider` is a write-through cache over `ICertificateStore`. Admin UI:
the certificates panel in `OrgSettings.razor` (`IOrgCertificateAdminService`). Metric
`sorcha_org_cert_issuance_total{provenance,outcome,reason}`.

---

## Development

### Project Structure

```
src/Services/Sorcha.Tenant.Service/
├── Endpoints/              # Minimal API endpoint groups
│   ├── AuthEndpoints.cs              # Login, register, logout, token management
│   ├── OidcEndpoints.cs              # OIDC initiate, callback, profile, email verification
│   ├── OrganizationEndpoints.cs      # Org CRUD, user management, lifecycle
│   ├── IdpConfigurationEndpoints.cs  # IDP CRUD, discover, test, toggle
│   ├── InvitationEndpoints.cs        # Create, list, revoke invitations
│   ├── DomainRestrictionEndpoints.cs # Email domain restrictions
│   ├── TotpEndpoints.cs              # TOTP 2FA setup, verify, validate, backup
│   ├── OrgSettingsEndpoints.cs       # Org settings management
│   ├── CustomDomainEndpoints.cs      # Custom domain CNAME management
│   ├── DashboardEndpoints.cs         # Admin dashboard KPIs
│   ├── AuditEndpoints.cs             # Audit log query and retention
│   ├── InternalEndpoints.cs          # Domain resolution (API Gateway internal)
│   ├── BootstrapEndpoints.cs         # Initial system bootstrap (creates System Admin Org + Public Org, PlatformUser for admin, PlatformSettings)
│   ├── ServiceAuthEndpoints.cs       # Service-to-service auth
│   ├── PasskeyEndpoints.cs             # Org user passkey registration and 2FA
│   ├── PublicAuthEndpoints.cs          # Public user passkey, social login, method management
│   ├── ParticipantEndpoints.cs       # Participant identity management
│   ├── PushSubscriptionEndpoints.cs  # Push notification subscriptions
│   └── UserPreferenceEndpoints.cs    # User preference management
├── Services/               # Business logic services
│   ├── OrganizationService.cs
│   ├── TokenService.cs
│   ├── TotpService.cs
│   ├── IdpConfigurationService.cs
│   ├── OidcExchangeService.cs
│   ├── OidcProvisioningService.cs
│   ├── InvitationService.cs
│   ├── CustomDomainService.cs
│   ├── DashboardService.cs
│   ├── PasswordPolicyService.cs
│   ├── EmailVerificationService.cs
│   ├── PassKeyService.cs
│   ├── PublicUserService.cs
│   └── ...
├── Data/                   # Data access layer
│   ├── TenantDbContext.cs
│   ├── Repositories/
│   │   ├── IOrganizationRepository.cs
│   │   ├── IIdentityRepository.cs
│   │   ├── ICustomDomainRepository.cs
│   │   └── ...
│   └── Migrations/
├── Models/                 # Domain models and DTOs
│   ├── Dtos/               # Request/response DTOs
│   ├── UserIdentity.cs
│   ├── Organization.cs
│   ├── IdentityProviderConfiguration.cs
│   ├── Invitation.cs
│   ├── CustomDomainMapping.cs
│   ├── AuditLogEntry.cs
│   └── ...
├── Extensions/             # Service extensions
│   ├── ServiceCollectionExtensions.cs
│   └── ApplicationBuilderExtensions.cs
├── appsettings.json
├── appsettings.Development.json
└── Program.cs
```

### Running Tests

```bash
# Unit tests
dotnet test tests/Sorcha.Tenant.Service.Tests

# Integration tests (uses Testcontainers)
dotnet test tests/Sorcha.Tenant.Service.IntegrationTests

# Performance tests
dotnet run --project tests/Sorcha.Tenant.Service.PerformanceTests
```

### Code Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:Html
```

### Database Migrations

```bash
# Create new migration
dotnet ef migrations add MigrationName --context TenantDbContext

# Apply migrations
dotnet ef database update

# Revert migration
dotnet ef database update PreviousMigrationName

# Generate SQL script
dotnet ef migrations script --output migrations.sql
```

---

## Security Considerations

### Secrets Management

- **Local Development**: Use .NET User Secrets (stored outside project directory)
- **Production**: Use Azure Key Vault, AWS Secrets Manager, or HashiCorp Vault
- **NEVER commit secrets** to source control

### JWT Signing Keys

- **Algorithm**: RS256 (RSA-SHA256) with 4096-bit keys
- **Rotation**: Rotate keys every 90 days
- **Storage**: Private key in Key Vault, public key in JWKS endpoint

### Multi-Tenancy

- **Data Isolation**: PostgreSQL schemas per organization (`org_{id}`)
- **Row-Level Security**: EF Core query filters prevent cross-tenant data access
- **Audit Logging**: All operations logged with organization context

### Password Policy (NIST SP 800-63B)

- **Minimum Length**: 12 characters
- **No Complexity Rules**: No forced uppercase/numbers/symbols
- **Breach List Check**: Validates against HIBP (Have I Been Pwned) database
- **BCrypt Hashing**: Passwords stored as BCrypt hashes

### Two-Factor Authentication

- **TOTP**: Time-based One-Time Password (RFC 6238) via authenticator apps
- **Backup Codes**: 8-character alphanumeric one-time recovery codes
- **Login Flow**: Password verification issues a short-lived loginToken, then TOTP validation issues full JWT

### Rate Limiting & Progressive Lockout

- **Login Attempts**: Progressive lockout (5 fails=5min, 10=30min, 15=24h, 25=permanent admin unlock)
- **Token Requests**: 100 requests per minute per client
- **Admin Operations**: 20 requests per minute per user
- **TOTP Validation**: 5 attempts per minute per user/IP
- **Email Verification Resend**: 3 per hour per user

---

## Transactional Email Architecture (Feature 112)

All transactional email the Tenant Service sends — verification, invitation,
password reset, and welcome — goes through a single templated pipeline. The
entry point application code uses is `ITransactionalEmailService`.

```
Caller (EmailVerificationService / InvitationService / PasswordResetService /
        WelcomeEmailDispatcher)
      │
      ▼ typed dispatch record
ITransactionalEmailService  ← the only surface callers touch
      │
      ├── IEmailTemplateRenderer (Scriban, embedded resources, pre-parsed at startup)
      │        │
      │        ▼
      │   Emails/Templates/*.html + .txt (six pairs; base.* is shared layout)
      │
      └── IEmailSender (SMTP via MailKit OR Azure Communication Services)
               multipart HTML + plaintext on every message
```

**Templates** live under `Emails/Templates/` as embedded resources:

| Name | Purpose | Branding |
|------|---------|----------|
| `base.html` / `base.txt` | Shared frame (logo/sender header, body, footer with reply-to) | — |
| `verify.html` / `.txt` | Confirm-your-email after email+password signup | Sorcha |
| `invite.html` / `.txt` | Organisation invitation, clear org name + role | Per-org (logo, colour) |
| `reset.html` / `.txt` | Password reset link | Sorcha |
| `welcome-public.html` / `.txt` | First-verify greeting with recovery-phrase advance warning | Sorcha |
| `welcome-invited.html` / `.txt` | First-login greeting for org-invited users | Per-org |

**Branding** is resolved per-send by `IEmailBrandingResolver`. Invitations and
invited welcomes pull `Organization.Name` / `Branding.LogoUrl` /
`Branding.PrimaryColor` from the inviting org with per-field fallback to Sorcha
platform defaults — the org name always wins; any branding field missing on the
org falls back to Sorcha's default.

**Welcome email** is one-shot per user. `WelcomeEmailDispatcher.SendIfPendingAsync`
is called from three trigger points and is idempotent (guarded by
`PlatformUser.WelcomeSentAt`) and non-throwing (a send failure is logged but
never reverses the authentication flow):

1. `EmailVerificationService.VerifyTokenAsync` — after `EmailVerified = true`
   on the email+password signup path
2. `LoginService` — after a successful password login (covers users who've
   already verified and are logging in for the first time)
3. `SocialCallback` Razor PageModel — after successful social-login OAuth
   exchange (social users skip verification; the IdP pre-verified the email)

Variant selection is based on the user's `PlatformUserOrgMembership` rows:
public-org-only → `welcome-public` (with recovery-phrase advance-warning
section); any standard-org membership → `welcome-invited` with the earliest-
joined standard org as the "inviting" org.

**Design-history reference**:
[`docs/superpowers/specs/2026-04-24-email-sweep-design.md`](https://github.com/Sorcha-Platform/Sorcha/blob/master/docs/superpowers/specs/2026-04-24-email-sweep-design.md)
carries the full design rationale. The feature spec, plan, tasks, and contracts
live under [`specs/112-email-sweep/`](https://github.com/Sorcha-Platform/Sorcha/blob/master/specs/112-email-sweep/).

**Snapshot fixtures** for every template pair are committed under
`tests/Sorcha.Tenant.Service.Tests/Fixtures/Emails/`. When a deliberate copy
change is made to a template, regenerate fixtures with
`UPDATE_EMAIL_FIXTURES=1 dotnet test --filter "~EmailTemplateSnapshotTests"`.

### Tone and content guardrails

- Single clear action per message (one CTA button).
- No recovery-phrase content in any email body. The public welcome primes users
  for the recovery-phrase moment at wallet creation but never includes phrase
  material — the phrase is shown exactly once in the UI at wallet creation and
  is never stored.
- No phishing-shaped language. Every email footer includes a reply-to.

---

## Authorization Roles

The Tenant Service uses 5 consolidated roles for access control:

| Role | Description | Key Permissions |
|------|-------------|-----------------|
| **SystemAdmin** | Platform-level administrator | Full access, cannot be assigned via API |
| **Administrator** | Organization administrator | IDP config, user management, invitations, settings, dashboard |
| **Designer** | Blueprint designer | Create/manage blueprints and workflows |
| **Auditor** | Compliance/audit reviewer | Read-only access to audit logs and reports |
| **Member** | Standard organization member | Basic access, participate in workflows |

### Authorization Policies

| Policy | Required Role(s) |
|--------|-------------------|
| `RequireAdministrator` | SystemAdmin or Administrator |
| `RequireAuditor` | SystemAdmin, Administrator, or Auditor |
| `RequireOrganizationMember` | Any authenticated organization member |
| `RequireService` | Service-to-service tokens only |

---

## OIDC Integration Flow

The service implements a full authorization code + PKCE exchange flow:

1. **Initiate** (`POST /api/auth/oidc/initiate`): Client sends org subdomain, receives authorization URL
2. **Redirect**: User is redirected to the external IDP (Microsoft Entra, Google, etc.)
3. **Callback** (`GET /api/auth/callback/{orgSubdomain}`): IDP redirects back with authorization code
4. **Exchange**: Service exchanges code for external tokens, validates ID token
5. **Provision**: Auto-provisions new users or matches existing users
6. **JWT Issuance**: Issues Sorcha JWT (downstream services never see external tokens)
7. **2FA Check**: If TOTP is enabled, returns a loginToken for second-factor validation
8. **Profile Completion**: If required claims are missing, prompts for profile completion

### Provider Presets

The IDP configuration supports auto-discovery and presets for top providers:

| Provider | Preset Name | Discovery URL Pattern |
|----------|-------------|----------------------|
| Microsoft Entra ID | `MicrosoftEntra` | `https://login.microsoftonline.com/{tenantId}/v2.0` |
| Google | `Google` | `https://accounts.google.com` |
| Okta | `Okta` | `https://{domain}.okta.com` |
| Apple | `Apple` | `https://appleid.apple.com` |
| Amazon Cognito | `AmazonCognito` | `https://cognito-idp.{region}.amazonaws.com/{poolId}` |
| Generic OIDC | `GenericOidc` | Any `.well-known/openid-configuration` URL |

---

## Multi-Tenant URL Resolution

The service supports 3-tier URL resolution for organizations:

| Tier | Pattern | Example |
|------|---------|---------|
| **Path** | `/org/{subdomain}` | `https://sorcha.dev/org/acme` |
| **Subdomain** | `{subdomain}.sorcha.dev` | `https://acme.sorcha.dev` |
| **Custom Domain** | CNAME to platform | `https://id.acme.com` |

Custom domains require CNAME DNS configuration and verification. The internal `/api/internal/resolve-domain/{domain}` endpoint is used by the API Gateway for domain-based routing.

---

## Deployment

### .NET Aspire (Development)

```bash
# Run via Aspire orchestration
dotnet run --project src/Apps/Sorcha.AppHost

# Aspire Dashboard: http://localhost:15888
```

### Docker

```bash
# Build image
docker build -t sorcha-tenant-service -f src/Services/Sorcha.Tenant.Service/Dockerfile .

# Run container
docker run -p 7080:8080 \
  -e ConnectionStrings__TenantDatabase="Host=db;..." \
  -e Redis__ConnectionString="redis:6379" \
  sorcha-tenant-service
```

### Azure App Service

```bash
# Deploy via Azure CLI
az webapp create --name sorcha-tenant-service --resource-group sorcha-rg --plan sorcha-plan
az webapp deployment source config-zip --name sorcha-tenant-service --resource-group sorcha-rg --src publish.zip
```

---

## Observability

### Logging (Serilog + OTLP)

- **Structured Logging**: Serilog with machine name, thread ID, application enrichment
- **Correlation IDs**: Track requests across services
- **Aspire Dashboard**: Centralized log viewer via OTLP (http://localhost:18888)

```csharp
// Example log entry
Log.Information("User {UserId} authenticated for organization {OrgId}", userId, orgId);
```

### Tracing (OpenTelemetry + Zipkin)

- **Distributed Tracing**: End-to-end request tracing
- **Zipkin Dashboard**: http://localhost:9411

### Metrics (Prometheus)

- **Metrics Endpoint**: `/metrics`
- **Custom Metrics**: Login success/failure rates, token issuance latency

---

## Troubleshooting

### Database Connection Issues

**Error**: "Connection refused" or "password authentication failed"

**Solution**:
```bash
# Check PostgreSQL is running
docker ps | grep postgres

# Verify User Secrets
dotnet user-secrets list --project src/Services/Sorcha.Tenant.Service

# Test connection
psql -h localhost -U sorcha_user -d sorcha_tenant_dev
```

### Redis Connection Issues

**Error**: "It was not possible to connect to the redis server(s)"

**Solution**:
```bash
# Check Redis is running
docker ps | grep redis

# Test connection
redis-cli ping  # Should return: PONG
```

### Token Validation Failures

**Error**: "Invalid signature" or "Token has expired"

**Solution**:
- Ensure JWT signing key is configured in User Secrets
- Check system clock synchronization (token validation uses timestamps)
- Verify JWKS endpoint is accessible: `https://localhost:7080/.well-known/jwks.json`

---

## Contributing

### Development Workflow

1. **Create Feature Branch**: `git checkout -b feature/your-feature`
2. **Write Tests First**: Follow TDD (Test-Driven Development)
3. **Implement Feature**: Follow existing code patterns
4. **Run Tests**: Ensure all tests pass
5. **Update Documentation**: Update README, API docs, specs
6. **Submit PR**: Reference task ID in commit message

### Code Standards

- **C# Conventions**: Follow Microsoft C# coding conventions
- **Async/Await**: Use async for all I/O operations
- **Dependency Injection**: Use constructor injection
- **OpenAPI Documentation**: All endpoints must have XML documentation

---

## Resources

- **Authentication Setup**: [docs/guides/AUTHENTICATION-SETUP.md](../../../docs/guides/AUTHENTICATION-SETUP.md)
- **Architecture**: [docs/architecture.md](../../../docs/architecture.md)
- **Development Status**: [docs/reference/development-status.md](../../../docs/reference/development-status.md)

---

## Secret Protection at Rest (Feature 146)

Sensitive secrets stored by the Tenant Service — **TOTP 2FA secrets** and **OIDC identity-provider
client secrets** — are encrypted at rest with **AES-256-GCM** via `ISecretProtectionProvider`
(`SoftwareSecretProtectionProvider`). The 32-byte key is resolved once at startup by
`TenantSecretKeyResolver`:

1. If `Tenant:SecretProtection:Key` (base64-encoded 32 bytes) is set, it is used (KeyId `config-v1`).
2. Otherwise the key is **HKDF-SHA256-derived from the JWT signing key** (`JwtSettings:SigningKey`,
   info `sorcha:tenant:secret-protection:v1`) — so **no new mandatory configuration** is required
   (KeyId `jwt-derived-v1`).
3. In **Production/Staging**, if neither resolves, the service **fails to start** (fail-closed).

The KeyId is stored alongside each ciphertext to support rotation. The 2FA intermediate-token HMAC
key is derived from the same JWT signing key (info `sorcha:tenant:login-token-hmac:v1`), making it
**stable across replicas and restarts**. Generate an override key with `openssl rand -base64 32`.

> The seam intentionally mirrors the Wallet `IOrgKeyProtectionProvider`; the two converge onto a
> shared provider (and gain a KMS/HSM implementation) during the Hardware Key Storage initiative.
> Design: [docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md](https://github.com/Sorcha-Platform/Sorcha/blob/master/docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md).

---

## License

This project is licensed under the Apache License 2.0. See [LICENSE](https://github.com/Sorcha-Platform/Sorcha/blob/master/LICENSE) for details.

---

## Support

For issues, questions, or contributions:
- **GitHub Issues**: [Sorcha Issues](https://github.com/Sorcha-Platform/Sorcha/issues)
- **Documentation**: [Sorcha Docs](../../../docs/)
- **CLAUDE.md**: [AI Assistant Guide](https://github.com/Sorcha-Platform/Sorcha/blob/master/CLAUDE.md)

---

## Unified Account Security Surface (Feature 150)

The successor to Feature 116 — one discoverable **Security** home plus an assurance-aware floor rule.

- **Assurance model** — `AssurancePolicy` is the server-authoritative, computed-never-stored source of truth: `AuthAssuranceTier` (`Basic < Strong < Strongest`), `TierOfMethod`, `TierOfProof`, `RequiredProofTier(operation, target)`. **The password is `Basic` everywhere (T061 resolved)** — its change/remove are Basic-gated; a Basic proof can never disable TOTP (Strong) or remove a passkey (Strongest).
- **Floor rule** — a step-up proof authorises a destructive/downgrade op **iff `proofTier >= RequiredProofTier`** (plus the last-method floor). Enforced in `AuthChallengeService`: initiate offers only floor-eligible proofs; verify returns `403 proof_tier_insufficient`. The ambiguous `RemoveAuthMethod` carries a `TargetMethodKind` (passkey-revoke vs social-unlink); a null target fails safe to Strongest. A Basic factor can never strip a Strong/Strongest method.
- **Always-notify** — `ISecurityChangeNotifier` writes an F118 inbox entry **and** sends the Sorcha-branded `security-change` email on every mutation (password / social / passkey / TOTP). Both legs best-effort; a notify failure never rolls back the operation.
- **Surface** — `GET /api/me/auth-methods` returns per-row `AssuranceTier` + `RequiredProofTier` + `CanRemove`; `POST /api/auth/challenge/{initiate,verify}` take the optional `TargetMethodKind`. The shared `SecurityHome` renders on web (`/app/security`) and the wallet PWA (`/wallet/security`).
- **Phasing** — US1 (consolidation + floor + finished Passkey/Re-OAuth proofs + always-notify) shipped. Email OTP (US2), config-gated SMS OTP (US3, via `ISmsSender`), and PWA parity (US4) are follow-up phases; US2 owns the pre-release schema squash (`PlatformUser.PhoneNumber`/`PhoneVerifiedAt` + `PlatformUserTwoFactor`) and a Redis-backed `ServerSentOtpService` + `VerificationChannelRegistry`.

See the **`sorcha-architecture`** skill ("Feature 150") and `specs/150-account-security/` for the full reference.

---

**Last Updated**: 2026-06-11
**Maintained By**: Sorcha Contributors
**Deferred (Post-MVD)**: Azure AD B2C integration
