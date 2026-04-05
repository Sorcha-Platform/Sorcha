# Sorcha Tenant Service Specification

**Version:** 1.1
**Date:** 2025-12-13
**Status:** Implementation Complete (Integration Pending)
**Related Constitution:** [constitution.md](../constitution.md)
**Related Tasks:** AUTH-001, AUTH-002, AUTH-003 in [MASTER-TASKS.md](../MASTER-TASKS.md)

---

## Executive Summary

The Sorcha Tenant Service is the **central authentication and authorization hub** for the Sorcha platform. It provides:

- **Multi-tenant organization management** - Isolated organizations with subdomain routing
- **JWT-based authentication** - Industry-standard OAuth2/JWT tokens with configurable lifetimes
- **Service-to-service authentication** - OAuth2 client credentials flow with service principals
- **Delegated authority tokens** - Services acting on behalf of authenticated users
- **Role-based authorization (RBAC)** - Fine-grained permission policies
- **Token lifecycle management** - Redis-backed token revocation and introspection
- **Identity provider integration** (Planned) - Azure AD, Azure B2C, PassKey/WebAuthn

**Current Status:**
- ✅ Core JWT authentication and token service (100%)
- ✅ Organization and user management (100%)
- ✅ Service principal management (100%)
- ✅ Authorization policies (100%)
- ✅ 67 integration tests passing (100%)
- ⚠️ Service integration pending (Blueprint, Wallet, Register services need to integrate)
- ❌ Identity provider integration not yet implemented (Azure AD, PassKey)

---

## Clarifications

### Session 2025-12-10

- Q: What is the target availability SLA for the Tenant Service in the MVD/production environment? → A: 99.5% uptime (4.38 hours downtime/month)
- Q: When Redis (token revocation store) becomes unavailable, how should the Tenant Service behave? → A: Degraded operation with caching (continue token operations with local JWT validation, log revocation attempts, warn about limited enforcement). Future enhancement: administrative control to change failure mode.
- Q: For local development and MVD deployment, how should the first administrator user and service principals be created (bootstrap problem)? → A: Seed script with documented credentials (automated script creates admin + service principals with test credentials from environment variables)
- Q: What is the horizontal scaling strategy for the Tenant Service (load balancing, state management, instance limits)? → A: Stateless horizontal scaling with Redis (multiple service instances behind load balancer, Redis shared state, no sticky sessions required)

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Authentication Flows](#authentication-flows)
3. [Token Types and Lifecycle](#token-types-and-lifecycle)
4. [API Endpoints](#api-endpoints)
5. [Data Models](#data-models)
6. [Authorization Policies](#authorization-policies)
7. [Service Integration Guide](#service-integration-guide)
8. [Security Considerations](#security-considerations)
9. [Configuration](#configuration)
10. [Testing Strategy](#testing-strategy)
11. [Future Enhancements](#future-enhancements)

---

## Architecture Overview

### System Context

```
┌─────────────────────────────────────────────────────────────────┐
│                        Sorcha Platform                          │
│                                                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐         │
│  │   Client     │  │  Blueprint   │  │   Wallet     │         │
│  │   Apps       │  │   Service    │  │   Service    │         │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘         │
│         │                 │                  │                  │
│         │  User Token     │ Service Token    │ Service Token    │
│         │  Delegation     │ + Delegation     │ + Delegation     │
│         │                 │                  │                  │
│         └─────────────────┼──────────────────┘                  │
│                          │                                      │
│                ┌─────────▼──────────┐                          │
│                │  Tenant Service    │                          │
│                │  (Auth Hub)        │                          │
│                └─────────┬──────────┘                          │
│                          │                                      │
│              ┌───────────┼───────────┐                         │
│              │           │           │                         │
│         ┌────▼────┐ ┌───▼────┐ ┌───▼────┐                    │
│         │ Postgres│ │ Redis  │ │ Azure  │                    │
│         │   DB    │ │ Cache  │ │KeyVault│                    │
│         └─────────┘ └────────┘ └────────┘                    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

**Tenant Service:**
- Issue and validate JWT access tokens
- Issue and validate JWT refresh tokens
- Manage service principals (service-to-service credentials)
- Issue delegation tokens for services acting on behalf of users
- Manage organizations (multi-tenant isolation)
- Manage users within organizations
- Enforce authorization policies
- Track and revoke tokens (Redis-backed)

**Other Services (Blueprint, Wallet, Register):**
- Validate incoming JWT tokens (local validation or introspection)
- Extract claims for authorization decisions
- Include delegation tokens when calling other services on behalf of users
- Register as service principals during deployment

### Technology Stack

- **.NET 10** - Latest .NET framework
- **PostgreSQL** - Organization and user data persistence
- **Redis** - Token revocation tracking and distributed caching
- **JWT (HS256)** - Token signing algorithm (symmetric key for development, RSA for production)
- **.NET Aspire** - Service orchestration and observability
- **Minimal APIs** - Modern endpoint routing
- **OpenAPI/Scalar** - API documentation
- **gRPC** - Internal service-to-service communication protocol

### Service Communication Protocol

**Tenant Service gRPC Contract:**
- **Protocol Buffer:** `src/Services/Sorcha.Tenant.Service/Protos/tenant.proto`
- **Service:** `sorcha.tenant.v1.TenantService`
- **RPCs:**
  - `IntrospectToken` - Validate JWT tokens and return claims
  - `GetDelegationToken` - Issue delegation tokens for service-to-service calls
  - `GetServiceToken` - Issue service tokens via OAuth2 client credentials
  - `RevokeToken` - Revoke specific tokens
- **Authentication:** Mutual TLS (mTLS) with service certificates
- **Transport:** HTTP/2 with multiplexing
- **Related:** [ADR-001: gRPC Service Communication](../adrs/adr-001-grpc-service-communication.md)
- **Related:** [Constitution v1.3](../constitution.md) - Service Communication Standards

**Internal Service-to-Service Communication:**
- All services communicate with Tenant Service via gRPC for authentication operations
- Service tokens issued via OAuth2 client credentials flow (gRPC)
- Delegation tokens included in gRPC metadata (`x-delegation-token`)
- mTLS ensures mutual authentication between services
- Service discovery via .NET Aspire

**External Client-Facing APIs:**
- **Protocol:** REST/HTTP (for user authentication endpoints)
- **Authentication:** JWT Bearer tokens
- **Documentation:** OpenAPI 3.0 with Scalar UI
- **Note:** Client applications use REST, internal services use gRPC

### Operational Requirements

**Availability:**
- **Target SLA:** 99.5% uptime (4.38 hours downtime/month)
- Allows planned maintenance windows while maintaining reliability for MVD/production
- Single-region deployment acceptable for MVD phase
- Planned maintenance should be scheduled during low-traffic periods
- Critical dependency: Service downtime blocks all authentication across platform

**Scalability:**
- **Architecture:** Stateless horizontal scaling
- **Load Balancing:** Round-robin or least-connections behind Azure Load Balancer / Application Gateway
- **State Management:** Redis provides shared state for token revocation across all instances
- **No Sticky Sessions Required:** JWT validation is stateless; any instance can validate any token
- **Instance Scaling:**
  - **MVD Phase:** 2-3 instances (high availability + rolling updates)
  - **Production:** Auto-scale based on CPU/memory (min 3, max 10 instances)
  - **Performance Target:** Each instance handles ~1000 token operations/second
- **Database Connection Pooling:** Each instance maintains connection pool to PostgreSQL (max 20 connections per instance)
- **Redis Connection:** Single connection per instance with automatic reconnection

**Dependencies:**
- PostgreSQL (organizations, users, service principals)
- Redis (token revocation, caching, shared state across instances)
- Network connectivity to Azure AD/B2C (future)
- Load balancer (Azure Application Gateway or equivalent)

**Failure Mode Behavior:**

*Redis Unavailable:*
- **Immediate behavior:** Degraded operation mode
  - Continue token issuance and validation using local JWT signature verification
  - Log token revocation attempts but do not block operations
  - Emit warning metrics indicating limited revocation enforcement
  - Tokens rely on expiry rather than real-time revocation checks
- **Monitoring:** Alert operators when Redis connection lost
- **Recovery:** Automatic reconnection when Redis becomes available
- **Future enhancement:** Administrative configuration to change failure mode (fail closed, manual intervention, etc.)

*PostgreSQL Unavailable:*
- Service cannot issue new tokens for new authentications (user/service principal data unavailable)
- Existing valid tokens continue to work for authorization (JWT validation is stateless)
- Token refresh operations fail (cannot validate user still exists/active)
- Return 503 Service Unavailable for operations requiring database access

---

## Authentication Flows

### 1. User Authentication Flow (Organization Users)

**Future Implementation - Azure AD Integration:**

```
┌──────┐                ┌──────────┐              ┌──────────┐
│Client│                │ Tenant   │              │ Azure AD │
│ App  │                │ Service  │              │          │
└──┬───┘                └────┬─────┘              └────┬─────┘
   │                         │                         │
   │ 1. Initiate Login       │                         │
   ├────────────────────────>│                         │
   │                         │                         │
   │ 2. Redirect to Azure AD │                         │
   │<────────────────────────┤                         │
   │                         │                         │
   │ 3. User authenticates   │                         │
   ├─────────────────────────┼────────────────────────>│
   │                         │                         │
   │ 4. Authorization code   │                         │
   │<────────────────────────┼─────────────────────────┤
   │                         │                         │
   │ 5. Exchange code        │                         │
   ├────────────────────────>│                         │
   │                         │                         │
   │                         │ 6. Validate with Azure  │
   │                         ├────────────────────────>│
   │                         │                         │
   │                         │ 7. User claims          │
   │                         │<────────────────────────┤
   │                         │                         │
   │ 8. Sorcha JWT tokens    │                         │
   │    (access + refresh)   │                         │
   │<────────────────────────┤                         │
   │                         │                         │
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresIn": 3600
}
```

**Access Token Claims:**
```json
{
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "email": "alice@acme.com",
  "name": "Alice Johnson",
  "org_id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "org_name": "Acme Corporation",
  "role": ["Administrator"],
  "token_type": "user",
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io",
  "exp": 1702396800,
  "iat": 1702393200,
  "jti": "a7b3c2d1-4e5f-6a7b-8c9d-0e1f2a3b4c5d"
}
```

### 2. Service-to-Service Authentication Flow (OAuth2 Client Credentials)

**Current Implementation - Fixed Credentials:**

```
┌──────────────┐              ┌──────────────┐
│  Blueprint   │              │   Tenant     │
│  Service     │              │   Service    │
└──────┬───────┘              └──────┬───────┘
       │                             │
       │ 1. Register Service         │
       │    (admin creates principal)│
       │                             │
       │<────────────────────────────┤
       │    Client ID: blueprint-svc │
       │    Client Secret: <secret>  │
       │                             │
       │ 2. Request Service Token    │
       │    POST /api/service-auth/token
       │    {                        │
       │      "grant_type": "client_credentials",
       │      "client_id": "blueprint-svc",
       │      "client_secret": "<secret>",
       │      "scope": "blueprints:write"
       │    }                        │
       ├────────────────────────────>│
       │                             │
       │                             │ 3. Validate credentials
       │                             │    against ServicePrincipal DB
       │                             │
       │ 4. Service Access Token     │
       │<────────────────────────────┤
       │    {                        │
       │      "access_token": "eyJ...",
       │      "token_type": "Bearer",
       │      "expires_in": 28800     │
       │    }                        │
       │                             │
```

**Service Token Claims:**
```json
{
  "sub": "blueprint-service-id",
  "client_id": "blueprint-svc",
  "service_name": "Sorcha.Blueprint.Service",
  "scope": ["blueprints:write", "blueprints:read"],
  "token_type": "service",
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io",
  "exp": 1702422400,
  "iat": 1702393600,
  "jti": "b8c4d3e2-5f6a-7b8c-9d0e-1f2a3b4c5d6e"
}
```

### 3. Delegated Authority Flow (Service Acting on Behalf of User)

**This is the CRITICAL flow for Blueprint Service executing actions:**

```
┌──────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐
│Client│    │ Tenant   │    │Blueprint │    │  Wallet  │
│ App  │    │ Service  │    │ Service  │    │ Service  │
└──┬───┘    └────┬─────┘    └────┬─────┘    └────┬─────┘
   │             │               │               │
   │ 1. User Login (Azure AD)   │               │
   ├────────────>│               │               │
   │             │               │               │
   │ 2. User Token              │               │
   │<────────────┤               │               │
   │   (access + refresh)       │               │
   │                            │               │
   │ 3. Execute Blueprint Action│               │
   │    POST /instances/{id}/actions/{actionId}/execute
   │    X-Delegation-Token: <user-token>        │
   ├────────────────────────────>│               │
   │                            │               │
   │                            │ 4. Get Service Token
   │                            │    (if not cached)
   │                            ├──────────────>│
   │                            │               │
   │                            │ 5. Service Token
   │                            │<──────────────┤
   │                            │               │
   │                            │ 6. Request Delegation Token
   │                            │    POST /api/service-auth/token/delegated
   │                            │    {
   │                            │      "client_id": "blueprint-svc",
   │                            │      "client_secret": "<secret>",
   │                            │      "delegated_user_id": "550e8400-...",
   │                            │      "delegated_org_id": "7c9e6679-...",
   │                            │      "scope": "wallets:sign"
   │                            │    }
   │                            ├──────────────>│
   │                            │               │
   │                            │ 7. Delegation Token
   │                            │<──────────────┤
   │                            │               │
   │                            │ 8. Sign Transaction
   │                            │    POST /wallets/{id}/sign
   │                            │    Authorization: Bearer <service-token>
   │                            │    X-Delegation-Token: <delegation-token>
   │                            ├──────────────────────────>│
   │                            │               │
   │                            │               │ 9. Validate both tokens
   │                            │               │    - Service token (Blueprint identity)
   │                            │               │    - Delegation token (user authority)
   │                            │               │
   │                            │ 10. Signed TX │
   │                            │<──────────────────────────┤
   │                            │               │
   │ 11. Action Result          │               │
   │<────────────────────────────┤               │
   │                            │               │
```

**Delegation Token Claims:**
```json
{
  "sub": "blueprint-service-id",
  "client_id": "blueprint-svc",
  "service_name": "Sorcha.Blueprint.Service",
  "token_type": "service",
  "delegated_user_id": "550e8400-e29b-41d4-a716-446655440000",
  "delegated_org_id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "scope": ["wallets:sign"],
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io",
  "exp": 1702422400,
  "iat": 1702393600,
  "jti": "c9d5e4f3-6a7b-8c9d-0e1f-2a3b4c5d6e7f"
}
```

### 4. Token Refresh Flow

```
┌──────┐              ┌──────────┐
│Client│              │  Tenant  │
│ App  │              │  Service │
└──┬───┘              └────┬─────┘
   │                       │
   │ 1. Access token expired
   │                       │
   │ 2. Refresh Token      │
   │    POST /api/auth/token/refresh
   │    {                  │
   │      "refresh_token": "eyJ..."
   │    }                  │
   ├──────────────────────>│
   │                       │
   │                       │ 3. Validate refresh token
   │                       │    - Check signature
   │                       │    - Check expiry
   │                       │    - Check revocation (Redis)
   │                       │
   │ 4. New Access Token   │
   │<──────────────────────┤
   │    {                  │
   │      "access_token": "eyJ...",
   │      "refresh_token": "eyJ...", (same)
   │      "expires_in": 3600
   │    }                  │
   │                       │
```

---

## Token Types and Lifecycle

### Token Types

| Token Type | Lifetime | Use Case | Refresh? |
|------------|----------|----------|----------|
| **User Access Token** | 60 minutes (configurable) | API access for authenticated users | Yes (via refresh token) |
| **User Refresh Token** | 24 hours (configurable) | Obtain new access tokens | No |
| **Service Access Token** | 8 hours (configurable) | Service-to-service authentication | No (request new token) |
| **Delegation Token** | 8 hours (configurable) | Service acting on behalf of user | No |

### Token Validation Strategies

**Both local validation AND centralized introspection** (as per your requirement):

#### Local Validation (Fast Path)
```csharp
// Each service validates JWT signature locally
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://tenant.sorcha.io",
            ValidateAudience = true,
            ValidAudiences = new[] { "https://api.sorcha.io" },
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    });
```

**Advantages:**
- ✅ Fast (no network call)
- ✅ Offline validation possible
- ✅ Scales well under high load

**Disadvantages:**
- ❌ Cannot detect revoked tokens immediately (relies on expiry)
- ❌ Requires shared signing key distribution

#### Centralized Introspection (Real-time Revocation Check)
```http
POST /api/auth/token/introspect HTTP/1.1
Authorization: Bearer <service-token>
Content-Type: application/json

{
  "token": "eyJhbGciOiJIUzI1NiIs..."
}
```

**Response:**
```json
{
  "active": true,
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "client_id": "blueprint-svc",
  "scope": "blueprints:write",
  "exp": 1702396800,
  "iat": 1702393200,
  "jti": "a7b3c2d1-4e5f-6a7b-8c9d-0e1f2a3b4c5d",
  "org_id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "token_type": "Bearer"
}
```

**Advantages:**
- ✅ Real-time revocation detection
- ✅ Centralized audit trail
- ✅ Can return extended claims

**Disadvantages:**
- ❌ Network latency overhead
- ❌ Tenant Service becomes critical path
- ❌ Requires service authentication

#### Recommended Hybrid Approach

**For most requests:**
1. Validate JWT signature locally (fast)
2. Check claims for authorization decisions
3. Accept token if valid and not expired

**For sensitive operations:**
1. Validate JWT signature locally
2. Call introspection endpoint to check revocation status
3. Proceed only if `active: true`

**Implementation:**
```csharp
// Regular endpoint - local validation only
app.MapGet("/api/blueprints", GetBlueprints)
    .RequireAuthorization();

// Sensitive endpoint - local + introspection
app.MapPost("/api/blueprints/{id}/publish", PublishBlueprint)
    .RequireAuthorization()
    .AddEndpointFilter(async (context, next) =>
    {
        var token = context.HttpContext.GetToken();
        var isActive = await tenantClient.IntrospectTokenAsync(token);
        if (!isActive)
            return Results.Unauthorized();
        return await next(context);
    });
```

### Token Revocation

**Revocation Mechanisms:**

1. **Individual Token Revocation**
   ```http
   POST /api/auth/token/revoke
   Authorization: Bearer <token>

   {
     "token": "eyJhbGciOiJIUzI1NiIs..."
   }
   ```

2. **User Token Revocation** (Admin only)
   ```http
   POST /api/auth/token/revoke-user
   Authorization: Bearer <admin-token>

   {
     "userId": "550e8400-e29b-41d4-a716-446655440000"
   }
   ```

3. **Organization Token Revocation** (Admin only)
   ```http
   POST /api/auth/token/revoke-organization
   Authorization: Bearer <admin-token>

   {
     "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
   }
   ```

**Redis Implementation:**
- Revoked tokens stored in Redis with TTL matching token expiry
- Key pattern: `revoked:token:{jti}`
- Bulk revocation uses secondary indices: `revoked:user:{userId}` and `revoked:org:{orgId}`
- Tokens checked against Redis on introspection requests
- Local validation does NOT check Redis (trade-off for performance)

---

## API Endpoints

### Authentication Endpoints

#### `POST /api/service-auth/token`
**OAuth2 Client Credentials - Get Service Token**

**Request:**
```json
{
  "grant_type": "client_credentials",
  "client_id": "blueprint-svc",
  "client_secret": "sk_live_abc123...",
  "scope": "blueprints:write blueprints:read"
}
```

**Response:**
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 28800,
  "scope": "blueprints:write blueprints:read"
}
```

**Status Codes:**
- `200 OK` - Token issued
- `400 Bad Request` - Invalid grant_type or missing parameters
- `401 Unauthorized` - Invalid credentials

---

#### `POST /api/service-auth/token/delegated`
**Get Delegation Token (Service Acting on Behalf of User)**

**Request:**
```json
{
  "client_id": "blueprint-svc",
  "client_secret": "sk_live_abc123...",
  "delegated_user_id": "550e8400-e29b-41d4-a716-446655440000",
  "delegated_organization_id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "scope": "wallets:sign registers:write"
}
```

**Response:**
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 28800,
  "scope": "wallets:sign registers:write"
}
```

**Status Codes:**
- `200 OK` - Delegation token issued
- `400 Bad Request` - Missing delegated_user_id
- `401 Unauthorized` - Invalid service credentials

---

#### `POST /api/auth/token/refresh`
**Refresh Access Token**

**Request:**
```json
{
  "refresh_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response:**
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refresh_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expires_in": 3600
}
```

---

#### `POST /api/auth/token/revoke`
**Revoke a Token**

**Authorization:** Bearer token required

**Request:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response:**
```json
{
  "success": true,
  "message": "Token revoked successfully"
}
```

---

#### `POST /api/auth/token/introspect`
**Introspect Token (Service-to-Service Only)**

**Authorization:** Service token required (`RequireService` policy)

**Request:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Response:**
```json
{
  "active": true,
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "client_id": "blueprint-svc",
  "scope": "blueprints:write",
  "exp": 1702396800,
  "iat": 1702393200,
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io",
  "token_type": "Bearer",
  "jti": "a7b3c2d1-4e5f-6a7b-8c9d-0e1f2a3b4c5d",
  "org_id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "roles": ["Administrator"]
}
```

---

#### `GET /api/auth/me`
**Get Current User Information**

**Authorization:** Bearer token required

**Response:**
```json
{
  "userId": "550e8400-e29b-41d4-a716-446655440000",
  "email": "alice@acme.com",
  "name": "Alice Johnson",
  "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "organizationName": "Acme Corporation",
  "tokenType": "user",
  "roles": ["Administrator"],
  "scopes": [],
  "authMethod": "oidc"
}
```

---

#### `POST /api/auth/logout`
**Logout (Revoke Current Token)**

**Authorization:** Bearer token required

**Response:**
```json
{
  "success": true,
  "message": "Logged out successfully"
}
```

---

### Organization Management Endpoints

#### `POST /api/organizations`
**Create Organization**

**Authorization:** Authenticated user

**Request:**
```json
{
  "name": "Acme Corporation",
  "subdomain": "acme",
  "branding": {
    "logoUrl": "https://acme.com/logo.png",
    "primaryColor": "#0078D4",
    "secondaryColor": "#50E6FF",
    "companyTagline": "Innovation at Scale"
  }
}
```

**Response:**
```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "name": "Acme Corporation",
  "subdomain": "acme",
  "status": "Active",
  "createdAt": "2025-12-10T14:30:00Z",
  "branding": {
    "logoUrl": "https://acme.com/logo.png",
    "primaryColor": "#0078D4",
    "secondaryColor": "#50E6FF",
    "companyTagline": "Innovation at Scale"
  }
}
```

**Status Codes:**
- `201 Created` - Organization created
- `400 Bad Request` - Invalid subdomain (too short, reserved, already taken)
- `401 Unauthorized` - Not authenticated

---

#### `GET /api/organizations`
**List Organizations**

**Authorization:** Administrator role required

**Query Parameters:**
- `includeInactive` (bool) - Include suspended/deleted organizations

**Response:**
```json
{
  "organizations": [
    {
      "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "name": "Acme Corporation",
      "subdomain": "acme",
      "status": "Active",
      "createdAt": "2025-12-10T14:30:00Z"
    }
  ],
  "total": 1
}
```

---

#### `GET /api/organizations/{id}`
**Get Organization Details**

**Authorization:** Organization member

**Response:**
```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "name": "Acme Corporation",
  "subdomain": "acme",
  "status": "Active",
  "createdAt": "2025-12-10T14:30:00Z",
  "branding": {
    "logoUrl": "https://acme.com/logo.png",
    "primaryColor": "#0078D4"
  }
}
```

---

#### `GET /api/organizations/by-subdomain/{subdomain}`
**Get Organization by Subdomain**

**Authorization:** None (public endpoint for routing)

**Response:**
```json
{
  "id": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "name": "Acme Corporation",
  "subdomain": "acme",
  "status": "Active"
}
```

---

#### `GET /api/organizations/validate-subdomain/{subdomain}`
**Validate Subdomain Availability**

**Authorization:** None (public)

**Response:**
```json
{
  "subdomain": "acme",
  "isValid": false,
  "errorMessage": "Subdomain 'acme' is already taken"
}
```

---

### Service Principal Management Endpoints

#### `POST /api/service-principals`
**Register Service Principal**

**Authorization:** Administrator only

**Request:**
```json
{
  "serviceName": "Sorcha.Blueprint.Service",
  "scopes": [
    "blueprints:write",
    "blueprints:read",
    "wallets:sign",
    "registers:write"
  ]
}
```

**Response:**
```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "serviceName": "Sorcha.Blueprint.Service",
  "clientId": "blueprint-svc-20251210",
  "clientSecret": "sk_live_a7b3c2d1_4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b",
  "scopes": [
    "blueprints:write",
    "blueprints:read",
    "wallets:sign",
    "registers:write"
  ],
  "status": "Active",
  "createdAt": "2025-12-10T14:45:00Z"
}
```

**⚠️ SECURITY WARNING:** The `clientSecret` is only returned ONCE during registration. Store it securely!

---

#### `GET /api/service-principals`
**List Service Principals**

**Authorization:** Administrator only

**Response:**
```json
{
  "servicePrincipals": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "serviceName": "Sorcha.Blueprint.Service",
      "clientId": "blueprint-svc-20251210",
      "scopes": ["blueprints:write", "blueprints:read"],
      "status": "Active",
      "createdAt": "2025-12-10T14:45:00Z"
    }
  ],
  "total": 1
}
```

---

#### `PUT /api/service-principals/{id}/scopes`
**Update Service Principal Scopes**

**Authorization:** Administrator only

**Request:**
```json
{
  "scopes": [
    "blueprints:write",
    "blueprints:read",
    "wallets:sign"
  ]
}
```

---

#### `POST /api/service-principals/{id}/suspend`
**Suspend Service Principal**

**Authorization:** Administrator only

**Response:**
```json
{
  "success": true,
  "message": "Service principal suspended"
}
```

---

#### `POST /api/service-principals/{id}/reactivate`
**Reactivate Service Principal**

**Authorization:** Administrator only

---

#### `DELETE /api/service-principals/{id}`
**Revoke Service Principal**

**Authorization:** Administrator only

**Status Codes:**
- `204 No Content` - Service principal revoked
- `404 Not Found` - Service principal not found

---

#### `POST /api/service-auth/rotate-secret`
**Rotate Service Principal Secret**

**Authorization:** None (requires current secret)

**Request:**
```json
{
  "currentSecret": "sk_live_old_secret..."
}
```

**Query Parameters:**
- `clientId` (required) - Service client ID

**Response:**
```json
{
  "clientId": "blueprint-svc-20251210",
  "newClientSecret": "sk_live_new_secret_b8c4d3e25f6a7b8c9d0e1f2a3b4c5d6e",
  "expiresAt": "2025-12-17T14:50:00Z"
}
```

---

### User Management Endpoints

#### `POST /api/organizations/{organizationId}/users`
**Add User to Organization**

**Authorization:** Administrator only

**Request:**
```json
{
  "email": "bob@acme.com",
  "displayName": "Bob Smith",
  "roles": ["Member"]
}
```

**Response:**
```json
{
  "id": "b2c3d4e5-f6a7-8901-bcde-f23456789012",
  "email": "bob@acme.com",
  "displayName": "Bob Smith",
  "roles": ["Member"],
  "status": "Active",
  "createdAt": "2025-12-10T15:00:00Z"
}
```

---

#### `GET /api/organizations/{organizationId}/users`
**List Organization Users**

**Authorization:** Organization member

**Response:**
```json
{
  "users": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "email": "alice@acme.com",
      "displayName": "Alice Johnson",
      "roles": ["Administrator"],
      "status": "Active"
    }
  ],
  "total": 1
}
```

---

## Data Models

### Organization
```csharp
public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Subdomain { get; set; }
    public OrganizationStatus Status { get; set; }
    public Guid? CreatorIdentityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public BrandingConfiguration? Branding { get; set; }
    public IdentityProviderConfiguration? IdentityProvider { get; set; }
}

public enum OrganizationStatus
{
    Active,
    Suspended,
    Deleted
}
```

### UserIdentity
```csharp
public class UserIdentity
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string DisplayName { get; set; }
    public Guid OrganizationId { get; set; }
    public UserRole[] Roles { get; set; }
    public UserStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public enum UserRole
{
    Administrator,
    Member,
    Auditor
}
```

### ServicePrincipal
```csharp
public class ServicePrincipal
{
    public Guid Id { get; set; }
    public string ServiceName { get; set; }
    public string ClientId { get; set; }
    public string ClientSecretHash { get; set; }
    public string[] Scopes { get; set; }
    public ServicePrincipalStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
```

---

## Authorization Policies

### Built-in Policies

| Policy Name | Requirements | Use Case |
|-------------|--------------|----------|
| `RequireAdministrator` | Role: Administrator | Organization admin operations |
| `RequireAuditor` | Role: Administrator OR Auditor | Read-only access to audit logs |
| `RequireOrganizationMember` | Claim: org_id | Organization-scoped operations |
| `RequireService` | Claim: token_type = "service" | Service-to-service endpoints |
| `RequireDelegatedAuthority` | Claim: token_type = "service" AND delegated_user_id | Service acting on behalf of user |
| `RequirePublicUser` | Claim: token_type = "user" | Public user operations |
| `RequireAuthenticated` | Any valid token | General authenticated access |
| `CanCreateBlockchain` | Claim: can_create_blockchain = "true" | Blockchain creation (future) |
| `CanPublishBlueprint` | Claim: can_publish_blueprint = "true" | Blueprint publishing |

### Policy Usage Examples

```csharp
// Require organization administrator
app.MapPost("/api/organizations/{id}", UpdateOrganization)
    .RequireAuthorization("RequireAdministrator");

// Require service token
app.MapPost("/api/auth/token/introspect", IntrospectToken)
    .RequireAuthorization("RequireService");

// Require delegated authority
app.MapPost("/api/wallets/{id}/sign", SignTransaction)
    .RequireAuthorization("RequireDelegatedAuthority");
```

---

## Service Integration Guide

### Step 1: Register as Service Principal

**During deployment/setup, each service needs credentials:**

```bash
# Administrator creates service principal via Tenant Service API
curl -X POST https://tenant.sorcha.io/api/service-principals \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  -d '{
    "serviceName": "Sorcha.Blueprint.Service",
    "scopes": ["blueprints:write", "wallets:sign", "registers:write"]
  }'

# Response includes client_id and client_secret (store securely!)
```

**Configuration:**
```json
{
  "TenantService": {
    "BaseUrl": "https://tenant.sorcha.io",
    "ClientId": "blueprint-svc-20251210",
    "ClientSecret": "<from-environment-variable>",
    "Scopes": "blueprints:write wallets:sign registers:write"
  }
}
```

### Step 2: Implement Token Acquisition

```csharp
public class TenantServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;

    public async Task<string> GetServiceTokenAsync(CancellationToken ct = default)
    {
        // Return cached token if still valid
        if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            return _cachedToken;
        }

        // Request new service token
        var request = new
        {
            grant_type = "client_credentials",
            client_id = _clientId,
            client_secret = _clientSecret,
            scope = "blueprints:write wallets:sign"
        };

        var response = await _httpClient.PostAsJsonAsync("/api/service-auth/token", request, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);

        _cachedToken = tokenResponse.AccessToken;
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

        return _cachedToken;
    }

    public async Task<string> GetDelegationTokenAsync(
        Guid userId,
        Guid? orgId,
        string scope,
        CancellationToken ct = default)
    {
        var request = new
        {
            client_id = _clientId,
            client_secret = _clientSecret,
            delegated_user_id = userId,
            delegated_organization_id = orgId,
            scope
        };

        var response = await _httpClient.PostAsJsonAsync(
            "/api/service-auth/token/delegated", request, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return tokenResponse.AccessToken;
    }
}
```

### Step 3: Configure JWT Authentication

```csharp
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://tenant.sorcha.io";
        options.Audience = "https://api.sorcha.io";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "https://tenant.sorcha.io",
            ValidateAudience = true,
            ValidAudiences = new[] { "https://api.sorcha.io" },
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetSigningKeyFromConfig(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Import standard policies from Tenant Service
    options.AddPolicy("RequireService", policy =>
        policy.RequireClaim("token_type", "service"));

    options.AddPolicy("RequireDelegatedAuthority", policy =>
    {
        policy.RequireClaim("token_type", "service");
        policy.RequireClaim("delegated_user_id");
    });
});
```

### Step 4: Use Delegation Tokens in Endpoints

```csharp
// Blueprint Service - Action execution endpoint
app.MapPost("/instances/{instanceId}/actions/{actionId}/execute",
    async (
        HttpContext context,
        string instanceId,
        int actionId,
        ActionSubmissionRequest request,
        TenantServiceClient tenantClient,
        WalletServiceClient walletClient) =>
{
    // 1. Extract user delegation token from header
    var userDelegationToken = context.Request.Headers["X-Delegation-Token"].FirstOrDefault();

    if (string.IsNullOrEmpty(userDelegationToken))
    {
        return Results.BadRequest(new { error = "X-Delegation-Token header is required" });
    }

    // 2. Validate user delegation token via introspection
    var introspection = await tenantClient.IntrospectTokenAsync(userDelegationToken);

    if (!introspection.Active)
    {
        return Results.Unauthorized();
    }

    var userId = Guid.Parse(introspection.Sub);
    var orgId = introspection.OrgId != null ? Guid.Parse(introspection.OrgId) : (Guid?)null;

    // 3. Execute action (may need to call Wallet Service)

    // 4. Get delegation token for Wallet Service call
    var walletDelegationToken = await tenantClient.GetDelegationTokenAsync(
        userId, orgId, "wallets:sign");

    // 5. Call Wallet Service with delegation
    var signature = await walletClient.SignTransactionAsync(
        walletAddress,
        transaction,
        walletDelegationToken);

    // 6. Return result
    return Results.Ok(new { transactionHash = signature.Hash });
})
.RequireAuthorization(); // Requires valid JWT (service or user)
```

### Step 5: Implement Token Introspection (Sensitive Operations)

```csharp
public class TenantServiceClient
{
    public async Task<TokenIntrospectionResponse> IntrospectTokenAsync(
        string token,
        CancellationToken ct = default)
    {
        // Get our service token first
        var serviceToken = await GetServiceTokenAsync(ct);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/token/introspect");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
        request.Content = JsonContent.Create(new { token });

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TokenIntrospectionResponse>(ct);
    }
}
```

---

## Security Considerations

### 1. Token Storage

**DO:**
- ✅ Store tokens in secure, encrypted storage (Azure Key Vault in production)
- ✅ Use HttpOnly cookies for web clients
- ✅ Clear tokens on logout
- ✅ Use short-lived access tokens (60 minutes)

**DON'T:**
- ❌ Store tokens in localStorage (XSS vulnerability)
- ❌ Log tokens in application logs
- ❌ Commit tokens to source control
- ❌ Send tokens over HTTP (HTTPS only)

### 2. Service Principal Secrets

**Generation:**
- Cryptographically secure random 256-bit secrets
- Prefix: `sk_live_` (production), `sk_dev_` (development)
- One-time display on creation
- BCrypt hashing for storage (cost factor 12)

**Rotation:**
- Regular rotation every 90 days recommended
- Grace period: Old secret valid for 24 hours after rotation
- Audit log of all secret rotations

### 3. Token Signing Keys

**Development:**
- Symmetric key (HS256) from configuration
- Minimum 256 bits
- Shared across services via secure configuration

**Production:**
- Asymmetric keys (RS256) from Azure Key Vault
- Public key distribution via JWKS endpoint
- Automatic key rotation every 6 months

### 4. HTTPS/TLS

**All communication must use HTTPS:**
- TLS 1.2 minimum (TLS 1.3 recommended)
- Certificate validation enforced
- HSTS headers on all responses

### 5. Rate Limiting

Uses the centralised `RateLimitSettings` from ServiceDefaults via `builder.AddRateLimiting()`. All limits are configurable via the `"RateLimiting"` section of `appsettings.json`.

**Tenant-specific policies applied to endpoints:**
- TOTP/2FA validation: `totp-validate` policy (per IP)
- Platform auth (login, registration, social, passkeys): `platform-auth` policy (per IP)
- All other endpoints: `api` default policy (per IP)

```csharp
// Single call registers all standard policies from RateLimitSettings
builder.AddRateLimiting();
```

Override limits in `appsettings.json`:
```json
{
  "RateLimiting": {
    "TotpPermitLimit": 5,
    "PlatformAuthPermitLimit": 5
  }
}
```

### 6. Audit Logging

**All security events logged:**
- Token issuance (user, service, delegation)
- Token revocation
- Failed authentication attempts
- Service principal creation/modification
- User permission changes

**Log format:**
```json
{
  "timestamp": "2025-12-10T15:30:00Z",
  "event": "token.issued",
  "userId": "550e8400-e29b-41d4-a716-446655440000",
  "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "tokenType": "user",
  "jti": "a7b3c2d1-4e5f-6a7b-8c9d-0e1f2a3b4c5d",
  "clientIp": "203.0.113.45",
  "userAgent": "Mozilla/5.0..."
}
```

---

## Configuration

### appsettings.json

```json
{
  "JwtSettings": {
    "Issuer": "https://tenant.sorcha.io",
    "Audiences": ["https://api.sorcha.io"],
    "SigningKey": "${JWT_SIGNING_KEY}",
    "AccessTokenLifetimeMinutes": 60,
    "RefreshTokenLifetimeHours": 24,
    "ServiceTokenLifetimeHours": 8,
    "ClockSkewMinutes": 5,
    "ValidateIssuer": true,
    "ValidateAudience": true,
    "ValidateIssuerSigningKey": true,
    "ValidateLifetime": true
  },
  "ConnectionStrings": {
    "PostgreSQL": "${POSTGRES_CONNECTION_STRING}",
    "Redis": "${REDIS_CONNECTION_STRING}"
  },
  "RateLimiting": {
    "EnableRateLimiting": true,
    "PermitLimit": 60,
    "Window": 60,
    "QueueLimit": 10
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  }
}
```

### Environment Variables (Production)

```bash
JWT_SIGNING_KEY=<256-bit-key-from-azure-key-vault>
POSTGRES_CONNECTION_STRING=<azure-postgres-connection>
REDIS_CONNECTION_STRING=<azure-redis-connection>
ASPNETCORE_ENVIRONMENT=Production
```

### Bootstrap & Seeding (Development/MVD)

**Problem:** Initial deployment requires administrator user and service principals, but creating them requires authentication (chicken-and-egg).

**Solution:** Automated seed script with documented test credentials

**Seed Script Location:**
- `scripts/seed-tenant-service.ps1` (PowerShell)
- `scripts/seed-tenant-service.sh` (Bash)

**Seed Script Actions:**
1. Check if default admin user exists (email: `admin@sorcha.local`)
2. If not exists, create:
   - Default organization: "Sorcha Platform" (subdomain: `sorcha`)
   - Default administrator user with documented test credentials
   - Service principals for each platform service (Blueprint, Wallet, Register, Peer)
3. Output service principal credentials to console (client_id + client_secret)
4. Optionally write credentials to `.env.local` file (gitignored)

**Test Credentials (.env.example):**
```bash
# Default administrator (local development only - CHANGE IN PRODUCTION)
TENANT_ADMIN_EMAIL=admin@sorcha.local
TENANT_ADMIN_PASSWORD=Dev_Pass_2025!

# Service principal credentials (generated by seed script)
BLUEPRINT_SERVICE_CLIENT_ID=<generated-by-seed-script>
BLUEPRINT_SERVICE_CLIENT_SECRET=<generated-by-seed-script>
WALLET_SERVICE_CLIENT_ID=<generated-by-seed-script>
WALLET_SERVICE_CLIENT_SECRET=<generated-by-seed-script>
REGISTER_SERVICE_CLIENT_ID=<generated-by-seed-script>
REGISTER_SERVICE_CLIENT_SECRET=<generated-by-seed-script>
```

**Usage:**
```bash
# Local development
cd scripts
./seed-tenant-service.ps1 -Environment Development

# MVD deployment (Azure)
./seed-tenant-service.ps1 -Environment Staging -AdminEmail mvd-admin@company.com
```

**Security Notes:**
- Seed script only runs when database is empty OR explicit `--force` flag provided
- Default credentials must be changed on first login in non-development environments
- Service principal secrets displayed only once (store in Azure Key Vault for production)
- Script logs all actions to audit log
- Production deployment should use Azure AD admin instead of default credentials

**Implementation Status:**
- [ ] Create PowerShell seed script
- [ ] Create Bash seed script
- [ ] Add to deployment documentation
- [ ] Integrate with .NET Aspire startup

---

## Testing Strategy

### Unit Tests (85% coverage achieved)

**Token Service Tests:**
- Token generation with correct claims
- Token expiry validation
- Refresh token flow
- Token revocation logic
- Delegation token creation

**Organization Service Tests:**
- Organization CRUD operations
- Subdomain validation rules
- User membership management

### Integration Tests (67 tests passing)

**Authentication Flow Tests:**
```csharp
[Fact]
public async Task GetServiceToken_ShouldReturnToken_WithValidCredentials()
{
    // Arrange
    var request = new ClientCredentialsRequest
    {
        GrantType = "client_credentials",
        ClientId = "test-service",
        ClientSecret = "test-secret"
    };

    // Act
    var response = await _client.PostAsJsonAsync("/api/service-auth/token", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
    tokenResponse.AccessToken.Should().NotBeNullOrEmpty();
    tokenResponse.ExpiresIn.Should().BeGreaterThan(0);
}
```

**Authorization Policy Tests:**
```csharp
[Fact]
public async Task ListOrganizations_ShouldReturnForbidden_WhenNotAdmin()
{
    // Arrange
    var token = GenerateUserToken(roles: new[] { "Member" });
    _client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);

    // Act
    var response = await _client.GetAsync("/api/organizations");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

### Performance Tests

**Token generation throughput:**
- Target: 1000 tokens/second
- Current: ~850 tokens/second (acceptable)

**Introspection latency:**
- Target: <10ms (Redis cache hit)
- Current: ~8ms (average)

---

## Future Enhancements

### Phase 1 (Q1 2026): Identity Provider Integration

**Azure AD Integration:**
- OIDC authentication flow
- Group-based role mapping
- Automatic user provisioning

**Azure B2C Integration:**
- External user authentication
- Social identity providers (Google, Microsoft, GitHub)
- Custom branding per organization

**Implementation Effort:** 40 hours

---

### Phase 2 (Q2 2026): PassKey/WebAuthn Support

**Public User Authentication:**
- PassKey registration and authentication
- Device management
- Biometric authentication
- FIDO2 compliance

**Implementation Effort:** 60 hours

---

### Phase 3 (Q2 2026): Advanced Authorization

**Fine-grained Permissions:**
- Resource-level permissions (specific blueprints, wallets)
- Attribute-based access control (ABAC)
- Dynamic policy evaluation

**Implementation Effort:** 50 hours

---

### Phase 4 (Q3 2026): Multi-Region Support

**Geographic Token Issuance:**
- Region-specific token issuers
- Cross-region token validation
- Latency optimization

**Implementation Effort:** 80 hours

---

### Phase 5 (Q3 2026): Operational Resilience Enhancements

**Configurable Failure Modes:**
- Administrative configuration for Redis failure behavior (degraded/fail-closed/manual)
- Dashboard for monitoring service health and failure states
- Automated failover for Redis and PostgreSQL
- Circuit breaker patterns for external dependencies
- Graceful degradation controls per-endpoint

**Implementation Effort:** 30 hours

---

## Implementation Checklist

### ✅ Completed (100%)

- [x] JWT token service with HS256 signing
- [x] User access and refresh tokens
- [x] Service access tokens (client credentials)
- [x] Delegation tokens for service-to-service
- [x] Token revocation (Redis-backed)
- [x] Token introspection endpoint
- [x] Organization management (CRUD)
- [x] User management within organizations
- [x] Service principal management
- [x] Authorization policies (9 policies)
- [x] PostgreSQL data persistence
- [x] Redis caching and revocation
- [x] Rate limiting per IP
- [x] Audit logging
- [x] 67 integration tests
- [x] OpenAPI documentation

### ⚠️ In Progress (Service Integration)

- [ ] Bootstrap seed script (PowerShell + Bash)
- [ ] Default admin user creation workflow
- [ ] Service principal auto-provisioning in seed script
- [ ] Blueprint Service integration (AUTH-001)
- [ ] Wallet Service integration (AUTH-001)
- [ ] Register Service integration (AUTH-001)
- [ ] Peer Service integration (AUTH-001)
- [ ] API Gateway integration (AUTH-001)
- [ ] Update MASTER-TASKS.md with correct status

### ❌ Not Started (Future Features)

- [ ] Azure AD OIDC integration (AUTH-003)
- [ ] Azure B2C integration (AUTH-003)
- [ ] PassKey/WebAuthn support (AUTH-003)
- [ ] JWKS endpoint for public key distribution
- [ ] RS256 asymmetric signing (production)
- [ ] Azure Key Vault integration
- [ ] Multi-region support
- [ ] Advanced ABAC policies

---

## Appendix A: Error Codes

| Code | HTTP Status | Description | Resolution |
|------|-------------|-------------|------------|
| `invalid_grant` | 400 | Invalid grant type | Use `client_credentials` |
| `invalid_client` | 401 | Invalid client credentials | Check client_id and client_secret |
| `invalid_token` | 401 | Token validation failed | Token expired or invalid signature |
| `token_revoked` | 401 | Token has been revoked | Obtain new token |
| `insufficient_scope` | 403 | Insufficient permissions | Request token with required scope |
| `subdomain_taken` | 400 | Subdomain already in use | Choose different subdomain |
| `subdomain_reserved` | 400 | Subdomain is reserved | Choose different subdomain |
| `rate_limit_exceeded` | 429 | Too many requests | Retry after delay |

---

## Appendix B: Sample Workflows

### Workflow 1: New Service Onboarding

```bash
# 1. Admin registers service principal
curl -X POST https://tenant.sorcha.io/api/service-principals \
  -H "Authorization: Bearer ${ADMIN_TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "serviceName": "Sorcha.NewService",
    "scopes": ["newservice:write"]
  }'

# 2. Store client_id and client_secret from response

# 3. Service requests token on startup
curl -X POST https://tenant.sorcha.io/api/service-auth/token \
  -H "Content-Type: application/json" \
  -d '{
    "grant_type": "client_credentials",
    "client_id": "newservice-20251210",
    "client_secret": "sk_live_abc123...",
    "scope": "newservice:write"
  }'

# 4. Use access token for authenticated requests
curl -X GET https://api.sorcha.io/some-resource \
  -H "Authorization: Bearer ${ACCESS_TOKEN}"
```

### Workflow 2: User Executes Blueprint Action

```bash
# 1. User authenticates (Azure AD - future)
# Gets user access token

# 2. User requests blueprint action execution
curl -X POST https://blueprint.sorcha.io/instances/${INSTANCE_ID}/actions/0/execute \
  -H "Authorization: Bearer ${USER_TOKEN}" \
  -H "X-Delegation-Token: ${USER_TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "actionData": { "amount": 100 },
    "participantId": "employee",
    "walletAddress": "ws11qq..."
  }'

# 3. Blueprint Service validates delegation token
curl -X POST https://tenant.sorcha.io/api/auth/token/introspect \
  -H "Authorization: Bearer ${BLUEPRINT_SERVICE_TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "token": "${USER_TOKEN}"
  }'

# 4. Blueprint Service gets delegation token for Wallet Service
curl -X POST https://tenant.sorcha.io/api/service-auth/token/delegated \
  -H "Content-Type: application/json" \
  -d '{
    "client_id": "blueprint-svc",
    "client_secret": "${BLUEPRINT_SECRET}",
    "delegated_user_id": "${USER_ID}",
    "delegated_organization_id": "${ORG_ID}",
    "scope": "wallets:sign"
  }'

# 5. Blueprint Service calls Wallet Service with delegation
curl -X POST https://wallet.sorcha.io/wallets/${WALLET_ID}/sign \
  -H "Authorization: Bearer ${BLUEPRINT_SERVICE_TOKEN}" \
  -H "X-Delegation-Token: ${DELEGATION_TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "transaction": "..."
  }'
```

---

**Document Status:** Implementation Complete - Integration Pending
**Next Steps:**
1. Integrate Blueprint Service with Tenant Service (AUTH-001)
2. Integrate Wallet Service with Tenant Service (AUTH-001)
3. Integrate Register Service with Tenant Service (AUTH-001)
4. Update MASTER-TASKS.md with correct status

**Questions/Clarifications Needed:**
- ✅ Delegation token flow confirmed (get token first in auth flow)
- ✅ Demo authentication approach confirmed (proper flow with fixed credentials)
- ✅ Token validation strategy confirmed (both local + introspection)
- ⏳ Azure AD integration timeline (deferred to Phase 1)
- ⏳ PassKey/WebAuthn priority (deferred to Phase 2)

---

**Version History:**
- v1.0 (2025-12-10): Initial comprehensive specification documenting existing implementation
- v0.1 (2025-11-13): Placeholder boilerplate
