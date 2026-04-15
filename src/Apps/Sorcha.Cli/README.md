# Sorcha CLI - Administrative Tool

**Version:** 1.0.5-build.2153+08a162d9ce
**Status:** 🟡 **In Active Development** (60% Feature Complete)
**Last Updated:** 2026-01-05

The Sorcha CLI is a cross-platform command-line interface for managing the Sorcha decentralised register platform. It provides comprehensive commands for authentication, organization management, wallet operations, transaction handling, register administration, and peer network monitoring.

## 📊 **Current Implementation Status**

| Component | Status | Completion |
|-----------|--------|-----------|
| **Foundation** (Config, Auth, Token Cache) | ✅ Complete | 100% |
| **Authentication Commands** | ✅ Complete | 100% |
| **Configuration Commands** | ✅ Complete | 100% |
| **List/Get Operations** | 🟢 Implemented | 80% |
| **Create/Update/Delete Operations** | 🟡 Partial | 20% |
| **Advanced Features** (Export, Crypto, Monitoring) | 🔴 Not Started | 0% |
| **Interactive REPL Mode** | 🔴 Not Started | 0% |

**What works:** Authentication, configuration, viewing organizations/users/registers/wallets/transactions
**What doesn't work yet:** Creating/updating entities, transaction export, wallet signing, peer monitoring

See [CLI-CAPABILITIES-AUDIT.md](CLI-CAPABILITIES-AUDIT.md) for detailed status.

## Table of Contents

- [Installation](#installation)
- [Quick Start](#quick-start)
- [Authentication](#authentication)
- [Configuration](#configuration)
- [Command Reference](#command-reference)
- [Architecture](#architecture)
- [Development](#development)

## Installation

### Install as Global Tool

```bash
# Build and pack the CLI
dotnet pack src/Apps/Sorcha.Cli

# Install globally
dotnet tool install --global --add-source ./src/Apps/Sorcha.Cli/bin/Release Sorcha.Cli

# Verify installation
sorcha --version
```

### Run Without Installing

```bash
# Run directly from source
dotnet run --project src/Apps/Sorcha.Cli -- [command] [options]

# Example: Check status
dotnet run --project src/Apps/Sorcha.Cli -- auth status
```

## Quick Start

### 1. First-Time Setup

On first run, the CLI will create a default configuration file at `~/.sorcha/config.json` with a single **docker** profile preconfigured for local Docker Compose deployments:

- **docker** - Local Docker Compose deployment via API Gateway (http://localhost)

You can add additional profiles using `sorcha config init` command.

### 2. Authenticate

```bash
# Login interactively (recommended for security)
# Uses the active profile (docker by default)
sorcha auth login

# Or login with a specific profile
sorcha auth login --profile staging
```

The CLI will prompt you for credentials securely (password input is masked).

### 3. Check Authentication Status

```bash
sorcha auth status
```

Output:
```
Profile: docker
Status: Authenticated ✓
Token expires: 2025-12-11T10:30:00Z (59.5 minutes remaining)
Subject: admin@sorcha.dev
Type: user
```

### 4. Start Using Commands

```bash
# List organizations
sorcha org list

# Create a wallet
sorcha wallet create --name "My Wallet" --algorithm ED25519

# List registers
sorcha register list
```

## Authentication

### Overview

The CLI uses OAuth2 for authentication and supports two grant types:

1. **Password Grant** - For user authentication
2. **Client Credentials Grant** - For service principal (application) authentication

### User Authentication

**Interactive Mode (Recommended):**
```bash
sorcha auth login
```

This will prompt you securely for:
- Username
- Password (input is masked with asterisks)

**Non-Interactive Mode (Less Secure):**
```bash
sorcha auth login --username admin@acme.com --password mypassword
```

⚠️ **Warning:** Command-line arguments are visible in process lists. Use interactive mode in production.

### Service Principal Authentication

Service principals are used for automation, CI/CD pipelines, and application-to-application authentication.

**Interactive Mode:**
```bash
sorcha auth login --client-id my-app-id
```

This will prompt for the client secret securely.

**Non-Interactive Mode:**
```bash
sorcha auth login --client-id my-app-id --client-secret my-secret
```

### Token Storage & Security

**Platform-Specific Encryption:**

- **Windows**: Uses DPAPI (Data Protection API) to encrypt tokens
- **macOS**: Uses Keychain for secure token storage
- **Linux**: Uses encrypted storage with user-specific keys

**Token Storage Location:**
- **Windows**: `%USERPROFILE%\.sorcha\tokens\`
- **macOS/Linux**: `~/.sorcha/tokens/`

**Token Lifecycle:**

1. **Login**: Access token and refresh token are stored encrypted
2. **Usage**: Access token is automatically included in API requests
3. **Expiration**: When token expires (< 5 minutes remaining), it's automatically refreshed
4. **Logout**: Tokens are deleted from encrypted storage

### Multi-Profile Authentication

You can authenticate separately for each profile:

```bash
# Login to docker (default)
sorcha auth login

# Login to staging
sorcha auth login --profile staging

# Check status for specific profile
sorcha auth status --profile staging

# Logout from specific profile
sorcha auth logout --profile staging

# Logout from all profiles
sorcha auth logout --all
```

### Security Best Practices

✅ **DO:**
- Use interactive mode for credential input
- Use service principals for CI/CD and automation
- Regularly rotate service principal secrets
- Store production credentials in secure vaults (Azure Key Vault, AWS Secrets Manager)
- Use separate profiles for dev, staging, and production

❌ **DON'T:**
- Pass credentials as command-line arguments in production
- Commit credentials to source control
- Share service principal credentials
- Reuse the same credentials across environments

## Configuration

### Configuration File

The CLI stores its configuration at `~/.sorcha/config.json`.

**Default Configuration:**

The CLI comes with a single preconfigured profile optimized for local Docker Compose deployments:

- **docker** - Local Docker Compose deployment via API Gateway (http://localhost)

All service URLs are routed through the API Gateway, which handles routing to the individual services (tenant, wallet, register, peer).

```json
{
  "activeProfile": "docker",
  "defaultOutputFormat": "table",
  "verboseLogging": false,
  "quietMode": false,
  "profiles": {
    "docker": {
      "name": "docker",
      "serviceUrl": "http://localhost",
      "tenantServiceUrl": null,
      "registerServiceUrl": null,
      "peerServiceUrl": null,
      "walletServiceUrl": null,
      "authTokenUrl": "http://localhost/api/service-auth/token",
      "defaultClientId": "sorcha-cli",
      "verifySsl": false,
      "timeoutSeconds": 30
    }
  }
}
```

**Note:** When service-specific URLs are `null`, they are derived from `serviceUrl` via the API Gateway routing.

### Managing Profiles

**List all profiles:**
```bash
sorcha config list
```

**Create a new profile:**
```bash
# Create profile with base service URL (recommended)
sorcha config init --profile staging --service-url https://staging.sorcha.dev

# Create profile with specific service URLs
sorcha config init --profile prod \
  --tenant-url https://tenant.sorcha.io \
  --wallet-url https://wallet.sorcha.io \
  --register-url https://register.sorcha.io \
  --peer-url https://peer.sorcha.io

# Create Aspire profile for local .NET Aspire development
sorcha config init --profile aspire --service-url https://localhost:7082
```

**Switch active profile:**
```bash
sorcha config set-active staging
```

**Use a specific profile for a single command:**
```bash
sorcha auth login --profile staging
sorcha org list --profile prod
```

### Environment Variables

You can override the configuration directory:

```bash
export SORCHA_CONFIG_DIR=/custom/path
sorcha auth login
```

This is useful for:
- Testing with isolated configurations
- Running multiple CLI instances with different configs
- CI/CD environments

## Command Reference

### Configuration Commands

| Command | Description |
|---------|-------------|
| `sorcha config list` | List all configuration profiles |
| `sorcha config init` | Initialize or update a configuration profile |
| `sorcha config set-active` | Set the active profile |

**Config Init Options:**
- `--profile, -p` - Profile name (default: docker)
- `--service-url, -s` - Base URL for all services (recommended)
- `--tenant-url, -t` - Tenant Service URL override
- `--register-url, -r` - Register Service URL override
- `--wallet-url, -w` - Wallet Service URL override
- `--peer-url` - Peer Service URL override
- `--auth-url, -a` - Auth Token URL override
- `--client-id, -c` - Default client ID (default: sorcha-cli)
- `--verify-ssl` - Verify SSL certificates (default: false)
- `--timeout` - Request timeout in seconds (default: 30)
- `--check-connectivity` - Verify connectivity to services (default: true)
- `--set-active` - Set as active profile (default: true)

### Authentication Commands

| Command | Description |
|---------|-------------|
| `sorcha auth login` | Authenticate as a user or service principal |
| `sorcha auth logout` | Clear cached authentication tokens |
| `sorcha auth status` | Check authentication status |

**Options:**
- `--username, -u` - Username for user authentication
- `--password, -p` - Password (use interactive mode instead)
- `--client-id, -c` - Client ID for service principal authentication
- `--client-secret, -s` - Client secret (use interactive mode instead)
- `--interactive, -i` - Use interactive login (default: true)
- `--profile` - Profile to authenticate with
- `--all, -a` - (logout) Clear tokens for all profiles

### Organization Commands

| Command | Description |
|---------|-------------|
| `sorcha org list` | List all organizations |
| `sorcha org get` | Get organization details |
| `sorcha org create` | Create new organization |
| `sorcha org update` | Update organization |
| `sorcha org delete` | Delete organization |

### User Commands

| Command | Description |
|---------|-------------|
| `sorcha user list` | List users in organization |
| `sorcha user get` | Get user details |
| `sorcha user create` | Create new user |
| `sorcha user update` | Update user |
| `sorcha user delete` | Delete user |

### Service Principal Commands

| Command | Description |
|---------|-------------|
| `sorcha sp list` | List service principals |
| `sorcha sp get` | Get service principal details |
| `sorcha sp create` | Create new service principal |
| `sorcha sp delete` | Delete service principal |

### Wallet Commands

| Command | Description |
|---------|-------------|
| `sorcha wallet list` | List all wallets |
| `sorcha wallet get` | Get wallet details |
| `sorcha wallet create` | Create new wallet |
| `sorcha wallet sign` | Sign data with wallet |
| `sorcha wallet verify` | Verify signature |
| `sorcha wallet delete` | Delete wallet |

### Register Commands

| Command | Description |
|---------|-------------|
| `sorcha register list` | List all registers |
| `sorcha register get` | Get register details |
| `sorcha register create` | Create new register |
| `sorcha register delete` | Delete register |

### Transaction Commands

| Command | Description |
|---------|-------------|
| `sorcha tx list` | List transactions |
| `sorcha tx get` | Get transaction details |
| `sorcha tx submit` | Submit new transaction |
| `sorcha tx query` | Query transactions with filters |

### Peer Commands (Sprint 4 - Stub Implementation)

| Command | Description |
|---------|-------------|
| `sorcha peer list` | List all peers in the network |
| `sorcha peer get` | Get peer details |
| `sorcha peer topology` | View network topology |
| `sorcha peer stats` | Network statistics |
| `sorcha peer health` | Health checks |

**Note:** Peer commands currently provide stub output. Full gRPC client integration planned for future sprint.

### Global Options

All commands support these global options:

| Option | Description | Default |
|--------|-------------|---------|
| `--profile, -p` | Configuration profile to use | docker |
| `--output, -o` | Output format (table, json, csv) | table |
| `--quiet, -q` | Suppress non-essential output | false |
| `--verbose, -v` | Enable verbose logging | false |

## Architecture

### Technology Stack

- **.NET 10** - Latest .NET framework
- **System.CommandLine** - Modern CLI framework for .NET
- **Microsoft.Extensions.DependencyInjection** - Built-in DI container
- **Microsoft.Extensions.Logging** - Logging infrastructure
- **Polly** - Resilience and transient fault handling
- **Refit** - Type-safe HTTP client (planned)

### Project Structure

```
Sorcha.Cli/
├── Commands/                # Command implementations
│   ├── AuthCommands.cs     # Authentication commands
│   ├── OrganizationCommands.cs
│   ├── UserCommands.cs
│   ├── WalletCommands.cs
│   ├── RegisterCommands.cs
│   ├── TransactionCommands.cs
│   └── PeerCommands.cs
├── Services/               # Business logic services
│   ├── AuthenticationService.cs
│   ├── ConfigurationService.cs
│   └── Interfaces/
├── Infrastructure/         # Shared infrastructure
│   ├── TokenCache.cs      # Encrypted token storage
│   ├── ConsoleHelper.cs   # Console I/O utilities
│   ├── WindowsDpapiEncryption.cs
│   ├── MacOsKeychainEncryption.cs
│   └── LinuxEncryption.cs
├── Models/                 # DTOs and domain models
│   ├── LoginRequest.cs
│   ├── TokenResponse.cs
│   └── CliConfiguration.cs
└── Program.cs              # Entry point with DI setup
```

### Dependency Injection

The CLI uses Microsoft.Extensions.DependencyInjection for service registration:

```csharp
services.AddSingleton<IConfigurationService, ConfigurationService>();
services.AddHttpClient("SorchaApi", client => { /* config */ });
services.AddSingleton<TokenCache>();
services.AddSingleton<IAuthenticationService, AuthenticationService>();
```

Commands receive dependencies via constructor injection:

```csharp
public class AuthCommand : Command
{
    public AuthCommand(
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("auth", "Manage authentication and login sessions")
    {
        // Wire up subcommands with services
    }
}
```

### Error Handling

The CLI uses exit codes to indicate success or failure:

| Exit Code | Description |
|-----------|-------------|
| 0 | Success |
| 1 | General error |
| 2 | Authentication error |
| 3 | Validation error |
| 4 | Not found error |

## Development

### Building

```bash
# Build the project
dotnet build src/Apps/Sorcha.Cli

# Run from source
dotnet run --project src/Apps/Sorcha.Cli -- --help
```

### Testing

```bash
# Run all CLI tests
dotnet test tests/Sorcha.Cli.Tests

# Run specific test class
dotnet test tests/Sorcha.Cli.Tests --filter "FullyQualifiedName~AuthCommandsTests"

# Run with coverage
dotnet test tests/Sorcha.Cli.Tests --collect:"XPlat Code Coverage"
```

### Adding a New Command

1. **Create command class** in `Commands/` folder
2. **Implement command logic** with proper options and handlers
3. **Wire up dependencies** via constructor injection
4. **Register command** in `Program.cs` BuildRootCommand()
5. **Add tests** in `tests/Sorcha.Cli.Tests/Commands/`
6. **Update documentation** in README.md

**Example:**

```csharp
public class MyNewCommand : Command
{
    private readonly IMyService _myService;

    public MyNewCommand(IMyService myService)
        : base("mynew", "Description of my new command")
    {
        _myService = myService;

        var myOption = new Option<string>(
            aliases: new[] { "--my-option", "-m" },
            description: "My option description");

        AddOption(myOption);

        this.SetHandler(async (myOptionValue) =>
        {
            try
            {
                await _myService.DoSomethingAsync(myOptionValue);
                ConsoleHelper.WriteSuccess("Operation completed!");
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Operation failed: {ex.Message}");
                Environment.ExitCode = ExitCodes.GeneralError;
            }
        }, myOption);
    }
}
```

### Debugging

**Visual Studio / VS Code:**

Set launch configuration in `.vscode/launch.json`:

```json
{
  "name": ".NET Core Launch (CLI)",
  "type": "coreclr",
  "request": "launch",
  "program": "${workspaceFolder}/src/Apps/Sorcha.Cli/bin/Debug/net10.0/Sorcha.Cli.dll",
  "args": ["auth", "status"],
  "cwd": "${workspaceFolder}",
  "console": "integratedTerminal"
}
```

**Command Line:**

```bash
# Enable verbose logging
sorcha auth status --verbose

# Or set environment variable
export DOTNET_CLI_DEBUG=1
sorcha auth status
```

## Contributing

See [CONTRIBUTING.md](../../../CONTRIBUTING.md) for contribution guidelines.

## License

See [LICENSE](../../../LICENSE) for license information.
