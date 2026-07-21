# Register Creation Flow Walkthrough

## Overview

This walkthrough demonstrates the complete two-phase register creation workflow with cryptographic attestations and genesis transaction processing.

**Recommended Method:** Use the Sorcha CLI (`sorcha register create`) which handles the entire two-phase flow internally.

## Quick Start (CLI)

```powershell
# Prerequisites: Docker services running, CLI built
docker-compose up -d
dotnet build src/Apps/Sorcha.Cli

# Run the CLI-based walkthrough (recommended)
pwsh walkthroughs/RegisterCreationFlow/test-register-creation-cli.ps1

# With different cryptographic algorithm
pwsh walkthroughs/RegisterCreationFlow/test-register-creation-cli.ps1 -Algorithm NISTP256

# Show full JSON output
pwsh walkthroughs/RegisterCreationFlow/test-register-creation-cli.ps1 -ShowJson
```

## CLI Commands Reference

### Register Management

```bash
# Create a register (handles two-phase flow internally)
sorcha register create \
  --name "My Register" \
  --tenant-id "tenant-001" \
  --owner-wallet "wallet-address" \
  --description "Optional description"

# List all registers
sorcha register list

# Get register details
sorcha register get --id <register-id>

# Update register metadata
sorcha register update --id <register-id> --name "New Name" --status Online

# Delete a register
sorcha register delete --id <register-id>
```

### Docket Inspection

```bash
# List dockets (sealed blocks) in a register
sorcha docket list --register-id <register-id>

# Get specific docket details
sorcha docket get --register-id <register-id> --docket-id 0

# List transactions in a docket
sorcha docket transactions --register-id <register-id> --docket-id 0
```

### Transaction Queries

```bash
# Query by wallet address
sorcha query wallet --address <address>

# Query by sender address
sorcha query sender --address <address>

# Query by blueprint ID
sorcha query blueprint --id <blueprint-id>

# Get query statistics
sorcha query stats

# OData queries
sorcha query odata --resource transactions --filter "status eq 'confirmed'"
```

## Two-Phase Register Creation Flow

The CLI handles this flow internally, but understanding it helps with debugging:

### Phase 1: Initiate

```
POST /api/registers/initiate
```

- Accepts `owners` array (userId, walletId)
- Computes SHA-256 hash of canonical JSON attestation data
- Returns registerId, nonce, and `attestationsToSign` with hex-encoded hashes
- Hash bytes stored in pending state (5-minute expiration)

### Phase 2: Sign (Client-side)

- CLI converts hex hash to bytes
- Base64-encodes for Wallet Service API
- Signs with `isPreHashed=true` (prevents double-hashing)
- Wallet Service returns signature + derived public key

### Phase 3: Finalize

```
POST /api/registers/finalize
```

- Validates nonce (replay protection)
- Verifies signatures against stored hash bytes (no re-serialization)
- Submits genesis transaction to Validator Service (atomic)
- Creates register in database only if genesis succeeds

## Architecture Flow

```
┌─────────────────┐
│   Sorcha CLI    │  sorcha register create --name "..." --owner-wallet "..."
└────────┬────────┘
         │
         │ Phase 1: POST /api/registers/initiate
         ▼
┌─────────────────────────────────────────┐
│     Register Service                    │
│  ┌────────────────────────────────┐    │
│  │ RegisterCreationOrchestrator   │    │
│  │  - Generate registerId         │    │
│  │  - Create attestation data     │    │
│  │  - Compute SHA-256 hash        │    │
│  │  - Store hash bytes + pending  │    │
│  └────────────────────────────────┘    │
└────────┬────────────────────────────────┘
         │
         │ Returns: registerId, nonce, attestationsToSign (hex hashes)
         ▼
┌─────────────────┐
│   Sorcha CLI    │
│  [Sign hash]    │  Phase 2: Wallet signs with isPreHashed=true
└────────┬────────┘
         │
         │ Phase 3: POST /api/registers/finalize (with signatures)
         ▼
┌─────────────────────────────────────────┐
│     Register Service                    │
│  ┌────────────────────────────────┐    │
│  │ RegisterCreationOrchestrator   │    │
│  │  - Verify nonce                │    │
│  │  - Verify sigs (stored hashes) │    │
│  │  - Sign with system wallet     │    │
│  │  - Submit genesis (atomic)     │    │
│  │  - Create register in DB       │    │
│  └────────┬───────────────────────┘    │
└───────────┼────────────────────────────┘
            │
            │ POST /api/v1/transactions/validate
            ▼
┌─────────────────────────────────────────┐
│    Validator Service                    │
│  ┌────────────────────────────────┐    │
│  │ Generic Validation Endpoint    │    │
│  │  - Validate structure          │    │
│  │  - Verify signatures           │    │
│  │  - Store in mempool            │    │
│  └────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

## Prerequisites

**Docker-Compose (Recommended):**
```bash
# Start all services
docker-compose up -d

# Verify services are running
docker-compose ps

# Build CLI
dotnet build src/Apps/Sorcha.Cli
```

**OR .NET Aspire AppHost (Debugging):**
```bash
# Start all services with Aspire
dotnet run --project src/Apps/Sorcha.AppHost
```

## Walkthrough Scripts

| Script | Purpose | Usage |
|--------|---------|-------|
| `test-register-creation-cli.ps1` | **Recommended** - Full CLI workflow | `pwsh test-register-creation-cli.ps1` |
| `test-register-creation-rest.ps1` | REST API workflow (debugging) | `pwsh test-register-creation-rest.ps1` |
| `test-register-creation-with-real-signing.ps1` | Advanced real crypto testing | `pwsh test-register-creation-with-real-signing.ps1` |
| `test-register-creation-docker.ps1` | Docker internal network testing | `pwsh test-register-creation-docker.ps1` |

### CLI Walkthrough Options

```powershell
# Basic usage (ED25519, docker profile)
pwsh test-register-creation-cli.ps1

# With NIST P-256 algorithm
pwsh test-register-creation-cli.ps1 -Algorithm NISTP256

# With RSA-4096 algorithm
pwsh test-register-creation-cli.ps1 -Algorithm RSA4096

# Skip authentication (use existing session)
pwsh test-register-creation-cli.ps1 -SkipAuth

# Show full JSON output
pwsh test-register-creation-cli.ps1 -ShowJson

# Auto-cleanup created resources
pwsh test-register-creation-cli.ps1 -Cleanup
```

### REST API Walkthrough (Advanced)

For debugging or understanding the raw API flow:

```powershell
# Via API Gateway (recommended)
pwsh test-register-creation-rest.ps1 -Profile gateway

# Direct to services (debugging)
pwsh test-register-creation-rest.ps1 -Profile direct
```

## Expected Results

**CLI Walkthrough:**
1. CLI installation verified
2. Authentication successful
3. Wallet created (with mnemonic - save securely!)
4. Register created via two-phase flow
5. Genesis transaction submitted to Validator
6. Register and dockets verified

**REST API Walkthrough:**
1. Services accessible (via gateway or direct)
2. Initiate returns registerId, nonce, attestationsToSign
3. Hash-based `dataToSign` (64-char hex SHA-256)
4. Finalize accepts signed attestations
5. Register created in database
6. Genesis transaction in mempool with HIGH priority

## Key Components

### RegisterControlRecord Structure

```json
{
  "registerId": "abc123...",
  "name": "My Test Register",
  "description": "Testing register creation",
  "tenantId": "tenant-001",
  "createdAt": "2025-01-04T...",
  "metadata": { ... },
  "attestations": [
    {
      "role": "Owner",
      "subject": "did:sorcha:user-001",
      "publicKey": "[base64]",
      "signature": "[base64]",
      "algorithm": "ED25519",
      "grantedAt": "2025-01-04T..."
    }
  ]
}
```

### Genesis Transaction Metadata

- `blueprintId`: "genesis" (special marker)
- `actionId`: "register-creation"
- `priority`: HIGH
- `expiresAt`: null (never expires)
- `metadata.Type`: "Genesis"
- `metadata.RegisterName`: register name
- `metadata.TenantId`: tenant ID

## Security Features

1. **Nonce-based Replay Protection**: Each initiate generates unique nonce
2. **Signature Verification**: All attestations verified against stored hash bytes
3. **Expiration**: Pending registrations expire after 5 minutes
4. **Hash-based Signing**: SHA-256 hash stored at initiate, eliminates re-serialization issues
5. **Pre-hashed Signing**: `isPreHashed=true` prevents double-hashing
6. **Atomic Creation**: Genesis transaction submitted before register persist
7. **System Wallet Signing**: Genesis transactions signed by `ISystemWalletSigningService` with whitelist and rate limiting

## Troubleshooting

### CLI Issues

**"Sorcha CLI not found"**
```bash
# Build the CLI
dotnet build src/Apps/Sorcha.Cli

# Add to PATH or run from bin directory
./src/Apps/Sorcha.Cli/bin/Debug/net10.0/sorcha --version
```

**"Not authenticated"**
```bash
# Login with interactive mode
sorcha auth login --interactive

# Or with service credentials
sorcha auth login --client-id <id> --client-secret <secret>
```

**"Wallet not found"**
```bash
# List your wallets
sorcha wallet list

# Create a new wallet if needed
sorcha wallet create --name "my-wallet" --algorithm ED25519
```

### REST API Issues

**404 Not Found on /api/registers/initiate**
- Ensure Register Service is running
- Check API Gateway routing configuration

**401 Unauthorized on finalize**
- Check nonce matches between initiate and finalize
- Verify JWT token is still valid (not expired)

**Signature verification failure**
- Ensure using `isPreHashed=true` when signing
- Verify the hex hash was correctly converted to bytes

## Files in This Walkthrough

| File | Purpose |
|------|---------|
| `README.md` | This documentation |
| `test-register-creation-cli.ps1` | CLI-based walkthrough (recommended) |
| `test-register-creation-rest.ps1` | REST API walkthrough (legacy/debugging) |
| `test-register-creation-with-real-signing.ps1` | Advanced real crypto testing |
| `test-register-creation-docker.ps1` | Docker internal network testing |

## Next Steps

After completing this walkthrough:

1. **Try different algorithms**: Test with NISTP256 and RSA4096
2. **Multi-owner registers**: Add additional admins with roles
3. **Docket verification**: Inspect genesis docket and chain
4. **Transaction queries**: Use OData for advanced queries
5. **Production setup**: Configure Redis-backed pending storage

## References

- [Sorcha CLI Documentation](../../src/Apps/Sorcha.Cli/README.md)
- [Register Service Spec](../../.specify/specs/sorcha-register-service.md)
- [RegisterCreationOrchestrator.cs](../../src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs)
- [RegisterCommands.cs](../../src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs)
