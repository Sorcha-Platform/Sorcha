# Sorcha

A distributed ledger platform for secure, multi-participant data flow orchestration.

Sorcha lets organizations define structured workflows — called **blueprints** — where multiple parties exchange, validate, and record data with cryptographic guarantees. Every transaction is signed, every change is immutable, and every participant sees only what they're authorized to access.

## What Sorcha Does

| Capability | Description |
|------------|-------------|
| **Blueprint Workflows** | Define multi-step, multi-party data flows as declarative JSON with conditional routing, schema validation, and business logic evaluation |
| **Distributed Ledger** | Immutable, append-only transaction registers with chain validation, Merkle-tree dockets, and DID URI addressing |
| **Cryptographic Wallets** | HD wallet management (BIP32/39/44) with ED25519, P-256, RSA-4096, and post-quantum algorithms (ML-DSA, ML-KEM, SLH-DSA) |
| **Field-Level Encryption** | Envelope encryption with per-recipient key wrapping — participants see only the fields they're authorized to access |
| **Multi-Tenant Identity** | JWT authentication with OAuth2 client credentials, participant identity registry, and wallet address linking |
| **Peer Network** | gRPC-based P2P topology for register replication across nodes |
| **Real-Time Notifications** | SignalR hubs for live action notifications, inbound transaction alerts, and workflow state changes |
| **AI Integration** | MCP Server for AI assistant interaction + AI-assisted blueprint design |

### The DAD Security Model

Sorcha implements **DAD** (Disclosure, Alteration, Destruction):

- **Disclosure** — Field-level encryption and selective disclosure via JSON Pointers ensure participants see only what they're authorized to access
- **Alteration** — Every data change is recorded as a cryptographically signed transaction on an immutable ledger
- **Destruction** — Peer network replication eliminates single-point-of-failure data loss

## Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop) (required)
- [Git](https://git-scm.com/)

### Setup

```bash
git clone https://github.com/sorcha-platform/sorcha.git
cd sorcha

# Interactive setup — generates .env, pulls images, starts services
./scripts/sorcha-setup.sh

# Or manual setup:
cp .env.example .env          # Edit with your settings
docker-compose up -d          # Start all services
```

### Access Points

| Service | URL | Description |
|---------|-----|-------------|
| **Sorcha UI** | http://localhost/app | Main application interface |
| **API Gateway** | http://localhost/ | REST API entry point |
| **API Documentation** | http://localhost/scalar/ | Interactive Scalar API docs |
| **Health Check** | http://localhost/api/health | Aggregated service health |
| **Aspire Dashboard** | http://localhost:18888 | Observability and telemetry |

### Default Credentials

After first run, the system creates a default organization and admin user:

| Field | Value |
|-------|-------|
| Email | `admin@sorcha.local` |
| Password | `Dev_Pass_2025!` |

> **Change these immediately in production.** See [Authentication Setup](docs/AUTHENTICATION-SETUP.md) for production configuration.

## How It Works

### 1. Define a Blueprint

Blueprints are JSON documents that describe multi-party workflows:

```json
{
  "title": "Invoice Approval",
  "participants": [
    { "role": "submitter", "description": "Submits invoices" },
    { "role": "approver", "description": "Reviews and approves" }
  ],
  "actions": [
    {
      "title": "Submit Invoice",
      "assignedTo": "submitter",
      "schema": { "type": "object", "properties": { "amount": { "type": "number" } } }
    },
    {
      "title": "Review Invoice",
      "assignedTo": "approver",
      "routing": { "conditions": [{ "if": { ">": [{ "var": "amount" }, 1000] }, "then": "escalate" }] }
    }
  ]
}
```

See the [blueprints/](blueprints/) directory for ready-to-use templates across finance, healthcare, supply chain, and government domains.

### 2. Publish to a Register

Blueprints are published to **registers** — distributed ledgers that record every transaction immutably. Each register has its own chain of cryptographically signed dockets.

### 3. Execute the Workflow

Participants complete actions in sequence. The engine validates schemas, evaluates business logic, routes to the next participant, and records everything on the ledger.

### 4. Verify and Audit

Every transaction is signed, timestamped, and chained. The full history is available via the REST API or CLI tool.

## CLI Tool

The `sorcha` CLI provides administrative access to the platform:

```bash
# Authenticate
sorcha auth login

# Manage organizations
sorcha org list
sorcha org create --name "Acme Corp" --subdomain acme

# Wallet operations
sorcha wallet list
sorcha wallet create --name "Signing Key" --algorithm ED25519

# Register and transaction management
sorcha register list
sorcha tx submit --register-id reg-123 --payload '{"type":"invoice","amount":1500}'
```

See the [CLI documentation](src/Apps/Sorcha.Cli/README.md) for the full command reference.

## Architecture Overview

```
                    ┌─────────────────┐
                    │   API Gateway   │
                    │    (YARP)       │
                    └────────┬────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
   ┌─────▼─────┐     ┌──────▼──────┐    ┌───────▼───────┐
   │ Blueprint  │     │  Register   │    │    Tenant     │
   │  Service   │     │  Service    │    │   Service     │
   └─────┬──────┘     └──────┬──────┘    └───────────────┘
         │                   │
   ┌─────▼──────┐    ┌──────▼──────┐    ┌───────────────┐
   │   Wallet   │    │  Validator  │    │     Peer      │
   │  Service   │    │  Service    │    │   Service     │
   └────────────┘    └─────────────┘    └───────────────┘
```

| Service | Purpose |
|---------|---------|
| **API Gateway** | YARP reverse proxy — single entry point for all API traffic |
| **Blueprint Service** | Workflow management, execution engine, SignalR notifications |
| **Register Service** | Distributed ledger, transaction storage, OData queries |
| **Wallet Service** | Cryptographic key management, signing, encryption |
| **Tenant Service** | Multi-tenant auth, JWT issuer, participant identity |
| **Validator Service** | Transaction validation, consensus, docket building |
| **Peer Service** | P2P network topology, gRPC replication |

## Configuration

All configuration is managed through environment variables. See [`.env.example`](.env.example) for a fully documented template with every variable explained.

Key settings:

| Variable | Purpose | Default |
|----------|---------|---------|
| `JWT_SIGNING_KEY` | 256-bit key for JWT tokens | Generated by setup script |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | PostgreSQL credentials | `sorcha` / `sorcha_dev_password` |
| `MONGO_USERNAME` / `MONGO_PASSWORD` | MongoDB credentials | `sorcha` / `sorcha_dev_password` |
| `ANTHROPIC_API_KEY` | AI blueprint design (optional) | Empty |

## Documentation

| Document | Description |
|----------|-------------|
| [Docker Quick Start](docs/DOCKER-QUICK-START.md) | Getting started with Docker |
| [Authentication Setup](docs/AUTHENTICATION-SETUP.md) | JWT and auth configuration |
| [API Documentation](docs/API-DOCUMENTATION.md) | REST and gRPC endpoint reference |
| [Blueprint Quick Start](docs/blueprint-quick-start.md) | Creating your first blueprint |
| [Port Configuration](docs/PORT-CONFIGURATION.md) | Service ports and networking |
| [Architecture](docs/architecture.md) | System design and data flows |
| [Deployment Guide](docs/DEPLOYMENT.md) | Production deployment |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | Common issues and solutions |

## Walkthroughs

Interactive demos in the [walkthroughs/](walkthroughs/) directory:

| Walkthrough | Description |
|-------------|-------------|
| `BlueprintStorageBasic/` | Docker startup, bootstrap, JWT authentication |
| `PingPong/` | Simple two-party workflow |
| `ConstructionPermit/` | Multi-step approval process |
| `MedicalEquipmentRefurb/` | Healthcare equipment workflow |
| `OrganizationPingPong/` | Multi-organization data exchange |
| `RegisterCreationFlow/` | Register lifecycle management |

See `walkthroughs/README.md` for the full list and setup instructions.

## Development

For building from source, running tests, project structure, coding conventions, and contributing — see **[DEVELOPMENT.md](DEVELOPMENT.md)**.

## License

MIT License — see [LICENSE](LICENSE) for details.

## Links

- [GitHub Issues](https://github.com/Sorcha-Platform/Sorcha/issues)
- [GitHub Discussions](https://github.com/Sorcha-Platform/Sorcha/discussions)
- [Contributing Guide](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)

---

Built with .NET 10 and .NET Aspire
