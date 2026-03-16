# Quickstart: Platform Organisation Topology

**Feature**: 058-platform-org-topology
**Date**: 2026-03-16

---

## Overview

This feature transforms Sorcha from a single-org model to a three-tier organisation topology: system admin org, public org, and private orgs. It introduces platform-wide identity (`PlatformUser`), social login, email/password signup, blueprint-driven org creation, and org switching.

## Getting Started

### Prerequisites

- .NET 10 SDK
- Docker Desktop (PostgreSQL, Redis)
- Existing Sorcha development setup

### Build & Run

```bash
# Standard build (unchanged)
dotnet restore && dotnet build && dotnet test

# Docker (recommended)
docker-compose up -d

# Or Aspire
dotnet run --project src/Apps/Sorcha.AppHost
```

### Bootstrap Flow (Changed)

After bootstrap, the system now has **two** organisations:

```bash
# Bootstrap creates:
# 1. System Admin Org (ID: ...0001) — Active, IsPlatformOrg=true
# 2. Public Org (ID: ...0002) — Suspended, IsPlatformOrg=true
# 3. Admin PlatformUser
# 4. Admin UserIdentity in system admin org
# 5. PlatformSettings (PublicOrgEnabled=false, MaxOrgsPerUser=1)

curl -X POST http://localhost/api/tenants/bootstrap \
  -H "Content-Type: application/json" \
  -d '{
    "organizationName": "Sorcha Local",
    "organizationSubdomain": "sorcha-local",
    "adminEmail": "admin@sorcha.local",
    "adminPassword": "Dev_Pass_2025!",
    "adminName": "System Admin"
  }'
```

### Enable Public Org

```bash
# As system admin, enable the public org
curl -X PUT http://localhost/api/platform/settings/public-org \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"enabled": true}'
```

### Social Login Signup (Public Org)

```bash
# 1. Initiate social login
curl -X POST http://localhost/api/auth/social/initiate \
  -H "Content-Type: application/json" \
  -d '{"provider": "google", "returnUrl": "http://localhost/app"}'
# Returns: { "authorizationUrl": "https://accounts.google.com/...", "state": "..." }

# 2. After OAuth dance, callback
curl -X POST http://localhost/api/auth/social/callback \
  -H "Content-Type: application/json" \
  -d '{"provider": "google", "code": "auth_code_from_google", "state": "state_from_step_1"}'
# Returns: TokenResponse (accessToken, refreshToken)
```

### Email/Password Signup (Public Org)

```bash
curl -X POST http://localhost/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "orgSubdomain": "sorcha-local",
    "email": "user@example.com",
    "password": "MySecurePass1!",
    "displayName": "Jane Doe"
  }'
# Returns: TokenResponse + verification email sent
```

### Create Private Org (via Blueprint)

```bash
# As a public org member, trigger the Create Organisation blueprint
# This runs through the blueprint workflow: validate → provision → confirm
```

### Org Switching

```bash
# List my organisations
curl http://localhost/api/auth/me/organizations \
  -H "Authorization: Bearer $TOKEN"
# Returns: list of org memberships

# Switch to a different org
curl -X POST http://localhost/api/auth/switch-org \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"organizationId": "target-org-id"}'
# Returns: new TokenResponse scoped to target org
```

### Platform Management (System Admin)

```bash
# List all organisations
curl http://localhost/api/platform/organizations \
  -H "Authorization: Bearer $ADMIN_TOKEN"

# View org users (read-only for private orgs)
curl http://localhost/api/platform/organizations/{orgId}/users \
  -H "Authorization: Bearer $ADMIN_TOKEN"

# Suspend a private org
curl -X PUT http://localhost/api/platform/organizations/{orgId}/status \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"status": "Suspended"}'

# Create org with admin invite
curl -X POST http://localhost/api/platform/organizations \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Acme Corp",
    "subdomain": "acme-corp",
    "adminEmail": "ceo@acme.com"
  }'
```

## Key Concepts

### Identity Layers

| Layer | Schema | Purpose | Entity |
|-------|--------|---------|--------|
| Platform | public | Authentication, cross-org anchor | PlatformUser |
| Organisation | org_{id} | Authorisation, org-scoped role | UserIdentity |

### JWT Claims

| Claim | Value | Purpose |
|-------|-------|---------|
| `sub` | UserIdentity.Id | Org-scoped user ID |
| `platform_user_id` | PlatformUser.Id | Cross-org identity |
| `org_id` | Organization.Id | Current org scope |
| `org_name` | Organization.Name | Display name |
| roles | UserRole[] | Org-scoped permissions |

### Well-Known Organisation IDs

| ID | Purpose |
|----|---------|
| `00000000-0000-0000-0000-000000000001` | System Admin Org |
| `00000000-0000-0000-0000-000000000002` | Public Org |

## Testing

```bash
# Run all tests
dotnet test

# Run tenant service tests only
dotnet test --filter "FullyQualifiedName~Tenant"

# Key test areas:
# - PlatformUser CRUD and uniqueness
# - Social login flow (initiate → callback → PlatformUser resolution)
# - Org switching (JWT re-issuance)
# - Platform settings (enable/disable public org)
# - Org creation (blueprint + admin-initiated)
# - Permission boundaries (system admin audit access limits)
# - Bootstrap (two orgs + PlatformSettings created)
```
