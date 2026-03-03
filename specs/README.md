# Sorcha Feature Specifications

This directory contains detailed specifications for every feature built into the Sorcha platform. Each subdirectory includes data models, API contracts, implementation plans, and test criteria.

For a consolidated view of the entire platform suitable for AI consumption, see [PLATFORM-SPECIFICATION.md](PLATFORM-SPECIFICATION.md).

## Feature Index

### Foundation (001-series)

| ID | Feature | Description |
|----|---------|-------------|
| 001-blueprint-chat | AI Blueprint Design | Claude AI-assisted interactive blueprint design |
| 001-designer-completion | Blueprint Designer | Visual blueprint designer completion |
| 001-hardware-crypto-enclaves | Hardware Crypto | Hardware cryptographic storage and execution enclaves |
| 001-mcp-server | MCP Server | Model Context Protocol server for AI assistant integration |
| 001-participant-identity | Participant Identity | User-to-participant mapping with wallet linking |
| 001-participant-records | Published Participants | On-register published participant records |
| 001-register-genesis | Register Genesis | Peer service hub node connection and genesis creation |
| 001-tenant-auth | Tenant Auth | Multi-tenant authentication, JWT, service principals |
| 001-ui-token-refresh | UI Token Refresh | Authentication token management and login UX |
| 001-validator-service-wallet | Validator Wallet | System wallet initialization for validator service |
| 002-storage-abstraction | Storage Abstraction | Multi-tier storage with EF Core, MongoDB, Redis, InMemory |
| 002-validator-service | Validator Service | Memory pool, docket building, consensus |

### UI & Admin (011-013)

| ID | Feature | Description |
|----|---------|-------------|
| 011-admin-dashboard | Admin Dashboard | Administrative dashboard and management UI |
| 012-registers-transactions-ui | Register UI | Registers and transactions UI components |
| 013-system-schema-store | Schema Store | System-wide schema store with caching |

### Core Features (014-031)

| ID | Feature | Description |
|----|---------|-------------|
| 014-client-zkp | Browser Crypto | Browser-side zero-knowledge proof capabilities |
| 015-fix-register-crypto | Register Crypto Fix | Fully functional cryptographic register flow |
| 016-cli-register-update | CLI Register | CLI commands for register management |
| 017-ui-register-management | UI Register Mgmt | Wallet selection wizard, search/filter, transaction query |
| 018-blueprint-engine-integration | Engine Integration | Blueprint engine wired into service pipeline |
| 019-payload-encryption | Payload Encryption | DAD model payload encryption specification |
| 020-validator-engine-validation | Validator Engine | Schema and chain validation in validator |
| 021-transaction-query-api | Transaction Query | OData-compatible transaction query API |
| 022-resolve-runtime-stubs | Resolve Stubs | Replace runtime stub implementations |
| 023-consolidate-tx-versioning | TX Versioning | Unified transaction version handling |
| 024-peer-network-management | Peer Management | P2P network topology and observability |
| 025-ui-modernization | UI Modernization | Comprehensive UI overhaul and modernization |
| 026-fix-register-creation-pipeline | Register Pipeline | Register creation end-to-end fix |
| 027-blueprint-template-library | Template Library | Blueprint template library with ping-pong starter |
| 028-fix-transaction-pipeline | TX Pipeline Fix | Transaction submission pipeline corrections |
| 029-blueprint-visual-designer | Visual Designer | Drag-and-drop blueprint design UI |
| 030-peer-advertisement-resync | Peer Resync | Register-to-peer advertisement resynchronization |
| 031-register-governance | Register Governance | On-ledger governance rules and voting |
| 031-verifiable-credentials | Verifiable Credentials | SD-JWT VC, credential gating, selective disclosure |

### Advanced Features (032-047)

| ID | Feature | Description |
|----|---------|-------------|
| 032-action-form-renderer | Form Renderer | Dynamic action form rendering from schemas |
| 033-fix-wallet-dashboard-bugs | Wallet Dashboard | Wallet dashboard navigation and bug fixes |
| 034-schema-library | Schema Library | Reusable schema library with versioning |
| 036-unified-transaction-submission | Unified TX Submit | Single transaction submission path |
| 037-new-submission-page | Submission Page | New transaction submission UI |
| 038-content-type-payload | Content-Type Payload | Content-type aware payload encoding |
| 039-verifiable-presentations | VP Lifecycle | Verifiable credential lifecycle and presentations |
| 040-quantum-safe-crypto | Quantum-Safe Crypto | ML-DSA, ML-KEM, SLH-DSA, BLS12-381 |
| 041-auth-integration | Auth Integration | JWT auth across all services |
| 043-ui-cli-modernization | UI/CLI Modernize | Spectre.Console, MudBlazor upgrade |
| 044-codebase-consolidation | Consolidation | Shared policies, deduplicated code |
| 045-encrypted-payload-integration | Encrypted Payloads | Envelope encryption with per-recipient key wrapping |
| 046-ui-polish-designer | UI Polish | Dashboard wizard, notifications, dark mode, i18n |
| 047-inbound-tx-routing | Inbound TX Routing | Bloom filter routing, wallet notifications, recovery |

## Spec Structure

Each feature directory typically contains:

```
specs/NNN-feature-name/
├── spec.md              # Main specification (data models, requirements)
├── contracts/           # API contracts and interface definitions
│   └── README.md
├── code/                # Implementation code artifacts
└── plan.md              # Implementation plan and task breakdown
```

## Related Documents

- [.specify/constitution.md](../.specify/constitution.md) — Architectural principles
- [.specify/MASTER-PLAN.md](../.specify/MASTER-PLAN.md) — Development roadmap
- [.specify/MASTER-TASKS.md](../.specify/MASTER-TASKS.md) — Task tracking
- [docs/architecture.md](../docs/architecture.md) — System architecture
- [docs/API-DOCUMENTATION.md](../docs/API-DOCUMENTATION.md) — API reference
