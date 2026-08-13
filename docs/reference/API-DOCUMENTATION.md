# Sorcha Platform - API Documentation

**Version:** 2.5.0
**Last Updated:** 2026-07-21
**Status:** MVD Complete

---

## Table of Contents

1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Authentication](#authentication)
4. [Tenant Service API](#tenant-service-api)
5. [Platform Organisation Management API](#platform-organisation-management-api)
6. [Organization Identity & Admin API](#organization-identity--admin-api)
7. [Organisation Switching](#organisation-switching)
8. [Platform Settings](#platform-settings)
9. [Peer Service API](#peer-service-api)
10. [Blueprint Service API](#blueprint-service-api)
11. [Wallet Service API](#wallet-service-api)
12. [Register Service API](#register-service-api)
13. [Provenance API](#provenance-api)
14. [HAIP Service API](#haip-service-api)
15. [Action Workflow API](#action-workflow-api)
16. [Execution Helper API](#execution-helper-api)
17. [Real-time Notifications (SignalR)](#real-time-notifications-signalr)
18. [Error Handling](#error-handling)
19. [Rate Limiting](#rate-limiting)
20. [Code Examples](#code-examples)

---

## Overview

The Sorcha Platform provides a comprehensive REST API for building distributed ledger applications with blueprint-based workflows, secure wallet management, and transaction processing.

**Base URLs:**
- **Development (Local):** `http://localhost:5000`
- **Production:** `https://sorcha.dev/api`

**API Gateway:**
All services are accessed through the API Gateway which provides:
- Unified routing to all microservices
- Health aggregation across services
- Load balancing and failover
- Request logging and observability
- Security headers (OWASP best practices)

---

## Getting Started

### Prerequisites

- .NET 10 SDK
- Docker (optional, for containerized deployment)
- Redis (for caching and SignalR backplane)

### Quick Start

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Sorcha-Platform/Sorcha.git
   cd Sorcha
   ```

2. **Run with .NET Aspire:**
   ```bash
   dotnet run --project src/Apps/Sorcha.AppHost
   ```

3. **Access the API:**
   - API Gateway: http://localhost:5000
   - Swagger UI: http://localhost:5000/scalar/v1 (Blueprint Service)

### API Explorer

Visit the Scalar API documentation UI at `/scalar/v1` to explore all endpoints interactively.

---

## Authentication

### OAuth 2.0 Token Endpoint

The Sorcha Platform uses OAuth 2.0 for authentication. All protected endpoints require a valid JWT Bearer token.

**Token Endpoint (public, via the API Gateway):** `POST /api/service-auth/token`

**Supported Grant Types on the public endpoint:**
- `password` - User credentials (email + password)
- `refresh_token` - Browser/CLI session refresh

**`client_credentials` (service principal authentication) is internal-only** — see below. It is
**not** served on the public `/api/service-auth/token` route; the API Gateway proxies that route,
and serving `client_credentials` there would let anyone who obtained a service principal's
`client_id`/`client_secret` mint a service-tier token from outside the network (#1397).

### User Authentication (Password Grant)

```http
POST /api/service-auth/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&username=admin@sorcha.local&password=Dev_Pass_2025!
```

**Response:**
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "refresh_token_here"
}
```

### Service Principal Authentication (Client Credentials Grant) — internal-only

`client_credentials` is served only on `POST /api/internal/service-auth/token`, which the API
Gateway does **not** route (like the rest of `/api/internal/*`, it 404s from outside the Docker
network). Callers must reach the Tenant Service directly — this is how `Sorcha.ServiceClients`'
`ServiceAuthClient` already authenticates service-to-service.

```http
POST /api/internal/service-auth/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=service-blueprint&client_secret=<secret>
```

The delegated-authority (`POST /api/internal/service-auth/token/delegated`) and secret-rotation
(`POST /api/internal/service-auth/rotate-secret`) endpoints are internal-only for the same reason.

### Using the Access Token

Include the access token in the `Authorization` header:

```http
GET /api/organizations
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### Default Credentials (Staging Environment)

**Default Organization:**
- **Name:** Sorcha Local
- **Subdomain:** sorcha-local
- **ID:** `00000000-0000-0000-0000-000000000001`

**Default Administrator:**
- **Email:** `admin@sorcha.local`
- **Password:** `Dev_Pass_2025!`
- **User ID:** `00000000-0000-0000-0001-000000000001`
- **Roles:** Administrator

**⚠️ Security Warning:** Change default credentials immediately in production!

---

## Tenant Service API

The Tenant Service manages multi-tenant organizations, user identities, and service principals.

### Base Path: `/api/organizations`

### Endpoints

#### 1. List Organizations

```http
GET /api/organizations
Authorization: Bearer {token}
```

**Requires:** Administrator role

**Response:** `200 OK`
```json
{
  "organizations": [
    {
      "id": "00000000-0000-0000-0000-000000000001",
      "name": "Sorcha Local",
      "subdomain": "sorcha-local",
      "status": "Active",
      "createdAt": "2025-12-13T00:00:00Z",
      "branding": {
        "primaryColor": "#6366f1",
        "secondaryColor": "#8b5cf6",
        "companyTagline": "Decentralised Register Platform"
      }
    }
  ]
}
```

#### 2. Get Organization by ID

```http
GET /api/organizations/{id}
Authorization: Bearer {token}
```

**Response:** `200 OK` (same structure as above)

#### 3. Get Organization by Subdomain

```http
GET /api/organizations/by-subdomain/{subdomain}
```

**Note:** This endpoint allows anonymous access for subdomain validation.

**Response:** `200 OK`

#### 4. Create Organization

```http
POST /api/organizations
Authorization: Bearer {token}
Content-Type: application/json

{
  "name": "Acme Corporation",
  "subdomain": "acme",
  "branding": {
    "primaryColor": "#ff6600",
    "secondaryColor": "#333333",
    "companyTagline": "Innovation in Motion"
  }
}
```

**Response:** `201 Created`

#### 5. Add User to Organization

```http
POST /api/organizations/{organizationId}/users
Authorization: Bearer {token}
Content-Type: application/json

{
  "email": "john.doe@acme.com",
  "displayName": "John Doe",
  "password": "SecurePassword123!",
  "roles": ["User"]
}
```

**Response:** `201 Created`

#### 6. List Organization Users

```http
GET /api/organizations/{organizationId}/users
Authorization: Bearer {token}
```

**Response:** `200 OK`

---

## Platform Organisation Management API

Endpoints for platform-level organisation governance. Requires `SystemAdmin` role.

### List Organisations

```
GET /api/platform/organizations?page=1&pageSize=25&status=Active
Authorization: Bearer <system-admin-token>
```

**Response** (200):
```json
{
  "items": [
    {
      "id": "guid",
      "name": "Acme Corp",
      "subdomain": "acme-corp",
      "status": "Active",
      "orgType": "Private",
      "isPlatformOrg": false,
      "userCount": 12,
      "createdAt": "2026-03-16T10:00:00Z"
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 25
}
```

### Update Organisation Status

```
PUT /api/platform/organizations/{orgId}/status
Authorization: Bearer <system-admin-token>
Content-Type: application/json
```

**Request**:
```json
{ "status": "Suspended" }
```

**Response** (204): No content on success.

**Validation**: Status must be `Active` or `Suspended`. Platform organisations (System Admin, Public) cannot be suspended.

### Get Organisation Users

```
GET /api/platform/organizations/{orgId}/users?page=1&pageSize=25
Authorization: Bearer <system-admin-token>
```

**Response** (200):
```json
{
  "items": [
    {
      "id": "guid",
      "email": "user@example.com",
      "displayName": "Jane Doe",
      "roles": ["Administrator"],
      "status": "Active",
      "createdAt": "2026-03-16T10:00:00Z",
      "lastLoginAt": "2026-03-16T12:00:00Z"
    }
  ],
  "totalCount": 12,
  "page": 1,
  "pageSize": 25
}
```

---

## Organization Identity & Admin API

Feature 054 adds comprehensive organization identity management, OIDC single sign-on, user authentication, two-factor authentication, invitations, domain restrictions, custom domains, audit logging, and admin dashboard capabilities to the Tenant Service.

### IDP Configuration

Manages external identity provider configuration for OIDC-based single sign-on. Supports provider presets: MicrosoftEntra, Google, Okta, Apple, AmazonCognito, GenericOidc.

**Base Path:** `/api/organizations/{orgId}/idp`
**Authorization:** Administrator role required

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/organizations/{orgId}/idp` | Get IDP configuration including discovered endpoints and enabled status |
| PUT | `/api/organizations/{orgId}/idp` | Create or update IDP configuration (triggers OIDC discovery) |
| DELETE | `/api/organizations/{orgId}/idp` | Delete IDP configuration (disables SSO) |
| POST | `/api/organizations/{orgId}/idp/discover` | Discover IDP endpoints via .well-known/openid-configuration |
| POST | `/api/organizations/{orgId}/idp/test` | Test IDP connection with client_credentials grant |
| POST | `/api/organizations/{orgId}/idp/toggle` | Enable or disable IDP without removing configuration |

### OIDC Authentication

Handles the full OIDC authorization code + PKCE exchange, user provisioning, and Sorcha JWT issuance.

**Base Path:** `/api/auth`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/oidc/initiate` | Anonymous | Generate authorization URL for org's configured IDP |
| GET | `/api/auth/callback/{orgSubdomain}` | Anonymous | OIDC callback: exchange authorization code for Sorcha JWT |
| POST | `/api/auth/oidc/complete-profile` | Authenticated | Complete missing profile fields after OIDC provisioning |

### Email Verification

**Base Path:** `/api/auth`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/verify-email` | Anonymous | Verify email address with token |
| POST | `/api/auth/resend-verification` | Authenticated | Resend verification email (rate limited: 3/hour) |

### Authentication & Token Management

Local email/password authentication with progressive lockout, token lifecycle management, and self-registration.

**Base Path:** `/api/auth`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/login` | Anonymous | Login with email/password (returns JWT or 2FA challenge) |
| POST | `/api/auth/verify-2fa` | Anonymous | Verify TOTP code to complete login (rate limited). Optional `tier` field: `"consumer"` forces Consumer-tier token (wallet sign-in, spec 136). |
| POST | `/api/auth/register` | Anonymous | Self-register with email/password (public orgs only, NIST password policy) |
| POST | `/api/auth/token/refresh` | Anonymous | Exchange refresh token for new access token |
| POST | `/api/auth/token/revoke` | Authenticated | Revoke an access or refresh token |
| POST | `/api/auth/token/introspect` | Service | Introspect a token (service-to-service only) |
| POST | `/api/auth/token/revoke-user` | Administrator | Revoke all tokens for a specific user |
| POST | `/api/auth/token/revoke-organization` | Administrator | Revoke all tokens for all users in an organization |
| GET | `/api/auth/me` | Authenticated | Get current user information from token claims |
| POST | `/api/auth/logout` | Authenticated | Logout and revoke current access token |

### TOTP Two-Factor Authentication

TOTP-based 2FA with authenticator app support, QR code provisioning, and backup codes.

**Base Path:** `/api/totp`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/totp/setup` | Authenticated | Initiate TOTP setup (generates secret, QR URI, backup codes) |
| POST | `/api/totp/verify` | Authenticated | Verify initial TOTP code to complete enrollment |
| POST | `/api/totp/validate` | Anonymous | Validate TOTP code during login (requires loginToken, rate limited) |
| POST | `/api/totp/backup-validate` | Anonymous | Validate and consume backup code during login (rate limited) |
| DELETE | `/api/totp` | Authenticated | Disable TOTP 2FA for current user |
| GET | `/api/totp/status` | Authenticated | Get TOTP 2FA status for current user |

### Passkey Authentication (Feature 055)

FIDO2/WebAuthn passkey authentication for both organizational users (2FA) and public users (primary auth).

#### Org User Passkey 2FA

**Base Path:** `/api/passkey`
**Authorization:** Authenticated org user

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/passkey/register/options` | Authenticated | Get passkey registration options (Fido2NetLib creation options) |
| POST | `/api/passkey/register/verify` | Authenticated | Complete passkey registration with attestation response |
| GET | `/api/passkey/credentials` | Authenticated | List user's passkey credentials |
| DELETE | `/api/passkey/credentials/{id}` | Authenticated | Revoke a passkey credential |

#### Org User Passkey 2FA Login

**Base Path:** `/api/auth`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/verify-passkey/options` | Anonymous | Get passkey assertion options for 2FA verification (requires loginToken) |
| POST | `/api/auth/verify-passkey` | Anonymous | Verify passkey assertion to complete 2FA login |

#### Public User Passkey Signup

**Base Path:** `/api/auth/public/passkey`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/public/passkey/register/options` | Anonymous | Get registration options for new public user (display name + optional email) |
| POST | `/api/auth/public/passkey/register/verify` | Anonymous | Verify attestation, create PublicIdentity, issue tokens |

#### Public User Passkey Sign-in (Discoverable)

**Base Path:** `/api/auth/passkey`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/passkey/assertion/options` | Anonymous | Get assertion options (discoverable credentials, no email needed) |
| POST | `/api/auth/passkey/assertion/verify` | Anonymous | Verify assertion and issue tokens. Optional `tier` field: `"consumer"` forces Consumer-tier token (wallet sign-in, spec 136). |

#### Public User Social Login

**Base Path:** `/api/auth/social` (providers) and `/api/auth/public/social` (flows)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET  | `/api/auth/social/providers` | Anonymous | List configured social providers. Returns `{"providers":["google",…]}`. Drives conditional "Continue with…" buttons on the citizen wallet PWA sign-in screen. |
| POST | `/api/auth/public/social/initiate` | Anonymous | Initiate OAuth flow for provider (Google, Microsoft, GitHub, Apple). Optional `surface` field: `"wallet"` routes the post-OAuth callback into the citizen wallet PWA (mints Consumer-tier, redirects to `/wallet/#token=…&refresh=…&expires_in=…`, login-only — unknown identity → `?authError=no_account`). Null/`"app"` keeps the default web flow. |
| POST | `/api/auth/public/social/callback` | Anonymous | Handle OAuth callback (SPA flow), provision user under strict link policy, issue tokens. Returns 400 with refusal message when `email_verified=false` from provider or when target user is unverified. When the provider's verified email matches an existing verified account that isn't yet linked to this `(provider, subject)`, returns `{"outcome":"LinkRequired","linkPendingToken":"…"}` — no session is issued; the client must complete the step-up flow below (Feature 168). |
| GET  | `/auth/social/callback` | Anonymous | Razor-page browser redirect URI for OAuth providers. Single canonical path per environment. Provider is recovered from cached state, NOT a query parameter. See `docs/guides/SOCIAL-LOGIN-SETUP.md`. When `surface=wallet` was used, redirects to `/wallet/#token=…&refresh=…&expires_in=…` with a Consumer-tier token. On `LinkRequired`, redirects to `/app/#outcome=LinkRequired&linkPendingToken=…` instead. |

#### Step-Up Social Account Linking (Feature 168)

Pre-session challenge flow for linking an unconnected social identity to an existing verified account.
All three endpoints are **unauthenticated** — the `linkPendingToken` acts as the principal.
Rate-limited with `platform-auth`. See `contracts/social-link-stepup.md` and `contracts/link-confirm.md`.

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/social/link/challenge/initiate` | Anonymous | Begin a step-up challenge against the target account identified by the link-pending token. Body: `{"linkPendingToken":"…","preferredMethod":null}`. Returns `{"method":"totp","payload":null}` (or the appropriate challenge payload). 401 if token invalid/expired; 400 if no enrolled method meets the Strong floor. |
| POST | `/api/auth/social/link/challenge/verify` | Anonymous | Submit the challenge proof. Body: `{"linkPendingToken":"…","method":"totp","proof":{…}}`. Returns `{"token":"ch_…","expiresInSeconds":300}` on success. 403 `proof_tier_insufficient` if method tier < Strong; 401 for other failures. |
| POST | `/api/auth/social/link/confirm` | Anonymous | Redeem the link-pending token and the `X-Auth-Challenge` (header) challenge proof to link the social identity and issue a session. Body: `{"linkPendingToken":"…"}`. Header: `X-Auth-Challenge: ch_…`. Returns `{"accessToken":"…","refreshToken":"…","expiresIn":…}` on success. 401 missing/invalid/expired credentials; 403 account mismatch or wrong operation; 409 social identity already linked to a different account. |

**Floor rule (FR-010):** `ScopedOperation.LinkSocial` requires `AuthAssuranceTier.Strong`. TOTP, passkey, and re-auth social proofs satisfy the floor; bare password (Basic) is always rejected with 403. Password-only accounts without TOTP or a passkey cannot initiate this flow (NoMethodAvailable, 400).

#### Public User Auth Method Management

**Base Path:** `/api/auth/public`
**Authorization:** Authenticated public user (JWT with sub claim)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/auth/public/methods` | Authenticated | List passkeys and social links for current user |
| POST | `/api/auth/public/social/link` | Authenticated | Link a new social account |
| DELETE | `/api/auth/public/social/{linkId}` | Authenticated | Unlink social account (last-method guard prevents removing only auth method) |
| POST | `/api/auth/public/passkey/add/options` | Authenticated | Get options for adding passkey to existing account |
| POST | `/api/auth/public/passkey/add/verify` | Authenticated | Complete adding passkey to existing account |

### Invitations

Organization invitation management for onboarding users with specific roles.

**Base Path:** `/api/organizations/{organizationId}/invitations`
**Authorization:** Administrator role required

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/organizations/{organizationId}/invitations` | Send invitation with role (generates 32-byte token, 1-30 day expiry) |
| GET | `/api/organizations/{organizationId}/invitations` | List invitations (optional status filter: Pending, Accepted, Expired, Revoked) |
| POST | `/api/organizations/{organizationId}/invitations/{invitationId}/revoke` | Revoke a pending invitation |

### Domain Restrictions

Controls which email domains are allowed for OIDC auto-provisioning and self-registration.

**Base Path:** `/api/organizations/{organizationId}/domain-restrictions`
**Authorization:** Administrator role required

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/organizations/{organizationId}/domain-restrictions` | Get allowed email domains and restriction status |
| PUT | `/api/organizations/{organizationId}/domain-restrictions` | Update allowed domains (empty array disables restrictions) |

### Organization Settings

Manage organization type, self-registration, and audit retention configuration.

**Base Path:** `/api/organizations/{orgId}/settings`
**Authorization:** Administrator role required

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/organizations/{orgId}/settings` | Get org type, self-registration status, allowed domains, audit retention |
| PUT | `/api/organizations/{orgId}/settings` | Update self-registration and audit retention (1-120 months) |

### Custom Domain

Configure custom domains with CNAME verification for organization URL resolution.

**Base Path:** `/api/organizations/{organizationId}/custom-domain`
**Authorization:** Administrator role required

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/organizations/{organizationId}/custom-domain` | Get custom domain configuration and verification status |
| PUT | `/api/organizations/{organizationId}/custom-domain` | Configure custom domain (returns CNAME instructions) |
| DELETE | `/api/organizations/{organizationId}/custom-domain` | Remove custom domain configuration |
| POST | `/api/organizations/{organizationId}/custom-domain/verify` | Verify custom domain DNS/CNAME resolution |

### Audit Log

Query audit events and manage retention policy for the organization.

**Base Path:** `/api/organizations/{organizationId}/audit`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/organizations/{organizationId}/audit` | Auditor | Query audit events (filter by date, event type, user; max 200/page) |
| GET | `/api/organizations/{organizationId}/audit/retention` | Administrator | Get audit retention period (months) |
| PUT | `/api/organizations/{organizationId}/audit/retention` | Administrator | Update audit retention period (1-120 months) |

### Admin Dashboard

**Base Path:** `/api/organizations/{organizationId}/dashboard`
**Authorization:** Administrator role required

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/organizations/{organizationId}/dashboard` | Get aggregated stats: user counts, role distribution, recent logins, pending invitations, IDP status |

---

## Organisation Switching

### List My Organisations

```
GET /api/auth/me/organizations
Authorization: Bearer <token>
```

**Response** (200):
```json
{
  "items": [
    {
      "organizationId": "guid",
      "organizationName": "Acme Corp",
      "subdomain": "acme-corp",
      "role": "Administrator",
      "isCurrent": true
    }
  ]
}
```

### Switch Organisation

```
POST /api/auth/switch-org
Authorization: Bearer <token>
Content-Type: application/json
```

**Request**:
```json
{ "organizationId": "target-org-guid" }
```

**Response** (200): Returns new `TokenResponse` scoped to the target organisation.

---

## Platform Settings

### Get Platform Settings

```
GET /api/platform/settings
Authorization: Bearer <system-admin-token>
```

### Update Public Org Status

```
PUT /api/platform/settings/public-org
Authorization: Bearer <system-admin-token>
Content-Type: application/json
```

**Request**:
```json
{ "enabled": true }
```

---

## Peer Service API

The Peer Service manages P2P networking (over gRPC), system-register replication, and peer discovery. Nodes rendezvous via seed/anchor nodes (there is no central hub node — the earlier `n0/n1/n2` hub topology was retired in Feature 143).

**Auth:** the gateway applies `RequireAuthenticated` to `/api/peers/**` and to the peer-owned `/api/registers/*` replication routes; specific write/management operations require more (noted per route). The read/monitoring routes have no per-endpoint policy beyond the gateway's authenticated gate.

### Peer management

**Base path:** `/api/peers`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/peers` | Authenticated | List all known peers in the network |
| GET | `/api/peers/{peerId}` | Authenticated | Detailed information about a specific peer |
| GET | `/api/peers/stats` | Authenticated | Aggregated peer-network statistics |
| GET | `/api/peers/health` | Authenticated | Peer-network health status |
| GET | `/api/peers/quality` | Authenticated | Connection-quality metrics for all tracked peers |
| GET | `/api/peers/connected` | Anonymous (count only; full list if authenticated) | Count of connected peers |
| POST | `/api/peers/{peerId}/ban` | `CanManagePeers` (org member or service token) | Ban a peer from communication |
| DELETE | `/api/peers/{peerId}/ban` | `CanManagePeers` | Unban a peer, restoring communication |
| POST | `/api/peers/{peerId}/reset` | `CanManagePeers` | Reset a peer's failure count |

**Example — `GET /api/peers` →** `200 OK`
```json
[
  {
    "peerId": "peer-123",
    "address": "192.168.1.100",
    "port": 8080,
    "supportedProtocols": ["gRPC", "HTTP"],
    "firstSeen": "2026-07-14T10:00:00Z",
    "lastSeen": "2026-07-14T17:00:00Z",
    "failureCount": 0,
    "isSeedNode": true,
    "averageLatencyMs": 15.5
  }
]
```

### Register replication

Peers advertise the registers they can serve and subscribe to registers they want replicated. These routes share the `/api/registers` prefix with the Register Service; the peer-owned replication routes below are distinguished by path.

**Base path:** `/api/registers`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/api/registers/available` | Authenticated | List registers advertised across the peer network |
| GET | `/api/registers/subscriptions` | Authenticated | List all register subscriptions on this node |
| GET | `/api/registers/cache` | Authenticated | Register cache statistics |
| POST | `/api/registers/{registerId}/advertise` | Authenticated (rate-limited) | Advertise (or remove advertisement for) a register |
| POST | `/api/registers/bulk-advertise` | Authenticated | Bulk advertise / sync register advertisements |
| POST | `/api/registers/{registerId}/subscribe` | Service token (`RequireService`, rate-limited) | Subscribe to a register for replication |
| DELETE | `/api/registers/{registerId}/subscribe` | Service token (`RequireService`) | Unsubscribe from a register |
| DELETE | `/api/registers/{registerId}/cache` | Authenticated | Purge cached data for a register |

### Node info & health

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/` | Anonymous | Basic service information and ports |
| GET | `/api/health` | Anonymous | Service health check with metrics (also reachable via the gateway as `/peer/health`) |

### gRPC surface

The P2P transport is gRPC (container port `5000`, host-published `50051`). Reflection is enabled in Development only. Services:

| Proto service | Purpose |
|---|---|
| `PeerDiscovery` | Peer discovery and exchange |
| `PeerHeartbeat` | Liveness heartbeat |
| `RegisterSync` | Register replication sync (docket/transaction streaming) |
| `TransactionDistribution` | Transaction gossip / fan-out |
| `DocketSyncService` | Docket-finalisation sync |
| `PeerCommunication` | Relay / reverse-stream comms for NAT'd peers |

> There is also an internal `POST /api/internal/peer/distribute/{registerId}` (service-only, `CanWriteDockets`) that fans a signed submission out to a register's source peers (Feature 108). It is excluded from the OpenAPI/Scalar surface by design.

---

## Consumer Persona API (Feature 092)

Per-user identity attributes stored as ciphertext in Tenant Service, with the
content key derived by Wallet Service under `sorcha:persona-vault`. Used by
`SorchaFormRenderer` to autofill recognised form fields with a visible `self`
provenance tick.

### Tenant Service endpoints — `/me/persona`

All routes require a platform-user JWT and are rate-limited under
`RateLimitPolicies.Api`.

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/me/persona` | Read the signed-in user's persona. Returns an empty `PersonaReadModelV1` for new users (never 404). Optional `actingAs=self` query parameter reserved for future delegation. |
| PUT | `/me/persona` | Replace the persona with a full `PersonaAttributesV1` payload. Validates the 5-entry cap, exactly-one-default invariant, RFC 5322 email, E.164 phone, and ISO 3166-1 alpha-2 country codes. Returns the canonical read model. |
| DELETE | `/me/persona` | Hard-delete the persona row. Idempotent. |

PATCH is intentionally deferred in v1 — full-replace PUT covers every
consumer use case.

### Wallet Service endpoints — internal S2S only

These endpoints are **not** routed through the API Gateway. Tenant Service
calls them with a service-to-service JWT carrying the `persona:crypto`
scope; the `RequirePersonaCrypto` authorization policy enforces the scope.
Rate-limited under `RateLimitPolicies.Strict`.

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/wallets/{address}/persona/encrypt` | Derive the persona content key, encrypt the plaintext with XChaCha20-Poly1305, return the ciphertext, nonce, and opaque `wrappedKeyRef`. |
| POST | `/api/v1/wallets/{address}/persona/decrypt` | Derive the persona content key and decrypt the supplied ciphertext. Returns 400 on `wrappedKeyRef ≠ walletAddress` (v1 invariant) via a typed `PersonaKeyRefMismatchException`. |

### Key Models

- **PersonaAttributesV1** — Plaintext write-side shape: `GivenName`, `FamilyName`, `FullName`, `DateOfBirth`, `Emails[]`, `Phones[]`, `Addresses[]`, `Nationalities[]`. Each multi-value list capped at 5 with exactly one default.
- **PersonaReadModelV1** — Wire shape returned by GET. Scalar attributes wrapped in `PersonaAttribute<T>` carrying `Source` (`SelfAsserted` / `VerifiedCredential`), `VerifiedBy` (issuer DID, null in v1), and `LastUpdated` (`DateTimeOffset`).
- **PlatformUserPersona** — EF entity: one row per `PlatformUser`, hard-delete cascade, XChaCha20-Poly1305 ciphertext with 24-byte nonce, `WrappedKeyRef == walletAddress` in v1.

### Schema Extension — `x-persona`

Form authors pin fields to specific persona attributes via a JSON-Schema extension:

```json
{
  "applicantEmail": { "type": "string", "format": "email", "x-persona": "defaultEmail" },
  "nextOfKinEmail": { "type": "string", "format": "email", "x-persona": false }
}
```

`x-persona: false` blocks persona autofill on a field. Without an explicit
tag, the conservative inference allowlist applies (`format: email` → default
email, `format: tel` → default phone, field name `dateOfBirth`/`dob`/`birthDate`
→ date of birth, postal-address object shape → default address).

### Cryptography

- **Derivation purpose**: `sorcha:persona-vault` (registered under
  `SorchaDerivationPaths.PersonaVault`; BIP44 index 104 reserved for a
  future HD refactor)
- **AEAD**: XChaCha20-Poly1305 via `ISymmetricCrypto`, 24-byte nonce
- **HKDF**: HKDF-SHA256 derives per-request content key from the wallet
  private key
- **Ciphertext location**: Tenant database only. Content key never leaves
  Wallet Service.

---

## Address Lookup API (Feature 103)

Postcode-driven address autofill for citizen-facing forms. Hosted by the
Tenant Service and routed through the API Gateway. Uses pluggable providers:
**Postcodes.io** (UK, validate-only, default) and **OS Places** (UK, full
address candidates, opt-in via API key). When no provider is reachable the
`PostcodeLookupRenderer` gracefully degrades to a plain text input.

### Endpoints (via API Gateway `/api/*`)

| Method | Path | Purpose |
|--------|------|---------|
| GET    | `/api/address-lookup/providers` | List configured providers, capabilities, and live availability |
| POST   | `/api/address-lookup/postcode`  | Resolve a postcode (validate-only metadata or full-address candidate list) |

### Request — POST `/api/address-lookup/postcode`

```json
{ "postcode": "D02 Y1K8" }
```

### Response — full-address provider

```json
{
  "postcode": "D02 Y1K8",
  "isValid": true,
  "provider": "OSPlaces",
  "capability": "FullAddress",
  "candidates": [
    { "line1": "42 Grafton Street", "town": "Dublin", "region": "Leinster",
      "postcode": "D02 Y1K8", "country": "Ireland",
      "displayLabel": "42 Grafton Street, Dublin, D02 Y1K8" }
  ]
}
```

### Response — validate-only provider

```json
{
  "postcode": "EC1A 1BB",
  "isValid": true,
  "provider": "PostcodesIo",
  "capability": "ValidateOnly",
  "metadata": { "town": "London", "region": "Greater London", "country": "England",
                "latitude": 51.5188, "longitude": -0.0991 }
}
```

Both endpoints require Bearer authentication and apply the standard API
rate-limit policy. Schemas declare `x-address-lookup: true` on a string
property to opt the field into the renderer dispatch — the
`PostcodeLookupRenderer` then writes back to sibling fields (`line1`,
`line2`, `town`, `region`, `postcode`, `country`) on the parent scope when
a candidate is selected.

---

## Blueprint Service API

### Base Path: `/api/blueprints`

### `GET /api/instances/{instanceId}/actions/{actionId}` — consumer-readable action schema (P0 fix)

Instance-scoped, consumer-readable read of a single action's form-relevant schema. Added to close a P0
bug: the Wallet PWA previously read the action schema via the authoring endpoint
`GET /api/blueprints/{id}` (Feature 147), which is deliberately restricted to service/platform-tier
callers — every consumer-tier citizen 403'd, and the PWA (wrongly) reported this as "offline". This
endpoint sits on the existing `/api/instances` group (`CanExecuteBlueprints` — any authenticated
caller) and adds its own **participant gate**: at least one of the caller's resolved wallets must
appear in the instance's `ParticipantWallets`, or the request is refused with `403`. The caller's
wallet(s) are resolved via `ParticipantWalletResolver` — the `wallet_address` JWT claim when present
(platform-tier fast path), else a Wallet-Service lookup by owner (`platform_user_id`, falling back to
`sub`) — the same resolution `GET /api/actions/pending` and the Feature 176 disclosures endpoint use.
This was itself a P0-review fix: the original gate read only `wallet_address`, which consumer-tier
tokens (every real PWA sign-in, Feature 136) never carry, so every genuine citizen 403'd.

| Method | Path | Auth |
|--------|------|------|
| GET    | `/api/instances/{instanceId}/actions/{actionId}` | Authenticated + at least one of the caller's resolved wallets is a participant on the instance |

**Response — 200 OK** (`InstanceActionSchemaResponse`):

```json
{
  "actionId": 1,
  "title": "Claim your Assured Identity credential",
  "form": { "type": "Layout", "elements": [ ] },
  "dataSchemas": [ { "type": "object", "properties": { "email": { "type": "string" } } } ],
  "calculations": null,
  "credentialRequirements": null,
  "credentialIssuanceConfig": { "credentialType": "AssuredIdentityCredential", "vct": "https://sorcha.dev/vc/assured-identity/v1", "displayName": "Assured Identity", "claimMappings": [ ], "recipientParticipantId": "citizen" }
}
```

Deliberately **narrow** — this is not the full `Action` model and not a blueprint wrapper. It carries
only what `SorchaFormRenderer` reads to render and validate the form for this one action. It does
**not** return routing rules (`routes`, `condition`), other participants (`participants`, `target`,
`additionalRecipients`), state-reconstruction internals (`requiredPriorActions`), rejection routing
(`rejectionConfig`), or disclosure/sender fields (only meaningful when the caller is not the action's
sender, which this endpoint's caller always is). `404` if the instance, blueprint, or action is not
found; `403` if the caller is not a recorded participant on the instance.

> **That known gap is now CLOSED (issue #1182).** `GET /api/instances/{id}` and its two siblings had
> no participant check at all — any authenticated caller could read any instance by id. All three are
> now gated; see below.

### Instance read endpoints — participant gate (issue #1182)

`GET /api/instances/{instanceId}`, `GET /api/instances/{instanceId}/state` and
`GET /api/instances/{instanceId}/next-actions` previously returned instance content to **any**
authenticated caller. The `/api/instances` group carries only `CanExecuteBlueprints`, which resolves
to a bare `RequireAuthenticatedUser()` — so knowledge of a GUID was the only thing protecting another
citizen's in-flight application. `GET /{instanceId}` was the most serious: it returned the `Instance`
verbatim, including `accumulatedData` (on an identity workflow: name, date of birth, address and
portrait image tokens in plaintext) and `participantWallets` (which de-anonymises every participant).

| Method | Path | Authorization |
|--------|------|---------------|
| GET | `/api/instances/` | Authenticated; returns only instances where a wallet the caller controls is a participant |
| GET | `/api/instances/{instanceId}` | Authenticated **and** (caller controls a participant wallet **or** the instance is an unstarted open-participant shell) |
| GET | `/api/instances/{instanceId}/state` | As above, plus the `X-Delegation-Token` header |
| GET | `/api/instances/{instanceId}/next-actions` | As above |

Two behaviours are deliberate and easy to get wrong in the opposite direction:

- **The caller's wallets are resolved, not read from a claim.** A consumer-tier token — every real
  citizen sign-in, web and PWA — carries no `wallet_address` (Feature 136). Resolution goes through
  `ParticipantWalletResolver` (claim fast path, then Wallet-Service lookup by owner) and matches
  against the caller's full wallet set. A claim-only gate would 403 every genuine citizen while
  leaving platform-tier callers unrestricted.
- **An unstarted instance awaiting its open (Feature 103) participant is readable by any
  authenticated caller.** `CreateInstance` seeds `participantWallets` only from participants that
  already carry a wallet, so the walk-in applicant is absent until their first submission seals — and
  the PWA reads `GET /api/instances/{id}` to find the current action *before* they submit anything.
  The carve-out requires `completedActionCount == 0` **and** a current action whose sender is unbound,
  so it holds no accumulated data and only wallets the published blueprint already carries in the
  clear on the register. It closes the moment any action completes.

`403` returns the same body for "not a participant" as for "blueprint/action not found", so a
non-participant cannot probe instance internals by reading the difference (#1183).

**Also fixed (same root cause, opposite symptom):** `GET /api/instances/` read the `wallet_address`
claim directly and returned an empty page when it was absent — so every consumer-tier citizen saw
none of their own applications. It now resolves through the same seam, spans every wallet the caller
controls, deduplicates by instance id and orders newest-first.

### My Applications — the citizen read surface (Feature 186 / #1163)

*What did I submit, and what happened to it?* Under `/api/me/*`, the platform's personal-scope
convention. A **sibling** of `/api/instances`, not a reshaping of it: the Citizen Wallet PWA binds
`GET /api/instances/{id}`, so that group keeps its raw-model shape.

| Method | Path | Authorization |
|--------|------|---------------|
| GET | `/api/me/applications` | Authenticated; returns only applications the caller participates in |
| GET | `/api/me/applications/{instanceId}` | Authenticated **and** the caller controls a participant wallet |

Authorization is deliberately **plain**, not `RequireConsumerAudience`: the same citizen holds a
consumer-tier token on `/wallet` and a platform-tier token on `/app`, and must see the same
applications from either. There is no "any-human" tier (pattern #13), so a genuinely cross-tier
endpoint stays unclassified rather than being force-fitted to one.

The detail endpoint answers **`404` identically** for "not yours" and "does not exist", so the
instance-id space cannot be probed — those ids appear in URLs, logs and inbox `detailHref` values.

#### Row shape

```json
{
  "instanceId": "3f2a…",
  "blueprintTitle": "Assured Identity",
  "instanceReference": "AI-CYB-14-A7K3",
  "state": "Completed",
  "outcome": "NotApproved",
  "decisionTitle": "AIAS could not assure your identity",
  "decisionReason": "The document you provided could not be read clearly.",
  "decisionSeverity": "Warning",
  "stepNumber": null, "totalSteps": 4,
  "needsYou": false,
  "createdAt": "…", "updatedAt": "…", "completedAt": "…"
}
```

Optional fields are **omitted when absent**, never emitted as `""`. `decisionReason` especially: an
empty resolution means the route declares no reason, and a blank line reads to a citizen as a fault.

#### `outcome` is not `state`, and the difference is the point

`state` is the instance lifecycle value by **name** (never the underlying integer — this service
configures no `JsonStringEnumConverter`). `outcome` is what the citizen is shown.

They differ because, under Feature 184, a refusal is expressed as **taking a route that declares an
`x-decision-notice`** — not as a distinct instance state. When such a route ends the branch, the fold
sees an empty next-action set and assigns `Completed`, so a refused application and an approved one
are indistinguishable by state alone. `Instance.DecisionRouteId` and `Instance.DecisionReasonCode`
— projected by `InstanceProjection.ApplyInPlace` from the transaction's signed clear metadata, and so
identical on every node and reproduced by a rebuild — are what let the read path find the taken route
in the replicated blueprint, read its notice severity, and report `NotApproved`.

The wording itself is resolved **on read**, through the same `DecisionNotice.ResolveMessage` the
inbox dispatcher uses, so the page and the notification cannot disagree. It is deliberately *not*
folded: the blueprint is node-local state, and folding it would break the requirement that the
projection be identical on every node. The internal reason **code** is never returned to a citizen.

`needsYou` fails closed — terminal application, unresolvable blueprint, or absent participant binding
all yield `false` — so the surface cannot offer an action that turns out not to be takeable (#1268).

### Timebound Presentation Lifecycle (Feature 111)

Three-event on-register lifecycle for timebound credential presentations. When an action carries `credentialRequirements` targeting HaipExternalWallet **or SorchaWallet** (#1195 Phase 2, Task 6b) and the citizen submits without pre-attached presentations, `/execute` returns **`202 Accepted`** with `AwaitingPresentation=true` and a QR code / authorization request URI. A `PresentationInitiated` transaction is written to the register immediately. The action does NOT complete until the verifier callback writes a `PresentationOutcome` with `kind=success`. Retry after a decline is first-class; a second attempt after a successful outcome returns **`409 Conflict`**.

#### `POST /api/instances/{instanceId}/actions/{actionId}/execute` — 202 semantics

**Feature 145 — single async submission.** A normal action submission always returns **`202 Accepted`** with `isAsync: true` and an **empty** `nextActions` (and `isComplete: false`). The submitter no longer advances the instance: state machines forward when the sealed docket is folded by the `InstanceProjector` on every node. Callers observe advancement via the instance read (`GET /api/instances/{id}`) or the `instance-advanced` / `action-available` hub events — **not** from the submit response. The endpoint may bound-wait briefly for the local seal (chain ordering) before responding; on timeout it still returns `202`.

```json
{
  "transactionId": "abcd1234...",
  "instanceId": "78654d78-...",
  "isAsync": true,
  "nextActions": [],
  "isComplete": false,
  "issuedCredentialId": "urn:uuid:...",
  "calculations": { }
}
```

`nextActions` and inline `issuedCredential` advancement are no longer carried on the submit response — resolve outcomes by observing the instance and credential events. Contract: `specs/145-ledger-derived-instances/contracts/submission-response.md`.

When the action requires a HAIP presentation and the submitter has not attached presentations:

**Response:** `202 Accepted` with `Location: /api/presentations/{id}/status`
```json
{
  "transactionId": "abcd1234...",
  "instanceId": "78654d78-...",
  "isComplete": false,
  "awaitingPresentation": true,
  "presentationRequest": {
    "requestId": "9f7c2e1a-0b3d-...",
    "presentationRequestUri": "openid4vp://...",
    "credentialType": "VerifiedIdentityCredential",
    "requestedClaims": ["givenName", "familyName", "dateOfBirth"],
    "expiresAt": "2026-04-24T21:25:00Z"
  }
}
```

Rate-limited submitters receive `429 Too Many Requests` with `Retry-After`. Action already completed via prior success returns `409 Conflict`.

#### `GET /api/presentations/{presentationRequestId}/status`

Wallet-facing, intentionally unauthenticated for OpenID4VP wallet interoperability. Returns lifecycle state + expiry only; no register, instance, or consumer metadata leaks through a compromised `presentationRequestId`.

**Response:** `200 OK`
```json
{
  "presentationRequestId": "9f7c2e1a-...",
  "state": "awaiting-presentation",
  "expiresAt": "2026-04-24T21:25:00Z"
}
```

State values: `awaiting-presentation` · `success` · `decline` · `abandoned` · `abandoned-with-late-outcome` · `expired`.

The register's transaction stream is the authoritative history — query `PresentationInitiated` / `PresentationOutcome` / `PresentationAbandoned` transactions from the Register Service for the full event record.

#### `POST /api/presentations/callbacks/sorcha-wallet/{presentationRequestId}`

**#1195 Phase 2 (Task 6b)** — the Citizen Wallet PWA's direct_post target for a `SorchaWallet`-gated presentation (this URI is the `response_uri` in the served request object). **Consumer-tier** (`RequireConsumerAudience`) — the citizen's own token authorises the post; the literal route segment outranks the generic `{consumerName}` template, so every sorcha-wallet post lands here. Body: `{ "vpToken": "<compact SD-JWT presentation with KB-JWT>" }`. Verification happens server-side in the sorcha-wallet consumer against the verifier session persisted on pending state at initiation (nonce, vct, required claims, client_id — single-use, TTL-bound). Refusals are named: `session-missing` (unknown/pre-wiring attempt), `session-expired` (post-window), and validator errors (nonce mismatch, vct mismatch, missing claims) decline with distinct reasons. Response shape matches the generic callback below.

#### `POST /api/presentations/callbacks/{consumerName}/{presentationRequestId}`

Service-to-service only (`AuthorizationPolicies.RequireService`). Called by a registered `IPresentationConsumer` (e.g. the HAIP Service `PresentationCallbackRelay`) after verification. Writes the `PresentationOutcome` transaction. Idempotent by `presentationRequestId`. Body is an opaque JSON payload the consumer interprets (for HAIP: a serialised `VerificationResult`).

**Response:** `200 OK`
```json
{
  "kind": "Success",
  "outcomeTransactionId": "def5678...",
  "idempotentReplay": false,
  "lateAfterAbandonment": false
}
```

#### `GET /api/presentations/{presentationRequestId}/disclosed-claims?token={ClaimsFetchToken}`

**Feature 127** — council-page-facing endpoint that returns disclosed claims in plaintext for autofill after a successful Sorcha-wallet presentation. Token-authenticated, single-use, short-TTL. The token is minted by `PresentationLifecycleService.InitiateAsync` ONLY for consumers that produce council-page-readable claims (currently `"sorcha-wallet"`) and returned alongside the `presentationRequestId`. F111's existing `presentation-outcome` register transaction remains the authoritative record; this endpoint is the operational signal for the council page to read the claims without re-decrypting the register tx.

**Auth model:** the council page is unauthenticated at the broader sense — the token IS the auth scope. The token is single-use (atomic `GETDEL` via Lua against `IClaimsFetchTokenStore`), bound to a single `presentationRequestId`, expires when the presentation validity window expires.

**Response (success, claims ready):** `200 OK`
```json
{
  "presentationRequestId": "8f6b94de-...",
  "status": "success",
  "claims": {
    "givenName": "Sarah",
    "familyName": "Example",
    "dateOfBirth": "1968-04-12",
    "homeAddress": "12 Brae Road, Strathcarron, IV54 8YQ"
  },
  "subjectDisplayName": "Sarah Example"
}
```

**Response (outcome pending):** `200 OK` with `{ "presentationRequestId": "...", "status": "pending" }`. The token is consumed; the council page reuses the same token on retry only if it polled before the hub `PresentationOutcomeReady` signal arrived.

**Error responses:**
- `400 token-missing` — `?token=` not provided.
- `401 token-invalid` — Token unknown, expired, or already used.
- `401 token-mismatch` — Token doesn't bind to the path's `presentationRequestId`.
- `404 presentation-not-found` — No `presentation-initiated` record (expired or never minted).
- `410 outcome-decline` — Outcome was a decline; no claims to disclose.
- `410 outcome-abandoned` — Attempt was abandoned.
- `410 claims-expired` — Outcome was success but the disclosed-claims stash TTL elapsed.

Full contract: `specs/127-credential-gated-service/contracts/disclosed-claims-endpoint.md`.

### Endpoints

#### 1. Get All Blueprints

```http
GET /api/blueprints
```

**Query Parameters:**
- `page` (integer): Page number (default: 1)
- `pageSize` (integer): Items per page (default: 20, max: 100)
- `search` (string): Search in title/description
- `status` (string): Filter by status

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "bp-123",
      "title": "Purchase Order Workflow",
      "description": "Multi-party purchase order process",
      "createdAt": "2025-11-17T10:30:00Z",
      "updatedAt": "2025-11-17T10:30:00Z",
      "participantCount": 2,
      "actionCount": 3
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42,
  "totalPages": 3
}
```

#### 2. Get Blueprint by ID

```http
GET /api/blueprints/{id}
```

**Headers:**
- `Accept: application/ld+json` (optional, for JSON-LD format)

**Response:** `200 OK`
```json
{
  "id": "bp-123",
  "title": "Purchase Order Workflow",
  "description": "Multi-party purchase order process",
  "version": "1.0.0",
  "participants": [
    {
      "id": "buyer",
      "name": "Buyer Organization",
      "organisation": "ORG-001"
    },
    {
      "id": "seller",
      "name": "Seller Organization",
      "organisation": "ORG-002"
    }
  ],
  "actions": [
    {
      "id": "0",
      "title": "Submit Purchase Order",
      "description": "Buyer submits PO",
      "sender": "buyer",
      "data": {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
          "itemName": { "type": "string" },
          "quantity": { "type": "integer" },
          "unitPrice": { "type": "number" }
        },
        "required": ["itemName", "quantity", "unitPrice"]
      }
    }
  ]
}
```

#### 3. Create Blueprint

```http
POST /api/blueprints
```

**Request Body:**
```json
{
  "title": "Invoice Approval Workflow",
  "description": "Multi-step invoice approval",
  "version": "1.0.0",
  "participants": [
    {
      "id": "submitter",
      "name": "Invoice Submitter"
    },
    {
      "id": "approver",
      "name": "Finance Approver"
    }
  ],
  "actions": [
    {
      "id": "0",
      "title": "Submit Invoice",
      "sender": "submitter"
    }
  ]
}
```

**Response:** `201 Created`
```json
{
  "id": "bp-456",
  "title": "Invoice Approval Workflow",
  ...
}
```

#### 4. Publish Blueprint

```http
POST /api/blueprints/{id}/publish
```

**Response:** `200 OK`
```json
{
  "isSuccess": true,
  "publishedBlueprint": {
    "blueprintId": "bp-123",
    "version": 1,
    "publishedAt": "2025-11-17T11:00:00Z"
  },
  "errors": []
}
```

### File Chunks (Feature 085)

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/file-chunks` | Submit encrypted file chunk (staged submission) |

**Query Parameters (POST /api/file-chunks):** None — all parameters are in the request body (uploadId, chunkIndex, totalChunks, encryptedData, checksum).

**Auth:** JWT Bearer required. Rate limiting: `RateLimitPolicies.Strict`.

---

### Feature 142 — Blueprint Design Lifecycle

The staged designer workspace (Describe → Understand → Rehearse → Go live) is backed by a small set of new endpoints plus a CHANGED publish contract. The full OpenAPI shape lives at `specs/142-blueprint-lifecycle/contracts/blueprint-lifecycle.openapi.yaml`; the table below is the operator-facing summary.

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/blueprints/{id}/rehearsals` | Start a full rehearsal — provisions/reuses the per-org devMode sandbox register and mints ephemeral per-role wallets. |
| GET | `/api/blueprints/{id}/rehearsals/{rehearsalId}` | Read rehearsal status + walk-through log. |
| DELETE | `/api/blueprints/{id}/rehearsals/{rehearsalId}` | Reset / discard a rehearsal (sandbox register persists; ephemeral wallets are abandoned). |
| POST | `/api/blueprints/{id}/rehearsals/{rehearsalId}/role` | Switch the currently acting participant role. |
| POST | `/api/blueprints/{id}/rehearsals/{rehearsalId}/steps` | Submit the current step's payload as the acting role. Terminal success records a `RehearsalPass` for the exec-def hash. |
| POST | `/api/blueprints/{id}/publish` | CHANGED — now gated by governance (HARD) + rehearsal (SOFT). |
| POST | `/api/blueprints/from-published` | NEW — clone a published version back to a fresh draft for amend. |

**Read register response — new `advertise` + `sandbox` fields.**
`GET /api/registers/{id}` now carries the `advertise` flag and a computed `sandbox` flag (true when the register's metadata has `sandbox=true`). The Go-live picker excludes sandbox registers.

**`POST /api/blueprints/{id}/publish` (changed).** Request body now accepts an optional `override` block:

```json
{
  "registerId": "reg-abc",
  "override": { "confirm": true, "reason": "hot-fix policy update — rehearsal scheduled for end of week" }
}
```

Responses:

| Status | Body | When |
|--------|------|------|
| `200 OK` | `{ blueprintId, version, registerId, publishedAt, overridden, warnings? }` | Cleared — either via a matching `RehearsalPass` or via an authorised override (audited). |
| `403 Forbidden` | `{ error: "…" }` | Caller does not hold a publish-governance role (Owner / Admin / Designer) on the target register's roster. No record written. |
| `409 Conflict` | `{ code: "REHEARSAL_REQUIRED", execDefHash, message }` | No matching `RehearsalPass` for this exec-def hash and no override confirmed. Resend with `override.confirm=true` to proceed. |

Override paths write a `PublishOverride` audit row (`OverriddenByPlatformUserId`, `OverriddenAt`, `RegisterId`, `ExecDefHash`, optional `Reason`). Observability: counters `rehearsal_run_total`, `publish_override_total`, `sandbox_provision_total` and histogram `rehearsal_duration_seconds` on the `Sorcha.Blueprint.Designer` meter.

**`POST /api/blueprints/from-published` (new).** Request body `{ "publishedBlueprintId": "…" }` or `{ "sourceBlueprintId": "…", "version": 3 }`. Returns the new draft `Blueprint` with lineage keys (`x-source-register`, `x-source-blueprint-id`, `x-source-version`) on its `Metadata` so the designer can rehydrate the amend context.

**Auth (all of the above):** JWT Bearer required. Rehearsal endpoints require `CanManageBlueprints`; publish requires `CanPublishBlueprints` AND the governance gate; `from-published` requires `CanManageBlueprints`.

---

## Wallet Service API

### Base Path: `/api/wallets`

### Endpoints

#### 1. Create Wallet

```http
POST /api/wallets
```

**Request Body:**
```json
{
  "title": "My Secure Wallet",
  "description": "Personal wallet for transactions",
  "keyType": "ED25519"
}
```

**Key Types:**
- `ED25519`: EdDSA using Curve25519 (recommended)
- `NISTP256`: ECDSA using NIST P-256
- `RSA`: RSA-4096

**Response:** `201 Created`
```json
{
  "id": "wallet-789",
  "walletAddress": "0x1234567890abcdef",
  "title": "My Secure Wallet",
  "keyType": "ED25519",
  "createdAt": "2025-11-17T12:00:00Z"
}
```

#### 2. Sign Transaction

```http
POST /api/wallets/{id}/sign
```

**Request Body:**
```json
{
  "data": "SGVsbG8gV29ybGQ=",  // Base64-encoded data
  "algorithm": "ED25519"
}
```

**Response:** `200 OK`
```json
{
  "signature": "3045022100...",  // Base64-encoded signature
  "algorithm": "ED25519",
  "timestamp": "2025-11-17T12:05:00Z"
}
```

#### 3. Encrypt Payload

```http
POST /api/wallets/{id}/encrypt
```

**Request Body:**
```json
{
  "data": "Sensitive information to encrypt",
  "recipientWalletId": "wallet-999"
}
```

**Response:** `200 OK`
```json
{
  "encryptedData": "LS0tLS1CRUdJTi...",
  "recipientWalletId": "wallet-999",
  "algorithm": "AES-256-GCM"
}
```

#### 4. Decrypt Payload

```http
POST /api/wallets/{id}/decrypt
```

**Request Body:**
```json
{
  "encryptedData": "LS0tLS1CRUdJTi..."
}
```

**Response:** `200 OK`
```json
{
  "data": "Decrypted sensitive information",
  "timestamp": "2025-11-17T12:10:00Z"
}
```

#### 5. Provision Org Master Key (Feature 083)

```http
POST /api/wallets/org/{orgId}/master-key
```

Provisions an organisation master key for HD key derivation. One-shot operation — the mnemonic is returned once and cannot be retrieved again.

**Response:** `201 Created`
```json
{
  "orgId": "org-123",
  "mnemonic": "word1 word2 ... word24",
  "walletAddress": "did:sorcha:org:0xabc...",
  "message": "Store mnemonic securely. It cannot be retrieved again."
}
```

#### 6. Derive User Key (Feature 083)

```http
POST /api/wallets/org/{orgId}/derive-key
```

**Request Body:**
```json
{
  "userId": "user-456",
  "departmentId": "dept-789",
  "usage": "Identity",
  "custodyMode": "Custodial"
}
```

**Response:** `200 OK`
```json
{
  "derivedKeyId": "dk-001",
  "walletAddress": "did:sorcha:w:0xdef...",
  "publicKey": "base64-encoded-public-key",
  "derivationPath": "m/0x534F52'/org'/dept'/user'/0/0",
  "usage": "Identity",
  "status": "Active"
}
```

#### 7. Rotate Key (Feature 083)

```http
POST /api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate
```

Rotates a derived key by incrementing the index. The old key remains available for decryption only.

**Response:** `200 OK`
```json
{
  "newDerivedKeyId": "dk-002",
  "previousKeyId": "dk-001",
  "previousKeyStatus": "Rotated",
  "walletAddress": "did:sorcha:w:0xghi...",
  "publicKey": "base64-encoded-new-public-key"
}
```

#### 8. Revoke Key (Feature 083)

```http
DELETE /api/wallets/org/{orgId}/keys/{derivedKeyId}
```

Revokes a derived key. The wallet is locked and a DID revocation event is published for identity keys.

**Response:** `200 OK`
```json
{
  "derivedKeyId": "dk-001",
  "status": "Revoked",
  "revokedAt": "2026-04-04T12:00:00Z"
}
```

#### 9. Download Decrypted File Attachment (Feature 085)

```http
GET /api/v1/wallets/{address}/files/download
```

Downloads a decrypted file attachment from a stored data transaction. The wallet at `{address}` must have access to the encrypted field.

**Query Parameters:**
- `registerId` (string, **required**): The register containing the transaction
- `actionTxId` (string, **required**): Transaction ID of the action that holds the file
- `fieldName` (string, **required**): JSON field name of the file attachment in the action payload
- `fileIndex` (integer, optional, default `0`): Index within a multi-file field

**Response:** `200 OK` — binary file stream with appropriate `Content-Type` and `Content-Disposition` headers.

**Auth:** JWT Bearer required. The caller's wallet must be able to decrypt the field (owner or delegated ReadOnly+).

#### 10. Pending Application Notice (Feature 124)

A small, citizen-scoped surface that drives the Citizen Wallet PWA's waiting-state UX. The notice carries only a human-readable label — no credential content. Set by the walkthrough script (or a future application-submission flow) when a citizen submits a credential application; the PWA reads it on every Home render to decide whether to show the waiting card; cleared on credential delivery (or auto-expires after 24 hours).

All three endpoints require the citizen-wallet audience JWT (`sorcha:citizen-wallet`) and are scoped to the caller's `PlatformUserId`. Rate-limited by `RateLimitPolicies.Strict`. Contract: [`specs/124-assured-identity-pwa/contracts/pending-application-notice.openapi.yaml`](https://github.com/Sorcha-Platform/Sorcha/tree/master/specs/124-assured-identity-pwa/contracts/pending-application-notice.openapi.yaml).

##### GET /api/v1/wallet/pending-applications

Returns the active notice for the calling citizen, or `null` if none is set.

**Response:** `200 OK`
```json
{ "notice": { "label": "Assured Identity", "setAt": "2026-05-14T09:41:22Z" } }
```
or
```json
{ "notice": null }
```

##### PUT /api/v1/wallet/pending-applications

Set or replace the citizen's notice. Idempotent — re-setting with a new label replaces the prior one and resets the 24-hour TTL.

**Request Body:**
```json
{ "label": "Assured Identity" }
```

**Validation:** label non-empty + non-whitespace + ≤ 80 characters.

**Response:** `200 OK` with the same envelope shape as GET.

##### DELETE /api/v1/wallet/pending-applications

Clear the citizen's notice. Idempotent — returns `204 No Content` whether or not a notice was present.

**Response:** `204 No Content`

#### 10b. Holder Keys — cross-node delivery keys (Feature 137)

Returns the signed-in citizen's **public** delivery keys so a blueprint `sorcha-holder-key` form field can auto-fill the cross-node submission. Consumer-tier (`RequireConsumerAudience`). Public material only — never a private key. Contract: [`specs/137-cross-node-submission/contracts/holder-keys-endpoint.openapi.yaml`](https://github.com/Sorcha-Platform/Sorcha/tree/master/specs/137-cross-node-submission/contracts/holder-keys-endpoint.openapi.yaml).

##### GET /api/v1/wallet/holder-keys

Resolves the caller's slot-108 holder public JWK (for the SD-JWT `cnf` binding) and wallet encryption public key (for the on-register AEAD envelope) from the citizen JWT.

**Response:** `200 OK`
```json
{
  "holderJwk": { "kty": "EC", "crv": "P-256", "x": "…", "y": "…" },
  "encryptionPublicKey": "<base64 wallet public key>",
  "algorithm": "ED25519",
  "walletAddress": "ws1q…"
}
```
`401` — missing/invalid citizen token. `404` — no wallet resolvable for the caller (indistinguishable from non-existence).

##### POST /api/v1/wallet/presentations/sign-kb

Signs a KB-JWT signing input with the **authenticated caller's** server-custodied slot-108 holder key (#1195 Phase 2, Task 6a). Lets the wallet PWA present a holder-`cnf` credential (e.g. the `AssuredIdentityCredential` root) without the holder private key ever leaving server custody. Consumer-tier (`RequireConsumerAudience`), rate-limited (`Strict`). The signing wallet is resolved from the JWT only — the body carries no wallet address, so a citizen can only sign under their own holder key.

**Request:**
```json
{ "signingInput": "<base64url(header)>.<base64url(payload)>" }
```

The decoded header MUST carry `typ: "kb+jwt"` (the endpoint refuses to sign anything else — the same key signs device delegation credentials, so it must never be a general-purpose signing oracle) and an `alg` matching the holder key's algorithm (EC P-256 → `ES256`, OKP Ed25519 → `EdDSA`).

**Response:** `200 OK`
```json
{ "signature": "<base64url raw signature>", "algorithm": "EdDSA" }
```

`400` — not a KB-JWT header, or header `alg` mismatches the holder key (named error). `401` — missing/invalid citizen token. `404` — no wallet resolvable for the caller.

#### 11. Cross-Device Presentation History (Feature 114 US5)

The citizen's durable, cross-device record of presentations they have made. Reported presentations (via `POST /api/v1/wallet/presentations/log`) are persisted to a per-citizen Wallet Service store so the same history appears on any device the citizen pairs. **There is no register/ledger write** — a free-standing offline presentation has no originating register; these are citizen-owned convenience records carrying disclosed claim **names only**, never values.

Both endpoints require the citizen-wallet audience JWT (`sorcha:citizen-wallet`), are scoped to the caller's `PlatformUserId`, and are rate-limited by `RateLimitPolicies.Strict`. Contract: [`specs/134-presentation-history/contracts/presentation-history.openapi.yaml`](https://github.com/Sorcha-Platform/Sorcha/tree/master/specs/134-presentation-history/contracts/presentation-history.openapi.yaml).

##### GET /api/v1/wallet/presentations

Returns every presentation the authenticated citizen has reported, from any of their devices, newest-first. Returns an empty list (never `404`) when there is no history. Backs the PWA Activity page's cross-device history.

**Response:** `200 OK`
```json
{
  "entries": [
    {
      "id": "8f2c…",
      "credentialId": "1a9b…",
      "verifierLabel": "Strathcarron Council",
      "verifierDid": null,
      "disclosedClaims": ["givenName", "familyName"],
      "presentedAt": "2026-05-20T09:41:22Z",
      "outcome": "Presented",
      "registerId": null,
      "actionTxId": null
    }
  ]
}
```

`outcome` ∈ `Presented | DeclinedByCitizen | VerifierRejected | Acknowledged`. `registerId`/`actionTxId` are vestigial and always `null` for these citizen-owned records.

##### DELETE /api/v1/wallet/presentations/{id}

Server-authoritative delete: removes the entry from the citizen's history across all their devices. Idempotent. A delete targeting another citizen's entry, or a non-existent entry, returns `204` — indistinguishable from success, to avoid leaking existence. Does not affect the verifier's own records (there is no register/ledger record for these presentations).

**Response:** `204 No Content`

---

### Ethereum Transacting (Feature 182, Phase 4)

Native ETH transfers from a wallet's auxiliary secp256k1 identity, signed server-side with a pure-managed
EIP-1559 (type-2) encoder and broadcast over a write-capable EVM RPC. Testnet-first and gated by the
`Ethereum:Transactions` policy (chain allowlist, `AllowMainnet` master switch, per-tx value cap, fee
ceiling) plus the `CanTransactEthereum` authorization policy. Amounts are wei as decimal strings. Fire-and-
report-hash: the send returns immediately; poll the status endpoint for the receipt. Fail-closed — any
policy/RPC/estimate failure refuses the transfer with a machine-readable `reason` and no broadcast.

#### Send a native ETH transfer

```http
POST /api/v1/wallets/{walletAddress}/ethereum/transactions
```

**Request:** `{ "chainId": 11155111, "to": "0x…", "valueWei": "1000000000000000", "index": 0 }`

**Response:** `200 OK` — `{ "txHash": "0x…", "from": "0x…", "chainId": 11155111, "nonce": 3, "status": "submitted" }`

Refusals: `400` (`chain-not-enabled` / `mainnet-not-allowed` / `value-over-cap` / `fee-over-ceiling` /
`invalid-address` / `invalid-amount`), `403` (missing `CanTransactEthereum`), `404` (`wallet-not-found`),
`502` (`rpc-error` / `estimate-failed` / `broadcast-failed`).

#### Preview the cost (read-only)

```http
POST /api/v1/wallets/{walletAddress}/ethereum/transactions/preview
```

Same request body. **Response:** `200 OK` — `{ chainId, from, nonce, gasLimit, maxFeePerGasWei,
maxPriorityFeePerGasWei, valueWei, estimatedTotalCostWei }`. Nothing is signed or broadcast.

#### Get transfer status

```http
GET /api/v1/ethereum/transactions/{chainId}/{txHash}
```

**Response:** `200 OK` — `{ "txHash": "0x…", "status": "pending" | "success" | "reverted", "blockNumber": …, "gasUsed": … }`

---

## Register Service API

### Base Path: `/api/registers`

### Endpoints

#### 1. Initiate Register Creation

```http
POST /api/registers/initiate
```

**Authorization:** Requires `CanManageRegisters` policy (`org_id` claim + Administrator or SystemAdmin role). Anonymous access is not permitted.

**Request Body:**
```json
{
  "title": "Production Register",
  "description": "Main production ledger",
  "purpose": "General"
}
```

- `purpose` (optional): `General` (default) or `System`. Setting `System` requires the `CanCreateSystemRegisters` policy (SystemAdmin org + SystemAdmin role).

**Response:** `201 Created`
```json
{
  "id": "register-101",
  "title": "Production Register",
  "purpose": "General",
  "createdAt": "2025-11-17T13:00:00Z"
}
```

#### 1b. Finalize Register Creation

```http
POST /api/registers/finalize
```

**Authorization:** Requires authentication. Anonymous access is not permitted.

**Response:** `200 OK`

#### 1c. List Registers

```http
GET /api/registers
```

Returns only registers the caller's organization is subscribed to, plus system registers. Scope is derived from the `org_id` JWT claim. The `tenantId` query parameter has been removed.

**Response:** `200 OK`
```json
{
  "items": [
    {
      "id": "register-101",
      "title": "Production Register",
      "purpose": "General",
      "createdAt": "2025-11-17T13:00:00Z"
    }
  ]
}
```

#### 1d. Delete Register

```http
DELETE /api/registers/{id}
```

Authorization is based on control record attestations: the `wallet_address` JWT claim is matched against Owner/Admin attestations on the register. System registers cannot be deleted. The `tenantId` query parameter has been removed.

**Response:** `204 No Content`
```

#### 2. Submit Transaction

```http
POST /api/registers/{id}/transactions
```

**Request Body:**
```json
{
  "transactionType": "Action",
  "senderAddress": "wallet-789",
  "payload": "eyJkYXRhIjoid...",  // Base64-encoded
  "metadata": {
    "blueprintId": "bp-123",
    "actionId": "0"
  }
}
```

**Response:** `202 Accepted`
```json
{
  "transactionId": "tx-abc123",
  "status": "pending",
  "timestamp": "2025-11-17T13:05:00Z"
}
```

#### 3. Get Transaction

```http
GET /api/registers/{id}/transactions/{txId}
```

**Response:** `200 OK`
```json
{
  "transactionId": "tx-abc123",
  "transactionType": "Action",
  "senderAddress": "wallet-789",
  "timestamp": "2025-11-17T13:05:00Z",
  "docketId": "docket-001",
  "status": "confirmed"
}
```

#### 4. Query Transactions

```http
GET /api/registers/{id}/transactions
```

**Query Parameters:**
- `senderAddress` (string): Filter by sender
- `startTime` (ISO 8601): Start of time range
- `endTime` (ISO 8601): End of time range
- `page` (integer): Page number
- `pageSize` (integer): Items per page
- `$filter` (OData): OData V4 filter expression

**Example:**
```http
GET /api/registers/reg-101/transactions?senderAddress=wallet-789&startTime=2025-11-17T00:00:00Z
```

**Response:** `200 OK`
```json
{
  "items": [...],
  "page": 1,
  "totalCount": 150
}
```

#### 5. Transaction Graph (Register Map)

```http
GET /api/registers/{registerId}/transactions/graph
```

Returns a lightweight transaction graph for DAG visualization in the Transaction Explorer UI.

**Query Parameters:**
- `limit` (int, default 200, max 1000) — maximum nodes to return
- `before` (string, optional) — cursor TxId for pagination

**Response:** `200 OK`
```json
{
  "nodes": [
    {
      "txId": "tx-abc123",
      "prevTxId": "tx-abc122",
      "senderWallet": "wallet-789",
      "timeStamp": "2025-11-17T13:05:00Z",
      "docketNumber": 1,
      "blueprintId": "bp-123",
      "instanceId": "inst-456",
      "transactionType": "Action"
    }
  ],
  "totalCount": 150,
  "hasMore": true
}
```

#### 6. Seal Docket (Create Block)

```http
POST /api/registers/{id}/dockets/seal
```

**Response:** `201 Created`
```json
{
  "docketId": "docket-002",
  "previousHash": "0000abc123...",
  "merkleRoot": "def456...",
  "transactionCount": 25,
  "timestamp": "2025-11-17T14:00:00Z"
}
```

#### 6. Register Policy API (Feature 048)

##### Get Register Policy

```http
GET /api/registers/{registerId}/policy
```

**Response:** `200 OK`
```json
{
  "registerId": "aabbccdd11223344aabbccdd11223344",
  "policy": {
    "version": 1,
    "governance": { "quorumFormula": "strict-majority", "proposalTtlDays": 7 },
    "validators": { "registrationMode": "public", "minValidators": 1, "maxValidators": 100, "operationalTtlSeconds": 60 },
    "consensus": { "signatureThresholdMin": 2, "signatureThresholdMax": 10, "maxTransactionsPerDocket": 1000 },
    "leaderElection": { "mechanism": "rotating", "heartbeatIntervalMs": 1000, "leaderTimeoutMs": 5000 }
  },
  "isDefault": false
}
```

##### Propose Policy Update

```http
POST /api/registers/{registerId}/policy/update
```

**Request Body:**
```json
{
  "policy": { "version": 2, "governance": { "quorumFormula": "supermajority" }, "..." : "..." },
  "updatedBy": "did:sorcha:w:addr123",
  "transitionMode": "immediate"
}
```

**Response:** `202 Accepted`
```json
{
  "txId": "aabbcc...64hex",
  "registerId": "aabbccdd11223344aabbccdd11223344",
  "newVersion": 2,
  "status": "submitted"
}
```

##### Get Policy History

```http
GET /api/registers/{registerId}/policy/history?page=1&pageSize=20
```

##### List Approved Validators

```http
GET /api/registers/{registerId}/validators/approved
```

**Response:** `200 OK`
```json
{
  "registerId": "aabbccdd11223344aabbccdd11223344",
  "registrationMode": "consent",
  "validators": [
    { "did": "did:sorcha:w:validator1", "publicKey": "base64...", "approvedAt": "2026-03-01T00:00:00Z" }
  ],
  "count": 1
}
```

##### List Operational Validators

```http
GET /api/registers/{registerId}/validators/operational
```

**Response:** `200 OK`
```json
{
  "registerId": "aabbccdd11223344aabbccdd11223344",
  "validators": [
    { "validatorId": "val-001", "did": "did:sorcha:w:validator1", "status": "active", "ttlRemainingSeconds": 45 }
  ],
  "count": 1
}
```

#### 7. System Register API (Feature 048)

##### Get System Register

```http
GET /api/system-register
```

**Response:** `200 OK`
```json
{
  "registerId": "00000000000000000000000000000001",
  "name": "Sorcha System Register",
  "status": "online",
  "blueprintCount": 1,
  "createdAt": "2026-03-01T00:00:00Z"
}
```

##### List System Blueprints

```http
GET /api/system-register/blueprints?page=1&pageSize=20
```

##### Get System Blueprint

```http
GET /api/system-register/blueprints/{blueprintId}
```

##### Get System Blueprint Version

```http
GET /api/system-register/blueprints/{blueprintId}/versions/{version}
```

#### 6. Query Blueprint Version History (Feature 059)

Returns semantic version history for a published blueprint.

```http
GET /api/system-register/blueprints/{blueprintId}/versions
```

**Response:** `200 OK` — Array of version entries with `major`, `minor`, `changeType` (structural/documentation), `structuralHash`, `publishedAt`, `publishedBy`, `transactionId`.

#### 7. Classify Blueprint Change (Feature 059)

Compares a new blueprint against the latest published version to determine change type.

```http
POST /api/system-register/blueprints/{blueprintId}/classify-change
Content-Type: application/json

{ "newBlueprint": { /* full blueprint JSON */ } }
```

**Response:** `200 OK` — `changeType`, `currentVersion`, `proposedVersion`, `structuralHashCurrent`, `structuralHashNew`, `structuralFieldsChanged`.

#### 8. Disable Dev Mode (Feature 078)

Promotes a register from DevMode (plaintext payloads) to Normal (mandatory field-level encryption).
The transition is **one-way** and **asynchronous**.

```http
POST /api/registers/{registerId}/disable-dev-mode
```

**Authorization:** Requires `CanManageRegisters` policy.

The endpoint does not flip a local flag. It submits a `CryptoPolicyUpdate` **control transaction**
(action id `control.crypto.update`) through the Validator, so the promotion seals into a docket and
replicates to every node holding the register. `Register.DevMode` flips on each node as it writes
that docket — **not when this call returns**. Poll `GET /api/registers/{registerId}` until `devMode`
reads `false`; on a healthy single-validator node that is typically a few seconds.

**Response:** `200 OK` — the update was accepted for sealing, not yet applied.
```json
{
  "registerId": "register-101",
  "txId": "959f3d8c…",
  "policyVersion": 2,
  "status": "submitted",
  "message": "Dev mode disable submitted as a crypto-policy update. Field-level encryption becomes mandatory once the control transaction seals; the change replicates to all nodes."
}
```

**Errors:**
- `409 Conflict` — Dev mode is already disabled on this register.
- `422 Unprocessable Entity` — the Validator rejected the policy update.

**Already-sealed transactions are not retrospectively encrypted.** Promotion changes the posture for
*new* payloads only; everything written while the register was in DevMode stays plaintext forever.
That is immutability, not a defect — but it means a promoted register is not a wholly encrypted
register, and any assessment of it must say so.

> **Removed:** `PUT /api/registers/{registerId}/devmode` no longer exists. It wrote `Register.DevMode`
> directly, which made it a bidirectional local flag flip: it could re-enable DevMode on a Normal
> register, reverting new submissions to plaintext, and it never replicated. Because it emitted no
> control transaction, the Validator's one-way guard never ran. A register's DevMode posture is set
> once at genesis and may only ever be promoted DevMode→Normal, via the endpoint above.

#### 9. Recovery Sync Status (Feature 078)

Returns the current recovery/sync status for all local registers. Used for health monitoring of register replication.

```http
GET /health/sync
```

**Authorization:** None (health endpoint). This endpoint exposes register IDs and sync state but no payload data. In production, restrict access via API Gateway routing rules to prevent external enumeration of private register IDs.

**Response:** `200 OK`
```json
{
  "status": "synced",
  "registers": [
    {
      "registerId": "register-101",
      "status": "synced",
      "currentDocket": 42,
      "targetDocket": 42,
      "progressPercent": 100,
      "docketsProcessed": 0,
      "lastError": null,
      "isStale": false
    },
    {
      "registerId": "register-102",
      "status": "recovering",
      "currentDocket": 30,
      "targetDocket": 42,
      "progressPercent": 58,
      "docketsProcessed": 7,
      "lastError": null,
      "isStale": false
    }
  ],
  "checkedAt": "2026-03-16T12:00:00Z"
}
```

**Status values:**
- `synced` — Register is fully up-to-date with the network
- `recovering` — Register is catching up with missed dockets
- `stalled` — Recovery has stopped due to errors

**Top-level `status`** reflects the aggregate: `synced` if all registers are synced, `stalled` if any are stalled, otherwise `recovering`.

**Staleness detection:** A register in `recovering` status is flagged as `isStale: true` if no progress has been made in the last 10 seconds.

---

## Register Governance API (Feature 189)

Changing a register's roster, validators or crypto policy. A change moves through up to three
transactions — a **proposal**, one **approval** per organisation, and an **enactment** — and only the
enactment changes the roster.

**Base path:** `/api/registers/{registerId}/governance`

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| GET | `/roster` | `CanReadTransactions` | Current roster, reconstructed from the control chain |
| GET | `/history` | `CanReadTransactions` | Paginated control transactions |
| GET | `/proposals` | `CanReadTransactions` | Governance operations recorded on the register |
| POST | `/propose` | `CanSubmitTransactions` | Raise an operation |
| GET | `/proposals/{proposalId}/signing-request` | `CanReadTransactions` | What an approver must sign |
| POST | `/proposals/{proposalId}/approve` | `CanSubmitTransactions` | Submit a detached approval |
| GET/POST | `/crypto-policy` | `CanReadTransactions` / `CanSubmitTransactions` | Read or change the encryption posture |

> **`200`/`202` means accepted, never enacted.** A governance change takes effect when its transaction
> seals into a docket. Check the docket and the validator verdict, not the response body.

### POST `/propose`

Raises a governance operation. `operationType` is an **integer** on this endpoint
(`0` Add, `1` Remove, `2` Transfer, `3` AddValidator, `4` RemoveValidator, `5` RotateValidatorKey,
`6` RevokeIssuanceKey, `7` RotateIssuanceKey, `8` CryptoPolicyUpdate) — see issue #1384.

```json
{
  "operationType": 0,
  "proposerDid": "did:sorcha:w:ws11q…",
  "targetDid": "did:sorcha:w:ws11q…",
  "targetRole": 1,
  "justification": "why",
  "validatorEntry": { "validatorId": "…", "publicKey": "…", "algorithm": "ED25519",
                      "derivationContext": "sorcha:docket-signing", "status": 0 }
}
```

`validatorEntry` is **required** for `AddValidator` and `RotateValidatorKey`.

Two outcomes, and they are not interchangeable:

| Status | When | What was written |
|--------|------|------------------|
| `200` | Quorum is already met — the Owner override on a single-owner register | **One** control transaction that proposes *and* enacts, carrying the updated roster |
| `202` | Quorum is required | A **pending proposal** carrying `roster: null`, awaiting approvals |

A pending proposal deliberately carries no roster, so it does not become the roster head — otherwise
its own `RosterSnapshotId` would stop matching and it would invalidate itself the instant it sealed.

### GET `/proposals/{proposalId}/signing-request?approverDid=…`

Returns the full operation the approving organisation is being asked to authorise.

```json
{
  "requestId": "…", "registerId": "…",
  "operation": { "…the whole GovernanceOperation…" },
  "statementVersion": "sorcha:governance-approval:v2",
  "approverDid": "did:sorcha:w:ws11q…",
  "expiresAt": "2026-08-15T12:00:00Z"
}
```

**It carries no digest, deliberately.** A server-supplied digest could fail to match the operation the
client displayed, reinstating at the transport layer the substitution that statement v2 closes inside
the digest. The client derives the digest from the operation it rendered, so the two cannot disagree —
and it must render it, because signing an opaque value is not approval.

Returns `404` while the proposal has been accepted but not yet sealed.

### POST `/proposals/{proposalId}/approve`

Accepts an approval produced **outside** the platform — the server never holds a multi-party
register's slot-100 key, so it cannot manufacture one.

```json
{
  "requestId": "…",
  "approverDid": "did:sorcha:w:ws11q…",
  "isApproval": true,
  "signature": "…", "publicKey": "…",
  "authMethod": 3,
  "comment": "optional",
  "authorisation": {
    "kind": 0,
    "individualDid": "did:sorcha:w:ws11q…",
    "signature": "…", "publicKey": "…",
    "authMethod": 3,
    "algorithm": "ED25519"
  }
}
```

`signature` is over the **v2 approval statement**, produced with the organisation's slot-100 key —
not over the transaction. `authorisation` is required on every approval: an autonomous approver is
delegated, not unaccountable, so every approval resolves to a named individual, either directly or
through a signed delegation. For a direct authorisation the individual signs the same statement with
their wallet's **primary** key, because the wallet address derives from it and that is what binds the
signature to the DID named as accountable.

| Status | Meaning |
|--------|---------|
| `202` | Accepted and carried to the ledger. It counts when it seals |
| `400` | A signature did not verify (`reason: "SignatureInvalid"`) |
| `403` | Approver not on the proposal's roster, or the delegation does not cover this operation |
| `404` | No such proposal on this register |
| `409` | Proposal not open, expired, or invalidated by a roster change |
| `422` | The authorisation does not hold together — missing, incomplete, or naming an individual the key does not belong to |

Repeat submissions are idempotent: the approval's transaction id is derived from
`(register, proposal, approver)`, so a resubmission dedupes rather than adding a second vote.

### Enactment

There is no enactment endpoint. When a proposal's approvals reach quorum, the enactment is raised by a
reaction to the sealed docket — every node holding the register reaches the same answer from the same
sealed content, and the transaction id is deterministic so concurrent submissions dedupe. Poll
`GET /roster`: `lastControlTxId` moves to the enactment and the membership changes.

---

## Provenance API

**Read-only** evidence surfaces (Feature 188, Phase 1): a register's docket lineage, and per-docket
verification. Owned by the **Register Service**, which holds the evidence. Nothing is written.

**Authorization:** `RequireAdministrator` **composed with** `RequirePlatformAudience` — the tier gate
sits *on* the role gate (CLAUDE.md pattern #13). An Administrator role presented on a *consumer-tier*
token is refused `403`.

### Base Path: `/api/provenance`

Two endpoints rather than one, deliberately. Verification is O(n) hashing per docket, so a spine that
verified eagerly would be O(n·m) on a list view. **The spine runs no checks**; the trail verifies
exactly one docket.

#### 1. Register spine — list dockets from genesis

```http
GET /api/provenance/registers/{registerId}?fromDocket={n}&pageSize={1..200}
```

Returns docket summaries in order. **Runs no verification.**

**Response:** `200 OK`
```json
{
  "registerId": "b388e51816e34d4ea7ce275ca7e8219c",
  "dockets": [
    {
      "docketNumber": 0,
      "sealedAt": "2026-08-05T17:25:11Z",
      "proposerValidatorId": "local-validator",
      "signerCount": 0,
      "rosterChanged": false
    }
  ],
  "hasMore": true,
  "nextFromDocket": 6
}
```

- `signerCount: 0` **is valid and expected** on single-validator deployments. It means no quorum
  evidence was recorded — not that signatures are missing or invalid.
- `rosterChanged` marks a docket where the validator set changed. This is what makes network growth
  visible as history rather than inferred from a config file.
- A spine entry deliberately carries **no check result**. Verification belongs to the trail endpoint.

`404` when the register is not held on this node.

#### 2. Docket trail — verify one docket

```http
GET /api/provenance/registers/{registerId}/dockets/{docketNumber}
```

**Response:** `200 OK`
```json
{
  "registerId": "b388e51816e34d4ea7ce275ca7e8219c",
  "docketNumber": 5,
  "checks": [
    { "layer": "anchor",   "status": "unverified", "headline": "Origin does not trace to this node's trust anchor",
      "checkedAgainst": "the trust anchor this node holds, compared with the fingerprint recorded for the register's origin",
      "reason": "this node's trust anchor describes the 'sorcha-dev' network, whose genesis payload hash does not match the system register this node actually holds ... see issue #1374" },
    { "layer": "chain",    "status": "verified",   "headline": "Links to docket 4",
      "checkedAgainst": "the hash of the predecessor docket as held by this node" },
    { "layer": "seal",     "status": "verified",   "headline": "Contents are unchanged since sealing",
      "checkedAgainst": "the sealed Merkle root, compared with a root recomputed from the docket's stored transactions (one leaf per transaction id, in stored order)" },
    { "layer": "signers",  "status": "unverified", "headline": "No quorum evidence was recorded for this docket",
      "checkedAgainst": "the validator roster as it stood at this docket — version 1, established by control transaction ctrl-genesis (sealed in docket 0)",
      "reason": "this docket carries no consensus votes ... expected on a single-validator deployment" },
    { "layer": "proposer", "status": "unverified", "headline": "Proposer could not be matched to the validator set",
      "checkedAgainst": "the validator roster as it stood at this docket — version 1, ...",
      "reason": "the docket records its proposer as 'local-validator', which does not appear in the roster ..." }
  ]
}
```

Checks are ordered broadest-first: `anchor`, `chain`, `seal`, `signers`, `proposer`.

**`status` is one of `verified` / `failed` / `unverified`.**

> **`unverified` means the check COULD NOT RUN.** It is a first-class result — not an error, not a
> failure — and it never vetoes an otherwise-passing trail. A check that did not run must never report
> `verified`. A partial replica, a single-validator node, and a docket predating the evidence a check
> needs all produce `unverified` rows; rendering those as failures would tell an operator their ledger
> had been tampered with.

**`checkedAgainst` is required on every check**, including ones that could not run: a result whose
basis the reader cannot restate is an assertion, not evidence. For `seal` it says the root was
*recomputed* from stored data — which proves the contents are unchanged since sealing, **not** that
they are independently correct.

**Missing evidence returns `200`, never a `5xx`** — affected rows report `unverified` with a reason,
because an auditor needs to know *which* link could not be established. `404` is reserved for an
unknown register or docket.

#### 3. Application lineage (Phase 2 — reserved)

```http
GET /api/provenance/instances/{instanceId}
```

Reserved so the route shape is settled. Returns `501 Not Implemented` in Phase 1.

---

## HAIP Service API

The HAIP Service is Sorcha's boundary to the external wallet ecosystem — the OpenID4VCI **issuer** and OpenID4VP **verifier** that speak to holder wallets (EUDI Wallet, GOV.UK Wallet, and other HAIP 1.0 wallets). It issues SD-JWT VC / mdoc credentials to those wallets and verifies presentations from them.

Sorcha's own internal credential flows do **not** go through this service — HAIP is specifically the *external* wallet interop surface.

**Gateway routing:** HAIP routes are exposed at the gateway **root** (there is no `/haip` prefix) and forwarded 1:1 to the service. The issuer's base URL in emitted metadata is set by `Haip:IssuerUrl`. Wallet-facing endpoints are anonymous by protocol (the credential endpoint validates a Bearer access token inline so it can return OAuth-style error bodies); the service-to-service and verifier-management endpoints are authenticated.

### OpenID4VCI — issuance (to external wallets)

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| GET | `/.well-known/openid-credential-issuer` | Anonymous | Issuer metadata (HAIP 1.0 §5) — supported credential types, endpoints, algorithms. Wallets fetch this to discover the issuer |
| GET | `/.well-known/oauth-authorization-server` | Anonymous | OAuth 2.0 AS metadata — declares the pre-authorized-code grant and the token endpoint |
| POST | `/token` | Anonymous | Exchange a pre-authorized code (`grant_type=urn:ietf:params:oauth:grant-type:pre-authorized_code`, form-encoded) for an access token + `c_nonce`. Response is `Cache-Control: no-store` |
| POST | `/nonce` | Anonymous | Return a fresh `c_nonce` to bind into the wallet's JWT proof of possession |
| POST | `/credential` | Bearer access token (validated inline) | Issue a credential to the wallet: takes the access token (correlated to a credential offer) + a JWT proof of possession, returns a minted SD-JWT VC (`dc+sd-jwt`) or mdoc (`mso_mdoc`) bound to the wallet's holder key via `cnf`. Missing/invalid Bearer → `401 invalid_token` |

### Credential offers (service-to-service)

Internal surface the Blueprint Service uses to drive an issuance workflow. Requires a **service token** (`RequireService`).

**Base path:** `/api/v1/offers`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/v1/offers` | Service token | Create a Credential Offer with a pre-authorized code; returns the offer + a URI for QR rendering |
| GET | `/api/v1/offers/{offerId}` | Service token | Get a Credential Offer's lifecycle state (`Pending` / `Exchanged` / `Expired`) — the Blueprint Service polls this |

### OpenID4VP — verification (from external wallets)

**Base path:** `/api/v1/verifier`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/v1/verifier/requests` | Authenticated (any tier) | Create a Presentation Request; returns the Authorization Request URI for QR rendering. Accepted from the Blueprint Service (service tier), the citizen wallet PWA (consumer tier), or the desk verifier (platform/org tier) |
| GET | `/api/v1/verifier/requests/{requestId}/request-object` | Anonymous | The signed Request Object JWT (carries `dcql_query`, `nonce`, `response_mode`); the wallet fetches it via the `request_uri` from the QR code |
| POST | `/api/v1/verifier/requests/{requestId}/direct-post` | Anonymous | The wallet submits its `vp_token` + `presentation_submission` via `direct_post`; the verifier validates and stores the result |
| GET | `/api/v1/verifier/requests/{requestId}/result` | Authenticated (any tier) | Verification outcome once the wallet has posted — verified claims, accepted/rejected state, and any failure reasons |

---

## Action Workflow API

### Base Path: `/api/actions`

### Endpoints

#### 1. Get Available Blueprints

```http
GET /api/actions/{walletAddress}/{registerAddress}/blueprints
```

**Response:** `200 OK`
```json
{
  "walletAddress": "wallet-789",
  "registerAddress": "register-101",
  "blueprints": [
    {
      "blueprintId": "bp-123",
      "title": "Purchase Order Workflow",
      "version": 1,
      "availableActions": [
        {
          "actionId": "0",
          "title": "Submit Purchase Order",
          "isAvailable": true
        }
      ]
    }
  ]
}
```

#### 2. Submit Action

```http
POST /api/actions
```

**Request Body:**
```json
{
  "blueprintId": "bp-123",
  "actionId": "0",
  "instanceId": "instance-abc",  // Optional, auto-generated if omitted
  "senderWallet": "wallet-789",
  "registerAddress": "register-101",
  "previousTransactionHash": null,  // For first action
  "payloadData": {
    "itemName": "Widget Pro",
    "quantity": 100,
    "unitPrice": 49.99
  },
  "files": [  // Optional file attachments
    {
      "fileName": "invoice.pdf",
      "contentType": "application/pdf",
      "contentBase64": "JVBERi0xLjQK..."
    }
  ]
}
```

**Response:** `200 OK`
```json
{
  "transactionHash": "0xabc123def456",
  "instanceId": "instance-abc",
  "serializedTransaction": "{...}",
  "fileTransactionHashes": ["0xfile001", "0xfile002"],
  "timestamp": "2025-11-17T15:00:00Z"
}
```

#### 3. Get Action Details

```http
GET /api/actions/{walletAddress}/{registerAddress}/{transactionHash}
```

**Response:** `200 OK`
```json
{
  "transactionHash": "0xabc123def456",
  "blueprintId": "bp-123",
  "actionId": "0",
  "instanceId": "instance-abc",
  "senderWallet": "wallet-789",
  "registerAddress": "register-101",
  "payloadData": {...},
  "timestamp": "2025-11-17T15:00:00Z"
}
```

---

## Execution Helper API

### Base Path: `/api/execution`

Client-side helpers for validating and processing actions before submission.

### Endpoints

#### 1. Validate Action Data

```http
POST /api/execution/validate
```

**Request Body:**
```json
{
  "blueprintId": "bp-123",
  "actionId": "0",
  "data": {
    "itemName": "Widget Pro",
    "quantity": 100,
    "unitPrice": 49.99
  }
}
```

**Response:** `200 OK`
```json
{
  "isValid": true,
  "errors": []
}
```

#### 2. Apply Calculations

```http
POST /api/execution/calculate
```

**Request Body:**
```json
{
  "blueprintId": "bp-123",
  "actionId": "0",
  "data": {
    "quantity": 100,
    "unitPrice": 49.99
  }
}
```

**Response:** `200 OK`
```json
{
  "processedData": {
    "quantity": 100,
    "unitPrice": 49.99,
    "totalPrice": 4999.00  // Calculated field
  },
  "calculatedFields": ["totalPrice"]
}
```

#### 3. Determine Routing

```http
POST /api/execution/route
```

**Request Body:**
```json
{
  "blueprintId": "bp-123",
  "actionId": "0",
  "data": {
    "amount": 75000
  }
}
```

**Response:** `200 OK`
```json
{
  "nextActionId": "2",
  "nextParticipantId": "director",
  "isWorkflowComplete": false,
  "matchedCondition": "amount > 50000"
}
```

#### 4. Apply Disclosure Rules

```http
POST /api/execution/disclose
```

**Request Body:**
```json
{
  "blueprintId": "bp-123",
  "actionId": "0",
  "data": {
    "itemName": "Widget Pro",
    "quantity": 100,
    "unitPrice": 49.99,
    "internalNotes": "Confidential"
  }
}
```

**Response:** `200 OK`
```json
{
  "disclosures": [
    {
      "participantId": "seller",
      "disclosedData": {
        "itemName": "Widget Pro",
        "quantity": 100,
        "unitPrice": 49.99
        // "internalNotes" not disclosed to seller
      },
      "fieldCount": 3
    }
  ]
}
```

#### 5. Query Disclosed Prior-Action Data (Feature 176)

Returns the prior-action data of a workflow instance **disclosed to the calling participant**, for the
action being decided. This is the read-side of the DAD disclosure model — the autonomous agent reads it
to decide on the applicant's real submitted data (rather than a blank view), and the MCP participant tool
`sorcha_disclosed_data` consumes the same route. Only fields disclosed to the caller's participant are
ever returned; the disclosure model is never widened.

```http
GET /api/workflows/{instanceId}/actions/{actionId}/disclosures
GET /api/workflows/{instanceId}/disclosures          # instance-wide (anchored on the current action)
```

**Auth:** authenticated (`.RequireAuthorization()`). The caller's wallet(s) are resolved from the
`wallet_address` JWT claim when present, else via a Wallet Service lookup keyed by the caller's identity
(consumer/service-tier tokens omit the wallet binding under Feature 136). Supply the `X-Delegation-Token`
header to enable disclosure-group decryption on encrypted (non dev-mode) registers.

**Response:** `200 OK`
```json
{
  "instanceId": "59ad957b-…",
  "actionId": 2,
  "registerId": "a4f3ac58…",
  "recipientResolved": true,
  "disclosures": [
    {
      "actionId": 1,
      "actionTitle": "Submit Assured Identity Application",
      "disclosedAt": null,
      "data": { "name": { "fullName": "Ada Lovelace" }, "address": { "postcode": "SW1A 2AA" } }
    }
  ],
  "disclosedFields": {
    "name": { "fullName": "Ada Lovelace" },
    "address": { "postcode": "SW1A 2AA" }
  }
}
```

When the caller is **not** a disclosure recipient the endpoint returns `200 OK` with
`recipientResolved: false` and empty `disclosures` / `disclosedFields` (so a consumer can distinguish
"nothing disclosed" from an auth failure). A consuming agent treats that — and any fetch failure — as a
fail-closed hold.

---

## Encrypted Action Flow

### Async Encryption Pipeline

When an action has disclosure rules with encryption, the submission returns `202 Accepted` with an operation ID for tracking progress.

#### Submit Encrypted Action

```http
POST /api/actions
```

**Request Body** (same as standard action, encryption is automatic when blueprint has disclosure rules):
```json
{
  "blueprintId": "bp-123",
  "actionId": "0",
  "senderWallet": "wallet-789",
  "registerAddress": "register-101",
  "payloadData": { "itemName": "Widget Pro", "quantity": 100 },
  "externalRecipientKeys": {
    "wallet-abc": { "publicKey": "base64...", "algorithm": "ED25519" }
  }
}
```

**Response:** `202 Accepted`
```json
{
  "operationId": "op-uuid-123",
  "isAsync": true,
  "instanceId": "instance-abc"
}
```

#### Poll Operation Status

```http
GET /api/operations/{operationId}
Authorization: Bearer {token}
```

**Response:** `200 OK`
```json
{
  "operationId": "op-uuid-123",
  "status": "Encrypting",
  "currentStep": 2,
  "totalSteps": 4,
  "stepName": "Encrypting payloads",
  "percentComplete": 30,
  "transactionHash": null,
  "error": null,
  "createdAt": "2026-03-02T10:00:00Z"
}
```

**Status values:** `Pending`, `ResolvingKeys`, `Encrypting`, `BuildingTransaction`, `Submitting`, `Complete`, `Failed`

#### List Operations (Feature 052)

```http
GET /api/operations?wallet={walletAddress}&page={page}&pageSize={pageSize}
Authorization: Bearer {token}
```

**Query Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `wallet` | string | required | Wallet address to filter operations |
| `page` | int | 1 | Page number (1-based) |
| `pageSize` | int | 10 | Items per page (max 50) |

**Response:** `200 OK`
```json
{
  "items": [
    {
      "operationId": "op-uuid-123",
      "status": "complete",
      "blueprintId": "bp-001",
      "actionTitle": "Submit Disclosure",
      "instanceId": "inst-001",
      "walletAddress": "did:sorcha:w:abc123",
      "recipientCount": 3,
      "transactionHash": "a1b2c3...",
      "errorMessage": null,
      "createdAt": "2026-03-02T10:00:00Z",
      "completedAt": "2026-03-02T10:00:15Z"
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 25,
  "hasMore": true
}
```

**Authorization:** JWT `wallet_address` claim must match the requested wallet address (403 if mismatch).

### Batch Public Key Resolution

```http
POST /api/registers/{registerId}/participants/resolve-public-keys
Authorization: Bearer {token}
Content-Type: application/json

{
  "walletAddresses": ["wallet-abc", "wallet-def"],
  "algorithm": "ED25519"
}
```

**Response:** `200 OK`
```json
{
  "resolved": {
    "wallet-abc": {
      "participantId": "part-1",
      "walletAddress": "wallet-abc",
      "publicKey": "base64...",
      "algorithm": "ED25519",
      "status": "Active"
    }
  },
  "notFound": ["wallet-xyz"],
  "revoked": []
}
```

---

## Real-time Notifications (SignalR)

### Hub topology (Feature 118)

Five canonical hubs — every notification hub other than ChatHub conforms to the thin-signal contract: events carry opaque IDs, timestamps, and a W3C trace token only. Clients pull full detail via the authenticated REST endpoint linked from the typed-client interface.

| Hub | Route | Service | Purpose |
|---|---|---|---|
| BlueprintHub | `/hubs/blueprint` | Blueprint | Action lifecycle, workflow completion, encryption progress |
| WalletHub | `/hubs/wallet` | Wallet | Citizen-wallet device + credential events; future home for transaction-tick + org-credential events |
| RegisterHub | `/hubs/register` | Register | Register lifecycle, docket sealing, sync-state changes |
| TenantHub | `/hubs/tenant` | Tenant | User inbox events (entry added, unread count) |
| ChatHub | `/hubs/chat` | Blueprint | AI Designer streaming RPC — exempt from thin-signal contract per FR-019 |

Authentication: JWT Bearer via `?access_token=` query parameter (browsers can't set Authorization headers on WebSocket upgrades). The `platform_user_id` claim is required on every notification hub.

### Subscribe to BlueprintHub

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`/hubs/blueprint?access_token=${jwt}`)
  .build();

await connection.invoke("SubscribeToWallet", "wallet-789");

connection.on("ActionAvailable", (instanceId, actionId, occurredAt, traceId) => {
  // Fetch detail via GET /api/instances/{instanceId}/actions/{actionId}
});
```

See each `I*HubClient` interface in tree (`Sorcha.Blueprint.Service.Hubs.IBlueprintHubClient`, etc.) for the authoritative method list and REST detail-endpoint references.

---

## Inbox API

The durable per-user inbox lives in the Tenant Service and is fed by every other service via the internal write endpoint. Real-time updates are delivered through TenantHub's `InboxEntryAdded` and `InboxUnreadCountUpdated` events.

### Public surface (citizen JWT, base path `/api/me/inbox`)

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/me/inbox` | List the authenticated user's inbox entries (paginated, optional `category` filter) |
| GET | `/api/me/inbox/unread-count` | Authoritative unread count for the inbox bell |
| GET | `/api/me/inbox/{id}` | Fetch a single entry's full content |
| POST | `/api/me/inbox/{id}/read` | Mark an entry as read |
| POST | `/api/me/inbox/{id}/dismiss` | Dismiss an entry |
| POST | `/api/me/inbox/mark-all-read` | Bulk mark every unread entry as read |

### Internal surface (service principal, `RequireService` policy)

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/internal/inbox` | Bridge endpoint called by Blueprint / Wallet writers via `IPlatformInboxClient` to create entries on behalf of a target user. Idempotent on `(PlatformUserId, SourceEventId)`. |

Categories today: `Action`, `Credential`, `Membership`, `Security`. Each writer constructs a deterministic `SourceEventId` (SHA-1 over the natural keys of the event) so retries fold into a single entry.

---

## Enrolment Session API (Feature 126)

Backs the council-page cold-start onboarding gate. Two endpoints; full contract at `specs/126-enrol-inside-wizard/contracts/enrol-session.openapi.yaml`.

### `POST /api/auth/enrol-session` — mint

Authenticated. Mints a one-time enrolment session JWT bound to the calling user. Returns:

```json
{
  "sessionToken": "<JWT — scope: \"enrol\", exp = iat + 600>",
  "qrUrl": "https://<council-origin>/wallet/enrol?session=<sessionToken>",
  "expiresAt": "2026-05-15T12:30:00Z"
}
```

### `POST /api/auth/enrol-session/redeem` — redeem

Anonymous (the session token is the credential). Validates signature + scope + expiry, atomically consumes the JTI, returns the bound user's identifying details:

```json
{
  "accessToken": "<full citizen access token>",
  "expiresIn": 3600,
  "displayName": "Sarah Example",
  "email": "sarah@example.test"
}
```

Errors map 1:1 to status: `400` (malformed_token / invalid_signature / scope_mismatch), `409` (already_used), `410` (expired). Body shape: `{ "code": "<error_code>", "message": "<human readable>" }`.

### Cross-device pairing signal

`TenantHub.DeviceEnrolled(platformUserId, deviceId)` fires on every successful `PlatformUserDeviceService.RegisterAsync` (including idempotent re-register). Per-user group via `TenantHubGroups.User(platformUserId)`. Council pages subscribe via the existing `/hubs/tenant` connection.

### Return-to allowlist

`Auth:ReturnToAllowlist:Hosts` — entries are exact-host or `*.host` suffix. HTTPS only, with `http://localhost` accepted for dev. F116 signup / login pages consult this before honouring `?returnUrl=` against an external host.

---

## Pairing Short-Code API (Feature 128)

Short codes are human-typeable 6-digit tokens that wrap a standalone enrol-session token. They allow the F128 cold-start "Already started on another device?" flow and the mobile-web install fallback path.

### `POST /api/auth/enrol-session/short-code` — mint

**Auth:** bearer token required (any tier). Rate-limited (`PlatformAuth` policy).

Request body is optional. When supplied:

```json
{ "route": "DesktopHandoff" }
```

`route` is one of `DesktopHandoff` (default), `MobilewebHandoff`, `PwaTakeover`, `ColdLanding` — used for telemetry only.

Response `200 OK`:

```json
{
  "code": "482917",
  "expiresAt": "2026-06-10T10:35:00Z"
}
```

`code` is a 6-digit numeric string. TTL is 5 minutes.

### `POST /api/auth/enrol-session/redeem-short-code` — redeem

**Auth:** anonymous — the code is the credential for this call. Single-use, rate-limited (`PlatformAuth` policy; 5 per code).

Request body:

```json
{ "code": "482917" }
```

Success `200 OK` — returns the same shape as `POST /api/auth/enrol-session/redeem` (access token + user info):

```json
{
  "accessToken": "<citizen access token>",
  "expiresIn": 3600,
  "displayName": "Sarah Example",
  "email": "sarah@example.test"
}
```

Error responses — body shape `{ "code": "<errorCode>", "message": "<human readable>" }`:

| Status | `code` | Meaning |
|--------|--------|---------|
| `400` | `MalformedCode` | Code is absent or not 6 digits |
| `409` | `AlreadyUsedCode` | Code was already redeemed |
| `410` | `ExpiredCode` | Code has passed its 5-minute TTL |
| `429` | `RateLimited` | Too many redeem attempts for this code |

---

## Pairing Resumption API (Feature 128 US2)

The "Email me a link" affordance on the desktop handoff page. Sends the signed-in citizen a magic-link to reopen `/setup/add-device` when they cannot complete pairing in the same browser session.

### `POST /api/auth/pairing-resumption-email` — send

**Auth:** bearer token required. Rate-limited (`PlatformAuth` policy).

Request body: empty. The recipient email is derived from the authenticated principal — the body is intentionally ignored to prevent email enumeration.

Response `202 Accepted` (no body). Returns `401` if the authenticated principal cannot be resolved to a platform user.

### `GET /api/auth/pairing-resumption/redeem` — redeem

**Auth:** anonymous. Rate-limited (`PlatformAuth` policy). Single-use token.

Query parameter: `?token=<signed resumption token>`

Response: `302 Found` redirect. On valid token, redirects to `/auth/login?returnUrl=%2Fapp%2Fsetup%2Fadd-device&email=<hint>&reason=pairing-resumption` so the citizen re-authenticates (the link is a navigation convenience, not a credential bypass) and lands on the handoff page. On expired or consumed token, redirects to `/auth/login?reason=resumption-expired`.

Token lifetime: 24 hours, single-use.

---

## Device Management API (Feature 114 / Feature 128)

The Tenant Service exposes a recovery-path surface for citizens to list and revoke their enrolled wallet devices from the main Sorcha web UI — useful when the device is lost or compromised.

All endpoints are under `/api/v1/me/devices` and require a bearer token (any tier that resolves to a platform user). Auth tier: no per-endpoint policy name — the group-level `.RequireAuthorization()` gate admits any authenticated caller; the platform-user identity is resolved from the JWT claims.

### `GET /api/v1/me/devices` — list devices

Response `200 OK`:

```json
{
  "devices": [
    {
      "deviceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "label": "Stuart's iPhone 16",
      "platform": "iOS 19 / Safari 19",
      "status": "Active",
      "enrolledAt": "2026-05-01T09:00:00Z",
      "revokedAt": null,
      "lastSeenAt": "2026-06-09T14:22:00Z",
      "delegationExpiresAt": "2026-06-11T09:00:00Z"
    }
  ]
}
```

`status` is `"Active"` or `"Revoked"`. Devices are ordered by enrolment date descending and include both active and revoked entries.

### `DELETE /api/v1/me/devices/{deviceId}` — revoke device

Path parameter: `deviceId` (GUID).

- `204 No Content` — device revoked (or was already revoked — idempotent on the Tenant side).
- `401 Unauthorized` — unauthenticated.
- `404 Not Found` — device does not exist or is not owned by the caller (the two cases are intentionally indistinguishable).
- `502 Bad Gateway` — Tenant row was revoked but the Wallet Service status-list flip failed. The Tenant revocation is committed; retry the request to complete the Wallet-side flip.

When revocation succeeds end-to-end, the Wallet Service flips the credential-status-list bit and broadcasts a `DeviceRevoked` SignalR event to the device (causing the wallet PWA to lock itself).

### `GET /api/v1/me/devices/has-any` — aggregate probe

Used by F128 cold-start surfaces (nag banner, pairing takeover) to check whether the citizen has ever paired a device without leaking the full device list.

Response `200 OK`:

```json
{
  "hasAnyDevice": true,
  "latestEnrolledAt": "2026-05-01T09:00:00Z"
}
```

`hasAnyDevice` is `true` only when at least one ACTIVE (non-revoked) device exists. `latestEnrolledAt` is the enrolment timestamp of the most recent active device, or `null` when `hasAnyDevice` is `false`.

---

## EUDI Credential Format & Unified Trust API (Feature 135)

Adds the ISO `mso_mdoc` credential format beside SD-JWT VC and a single `ITrustEvaluator` consulted by every verification path. Most of the surface is internal (the trust evaluator + format handlers); two externally-visible touch-points:

### OpenID4VP `direct_post` — now accepts `mso_mdoc` (HAIP Service)

No new endpoint shape — `mso_mdoc` rides the existing OpenID4VP `direct_post` callback (specs 097/098). The authorization request advertises an mdoc credential via a DCQL query (`format: "mso_mdoc"`, `meta.doctype_value`), and the `vp_token` is a JSON object keyed by the DCQL query id with base64url-encoded CBOR `DeviceResponse` values:

```jsonc
vp_token = { "pid": ["<base64url(DeviceResponse CBOR)>"] }
```

The verifier base64url+CBOR-decodes the `DeviceResponse`, verifies `issuerAuth` (COSE_Sign1 over the tag-24 MSO; issuer key from the `x5chain` label-33 header), recomputes `valueDigests`, reconstructs the OpenID4VP `SessionTranscript` and verifies `DeviceAuth` (holder binding), checks the MSO status list, then routes the trust decision through `ITrustEvaluator`. SD-JWT VC `vp_token`s (compact `~`-delimited strings) keep their existing path. The same OpenID4VCI `/credential` issuance endpoint issues SD-JWT VC or `mso_mdoc` per the offer's `format` + `trustAnchor`.

### Trust-list snapshot admin (Tenant Service) — `/api/v1/trust/trustlists`

Operator-facing management of external trust-anchor snapshots consulted by the `trustlist` trust source. JWT admin-scoped, `RateLimitPolicies.Strict`, Scalar-documented.

| Method | Path | Purpose |
|--------|------|---------|
| PUT | `/api/v1/trust/trustlists/{trustListId}` | Upload/replace a snapshot — `{ source, roots: [base64 DER], freshness }` → `{ trustListId, rootCount, source, createdAt, freshness }` |
| GET | `/api/v1/trust/trustlists/{trustListId}` | Snapshot metadata (id, root count, source, freshness); 404 when unknown |
| GET | `/api/v1/trust/trustlists` | List loaded snapshot ids + freshness |

Snapshot `id` + `freshness` are copied into `TrustEvidence` on every decision that used the list. A live LOTL feed is a future provider behind the same `ITrustListProvider` seam.

> **Superseded by Feature 181 US3.** The placeholder `PUT /trustlists/{id}` above is replaced (clean break) by the ETSI TS 119 612 import surface documented under "EUDI Conformance API (Feature 181)" below — `POST /trustlists/import`, `GET /trustlists`, `GET /trustlists/{id}`, `DELETE /trustlists/{id}`, and the service-tier `GET /trustlists/{id}/anchors`.

### Trust models (request bodies)

- **`CredentialRequirement`** carries `format` (`sd-jwt-vc` | `mso_mdoc`) + `trustPolicy` (replaced the flat `acceptedIssuers`). **`CredentialIssuanceConfig`** carries `format` + `trustAnchor` (`register` | `x509-tenant` | `x509-lotl`).
- **`TrustPolicy`** = `{ sources: [{ kind, confersAssurance?, allowedIssuers?, trustListId? }], combinator: anyOf|allOf, minAssuranceLevel: Low|Substantial|High }`.

mdoc is ES256/P-256-only at the format layer (additive — Sorcha-native + PQC signing unchanged). Full design: `specs/135-eudi-credential-format-trust/` and the `sorcha-architecture` skill.

---

## EUDI Conformance API (Feature 181)

Moves every presentation surface onto the OpenID4VP 1.0 **final** dialect (DCQL, `dc+sd-jwt`) and adds the external X.509 trust rail. All six user stories are shipped. Full design: `specs/181-eudi-conformance/` and the `sorcha-architecture` skill.

### Presentation dialect (US1/US2)

- **DCQL wire shape.** Authorization requests carry a `dcql_query` (Presentation Exchange `presentation_definition` / `input_descriptors` retired and CI-gated). The `vp_token` response is the object-keyed envelope `{ "<queryId>": ["<presentation>"] }`.
- **`dc+sd-jwt`.** SD-JWT VC issuance emits the final `dc+sd-jwt` media type; verification dual-accepts stored `vc+sd-jwt`.
- **Multi-credential / alternatives.** A single request may declare multiple credential queries and `credential_sets` "present any one of" alternatives. Blueprint authors express alternatives via `CredentialRequirement.AnyOfGroup`. Verdict at `direct_post`: every required set has one fully-verified option; unknown envelope key → `DCQL_UNKNOWN_QUERY_ID`.

### Trusted-list snapshot admin (Tenant Service, US3) — `/api/v1/trust/trustlists`

Operators import a signed ETSI TS 119 612 trusted list; Blueprint + HAIP resolve CA anchors from it for the `x509-lotl` / `trustlist` trust source. Admin routes are `RequireAdministrator` + `RequirePlatformAudience`, `RateLimitPolicies.Strict`, Scalar-documented.

| Method | Path | Purpose |
|--------|------|---------|
| POST | `/api/v1/trust/trustlists/import` | Import a snapshot — `multipart` upload or HTTPS fetch-once. Enveloped XMLDSig core verify + TS 119 612 parse + granted CA/QC anchor extraction. Failures: `TRUSTLIST_MALFORMED` / `_SIGNATURE_INVALID` / `_SEQUENCE_REGRESSION` |
| GET | `/api/v1/trust/trustlists` | List loaded snapshots + freshness |
| GET | `/api/v1/trust/trustlists/{trustListId}` | Detail — anchors + extracted-vs-skipped summary |
| DELETE | `/api/v1/trust/trustlists/{trustListId}` | Remove all versions |
| GET | `/api/v1/trust/trustlists/{trustListId}/anchors` | **Service-tier** — DER roots + freshness for verifying services; 404 `TRUSTLIST_UNAVAILABLE` |

Snapshot identity flows into `TrustEvidence.TrustListId` as `{trustListId}#{sequenceNumber}`. Freshness gating: warn mode (default) vouches with a stale-flagged trail; strict mode (`Trust:TrustListStrictFreshness`) fails closed `TRUSTLIST_STALE`.

### Org certificate lifecycle (Tenant Service, US4/US5) — `/api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}`

An org generates a CSR bound to its P-256 issuing key, imports an externally-issued cert+chain, and issues credentials that chain to the external root. Admin routes are `RequireAdministrator` + `RequirePlatformAudience` (the chain reader is public).

| Method | Path | Purpose |
|--------|------|---------|
| GET | `.../certificates` | List internal + imported certs with status/validity/chain summary + `eligibility` (`eligible`, `reason`, `boundKeySource` ∈ `Primary`\|`HaipCoKey`) |
| POST | `.../csr` | Generate a CSR bound to the server-resolved org P-256 key (optional `subjectDn`) → `csrPem`, `boundKeySource`, `boundPublicKeyThumbprint` |
| POST | `.../certificates/import` | Import leaf `certificatePem` + `chainPem[]` (with/without root); supersedes the prior Active imported cert |
| DELETE | `.../certificates/{certificateId}` | Retire an imported cert (Status→Superseded); idempotent. Internal certs use `.../revoke` (CRL) |
| GET | `.../imported-cert-chain` | **Public** — imported chain for x5c resolution |
| POST | `.../enrol` | (existing route, changed semantics) Issue/re-issue the internal tenant-root cert; server resolves the key (no caller-supplied key); doubles as backfill for pre-existing orgs |

Typed failures (`422`, problem+json): `CERT_KEY_NOT_ELIGIBLE` (non-P-256 org key — replaces the prior ASN.1 500), `CERT_KEY_MISMATCH`, `CERT_CHAIN_INVALID`, `CERT_EXPIRED`, `CERT_UNSUITABLE`, `CERT_EXTERNAL_ANCHOR_UNAVAILABLE`. The org P-256 key is its primary key when ES256, else a derived HAIP co-key under `sorcha:haip-issuer-signing`. Auto-enrol runs best-effort after wallet provisioning (org creation + reconciliation ride-along) — it is a server-side hook, not an API. Wallet Service internal seam: `GET/POST /api/internal/wallets/{address}/issuer-cert-key[/sign]` (`IOrgIssuerCertKeyService`). `CredentialIssuanceConfig.TrustAnchor = x509-lotl` attaches the imported external chain and fails closed `CERT_EXTERNAL_ANCHOR_UNAVAILABLE` when unavailable. Metric `sorcha_org_cert_issuance_total{provenance,outcome,reason}`.

### Verifier authentication (HAIP Service, US6)

The HAIP verifier signs its OpenID4VP request object (ES256) with an X.509 **verifier certificate**, embeds the `x5c` chain, and identifies itself with a prefixed **`x509_san_dns:{host}`** `client_id` whose host equals the certificate SAN dNSName. Config: `Haip:VerifierCertificate` (PFX path or base64) + optional `Haip:VerifierCertificatePassword` + `Haip:PublicHost`; dev falls back to a self-signed certificate, prod/staging fail fast when unconfigured.

The wallet authenticates the verifier before consent via `RequestObjectValidator` (`Sorcha.Verifier.Engine`, BouncyCastle / WASM-safe): ES256 JWS verify over the x5c leaf → leaf SAN equals the `client_id` host → chain-walk to a trusted-list anchor → three-state `VerifierAuthState` (`TrustedListVerified` / `AuthenticUntrusted` / `Unverifiable`). Tampered signature / SAN mismatch is a hard refusal (`REQUEST_OBJECT_INVALID` / `REQUEST_HOST_MISMATCH`); absent anchors never block. KB-JWT `aud` is the full prefixed `client_id`. Metric `sorcha_request_auth_total{state}` on `Sorcha.Trust`. Note: the anchor-fetch → `TrustedListVerified` path awaits a public anchors read endpoint (US3's is service-tier), so v1 renders valid signed requests as `AuthenticUntrusted`.

---

## Error Handling

### Standard Error Response

```json
{
  "error": "Invalid blueprint ID",
  "code": "BLUEPRINT_NOT_FOUND",
  "timestamp": "2025-11-17T17:00:00Z",
  "path": "/api/blueprints/invalid-id"
}
```

### Common Error Codes

| HTTP Status | Error Code | Description |
|------------|------------|-------------|
| 400 | `INVALID_REQUEST` | Malformed request body |
| 401 | `UNAUTHORIZED` | Authentication required |
| 403 | `FORBIDDEN` | Insufficient permissions |
| 404 | `NOT_FOUND` | Resource not found |
| 409 | `CONFLICT` | Resource conflict |
| 429 | `RATE_LIMITED` | Too many requests |
| 500 | `INTERNAL_ERROR` | Server error |
| 503 | `SERVICE_UNAVAILABLE` | Service temporarily unavailable |

---

## Rate Limiting

**Current Status:** Not enforced in MVP

**Future Implementation:**
- 100 requests per minute per client
- 1000 requests per hour per client
- Burst allowance: 20 requests

**Headers:**
```http
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 95
X-RateLimit-Reset: 1700000000
```

---

## Code Examples

### Complete Workflow Example (C#)

```csharp
using System.Net.Http.Json;

var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };

// 1. Create wallet
var walletResponse = await client.PostAsJsonAsync("/api/wallets", new
{
    title = "My Wallet",
    keyType = "ED25519"
});
var wallet = await walletResponse.Content.ReadFromJsonAsync<dynamic>();
var walletId = wallet.id;

// 2. Create register
var registerResponse = await client.PostAsJsonAsync("/api/registers", new
{
    title = "My Register"
});
var register = await registerResponse.Content.ReadFromJsonAsync<dynamic>();
var registerId = register.id;

// 3. Create and publish blueprint
var blueprint = new
{
    title = "Simple Workflow",
    participants = new[] { new { id = "p1", name = "Participant 1" } },
    actions = new[] { new { id = "0", title = "Action 1", sender = "p1" } }
};
var bpResponse = await client.PostAsJsonAsync("/api/blueprints", blueprint);
var bp = await bpResponse.Content.ReadFromJsonAsync<dynamic>();
await client.PostAsync($"/api/blueprints/{bp.id}/publish", null);

// 4. Submit action
var action = new
{
    blueprintId = bp.id,
    actionId = "0",
    senderWallet = walletId,
    registerAddress = registerId,
    payloadData = new { message = "Hello World" }
};
var actionResponse = await client.PostAsJsonAsync("/api/actions", action);
var result = await actionResponse.Content.ReadFromJsonAsync<dynamic>();

Console.WriteLine($"Transaction Hash: {result.transactionHash}");
```

### SignalR Real-time Notifications (JavaScript)

Connect to a hub from the table above (here, BlueprintHub). JWT goes in the `access_token` query
parameter because browsers can't set an `Authorization` header on the WebSocket upgrade. Events are
**thin signals** (opaque IDs + timestamps); on receipt, fetch the detail from the authenticated REST
endpoint linked from the typed-client interface.

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl(`/hubs/blueprint?access_token=${token}`)
  .withAutomaticReconnect()
  .build();

connection.onclose(() => console.log("Connection closed; auto-reconnect will retry."));

await connection.start();

// Thin signal: the payload is an instance id + timestamp. Pull full detail via REST.
connection.on("InstanceAdvanced", async (instanceId) => {
  const res = await fetch(`/api/instances/${instanceId}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  updateUI(await res.json());
});
```

---

## API Versioning

**Current Version:** v1 (implicit)

**Future Versioning Strategy:**
- URL-based: `/api/v2/blueprints`
- Header-based: `X-API-Version: 2`

---

## Support and Resources

- **GitHub:** https://github.com/Sorcha-Platform/Sorcha
- **Documentation:** https://docs.sorcha.io
- **API Explorer:** http://localhost:5000/scalar/v1

---

**Last Updated:** 2026-06-10
**Document Version:** 2.4.0
**Feature:** 128 - Cold-start onboarding and device pairing UX

---

## Feature 150 — Account Security endpoints (Tenant Service)

Cross-tier `/me/*` (`.RequireAuthorization()`, F136) — one surface for consumer + platform tokens.

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/me/auth-methods` | Aggregate read: password/social/passkey rows with `AssuranceTier`/`RequiredProofTier`/`CanRemove`, plus `EmailOtpEnabled`, `SmsAvailable`, `SmsOtpEnabled`. |
| POST | `/api/auth/challenge/initiate` · `/verify` | Step-up; carries `TargetMethodKind`; verify → `403 proof_tier_insufficient` when below floor. |
| POST | `/api/me/2fa/email/enable` · `/verify` | Enable email OTP (sends a code; confirm). |
| DELETE | `/api/me/2fa/email` | Disable email OTP. |
| POST | `/api/me/2fa/sms/phone` · `/phone/verify` | Capture + verify a mobile number (404 if SMS unconfigured). |
| POST | `/api/me/2fa/sms/enable` | Enable SMS OTP (requires a verified number). |
| DELETE | `/api/me/2fa/sms` | Disable SMS OTP. |
| POST | `/api/auth/verify-2fa` | Complete login 2FA; `method=email|sms` for server-sent codes. |
| POST | `/api/auth/login/2fa/send-email` | Send/resend an email login code (loginToken-gated). |
