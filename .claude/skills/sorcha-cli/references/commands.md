# CLI Commands Reference

## Command Structure

The Sorcha CLI follows a hierarchical command structure:

```
sorcha
├── auth                    # Authentication commands
│   ├── login              # Authenticate with credentials
│   ├── logout             # Clear cached tokens
│   └── status             # Show authentication status
├── config                  # Configuration management
│   ├── profile list       # List profiles
│   ├── profile set        # Set active profile
│   └── profile create     # Create new profile
├── register               # Register (ledger) management
│   ├── list               # List all registers
│   ├── get                # Get register by ID
│   ├── create             # Create new register (two-phase)
│   ├── update             # Update register metadata
│   ├── delete             # Delete register
│   ├── stats              # Get register statistics
│   ├── relationship       # This node's derived role set for a register (Feature 108)
│   ├── sync-state         # A register's sync state (indeterminate/syncing/caught-up/error)
│   └── sync-health        # Recovery sync health across all registers on this node
├── tx                     # Transaction commands
│   ├── list               # List transactions in register
│   ├── get                # Get transaction by ID
│   ├── submit             # Submit new transaction
│   ├── status             # Check transaction lifecycle status (active/revoked/superseded)
│   ├── proof              # Generate a Merkle inclusion proof (--out to save)
│   ├── verify-proof       # Verify a saved inclusion proof (offline-capable)
│   └── revoke             # Revoke a transaction with a recorded reason
├── docket                 # Docket (block) inspection
│   ├── list               # List dockets in register
│   ├── get                # Get docket by ID
│   └── transactions       # List transactions in docket
├── query                  # Cross-register queries
│   ├── wallet             # Query by wallet address
│   ├── sender             # Query by sender address
│   ├── blueprint          # Query by blueprint ID
│   ├── stats              # Get query statistics
│   └── odata              # Execute OData query
├── wallet                 # Wallet management
│   ├── list               # List wallets
│   ├── get                # Get wallet by address
│   ├── create             # Create new wallet
│   ├── recover            # Recover from mnemonic
│   ├── delete             # Delete wallet
│   └── sign               # Sign data
├── org                    # Organization management
├── user                   # User management
├── sp                     # Service principal management
└── peer                   # Peer network management
```

## Common Option Patterns

### Required Options

```csharp
_idOption = new Option<string>("--id", "Resource ID") { Required = true };
```

### Optional Options with Defaults

```csharp
_pageOption = new Option<int?>("--page", "Page number (default: 1)");
_pageSizeOption = new Option<int?>("--page-size", "Items per page (default: 50)");
```

### Boolean Flags

```csharp
_yesOption = new Option<bool>("--yes", "Skip confirmation prompt");
_verboseOption = new Option<bool>("--verbose", "Enable verbose output");
```

### Nullable Options

```csharp
_descriptionOption = new Option<string?>("--description", "Optional description");
```

## Two-Phase Register Creation Pattern

The register creation uses a cryptographic attestation flow:

```csharp
// Phase 1: Initiate
var initiateRequest = new InitiateRegisterCreationRequest
{
    Name = name,
    TenantId = tenantId,
    Description = description,
    Owners = new List<OwnerInfo>
    {
        new OwnerInfo { UserId = userId, WalletId = ownerWallet }
    }
};
var initiateResponse = await registerClient.InitiateRegisterCreationAsync(
    initiateRequest, $"Bearer {token}");

// Phase 2: Sign attestations
var signedAttestations = new List<SignedAttestation>();
foreach (var attestation in initiateResponse.AttestationsToSign)
{
    var hashBytes = Convert.FromHexString(attestation.DataToSign);
    var base64Hash = Convert.ToBase64String(hashBytes);

    var signRequest = new SignTransactionRequest
    {
        TransactionData = base64Hash,
        IsPreHashed = true
    };

    var signResponse = await walletClient.SignTransactionAsync(
        attestation.WalletId, signRequest, $"Bearer {token}");

    signedAttestations.Add(new SignedAttestation
    {
        AttestationData = attestation.AttestationData,
        PublicKey = signResponse.PublicKey,
        Signature = signResponse.Signature,
        Algorithm = algorithm
    });
}

// Phase 3: Finalize
var finalizeRequest = new FinalizeRegisterCreationRequest
{
    RegisterId = initiateResponse.RegisterId,
    Nonce = initiateResponse.Nonce,
    SignedAttestations = signedAttestations
};
var finalizeResponse = await registerClient.FinalizeRegisterCreationAsync(
    finalizeRequest, $"Bearer {token}");
```

## Pagination Pattern

```csharp
// Options
_pageOption = new Option<int?>("--page", "Page number (default: 1)");
_pageSizeOption = new Option<int?>("--page-size", "Items per page (default: 50)");

// In action handler
var page = parseResult.GetValue(_pageOption);
var pageSize = parseResult.GetValue(_pageSizeOption);

// API call
var results = await client.ListAsync(page, pageSize, $"Bearer {token}");

// Display pagination info
if (page.HasValue || pageSize.HasValue)
{
    Console.WriteLine();
    ConsoleHelper.WriteInfo($"Page {page ?? 1} of {totalPages} (Total: {totalCount})");
}
```

## Table Display Pattern

```csharp
// Header
Console.WriteLine($"{"ID",-36} {"Name",-30} {"Status",-10} {"Created"}");
Console.WriteLine(new string('-', 100));

// Rows
foreach (var item in items)
{
    Console.WriteLine($"{item.Id,-36} {item.Name,-30} {item.Status,-10} {item.CreatedAt:yyyy-MM-dd}");
}
```

## Detail Display Pattern

```csharp
ConsoleHelper.WriteSuccess("Resource details:");
Console.WriteLine();
Console.WriteLine($"  ID:          {resource.Id}");
Console.WriteLine($"  Name:        {resource.Name}");
Console.WriteLine($"  Status:      {resource.Status}");
Console.WriteLine($"  Created:     {resource.CreatedAt:yyyy-MM-dd HH:mm:ss}");

if (!string.IsNullOrEmpty(resource.Description))
{
    Console.WriteLine($"  Description: {resource.Description}");
}
```

## Confirmation Prompt Pattern

```csharp
if (!confirm)
{
    ConsoleHelper.WriteWarning("WARNING: This action cannot be undone.");
    Console.Write($"Are you sure you want to delete '{id}'? [y/N]: ");
    var response = Console.ReadLine()?.Trim().ToLowerInvariant();

    if (response != "y" && response != "yes")
    {
        ConsoleHelper.WriteInfo("Operation cancelled.");
        return ExitCodes.Success;
    }
}
```

## JWT Token Extraction

```csharp
using System.IdentityModel.Tokens.Jwt;

var handler = new JwtSecurityTokenHandler();
var jwtToken = handler.ReadJwtToken(token);
var userId = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
    ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "userId")?.Value
    ?? throw new InvalidOperationException("Could not extract user ID from token");
```

## Organisation Key Derivation (Feature 133 / 083)

```bash
sorcha wallet org-key provision <orgId> [--algorithm ED25519]   # mnemonic shown ONCE
sorcha wallet org-key derive <orgId> --user-id <id> [--department N] --usage Identity
sorcha wallet org-key rotate <orgId> <derivedKeyId>
sorcha wallet org-key revoke <orgId> <derivedKeyId>
```

`--usage` values: `Identity`, `VCIssuance`, `Governance`, `Communications`, `ServiceAuth`.

**Reuse note (deviation from the literal plan)**: the org-key *response* DTOs are reused from
`Sorcha.ServiceClients.Wallet` (`OrgMasterKeyProvisionResponse`, `DerivedKeyResponse`,
`RevokeKeyResponse`) — no duplication. But the calls go through the CLI's own bearer-auth
`IWalletServiceClient`, NOT the shared `WalletServiceClient`, because the shared client
authenticates as a service principal (`IServiceAuthClient` client-credentials), which would put
org-key commands on a different auth principal than every other CLI command. `provision` surfaces
the mnemonic once and never persists it.

## Validator Roster Governance (Feature 133 / 086)

Extends `validator` (which already had approve/reject via `consent`) with the full roster lifecycle.
These hit `/api/validators/...` (distinct from the existing `/api/admin/validators/...` surface).

```bash
sorcha validator register --register-id <id> --validator-id <vid> --public-key <pk> --grpc-endpoint <url>
sorcha validator count --register-id <id>
sorcha validator audit --register-id <id> [--validator-id <vid>] [--limit N] [--offset N]
sorcha validator suspend    --register-id <id> --validator-id <vid> --reason "<text>"
sorcha validator reactivate --register-id <id> --validator-id <vid> [--notes "<text>"]
sorcha validator revoke     --register-id <id> --validator-id <vid> --reason "<text>"
sorcha validator sequence --register-id <id> --wallet <addr>
```

`suspend` and `revoke` are destructive and require an explicit `--validator-id` and `--reason`.

## Register Sync Diagnostics (Feature 133 / 108)

```bash
sorcha register relationship --id <registerId>   # owner / validator / subscriber role set
sorcha register sync-state   --id <registerId>   # Indeterminate / Syncing / CaughtUp / Error + heights
sorcha register sync-health                       # all registers on this node (table)
```

All read-only. `relationship` and `sync-state` reuse the shared `Sorcha.Register.Models.LocalRelationship`
record types (the Register Refit client's JsonStringEnumConverter handles their flag/enum fields).

## Trust-Hardening Transaction Commands (Feature 133 / 079)

These commands wrap the Register Service's trust-hardening surface. They reuse the shared
`Sorcha.Register.Models` types (`MerkleInclusionProof`, `TransactionStatusResponse`,
`RevocationReason`) rather than redefining them — the Register Refit client is built with a
`JsonStringEnumConverter` so the platform's string-serialized enums deserialize correctly.

```bash
# Generate a Merkle inclusion proof and save it for offline verification
sorcha tx proof --register-id <id> --tx-id <txId> --out proof.json

# Verify a saved proof (the verify endpoint is anonymous / offline-capable)
sorcha tx verify-proof --register-id <id> --file proof.json

# Revoke a transaction with a reason (Superseded requires --superseded-by)
sorcha tx revoke --register-id <id> --tx-id <txId> --reason Erroneous
sorcha tx revoke --register-id <id> --tx-id <txId> --reason Superseded --superseded-by <newTxId>

# Report lifecycle status — Active / Revoked / Superseded (NOT submission progress)
sorcha tx status --register-id <id> --tx-id <txId>
```

**`tx status` correctness note**: this command targets `GET …/transactions/{txId}/status`, which
returns the *lifecycle* status (`Active` / `Revoked` / `Superseded`), not submission progress.
Earlier the command deserialized that response into the submission-ack shape and always reported
"Unknown status"; it now uses `TransactionStatusResponse` and reports the lifecycle state plus any
revocation/superseding transaction pointers.

Valid `--reason` values: `Superseded`, `Erroneous`, `Compromised`, `Expired`, `Withdrawn`, `Regulatory`.
