# Sorcha Walkthroughs

Interactive demos and integration tests for the Sorcha platform. Each walkthrough uses a shared PowerShell module for consistent, idempotent execution.

---

## Quick Start

```powershell
# Prerequisites: Docker Desktop, PowerShell 7.5+, Sorcha services running
docker-compose up -d

# Generate secrets (first time only)
pwsh walkthroughs/initialize-secrets.ps1

# Run a single walkthrough
pwsh walkthroughs/PingPong/setup.ps1
pwsh walkthroughs/PingPong/run.ps1

# Run all walkthroughs
pwsh walkthroughs/run-all.ps1
```

---

## Walkthrough Catalog

### Foundation

Basic infrastructure verification.

| Walkthrough | Script Pattern | Purpose |
|-------------|---------------|---------|
| [BlueprintStorageBasic](./BlueprintStorageBasic/) | setup + run | Docker startup, bootstrap, JWT auth, blueprint CRUD |
| [AdminIntegration](./AdminIntegration/) | single script | Blazor WASM admin UI behind API Gateway |
| [McpServerBasics](./McpServerBasics/) | single script | MCP Server authentication and tool verification |

### Single-Org

Single organization with users, wallets, and registers.

| Walkthrough | Script Pattern | Purpose |
|-------------|---------------|---------|
| [PingPong](./PingPong/) | setup + run | 2-participant ping-pong workflow with signed register |
| [RegisterCreationFlow](./RegisterCreationFlow/) | setup + run | Register creation with attestation signing |
| [WalletVerification](./WalletVerification/) | setup + run | Wallet creation, signing, pre-hashed signing |
| [RegisterMongoDB](./RegisterMongoDB/) | single script | MongoDB integration health checks |

### Multi-Org

Multiple organizations with cross-org participants and complex workflows.

| Walkthrough | Script Pattern | Purpose |
|-------------|---------------|---------|
| [OrganizationPingPong](./OrganizationPingPong/) | setup + run | Multi-participant ping-pong with published participants |
| [ConstructionPermit](./ConstructionPermit/) | setup + run | 4-org, 5-participant permit workflow with conditional routing |
| [MedicalEquipmentRefurb](./MedicalEquipmentRefurb/) | setup + run | 3-org, 4-participant refurbishment with participant publishing |
| [SelfBuildHouse](./SelfBuildHouse/) | setup + run | 6-org, 7-participant self-build across 2 registers with cross-register VCs, staged inspections, and document uploads |

### Advanced

Specialized scenarios requiring additional infrastructure.

| Walkthrough | Script Pattern | Purpose |
|-------------|---------------|---------|
| [DistributedRegister](./DistributedRegister/) | setup + run | Cross-machine register replication (2 nodes) |
| [PerformanceBenchmark](./PerformanceBenchmark/) | setup + run | Payload, throughput, latency, concurrency benchmarks |

### Design-Only

Planning documentation only — not executable.

| Walkthrough | Purpose |
|-------------|---------|
| [UserWalletCreation](./UserWalletCreation/) | Design study for user + wallet creation flows |
| [WalletEncryption](./WalletEncryption/) | Design for production encryption providers |

---

## Script Conventions

### setup.ps1 + run.ps1 Pattern

Most walkthroughs follow a two-script pattern:

1. **`setup.ps1`** — Bootstrap org, create users/wallets/participants, create register, publish blueprint. Saves state to `state.json`.
2. **`run.ps1`** — Load `state.json`, execute walkthrough steps, report pass/fail.

Both are idempotent — safe to re-run.

### Parameters

| Parameter | Available On | Default | Description |
|-----------|-------------|---------|-------------|
| `-Profile` | setup.ps1 | `gateway` | `gateway` / `direct` / `aspire` |
| `-SkipHealthCheck` | setup.ps1 | off | Skip Docker health check |
| `-ShowJson` | run.ps1 | off | Show JSON responses |
| `-Rounds` | run.ps1 (ping-pong) | 3 | Number of round-trips |
| `-Scenario` | run.ps1 (multi-org) | `all` | Scenario filter (A/B/C/all) |

### Single-Script Pattern

Foundation walkthroughs use a single script (no state.json):
- `test-admin-integration.ps1`
- `test-mcp-server.ps1`
- `test-mongodb-integration.ps1`

---

## run-all.ps1

Master runner that executes all walkthroughs in order:

```powershell
pwsh walkthroughs/run-all.ps1                    # Run everything
pwsh walkthroughs/run-all.ps1 -SkipAdvanced      # Skip DistributedRegister + PerformanceBenchmark
pwsh walkthroughs/run-all.ps1 -OnlySetup         # Run only setup.ps1 (skip run.ps1)
pwsh walkthroughs/run-all.ps1 -Profile direct    # Use direct service URLs
```

Execution order: Foundation → Single-Org → Multi-Org → Advanced

---

## Secrets

Credentials are stored in `walkthroughs/.secrets/passwords.json` (git-ignored).

```powershell
# Generate secrets (first time)
pwsh walkthroughs/initialize-secrets.ps1

# Force regenerate
pwsh walkthroughs/initialize-secrets.ps1 -Force
```

Override via environment variable: `SORCHA_WT_SECRETS_<WALKTHROUGH>` (JSON string).

---

## Shared Module API

All walkthroughs import `modules/SorchaWalkthrough/SorchaWalkthrough.psm1`:

```powershell
$modulePath = Join-Path $PSScriptRoot "../modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force
```

### Console Output

| Function | Purpose |
|----------|---------|
| `Write-WtBanner $text` | Section banner (cyan) |
| `Write-WtStep $text` | Step header (yellow) |
| `Write-WtSuccess $text` | Success message (green) |
| `Write-WtFail $text` | Failure message (red) |
| `Write-WtInfo $text` | Info message (white) |
| `Write-WtWarn $text` | Warning message (yellow) |

### HTTP & Auth

| Function | Purpose |
|----------|---------|
| `Invoke-SorchaApi -Url -Method [-Body] [-Headers] [-ContentType] [-ShowJson] [-RawResponse]` | Consolidated HTTP caller |
| `Get-SorchaErrorBody $errorRecord` | Extract HTTP error body |
| `Decode-SorchaJwt $token` | Decode JWT payload (base64url) |
| `ConvertFrom-HexToBase64 $hex` | Hex string → base64 (for attestation signing) |

### Environment & Secrets

| Function | Purpose |
|----------|---------|
| `Initialize-SorchaEnvironment -Profile [-SkipHealthCheck]` | Docker health + profile URLs → `@{ TenantUrl, RegisterUrl, WalletUrl, BlueprintUrl }` |
| `Get-SorchaSecrets -WalkthroughName` | Read credentials from `.secrets/passwords.json` → `@{ adminEmail, adminName, adminPassword, ... }` |

### Auth & Resources

| Function | Purpose |
|----------|---------|
| `Connect-SorchaAdmin -TenantUrl -OrgName -OrgSubdomain -AdminEmail -AdminName -AdminPassword` | Bootstrap (409 fallback) → login → `@{ Token, OrganizationId, AdminUserId, Headers }` |
| `Get-OrCreateOrganization -TenantUrl -Name -Subdomain -Headers` | Idempotent org creation |
| `Get-OrCreateUser -TenantUrl -OrgId -Email -DisplayName -Password -Roles -Headers` | Idempotent user creation |

### Wallet & Participant

| Function | Purpose |
|----------|---------|
| `New-SorchaWallet -WalletUrl -Name -Headers [-FetchPublicKey]` | Create ED25519 wallet |
| `Register-SorchaParticipant -TenantUrl -WalletUrl -OrgId -UserId -DisplayName -WalletAddress -Headers` | Self-register + challenge-sign-verify wallet link |
| `Publish-SorchaParticipant -TenantUrl -RegisterUrl -WalletUrl -OrgId -ParticipantId -WalletAddress -RegisterId -Headers` | Publish participant record to register |

### Register & Blueprint

| Function | Purpose |
|----------|---------|
| `New-SorchaRegister -RegisterUrl -WalletUrl -Name -Description -TenantId -OwnerUserId -OwnerWalletAddress -Headers` | 3-phase register creation (initiate → sign → finalize) |
| `Publish-SorchaBlueprint -BlueprintUrl -RegisterUrl -TemplatePath -WalletMappings -Headers [-ShowJson]` | Load template, patch wallets, upload, publish |
| `Invoke-SorchaAction -BlueprintUrl -InstanceId -ActionId -SenderWallet -RegisterAddress -Headers [-PayloadData] [-Reject] [-ShowJson]` | Execute or reject action with X-Delegation-Token |

---

## Directory Structure

```
walkthroughs/
├── README.md                          # This file
├── MIGRATION.md                       # Old-to-new pattern changes
├── run-all.ps1                        # Master runner
├── initialize-secrets.ps1             # Secret generator
├── .secrets/                          # Git-ignored credentials
│   └── passwords.json
├── modules/
│   └── SorchaWalkthrough/
│       ├── SorchaWalkthrough.psd1     # Module manifest
│       └── SorchaWalkthrough.psm1     # Shared module (~600 lines)
├── BlueprintStorageBasic/             # Foundation
│   ├── config.json
│   ├── setup.ps1
│   ├── run.ps1
│   └── README.md
├── AdminIntegration/                  # Foundation
│   ├── config.json
│   └── test-admin-integration.ps1
├── McpServerBasics/                   # Foundation
│   ├── config.json
│   ├── test-mcp-server.ps1
│   └── get-token-and-run-mcp.ps1
├── PingPong/                          # Single-Org
│   ├── config.json
│   ├── setup.ps1
│   ├── run.ps1
│   ├── templates/
│   └── README.md
├── RegisterCreationFlow/              # Single-Org
├── WalletVerification/                # Single-Org
├── RegisterMongoDB/                   # Single-Org
├── OrganizationPingPong/              # Multi-Org
├── ConstructionPermit/                # Multi-Org
├── MedicalEquipmentRefurb/            # Multi-Org
├── DistributedRegister/               # Advanced
├── PerformanceBenchmark/              # Advanced
├── UserWalletCreation/                # Design-only
└── WalletEncryption/                  # Design-only
```

---

## Creating a New Walkthrough

1. Create directory: `walkthroughs/YourWalkthrough/`
2. Create `config.json` with name, description, category, organization details
3. Create `setup.ps1` — import module, bootstrap org, create resources, save `state.json`
4. Create `run.ps1` — import module, load `state.json`, execute steps, report pass/fail
5. Add entry to `run-all.ps1` walkthroughs array
6. Add secrets entry to `initialize-secrets.ps1`
7. Update this README

### config.json Template

```json
{
  "name": "YourWalkthrough",
  "description": "Brief description of what this tests.",
  "category": "foundation|single-org|multi-org|advanced",
  "organization": {
    "name": "Your Org Name",
    "subdomain": "your-subdomain"
  },
  "secretsKey": "your-subdomain",
  "requiresRegister": true,
  "requiresParticipants": false
}
```

---

## Migration

For details on the old-to-new pattern changes, see [MIGRATION.md](./MIGRATION.md).
