# Wallet Functionality Verification

**Purpose:** Verify Wallet Service functionality including wallet creation, data signing, and integration with Register Service for register creation with real cryptographic signatures.

**Date Created:** 2026-01-05
**Status:** ✅ Active
**Prerequisites:** Docker-Compose running, Bootstrap completed

---

## Overview

This walkthrough verifies core wallet functionality:
1. **Wallet Creation** - Create HD wallets with ED25519, NISTP256, or RSA4096
2. **Data Signing** - Sign arbitrary data with wallet private keys
3. **Signature Verification** - Verify signatures match expected format
4. **Register Integration** - Use wallet signing for register creation (end-to-end)

---

## Quick Start

### Prerequisites

```bash
# Ensure all services are running
docker-compose ps

# Expected: All services "Up"
```

### Test 1: Basic Wallet Functions

Tests wallet creation, listing, and basic operations:

```powershell
# From repository root
pwsh walkthroughs/WalletVerification/test-wallet-functions.ps1

# With specific algorithm
pwsh walkthroughs/WalletVerification/test-wallet-functions.ps1 -Algorithm NISTP256
```

**What this tests:**
- Admin authentication
- List existing wallets
- Create new wallet (ED25519/NISTP256/RSA4096)
- Sign arbitrary data (if endpoint available)
- Verify signature (if endpoint available)
- Confirm wallet appears in list

### Test 2: End-to-End Register Creation with Real Signing ⭐

Tests complete register creation workflow with real wallet signatures:

```powershell
# From repository root
pwsh walkthroughs/WalletVerification/test-register-with-wallet-signing.ps1

# With specific algorithm
pwsh walkthroughs/WalletVerification/test-register-with-wallet-signing.ps1 -Algorithm ED25519
```

**What this tests:**
1. Admin authentication via Tenant Service
2. Wallet creation via Wallet Service
3. Register creation initiation (get control record hash)
4. Sign control record hash with wallet private key
5. Finalize register creation with real signature
6. Verify signature validation by Register Service
7. Confirm genesis transaction submission to Validator

**Expected Flow:**
```
Admin Auth → Create Wallet → Initiate Register → Sign Data → Finalize Register → Genesis TX
```

---

## Test Scripts

### `test-wallet-functions.ps1`

**Basic wallet operations test**

Parameters:
- `-AdminEmail` - Admin user email (default: admin@sorcha.local)
- `-AdminPassword` - Admin password (default: Dev_Pass_2025!)
- `-ApiGatewayUrl` - API Gateway URL (default: http://localhost)
- `-Algorithm` - Crypto algorithm: ED25519, NISTP256, RSA4096 (default: ED25519)

Example:
```powershell
pwsh test-wallet-functions.ps1 `
  -AdminEmail "admin@sorcha.local" `
  -AdminPassword "Dev_Pass_2025!" `
  -Algorithm "ED25519"
```

### `test-register-with-wallet-signing.ps1` ⭐

**End-to-end register creation with real signing**

Parameters:
- `-AdminEmail` - Admin user email (default: admin@sorcha.local)
- `-AdminPassword` - Admin password (default: Dev_Pass_2025!)
- `-ApiGatewayUrl` - API Gateway URL (default: http://localhost)
- `-Algorithm` - Crypto algorithm: ED25519, NISTP256, RSA4096 (default: ED25519)

Example:
```powershell
pwsh test-register-with-wallet-signing.ps1 `
  -AdminEmail "admin@sorcha.local" `
  -AdminPassword "Dev_Pass_2025!" `
  -Algorithm "NISTP256"
```

---

## Expected Results

### Test 1: Basic Wallet Functions

```
================================================================================
  Wallet Functionality Verification
================================================================================

Configuration:
  API Gateway: http://localhost
  Admin User: admin@sorcha.local
  Wallet Algorithm: ED25519

================================================================================
  Step 1: Admin Authentication
================================================================================
[OK] Admin authenticated successfully
[i] Token expires in: 3600 seconds

================================================================================
  Step 2: Check Existing Wallets
================================================================================
[OK] Successfully retrieved wallet list
[i] Admin user has 2 wallet(s)

Existing Wallets:
  - Primary Wallet: SORCHA1a2b3c... (ED25519)
  - Test Wallet: SORCHA4d5e6f... (NISTP256)

================================================================================
  Step 3: Create New Wallet
================================================================================
[OK] Wallet created successfully!

Wallet Details:
  Name: Test Wallet 2026-01-05 12:34:56
  Address: SORCHA7g8h9i...
  Algorithm: ED25519
  Public Key: ED25519:1a2b3c4d5e6f...

================================================
  MNEMONIC PHRASE (SAVE SECURELY!)
================================================
abandon ability able about above absent absorb
abstract absurd abuse access accident
================================================

================================================================================
  Test Summary
================================================================================

Wallet Functionality Tests:
  [OK] Admin authentication
  [OK] List existing wallets
  [OK] Create new wallet (ED25519)
  [SKIP] Sign data with wallet (endpoint not available)
  [SKIP] Verify signature (sign failed)
  [OK] Verify wallet in list

================================================================================
  Wallet Functionality Verification: COMPLETE
================================================================================
```

### Test 2: Register Creation with Real Signing ⭐

```
================================================================================
  Register Creation with Real Wallet Signing
================================================================================

This test demonstrates the complete register creation workflow:
  1. Admin authentication
  2. Wallet creation with specified algorithm
  3. Register creation initiation (get data to sign)
  4. Sign register data with wallet
  5. Finalize register creation with real signature
  6. Verify register was created

Configuration:
  API Gateway: http://localhost
  Admin User: admin@sorcha.local
  Wallet Algorithm: ED25519

================================================================================
  Step 1: Admin Authentication
================================================================================
[OK] Admin authenticated successfully
[i] Token expires in: 3600 seconds

================================================================================
  Step 2: Create Wallet for Register Owner
================================================================================
[OK] Wallet created successfully!

Wallet Details:
  Name: Register Owner Wallet 2026-01-05-123456
  Address: SORCHA1a2b3c4d...
  Algorithm: ED25519
  Public Key: ED25519:3b6a27bcceb6a42d...

================================================================================
  Step 3: Initiate Register Creation
================================================================================
[OK] Register initiation successful!

Initiation Response:
  Register ID: 24c6b118d09b4f138be495df1d41e057
  Data to Sign (Hash): 30d82d6fca3329229cd8cadd900eae24...
  Nonce: HHqWxWW1IPm1aEFnND3bfyVY91vsbYj0...
  Expires At: 2026-01-05T12:40:31+00:00

================================================================================
  Step 4: Sign Register Data with Wallet
================================================================================
[OK] Data signed successfully!

Signature Details:
  Algorithm: ED25519
  Signature: ED25519:1234567890abcdef1234567890abcdef...
  Public Key: ED25519:3b6a27bcceb6a42d...

================================================================================
  Step 5: Finalize Register Creation
================================================================================
[OK] Register creation finalized successfully!

Finalization Response:
  Register ID: 24c6b118d09b4f138be495df1d41e057
  Status: Active
  Genesis Transaction ID: tx-genesis-001

================================================================================
  Step 6: Verify Genesis Transaction
================================================================================
[OK] Genesis transaction found in validator!

Transaction Details:
  Transaction ID: tx-genesis-001
  Type: Genesis
  Register ID: 24c6b118d09b4f138be495df1d41e057
  Status: Pending

================================================================================
  End-to-End Test Summary
================================================================================

Test Results:
  [OK] Admin authentication
  [OK] Wallet creation (ED25519)
  [OK] Register creation initiation
  [OK] Data signing with wallet
  [OK] Register creation finalization
  [OK] Genesis transaction submitted

Register Created Successfully!
  Name: Test Register with ED25519 Signing
  Register ID: 24c6b118d09b4f138be495df1d41e057
  Owner Wallet: SORCHA1a2b3c4d...
  Algorithm: ED25519

================================================================================
  COMPLETE SUCCESS - Real Wallet Signing Verified!
================================================================================

What was tested:
  - Wallet Service: Create wallet with ED25519
  - Wallet Service: Sign data with private key
  - Register Service: Initiate register creation
  - Register Service: Verify signature
  - Register Service: Create register with verified signature
  - Validator Service: Submit genesis transaction
```

---

## Architecture Validated

### Request Flow

```
┌──────────────────┐
│  Test Script     │
└────────┬─────────┘
         │
         │ 1. POST /api/service-auth/token (login)
         ▼
┌─────────────────────────────────────────┐
│     API Gateway (YARP)                  │
│  Routes to: Tenant Service              │
└────────┬────────────────────────────────┘
         │
         │ 2. POST /api/v1/wallets (create)
         ▼
┌─────────────────────────────────────────┐
│     API Gateway (YARP)                  │
│  Routes to: Wallet Service              │
└────────┬────────────────────────────────┘
         │
         │ 3. POST /api/registers/initiate
         ▼
┌─────────────────────────────────────────┐
│     API Gateway (YARP)                  │
│  Routes to: Register Service            │
│  Returns: dataToSign hash               │
└────────┬────────────────────────────────┘
         │
         │ 4. POST /api/v1/wallets/{addr}/sign
         ▼
┌─────────────────────────────────────────┐
│     Wallet Service                      │
│  - Load private key from wallet         │
│  - Sign data with ED25519/NISTP256/RSA  │
│  - Return signature                     │
└────────┬────────────────────────────────┘
         │
         │ 5. POST /api/registers/finalize (with signature)
         ▼
┌─────────────────────────────────────────┐
│     Register Service                    │
│  - Verify signature with public key     │
│  - Create register in database          │
│  - Sign with system wallet              │
│  - Submit genesis transaction           │
└────────┬────────────────────────────────┘
         │
         │ 6. POST /api/v1/transactions/validate
         ▼
┌─────────────────────────────────────────┐
│     Validator Service                   │
│  - Validate structure + signatures      │
│  - Store transaction in mempool         │
└─────────────────────────────────────────┘
```

---

## Supported Algorithms

| Algorithm | Key Size | Signature Size | Speed | Security | Recommended For |
|-----------|----------|----------------|-------|----------|-----------------|
| **ED25519** | 256 bits | 64 bytes | Fast | High | ✅ Default (recommended) |
| **NISTP256** | 256 bits | 64 bytes | Medium | High | NIST compliance |
| **RSA4096** | 4096 bits | 512 bytes | Slow | Very High | Maximum security |

**Test all algorithms:**
```powershell
# ED25519
pwsh test-register-with-wallet-signing.ps1 -Algorithm ED25519

# NISTP256
pwsh test-register-with-wallet-signing.ps1 -Algorithm NISTP256

# RSA4096
pwsh test-register-with-wallet-signing.ps1 -Algorithm RSA4096
```

---

## Troubleshooting

### Admin authentication fails with 401

**Problem:** Invalid credentials or JWT configuration issue

**Solution:**
```powershell
# Verify bootstrap completed successfully
docker logs sorcha-tenant-service --tail 50 | Select-String "bootstrap"

# Check admin user exists
curl http://localhost:5110/api/organizations/by-subdomain/sorcha-local

# Verify JWT_SIGNING_KEY in .env matches docker-compose.yml
```

### Wallet creation fails with 401

**Problem:** JWT token expired or invalid

**Solution:**
- Tokens expire after 60 minutes (default)
- Re-run the test script to get fresh token
- Check Tenant Service logs for JWT validation errors

### Sign endpoint returns 404

**Problem:** Wallet Service sign endpoint not implemented or not exposed

**Solution:**
```powershell
# Check Wallet Service API routes
curl http://localhost/api/v1/wallets | python -m json.tool

# Check API Gateway routing
grep -A5 "wallets" src/Services/Sorcha.ApiGateway/appsettings.json

# Check Wallet Service logs
docker logs sorcha-wallet-service --tail 50
```

### Register finalize fails with 500

**Problem:** Signature verification failed or register service error

**Solution:**
```powershell
# Check Register Service logs for signature verification errors
docker logs sorcha-register-service --tail 50 | Select-String "signature\|verify"

# Common causes:
# - Public key format mismatch
# - Signature algorithm mismatch
# - Nonce expired (5-minute TTL)
# - Pending registration not found
```

### Genesis transaction not found

**Problem:** Transaction submission failed or asynchronous processing

**Solution:**
- This is often expected behavior (async processing)
- Check Validator Service logs:
```powershell
docker logs sorcha-validator-service --tail 50
```

---

## Service APIs Used

### Tenant Service

**Admin Login:**
```http
POST /api/service-auth/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&username={email}&password={password}&client_id=sorcha-cli
```

### Wallet Service

**Create Wallet:**
```http
POST /api/v1/wallets
Authorization: Bearer {token}
Content-Type: application/json

{
  "name": "My Wallet",
  "algorithm": "ED25519",
  "wordCount": 12
}
```

**Sign Data:**
```http
POST /api/v1/wallets/{address}/sign
Authorization: Bearer {token}
Content-Type: application/json

{
  "data": "hexadecimal-hash-to-sign"
}
```

**List Wallets:**
```http
GET /api/v1/wallets
Authorization: Bearer {token}
```

### Register Service

**Initiate Register Creation:**
```http
POST /api/registers/initiate
Content-Type: application/json

{
  "name": "My Register",
  "description": "Test register",
  "tenantId": "test-tenant-001",
  "ownerDid": "did:sorcha:user",
  "ownerPublicKey": "ED25519:hexkey"
}
```

**Finalize Register Creation:**
```http
POST /api/registers/finalize
Content-Type: application/json

{
  "registerId": "uuid",
  "nonce": "base64-nonce",
  "signedData": {
    "dataToSign": "hex-hash",
    "signature": "ED25519:hex-signature",
    "publicKey": "ED25519:hexkey"
  }
}
```

---

## Next Steps

After completing this walkthrough:

1. **Test All Algorithms** - Run with ED25519, NISTP256, RSA4096
2. **Wallet Recovery** - Test recovering wallet from mnemonic phrase
3. **Multi-Signature** - Test registers with multiple owner attestations
4. **Transaction Chain** - Add transactions to register after creation
5. **Performance Testing** - Benchmark signing performance across algorithms

---

## Related Documentation

- [Register Creation Walkthrough](../RegisterCreationFlow/) - Register creation flow
- [Wallet Service Specification](../../.specify/specs/sorcha-wallet-service.md)
- [Register Service Specification](../../.specify/specs/sorcha-register-service.md)
- [Cryptography Specification](../../.specify/specs/sorcha-cryptography-rewrite.md)
- [Docker Development Workflow](../../docs/getting-started/DOCKER-DEVELOPMENT-WORKFLOW.md)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2026-01-05 | Initial wallet verification walkthrough |

---

**Ready to verify wallet functionality?** Start with the [Quick Start](#quick-start) guide!
