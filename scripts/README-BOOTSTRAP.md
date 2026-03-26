# Sorcha Bootstrap Scripts

Automated setup scripts for configuring a fresh Sorcha installation using the Sorcha CLI.

## Overview

These scripts guide you through the initial configuration of a Sorcha platform deployment, including:

- ✅ CLI configuration profile
- ✅ Initial authentication setup
- ✅ System organization (tenant) creation
- ✅ Administrative user setup
- ✅ Node configuration
- ✅ Service principal creation
- ✅ Initial register creation

## Prerequisites

### Required

- **Docker Desktop** running with Sorcha services started
- **.NET 10 SDK** (for certificate generation)
- **Sorcha CLI** installed or available to run from source

### Check Prerequisites

```bash
# Check Docker is running
docker-compose ps

# Check .NET SDK
dotnet --version

# Check Sorcha CLI
sorcha --version
# OR run from source:
dotnet run --project src/Apps/Sorcha.Cli -- --version
```

## Quick Start

### PowerShell (Windows)

```powershell
# Interactive mode (recommended)
.\scripts\bootstrap-sorcha.ps1

# Non-interactive mode with defaults
.\scripts\bootstrap-sorcha.ps1 -NonInteractive

# Specify custom profile
.\scripts\bootstrap-sorcha.ps1 -Profile production
```

### Bash (Linux/macOS)

```bash
# Make script executable (first time only)
chmod +x scripts/bootstrap-sorcha.sh

# Interactive mode (recommended)
./scripts/bootstrap-sorcha.sh

# Non-interactive mode with defaults
./scripts/bootstrap-sorcha.sh --non-interactive

# Specify custom profile
./scripts/bootstrap-sorcha.sh --profile production
```

## Configuration Phases

### Phase 1: CLI Configuration

Configures the Sorcha CLI with service URLs and connection settings.

**Prompts:**
- Tenant Service URL (default: `http://localhost/api/tenants`)
- Register Service URL (default: `http://localhost/api/register`)
- Wallet Service URL (default: `http://localhost/api/wallets`)
- Peer Service URL (default: `http://localhost/api/peers`)
- Auth Token URL (default: `http://localhost/api/service-auth/token`)

**Output:**
- Creates `~/.sorcha/config.json` with profile configuration

### Phase 2: Initial Authentication

Sets up bootstrap service principal for automation.

**Prompts:**
- Bootstrap Client ID (default: `sorcha-bootstrap`)
- Bootstrap Client Secret (generated randomly)

**Output:**
- Service principal credentials for CLI authentication

### Phase 3: System Organization

Creates the primary tenant organization.

**Prompts:**
- Organization Name (default: `System Organization`)
- Organization Subdomain (default: `system`)
- Organization Description (default: `Primary system organization for Sorcha platform`)

**Output:**
- Organization ID for subsequent operations

### Phase 4: Administrative User

Creates the system administrator account.

**Prompts:**
- Admin Email Address (default: `admin@sorcha.local`)
- Admin Display Name (default: `System Administrator`)
- Admin Password (default: `Admin@123!`)

**Output:**
- Administrator user credentials

### Phase 5: Node Configuration

Configures the peer node identity.

**Prompts:**
- Node ID/Name (default: `node-<hostname>`)
- Node Description (default: `Primary Sorcha node - <hostname>`)
- Enable P2P networking (default: `true`)

**Output:**
- Node configuration for P2P networking

### Phase 6: Initial Register

Creates the system register for transactions.

**Prompts:**
- Register Name (default: `System Register`)
- Register Description (default: `Primary system register for transactions`)

**Output:**
- Register ID for transaction operations

## Configuration Files

### CLI Configuration

**Location:** `~/.sorcha/config.json`

Example:
```json
{
  "activeProfile": "docker",
  "defaultOutputFormat": "json",
  "verboseLogging": false,
  "quietMode": false,
  "profiles": {
    "docker": {
      "name": "docker",
      "tenantServiceUrl": "http://localhost/api/tenants",
      "registerServiceUrl": "http://localhost/api/register",
      "walletServiceUrl": "http://localhost/api/wallets",
      "peerServiceUrl": "http://localhost/api/peers",
      "authTokenUrl": "http://localhost/api/service-auth/token",
      "defaultClientId": "sorcha-bootstrap",
      "verifySsl": false,
      "timeoutSeconds": 30
    }
  }
}
```

### Bootstrap Information

**Location:** `~/.sorcha/bootstrap-info.json`

Contains a record of the bootstrap process including:
- Timestamp
- Organization ID and name
- Admin email
- Node ID
- Service URLs
- Enhancement TODOs

## Next Steps After Bootstrap

### 1. Verify Installation

```bash
# Check API health
curl http://localhost/api/health

# View API documentation
open http://localhost/scalar/

# Check Aspire dashboard
open http://localhost:18888
```

### 2. Test Authentication

```bash
# Login with admin credentials
sorcha auth login --username admin@sorcha.local

# Check authentication status
sorcha auth status
```

### 3. Explore the Platform

```bash
# List organizations
sorcha org list

# List users
sorcha user list

# List registers
sorcha register list
```

## Current Limitations

**IMPORTANT:** The bootstrap scripts currently contain placeholder commands for features not yet implemented in the Sorcha CLI.

### Required CLI Enhancements

The following CLI commands need to be implemented for full bootstrap functionality:

#### CLI-BOOTSTRAP-001: `sorcha config init`
**Purpose:** Initialize CLI configuration profile

**Command:**
```bash
sorcha config init \
  --profile docker \
  --tenant-url http://localhost/api/tenants \
  --register-url http://localhost/api/register \
  --wallet-url http://localhost/api/wallets \
  --peer-url http://localhost/api/peers \
  --auth-url http://localhost/api/service-auth/token
```

**Tasks:**
- Create profile in config.json
- Validate service URLs connectivity
- Set default client ID
- Return success/failure

#### CLI-BOOTSTRAP-002: `sorcha org create`
**Purpose:** Create organization with subdomain

**Command:**
```bash
sorcha org create \
  --name "System Organization" \
  --subdomain "system" \
  --description "Primary system organization"
```

**Tasks:**
- Call Tenant Service API
- Create organization
- Return organization ID (JSON output)

#### CLI-BOOTSTRAP-003: `sorcha user create`
**Purpose:** Create user in organization

**Command:**
```bash
sorcha user create \
  --org-id <guid> \
  --email admin@sorcha.local \
  --name "System Administrator" \
  --password <secure> \
  --role Administrator
```

**Tasks:**
- Call Tenant Service API
- Create user with role
- Return user ID

#### CLI-BOOTSTRAP-004: `sorcha sp create`
**Purpose:** Create service principal

**Command:**
```bash
sorcha sp create \
  --name "sorcha-bootstrap" \
  --scopes "all" \
  --description "Bootstrap automation principal"
```

**Tasks:**
- Call Tenant Service API
- Create service principal
- Generate and return client secret (show once warning)
- Return client ID

#### CLI-BOOTSTRAP-005: `sorcha register create`
**Purpose:** Create register in organization

**Command:**
```bash
sorcha register create \
  --name "System Register" \
  --org-id <guid> \
  --description "Primary system register" \
  --publish
```

**Tasks:**
- Call Register Service API
- Create register
- Optionally publish
- Return register ID

#### CLI-BOOTSTRAP-006: `sorcha node configure` (NEW)
**Purpose:** Configure P2P node identity

**Command:**
```bash
sorcha node configure \
  --node-id "node-hostname" \
  --description "Primary Sorcha node" \
  --enable-p2p true
```

**Tasks:**
- Call Peer Service API
- Set node identity
- Configure P2P settings
- Return node status

### Required Service Enhancements

#### TENANT-SERVICE-001: Bootstrap API Endpoint
**Purpose:** Atomic bootstrap operation

**Endpoint:** `POST /api/tenants/bootstrap`

**Request:**
```json
{
  "organizationName": "System Organization",
  "organizationSubdomain": "system",
  "adminEmail": "admin@sorcha.local",
  "adminName": "System Administrator",
  "adminPassword": "<secure>",
  "servicePrincipalName": "sorcha-bootstrap"
}
```

**Response:**
```json
{
  "organizationId": "guid",
  "userId": "guid",
  "servicePrincipal": {
    "clientId": "string",
    "clientSecret": "string"
  }
}
```

**Benefits:**
- Single atomic operation
- Consistent state
- Rollback on failure
- Simplified bootstrap flow

#### PEER-SERVICE-001: Node Configuration API
**Purpose:** Configure peer node identity

**Endpoint:** `POST /api/peers/configure`

**Request:**
```json
{
  "nodeId": "node-hostname",
  "description": "Primary Sorcha node",
  "enableP2P": true,
  "publicAddress": "optional-external-ip"
}
```

**Response:**
```json
{
  "nodeId": "string",
  "status": "configured",
  "p2pEnabled": true
}
```

## Workaround: Manual Configuration

Until the CLI commands are implemented, you can manually configure using the API:

### 1. Create Organization

```bash
curl -X POST http://localhost/api/tenants/organizations \
  -H "Content-Type: application/json" \
  -d '{
    "name": "System Organization",
    "subdomain": "system",
    "description": "Primary system organization"
  }'
```

### 2. Create Admin User

```bash
curl -X POST http://localhost/api/tenants/users \
  -H "Content-Type: application/json" \
  -d '{
    "organizationId": "<org-id>",
    "email": "admin@sorcha.local",
    "name": "System Administrator",
    "password": "Admin@123!",
    "role": "Administrator"
  }'
```

### 3. Create Service Principal

```bash
curl -X POST http://localhost/api/tenants/service-principals \
  -H "Content-Type: application/json" \
  -d '{
    "name": "sorcha-bootstrap",
    "scopes": ["all"],
    "description": "Bootstrap automation"
  }'
```

## Troubleshooting

### Script Fails: "Sorcha CLI not found"

**Solution:**
```bash
# Install globally
dotnet tool install -g Sorcha.Cli

# OR run from source
dotnet run --project src/Apps/Sorcha.Cli -- <command>
```

### Script Fails: "Docker services not running"

**Solution:**
```bash
# Start Docker services
docker-compose up -d

# Verify services are running
docker-compose ps
```

### Script Fails: "Services not ready"

**Solution:**
```bash
# Check service logs
docker-compose logs -f api-gateway

# Verify health endpoint
curl http://localhost/api/health
```

### Configuration File Not Created

**Location:**
- Windows: `C:\Users\<username>\.sorcha\config.json`
- Linux/macOS: `~/.sorcha/config.json`

**Solution:**
```bash
# Check file exists
cat ~/.sorcha/config.json

# Verify permissions (Unix)
ls -la ~/.sorcha/

# Manually create directory
mkdir -p ~/.sorcha
```

## Development Notes

### Testing the Scripts

```bash
# Test PowerShell script
.\scripts\bootstrap-sorcha.ps1 -NonInteractive

# Test Bash script
./scripts/bootstrap-sorcha.sh --non-interactive

# View generated config
cat ~/.sorcha/config.json
cat ~/.sorcha/bootstrap-info.json
```

### Customizing Defaults

Edit the script files to change default values:

**PowerShell:** `scripts/bootstrap-sorcha.ps1`
**Bash:** `scripts/bootstrap-sorcha.sh`

Look for the `Get-UserInput` (PowerShell) or `get_user_input` (Bash) function calls and modify the second parameter (default value).

## Contributing

See [CONTRIBUTING.md](../CONTRIBUTING.md) for contribution guidelines.

## License

See [LICENSE](../LICENSE) for license information.

---

**Created:** 2026-01-01
**Status:** Beta - Placeholder implementation pending CLI feature completion
**Related Documents:**
- [Sorcha CLI README](../src/Apps/Sorcha.Cli/README.md)
- [CLI Specification](../.specify/specs/sorcha-cli-admin-tool.md)
- [MASTER-TASKS.md](../.specify/MASTER-TASKS.md)
