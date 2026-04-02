# Sorcha CLI Administrative Tool Specification

**Version:** 1.0
**Date:** 2025-12-10
**Status:** Draft - Ready for Implementation
**Related Constitution:** [constitution.md](../constitution.md)
**Related Specifications:**
- [sorcha-tenant-service.md](sorcha-tenant-service.md)
- [sorcha-register-service.md](sorcha-register-service.md)

---

## Executive Summary

The Sorcha CLI (`sorcha.cli`) is a **cross-platform administrative command-line tool** for managing the Sorcha decentralised register platform. It provides operators, administrators, and DevOps teams with direct access to platform services for:

- **Tenant Management** - Organizations, users, roles, and permissions
- **Register Operations** - Register lifecycle, transaction inspection, blockchain queries
- **Peer Network Monitoring** - Connection status, peer topology, network health
- **Authentication** - Service principal and user authentication flows
- **Multi-Environment Support** - Dev, staging, production configurations
- **Scripting & Automation** - Pipelines, IaC, operational scripts

**Key Benefits:**
- ✅ **No UI Required** - Administer platform from terminal/SSH
- ✅ **Automation-Friendly** - JSON output, exit codes, scriptable
- ✅ **Cross-Platform** - Windows, macOS, Linux support
- ✅ **Environment Profiles** - Switch between environments instantly
- ✅ **Secure** - OAuth2 authentication, credential management
- ✅ **Portable** - Distributed as .NET global tool (`dotnet tool install -g sorcha.cli`)

---

## Architecture Overview

### System Context

```
┌──────────────────────────────────────────────────────────────┐
│                    Sorcha Platform                            │
│                                                               │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐ │
│  │ Tenant   │   │ Register │   │  Peer    │   │ Wallet   │ │
│  │ Service  │   │ Service  │   │ Service  │   │ Service  │ │
│  └────┬─────┘   └────┬─────┘   └────┬─────┘   └────┬─────┘ │
│       │              │              │              │         │
│       │              │              │              │         │
│       └──────────────┼──────────────┼──────────────┘         │
│                      │              │                        │
│                 REST APIs       REST APIs                    │
│                      │              │                        │
└──────────────────────┼──────────────┼────────────────────────┘
                       │              │
                       │              │
              ┌────────▼──────────────▼────────┐
              │   Sorcha CLI (sorcha.cli)      │
              │                                 │
              │  ┌──────────────────────────┐  │
              │  │  Command Router          │  │
              │  │  - org, user, register   │  │
              │  │  - peer, auth, config    │  │
              │  └────────┬─────────────────┘  │
              │           │                     │
              │  ┌────────▼─────────────────┐  │
              │  │  HTTP Client Manager     │  │
              │  │  - Auth token handling   │  │
              │  │  - Service discovery     │  │
              │  └────────┬─────────────────┘  │
              │           │                     │
              │  ┌────────▼─────────────────┐  │
              │  │  Configuration Manager   │  │
              │  │  - Environment profiles  │  │
              │  │  - Credential storage    │  │
              │  └──────────────────────────┘  │
              └─────────────────────────────────┘
                           │
                           │
                      ┌────▼────┐
                      │  User   │
                      │ Terminal│
                      └─────────┘
```

### Component Responsibilities

**Sorcha CLI:**
- Authenticate users and service principals
- Call REST APIs on Tenant, Register, Peer, Wallet services
- Format and display responses (table, JSON, CSV)
- Manage configuration profiles for multiple environments
- Cache authentication tokens securely
- Provide interactive and non-interactive modes

**Service APIs (Consumed):**
- Tenant Service: Organizations, users, roles, service principals
- Register Service: Registers, transactions, blocks, blockchain state
- Peer Service: Peer connections, network topology, diagnostics
- Wallet Service: (Future) Wallet management, key operations

### Technology Stack

- **.NET 10** - Cross-platform runtime
- **System.CommandLine** - Modern CLI framework with subcommands, options, validation
- **Spectre.Console** - Rich terminal UI (tables, trees, progress bars)
- **Refit** - Type-safe HTTP client for API calls
- **Microsoft.Extensions.Configuration** - Configuration management
- **Microsoft.Extensions.Http.Polly** - Resilience policies (retry, circuit breaker)
- **Serilog** - Structured logging
- **KeePassLib** (or similar) - Secure credential storage

---

## Command Structure

### Top-Level Commands

```bash
sorcha [command] [subcommand] [options]

Commands:
  org         Manage organizations
  user        Manage users
  role        Manage roles and permissions
  principal   Manage service principals
  register    Manage registers
  transaction View and query transactions
  peer        Monitor peer network
  auth        Authenticate and manage credentials
  config      Manage CLI configuration and profiles
  version     Display version information
```

### Global Options

```bash
--profile, -p <name>       Use named profile (dev/staging/prod)
--output, -o <format>      Output format (table/json/csv/yaml)
--verbose, -v              Enable verbose logging
--quiet, -q                Suppress non-error output
--no-color                 Disable colored output
--help, -h                 Display help
```

---

## Operation Modes

The Sorcha CLI supports **two distinct operation modes** to accommodate different use cases:

### 1. Interactive Console Mode (REPL)

**Use Case:** Interactive exploration, administration, monitoring

**Launch:**
```bash
# Start interactive console
sorcha console

# Or use shorthand
sorcha
```

**Features:**
- **Command History** - Navigate previous commands with Up/Down arrows
- **Tab Completion** - Auto-complete commands, subcommands, and IDs
- **Context Awareness** - Remember current profile, organization, register
- **Persistent Authentication** - Single login for entire session
- **Rich Feedback** - Colors, tables, progress indicators
- **Multi-line Input** - Support for complex JSON input
- **Help Integration** - Inline help with `?` or `help <command>`
- **Command Aliases** - Shortcuts for common operations

**Example Session:**
```
$ sorcha console

╔════════════════════════════════════════════════════════════╗
║  Sorcha CLI v1.0.0 - Interactive Console                   ║
║  Type 'help' for available commands, 'exit' to quit        ║
╚════════════════════════════════════════════════════════════╝

Not authenticated. Logging in...

sorcha> auth login --username admin@acme.com
Password: ********

✓ Logged in as admin@acme.com (Acme Corporation)
✓ Session active for 60 minutes

sorcha> org list

┌──────────────────────────────────────┬──────────────────┬───────────┬────────┐
│ ID                                   │ Name             │ Subdomain │ Status │
├──────────────────────────────────────┼──────────────────┼───────────┼────────┤
│ 7c9e6679-7425-40de-944b-e07fc1f90ae7 │ Acme Corporation │ acme      │ Active │
└──────────────────────────────────────┴──────────────────┴───────────┴────────┘

sorcha> use org 7c9e6679-7425-40de-944b-e07fc1f90ae7

✓ Context set to organization: Acme Corporation

sorcha[acme]> user list

┌──────────────────────────────────────┬─────────────────┬────────────────┐
│ ID                                   │ Name            │ Role           │
├──────────────────────────────────────┼─────────────────┼────────────────┤
│ 550e8400-e29b-41d4-a716-446655440000 │ Alice Johnson   │ Administrator  │
│ 661f9511-f3ac-52e5-b827-f28fe3b91cf9 │ Bob Smith       │ Member         │
└──────────────────────────────────────┴─────────────────┴────────────────┘

sorcha[acme]> tx list --register-id reg_abc123 --limit 5

# Shows last 5 transactions

sorcha[acme]> exit

✓ Logged out. Session ended.
```

**Interactive Mode Commands:**
```
help [command]           Show help for command
clear                    Clear screen
history                  Show command history
use org <id>             Set current organization context
use register <id>        Set current register context
use profile <name>       Switch to different profile
status                   Show current context (auth, org, register)
exit, quit               Exit console
```

**Tab Completion Examples:**
```bash
sorcha> org <TAB>
  list  get  create  update  delete  suspend  reactivate

sorcha> org get <TAB>
  7c9e6679-7425-40de-944b-e07fc1f90ae7  (Acme Corporation)
  8d0f7780-8536-51ef-a827-f17fd2a90bf8  (Beta Industries)

sorcha> tx list --register-id <TAB>
  reg_abc123  (Supply Chain - Q4 2025)
  reg_def456  (Invoice Approval)
```

---

### 2. Flag-Based Mode (Scripts & AI Agents)

**Use Case:** Automation, CI/CD pipelines, AI agent tools, batch operations

**Key Features:**
- **Non-Interactive** - No prompts or interactive input required
- **Structured Output** - JSON output for easy parsing
- **Exit Codes** - Proper status codes for error handling
- **Silent Mode** - Suppress progress/info messages
- **Idempotent** - Safe to retry failed operations
- **Authentication Caching** - Reuse tokens across commands

**Example: Automation Script**
```bash
#!/bin/bash
set -e  # Exit on error

# Authenticate once (token cached for 60 minutes)
sorcha auth login \
  --service \
  --client-id automation-script \
  --client-secret "${CLI_SECRET}" \
  --profile production \
  --output json \
  --quiet

# Check authentication worked (exit code 0 = success)
if [ $? -ne 0 ]; then
  echo "Authentication failed"
  exit 1
fi

# Create organization
ORG_ID=$(sorcha org create \
  --name "New Customer Corp" \
  --subdomain "newcustomer" \
  --output json \
  --quiet | jq -r '.id')

echo "Created organization: ${ORG_ID}"

# Create admin user
USER_ID=$(sorcha user create \
  --org-id "${ORG_ID}" \
  --email "admin@newcustomer.com" \
  --name "Admin User" \
  --role Administrator \
  --output json \
  --quiet | jq -r '.id')

echo "Created user: ${USER_ID}"

# Create register
REG_ID=$(sorcha register create \
  --name "Production Register" \
  --org-id "${ORG_ID}" \
  --publish \
  --output json \
  --quiet | jq -r '.id')

echo "Created register: ${REG_ID}"

# Logout (clear cached token)
sorcha auth logout --quiet

echo "✓ Onboarding complete"
```

**Example: AI Agent Tool Integration**
```python
import subprocess
import json

class SorchaCliTool:
    """AI Agent tool for Sorcha CLI operations"""

    def __init__(self, profile="production"):
        self.profile = profile
        self._authenticate()

    def _authenticate(self):
        """Authenticate using service principal"""
        subprocess.run([
            "sorcha", "auth", "login",
            "--service",
            "--client-id", "ai-agent",
            "--client-secret", os.environ["SORCHA_SECRET"],
            "--profile", self.profile,
            "--quiet"
        ], check=True)

    def list_organizations(self) -> list:
        """List all organizations"""
        result = subprocess.run([
            "sorcha", "org", "list",
            "--output", "json",
            "--quiet"
        ], capture_output=True, text=True, check=True)

        return json.loads(result.stdout)["organizations"]

    def get_transaction(self, tx_id: str) -> dict:
        """Get transaction details"""
        result = subprocess.run([
            "sorcha", "tx", "get", tx_id,
            "--show-payload",
            "--output", "json",
            "--quiet"
        ], capture_output=True, text=True, check=True)

        return json.loads(result.stdout)

    def search_transactions(self, register_id: str, query: str) -> list:
        """Search transactions"""
        result = subprocess.run([
            "sorcha", "tx", "search",
            "--register-id", register_id,
            "--query", query,
            "--output", "json",
            "--quiet"
        ], capture_output=True, text=True, check=True)

        return json.loads(result.stdout)["transactions"]

# Use in AI agent
tool = SorchaCliTool()
orgs = tool.list_organizations()
print(f"Found {len(orgs)} organizations")

tx = tool.get_transaction("tx_550e8400e29b41d4a716446655440000")
print(f"Transaction status: {tx['status']}")
```

**Flag-Based Mode Options:**
```bash
# Required for non-interactive use
--yes, -y                 Auto-confirm all prompts
--quiet, -q              Suppress all non-error output
--output json            Structured output for parsing
--no-progress            Disable progress bars
--no-color               Disable ANSI colors

# Error handling
--continue-on-error      Don't exit on first error (batch mode)
--retry <count>          Retry failed operations
--timeout <seconds>      Command timeout
```

---

### 3. Authentication Caching

**Problem:** Running multiple CLI commands requires authentication for each command, causing delays and unnecessary API calls.

**Solution:** Token caching with automatic refresh

**How It Works:**

1. **Initial Authentication:**
   ```bash
   sorcha auth login --username admin@acme.com --save
   ```
   - Requests access token from Tenant Service
   - Caches token securely on disk with encryption
   - Stores token expiry timestamp
   - Saves refresh token (if available)

2. **Subsequent Commands:**
   ```bash
   sorcha org list
   ```
   - Checks token cache
   - If token valid (not expired), uses cached token
   - If token expired, automatically refreshes using refresh token
   - If refresh fails, prompts for re-authentication

3. **Token Lifecycle:**
   ```
   ┌─────────────────────────────────────────────────────┐
   │ Command Execution                                    │
   └────────────┬────────────────────────────────────────┘
                │
                ▼
   ┌─────────────────────────────────────────────────────┐
   │ Check Token Cache                                    │
   │ Location: ~/.sorcha/tokens/{profile}.token           │
   └────────────┬────────────────────────────────────────┘
                │
                ├─────────► Token Missing ───────────────┐
                │                                         │
                ├─────────► Token Expired ───────────────┤
                │                                         ▼
                ├─────────► Token Valid ──────────► Use Token
                │
                ▼
   ┌─────────────────────────────────────────────────────┐
   │ Attempt Token Refresh                                │
   │ - Use refresh token if available                     │
   │ - Request new access token                           │
   └────────────┬────────────────────────────────────────┘
                │
                ├─────────► Refresh Success ──────► Cache New Token
                │
                ▼
   ┌─────────────────────────────────────────────────────┐
   │ Refresh Failed                                       │
   │ - Prompt for re-authentication (interactive mode)    │
   │ - Exit with error code 2 (flag-based mode)          │
   └──────────────────────────────────────────────────────┘
   ```

**Token Cache Location:**
```
~/.sorcha/
├── config.json                 # Configuration profiles
├── tokens/
│   ├── default.token          # Default profile token (encrypted)
│   ├── dev.token              # Dev profile token
│   ├── staging.token          # Staging profile token
│   └── production.token       # Production profile token
└── audit.log                  # Command audit log
```

**Token Cache Format (Encrypted):**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "tokenType": "Bearer",
  "expiresAt": "2025-12-10T16:30:45Z",
  "issuedAt": "2025-12-10T15:30:45Z",
  "profile": "production",
  "userEmail": "admin@acme.com",
  "organizationId": "7c9e6679-7425-40de-944b-e07fc1f90ae7"
}
```

**Cache Management Commands:**
```bash
# View cached token status
sorcha auth token --show-cache

# Output:
# Cached Token Status
# ━━━━━━━━━━━━━━━━━━━━━━━━
# Profile:        production
# User:           admin@acme.com
# Issued:         2025-12-10 15:30:45
# Expires:        2025-12-10 16:30:45
# Time Remaining: 42 minutes
# Status:         Valid ✓

# Force token refresh
sorcha auth token --refresh

# Clear token cache (logout)
sorcha auth logout

# Clear all profiles
sorcha auth logout --all
```

**Security Considerations:**
- Tokens encrypted at rest using OS-specific secure storage
- Token cache files have restrictive permissions (0600 on Unix)
- Tokens never logged or displayed (except with explicit `--show-token` debug flag)
- Automatic cleanup of expired tokens
- Token revocation invalidates cache

**Configuration:**
```json
{
  "authentication": {
    "tokenCacheEnabled": true,
    "tokenCachePath": "~/.sorcha/tokens",
    "autoRefreshEnabled": true,
    "refreshBeforeExpiry": 300,  // Refresh 5 minutes before expiry
    "maxCacheAge": 3600           // Max 1 hour cache
  }
}
```

**Batch Operation Example:**
```bash
#!/bin/bash
# Deploy new environment - authentication cached across all commands

# Single authentication
sorcha auth login --service \
  --client-id deploy-automation \
  --client-secret "${SECRET}" \
  --profile production

# All subsequent commands use cached token (no re-auth needed)
sorcha org create --name "Production Env" --subdomain "prod"
sorcha user create --org-id "${ORG_ID}" --email "admin@prod.com" --name "Admin"
sorcha principal create --name "Prod Service" --scopes "all"
sorcha register create --name "Prod Register" --org-id "${ORG_ID}"

# Token automatically refreshed if it expires mid-script

sorcha auth logout  # Clean up cached token
```

---

## Feature Breakdown

### 1. Tenant Service - Organization Management

#### Commands

```bash
sorcha org list [options]
  --include-inactive       Include suspended/deleted organizations
  --output <format>        Output format (default: table)

sorcha org get <id>
  --show-branding          Include branding configuration
  --show-users             Include user count

sorcha org create [options]
  --name <name>            Organization name (required)
  --subdomain <subdomain>  Organization subdomain (required)
  --logo-url <url>         Logo URL
  --primary-color <hex>    Primary brand color
  --tagline <text>         Company tagline

sorcha org update <id> [options]
  --name <name>            New organization name
  --status <status>        Status (Active/Suspended)
  --logo-url <url>         New logo URL

sorcha org delete <id>
  --force                  Skip confirmation prompt

sorcha org suspend <id>
  --reason <text>          Suspension reason

sorcha org reactivate <id>
```

#### Example Usage

```bash
# List all organizations
sorcha org list

# Create organization
sorcha org create \
  --name "Acme Corporation" \
  --subdomain "acme" \
  --logo-url "https://acme.com/logo.png" \
  --primary-color "#0078D4"

# Get organization details (JSON output)
sorcha org get 7c9e6679-7425-40de-944b-e07fc1f90ae7 --output json

# Suspend organization
sorcha org suspend 7c9e6679-7425-40de-944b-e07fc1f90ae7 \
  --reason "Payment overdue"
```

#### Output Examples

**Table Format:**
```
┌──────────────────────────────────────┬──────────────────┬───────────┬────────┬─────────────────────┐
│ ID                                   │ Name             │ Subdomain │ Status │ Created             │
├──────────────────────────────────────┼──────────────────┼───────────┼────────┼─────────────────────┤
│ 7c9e6679-7425-40de-944b-e07fc1f90ae7 │ Acme Corporation │ acme      │ Active │ 2025-12-10 14:30:00 │
│ 8d0f7780-8536-51ef-a827-f17fd2a90bf8 │ Beta Industries  │ beta      │ Active │ 2025-12-09 10:15:00 │
└──────────────────────────────────────┴──────────────────┴───────────┴────────┴─────────────────────┘
```

**JSON Format:**
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

### 2. Tenant Service - User Management

#### Commands

```bash
sorcha user list [options]
  --org-id <guid>          Filter by organization
  --role <role>            Filter by role (Administrator/Member/Auditor)
  --status <status>        Filter by status (Active/Suspended)
  --include-inactive       Include inactive users

sorcha user get <user-id>
  --show-permissions       Include effective permissions
  --show-activity          Include login activity

sorcha user create [options]
  --org-id <guid>          Organization ID (required)
  --email <email>          Email address (required)
  --name <name>            Display name (required)
  --role <role>            User role (default: Member)

sorcha user update <user-id> [options]
  --name <name>            New display name
  --role <role>            New role
  --status <status>        New status

sorcha user delete <user-id>
  --force                  Skip confirmation

sorcha user suspend <user-id>
  --reason <text>          Suspension reason

sorcha user reactivate <user-id>

sorcha user revoke-tokens <user-id>
  --reason <text>          Revocation reason
```

#### Example Usage

```bash
# List users in organization
sorcha user list --org-id 7c9e6679-7425-40de-944b-e07fc1f90ae7

# Create administrator user
sorcha user create \
  --org-id 7c9e6679-7425-40de-944b-e07fc1f90ae7 \
  --email "admin@acme.com" \
  --name "Alice Administrator" \
  --role Administrator

# Get user details
sorcha user get 550e8400-e29b-41d4-a716-446655440000 --show-permissions

# Revoke all user tokens (force logout)
sorcha user revoke-tokens 550e8400-e29b-41d4-a716-446655440000 \
  --reason "Security incident - password compromised"
```

---

### 3. Tenant Service - Service Principal Management

#### Commands

```bash
sorcha principal list [options]
  --include-inactive       Include suspended principals
  --show-scopes            Show granted scopes

sorcha principal get <principal-id>
  --show-usage-stats       Include usage statistics

sorcha principal create [options]
  --name <name>            Service name (required)
  --scopes <scopes>        Comma-separated scopes (required)
  --description <text>     Service description

sorcha principal update <principal-id> [options]
  --name <name>            New service name
  --scopes <scopes>        New scopes (comma-separated)

sorcha principal suspend <principal-id>
  --reason <text>          Suspension reason

sorcha principal reactivate <principal-id>

sorcha principal delete <principal-id>
  --force                  Skip confirmation

sorcha principal rotate-secret <principal-id>
  --output-file <path>     Save new secret to file (secure)
```

#### Example Usage

```bash
# Create service principal
sorcha principal create \
  --name "Sorcha.Blueprint.Service" \
  --scopes "blueprints:write,wallets:sign,registers:write" \
  --description "Blueprint workflow execution service"

# Output:
# ✓ Service principal created successfully
#
# Client ID:     blueprint-svc-20251210
# Client Secret: sk_live_a7b3c2d1_4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b
#
# ⚠ SECURITY WARNING: This secret is only shown ONCE. Store it securely!

# Rotate service principal secret
sorcha principal rotate-secret a1b2c3d4-e5f6-7890-abcd-ef1234567890 \
  --output-file ~/.sorcha/secrets/blueprint-service.key

# List all service principals
sorcha principal list --show-scopes
```

---

### 4. Register Service - Register Management

#### Commands

```bash
sorcha register list [options]
  --org-id <guid>          Filter by organization
  --status <status>        Filter by status
  --include-inactive       Include inactive registers

sorcha register get <register-id>
  --show-participants      Include participant list
  --show-stats             Include transaction statistics

sorcha register create [options]
  --name <name>            Register name (required)
  --description <text>     Register description
  --org-id <guid>          Organization ID
  --publish                Publish immediately after creation

sorcha register update <register-id> [options]
  --name <name>            New register name
  --description <text>     New description

sorcha register delete <register-id>
  --force                  Skip confirmation

sorcha register stats <register-id>
  --window <duration>      Time window (1h/24h/7d/30d)

sorcha register participants <register-id>
  --show-roles             Include participant roles
```

#### Example Usage

```bash
# List all registers
sorcha register list

# Create new register
sorcha register create \
  --name "Supply Chain - Q4 2025" \
  --description "Supply chain transactions for Q4 2025" \
  --org-id 7c9e6679-7425-40de-944b-e07fc1f90ae7 \
  --publish

# Get register statistics
sorcha register stats reg_abc123 --window 24h

# Output:
# Register Statistics (Last 24 hours)
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Total Transactions:        1,245
# Average Block Size:        45 tx
# Average Block Time:        12.3s
# Total Participants:        12
# Active Participants:       8
```

---

### 5. Register Service - Transaction Viewer

#### Commands

```bash
sorcha tx list [options]
  --register-id <id>       Filter by register (required)
  --from <datetime>        Start time filter
  --to <datetime>          End time filter
  --participant <id>       Filter by participant
  --limit <n>              Limit results (default: 50)
  --offset <n>             Skip first N results

sorcha tx get <tx-id>
  --show-payload           Include transaction payload
  --show-signatures        Include signature details
  --show-metadata          Include metadata

sorcha tx search [options]
  --register-id <id>       Register to search (required)
  --query <text>           Search query (JSON path syntax)
  --blueprint-id <id>      Filter by blueprint
  --action-id <id>         Filter by action

sorcha tx verify <tx-id>
  --check-signatures       Verify all signatures
  --check-chain            Verify blockchain integrity

sorcha tx export [options]
  --register-id <id>       Register to export (required)
  --from <datetime>        Start time
  --to <datetime>          End time
  --format <format>        Export format (json/csv/excel)
  --output <path>          Output file path

sorcha tx timeline <tx-id>
  --show-events            Include lifecycle events
```

#### Example Usage

```bash
# List recent transactions in register
sorcha tx list --register-id reg_abc123 --limit 20

# Get transaction details
sorcha tx get tx_550e8400e29b41d4a716446655440000 --show-payload

# Search transactions by blueprint action
sorcha tx search \
  --register-id reg_abc123 \
  --blueprint-id supply-chain-v2 \
  --action-id 3

# Verify transaction integrity
sorcha tx verify tx_550e8400e29b41d4a716446655440000 \
  --check-signatures \
  --check-chain

# Export transactions to CSV
sorcha tx export \
  --register-id reg_abc123 \
  --from "2025-12-01" \
  --to "2025-12-10" \
  --format csv \
  --output ~/exports/december-transactions.csv
```

#### Output Examples

**Transaction List (Table):**
```
┌──────────────────────┬─────────────────────┬──────────────────┬─────────────┬────────┐
│ Transaction ID       │ Timestamp           │ Blueprint        │ Action      │ Status │
├──────────────────────┼─────────────────────┼──────────────────┼─────────────┼────────┤
│ tx_550e8400e29b...   │ 2025-12-10 15:30:45 │ supply-chain-v2  │ ship-goods  │ Sealed │
│ tx_661f9511f3ac...   │ 2025-12-10 15:28:12 │ supply-chain-v2  │ inspect     │ Sealed │
│ tx_772ga622g4bd...   │ 2025-12-10 15:25:03 │ invoice-approval │ approve     │ Sealed │
└──────────────────────┴─────────────────────┴──────────────────┴─────────────┴────────┘
```

**Transaction Details (JSON):**
```json
{
  "txId": "tx_550e8400e29b41d4a716446655440000",
  "timestamp": "2025-12-10T15:30:45Z",
  "blueprintId": "supply-chain-v2",
  "actionId": 3,
  "actionTitle": "Ship Goods",
  "sender": "warehouse@acme.com",
  "recipients": ["shipper@logistics.com", "buyer@retail.com"],
  "status": "Sealed",
  "blockHeight": 12345,
  "blockHash": "0x742d35Cc6634C0532925a3b844Bc9e7595f0bEb",
  "payload": {
    "shipmentId": "SHIP-2025-001234",
    "carrier": "FastShip Logistics",
    "trackingNumber": "1Z999AA10123456784",
    "estimatedDelivery": "2025-12-15T18:00:00Z"
  },
  "signatures": [
    {
      "participant": "warehouse@acme.com",
      "algorithm": "ED25519",
      "signature": "base64-encoded-signature",
      "verified": true
    }
  ]
}
```

---

### 6. Peer Service - Network Monitoring

#### Commands

```bash
sorcha peer list [options]
  --status <status>        Filter by status (connected/disconnected)
  --sort <field>           Sort by (name/uptime/latency)

sorcha peer get <peer-id>
  --show-history           Include connection history
  --show-metrics           Include performance metrics

sorcha peer topology
  --format <format>        Output format (tree/graph/json)

sorcha peer stats
  --window <duration>      Time window (1h/24h/7d)

sorcha peer health
  --check-connectivity     Test connectivity to all peers
  --check-consensus        Verify consensus state
```

#### Example Usage

```bash
# List all connected peers
sorcha peer list --status connected

# Get peer connection details
sorcha peer get peer_ws11qq2yz3rr4ss5tt6uu7vv8ww9xx0

# Display peer network topology
sorcha peer topology --format tree

# Output (tree format):
# Peer Network Topology
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# ├─ peer-node-01 (Primary)
# │  ├─ peer-node-03 (Connected, 12ms)
# │  ├─ peer-node-05 (Connected, 8ms)
# │  └─ peer-node-07 (Connected, 15ms)
# ├─ peer-node-02 (Secondary)
# │  ├─ peer-node-04 (Connected, 10ms)
# │  └─ peer-node-06 (Connected, 9ms)
# └─ peer-node-08 (Standby)

# Check network health
sorcha peer health --check-connectivity --check-consensus
```

#### Output Examples

**Peer List (Table):**
```
┌──────────────────────────────────┬─────────────┬────────────┬─────────┬──────────┐
│ Peer ID                          │ Status      │ Uptime     │ Latency │ Version  │
├──────────────────────────────────┼─────────────┼────────────┼─────────┼──────────┤
│ peer_ws11qq2yz3rr4ss5tt6uu7vv... │ Connected   │ 15d 6h 23m │ 12ms    │ v1.5.2   │
│ peer_xs22rr3za4ss5tt6uu7vv8ww... │ Connected   │ 8d 2h 45m  │ 8ms     │ v1.5.2   │
│ peer_yt33ss4ab5tt6uu7vv8ww9xx... │ Connected   │ 3d 18h 12m │ 15ms    │ v1.5.1   │
│ peer_zu44tt5bc6uu7vv8ww9xx0yy... │ Disconnected│ -          │ -       │ v1.4.8   │
└──────────────────────────────────┴─────────────┴────────────┴─────────┴──────────┘
```

---

### 7. Authentication & Credential Management

#### Commands

```bash
sorcha auth login [options]
  --profile <name>         Profile name (default: default)
  --username <email>       User email
  --password <password>    User password (prompted if not provided)
  --service                Login as service principal
  --client-id <id>         Service client ID (if --service)
  --client-secret <secret> Service client secret (if --service)
  --save                   Save credentials to profile

sorcha auth logout [options]
  --profile <name>         Profile to clear (default: current)
  --all                    Logout all profiles

sorcha auth whoami
  --show-permissions       Include effective permissions
  --show-token             Display access token (for debugging)

sorcha auth token
  --refresh                Force token refresh
  --introspect             Introspect current token

sorcha auth profiles
  --list                   List all profiles
  --current                Show current active profile
```

#### Example Usage

```bash
# Login as user (interactive password prompt)
sorcha auth login --username admin@acme.com --save

# Login as service principal
sorcha auth login --service \
  --client-id blueprint-svc-20251210 \
  --client-secret sk_live_abc123... \
  --profile production \
  --save

# Check current authentication
sorcha auth whoami

# Output:
# Current User
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# Name:           Alice Administrator
# Email:          admin@acme.com
# Organization:   Acme Corporation (7c9e6679-7425...)
# Role:           Administrator
# Token Type:     User
# Token Expires:  2025-12-10 16:30:45 (in 42 minutes)
# Profile:        default

# Force token refresh
sorcha auth token --refresh
```

---

### 8. Configuration Management

#### Commands

```bash
sorcha config init [options]
  --profile <name>         Profile name (default: default)
  --tenant-url <url>       Tenant service URL
  --register-url <url>     Register service URL
  --peer-url <url>         Peer service URL
  --wallet-url <url>       Wallet service URL

sorcha config list
  --profiles               List all profiles
  --show-urls              Include service URLs

sorcha config get <key>
  --profile <name>         Profile to query

sorcha config set <key> <value>
  --profile <name>         Profile to update

sorcha config profile [options]
  --use <name>             Switch to profile
  --list                   List all profiles
  --delete <name>          Delete profile

sorcha config export [options]
  --profile <name>         Profile to export (default: all)
  --output <path>          Output file path
  --include-secrets        Include credentials (encrypted)

sorcha config import <path>
  --profile <name>         Import as profile name
  --merge                  Merge with existing config
```

#### Example Usage

```bash
# Initialize development profile
sorcha config init --profile dev \
  --tenant-url https://localhost:7080 \
  --register-url https://localhost:7081 \
  --peer-url https://localhost:7082

# Initialize production profile
sorcha config init --profile production \
  --tenant-url https://tenant.sorcha.io \
  --register-url https://register.sorcha.io \
  --peer-url https://peer.sorcha.io

# Switch to production profile
sorcha config profile --use production

# List all profiles
sorcha config list --profiles

# Output:
# Available Profiles
# ━━━━━━━━━━━━━━━━━━━━━━━━━━━━
# * production    (active)
#   dev
#   staging

# Set configuration value
sorcha config set output.format json --profile dev

# Export configuration
sorcha config export --output ~/.sorcha-backup.json
```

#### Configuration File Structure

**Location:** `~/.sorcha/config.json`

```json
{
  "currentProfile": "production",
  "profiles": {
    "dev": {
      "services": {
        "tenant": {
          "baseUrl": "https://localhost:7080",
          "timeout": 30
        },
        "register": {
          "baseUrl": "https://localhost:7081",
          "timeout": 60
        },
        "peer": {
          "baseUrl": "https://localhost:7082",
          "timeout": 30
        }
      },
      "authentication": {
        "type": "service",
        "clientId": "dev-cli",
        "tokenCachePath": "~/.sorcha/tokens/dev.token"
      },
      "output": {
        "format": "table",
        "colorEnabled": true
      }
    },
    "production": {
      "services": {
        "tenant": {
          "baseUrl": "https://tenant.sorcha.io",
          "timeout": 30
        },
        "register": {
          "baseUrl": "https://register.sorcha.io",
          "timeout": 60
        },
        "peer": {
          "baseUrl": "https://peer.sorcha.io",
          "timeout": 30
        }
      },
      "authentication": {
        "type": "service",
        "clientId": "prod-cli",
        "tokenCachePath": "~/.sorcha/tokens/production.token"
      },
      "output": {
        "format": "json",
        "colorEnabled": false
      }
    }
  }
}
```

---

## Installation & Distribution

### .NET Global Tool

**Installation:**
```bash
# Install from NuGet (published package)
dotnet tool install -g sorcha.cli

# Update to latest version
dotnet tool update -g sorcha.cli

# Uninstall
dotnet tool uninstall -g sorcha.cli
```

**Local Development Installation:**
```bash
# Pack and install from local source
cd src/Apps/Sorcha.Cli
dotnet pack
dotnet tool install -g --add-source ./nupkg sorcha.cli
```

### Package Metadata

**NuGet Package:** `Sorcha.Cli`

**Tool Command:** `sorcha`

**.csproj Configuration:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>sorcha</ToolCommandName>
    <PackageId>Sorcha.Cli</PackageId>
    <Version>1.0.0</Version>
    <Authors>Sorcha Team</Authors>
    <Description>Administrative command-line tool for Sorcha decentralised register platform</Description>
    <PackageTags>blockchain;distributed-ledger;cli;admin</PackageTags>
    <PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
  </PropertyGroup>
</Project>
```

---

## Technical Implementation

### Project Structure

```
src/Apps/Sorcha.Cli/
├── Commands/
│   ├── OrgCommands.cs          # Organization management commands
│   ├── UserCommands.cs         # User management commands
│   ├── PrincipalCommands.cs    # Service principal commands
│   ├── RegisterCommands.cs     # Register management commands
│   ├── TransactionCommands.cs  # Transaction viewer commands
│   ├── PeerCommands.cs         # Peer network commands
│   ├── AuthCommands.cs         # Authentication commands
│   └── ConfigCommands.cs       # Configuration commands
├── Services/
│   ├── ITenantServiceClient.cs # Tenant service API client
│   ├── IRegisterServiceClient.cs # Register service API client
│   ├── IPeerServiceClient.cs   # Peer service API client
│   ├── AuthenticationService.cs # Token management
│   ├── ConfigurationService.cs # Profile management
│   └── OutputFormatter.cs      # Display formatting (table/json/csv)
├── Models/
│   ├── CliConfiguration.cs     # Configuration models
│   ├── Profile.cs              # Profile model
│   └── [API DTOs]              # Request/response models
├── Infrastructure/
│   ├── HttpClientExtensions.cs # Resilience policies
│   ├── CredentialManager.cs    # Secure credential storage
│   └── TokenCache.cs           # Token caching
├── Program.cs                  # Entry point
└── Sorcha.Cli.csproj

tests/Sorcha.Cli.Tests/
├── Commands/
│   ├── OrgCommandsTests.cs
│   ├── UserCommandsTests.cs
│   └── [Other command tests]
├── Services/
│   ├── AuthenticationServiceTests.cs
│   └── ConfigurationServiceTests.cs
└── Integration/
    └── E2ETests.cs              # End-to-end integration tests
```

### Interactive Console Mode Implementation

**REPL Framework:**

The interactive console uses a custom REPL (Read-Eval-Print Loop) implementation with the following components:

```csharp
// Interactive/ConsoleHost.cs
public class ConsoleHost
{
    private readonly IServiceProvider _services;
    private readonly ConsoleContext _context;
    private readonly CommandHistory _history;
    private readonly TabCompleter _completer;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        DisplayWelcomeBanner();

        // Prompt for authentication if not cached
        if (!await IsAuthenticatedAsync())
        {
            await PromptAuthenticationAsync();
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Display prompt with context (e.g., "sorcha[acme]>")
                var prompt = BuildPrompt();

                // Read command with history and tab completion
                var input = await ReadLineWithCompletionAsync(prompt);

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                // Handle special commands (exit, help, clear, etc.)
                if (await HandleSpecialCommandAsync(input))
                    continue;

                // Save to history
                _history.Add(input);

                // Parse and execute command
                await ExecuteCommandAsync(input);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
            }
        }

        await CleanupAsync();
    }

    private async Task<string> ReadLineWithCompletionAsync(string prompt)
    {
        var readLine = new ReadLineBuilder()
            .WithHistory(_history)
            .WithCompletion(_completer)
            .WithPrompt(prompt)
            .WithColors(new ColorConfig
            {
                PromptColor = Color.Cyan1,
                InputColor = Color.White
            })
            .Build();

        return await readLine.ReadAsync();
    }
}

// Interactive/ConsoleContext.cs
public class ConsoleContext
{
    public string? CurrentProfile { get; set; }
    public Guid? CurrentOrganizationId { get; set; }
    public string? CurrentOrganizationName { get; set; }
    public Guid? CurrentRegisterId { get; set; }
    public string? CurrentRegisterName { get; set; }
    public AuthenticationState AuthState { get; set; }

    public string BuildPrompt()
    {
        if (CurrentOrganizationName != null)
            return $"[cyan1]sorcha[/][yellow][[{CurrentOrganizationName}]][/]> ";

        return "[cyan1]sorcha[/]> ";
    }
}

// Interactive/TabCompleter.cs
public class TabCompleter : IAutoCompleteHandler
{
    private readonly IServiceProvider _services;
    private readonly ConsoleContext _context;

    public async Task<string[]> GetSuggestionsAsync(string text, int index)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
            return GetTopLevelCommands();

        if (tokens.Length == 1)
        {
            // Complete top-level command
            return GetTopLevelCommands()
                .Where(c => c.StartsWith(tokens[0], StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (tokens.Length == 2)
        {
            // Complete subcommand (e.g., "org <TAB>")
            return GetSubCommands(tokens[0])
                .Where(c => c.StartsWith(tokens[1], StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        // Complete IDs based on command context
        if (tokens.Length >= 3 && tokens[1] == "get")
        {
            return await GetResourceIdsAsync(tokens[0]);
        }

        // Complete option flags
        if (text.EndsWith("--"))
        {
            return GetOptionsForCommand(tokens[0], tokens[1]);
        }

        return Array.Empty<string>();
    }

    private async Task<string[]> GetResourceIdsAsync(string resource)
    {
        return resource switch
        {
            "org" => await GetOrganizationIdsAsync(),
            "user" => await GetUserIdsAsync(),
            "register" => await GetRegisterIdsAsync(),
            _ => Array.Empty<string>()
        };
    }

    private async Task<string[]> GetOrganizationIdsAsync()
    {
        var client = _services.GetRequiredService<ITenantServiceClient>();
        var orgs = await client.ListOrganizationsAsync();

        return orgs.Select(o => $"{o.Id}  ({o.Name})").ToArray();
    }
}

// Interactive/CommandHistory.cs
public class CommandHistory : IEnumerable<string>
{
    private readonly List<string> _history = new();
    private readonly string _historyFile;
    private int _currentIndex = -1;

    public CommandHistory()
    {
        _historyFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".sorcha",
            "history.txt");

        LoadHistory();
    }

    public void Add(string command)
    {
        _history.Add(command);
        _currentIndex = _history.Count;
        SaveHistory();
    }

    public string? GetPrevious()
    {
        if (_history.Count == 0) return null;

        _currentIndex = Math.Max(0, _currentIndex - 1);
        return _history[_currentIndex];
    }

    public string? GetNext()
    {
        if (_history.Count == 0) return null;

        _currentIndex = Math.Min(_history.Count - 1, _currentIndex + 1);
        return _history[_currentIndex];
    }

    private void LoadHistory()
    {
        if (!File.Exists(_historyFile)) return;

        var lines = File.ReadAllLines(_historyFile);
        _history.AddRange(lines.TakeLast(500)); // Keep last 500 commands
    }

    private void SaveHistory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
        File.WriteAllLines(_historyFile, _history.TakeLast(500));
    }

    public IEnumerator<string> GetEnumerator() => _history.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

**Interactive Mode Entry Point:**

```csharp
// Program.cs
static async Task<int> Main(string[] args)
{
    var services = ConfigureServices();

    // If no arguments, start interactive console
    if (args.Length == 0 || args[0] == "console")
    {
        var console = services.GetRequiredService<ConsoleHost>();
        await console.RunAsync(CancellationToken.None);
        return 0;
    }

    // Otherwise, execute as single command (flag-based mode)
    var rootCommand = BuildRootCommand(services);
    return await rootCommand.InvokeAsync(args);
}
```

---

### Token Cache Implementation

**Token Cache Manager:**

```csharp
// Infrastructure/TokenCache.cs
public class TokenCache
{
    private readonly string _cacheDirectory;
    private readonly IEncryptionProvider _encryption;

    public TokenCache()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".sorcha",
            "tokens");

        Directory.CreateDirectory(_cacheDirectory);

        // Use OS-specific encryption
        _encryption = CreateEncryptionProvider();
    }

    public async Task SetAsync(string profile, TokenResponse token)
    {
        var cacheEntry = new TokenCacheEntry
        {
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            TokenType = token.TokenType,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn),
            IssuedAt = DateTimeOffset.UtcNow,
            Profile = profile
        };

        var json = JsonSerializer.Serialize(cacheEntry);
        var encrypted = await _encryption.EncryptAsync(json);

        var cacheFile = GetCacheFilePath(profile);
        await File.WriteAllBytesAsync(cacheFile, encrypted);

        // Set restrictive permissions (Unix)
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(cacheFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public async Task<string?> GetAsync(string profile)
    {
        var cacheFile = GetCacheFilePath(profile);

        if (!File.Exists(cacheFile))
            return null;

        try
        {
            var encrypted = await File.ReadAllBytesAsync(cacheFile);
            var json = await _encryption.DecryptAsync(encrypted);
            var entry = JsonSerializer.Deserialize<TokenCacheEntry>(json);

            if (entry == null || entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                // Token expired
                return null;
            }

            // Check if token expiring soon (within 5 minutes)
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5))
            {
                // Attempt refresh
                return await TryRefreshTokenAsync(entry);
            }

            return entry.AccessToken;
        }
        catch
        {
            // Cache corrupted, delete and return null
            File.Delete(cacheFile);
            return null;
        }
    }

    public async Task ClearAsync(string profile)
    {
        var cacheFile = GetCacheFilePath(profile);
        if (File.Exists(cacheFile))
        {
            File.Delete(cacheFile);
        }
    }

    public async Task ClearAllAsync()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    private async Task<string?> TryRefreshTokenAsync(TokenCacheEntry entry)
    {
        if (string.IsNullOrEmpty(entry.RefreshToken))
            return null;

        try
        {
            var tenantClient = /* get from DI */;
            var newToken = await tenantClient.RefreshTokenAsync(entry.RefreshToken);

            // Cache new token
            await SetAsync(entry.Profile, newToken);

            return newToken.AccessToken;
        }
        catch
        {
            // Refresh failed, delete cache
            await ClearAsync(entry.Profile);
            return null;
        }
    }

    private IEncryptionProvider CreateEncryptionProvider()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDpapiEncryption();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return new MacOsKeychainEncryption();
        }
        else
        {
            // Linux - use libsecret or encrypted file fallback
            return new LinuxSecretServiceEncryption();
        }
    }

    private string GetCacheFilePath(string profile) =>
        Path.Combine(_cacheDirectory, $"{profile}.token");
}

// Infrastructure/Encryption/IEncryptionProvider.cs
public interface IEncryptionProvider
{
    Task<byte[]> EncryptAsync(string plaintext);
    Task<string> DecryptAsync(byte[] ciphertext);
}

// Infrastructure/Encryption/WindowsDpapiEncryption.cs
public class WindowsDpapiEncryption : IEncryptionProvider
{
    public Task<byte[]> EncryptAsync(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(
            data,
            entropy: null, // Could use machine-specific entropy
            DataProtectionScope.CurrentUser);

        return Task.FromResult(encrypted);
    }

    public Task<string> DecryptAsync(byte[] ciphertext)
    {
        var decrypted = ProtectedData.Unprotect(
            ciphertext,
            entropy: null,
            DataProtectionScope.CurrentUser);

        return Task.FromResult(Encoding.UTF8.GetString(decrypted));
    }
}

// Infrastructure/Encryption/MacOsKeychainEncryption.cs
public class MacOsKeychainEncryption : IEncryptionProvider
{
    public async Task<byte[]> EncryptAsync(string plaintext)
    {
        // Store in macOS Keychain
        var accountName = $"sorcha-cli-{Environment.UserName}";

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            Arguments = $"add-generic-password -s sorcha-cli -a {accountName} -w {plaintext} -U",
            RedirectStandardOutput = true,
            UseShellExecute = false
        });

        await process!.WaitForExitAsync();

        // Return placeholder (actual value stored in Keychain)
        return Encoding.UTF8.GetBytes($"keychain:{accountName}");
    }

    public async Task<string> DecryptAsync(byte[] ciphertext)
    {
        var reference = Encoding.UTF8.GetString(ciphertext);
        var accountName = reference.Replace("keychain:", "");

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "security",
            Arguments = $"find-generic-password -s sorcha-cli -a {accountName} -w",
            RedirectStandardOutput = true,
            UseShellExecute = false
        });

        var output = await process!.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.Trim();
    }
}
```

**Authentication Service with Caching:**

```csharp
// Services/AuthenticationService.cs
public class AuthenticationService
{
    private readonly ITenantServiceClient _tenantClient;
    private readonly TokenCache _tokenCache;
    private readonly IConfiguration _config;

    public async Task<AuthenticationResult> LoginAsync(
        string username,
        string password,
        string profile,
        bool saveToken = false)
    {
        // Request token from Tenant Service
        var tokenResponse = await _tenantClient.AuthenticateAsync(new
        {
            username,
            password
        });

        // Cache token if requested
        if (saveToken)
        {
            await _tokenCache.SetAsync(profile, tokenResponse);
        }

        return new AuthenticationResult
        {
            Success = true,
            TokenType = "User",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        };
    }

    public async Task<string?> GetAccessTokenAsync(string profile)
    {
        // Try cache first
        var cachedToken = await _tokenCache.GetAsync(profile);
        if (!string.IsNullOrEmpty(cachedToken))
            return cachedToken;

        return null;
    }

    public async Task<bool> IsAuthenticatedAsync(string profile)
    {
        var token = await GetAccessTokenAsync(profile);
        return !string.IsNullOrEmpty(token);
    }
}
```

---

### Key Dependencies

```xml
<ItemGroup>
  <!-- CLI Framework -->
  <PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
  <PackageReference Include="Spectre.Console" Version="0.49.1" />
  <PackageReference Include="ReadLine" Version="2.0.1" /> <!-- REPL readline with history/completion -->

  <!-- HTTP Clients -->
  <PackageReference Include="Refit" Version="7.0.0" />
  <PackageReference Include="Refit.HttpClientFactory" Version="7.0.0" />

  <!-- Configuration -->
  <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />

  <!-- Dependency Injection -->
  <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
  <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="10.0.0" />

  <!-- Logging -->
  <PackageReference Include="Serilog" Version="3.1.1" />
  <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
  <PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />

  <!-- Security -->
  <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.0" />
</ItemGroup>
```

### Authentication Flow

```csharp
public class AuthenticationService
{
    private readonly ITenantServiceClient _tenantClient;
    private readonly TokenCache _tokenCache;
    private readonly IConfiguration _config;

    public async Task<AuthenticationResult> LoginAsync(
        string username,
        string password,
        string profile)
    {
        // 1. Request user token from Tenant Service
        var tokenResponse = await _tenantClient.AuthenticateAsync(new
        {
            username,
            password
        });

        // 2. Cache token securely
        await _tokenCache.SetAsync(profile, tokenResponse.AccessToken, tokenResponse.ExpiresIn);

        // 3. Return authentication result
        return new AuthenticationResult
        {
            Success = true,
            TokenType = "User",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        };
    }

    public async Task<AuthenticationResult> LoginAsServiceAsync(
        string clientId,
        string clientSecret,
        string profile)
    {
        // 1. Request service token using OAuth2 client credentials
        var tokenResponse = await _tenantClient.GetServiceTokenAsync(new
        {
            grant_type = "client_credentials",
            client_id = clientId,
            client_secret = clientSecret
        });

        // 2. Cache token
        await _tokenCache.SetAsync(profile, tokenResponse.AccessToken, tokenResponse.ExpiresIn);

        return new AuthenticationResult
        {
            Success = true,
            TokenType = "Service",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        };
    }

    public async Task<string> GetAccessTokenAsync(string profile)
    {
        // Check cache first
        var cachedToken = await _tokenCache.GetAsync(profile);
        if (!string.IsNullOrEmpty(cachedToken))
            return cachedToken;

        throw new InvalidOperationException("Not authenticated. Run 'sorcha auth login' first.");
    }
}
```

### HTTP Client Setup

```csharp
// Program.cs - Service registration
var services = new ServiceCollection();

// Configure Tenant Service client
services.AddRefitClient<ITenantServiceClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var profile = config["currentProfile"] ?? "default";
        var baseUrl = config[$"profiles:{profile}:services:tenant:baseUrl"];
        client.BaseAddress = new Uri(baseUrl);
    })
    .AddHttpMessageHandler<AuthenticationHandler>()
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

// Register authentication handler (adds Bearer token to requests)
services.AddTransient<AuthenticationHandler>();

// Polly resilience policies
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}
```

### Output Formatting

```csharp
public class OutputFormatter
{
    public void Display<T>(T data, OutputFormat format, OutputOptions options)
    {
        switch (format)
        {
            case OutputFormat.Table:
                DisplayAsTable(data, options);
                break;
            case OutputFormat.Json:
                DisplayAsJson(data, options);
                break;
            case OutputFormat.Csv:
                DisplayAsCsv(data, options);
                break;
            case OutputFormat.Yaml:
                DisplayAsYaml(data, options);
                break;
        }
    }

    private void DisplayAsTable<T>(T data, OutputOptions options)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);

        if (data is IEnumerable<object> items)
        {
            // Dynamically create columns from properties
            var properties = typeof(T).GetGenericArguments()[0].GetProperties();
            foreach (var prop in properties)
            {
                table.AddColumn(prop.Name);
            }

            // Add rows
            foreach (var item in items)
            {
                var values = properties.Select(p => p.GetValue(item)?.ToString() ?? "");
                table.AddRow(values.ToArray());
            }
        }

        AnsiConsole.Write(table);
    }

    private void DisplayAsJson<T>(T data, OutputOptions options)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = options.PrettyPrint,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (options.ColorEnabled)
        {
            AnsiConsole.Write(new JsonText(json));
        }
        else
        {
            Console.WriteLine(json);
        }
    }
}
```

---

## Security Considerations

### 1. Credential Storage

**Secure Storage:**
- Use `System.Security.Cryptography.ProtectedData` on Windows (DPAPI)
- Use macOS Keychain on macOS (`security` command-line tool)
- Use Linux Secret Service on Linux (libsecret)
- Fall back to encrypted file storage with user-specific key

**Implementation:**
```csharp
public class CredentialManager
{
    public void StoreCredential(string profile, string key, string value)
    {
        if (OperatingSystem.IsWindows())
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                entropy: GetProfileEntropy(profile),
                DataProtectionScope.CurrentUser);
            File.WriteAllBytes(GetCredentialPath(profile, key), encrypted);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Use macOS Keychain
            Process.Start("security", $"add-generic-password -s sorcha-cli-{profile} -a {key} -w {value}");
        }
        else
        {
            // Linux - use Secret Service or encrypted file
            StoreEncryptedFile(profile, key, value);
        }
    }
}
```

### 2. Token Management

**Best Practices:**
- Store tokens in memory when possible (short-lived CLI sessions)
- Cache tokens to disk with encryption (for repeated commands)
- Clear token cache on logout
- Automatic token refresh before expiry
- Detect token revocation and prompt re-authentication

### 3. HTTPS/TLS Enforcement

**Requirements:**
- All API calls use HTTPS only
- Certificate validation enabled
- Support for custom CA certificates (corporate environments)
- Option to disable cert validation for local development (with warning)

```bash
# Local development with self-signed cert
sorcha --insecure org list

# WARNING: Certificate validation disabled. Do not use in production!
```

### 4. Audit Logging

**Local Audit Log:**
- Log all commands executed
- Include timestamp, user, profile, command, arguments
- Store in `~/.sorcha/audit.log`
- Rotate logs (max 10MB, keep 7 days)

**Format:**
```json
{
  "timestamp": "2025-12-10T15:30:45Z",
  "profile": "production",
  "user": "admin@acme.com",
  "command": "sorcha org delete 7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "success": true,
  "duration": "1.234s"
}
```

---

## Testing Strategy

### Unit Tests

**Target Coverage:** >85%

**Test Categories:**
- Command parsing and validation
- Authentication service logic
- Configuration management
- Output formatting
- HTTP client interactions (mocked)

**Example:**
```csharp
[Fact]
public async Task OrgCreate_ShouldCallTenantService_WithCorrectParameters()
{
    // Arrange
    var mockClient = new Mock<ITenantServiceClient>();
    var command = new OrgCreateCommand(mockClient.Object);

    // Act
    await command.ExecuteAsync(new OrgCreateOptions
    {
        Name = "Test Corp",
        Subdomain = "testcorp"
    });

    // Assert
    mockClient.Verify(c => c.CreateOrganizationAsync(It.Is<CreateOrgRequest>(r =>
        r.Name == "Test Corp" && r.Subdomain == "testcorp")), Times.Once);
}
```

### Integration Tests

**Scope:**
- E2E command execution against test services
- Authentication flow (service principal)
- Configuration profile switching
- Output format validation

**Test Environment:**
- Use Testcontainers for service dependencies
- Seed test data (organizations, users, registers)
- Clean up after test runs

### Manual Testing Scenarios

**Checklist:**
- [ ] Install as global tool
- [ ] Initialize configuration profiles (dev, prod)
- [ ] Login as user and service principal
- [ ] Create organization, user, service principal
- [ ] Create register and submit transactions
- [ ] View transaction list and details
- [ ] Monitor peer network status
- [ ] Switch between profiles
- [ ] Export/import configuration
- [ ] Test all output formats (table, JSON, CSV)
- [ ] Verify error handling and messages
- [ ] Test on Windows, macOS, Linux

---

## Acceptance Criteria

### Must Have (P0)

- [x] **Tenant Service Integration**
  - [x] Organization CRUD commands
  - [x] User CRUD commands
  - [x] Service principal CRUD commands
  - [x] Authentication (user + service principal)

- [x] **Register Service Integration**
  - [x] Register CRUD commands
  - [x] Transaction list command
  - [x] Transaction details command
  - [x] Transaction search command

- [x] **Peer Service Integration**
  - [x] Peer list command
  - [x] Peer details command
  - [x] Network topology command
  - [x] Health check command

- [x] **Configuration Management**
  - [x] Profile management (create, switch, delete)
  - [x] Multi-environment support
  - [x] Credential storage

- [x] **Authentication & Token Management**
  - [x] Token caching with encryption (OS-specific)
  - [x] Automatic token refresh before expiry
  - [x] Multi-profile token storage
  - [x] Token cache management commands

- [x] **Operation Modes**
  - [x] Flag-based mode for scripts/AI agents
  - [x] Non-interactive execution with proper exit codes
  - [x] JSON output for parsing

- [x] **Output Formatting**
  - [x] Table format (default)
  - [x] JSON format
  - [x] CSV format

- [x] **Cross-Platform Support**
  - [x] Windows
  - [x] macOS
  - [x] Linux

- [x] **Packaging & Distribution**
  - [x] .NET global tool
  - [x] NuGet package

### Should Have (P1)

- [ ] **Interactive Console Mode (REPL)**
  - [ ] REPL loop with prompt and command execution
  - [ ] Command history (Up/Down arrows, persistent storage)
  - [ ] Tab completion for commands, subcommands, and resource IDs
  - [ ] Context awareness (current org, register, profile in prompt)
  - [ ] Special commands (help, clear, status, use, exit)
  - [ ] Persistent authentication for session
  - [ ] Multi-line input support

- [ ] **Advanced Transaction Features**
  - [ ] Transaction verification (signatures + chain)
  - [ ] Transaction export (JSON/CSV/Excel)
  - [ ] Transaction timeline view

- [ ] **Enhanced Peer Monitoring**
  - [ ] Peer performance metrics
  - [ ] Consensus status
  - [ ] Network health dashboard

- [ ] **Scripting Support**
  - [ ] Exit codes for error handling
  - [ ] Non-interactive mode (--yes flag)
  - [ ] Environment variable support for credentials

- [ ] **Configuration**
  - [ ] Configuration export/import
  - [ ] YAML output format

### Could Have (P2)

- [ ] **Wallet Service Integration** (future)
  - [ ] Wallet CRUD commands
  - [ ] Key management commands
  - [ ] Transaction signing commands

- [ ] **Interactive Mode**
  - [ ] REPL shell (sorcha shell)
  - [ ] Command history
  - [ ] Tab completion

- [ ] **Monitoring & Alerts**
  - [ ] Watch mode (continuous monitoring)
  - [ ] Alerting on events
  - [ ] Log streaming

---

## Future Enhancements

### Phase 1 (Q1 2026): Advanced Features

- **Interactive Shell Mode:** REPL with tab completion, history, context-aware help
- **Transaction Replay:** Replay transaction submission for testing
- **Bulk Operations:** Batch import/export of organizations, users
- **Configuration Templates:** Pre-configured profiles for common deployment patterns

### Phase 2 (Q2 2026): Monitoring & Observability

- **Dashboard Mode:** Real-time terminal dashboard (Spectre.Console Live)
- **Alert Rules:** Define alert rules for network events
- **Metrics Export:** Export metrics to Prometheus/Grafana
- **Log Aggregation:** Stream logs from services

### Phase 3 (Q3 2026): Automation & DevOps

- **CI/CD Integration:** GitHub Actions, Azure DevOps tasks
- **Terraform Provider:** Infrastructure as Code support
- **Ansible Modules:** Configuration management
- **API Code Generation:** Generate client code from OpenAPI specs

---

## Implementation Plan

### Sprint 1 (Week 1-2): Foundation

**Tasks:**
1. Create project structure (Sorcha.Cli, tests)
2. Set up System.CommandLine framework
3. Implement configuration management (profiles, file storage)
4. Implement authentication service (token management)
5. Create base command classes

**Deliverables:**
- CLI project compiles and installs as global tool
- Configuration profiles work
- Authentication service implemented

---

### Sprint 2 (Week 3-4): Tenant Service Commands

**Tasks:**
1. Implement Tenant Service API client (Refit)
2. Create organization commands (list, get, create, update, delete)
3. Create user commands (list, get, create, update, delete)
4. Create service principal commands (list, get, create, delete, rotate-secret)
5. Implement table output formatter

**Deliverables:**
- All Tenant Service commands functional
- Table output works
- Unit tests for commands

---

### Sprint 3 (Week 5-6): Register & Transaction Commands

**Tasks:**
1. Implement Register Service API client
2. Create register commands (list, get, create, update, delete)
3. Create transaction commands (list, get, search)
4. Implement JSON and CSV output formatters
5. Add pagination support

**Deliverables:**
- Register and transaction commands functional
- Multiple output formats work
- Integration tests pass

---

### Sprint 4 (Week 7-8): Peer Service & Polish

**Tasks:**
1. Implement Peer Service API client
2. Create peer commands (list, get, topology, health)
3. Implement configuration export/import
4. Add comprehensive error handling
5. Write user documentation
6. Publish NuGet package

**Deliverables:**
- All commands complete
- Documentation complete
- CLI published to NuGet

---

## Documentation Requirements

### User Documentation

**README.md:**
- Installation instructions
- Quick start guide
- Command reference
- Configuration guide
- Troubleshooting

**Wiki/Docs Site:**
- Complete command reference
- Tutorial: Managing organizations
- Tutorial: Viewing transactions
- Tutorial: Monitoring peer network
- Best practices for automation

### Developer Documentation

**CONTRIBUTING.md:**
- Development setup
- Adding new commands
- Testing guidelines
- Release process

**Architecture.md:**
- System architecture
- Command routing
- Authentication flow
- Output formatting

---

## Appendix A: Command Reference Summary

### Quick Reference

```bash
# Organizations
sorcha org list
sorcha org get <id>
sorcha org create --name <name> --subdomain <subdomain>
sorcha org delete <id>

# Users
sorcha user list --org-id <guid>
sorcha user get <id>
sorcha user create --org-id <guid> --email <email> --name <name>
sorcha user delete <id>

# Service Principals
sorcha principal list
sorcha principal get <id>
sorcha principal create --name <name> --scopes <scopes>
sorcha principal rotate-secret <id>

# Registers
sorcha register list
sorcha register get <id>
sorcha register create --name <name>

# Transactions
sorcha tx list --register-id <id>
sorcha tx get <id>
sorcha tx search --register-id <id> --query <query>

# Peers
sorcha peer list
sorcha peer get <id>
sorcha peer topology
sorcha peer health

# Authentication
sorcha auth login --username <email>
sorcha auth login --service --client-id <id> --client-secret <secret>
sorcha auth logout
sorcha auth whoami

# Configuration
sorcha config init --profile <name>
sorcha config profile --use <name>
sorcha config list
```

---

## Appendix B: Exit Codes

| Code | Description | Example Scenario |
|------|-------------|------------------|
| 0 | Success | Command executed successfully |
| 1 | General error | Unspecified error occurred |
| 2 | Authentication error | Invalid credentials, token expired |
| 3 | Authorization error | Insufficient permissions |
| 4 | Validation error | Invalid command arguments |
| 5 | Not found | Resource not found (organization, user, register) |
| 6 | Configuration error | Invalid profile, missing configuration |
| 7 | Network error | Service unreachable, timeout |
| 8 | Service error | API returned 500 Internal Server Error |

---

## Appendix C: Environment Variables

```bash
# Profile override
SORCHA_PROFILE=production

# Service URLs (override config file)
SORCHA_TENANT_URL=https://tenant.sorcha.io
SORCHA_REGISTER_URL=https://register.sorcha.io
SORCHA_PEER_URL=https://peer.sorcha.io

# Authentication
SORCHA_CLIENT_ID=cli-service-principal
SORCHA_CLIENT_SECRET=sk_live_abc123...

# Output
SORCHA_OUTPUT_FORMAT=json
SORCHA_NO_COLOR=1

# Logging
SORCHA_LOG_LEVEL=Debug
SORCHA_LOG_FILE=~/.sorcha/debug.log

# Configuration directory override
SORCHA_CONFIG_DIR=~/custom-config
```

---

**Document Status:** Draft - Ready for Implementation

**Next Steps:**
1. Review specification with team
2. Create initial project structure
3. Set up CI/CD pipeline
4. Begin Sprint 1 implementation

**Questions/Clarifications Needed:**
- Priority of wallet service integration (Phase 1 or defer?)
- NuGet.org publishing process and credentials
- Target release date for v1.0
- Support policy (LTS releases?)

---

**Version History:**
- v1.0 (2025-12-10): Initial specification
