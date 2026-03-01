# Sorcha API Changes: Beta to Current

**Document Version**: 1.0
**Created**: 2025-11-23
**Author**: Sorcha Contributors
**Status**: Active

---

## Executive Summary

This document provides a comprehensive comparison between the **Sorcha Beta API** (as documented in the Postman collection) and the **Current API implementation** (as implemented in the Sorcha platform with .NET 10).

### Compatibility Overview

| Metric | Value |
|--------|-------|
| **Overall API Compatibility** | ~35% |
| **Beta Endpoints** | 75+ endpoints across 6 services |
| **Current Endpoints** | 80+ endpoints across 6 services |
| **Breaking Changes** | ~45% of beta endpoints |
| **New Features** | ~30% of current endpoints |
| **Removed Features** | ~20% of beta endpoints |

### Migration Impact

- **High Impact**: Blueprint and Wallet services have significant API changes
- **Medium Impact**: Actions API redesigned, some endpoints moved to Blueprint Service
- **Low Impact**: Health checks and basic CRUD operations mostly compatible

---

## Table of Contents

1. [Blueprint Service Changes](#1-blueprint-service-changes)
2. [Wallet Service Changes](#2-wallet-service-changes)
3. [Register Service Changes](#3-register-service-changes)
4. [Actions Service Changes](#4-actions-service-changes)
5. [Tenant Service Changes](#5-tenant-service-changes)
6. [Peer Service Changes](#6-peer-service-changes)
7. [Breaking Changes Summary](#7-breaking-changes-summary)
8. [Migration Guide](#8-migration-guide)
9. [Deprecated Features](#9-deprecated-features)
10. [New Features](#10-new-features)

---

## 1. Blueprint Service Changes

### 1.1 Base URL Changes

**Beta**: `/api/Blueprints` (PascalCase)
**Current**: `/api/blueprints` (lowercase)

⚠️ **Breaking Change**: URL paths are now lowercase for consistency with modern REST API conventions.

### 1.2 Endpoint Changes

#### Publish Blueprint

**Beta**:
```http
POST /api/Blueprints/{walletAddress}/{registerId}/publish
```

**Current**:
```http
POST /api/blueprints/{id}/publish
Body: { "registerId": "...", "walletAddress": "..." }
```

**Migration**: Move `walletAddress` and `registerId` from URL to request body.

#### Get Published Blueprints

**Beta**:
```http
GET /api/Blueprints/{registerId}/published
```

**Current**:
```http
GET /api/actions/{walletAddress}/{registerId}/blueprints
```

⚠️ **Breaking Change**: Endpoint moved to Actions API for better logical grouping.

#### Get Blueprint by ID

**Beta**:
```http
GET /api/Blueprints/{blueprintId}
```

**Current**:
```http
GET /api/blueprints/{id}
```

✅ **Compatible**: Lowercase URL, otherwise same functionality.

### 1.3 New Endpoints (Current Only)

| Endpoint | Description |
|----------|-------------|
| `POST /api/blueprints/` | Create blueprint |
| `PUT /api/blueprints/{id}` | Update blueprint |
| `DELETE /api/blueprints/{id}` | Delete blueprint (soft delete) |
| `GET /api/blueprints/{id}/versions` | Get all published versions |
| `GET /api/blueprints/{id}/versions/{version}` | Get specific version |
| `POST /api/templates/` | Create blueprint template |
| `POST /api/templates/evaluate` | Evaluate template with parameters |
| `POST /api/execution/validate` | Validate action data against schema |
| `POST /api/execution/calculate` | Apply JSON Logic calculations |
| `POST /api/execution/route` | Determine routing destinations |
| `POST /api/execution/disclose` | Apply disclosure rules |

**Impact**: Major new functionality for template-based blueprints and client-side execution helpers.

### 1.4 Removed Endpoints (Beta Only)

- `GET /api/Blueprints/{walletAddress}/{registerId}/published` - Use Actions API instead
- `GET /api/Blueprints/{blueprintId}/participants` - Participants now included in blueprint object

---

## 2. Wallet Service Changes

### 2.1 Base URL Changes

**Beta**: `/api/Wallets` (PascalCase)
**Current**: `/api/v1/wallets` (lowercase with versioning)

⚠️ **Breaking Change**: Added API versioning (`/v1/`) for future-proofing.

### 2.2 Major Architecture Changes

#### HD Wallet Support (New)

**Beta**: Only basic wallets with single addresses
**Current**: Full HD wallet support with BIP32/BIP39/BIP44 hierarchical derivation

**Impact**: Wallets can now derive unlimited addresses from a single mnemonic without server-side storage.

#### Client-Side Address Derivation (New)

**Beta**: All addresses generated server-side
**Current**: Addresses derived client-side using extended public keys (xpub)

**Migration Example**:
```javascript
// Beta (server-side generation)
POST /api/Wallets/{address}/newaddress

// Current (client-side derivation)
GET /api/v1/wallets/{address}/account/{accountIndex}
// Returns xpub for client-side derivation
// Client derives: m/44'/coin'/accountIndex'/0/addressIndex
```

### 2.3 Endpoint Changes

#### Create Wallet

**Beta**:
```http
POST /api/Wallets
Body: { "address": "...", "publicKey": "..." }
```

**Current**:
```http
POST /api/v1/wallets
Body: {
  "mnemonic": "word1 word2 ...",
  "walletName": "My Wallet",
  "algorithm": "ED25519"
}
```

⚠️ **Breaking Change**:
- Mnemonics are now primary wallet identifiers
- Server never stores mnemonics (user responsibility)
- Multi-algorithm support (ED25519, NISTP256, RSA4096)

#### Get Wallet

**Beta**:
```http
GET /api/Wallets/{address}
```

**Current**:
```http
GET /api/v1/wallets/{address}
```

✅ **Compatible**: Same functionality with versioned URL.

#### Sign Transaction

**Beta**:
```http
POST /api/Wallets/{address}/sign
Body: { "data": "..." }
```

**Current**:
```http
POST /api/v1/wallets/{address}/sign
Body: { "transaction": { /* TransactionModel */ } }
```

⚠️ **Breaking Change**: Now accepts full transaction model for comprehensive signing.

### 2.4 New Endpoints (Current Only)

| Endpoint | Description |
|----------|-------------|
| `GET /api/v1/wallets/{address}/mnemonic` | Get wallet mnemonic (requires PIN/password) |
| `GET /api/v1/wallets/{address}/account/{accountIndex}` | Get HD account info with xpub |
| `POST /api/v1/wallets/{address}/account` | Create new HD account |
| `GET /api/v1/wallets/{address}/addresses` | Get all derived addresses |
| `POST /api/v1/wallets/{address}/addresses` | Derive new address |
| `POST /api/v1/wallets/{address}/addresses/gap-limit` | Check gap limit compliance |
| `POST /api/v1/wallets/{address}/encrypt` | Encrypt payload for wallet |
| `POST /api/v1/wallets/{address}/decrypt` | Decrypt payload with wallet |
| `POST /api/v1/wallets/import` | Import wallet from mnemonic |

**Impact**: Significant new functionality for HD wallet management and cryptographic operations.

### 2.5 Removed Endpoints (Beta Only)

- `POST /api/Wallets/{address}/newaddress` - Use client-side derivation or `POST /addresses` endpoint
- `GET /api/Wallets/{address}/balance` - Balance tracking moved to separate analytics service
- `GET /api/Wallets/{address}/transactions` - Use Register Service query endpoints

---

## 3. Register Service Changes

### 3.1 Base URL Changes

**Beta**: `/api/Registers` (PascalCase)
**Current**: `/api/registers` (lowercase)

### 3.2 Endpoint Changes

#### Create Register

**Beta**:
```http
POST /api/Registers
Body: { "id": "...", "name": "..." }
```

**Current**:
```http
POST /api/registers/
Body: { "name": "...", "advertise": true, "isFullReplica": true }
```

✅ **Compatible**: ID now auto-generated, otherwise compatible.

#### Get Register

**Beta**:
```http
GET /api/Registers/{registerId}
```

**Current**:
```http
GET /api/registers/{id}
```

✅ **Compatible**: Same functionality.

#### Submit Transaction

**Beta**:
```http
POST /api/Registers/{registerId}/transactions
```

**Current**:
```http
POST /api/registers/{registerId}/transactions
```

✅ **Compatible**: Same functionality.

### 3.3 New Endpoints (Current Only)

| Endpoint | Description |
|----------|-------------|
| `GET /api/registers/` | Get all registers (paginated) |
| `PUT /api/registers/{id}` | Update register metadata |
| `DELETE /api/registers/{id}` | Delete register and all data |
| `GET /api/registers/stats/count` | Get total register count |
| `GET /api/registers/{registerId}/dockets` | Get all dockets (blocks) |
| `GET /api/registers/{registerId}/dockets/{docketId}` | Get docket by ID |
| `GET /api/query/wallets/{address}/transactions` | Get transactions by wallet |
| `GET /api/query/senders/{address}/transactions` | Get transactions sent by address |
| `GET /api/query/blueprints/{blueprintId}/transactions` | Get transactions by blueprint |
| `GET /api/query/stats` | Get transaction statistics |
| `GET /odata/Transactions` | OData V4 query endpoint |
| `GET /odata/Registers` | OData V4 register queries |
| `GET /odata/Dockets` | OData V4 docket queries |

**Impact**: Significant new functionality for advanced querying and analytics.

### 3.4 OData Support (New)

**Current Only**: Full OData V4 support for complex queries.

**Examples**:
```http
# Get first 10 transactions ordered by timestamp
GET /odata/Transactions?$top=10&$orderby=TimeStamp desc

# Filter transactions by sender
GET /odata/Transactions?$filter=SenderWallet eq '1A2B3C...'

# Count transactions
GET /odata/Transactions?$count=true
```

**Impact**: Powerful query capabilities not available in Beta.

---

## 4. Actions Service Changes

### 4.1 Integration Changes

**Beta**: Separate service at `/api/Actions`
**Current**: Integrated into Blueprint Service at `/api/actions`

⚠️ **Breaking Change**: Actions are now part of Blueprint Service for better cohesion.

### 4.2 Endpoint Changes

#### Submit Action

**Beta**:
```http
POST /api/Actions
Body: { "blueprintId": "...", "actionId": 1, "data": {...} }
```

**Current**:
```http
POST /api/actions/
Body: {
  "blueprintId": "...",
  "walletAddress": "...",
  "registerId": "...",
  "actionData": {...}
}
```

⚠️ **Breaking Change**: Requires explicit wallet and register identification.

#### Get Actions

**Beta**:
```http
GET /api/Actions/{walletAddress}
```

**Current**:
```http
GET /api/actions/{walletAddress}/{registerId}
```

⚠️ **Breaking Change**: Now requires registerId for multi-register support.

#### Get Action Details

**Beta**:
```http
GET /api/Actions/{actionId}
```

**Current**:
```http
GET /api/actions/{walletAddress}/{registerId}/{transactionId}
```

⚠️ **Breaking Change**: Action IDs replaced with transaction IDs from blockchain.

### 4.3 New Endpoints (Current Only)

| Endpoint | Description |
|----------|-------------|
| `GET /api/actions/{walletAddress}/{registerId}/blueprints` | Get available blueprints |
| `POST /api/actions/reject` | Reject a pending action |
| `GET /api/files/{walletAddress}/{registerId}/{txId}/{fileId}` | Download file attachment |

**Impact**: New file attachment support and action rejection workflow.

### 4.4 SignalR Hub Changes

**Beta**: `/hubs/actions`
**Current**: `/actionshub`

**Events Changed**:
- `TransactionSubmitted` → `TransactionConfirmed`
- `ActionCompleted` → Now includes transaction ID and blockchain confirmation

---

## 5. Tenant Service Changes

### 5.1 New Service (Not in Beta)

**Beta**: Basic authentication via external provider
**Current**: Full multi-tenant service with identity management

### 5.2 New Endpoints (Current Only)

| Endpoint | Description |
|----------|-------------|
| `POST /api/v1/organizations` | Create organization (tenant) |
| `GET /api/v1/organizations/{id}` | Get organization details |
| `POST /api/v1/users` | Create user identity |
| `GET /api/v1/users/{id}` | Get user details |
| `POST /api/v1/service-principals` | Create service principal |
| `POST /api/v1/auth/login` | Authenticate user (Entra ID integration) |
| `POST /api/v1/auth/token` | Get JWT access token |
| `POST /api/v1/auth/refresh` | Refresh access token |

**Impact**: Major new functionality for enterprise multi-tenancy.

---

## 6. Peer Service Changes

### 6.1 New Service (Not in Beta)

**Beta**: No P2P networking layer
**Current**: Full gRPC-based peer-to-peer network service

### 6.2 Architecture Change

**Beta**: Centralized transaction distribution
**Current**: Decentralized gossip protocol for transaction propagation

### 6.3 gRPC Endpoints (Current Only)

| Method | Description | Type |
|--------|-------------|------|
| `GetPeerList` | Retrieve list of known peers | Unary |
| `RegisterPeer` | Register node with network | Unary |
| `Ping` | Health check for peer | Unary |
| `DistributeTransaction` | Send single transaction | Unary |
| `StreamTransactions` | Stream multiple transactions | Bidirectional Stream |
| `AnnounceTransaction` | Announce transaction availability | Unary |

**Impact**: Enables decentralized network topology and improved scalability.

---

## 7. Breaking Changes Summary

### 7.1 Critical Breaking Changes

| Change | Impact | Migration Effort |
|--------|--------|------------------|
| URL casing (PascalCase → lowercase) | High | Low (simple find/replace) |
| Wallet API versioning (`/v1/`) | High | Low (URL prefix change) |
| HD wallet architecture | High | High (requires client-side derivation logic) |
| Actions API integration into Blueprint Service | High | Medium (endpoint changes) |
| Transaction ID-based action retrieval | High | Medium (ID mapping required) |
| Blueprint publish endpoint signature | Medium | Low (body parameter changes) |
| Mnemonic-based wallet creation | High | High (user backup workflow required) |

### 7.2 Minor Breaking Changes

| Change | Impact | Migration Effort |
|--------|--------|------------------|
| Register CRUD endpoints | Low | Low (backward compatible) |
| SignalR hub names | Medium | Low (client connection string update) |
| Response status codes (200 vs 202) | Low | Low (handle 202 Accepted) |
| Error response format | Low | Low (update error handling) |

---

## 8. Migration Guide

### 8.1 Immediate Actions Required

1. **Update all API URLs** to lowercase and add `/v1/` versioning for Wallet Service
2. **Implement HD wallet client-side derivation** using BIP44 libraries
3. **Update Actions API calls** to use Blueprint Service endpoints
4. **Migrate to transaction-based action retrieval** from action IDs
5. **Implement mnemonic backup workflow** for user wallets

### 8.2 Wallet Service Migration

**Step 1**: Update wallet creation to use mnemonics

```javascript
// Beta
const wallet = await fetch('/api/Wallets', {
  method: 'POST',
  body: JSON.stringify({ address: '...', publicKey: '...' })
});

// Current
const wallet = await fetch('/api/v1/wallets', {
  method: 'POST',
  body: JSON.stringify({
    mnemonic: generateMnemonic(), // User-generated 12-24 words
    walletName: 'My Wallet',
    algorithm: 'ED25519'
  })
});

// CRITICAL: User must backup mnemonic immediately!
alert('BACKUP YOUR MNEMONIC: ' + wallet.mnemonic);
```

**Step 2**: Implement client-side address derivation

```javascript
// Beta (server-side)
const newAddress = await fetch(`/api/Wallets/${walletAddress}/newaddress`, {
  method: 'POST'
});

// Current (client-side)
// 1. Get extended public key (xpub)
const account = await fetch(`/api/v1/wallets/${walletAddress}/account/0`);
const xpub = account.extendedPublicKey;

// 2. Derive address client-side using BIP44 library
import * as bip32 from 'bip32';
const node = bip32.fromBase58(xpub);
const addressNode = node.derive(0).derive(addressIndex); // External chain, index
const address = addressNode.publicKey.toString('hex');

// 3. Optional: Register derived address with server for tracking
await fetch(`/api/v1/wallets/${walletAddress}/addresses`, {
  method: 'POST',
  body: JSON.stringify({ address, derivationPath: `m/44'/0'/0'/0/${addressIndex}` })
});
```

### 8.3 Actions API Migration

**Update endpoint URLs**:

```javascript
// Beta
POST /api/Actions
GET /api/Actions/{walletAddress}
GET /api/Actions/{actionId}

// Current
POST /api/actions/
GET /api/actions/{walletAddress}/{registerId}
GET /api/actions/{walletAddress}/{registerId}/{transactionId}
```

**Update action submission**:

```javascript
// Beta
await fetch('/api/Actions', {
  method: 'POST',
  body: JSON.stringify({
    blueprintId: '...',
    actionId: 1,
    data: { /* action data */ }
  })
});

// Current
await fetch('/api/actions/', {
  method: 'POST',
  body: JSON.stringify({
    blueprintId: '...',
    walletAddress: '...',
    registerId: '...',
    actionData: { /* action data */ }
  })
});
```

### 8.4 Blueprint Publishing Migration

```javascript
// Beta
POST /api/Blueprints/{walletAddress}/{registerId}/publish

// Current
POST /api/blueprints/{blueprintId}/publish
Body: {
  "registerId": "...",
  "walletAddress": "...",
  "blueprint": { /* blueprint object */ }
}
```

### 8.5 SignalR Hub Migration

```javascript
// Beta
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/actions')
  .build();

connection.on('TransactionSubmitted', (actionId) => { /* ... */ });

// Current
const connection = new signalR.HubConnectionBuilder()
  .withUrl('/actionshub')
  .build();

connection.on('TransactionConfirmed', (transactionId, status) => { /* ... */ });
```

---

## 9. Deprecated Features

### 9.1 Removed in Current

| Feature | Reason | Alternative |
|---------|--------|-------------|
| Server-side address generation | Security (server shouldn't generate private keys) | Client-side HD wallet derivation |
| Action IDs | Replaced with blockchain transaction IDs | Use transaction IDs from Register Service |
| Wallet balance tracking | Moved to analytics service | Use Register Service transaction queries |
| Wallet transaction history endpoint | Redundant with Register queries | `/api/query/wallets/{address}/transactions` |
| Blueprint participants endpoint | Included in blueprint object | Get full blueprint via `/api/blueprints/{id}` |

### 9.2 Deprecated but Still Supported

| Feature | Status | Migration Deadline | Alternative |
|---------|--------|-------------------|-------------|
| PascalCase URLs | Deprecated | 2026-01-01 | Use lowercase URLs |
| Non-versioned Wallet API | Deprecated | 2026-03-01 | Use `/api/v1/wallets` |

---

## 10. New Features

### 10.1 HD Wallet Management

- **BIP32/BIP39/BIP44** hierarchical deterministic wallets
- **Client-side derivation** with extended public keys (xpub)
- **Gap limit checking** for address reuse prevention
- **Multi-algorithm support** (ED25519, NISTP256, RSA4096)

### 10.2 Blueprint Templates

- **JSON-e template engine** for dynamic blueprint generation
- **Parameter substitution** at runtime
- **Template library** for common workflow patterns

### 10.3 Execution Helpers

- **Client-side validation** against JSON schemas
- **JSON Logic calculations** for business rules
- **Routing determination** for multi-party workflows
- **Disclosure rules** for selective data sharing

### 10.4 OData Query Support

- **Advanced filtering** with `$filter`, `$select`, `$orderby`
- **Pagination** with `$top`, `$skip`, `$count`
- **Complex queries** for transaction analytics

### 10.5 Multi-Tenancy

- **Organization management** for enterprise deployments
- **User identity management** with Entra ID integration
- **Service principal authentication** for inter-service security

### 10.6 P2P Networking

- **Decentralized gossip protocol** for transaction distribution
- **NAT traversal** with STUN support
- **Offline queue** for resilient transaction submission

### 10.7 Secure Validation

- **Validator Service** for consensus and blockchain validation
- **Enclave support** for Intel SGX and AMD SEV
- **Docket (block) sealing** with cryptographic integrity

---

## 11. API Compatibility Matrix

| Service | Endpoint Category | Compatibility | Breaking Changes | New Features |
|---------|-------------------|---------------|------------------|--------------|
| Blueprint Service | CRUD Operations | 60% | URL casing, publish signature | Templates, execution helpers |
| Blueprint Service | Publishing | 40% | Endpoint signature | Versioning support |
| Blueprint Service | Templates | 0% (New) | N/A | Complete new feature |
| Wallet Service | Wallet Management | 30% | HD wallets, mnemonics | Client-side derivation |
| Wallet Service | Signing | 70% | Transaction model format | Multi-algorithm support |
| Wallet Service | Address Management | 0% (Redesigned) | Server-side generation removed | HD account management |
| Register Service | CRUD Operations | 80% | URL casing | Pagination, statistics |
| Register Service | Transactions | 90% | Minor query changes | Transaction confirmation |
| Register Service | Queries | 20% (New) | N/A | OData V4 support |
| Actions Service | Action Submission | 50% | Integration into Blueprint | File attachments |
| Actions Service | Action Retrieval | 30% | Transaction-based IDs | Register-scoped queries |
| Tenant Service | All | 0% (New) | N/A | Complete new service |
| Peer Service | All | 0% (New) | N/A | Complete new service |

**Legend**:
- **100%**: Fully compatible
- **80-99%**: Minor breaking changes
- **50-79%**: Moderate breaking changes
- **0-49%**: Major breaking changes or new features

---

## 12. Recommendations

### 12.1 For Existing Beta Users

1. **Plan Migration Window**: Allow 2-4 weeks for API migration and testing
2. **Implement HD Wallet Support**: Priority 1 - Critical for current platform
3. **Update All API Endpoints**: Use find/replace for URL casing changes
4. **Test Thoroughly**: Especially wallet creation and action submission flows
5. **Backup User Mnemonics**: Implement secure backup workflow immediately

### 12.2 For New Implementers

1. **Start with Current API**: Do not use Beta documentation
2. **Use HD Wallets**: Client-side derivation is the supported pattern
3. **Leverage Templates**: Use blueprint templates for common workflows
4. **Implement OData Queries**: For advanced analytics and reporting
5. **Use Multi-Tenancy**: Organization structure for enterprise deployments

### 12.3 For Sorcha Contributors

1. **Maintain API Versioning**: `/v1/`, `/v2/` for future compatibility
2. **Provide Migration Tools**: Scripts to convert Beta clients to Current
3. **Document Breaking Changes**: Clear changelog for each release
4. **Support Deprecated Endpoints**: Maintain for 12 months with warnings
5. **Create Migration Examples**: Sample code for all breaking changes

---

## 13. Contact and Support

**Questions about API migration?**
- GitHub Issues: https://github.com/sorcha/sorcha/issues
- Documentation: https://docs.sorcha.io
- Community Forum: https://community.sorcha.io

**Reporting API bugs:**
- Use GitHub issue template: "API Bug Report"
- Include API version, endpoint, request/response examples

---

## 14. Appendix

### 14.1 API Version History

| Version | Release Date | Major Changes |
|---------|--------------|---------------|
| Beta | 2022-02-07 | Initial beta release |
| v1.0 | 2025-11-23 | Production release with breaking changes |

### 14.2 Full Endpoint Mapping

#### Blueprint Service

| Beta Endpoint | Current Endpoint | Status |
|---------------|------------------|--------|
| `GET /api/Blueprints/{id}` | `GET /api/blueprints/{id}` | ✅ Compatible |
| `POST /api/Blueprints/{wallet}/{register}/publish` | `POST /api/blueprints/{id}/publish` | ⚠️ Breaking |
| `GET /api/Blueprints/{register}/published` | `GET /api/actions/{wallet}/{register}/blueprints` | ⚠️ Moved |
| N/A | `POST /api/blueprints/` | ✨ New |
| N/A | `PUT /api/blueprints/{id}` | ✨ New |
| N/A | `DELETE /api/blueprints/{id}` | ✨ New |
| N/A | `GET /api/blueprints/{id}/versions` | ✨ New |
| N/A | `POST /api/templates/` | ✨ New |
| N/A | `POST /api/execution/validate` | ✨ New |

#### Wallet Service

| Beta Endpoint | Current Endpoint | Status |
|---------------|------------------|--------|
| `POST /api/Wallets` | `POST /api/v1/wallets` | ⚠️ Breaking |
| `GET /api/Wallets/{address}` | `GET /api/v1/wallets/{address}` | ✅ Compatible |
| `POST /api/Wallets/{address}/sign` | `POST /api/v1/wallets/{address}/sign` | ⚠️ Breaking |
| `POST /api/Wallets/{address}/newaddress` | ❌ Removed (use client-side derivation) | ❌ Removed |
| N/A | `GET /api/v1/wallets/{address}/mnemonic` | ✨ New |
| N/A | `GET /api/v1/wallets/{address}/account/{accountIndex}` | ✨ New |
| N/A | `POST /api/v1/wallets/{address}/addresses` | ✨ New |
| N/A | `POST /api/v1/wallets/{address}/encrypt` | ✨ New |
| N/A | `POST /api/v1/wallets/{address}/decrypt` | ✨ New |

#### Register Service

| Beta Endpoint | Current Endpoint | Status |
|---------------|------------------|--------|
| `POST /api/Registers` | `POST /api/registers/` | ✅ Compatible |
| `GET /api/Registers/{id}` | `GET /api/registers/{id}` | ✅ Compatible |
| `POST /api/Registers/{id}/transactions` | `POST /api/registers/{id}/transactions` | ✅ Compatible |
| N/A | `GET /api/registers/` | ✨ New |
| N/A | `PUT /api/registers/{id}` | ✨ New |
| N/A | `DELETE /api/registers/{id}` | ✨ New |
| N/A | `GET /api/registers/{id}/dockets` | ✨ New |
| N/A | `GET /api/query/wallets/{address}/transactions` | ✨ New |
| N/A | `GET /odata/Transactions` | ✨ New |

---

**Document End**
**Last Updated**: 2025-11-23
**Version**: 1.0
**Status**: Active
