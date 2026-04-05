# Sorcha Platform - API Documentation

**Version:** 2.3.0
**Last Updated:** 2026-04-04
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
13. [Action Workflow API](#action-workflow-api)
14. [Execution Helper API](#execution-helper-api)
15. [Real-time Notifications (SignalR)](#real-time-notifications-signalr)
16. [Error Handling](#error-handling)
17. [Rate Limiting](#rate-limiting)
18. [Code Examples](#code-examples)

---

## Overview

The Sorcha Platform provides a comprehensive REST API for building distributed ledger applications with blueprint-based workflows, secure wallet management, and transaction processing.

**Base URLs:**
- **Development (Local):** `http://localhost:5000`
- **Staging (Azure):** `https://api-gateway.livelydune-b02bab51.uksouth.azurecontainerapps.io`
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
   git clone https://github.com/yourusername/Sorcha.git
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

**Token Endpoint:** `POST /api/service-auth/token`

**Supported Grant Types:**
- `password` - User credentials (email + password)
- `client_credentials` - Service principal authentication

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

### Service Principal Authentication (Client Credentials Grant)

```http
POST /api/service-auth/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&client_id=service-blueprint&client_secret=<secret>
```

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
| POST | `/api/auth/verify-2fa` | Anonymous | Verify TOTP code to complete login (rate limited) |
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
| POST | `/api/auth/passkey/assertion/verify` | Anonymous | Verify assertion and issue tokens |

#### Public User Social Login

**Base Path:** `/api/auth/public/social`

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| POST | `/api/auth/public/social/initiate` | Anonymous | Initiate OAuth flow for provider (Google, Microsoft, GitHub, Apple) |
| POST | `/api/auth/public/social/callback` | Anonymous | Handle OAuth callback, provision user, issue tokens |

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

The Peer Service manages P2P networking, system register replication, and peer discovery.

### Base Path: `/api/peers`

### Endpoints

#### 1. Get All Peers

```http
GET /api/peers
```

**Response:** `200 OK`
```json
[
  {
    "peerId": "peer-123",
    "address": "192.168.1.100",
    "port": 8080,
    "supportedProtocols": ["gRPC", "HTTP"],
    "firstSeen": "2025-12-14T10:00:00Z",
    "lastSeen": "2025-12-14T17:00:00Z",
    "failureCount": 0,
    "isBootstrapNode": true,
    "averageLatencyMs": 15.5
  }
]
```

#### 2. Get Peer by ID

```http
GET /api/peers/{peerId}
```

**Response:** `200 OK` (same structure as single peer above)

#### 3. Get Peer Statistics

```http
GET /api/peers/stats
```

**Response:** `200 OK`
```json
{
  "totalPeers": 10,
  "healthyPeers": 8,
  "averageLatency": 25.3,
  "throughput": 1500,
  "networkHealth": "Good"
}
```

#### 4. Get Peer Health

```http
GET /api/peers/health
```

**Response:** `200 OK`
```json
{
  "totalPeers": 10,
  "healthyPeers": 8,
  "unhealthyPeers": 2,
  "healthPercentage": 80.0,
  "peers": [
    {
      "peerId": "peer-123",
      "address": "192.168.1.100",
      "port": 8080,
      "lastSeen": "2025-12-14T17:00:00Z",
      "averageLatencyMs": 15.5
    }
  ]
}
```

**Hub Node URLs:**
- **n0.sorcha.dev** - Primary hub node (Priority 0)
- **n1.sorcha.dev** - Secondary hub node (Priority 1) - *Coming soon*
- **n2.sorcha.dev** - Tertiary hub node (Priority 2) - *Coming soon*

---

## Blueprint Service API

### Base Path: `/api/blueprints`

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

Irreversibly disables dev mode on a register. Once disabled, field-level encryption becomes mandatory for all new transactions. This operation cannot be undone.

```http
POST /api/registers/{registerId}/disable-dev-mode
```

**Authorization:** Requires `CanManageRegisters` policy.

**Response:** `200 OK`
```json
{
  "registerId": "register-101",
  "devMode": false,
  "message": "Dev mode disabled. Field-level encryption is now required for new transactions."
}
```

**Error:** `409 Conflict` — Dev mode is already disabled on this register.

#### 9. Toggle Dev Mode

Enables or disables dev mode on a register. When enabled, payloads are stored as plaintext with disclosure filtering at read time. When disabled, new payloads use envelope encryption.

> **Security Warning:** This endpoint allows re-enabling dev mode, bypassing the one-way constraint. It is intended for development and testing only. For production registers, use the irreversible `POST /{registerId}/disable-dev-mode` endpoint above. Consider restricting this endpoint via API Gateway routing rules in production deployments.

```http
PUT /api/registers/{registerId}/devmode
```

**Authorization:** Requires `CanManageRegisters` policy.

**Request Body:**
```json
{
  "enabled": false
}
```

**Response:** `200 OK`
```json
{
  "registerId": "register-101",
  "devMode": false,
  "effectiveFrom": "2026-03-16T12:00:00Z"
}
```

**Error:** `404 Not Found` — Register not found.

#### 10. Recovery Sync Status (Feature 078)

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

### Hub Endpoint: `/actionshub`

### Events

#### 1. Subscribe to Actions

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/actionshub")
  .build();

// Subscribe to actions for specific wallet/register
await connection.invoke("SubscribeToActions", "wallet-789", "register-101");

// Listen for action confirmed events
connection.on("ActionConfirmed", (notification) => {
  console.log("Action confirmed:", notification);
});
```

#### 2. Notification Format

```javascript
{
  "transactionHash": "0xabc123def456",
  "walletAddress": "wallet-789",
  "registerAddress": "register-101",
  "blueprintId": "bp-123",
  "actionId": "0",
  "instanceId": "instance-abc",
  "timestamp": "2025-11-17T16:00:00Z",
  "message": "Transaction confirmed"
}
```

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

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5000/actionshub")
  .withAutomaticReconnect()
  .build();

// Handle disconnection
connection.onclose(async () => {
  console.log("Connection closed. Attempting to reconnect...");
});

// Subscribe to actions
await connection.start();
await connection.invoke("SubscribeToActions", "wallet-789", "register-101");

// Listen for notifications
connection.on("ActionConfirmed", (notification) => {
  console.log("Action confirmed:", notification);
  updateUI(notification);
});

connection.on("ActionPending", (notification) => {
  console.log("Action pending:", notification);
});

// Encryption progress events (sent to wallet:{address} group)
connection.on("EncryptionProgress", (notification) => {
  console.log(`Step ${notification.step}/${notification.totalSteps}: ${notification.stepName} (${notification.percentComplete}%)`);
});

connection.on("EncryptionComplete", (notification) => {
  console.log("Encryption complete, tx:", notification.transactionHash);
});

connection.on("EncryptionFailed", (notification) => {
  console.error("Encryption failed:", notification.error);
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

- **GitHub:** https://github.com/yourusername/Sorcha
- **Documentation:** https://docs.sorcha.io
- **API Explorer:** http://localhost:5000/scalar/v1

---

**Last Updated:** 2026-03-16
**Document Version:** 2.2.0
**Feature:** 058 - Platform Organisation Topology
