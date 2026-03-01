# Sorcha Documentation

Comprehensive documentation for the Sorcha distributed ledger platform.

**Current Status:** 100% MVD (Minimum Viable Deployment) | [Detailed Status Report](development-status.md)

---

## Quick Start

| Guide | Description |
|-------|-------------|
| [Getting Started](getting-started.md) | Prerequisites, first run, service overview |
| [Docker Quick Start](DOCKER-QUICK-START.md) | Start all services with Docker Compose |
| [Docker Development Workflow](DOCKER-DEVELOPMENT-WORKFLOW.md) | Rebuild, hot reload, and debug with Docker |
| [First Run Setup](FIRST-RUN-SETUP.md) | Bootstrap credentials and initial configuration |
| [Port Configuration](PORT-CONFIGURATION.md) | Complete port assignments for Docker, Aspire, and individual services |

---

## Architecture & Design

| Document | Description |
|----------|-------------|
| [Architecture Overview](architecture.md) | High-level system architecture and service interactions |
| [Data Persistence](data-persistence-architecture.md) | Storage architecture: PostgreSQL, MongoDB, Redis |
| [Blockchain Transaction Format](blockchain-transaction-format.md) | JSON-LD transaction model and DID URI addressing |
| [Transaction Submission Flow](transaction-submission-flow.md) | End-to-end transaction lifecycle |
| [Wallet Encryption](wallet-encryption-architecture.md) | Key management and encryption provider architecture |
| [Cryptography & Quantum Analysis](cryptography-zkp-quantum-analysis.md) | Post-quantum cryptography, ZK proofs, algorithm analysis |

---

## Blueprint System

| Document | Description |
|----------|-------------|
| [Blueprint Quick Start](blueprint-quick-start.md) | Create your first blueprint in 5 minutes |
| [Blueprint Format](blueprint-format.md) | JSON/YAML blueprint specification |
| [Blueprint Architecture](blueprint-architecture.md) | 4-step execution pipeline: validate, calculate, route, disclose |
| [JSON-LD Quick Reference](json-ld-quick-reference.md) | JSON-LD context usage in Sorcha models |
| [JSON-LD Implementation](json-ld-implementation-summary.md) | JSON-LD integration status and patterns |
| [JSON-e Templates](json-e-templates.md) | Template language for dynamic payload generation |
| [JSON Logic Guide](json-logic-guide.md) | Conditional routing and calculation expressions |

---

## API & Integration

| Document | Description |
|----------|-------------|
| [API Documentation](API-DOCUMENTATION.md) | Complete REST API reference for all services |
| [Integration Guide](INTEGRATION-GUIDE.md) | SDK patterns, wallet integration, SignalR examples |
| [Payload Serialization](PAYLOAD-SERIALIZATION-GUIDE.md) | Transaction serialization, Base64url encoding, hash computation |
| [OpenAPI Documentation](openapi-documentation-decisions.md) | Scalar UI configuration and OpenAPI decisions |

---

## Security & Authentication

| Document | Description |
|----------|-------------|
| [Authentication Setup](AUTHENTICATION-SETUP.md) | JWT Bearer configuration, delegation tokens, policies |
| [JWT Configuration](JWT-CONFIGURATION.md) | Token lifetimes, signing keys, service-to-service auth |
| [Bootstrap Credentials](BOOTSTRAP-CREDENTIALS.md) | Default credentials for development environments |
| [Security Requirements](SECURITY-REQUIREMENTS.md) | Component placement rules, crypto isolation, threat model |
| [Infrastructure Setup](INFRASTRUCTURE-SETUP.md) | PostgreSQL, MongoDB, Redis deployment and configuration |

---

## Development

| Document | Description |
|----------|-------------|
| [Testing Guide](testing.md) | Test strategy, naming conventions, coverage targets |
| [NuGet Setup](nuget-setup-guide.md) | Package management and private feed configuration |
| [Claude Code Guidelines](claude-code-guidelines.md) | AI-assisted development conventions |
| [Validator Quick Reference](VALIDATOR-SERVICE-QUICK-REFERENCE.md) | Validator Service API cheat sheet |

---

## Deployment

| Document | Description |
|----------|-------------|
| [Azure Deployment Quick Start](AZURE-DEPLOYMENT-QUICK-START.md) | Deploy to Azure Container Apps |
| [Azure Database Initialization](AZURE-DATABASE-INITIALIZATION.md) | Azure PostgreSQL and MongoDB setup |
| [Azure Custom Domain](AZURE-CUSTOM-DOMAIN-SETUP.md) | Custom domain and SSL configuration |

---

## Status & Tracking

| Document | Description |
|----------|-------------|
| [Development Status](development-status.md) | Overall platform completion and recent updates |
| [Service Status Reports](status/) | Per-service detailed status (Blueprint, Wallet, Register, Peer, Validator, Tenant) |
| [Architecture Decisions](architecture/) | ADRs (Architecture Decision Records) |

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
- [Specification & Planning](../.specify/README.md)
- [Walkthroughs](../walkthroughs/README.md)
- [Sample Blueprints](../samples/blueprints/README.md)
- [License](../LICENSE)
- [GitHub Repository](https://github.com/Sorcha-Platform/Sorcha)
