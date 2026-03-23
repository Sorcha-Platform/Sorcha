# Register Subscriptions & Private Register Invitations

**Date:** 2026-03-23
**Status:** Draft
**Issue:** #113 (UX-001)
**Priority:** P1 (Pre-release)

---

## Overview

Organisations subscribe to registers to scope what their users see. Public registers are discovered via the peer network. Private registers require a signed, encrypted invitation from the register owner — formalised through a governance blueprint on the Sorcha System Register.

Two phases:
- **Phase 1:** Org register subscription management, UI scoping, org wallet provisioning, System Register name fix
- **Phase 2:** Private register invitation flow, org DID method, on-ledger audit trail

---

## Data Model

### Organisation Extensions (Tenant Service)

New fields on `Organization`:

| Field | Type | Purpose |
|-------|------|---------|
| `WalletAddress` | `string?` | Org's HD wallet address (null for legacy orgs until migrated) |
| `PublicKey` | `string?` | Org's public signing key (base64, ED25519) |
| `EncryptionPublicKey` | `string?` | Org's X25519 encryption key (derived from ED25519, base64) |
| `SigningAlgorithm` | `string?` | Signing algorithm (ED25519 default for orgs) |

**Encryption note:** ED25519 is signing-only. For invitation payload encryption (Phase 2), the org's ED25519 key is converted to X25519 via `Sorcha.Cryptography`. The `EncryptionPublicKey` is stored at wallet provisioning time to avoid repeated derivation.

### OrganizationRegisterSubscription (New Entity)

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | `Guid` | PK |
| `OrganizationId` | `Guid` | FK to Organization |
| `RegisterId` | `string` | Register ID. Validated: `^[a-f0-9]{32}$` |
| `RegisterName` | `string?` | Cached display name (denormalised from register metadata) |
| `SubscriptionType` | enum | `Owner` / `Public` / `Invited` |
| `Status` | enum | `Pending` / `Active` / `Suspended` / `Revoked` |
| `InvitationId` | `string?` | Links to the invitation that created this (Phase 2, null for public/owner) |
| `SubscribedAt` | `DateTimeOffset` | When subscription was created |
| `SubscribedByUserId` | `Guid` | Admin who accepted |
| `RevokedAt` | `DateTimeOffset?` | When subscription was revoked |
| `RevokedByUserId` | `Guid?` | Admin who revoked |

**Unique constraint:** `(OrganizationId, RegisterId)` — one subscription per org per register.

**Status transitions:**
- `Pending` → `Active`: Peer-level subscription confirmed
- `Active` → `Suspended`: Admin action or register offline
- `Active` → `Revoked`: Admin unsubscribes (not allowed for `Owner` type)
- `Pending` → `Revoked`: Peer subscription failed after max retries

If peer-level subscription fails after Tenant record creation, a background retry promotes `Pending` to `Active` when successful (max 5 retries, exponential backoff starting at 30s).

### Auto-Subscription Rules

| Trigger | SubscriptionType | Notes |
|---------|-----------------|-------|
| Org creates a register | `Owner` | Automatic, cannot be unsubscribed |
| Bootstrap: System Admin org → System Register | `Owner` | Automatic on first bootstrap |
| Admin subscribes to public register | `Public` | Via UI or API |
| Admin accepts private invitation | `Invited` | Phase 2 |

**Register creation auto-subscribe:** Handled by the API Gateway / UI layer after successful register creation — the JWT contains the org context. No Register Service → Tenant Service cross-service callback needed.

### InvitationNonce (Phase 2 — New Entity, Tenant Service)

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | `Guid` | PK |
| `Nonce` | `string` | UUID v4 nonce from invitation token |
| `InvitationId` | `string` | Invitation ID |
| `SourceOrgId` | `Guid` | Org that created the invitation |
| `TargetOrgId` | `Guid` | Org that accepted |
| `RegisterId` | `string` | Target register |
| `ConsumedAt` | `DateTimeOffset` | When nonce was consumed |

**Unique index on `Nonce`** for fast duplicate lookup. The nonce is also recorded on-ledger via blueprint instance for audit, but the PostgreSQL table provides the fast-path check.

### DID Method Addition (Phase 2)

New format: `did:sorcha:org:<walletAddress>`

- New `SorchaDidType.Organization` enum value
- Regex pattern: `^did:sorcha:org:([A-Za-z1-9]+)$` (matching existing wallet address format)
- Factory method: `SorchaDidIdentifier.FromOrganization(walletAddress)`
- `SorchaDidResolver` extended: resolves `org` method by querying Wallet Service for the public key at that address
- Registered in `DidResolverRegistry`

**Why a separate DID method (not reusing `did:sorcha:w:`):** The `org` prefix provides audit clarity — ledger records clearly distinguish org-level actions from individual wallet actions. Resolution mechanism is identical (wallet address → public key).

---

## API Endpoints

### Phase 1 — Org Register Subscriptions (Tenant Service)

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `GET` | `/api/organizations/{orgId}/register-subscriptions` | Member+ | List org's subscribed registers (paginated, `?page=1&pageSize=25`) |
| `GET` | `/api/organizations/{orgId}/register-subscriptions/{registerId}` | Member+ | Get single subscription status |
| `POST` | `/api/organizations/{orgId}/register-subscriptions` | Admin+ | Subscribe to a public register (body: `{ registerId }`) |
| `DELETE` | `/api/organizations/{orgId}/register-subscriptions/{registerId}` | Admin+ | Unsubscribe (cannot unsubscribe `Owner` type) |
| `GET` | `/api/me/subscribed-registers` | Authenticated | Returns subscriptions for user's active org |

**POST subscribe flow:**
1. Validate register exists via Peer Service `/api/registers/available` (public) or Register Service (local)
2. Create `OrganizationRegisterSubscription` record with status `Pending`
3. Trigger Peer Service to subscribe at replication level
4. On success: promote to `Active`. On failure: remain `Pending`, background retry
5. Return subscription record

**Service communication note:** Tenant Service calls Peer Service and Register Service via existing REST service clients (`Sorcha.ServiceClients`). This follows the established pragmatic pattern throughout the codebase, documented as an exception to the constitution's gRPC preference.

### Phase 2 — Invitations (Tenant Service)

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `POST` | `/api/organizations/{orgId}/register-invitations` | Admin+ | Create invitation (body: `{ registerId, targetOrgDid }`) |
| `POST` | `/api/organizations/{orgId}/register-invitations/accept` | Admin+ | Accept invitation (body: `{ invitationToken }`) |
| `GET` | `/api/organizations/{orgId}/register-invitations` | Admin+ | List pending sent/received invitations |
| `DELETE` | `/api/organizations/{orgId}/register-invitations/{id}` | Admin+ | Revoke a sent invitation |

### Org Wallet Provisioning (Extended Existing Endpoints)

- `POST /api/tenants/bootstrap` — extended to create System Admin org wallet
- `POST /platform/organizations` — extended to create org wallet on org creation

### YARP Gateway Routes

New routes added for subscription and invitation endpoints through API Gateway.

---

## Org Wallet Provisioning

### Wallet Service Integration

Uses existing `CreateWalletRequest` with conventions — no contract changes needed:
- `Name`: `"org-{subdomain}-signing"` (e.g., `"org-sorcha-dev-signing"`)
- `Algorithm`: `"ED25519"`
- `Tags`: `{ "ownerType": "Organization", "ownerId": "{orgId}" }`
- Wallet Service's existing `owner` field set to `"org:{orgId}"`

After creation, Tenant Service stores:
- `WalletAddress` from `CreateWalletResponse`
- `PublicKey` from wallet's public key endpoint
- `EncryptionPublicKey` derived via `Sorcha.Cryptography` ED25519→X25519 conversion
- `SigningAlgorithm` = `"ED25519"`

### Bootstrap Flow (System Admin Org)

1. Existing `POST /api/tenants/bootstrap` creates org + admin user
2. Extended: calls Wallet Service to create HD wallet for the org
3. Wallet address, public key, and encryption key stored on Organisation record
4. System Admin org DID: `did:sorcha:org:<walletAddress>`

### New Org Creation Flow

1. `POST /platform/organizations` (or any org creation path)
2. After org record persisted, Tenant Service creates wallet via Wallet Service
3. Wallet address written back to Organisation record
4. If Wallet Service unavailable, org creation succeeds — wallet provisioned by background reconciliation

### Background Wallet Reconciliation

`OrgWalletReconciliationService : BackgroundService` in Tenant Service:
- Runs on startup, then every 60 seconds
- Finds orgs with null `WalletAddress`
- Provisions wallet with exponential backoff (30s, 60s, 120s, 240s, 480s)
- Max 5 retries per org per service lifetime, then logs warning for manual intervention
- Phase 2 features (invitations) gated behind `WalletAddress != null`

### Key Management

- Org wallet mnemonic follows same pattern as user wallets — platform doesn't store it
- System Admin org: mnemonic is ephemeral, recovery via `OrgRecoveryConfig`
- Org admins can export recovery key via Org Settings UI (Phase 2)

---

## Invitation Flow (Phase 2)

### Encryption Mechanism

Invitation payload encryption uses X25519 key agreement (ECDH):
1. Source org's ED25519 signing key converted to X25519 private key
2. Target org's `EncryptionPublicKey` (X25519, stored on Organisation) used as recipient
3. Shared secret derived via X25519 ECDH, key expanded via HKDF
4. Payload encrypted with AES-256-GCM (constitution §II alignment)
5. `Sorcha.Cryptography` extended with `ED25519ToX25519` conversion if not already present

**Token wire format:** Base64url JSON envelope: `{ v, sig, enc, nonce, sender, epk }`

### Lifecycle

```
1. Source org admin creates invitation
   ├─ Specifies: registerId, targetOrgDid (did:sorcha:org:<address>)
   ├─ Tenant Service resolves target org's encryption public key via DID resolver
   └─ Creates InvitationToken:
      ├─ Payload: { registerId, sourceOrgDid, targetOrgDid, invitationId,
      │             expiresAt, nonce (UUID v4), registerGenesisHash }
      ├─ Signed with source org's ED25519 wallet key
      └─ Payload encrypted with X25519 key agreement (target org's encryption key)

2. Token delivery (out-of-band)
   └─ Email, messaging, QR code — platform-agnostic

3. Target org admin accepts
   ├─ Submits token to accept endpoint
   ├─ Tenant Service decrypts with org's X25519 private key (via Wallet Service)
   ├─ Validates:
   │   ├─ Source org signature (via DID resolution)
   │   ├─ Invitation not expired
   │   ├─ Nonce not consumed (fast check: PostgreSQL InvitationNonce table)
   │   ├─ Target DID matches accepting org
   │   └─ Register genesis hash matches (via Peer/Register Service)
   ├─ Consumes nonce (writes to InvitationNonce table)
   ├─ Creates OrganizationRegisterSubscription (type: Invited)
   ├─ Triggers peer-level subscription to the register
   └─ Records acceptance on System Register (or scenario register) via blueprint instance

4. On-ledger record
   ├─ Blueprint instance records: source org DID, target org DID,
   │   register ID, timestamp, invitation hash (not token itself)
   └─ Auditable trail of who invited whom
```

### Join Private Register Blueprint

Published to the System Register as a governance blueprint. Can be republished to any scenario register for domain-specific customisation (e.g., adding KYC, approval steps, fees).

| Field | Value |
|-------|-------|
| `title` | "Join Private Register" |
| `participants` | `invitor` (source org DID), `invitee` (target org DID) |
| `actions[0]` | CreateInvitation — invitor signs invitation payload |
| `actions[1]` | AcceptInvitation — invitee decrypts and counter-signs |
| `actions[2]` | RecordSubscription — system records the join event |
| `schemas` | Invitation payload schema, acceptance schema |

---

## Security Model

### Attack Vector Mitigations

| Vector | Mitigation |
|--------|------------|
| **Invitation replay** | One-time UUID v4 nonce. Fast check against PostgreSQL `InvitationNonce` table (unique index). Also recorded on-ledger for audit. Configurable expiry (default 7 days). |
| **Org impersonation** | Invitation encrypted to target org's X25519 key via DID. Wrong org can't decrypt. Source ED25519 signature verified via DID resolution — can't forge. |
| **Register ID spoofing** | Invitation payload includes register's genesis hash. Subscribing node verifies genesis hash matches when connecting to peers advertising that register. Mismatch → reject subscription. |
| **Rogue admin** | Acceptance is an auditable on-ledger event. Org can revoke subscription. Other org admins can see full audit trail. |
| **Org enumeration** | No global org directory required. Orgs discoverable only on registers they've published to. Out-of-band DID sharing always available. |
| **Token interception** | Payload encrypted via X25519 ECDH + XChaCha20-Poly1305. Interceptor sees signed envelope but not register ID or terms. |
| **Expired org keys** | DID resolution fetches current key. If org rotated keys since invitation, decryption fails gracefully — source org re-issues with current key. |
| **Denial of service** | Rate limiting on invitation endpoints. Max pending invitations per org (configurable, default 50). Max 10 invitations per hour per org. |

---

## UI Changes

### Phase 1

**Registers page (`/registers`) — consolidated view:**
- Shows only registers the user's active org is subscribed to
- Each card: Name, Description, Status, Subscription Type badge (`Owner`/`Public`/`Invited`)
- "Subscribe to Register" button (Admin+) → dialog showing available public registers from peer network
- "Create Register" button remains (Admin+)
- Unsubscribe action on each card (Admin+, disabled for `Owner` type)

**New Submission page (`/workflows`):**
- Register dropdown filtered to org's subscribed registers only

**Peer Network Admin — simplification:**
- Remove "Available Registers" tab (functionality moved to Registers page subscribe dialog)
- "Register Subscriptions" tab remains for technical peer-level monitoring (sync state, progress)
- Clear separation: Registers page = org-level what, Peer Admin = node-level how

**System Register name fix:**
- Register Service bootstrap ensures System Register name included in peer advertisement

### Phase 2

**Registers page — new actions:**
- "Invite Organisation" button on registers where org is `Owner` → dialog for target org DID
- Invitation token displayed for copying after creation

**Invitations panel (within Registers page):**
- Sent invitations: status, target org, register, expiry, revoke action
- Received invitations: accept/decline, source org details, register info
- "Accept Invitation" button → paste token → preview → confirm

**Org Settings — new section:**
- Org wallet address and DID (read-only)
- Public key fingerprint for verification
- Key rotation (Admin+ only)

---

## Phase Breakdown & Dependencies

### Phase 1 — Org Register Subscriptions

| Step | Depends on | Deliverable |
|------|-----------|-------------|
| 1a | — | System Register name fix (advertisement bug) |
| 1b | — | `OrganizationRegisterSubscription` model + EF migration |
| 1c | — | Org wallet fields on `Organization` model + EF migration (can combine with 1b) |
| 1d | 1c | Org wallet provisioning in bootstrap + org creation + reconciliation service |
| 1e | 1b | Subscription CRUD endpoints in Tenant Service |
| 1f | 1e | Auto-subscribe on register creation (API Gateway / UI orchestration) |
| 1g | 1e | Auto-subscribe System Admin org to System Register (bootstrap) |
| 1h | 1e | YARP gateway routes for subscription endpoints |
| 1i | 1e | UI: Registers page consolidated with subscription scoping |
| 1j | 1i | UI: New Submission filtered to subscribed registers |
| 1k | 1i | UI: Remove Available Registers from Peer Admin tab |

### Phase 2 — Private Register Invitations

| Step | Depends on | Deliverable |
|------|-----------|-------------|
| 2a | 1d | `did:sorcha:org:<address>` — DID identifier parser + resolver |
| 2b | 2a | "Join Private Register" blueprint definition + publish to System Register |
| 2c | 2a | Invitation creation endpoint (X25519 encrypt + ED25519 sign) |
| 2d | 2c | Invitation acceptance endpoint (decrypt + verify + subscribe) |
| 2e | 2d | On-ledger invitation record via blueprint instance |
| 2f | 2c | Invitation revocation endpoint + nonce registry (PostgreSQL + ledger) |
| 2g | 2c, 2d | UI: Invite Organisation dialog + token sharing |
| 2h | 2d | UI: Accept Invitation flow + invitations panel |
| 2i | 1d | UI: Org Settings wallet/DID display |
| 2j | 2e | Attack vector mitigations: rate limiting, max pending, genesis hash verification |

### Parallelisation

- Phase 1: Steps 1a, 1b, 1c can start simultaneously
- Phase 1: Steps 1e and 1d can run in parallel once prereqs met
- Phase 2: Steps 2a and 2b can start together once Phase 1 complete

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Org identity | HD wallet per org (BIP44, ED25519) | Consistent with participant wallets, enables signing |
| Org encryption | X25519 derived from ED25519 | Signing and encryption from one key pair, standard Curve25519 ECDH |
| Org DID | `did:sorcha:org:<walletAddress>` | Extends existing DID infrastructure, audit-distinguishable from wallet DIDs |
| Subscription storage | Tenant Service (PostgreSQL) | Org-scoped data belongs with org management |
| Subscription status | `Pending` → `Active` with async retry | Handles partial failures gracefully |
| Auto-subscribe orchestration | API Gateway / UI layer | Avoids new cross-service dependency (Register → Tenant) |
| Invitation delivery | Out-of-band (platform-agnostic) | No dependency on messaging infrastructure |
| Invitation audit | On-ledger via blueprint instance | Immutable, auditable, consistent with DAD model |
| Nonce replay protection | PostgreSQL (fast) + ledger (audit) | Hybrid: fast check for real-time, ledger for immutability |
| Register name caching | Denormalised on subscription record | Avoids cross-service call on every page load |
| No global org directory | Orgs published to domain-specific registers | Expandable — Companies House, Charity Commission, regional registers |
| Blueprint publishability | System Register default, any register allowed | Domain-specific workflows can extend the join process |
| Service communication | REST via ServiceClients (pragmatic exception) | Consistent with existing codebase patterns |
| Wallet API integration | Existing `CreateWalletRequest` with conventions | No contract changes — uses `owner` field and `Tags` |
