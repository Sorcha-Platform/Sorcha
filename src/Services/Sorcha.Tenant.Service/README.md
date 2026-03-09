# Sorcha Tenant Service

**Version**: 2.0.0
**Status**: 95% Complete
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
- **PassKey Authentication**: FIDO2/WebAuthn passwordless authentication for enhanced security
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
│  │             Service Layer                            │  │
│  │  • OrganizationService  • TokenService               │  │
│  │  • OidcExchangeService  • OidcProvisioningService    │  │
│  │  • IdpConfigurationService • TotpService             │  │
│  │  • InvitationService    • CustomDomainService        │  │
│  │  • PasswordPolicyService • EmailVerificationService  │  │
│  │  • DashboardService     • PassKeyService             │  │
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

For detailed secrets management guide, see [specs/001-tenant-auth/secrets-setup.md](../../../specs/001-tenant-auth/secrets-setup.md).

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

---

## API Endpoints

### Authentication API (`/api/auth`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/login` | POST | Login with email and password (returns 2FA challenge if enabled) |
| `/api/auth/verify-2fa` | POST | Verify TOTP code or backup code to complete login |
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

### PassKey Authentication API (`/api/auth/passkey`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/auth/passkey/register-options` | POST | Get PassKey registration options |
| `/api/auth/passkey/register` | POST | Complete PassKey registration |
| `/api/auth/passkey/login-options` | POST | Get PassKey login options |
| `/api/auth/passkey/login` | POST | Complete PassKey login |

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

**Rate limiting:** 5 attempts per minute per user/IP on validation endpoints.

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

### Internal API (`/api/internal`)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/internal/resolve-domain/{domain}` | GET | Resolve custom domain to organization subdomain (API Gateway use only) |

**Note:** Internal endpoints are excluded from public API documentation.

For full API documentation, open **Scalar UI** at `https://localhost:7080/scalar`.

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
│   ├── BootstrapEndpoints.cs         # Initial system bootstrap
│   ├── ServiceAuthEndpoints.cs       # Service-to-service auth
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
| **Path** | `/org/{subdomain}` | `https://sorcha.io/org/acme` |
| **Subdomain** | `{subdomain}.sorcha.io` | `https://acme.sorcha.io` |
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

- **Original Specification**: [specs/001-tenant-auth/spec.md](../../../specs/001-tenant-auth/spec.md)
- **Org Identity Admin Spec (054)**: [specs/054-org-identity-admin/spec.md](../../../specs/054-org-identity-admin/spec.md)
- **054 Design Document**: [docs/plans/2026-03-08-org-identity-admin-design.md](../../../docs/plans/2026-03-08-org-identity-admin-design.md)
- **Implementation Plan**: [specs/001-tenant-auth/plan.md](../../../specs/001-tenant-auth/plan.md)
- **Secrets Setup**: [specs/001-tenant-auth/secrets-setup.md](../../../specs/001-tenant-auth/secrets-setup.md)
- **Quickstart Guide**: [specs/001-tenant-auth/quickstart.md](../../../specs/001-tenant-auth/quickstart.md)
- **API Contracts**: [specs/001-tenant-auth/contracts/](../../../specs/001-tenant-auth/contracts/)

---

## License

This project is licensed under the Apache License 2.0. See [LICENSE](../../../LICENSE) for details.

---

## Support

For issues, questions, or contributions:
- **GitHub Issues**: [Sorcha Issues](https://github.com/your-org/sorcha/issues)
- **Documentation**: [Sorcha Docs](../../../docs/)
- **CLAUDE.md**: [AI Assistant Guide](../../../CLAUDE.md)

---

**Last Updated**: 2026-03-09
**Maintained By**: Sorcha Contributors
**Deferred (Post-MVD)**: Azure AD B2C integration
