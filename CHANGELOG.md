# Changelog

All notable changes to the Sorcha project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.1] - 2025-11-16

### Added
- **SignalR Integration Tests for Blueprint Service**
  - 14 comprehensive tests (520+ lines) covering all hub functionality
  - Hub connection/disconnection lifecycle tests
  - Wallet subscription/unsubscription with error handling
  - All notification types: ActionAvailable, ActionConfirmed, ActionRejected
  - Multi-client broadcast scenarios
  - Wallet-specific notification isolation
  - Post-unsubscribe notification filtering

### Changed
- Blueprint-Action Service completion upgraded from 95% to 100%
- Overall platform completion increased from 90% to 92%
- Test coverage for Blueprint Service increased from 85% to >90%
- Resolved Issue #3: Missing SignalR Integration Tests

## [0.8.0] - 2025-11-16

### Added - Major Features
- **Wallet Service (90% Complete)**
  - Complete core implementation with HD wallet support (BIP32/BIP39/BIP44)
  - Domain model: Wallet, WalletAddress, WalletAccess, WalletTransaction
  - Service layer: WalletManager, KeyManagementService, TransactionService, DelegationService
  - Infrastructure: InMemoryRepository, LocalEncryptionProvider, EventPublisher
  - REST API endpoints (Phase 2): create, get, sign, decrypt, generate address
  - Comprehensive unit and integration tests (WS-030, WS-031)
  - Integration with Sorcha.Cryptography for all crypto operations

- **Portable Blueprint Execution Engine (100% Complete)**
  - Stateless engine for client-side (Blazor WASM) and server-side execution
  - JSON Schema validation (Draft 2020-12)
  - JSON Logic evaluation for calculations and conditions
  - Selective data disclosure using JSON Pointers (RFC 6901)
  - Conditional routing between participants
  - Thread-safe, immutable design pattern
  - 93 unit tests + 9 integration tests
  - Real-world scenarios: loan applications, purchase orders, multi-step surveys

- **Unified Blueprint-Action Service (Sprints 3-5 Complete)**
  - Sprint 3: Service layer foundation
    - ActionResolverService - Action resolution from blueprints
    - PayloadResolverService - Encryption/decryption orchestration
    - TransactionBuilderService - Transaction building
    - Redis caching layer
  - Sprint 4: Action API Endpoints
    - GET /api/actions/{wallet}/{register}/blueprints
    - GET /api/actions/{wallet}/{register} (paginated)
    - GET /api/actions/{wallet}/{register}/{tx}
    - POST /api/actions (submit)
    - POST /api/actions/reject
    - GET /api/files/{wallet}/{register}/{tx}/{fileId}
  - Sprint 5: Execution Helpers & SignalR
    - POST /api/execution/validate
    - POST /api/execution/calculate
    - POST /api/execution/route
    - POST /api/execution/disclose
    - SignalR ActionsHub for real-time notifications
    - Redis backplane for scalability

- **Validator Service Design**
  - Complete design and implementation plan
  - Core validation library specification (Sorcha.Validator.Core)
  - Service infrastructure design
  - Consensus engine design (Simple Quorum)
  - 10-week implementation roadmap

- **Register Service Integration**
  - Infrastructure integration with Wallet and Blueprint services
  - Stub implementation for graceful degradation
  - Transaction submission and retrieval interfaces

### Added - Infrastructure
- SignalR real-time notifications with Redis backplane
- Enhanced health check endpoints for Blueprint and Peer services
- API Gateway enhancements (health aggregation, client download, OpenAPI aggregation)
- Comprehensive integration tests across services
- Performance testing with NBomber

### Changed
- Blueprint Service evolved to Unified Blueprint-Action Service
- Overall project completion: 70% → 80%
- Enhanced test coverage across all components
- Updated all .NET projects to target .NET 10 only (removed multi-targeting)

### Fixed
- Multiple build errors across Blueprint Engine and tests
- Type conversion errors in ExecutionEngineTests
- Namespace conflicts in Blueprint Engine tests
- FluentAssertions method name corrections
- Port binding permission errors (changed to safer port range)
- Cryptography library updates for .NET 10 compatibility

## [Unreleased]

### Added
- Initial project structure with .NET 10
- .NET Aspire orchestration for cloud-native development
- Blueprint Engine service with minimal APIs
- Blueprint Designer web UI with Blazor Server
- Service Defaults for shared configurations
- OpenTelemetry integration for observability
- Health check endpoints
- Service discovery support
- GitHub Actions CI/CD workflows
  - Build and test workflow
  - Release workflow with NuGet and Docker support
  - CodeQL security analysis
- Comprehensive documentation
  - Architecture overview
  - Getting started guide
  - Blueprint schema specification
  - Contributing guidelines
- MIT License
- .gitignore and .gitattributes for repository hygiene

### Changed
- Built from the Sorcha AI Development spike
- Modernized to .NET 10 from .NET 8/9
- Simplified microservices to focus on core blueprint execution
- Adopted minimal API pattern for REST endpoints
- Replaced custom orchestration with .NET Aspire

### Removed
- Legacy dependencies (IdentityServer4, Dapr)
- Domain-specific services (to be re-added as needed)

## [0.1.0] - TBD

### Planned
- [ ] Core blueprint schema implementation
- [ ] Blueprint validation engine
- [ ] Basic execution engine
- [ ] Visual designer prototype
- [ ] Unit test coverage
- [ ] Integration tests
- [ ] API documentation
- [ ] Docker support
- [ ] Kubernetes manifests

---

## Version History

### Versioning Strategy

- **Major version (X.0.0)**: Breaking changes
- **Minor version (0.X.0)**: New features, backwards compatible
- **Patch version (0.0.X)**: Bug fixes

### Release Cycle

- **Alpha**: Internal testing, frequent changes
- **Beta**: Public preview, feature complete
- **RC**: Release candidate, production ready
- **Stable**: General availability

---

[Unreleased]: https://github.com/yourusername/sorcha/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/yourusername/sorcha/releases/tag/v0.1.0
