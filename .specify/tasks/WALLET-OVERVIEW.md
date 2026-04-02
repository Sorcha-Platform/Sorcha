# Sorcha.WalletService - Task Overview

**Last Updated:** 2025-11-13
**Status:** Planning Phase
**Related Specification:** [sorcha-wallet-service.md](../specs/sorcha-wallet-service.md)

## Summary

This document provides an overview of all tasks required to complete the Sorcha.WalletService library. The library will handle cryptographic wallet creation, key management, transaction signing, delegation/access control, and secure key storage for the Sorcha decentralised register platform.

## Task Dependencies

```
WALLET-001 (Project Setup)
    ├── WALLET-002 (Domain Models & Enums)
    │   ├── WALLET-003 (Service Interfaces)
    │   ├── WALLET-004 (WalletManager Implementation)
    │   ├── WALLET-005 (KeyManager Implementation)
    │   ├── WALLET-006 (TransactionService Implementation)
    │   ├── WALLET-007 (DelegationManager Implementation)
    │   └── WALLET-008 (Repository Abstractions)
    ├── WALLET-009 (EF Core Repository)
    ├── WALLET-010 (In-Memory Repository)
    ├── WALLET-011 (Encryption Providers)
    │   ├── WALLET-012 (Azure Key Vault Provider)
    │   ├── WALLET-013 (AWS KMS Provider)
    │   └── WALLET-014 (Local AES-GCM Provider)
    ├── WALLET-015 (Event System)
    │   ├── WALLET-016 (.NET Aspire Messaging Integration)
    │   └── WALLET-017 (In-Memory Event Bus)
    ├── WALLET-018 (Setup Test Project)
    │   ├── WALLET-019 (Unit Tests - Core Services)
    │   ├── WALLET-020 (Unit Tests - Repositories)
    │   ├── WALLET-021 (Unit Tests - Encryption)
    │   ├── WALLET-022 (Integration Tests - Database)
    │   ├── WALLET-023 (Integration Tests - Events)
    │   └── WALLET-024 (End-to-End Tests)
    ├── WALLET-025 (API Layer)
    │   ├── WALLET-026 (Minimal API Endpoints)
    │   └── WALLET-027 (Aspire Integration)
    ├── WALLET-028 (Performance Benchmarks)
    ├── WALLET-029 (Security Tests)
    ├── WALLET-030 (XML Documentation)
    ├── WALLET-031 (Integration Guide)
    └── WALLET-032 (Deployment & Rollout)
```

## Task List

### Phase 1: Foundation (Weeks 1-2)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-001 | Setup Sorcha.WalletService Project | Critical | 6 | Not Started | - |
| WALLET-002 | Implement Domain Models & Enums | Critical | 12 | Not Started | - |
| WALLET-003 | Define Service Interfaces | Critical | 8 | Not Started | - |
| WALLET-018 | Setup Test Project Structure | High | 6 | Not Started | - |

**Deliverables:**
- New Sorcha.WalletService project with proper structure
- All domain models (Wallet, WalletAddress, WalletAccess, WalletTransaction)
- Service interfaces (IWalletService, IKeyManagementService, etc.)
- Test project with xUnit, Moq, FluentAssertions
- Depends on Sorcha.Cryptography and Sorcha.TransactionHandler

### Phase 2: Core Services (Weeks 3-5)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-004 | Implement WalletManager | Critical | 20 | Not Started | - |
| WALLET-005 | Implement KeyManager | Critical | 24 | Not Started | - |
| WALLET-006 | Implement TransactionService | Critical | 16 | Not Started | - |
| WALLET-007 | Implement DelegationManager | High | 12 | Not Started | - |
| WALLET-019 | Unit Tests - Core Services | Critical | 24 | Not Started | - |

**Deliverables:**
- Wallet creation, recovery, update, delete operations
- HD wallet support (BIP32/BIP39/BIP44)
- Transaction signing and verification
- Access control and delegation management
- >90% unit test coverage for core services

### Phase 3: Storage Layer (Weeks 6-7)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-008 | Repository Abstractions | Critical | 8 | Not Started | - |
| WALLET-009 | EF Core Repository Implementation | Critical | 20 | Not Started | - |
| WALLET-010 | In-Memory Repository Implementation | High | 12 | Not Started | - |
| WALLET-020 | Unit Tests - Repositories | Critical | 16 | Not Started | - |
| WALLET-022 | Integration Tests - Database | Critical | 20 | Not Started | - |

**Deliverables:**
- IWalletRepository and ITransactionRepository interfaces
- EF Core implementation with PostgreSQL/MySQL support
- In-memory implementation for testing
- Database migrations
- Integration tests with real databases (Testcontainers)

### Phase 4: Encryption Layer (Weeks 8-9)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-011 | Encryption Provider Abstractions | Critical | 6 | Not Started | - |
| WALLET-012 | Azure Key Vault Provider | High | 16 | Not Started | - |
| WALLET-013 | AWS KMS Provider | Medium | 16 | Not Started | - |
| WALLET-014 | Local AES-GCM Provider | High | 12 | Not Started | - |
| WALLET-021 | Unit Tests - Encryption | Critical | 16 | Not Started | - |

**Deliverables:**
- IEncryptionProvider interface
- Azure Key Vault integration
- AWS KMS integration
- Local AES-256-GCM fallback
- Key rotation support
- Unit tests with mock providers

### Phase 5: Event System (Week 10)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-015 | Event Model Definitions | Critical | 6 | Not Started | - |
| WALLET-016 | .NET Aspire Messaging Integration | Critical | 12 | Not Started | - |
| WALLET-017 | In-Memory Event Bus Implementation | High | 8 | Not Started | - |
| WALLET-023 | Integration Tests - Events | Critical | 16 | Not Started | - |

**Deliverables:**
- WalletCreated, AddressGenerated, TransactionSigned events
- .NET Aspire messaging integration
- In-memory event bus for testing
- Event integration tests

### Phase 6: API Layer (Weeks 11-12)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-025 | Sorcha.WalletService.Api Project | Critical | 12 | Not Started | - |
| WALLET-026 | Minimal API Endpoints | Critical | 20 | Not Started | - |
| WALLET-027 | Aspire Integration | Critical | 16 | Not Started | - |
| WALLET-024 | End-to-End Tests | Critical | 24 | Not Started | - |

**Deliverables:**
- New API project using Minimal APIs
- Wallet CRUD endpoints
- Integrate with Sorcha.AppHost
- Integrate with Sorcha.ApiGateway
- API integration tests
- .NET 10 built-in OpenAPI documentation with Scalar UI

### Phase 7: Testing & Performance (Week 13)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-028 | Performance Benchmarks | High | 16 | Not Started | - |
| WALLET-029 | Security Tests & Audit | Critical | 24 | Not Started | - |

**Deliverables:**
- BenchmarkDotNet performance tests
- Load tests (10,000+ wallets, 1,000 concurrent ops)
- Security audit (OWASP Top 10)
- Performance report

### Phase 8: Documentation (Week 14)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-030 | XML Documentation & API Reference | Critical | 12 | Not Started | - |
| WALLET-031 | Integration Guide | High | 12 | Not Started | - |

**Deliverables:**
- Complete XML documentation for all public APIs
- Integration guide (DI setup, configuration)
- Security best practices guide
- Code examples

### Phase 9: Deployment (Week 15)

| ID | Task | Priority | Est. Hours | Status | Assignee |
|----|------|----------|------------|--------|----------|
| WALLET-032 | Deployment & Rollout | Critical | 24 | Not Started | - |

**Deliverables:**
- NuGet package for Sorcha.WalletService
- Aspire orchestration configuration
- Production deployment plan
- Monitoring and alerting
- CI/CD pipeline configuration

## Estimated Totals

**Total Tasks:** 32
**Total Estimated Hours:** 456 hours (~11-12 weeks with 2 developers)
**Timeline:** 15 weeks with buffer for testing, security, and documentation

## Risk Areas Requiring Extra Attention

1. **Encryption Providers (WALLET-012, WALLET-013)** - Security critical
2. **Performance (WALLET-028)** - Must meet scalability requirements
3. **Security Audit (WALLET-029)** - Required before production deployment
4. **Aspire Integration (WALLET-027)** - Critical for cloud-native architecture

## Success Metrics

- ✅ All 32 tasks completed
- ✅ >90% unit test coverage
- ✅ All integration tests passing
- ✅ Performance benchmarks met (<50ms signing, <100ms decryption)
- ✅ Security audit passed
- ✅ Complete documentation
- ✅ Successful Aspire integration

## Notes

- Tasks are designed to be parallelizable where dependencies allow
- Each task should have acceptance criteria defined before starting
- Code reviews required for all critical path tasks
- Weekly progress reviews recommended
- Focus on .NET 10 and .NET Aspire integration throughout

---

**Next Steps:**
1. Review and approve this task breakdown
2. Assign developers to Phase 1 tasks
3. Setup project repository structure
4. Begin WALLET-001 (Project Setup)
