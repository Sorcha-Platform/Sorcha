# Sorcha

A distributed ledger platform for secure, multi-participant data flow orchestration built on .NET 10 and .NET Aspire.

Sorcha implements the **DAD** (Disclosure, Alteration, Destruction) security model - creating cryptographically secured registers where disclosure is managed through defined schemas, alteration is recorded on immutable ledgers, and destruction risk is eliminated through peer network replication.

## Platform Capabilities

Sorcha is a **distributed ledger platform** for building secure, multi-participant workflows — where multiple parties need to exchange, validate, and record structured data with cryptographic guarantees.

| Capability | Description |
|------------|-------------|
| **Blueprint Workflows** | Define multi-step, multi-party data flows as declarative JSON blueprints with conditional routing, JSON Schema validation, and JSON Logic evaluation |
| **Distributed Ledger** | Immutable, append-only transaction registers with chain validation, Merkle-tree dockets, and DID URI addressing (`did:sorcha:register:{id}/tx:{txId}`) |
| **Cryptographic Wallets** | HD wallet management (BIP32/39/44) with multi-algorithm support — ED25519, NISTP-256, RSA-4096 — for signing, verification, and payload encryption |
| **Portable Execution Engine** | Stateless blueprint engine runs identically on server (.NET) and client (Blazor WASM) for validation, calculation, routing, and selective disclosure |
| **Multi-Tenant Identity** | JWT-based authentication with service-to-service OAuth2 client credentials, delegation tokens, participant identity registry, and wallet address linking |
| **Peer Network** | gRPC-based P2P topology for register replication across nodes with hub/peer architecture and heartbeat monitoring |
| **Consensus & Validation** | Validator service with memory pool, docket building, genesis creation, and transaction integrity verification |
| **Real-Time Notifications** | SignalR hubs with Redis backplane for live action notifications, register events, and workflow state changes |
| **API Gateway** | YARP reverse proxy with aggregated OpenAPI documentation, health check aggregation, and centralized CORS/security policies |
| **AI Integration** | MCP Server for AI assistant interaction (Claude Desktop, etc.) + AI-assisted blueprint design chat via Claude API |

**Security Model — DAD (Disclosure, Alteration, Destruction):**
- **Disclosure**: Field-level encryption and selective data disclosure via JSON Pointers (RFC 6901) ensure participants see only what they're authorized to access
- **Alteration**: Every data change is recorded as a cryptographically signed transaction on an immutable ledger
- **Destruction**: Peer network replication eliminates single-point-of-failure data loss risk

## Development Status

**Current Stage:** Active Development - MVD Phase (98% Complete) | [View Detailed Status Report](docs/development-status.md)

| Component | Status | Completion |
|-----------|--------|------------|
| Core Libraries | Production Ready | 97% |
| **⭐ Execution Engine (Portable)** | **✅ COMPLETE** | **100%** |
| **⭐ Blueprint Service** | **✅ COMPLETE** | **100%** |
| **⭐ Wallet Service** | **✅ COMPLETE** | **95%** |
| **⭐ Register Service** | **✅ COMPLETE** | **100%** |
| **⭐ Peer Service** | **Functional** | **70%** |
| **⭐ Validator Service** | **✅ Nearly Complete** | **95%** |
| **⭐ Sorcha.UI (Unified)** | **✅ COMPLETE** | **100%** |
| Services & APIs | Enhanced | 97% |
| Testing & CI/CD | Production Ready | 95% |

> **⚠️ Production Readiness: 30%** - Core functionality and authentication complete. Database persistence and security hardening are pending. See [MASTER-PLAN.md](.specify/MASTER-PLAN.md) for details.

**Recent Updates (2026-02-27):**
- ✅ **Codebase Consolidation** — Eliminated ~1,000+ lines of duplicated code across 69 projects
  - Shared authorization policies in `ServiceDefaults` (6 policies consolidated from 6 services)
  - Shared OpenAPI/Scalar and CORS configuration helpers
  - Fixed Wallet Service middleware ordering bug, verified Tenant Service pipeline
  - Fixed 11 orphaned CLI endpoints (admin alerts path, credential API versioning)
  - Extracted shared MCP Server `ErrorResponse` model (from 18 duplicate classes)
  - Consolidated `CreateWalletRequest` DTO with PQC fields
  - SPDX license headers added to 547 source files (100% coverage)
- ✅ **EF Core Migration Consolidation** — Squashed migrations to single InitialCreate per DbContext
- ✅ **Service-to-Service Auth Fixes** — ValidatorServiceClient auth, register scopes, blueprint role check
- ✅ **UI/CLI Modernization** — Spectre.Console tables, MudBlazor 8 upgrade, activity log panel

**Previous Updates (2026-02-03):**
- ✅ **Validator Service Transaction Storage** - Full end-to-end genesis docket creation with transaction documents stored in MongoDB
- ✅ **AI-Assisted Blueprint Design Chat** - Interactive blueprint design with Claude AI integration
- ✅ **UI Authentication Improvements** - Token management and login UX enhancements

**Previous Updates:**
- *2026-01-28* — UI Register Management, CLI Register Commands, transaction query
- *2026-01-21* — UI Consolidation (Sorcha.Admin → Sorcha.UI), consumer pages, SignalR integration
- *2026-01-01* — Tenant Bootstrap API, system schema store
- *2025-12-12* — Service Authentication (JWT), Wallet EF Core, Peer Service P2P

**Key Milestones:**
- ✅ Blueprint modeling and fluent API
- ✅ REST API for blueprint management
- ✅ Cryptography and transaction handling
- ✅ Production-grade CI/CD pipeline
- ✅ Portable execution engine complete (client + server side)
- ✅ Comprehensive unit and integration test coverage (102+ tests for engine alone)
- ✅ **Unified Blueprint-Action service with SignalR**
- ✅ **Wallet Service core implementation with EF Core persistence**
- ✅ **Execution helper endpoints for client-side validation**
- ✅ **Register Service full implementation with comprehensive testing (100%)**
- ✅ **Register Service Phase 5 API with 20 REST endpoints, OData, and SignalR**
- ✅ **UI Consolidation complete** - Single unified Sorcha.UI application
- ✅ **UI Register Management** - Wallet selection wizard, transaction query
- ✅ **CLI Register Commands** - Two-phase creation, dockets, queries
- ✅ **Peer Service Phase 1-3** - Hub connection, replication, heartbeat
- ✅ **Validator Service 95%** - Memory pool, docket building, consensus, transaction storage
  - System wallet auto-initialization
  - Redis-backed memory pool and register monitoring
  - Full genesis docket creation with transaction document storage
- 🚧 Validator Service decentralized consensus (leader election, multi-validator)
- 🚧 Production deployment (Azure Key Vault, MongoDB persistence)

See the [detailed development status](docs/development-status.md) for complete information on modules, testing coverage, and infrastructure.

## Overview

Sorcha is a modernized, cloud-native platform for defining, designing, and executing data flow blueprints. Built on .NET 10 and leveraging .NET Aspire for cloud-native orchestration, Sorcha provides a flexible and scalable solution for workflow automation and data processing pipelines.

## Specification & Planning

This project uses [Spec-Kit](https://github.com/github/spec-kit) for specification-driven development. All project specifications, architectural plans, and task tracking are maintained in the [.specify/](.specify/README.md) directory.

**Key Documents:**
- **[Constitution](.specify/constitution.md)** - Project principles and development standards
- **[Specification](.specify/spec.md)** - Requirements, architecture, and user scenarios
- **[Master Plan](.specify/MASTER-PLAN.md)** - Unified implementation strategy and phases
- **[Master Tasks](.specify/MASTER-TASKS.md)** - Consolidated task list with priorities
- **[Service Specs](.specify/specs/)** - Detailed specifications for each service

**For Developers:**
- Start with the [.specify README](.specify/README.md) to understand the specification structure
- Check [MASTER-PLAN.md](.specify/MASTER-PLAN.md) for current development phase and priorities
- Find tasks in [MASTER-TASKS.md](.specify/MASTER-TASKS.md) (P0 = MVD blockers, P1 = Core, P2 = Nice-to-have, P3 = Post-MVD)
- Follow [constitution.md](.specify/constitution.md) for architectural principles and coding standards

**For AI Agents:**
All specifications are designed to provide context for AI-assisted development. Consult the constitution for guardrails, the spec for requirements, and the master plan for implementation priorities.

## Features

### Core Capabilities
- **✅ Portable Blueprint Execution Engine** (COMPLETE): Stateless engine that runs client-side (Blazor WASM) and server-side
  - ✅ JSON Schema validation (Draft 2020-12)
  - ✅ JSON Logic evaluation for calculations and conditions
  - ✅ Selective data disclosure using JSON Pointers (RFC 6901)
  - ✅ Conditional routing between participants
  - ✅ Thread-safe, immutable design pattern
  - ✅ Comprehensive test coverage: 93 unit tests + 9 integration tests
  - ✅ Real-world scenarios tested: loan applications, purchase orders, multi-step surveys

- **✅ Unified Blueprint-Action Service** (Sprints 3-5 COMPLETE): Complete workflow management
  - ✅ Blueprint CRUD operations and versioning
  - ✅ Action retrieval, submission, and rejection (Sprint 4)
  - ✅ Real-time notifications via SignalR with Redis backplane (Sprint 5)
  - ✅ Execution helper endpoints (validate, calculate, route, disclose) (Sprint 5)
  - ✅ File upload/download support
  - ✅ Integration with Wallet Service (encryption/decryption) (Sprint 3)
  - ✅ Integration with Register Service (blockchain transactions) (Sprint 3)
  - ✅ JWT Bearer authentication with authorization policies (AUTH-002 COMPLETE)

- **✅ Wallet Service** (95% COMPLETE): Secure cryptographic wallet management ([View Detailed Status](docs/wallet-service-status.md))
  - ✅ HD wallet support with BIP32/BIP39/BIP44 standards (NBitcoin)
  - ✅ Multi-algorithm support (ED25519, NISTP256, RSA-4096)
  - ✅ Transaction signing and verification
  - ✅ Payload encryption/decryption
  - ✅ Access delegation and control (Owner/ReadWrite/ReadOnly)
  - ✅ 14 REST API endpoints with comprehensive OpenAPI docs
  - ✅ 80+ unit tests, 20+ integration tests (~85% coverage)
  - ✅ EF Core repository with PostgreSQL persistence (AUTH-003 COMPLETE)
  - ✅ JWT Bearer authentication with authorization policies (AUTH-002 COMPLETE)
  - 🚧 Azure Key Vault integration (pending - P1)
  - 🚧 HD address generation (not implemented - design needed)

- **✅ Register Service** (100% COMPLETE): Distributed ledger for transaction storage
  - ✅ Complete domain models (Register, TransactionModel, Docket, PayloadModel)
  - ✅ RegisterManager, TransactionManager, DocketManager, QueryManager (~3,500 LOC)
  - ✅ 20 REST endpoints (registers, transactions, dockets, query API)
  - ✅ Real-time notifications via SignalR with RegisterHub
  - ✅ OData V4 support for flexible queries
  - ✅ Comprehensive testing (112 tests, ~2,459 LOC)
  - ✅ Chain validation and block sealing
  - ✅ DID URI support: `did:sorcha:register:{id}/tx:{txId}`
  - ✅ JWT Bearer authentication with authorization policies (AUTH-002 COMPLETE)
  - 🚧 MongoDB repository (InMemory implementation complete)

- **✅ Validator Service** (95% COMPLETE): Blockchain consensus and validation
  - ✅ System wallet auto-initialization with `ISystemWalletProvider`
  - ✅ Redis-backed memory pool (`IMemPoolManager`) for transaction persistence
  - ✅ Redis-backed register monitoring (`IRegisterMonitoringRegistry`) for docket build tracking
  - ✅ Genesis docket creation with Merkle tree computation
  - ✅ Docket building and signing with system wallet
  - ✅ Transaction validation and consensus
  - ✅ Full transaction document storage integration with Register Service
  - ✅ Periodic docket build triggers (`DocketBuildTriggerService`)
  - ✅ JWT Bearer authentication for service-to-service communication
  - 🚧 Decentralized consensus (leader election, multi-validator)
  - 🚧 Byzantine fault tolerance mechanisms

- **✅ Tenant Service** (85% COMPLETE): Multi-tenant authentication and authorization ([View Specification](.specify/specs/sorcha-tenant-service.md))
  - ✅ User authentication with JWT tokens (60 min lifetime)
  - ✅ Service-to-service authentication (OAuth2 client credentials, 8 hour tokens)
  - ✅ Delegation tokens for services acting on behalf of users
  - ✅ Token refresh flow (24 hour refresh token lifetime)
  - ✅ Hybrid token validation (local JWT + optional introspection)
  - ✅ Token revocation with Redis-backed store
  - ✅ Multi-tenant organization management with subdomain routing
  - ✅ Role-based access control (9 authorization policies)
  - ✅ 30+ REST API endpoints fully documented
  - ✅ Bootstrap API endpoint for system initialization
  - ✅ 67 integration tests (91% pass rate)
  - ✅ Participant Identity API with wallet linking
  - 🚧 PostgreSQL repository (partially implemented)
  - 🚧 6 failing tests to resolve
  - 🚧 Production deployment with Azure AD/B2C (pending)

- **Blueprint Designer**: Visual designer for creating and managing workflows
  - Blazor WASM client with offline capabilities
  - Client-side validation using portable execution engine
  - Real-time blueprint testing mode
  - Schema browser and form designer

### Platform Features
- **.NET 10**: Built on the latest .NET platform for maximum performance
- **.NET Aspire**: Cloud-native orchestration and service discovery
- **Minimal APIs**: Modern, lightweight API design
- **SignalR**: Real-time notifications with Redis backplane
- **Observability**: Built-in OpenTelemetry support for monitoring and tracing
- **Security**: JWT authentication, rate limiting, audit logging

## Project Structure

```
Sorcha/
├── src/
│   ├── Apps/                        # Application layer
│   │   ├── Sorcha.AppHost/         # .NET Aspire orchestration host
│   │   ├── Sorcha.Cli/             # Administrative CLI tool
│   │   ├── Sorcha.Demo/            # Blueprint workflow demo CLI
│   │   ├── Sorcha.McpServer/       # MCP Server for AI assistants
│   │   └── Sorcha.UI/              # Unified Blazor WASM application
│   │       ├── Sorcha.UI.Core/     # Shared UI components
│   │       ├── Sorcha.UI.Web/      # Web host
│   │       └── Sorcha.UI.Web.Client/ # Web client (Blazor WASM)
│   ├── Common/                      # Cross-cutting concerns
│   │   ├── Sorcha.Blueprint.Models/ # Domain models with JSON-LD
│   │   ├── Sorcha.Cryptography/    # Multi-algorithm crypto (ED25519, P-256, RSA)
│   │   ├── Sorcha.ServiceClients/  # Consolidated HTTP/gRPC clients & shared DTOs
│   │   └── Sorcha.ServiceDefaults/ # Shared service configuration, auth policies, OpenAPI
│   ├── Core/                        # Business logic
│   │   ├── Sorcha.Blueprint.Engine/ # Blueprint execution engine
│   │   ├── Sorcha.Blueprint.Fluent/ # Fluent API builders
│   │   └── Sorcha.Blueprint.Schemas/ # Schema management
│   └── Services/                    # 7 microservices
│       ├── Sorcha.ApiGateway/      # YARP reverse proxy
│       ├── Sorcha.Blueprint.Service/ # Workflow management + SignalR
│       ├── Sorcha.Peer.Service/    # P2P networking (gRPC)
│       ├── Sorcha.Register.Service/ # Distributed ledger + OData
│       ├── Sorcha.Tenant.Service/  # Multi-tenant auth + JWT issuer
│       ├── Sorcha.Validator.Service/ # Consensus + chain integrity
│       └── Sorcha.Wallet.Service/  # Crypto wallet management
├── tests/                           # Test projects
├── docs/                            # Documentation
└── .github/                         # GitHub workflows
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later (version 10.0.100+)
- [Git](https://git-scm.com/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (required for integration tests and Redis)
- A code editor:
  - [Visual Studio 2025](https://visualstudio.microsoft.com/) (recommended for Windows)
  - [Visual Studio Code](https://code.visualstudio.com/) with C# extension
  - [JetBrains Rider](https://www.jetbrains.com/rider/)

### Quick Start

> **⚠️ IMPORTANT:** Sorcha now uses **Docker-Compose as the primary development environment**. When you modify service code, you must rebuild the Docker container. See [Docker Development Workflow](docs/DOCKER-DEVELOPMENT-WORKFLOW.md) for details.

1. **Clone the repository**
   ```bash
   git clone https://github.com/sorcha-platform/sorcha.git
   cd sorcha
   ```

2. **Start all services with Docker Compose** ⭐ **RECOMMENDED**
   ```bash
   docker-compose up -d
   ```

   **Access the services:**
   - Register Service: http://localhost:5290
   - Validator Service: http://localhost:5100
   - API Gateway: http://localhost:5110
   - Sorcha UI: http://localhost/app
   - Aspire Dashboard: http://localhost:18888

3. **Run walkthrough tests**
   ```bash
   pwsh walkthroughs/RegisterCreationFlow/test-register-creation-docker.ps1
   ```

4. **After making code changes, rebuild the service:**
   ```bash
   # Quick rebuild script
   pwsh scripts/rebuild-service.ps1 <service-name>

   # Or manually:
   docker-compose build <service-name>
   docker-compose up -d --force-recreate <service-name>
   ```

### Running in Development

> **📘 Full Docker Workflow Guide:** [docs/DOCKER-DEVELOPMENT-WORKFLOW.md](docs/DOCKER-DEVELOPMENT-WORKFLOW.md)

#### Option 1: Using Docker Compose (Recommended for Active Development)

**Start all services:**
```bash
docker-compose up -d
```

**View logs:**
```bash
docker-compose logs -f                    # All services
docker logs sorcha-register-service -f    # Specific service
```

**Rebuild after code changes:**
```bash
# Using helper script (recommended)
pwsh scripts/rebuild-service.ps1 register-service

# Or manually
docker-compose build register-service
docker-compose up -d --force-recreate register-service
```

**Stop all services:**
```bash
docker-compose down
```

**Why Docker-Compose for development:**
- ✅ Production-like environment
- ✅ Complete service isolation
- ✅ Consistent across team
- ✅ Works with walkthroughs
- ✅ No .NET version conflicts
- ✅ Easy to reset state

#### Option 2: Using .NET Aspire AppHost (For Debugging)

Use AppHost when you need Visual Studio debugging with breakpoints:

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

This will:
- Start all services with .NET process isolation
- Launch the Aspire dashboard at `http://localhost:18888`
- Enable Visual Studio debugger attachment
- Start PostgreSQL, MongoDB, and Redis containers via Docker

**Access Points:**
- **Aspire Dashboard**: `http://localhost:18888`
- **API Gateway**: `https://localhost:7082`
- **Sorcha UI**: `https://localhost:7083`
- **Tenant Service (Auth)**: `https://localhost:7110`
- **Blueprint Service**: `https://localhost:7000`
- **Wallet Service**: `https://localhost:7001`
- **Register Service**: `https://localhost:7290`
- **Peer Service**: `https://localhost:7002`

> 📘 **Port Configuration Reference**: See [docs/PORT-CONFIGURATION.md](docs/PORT-CONFIGURATION.md) for complete port assignments, environment-specific URLs, and troubleshooting.

#### Option 2: Running Individual Services

> ⚠️ **Note**: Individual services use the standardized port scheme. All ports are fixed and documented.

**Tenant Service (Authentication):**
```bash
dotnet run --project src/Services/Sorcha.Tenant.Service
# HTTP: http://localhost:5110
# HTTPS: https://localhost:7110
```

**Blueprint Service:**
```bash
dotnet run --project src/Services/Sorcha.Blueprint.Service
# HTTP: http://localhost:5000
# HTTPS: https://localhost:7000
```

**Wallet Service:**
```bash
dotnet run --project src/Services/Sorcha.Wallet.Service
# HTTP: http://localhost:5001
# HTTPS: https://localhost:7001
```

**Register Service:**
```bash
dotnet run --project src/Services/Sorcha.Register.Service
# HTTP: http://localhost:5290
# HTTPS: https://localhost:7290
```

**Peer Service:**
```bash
dotnet run --project src/Services/Sorcha.Peer.Service
# HTTP: http://localhost:5002
# HTTPS: https://localhost:7002
```

**API Gateway:**
```bash
dotnet run --project src/Services/Sorcha.ApiGateway
# HTTP: http://localhost:8080
# HTTPS: https://localhost:7082
```

**Sorcha UI (Blazor WebAssembly):**
```bash
dotnet run --project src/Apps/Sorcha.UI/Sorcha.UI.Web
# HTTP: http://localhost:8081
# HTTPS: https://localhost:7083
```

#### Option 3: Using Docker Compose (Production-Like)

For a production-like environment with all services containerized.

> 📘 **Quick Start Guide**: See [docs/DOCKER-QUICK-START.md](docs/DOCKER-QUICK-START.md) for a comprehensive Docker deployment guide.

**Prerequisites:**
1. **Generate HTTPS Certificate** (required for API Gateway):
   ```bash
   # Create certificates directory
   mkdir -p docker/certs

   # Generate development certificate
   dotnet dev-certs https -ep docker/certs/aspnetapp.pfx -p SorchaDev2025 --trust
   ```

2. **Start Services**:
   ```bash
   # Start all services with Docker Compose
   docker-compose up -d

   # View logs
   docker-compose logs -f

   # Stop all services
   docker-compose down
   ```

**Access Points:**
- **API Gateway (HTTP)**: `http://localhost/` - Landing page with system dashboard
- **API Gateway (HTTPS)**: `https://localhost/` - Secure access (requires certificate)
- **API Documentation**: `http://localhost/scalar/` - Interactive Scalar API docs
- **Health Check**: `http://localhost/api/health` - Aggregated service health
- **Dashboard Stats**: `http://localhost/api/dashboard` - Platform statistics (blueprints, wallets, registers)
- **Aspire Dashboard**: `http://localhost:18888` - Observability and telemetry

**Infrastructure Services:**
- **PostgreSQL**: `localhost:5432` - User: `sorcha`, Password: `sorcha_dev_password`
- **MongoDB**: `localhost:27017` - User: `sorcha`, Password: `sorcha_dev_password`
- **Redis**: `localhost:6379` - No authentication

**P2P gRPC Endpoints:**
- **Hub Node**: `localhost:50051` - gRPC P2P hub for external connections
- **Peer Service**: `localhost:50052` - gRPC peer node connections

**Networking:**
- Single bridge network (`sorcha-network`)
- Services communicate via Docker DNS (e.g., `http://wallet-service:8080`)
- External access via published ports only
- Backend services (blueprint, wallet, register, tenant, validator) are not directly exposed
- All HTTP/HTTPS API access goes through the API Gateway
- See [docs/DOCKER-BRIDGE-NETWORKING.md](docs/DOCKER-BRIDGE-NETWORKING.md) for detailed networking architecture

### Development Workflow

1. **Make code changes** in your preferred editor

2. **Run tests** to verify changes
   ```bash
   dotnet test
   ```

3. **Hot reload** - Many changes reload automatically without restart when using `dotnet watch`
   ```bash
   dotnet watch --project src/Services/Sorcha.Blueprint.Service
   ```

4. **Format code** before committing
   ```bash
   dotnet format
   ```

5. **Check for issues**
   ```bash
   # Check for vulnerable packages
   dotnet list package --vulnerable

   # Check for outdated packages
   dotnet list package --outdated
   ```

## Administrative CLI Tool

The Sorcha CLI (`sorcha`) is a cross-platform administrative tool for managing the distributed ledger platform. It provides commands for organization management, wallet operations, transaction handling, register administration, and peer network monitoring.

### Installation

The CLI is packaged as a .NET global tool:

```bash
# Install from local build
dotnet pack src/Apps/Sorcha.Cli
dotnet tool install --global --add-source ./src/Apps/Sorcha.Cli/bin/Release Sorcha.Cli

# Or run directly without installing
dotnet run --project src/Apps/Sorcha.Cli -- [command] [options]
```

### Available Commands

#### Organization Management
```bash
# List organizations
sorcha org list --profile dev

# Get organization details
sorcha org get --org-id acme-corp

# Create new organization
sorcha org create --name "Acme Corporation" --subdomain acme
```

#### User Management
```bash
# List users in organization
sorcha user list --org-id acme-corp

# Get user details
sorcha user get --username admin@acme.com
```

#### Authentication & Session Management
```bash
# Login as a user (interactive - recommended)
sorcha auth login

# Login with explicit credentials (less secure - use interactive mode)
sorcha auth login --username admin@acme.com --password mypassword

# Login as a service principal (interactive)
sorcha auth login --client-id my-app-id

# Login as a service principal (non-interactive)
sorcha auth login --client-id my-app-id --client-secret my-secret

# Check authentication status for current profile
sorcha auth status

# Check authentication status for specific profile
sorcha auth status --profile staging

# Logout from current profile
sorcha auth logout

# Logout from all profiles
sorcha auth logout --all
```

**Authentication Features:**
- **Secure Token Storage**: Tokens are encrypted using platform-specific mechanisms:
  - **Windows**: DPAPI (Data Protection API)
  - **macOS**: Keychain
  - **Linux**: Encrypted storage with user-specific keys
- **Automatic Token Refresh**: Access tokens are automatically refreshed when they expire
- **Multi-Profile Support**: Authenticate separately for dev, staging, and production environments
- **Interactive Mode**: Passwords and secrets are masked during input (recommended for security)
- **OAuth2 Support**: Both password grant (users) and client credentials grant (service principals)

**Security Best Practices:**
- Always use interactive mode (`--interactive`) to avoid exposing credentials in process lists
- Never commit credentials to source control
- Use service principals for automated/CI scenarios
- Regularly rotate service principal secrets

#### Wallet Operations
```bash
# List wallets
sorcha wallet list

# Create new wallet
sorcha wallet create --name "My Wallet" --algorithm ED25519

# Get wallet details
sorcha wallet get --address wallet-addr-123

# Sign data
sorcha wallet sign --address wallet-addr-123 --data dGVzdCBkYXRh
```

#### Register & Transaction Management
```bash
# List registers
sorcha register list

# Get register details
sorcha register get --register-id reg-123

# Submit transaction
sorcha tx submit --register-id reg-123 --payload '{"type":"invoice","amount":1500.00}'

# Query transactions
sorcha tx list --register-id reg-123
```

#### Peer Network Monitoring _(Sprint 4 - Stub Implementation)_
```bash
# List all peers in the network
sorcha peer list --status connected

# Get peer details
sorcha peer get --peer-id peer-node-01 --show-metrics

# View network topology
sorcha peer topology --format tree

# Network statistics
sorcha peer stats --window 24h

# Health checks
sorcha peer health --check-connectivity --check-consensus
```

**Note:** Peer commands currently provide stub output. Full gRPC client integration with the Peer Service is planned for a future sprint.

### Global Options

All commands support these global options:

```bash
--profile, -p     # Configuration profile (dev, staging, production) [default: dev]
--output, -o      # Output format (table, json, csv) [default: table]
--quiet, -q       # Suppress non-essential output
--verbose, -v     # Enable verbose logging
```

### Examples

**List organizations in table format (default):**
```bash
sorcha org list --profile dev
```

**Get wallet details in JSON format:**
```bash
sorcha wallet get --address wallet-123 --output json
```

**Submit transaction with complex payload:**
```bash
sorcha tx submit --register-id reg-123 --payload '{
  "type": "invoice",
  "amount": 1500.00,
  "metadata": {
    "invoice_id": "INV-2025-001"
  }
}'
```

### Future: Interactive Mode (REPL)

An interactive console mode is planned for Sprint 5, which will enable:
- Persistent session with authentication
- Context awareness (set current org/register)
- Command history and tab completion
- Multi-line input for complex JSON payloads

See [CLI-SPRINT-4-SUMMARY.md](docs/CLI-SPRINT-4-SUMMARY.md) for full planning details.

### CLI Architecture

The CLI is built with:
- **System.CommandLine** - Modern CLI framework
- **Spectre.Console** - Rich terminal UI and formatting
- **Refit** - HTTP client for service communication
- **Polly** - Resilience policies for API calls

All commands follow a consistent pattern with proper validation, error handling, and output formatting.

## Testing

Sorcha includes comprehensive test coverage across multiple layers.

### Test Projects

- **Sorcha.Blueprint.Api.Tests** - API endpoint tests
- **Sorcha.Blueprint.Engine.Tests** - Blueprint engine and workflow demo tests
- **Sorcha.Blueprint.Fluent.Tests** - Fluent builder pattern tests
- **Sorcha.Cryptography.Tests** - Cryptography library tests
- **Sorcha.Gateway.Integration.Tests** - Gateway routing and integration tests
- **Sorcha.Performance.Tests** - NBomber load/performance tests
- **Sorcha.UI.E2E.Tests** - End-to-end Playwright tests

### Running Tests

**Run all tests:**
```bash
dotnet test
```

**Run specific test project:**
```bash
dotnet test tests/Sorcha.Blueprint.Api.Tests
dotnet test tests/Sorcha.Cryptography.Tests
```

**Run with code coverage:**
```bash
dotnet test --collect:"XPlat Code Coverage"
```

**Run tests in watch mode (auto-rerun on changes):**
```bash
dotnet watch test --project tests/Sorcha.Blueprint.Api.Tests
```

**Filter tests by name:**
```bash
dotnet test --filter "FullyQualifiedName~CryptoModule"
```

### Integration Tests

Integration tests require Docker for Redis.

**Prerequisites:**
```bash
# Ensure Docker Desktop is running
docker ps

# Run integration tests
dotnet test tests/Sorcha.Gateway.Integration.Tests
```

**What they test:**
- Full Aspire AppHost with all services
- YARP gateway routing
- Service-to-service communication
- Health check aggregation
- Redis caching

### Workflow Demo Tests

The Expense Approval Workflow demonstrates Blueprint functionality with JSON Logic routing:

**Run the workflow demo tests:**
```bash
dotnet test tests/Sorcha.Blueprint.Engine.Tests --filter "FullyQualifiedName~ExpenseApprovalWorkflowDemoTests"
```

**What it demonstrates:**
- **JSON Logic Routing**: Dynamic workflow paths based on expense amount:
  - < $100 → Instant system approval
  - $100-$1000 → Route to manager for review
  - ≥ $1000 → Route to finance director for approval
- **Participant Role Fulfillment**: Employee, Manager, Finance Director, and System roles
- **Data Flow Visualization**: CLI output shows the complete approval workflow
- **Workflow Execution**: Real-time routing decisions with formatted console output

**Example output:**
```
╔═══════════════════════════════════════════════════════════╗
║ Expense Approval - Manager Review ($100-$1000)            ║
╚═══════════════════════════════════════════════════════════╝

📝 Expense Claim Submitted:
   Employee: Alice Johnson (employee)
   Amount: $450
   Description: Client dinner and entertainment
   Category: Entertainment

🔀 Routing Decision:
   Next Action: 2 - Manager Review
   Assigned To: Bob Smith (manager)

👔 Manager: Bob Smith
   Decision: APPROVED ✅
   Comments: Approved - valid client entertainment expense

✅ Result: APPROVED BY MANAGER
   Amount: $450
════════════════════════════════════════════════════════════
```

### Performance Tests

Load test the application using NBomber:

```bash
# Run performance tests (30s duration, 50 RPS target)
dotnet run --project tests/Sorcha.Performance.Tests --configuration Release -- http://localhost:5000 30 50

# Quick test (10s duration, 10 RPS)
dotnet run --project tests/Sorcha.Performance.Tests --configuration Release -- http://localhost:5000 10 10
```

**Baseline Performance Metrics (Sprint 7 - 2025-11-18):**

```
Environment: .NET 10.0, Aspire 13.0.0, Windows 11
Test Load: 13,065 requests over 30 seconds, 50 RPS target

Average Latency:
├─ Mean:    1.16 ms  ⚡ Excellent
├─ P50:     0.84 ms  ⚡ Excellent
├─ P95:     2.85 ms  ⚡ Excellent
└─ P99:     5.08 ms  ✅ Very Good

Throughput:
├─ Peak RPS: 55.5 req/sec (stress test)
└─ Average:  435.5 req/sec across all scenarios

Top Performing Scenarios:
├─ Stress Test (ramping):  0.98ms mean, 55.5 RPS
├─ Health Check:           1.09ms mean, 50.0 RPS
└─ Execution Helpers:      1.10ms mean, 50.0 RPS
```

**Test Scenarios:**
- Health endpoint load test (50 RPS)
- Blueprint CRUD operations (25 RPS)
- Action submission workflow (20 RPS)
- Wallet signing operations (30 RPS)
- Register transaction queries (25 RPS)
- Mixed workload with concurrent operations
- Stress test with ramping load (up to 100 RPS)

**Performance Tracking:**
- Baseline metrics: `tests/Sorcha.Performance.Tests/PERFORMANCE-BASELINE.md`
- Historical data: `tests/Sorcha.Performance.Tests/baseline-metrics.csv`
- Reports generated in: `tests/Sorcha.Performance.Tests/performance-reports/`

**Regression Detection:**
- Mean latency >20% worse: **investigate**
- P95 latency >20% worse: **investigate**
- Throughput >20% lower: **investigate**

### Cryptography Library Tests

Test the cryptography library with multiple key types:

```bash
dotnet test tests/Sorcha.Cryptography.Tests
```

**Example: Performance testing different key types**
```bash
# Run specific crypto tests
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~ED25519"
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~NISTP256"
dotnet test tests/Sorcha.Cryptography.Tests --filter "FullyQualifiedName~RSA4096"
```

**Benchmarking crypto operations:**
```csharp
// Example: Load test key generation
for (int i = 0; i < 1000; i++)
{
    var result = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
}

// Example: Load test signing
var keySet = await cryptoModule.GenerateKeySetAsync(WalletNetworks.ED25519);
byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("test data"));

for (int i = 0; i < 10000; i++)
{
    await cryptoModule.SignAsync(hash, (byte)WalletNetworks.ED25519, keySet.Value!.PrivateKey.Key!);
}
```

### Code Coverage Reports

Generate HTML coverage reports:

```bash
# Install report generator (one time)
dotnet tool install -g dotnet-reportgenerator-globaltool

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Generate HTML report
reportgenerator \
  -reports:"**/coverage.cobertura.xml" \
  -targetdir:"coverage-report" \
  -reporttypes:Html

# Open report (Windows)
start coverage-report/index.html

# Open report (Mac/Linux)
open coverage-report/index.html
```

### E2E Tests (Playwright)

End-to-end browser tests require Playwright setup:

```bash
# First-time setup
cd tests/Sorcha.UI.E2E.Tests
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps

# Run E2E tests
dotnet test tests/Sorcha.UI.E2E.Tests

# Run in headed mode (see browser)
dotnet test tests/Sorcha.UI.E2E.Tests -- NUnit.Headless=false
```

### Continuous Testing

Watch tests and auto-run on file changes:

```bash
# Watch all tests
dotnet watch test

# Watch specific project
dotnet watch test --project tests/Sorcha.Cryptography.Tests
```

### Test Best Practices

See [docs/testing.md](docs/testing.md) for comprehensive testing guidelines including:
- Test naming conventions
- AAA pattern (Arrange-Act-Assert)
- Mocking with Moq
- FluentAssertions usage
- Test data builders
- Coverage targets

## Development

### Solution Structure

- **Sorcha.AppHost**: The .NET Aspire orchestration project that manages all services
- **Sorcha.ServiceDefaults**: Shared configurations including auth policies, OpenAPI/Scalar, CORS, OpenTelemetry, health checks, and service discovery
- **Sorcha.ServiceClients**: Consolidated HTTP/gRPC clients and shared DTOs for inter-service communication
- **Sorcha.UI**: Unified Blazor WebAssembly application for administration and blueprint design
- **Sorcha.McpServer**: MCP Server for AI assistant integration (Claude Desktop, etc.)
- **Sorcha.Cryptography**: Standalone cryptography library for key management and digital signatures

### Architecture

Sorcha follows a microservices architecture with:

- **Service-oriented design**: Each component is independently deployable
- **Cloud-native patterns**: Built-in support for service discovery, health checks, and distributed tracing
- **Modern APIs**: RESTful APIs using minimal API patterns
- **WebAssembly UI**: Blazor WebAssembly for responsive, offline-capable user interfaces
- **Gateway Pattern**: YARP-based API gateway for routing and aggregation

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for details on:

- Code of conduct
- Development workflow
- Submitting pull requests
- Reporting issues

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Roadmap

- [x] Core blueprint execution engine (100% - Portable, client + server)
- [x] Blueprint validation and testing framework (100%)
- [x] Unified Blueprint-Action Service with SignalR (100%)
- [x] Wallet Service with EF Core persistence (95%)
- [x] Register Service with distributed ledger (100%)
- [x] Peer Service hub connection and replication (70%)
- [x] Validator Service memory pool, consensus, and transaction storage (95%)
- [x] UI Consolidation - Single unified Sorcha.UI (100%)
- [x] UI Register Management with wallet selection (100%)
- [x] UI Authentication with token refresh (100%)
- [x] CLI Register Commands (100%)
- [x] AI-Assisted Blueprint Design Chat with Claude integration (100%)
- [x] Codebase Consolidation — shared auth policies, pipeline helpers, CLI fixes, MCP model extraction (100%)
- [ ] Validator Service decentralized consensus (leader election)
- [ ] MongoDB persistence for Register Service
- [ ] Azure Key Vault integration for Wallet Service
- [ ] Plugin system for custom actions
- [ ] Multi-tenant support (Tenant Service 85%)
- [ ] Cloud deployment templates (Azure, AWS, GCP)
- [ ] Advanced consensus mechanisms
- [ ] Real-time monitoring dashboard

## Documentation

Full documentation is available in the [docs](docs/) directory:

- [Architecture Overview](docs/architecture.md)
- [Getting Started Guide](docs/getting-started.md)
- [Blueprint Schema](docs/blueprint-schema.md)
- [Development Status](docs/development-status.md)
- [Wallet Service Status](docs/wallet-service-status.md) ⭐ NEW
- [API Reference](docs/api-reference.md)
- [Deployment Guide](docs/deployment.md)

## Support

- Documentation: [docs/](docs/)
- Issues: [GitHub Issues](https://github.com/yourusername/sorcha/issues)
- Discussions: [GitHub Discussions](https://github.com/yourusername/sorcha/discussions)

## Acknowledgments

This project is inspired by and modernizes concepts from the Sorcha AI Development spike.

---

Built with ❤️ using .NET 10 and .NET Aspire
