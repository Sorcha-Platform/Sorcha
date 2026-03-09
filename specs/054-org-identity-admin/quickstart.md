# Quickstart: Organization Admin & Identity Management

**Branch**: `054-org-identity-admin` | **Date**: 2026-03-08

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for PostgreSQL, Redis)
- An OIDC provider for testing (Microsoft Entra ID dev tenant, Google Cloud Console, or Okta dev account)

## Development Setup

```bash
# 1. Start infrastructure
docker-compose up -d postgres redis

# 2. Run Tenant Service (applies migrations automatically)
dotnet run --project src/Services/Sorcha.Tenant.Service

# 3. Bootstrap platform (creates system admin + default org)
curl -X POST http://localhost:5110/api/bootstrap \
  -H "Content-Type: application/json" \
  -d '{"adminEmail":"admin@sorcha.io","adminPassword":"SecureP@ss123!","organizationName":"Sorcha Platform","subdomain":"platform"}'

# 4. Login as admin
curl -X POST http://localhost:5110/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@sorcha.io","password":"SecureP@ss123!"}'
# → Returns { accessToken, refreshToken }
```

## Key Flows

### Flow 1: Configure an OIDC Provider

```bash
TOKEN="<admin access token>"
ORG_ID="<org id>"

# Step 1: Discover IDP endpoints
curl -X POST "http://localhost:5110/api/organizations/$ORG_ID/idp/discover" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"issuerUrl":"https://accounts.google.com"}'

# Step 2: Save IDP configuration
curl -X PUT "http://localhost:5110/api/organizations/$ORG_ID/idp" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "providerPreset": "Google",
    "issuerUrl": "https://accounts.google.com",
    "clientId": "your-client-id.apps.googleusercontent.com",
    "clientSecret": "your-client-secret",
    "displayName": "Google Workspace"
  }'

# Step 3: Test connection
curl -X POST "http://localhost:5110/api/organizations/$ORG_ID/idp/test" \
  -H "Authorization: Bearer $TOKEN"

# Step 4: Enable IDP
curl -X POST "http://localhost:5110/api/organizations/$ORG_ID/idp/toggle" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"enabled": true}'
```

### Flow 2: OIDC Login (User)

```bash
# Step 1: Initiate OIDC flow (browser redirect)
curl -X POST "http://localhost:5110/api/auth/oidc/initiate" \
  -H "Content-Type: application/json" \
  -d '{"orgSubdomain":"acmestores"}'
# → Returns { authorizationUrl } — redirect browser here

# Step 2: After IDP auth, browser redirects to callback
# GET /api/auth/callback/acmestores?code=xxx&state=yyy
# → Auto-provisions user, issues Sorcha JWT, redirects to app

# Step 3: If profile incomplete, user completes it
curl -X POST "http://localhost:5110/api/auth/oidc/complete-profile" \
  -H "Authorization: Bearer $PARTIAL_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com","displayName":"Jane Doe"}'
```

### Flow 3: Send an Invitation

```bash
# Create invitation
curl -X POST "http://localhost:5110/api/organizations/$ORG_ID/invitations" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"email":"partner@external.com","role":"Designer","expiryDays":7}'

# List pending invitations
curl "http://localhost:5110/api/organizations/$ORG_ID/invitations?status=Pending" \
  -H "Authorization: Bearer $TOKEN"
```

### Flow 4: Self-Register (Public Org)

```bash
# Register with email/password
curl -X POST "http://localhost:5110/api/auth/register" \
  -H "Content-Type: application/json" \
  -d '{
    "orgSubdomain": "publicorg",
    "email": "newuser@example.com",
    "password": "MySecurePass123",
    "displayName": "New User"
  }'
# → Returns 201, verification email sent

# Verify email (from link in email)
curl -X POST "http://localhost:5110/api/auth/verify-email" \
  -H "Content-Type: application/json" \
  -d '{"token":"verification-token-from-email","orgSubdomain":"publicorg"}'
```

## Testing an OIDC Provider Locally

### Google (easiest for testing)

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create OAuth 2.0 credentials (Web Application)
3. Add redirect URI: `http://localhost:5110/api/auth/callback/{your-subdomain}`
4. Use Client ID and Secret in IDP configuration

### Microsoft Entra ID

1. Go to [Azure Portal](https://portal.azure.com/) → App Registrations
2. Register new app, add redirect URI
3. Note: Issuer URL is `https://login.microsoftonline.com/{tenant-id}/v2.0`

## Running Tests

```bash
# Unit tests for new OIDC services
dotnet test tests/Sorcha.Tenant.Service.Tests --filter "Category=OidcIntegration"

# All Tenant Service tests
dotnet test tests/Sorcha.Tenant.Service.Tests
```
