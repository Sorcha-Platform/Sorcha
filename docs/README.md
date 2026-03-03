# Sorcha Documentation

Comprehensive documentation for the Sorcha distributed ledger platform.

**Current Status:** 100% MVD (Minimum Viable Deployment) | [Detailed Status Report](reference/development-status.md)

---

## Getting Started

Setup guides, first-run configuration, and prerequisites.

| Guide | Description |
|-------|-------------|
| [Getting Started](getting-started/getting-started.md) | Prerequisites, first run, service overview |
| [Docker Quick Start](getting-started/DOCKER-QUICK-START.md) | Start all services with Docker Compose |
| [Docker Development Workflow](getting-started/DOCKER-DEVELOPMENT-WORKFLOW.md) | Rebuild, hot reload, and debug with Docker |
| [First Run Setup](getting-started/FIRST-RUN-SETUP.md) | Bootstrap credentials and initial configuration |
| [Bootstrap Credentials](getting-started/BOOTSTRAP-CREDENTIALS.md) | Default credentials for development environments |
| [Port Configuration](getting-started/PORT-CONFIGURATION.md) | Complete port assignments for Docker, Aspire, and individual services |
| [Infrastructure Setup](getting-started/INFRASTRUCTURE-SETUP.md) | PostgreSQL, MongoDB, Redis deployment and configuration |
| [Blueprint Quick Start](getting-started/blueprint-quick-start.md) | Create your first blueprint in 5 minutes |
| [NuGet Setup](getting-started/nuget-setup-guide.md) | Package management and private feed configuration |

---

## Guides

How-to guides for specific tasks, integrations, and workflows.

### Authentication & Security

| Guide | Description |
|-------|-------------|
| [Authentication Setup](guides/AUTHENTICATION-SETUP.md) | JWT Bearer configuration, delegation tokens, policies |
| [JWT Configuration](guides/JWT-CONFIGURATION.md) | Token lifetimes, signing keys, service-to-service auth |

### Blueprints

| Guide | Description |
|-------|-------------|
| [Blueprint Format](guides/blueprints/blueprint-format.md) | JSON/YAML blueprint specification |
| [Blueprint Architecture](guides/blueprints/blueprint-architecture.md) | 4-step execution pipeline: validate, calculate, route, disclose |
| [JSON-LD Quick Reference](guides/blueprints/json-ld-quick-reference.md) | JSON-LD context usage in Sorcha models |
| [JSON-LD Implementation](guides/blueprints/json-ld-implementation-summary.md) | JSON-LD integration status and patterns |
| [JSON-e Templates](guides/blueprints/json-e-templates.md) | Template language for dynamic payload generation |
| [JSON Logic Guide](guides/blueprints/json-logic-guide.md) | Conditional routing and calculation expressions |

### Integration & Serialization

| Guide | Description |
|-------|-------------|
| [Integration Guide](guides/INTEGRATION-GUIDE.md) | SDK patterns, wallet integration, SignalR examples |
| [Payload Serialization](guides/PAYLOAD-SERIALIZATION-GUIDE.md) | Transaction serialization, Base64url encoding, hash computation |

### Testing

| Guide | Description |
|-------|-------------|
| [Testing Guide](guides/testing/testing.md) | Test strategy, naming conventions, coverage targets |
| [Peer Integration Tests](guides/testing/peer-service-integration-testing.md) | Peer Service integration test suite |

### Deployment

| Guide | Description |
|-------|-------------|
| [Deployment](guides/DEPLOYMENT.md) | Production deployment guide |
| [Azure Quick Start](guides/azure/AZURE-DEPLOYMENT-QUICK-START.md) | Deploy to Azure Container Apps |
| [Azure Database Setup](guides/azure/AZURE-DATABASE-INITIALIZATION.md) | Azure PostgreSQL and MongoDB setup |
| [Azure Custom Domain](guides/azure/AZURE-CUSTOM-DOMAIN-SETUP.md) | Custom domain and SSL configuration |

### Development

| Guide | Description |
|-------|-------------|
| [Claude Code Guidelines](guides/claude-code-guidelines.md) | AI-assisted development conventions |
| [Troubleshooting](guides/TROUBLESHOOTING.md) | Common issues and solutions |

---

## Reference

Architecture, API documentation, and technical specifications.

| Document | Description |
|----------|-------------|
| [API Documentation](reference/API-DOCUMENTATION.md) | Complete REST API reference for all services |
| [Architecture Overview](reference/architecture.md) | High-level system architecture and service interactions |
| [Project Structure](reference/project-structure.md) | Solution layout and project organization |
| [Development Status](reference/development-status.md) | Overall platform completion and recent updates |
| [Security Requirements](reference/SECURITY-REQUIREMENTS.md) | Component placement rules, crypto isolation, threat model |
| [OpenAPI Decisions](reference/openapi-documentation-decisions.md) | Scalar UI configuration and OpenAPI decisions |

### Data & Transactions

| Document | Description |
|----------|-------------|
| [Blockchain Transaction Format](reference/blockchain-transaction-format.md) | JSON-LD transaction model and DID URI addressing |
| [Transaction Submission Flow](reference/transaction-submission-flow.md) | End-to-end transaction lifecycle |
| [Data Persistence](reference/data-persistence-architecture.md) | Storage architecture: PostgreSQL, MongoDB, Redis |

### Cryptography & Wallets

| Document | Description |
|----------|-------------|
| [Wallet Encryption](reference/wallet-encryption-architecture.md) | Key management and encryption provider architecture |
| [Cryptography & Quantum Analysis](reference/cryptography-zkp-quantum-analysis.md) | Post-quantum cryptography, ZK proofs, algorithm analysis |

### Service Reference

| Document | Description |
|----------|-------------|
| [Validator Design](reference/validator-service-design.md) | Validator Service architecture and design |
| [Validator Quick Reference](reference/VALIDATOR-SERVICE-QUICK-REFERENCE.md) | Validator Service API cheat sheet |
| [Architecture Decisions](reference/architecture/) | ADRs (Architecture Decision Records) |
| [Service Status Reports](reference/status/) | Per-service detailed status |

---

## AI Blueprint Design

| Document | Description |
|----------|-------------|
| [Blueprint Examples](ai-prompts/blueprint-examples.md) | Example blueprints for AI assistants |
| [Blueprint Builder Prompt](ai-prompts/blueprint-builder-system-prompt.md) | System prompt for AI-powered blueprint design |

---

## Archive

Historical implementation plans, session summaries, and completion reports are preserved in [archive/](archive/).

---

## Quick Links

- [Main README](../README.md)
- [Development Guide](../DEVELOPMENT.md)
- [Specification & Planning](../.specify/README.md)
- [Walkthroughs](../walkthroughs/README.md)
- [Blueprint Templates](../blueprints/README.md)
- [Platform Specification](../specs/PLATFORM-SPECIFICATION.md)
- [License](../LICENSE)
- [GitHub Repository](https://github.com/Sorcha-Platform/Sorcha)
