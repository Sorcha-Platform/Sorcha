# Sorcha Wallet Service Specification

**Version:** 1.2
**Date:** 2025-12-13
**Status:** Active
**Related Constitution:** [constitution.md](../constitution.md)
**Related Specifications:**
- [sorcha-cryptography-rewrite.md](sorcha-cryptography-rewrite.md)
- [sorcha-transaction-handler.md](sorcha-transaction-handler.md)

## Executive Summary

This specification defines the requirements for the Sorcha Wallet Service. This service handles cryptographic wallet creation, key management, transaction signing, delegation/access control, and secure key storage for the Sorcha decentralised register platform. The implementation follows the standard Sorcha service architecture pattern.

## Background

### Current State Analysis

The Sorcha platform currently lacks a dedicated wallet service. This specification draws from the legacy alpha implementation to create a modern, cloud-native wallet service suitable for the Sorcha architecture.

#### Architecture

**Project Structure:**
- `Sorcha.Wallet.Core` (in src/Common) - Core wallet library (domain models, interfaces, services)
- `Sorcha.Wallet.Service` (in src/Services) - ASP.NET Core Minimal API service
- `Sorcha.Wallet.Service.Tests` - Unit test suite
- `Sorcha.Wallet.Service.IntegrationTests` - Integration test suite
- `Sorcha.Wallet.Service.Api.Tests` - API endpoint test suite

**Key Components:**
- **IWalletService** - Main service interface for wallet operations
- **WalletManager** - Creates/recovers wallets from mnemonics using ED25519/SECP256K1 keys
- **IWalletRepository** - Data persistence with encryption
- **IEncryptionProvider** - Encrypts/decrypts private keys at rest using Azure Key Vault or local providers
- **WalletDbContext** - EF Core DbContext supporting MySQL, PostgreSQL, and Cosmos SQL

### Goals

1. **Separation of Concerns** - Wallet logic separate from HTTP/REST API
2. **Portable Architecture** - Usable in any .NET application context
3. **Pluggable Storage** - Abstract storage layer with multiple implementations
4. **Enhanced Security** - Azure Key Vault support, better key management, audit logging
5. **Comprehensive Testing** - >90% coverage with unit and integration tests
6. **Well-Documented** - Clear API with usage examples
7. **Multi-Algorithm Support** - ED25519, SECP256K1, RSA
8. **HD Wallet Support** - Full BIP32/BIP39/BIP44 implementation
9. **Event-Driven Design** - Integrate with .NET Aspire messaging

## Scope

### In Scope

#### Core Wallet Management

1. **Wallet Creation & Recovery**
   - Generate new wallets with BIP39 mnemonics (12/24 words)
   - Recover wallets from existing mnemonics
   - Support multiple cryptographic algorithms (ED25519, SECP256K1, RSA)
   - Hierarchical Deterministic (HD) wallet support (BIP32/BIP44)
   - Wallet metadata management (name, description, tags)
   - Wallet archiving and soft deletion

2. **Key Management**
   - Master key generation using cryptographically secure RNG
   - Derived key creation with BIP44 paths (m/44'/coin'/account'/change/index)
   - Private key encryption at rest (multiple encryption providers)
   - Public key derivation and address generation
   - Key rotation and versioning support
   - Export keys in WIF (Wallet Import Format)

3. **Address Management**
   - Generate receive addresses from HD paths
   - Track used vs. unused addresses
   - Address labeling and categorization
   - Watch-only address support
   - Multi-signature address generation (future)

4. **Access Control & Delegation**
   - Owner, delegate read-write, delegate read-only roles
   - Subject-based access control (user/service principals)
   - Reason tracking for access grants
   - Time-based access expiration
   - Revocation and audit trail
   - Multi-tenant isolation

5. **Transaction Operations**
   - Sign transactions using wallet private keys
   - Validate transaction signatures
   - Decrypt transaction payloads for wallet recipients
   - Track wallet transaction history
   - UTXO management (unspent transaction outputs)
   - Transaction state tracking (pending, confirmed, spent)

6. **Security Features**
   - Encrypted private key storage (AES-256-GCM)
   - Multiple encryption providers (Azure Key Vault, AWS KMS, local)
   - Hardware Security Module (HSM) integration
   - Key derivation using PBKDF2 or Argon2
   - Secure mnemonic generation and validation
   - Audit logging for all sensitive operations
   - Rate limiting for key operations

#### Storage Abstraction

1. **Repository Pattern**
   - Abstract `IWalletRepository` interface
   - Support for CRUD operations
   - Query support (by owner, by tenant, by address)
   - Transaction support for atomic operations
   - Pagination and filtering

2. **Storage Implementations**
   - Entity Framework Core provider (SQL databases)
   - In-memory provider (testing, caching)
   - Document database provider (MongoDB, CosmosDB)
   - File-based provider (encrypted JSON files)
   - Distributed cache provider (Redis)

3. **Migration Support**
   - Import from legacy alpha wallet database (if needed)
   - Export to portable formats
   - Schema versioning and migrations
   - Backward compatibility considerations

#### Event System

1. **Event Definitions**
   - `WalletCreated` - New wallet generated
   - `WalletRecovered` - Wallet restored from mnemonic
   - `WalletDeleted` - Wallet marked for deletion
   - `AddressGenerated` - New address derived
   - `TransactionSigned` - Transaction signed by wallet
   - `DelegateAdded/Updated/Removed` - Access control changes
   - `KeyRotated` - Encryption key changed

2. **Event Bus Integration**
   - Integrate with .NET Aspire messaging
   - Event handlers with retry and dead-letter support
   - Event versioning and schema evolution

#### API & Client Library

1. **Core Service Interface**
   - `IWalletService` - Main wallet operations
   - `IKeyManagementService` - Key generation and derivation
   - `ITransactionService` - Signing and validation
   - `IDelegationService` - Access control management
   - Dependency injection support with .NET Aspire

2. **API Service**
   - Minimal API endpoints for wallet operations
   - Integration with Sorcha.ApiGateway
   - .NET 10 built-in OpenAPI documentation with Scalar UI
   - Health checks and observability

### Out of Scope

1. **Legacy alpha Migration** - Not migrating from alpha (greenfield implementation)
2. **Multi-signature Coordination** - Future enhancement
3. **Hardware Wallet Integration** - Future enhancement (Ledger, Trezor)
4. **Decentralized Identity (DID)** - Future integration
5. **Cross-chain Support** - Future enhancement

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│              Sorcha.WalletService.Api (Minimal API)             │
│                   Integrated with .NET Aspire                    │
└────────────────────────┬────────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────────┐
│                    Sorcha.WalletService                         │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │              IWalletService (Facade)                     │  │
│  └───┬──────────────────┬──────────────────┬────────────────┘  │
│      │                  │                  │                    │
│  ┌───▼──────┐    ┌──────▼──────┐    ┌─────▼────────────┐      │
│  │  Wallet  │    │     Key     │    │   Transaction    │      │
│  │ Manager  │    │  Manager    │    │     Service      │      │
│  └───┬──────┘    └──────┬──────┘    └─────┬────────────┘      │
│      │                  │                  │                    │
│  ┌───▼──────────────────▼──────────────────▼────────────┐      │
│  │           IWalletRepository (Abstract)               │      │
│  └───┬──────────────────┬──────────────────┬────────────┘      │
│      │                  │                  │                    │
│  ┌───▼────┐      ┌──────▼─────┐      ┌────▼──────┐            │
│  │   EF   │      │  Document  │      │ In-Memory │            │
│  │  Core  │      │     DB     │      │  Storage  │            │
│  └────────┘      └────────────┘      └───────────┘            │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │           IEncryptionProvider (Abstract)                 │  │
│  └───┬──────────────────┬──────────────────┬────────────────┘  │
│      │                  │                  │                    │
│  ┌───▼────┐      ┌──────▼─────┐      ┌────▼──────┐            │
│  │ Azure  │      │    AWS     │      │   Local   │            │
│  │  KV    │      │    KMS     │      │  DPAPI    │            │
│  └────────┘      └────────────┘      └───────────┘            │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │         .NET Aspire Messaging Integration                │  │
│  └───┬──────────────────┬──────────────────┬────────────────┘  │
│      │                  │                  │                    │
│  ┌───▼────┐      ┌──────▼─────┐      ┌────▼──────┐            │
│  │ RabbitMQ│     │   Redis    │      │ In-Memory │            │
│  │  Bus    │     │  Streams   │      │   Events  │            │
│  └────────┘      └────────────┘      └───────────┘            │
└─────────────────────┬───────────────────────────────────────────┘
                      │
                      ▼
           ┌──────────────────────┐
           │ Sorcha.Cryptography  │
           │ (Key Generation,     │
           │  Signing, Hashing)   │
           └──────────────────────┘
                      │
                      ▼
        ┌──────────────────────────────┐
        │ Sorcha.TransactionHandler    │
        │ (Transaction Building,       │
        │  Payload Management)         │
        └──────────────────────────────┘
```

### Integration with Sorcha Platform

**Sorcha.AppHost Integration:**
```csharp
// Add wallet service to Aspire orchestration
var walletService = builder.AddProject<Sorcha_WalletService_Api>("wallet-service")
    .WithReference(redis)
    .WithReference(postgres);

// Add to API Gateway routing
builder.AddProject<Sorcha_ApiGateway>("api-gateway")
    .WithReference(walletService);
```

### Layer Responsibilities

#### 1. Service Layer (`Services/`)

**IWalletService** - Facade for all wallet operations
- Coordinates between managers
- Handles cross-cutting concerns (logging, validation)
- Manages transaction boundaries
- Enforces business rules

**WalletManager** - Wallet lifecycle management
- Create, recover, update, delete wallets
- Manage wallet metadata
- Handle wallet archiving

**KeyManager** - Cryptographic key operations
- Generate master keys from mnemonics
- Derive keys using BIP44 paths
- Encrypt/decrypt private keys
- Key rotation and versioning

**TransactionServiceAdapter** - Transaction operations
- Sign transactions with wallet keys
- Verify transaction signatures
- Decrypt payloads
- Track transaction state

**DelegationManager** - Access control
- Grant, revoke, update delegations
- Validate access permissions
- Audit delegation changes

#### 2. Domain Layer (`Domain/`)

**Entities:**
- `Wallet` - Core wallet entity with encrypted keys
- `WalletAddress` - Derived addresses with paths
- `WalletAccess` - Delegation/access control records
- `WalletTransaction` - Transaction tracking
- `WalletMetadata` - Extended wallet information
- `AuditLog` - Security audit trail

**Value Objects:**
- `Mnemonic` - BIP39 mnemonic phrase
- `PrivateKey` - Encrypted private key
- `PublicKey` - Public key and address
- `DerivationPath` - BIP44 path parser
- `AccessRight` - Permission enumeration

**Domain Events:**
- `WalletCreatedEvent`
- `AddressGeneratedEvent`
- `TransactionSignedEvent`
- `DelegateChangedEvent`

## Data Models

### Wallet Entity

```csharp
public class Wallet
{
    // Primary Key
    public string Address { get; set; }  // Primary wallet address (public key)

    // Cryptographic Data (Encrypted)
    public string EncryptedPrivateKey { get; set; }  // AES-256-GCM encrypted
    public string EncryptionKeyId { get; set; }  // Reference to encryption key
    public string Algorithm { get; set; }  // ED25519, SECP256K1, RSA

    // Ownership & Multi-tenancy
    public string Owner { get; set; }  // User subject (sub claim)
    public string Tenant { get; set; }  // Tenant identifier

    // Metadata
    public string Name { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string>? Tags { get; set; }

    // State
    public WalletStatus Status { get; set; }  // Active, Archived, Deleted
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Relationships
    public ICollection<WalletAddress> Addresses { get; set; }
    public ICollection<WalletAccess> Delegates { get; set; }
    public ICollection<WalletTransaction> Transactions { get; set; }

    // Version & Concurrency
    public int Version { get; set; }
    public byte[] RowVersion { get; set; }  // For optimistic concurrency
}

public enum WalletStatus
{
    Active,
    Archived,
    Deleted,
    Locked
}
```

## Service Interactions

### Service Communication Protocol

**Internal Service-to-Service Communication:**
- **Protocol:** gRPC with Protocol Buffers (per [Constitution v1.3](../constitution.md))
- **Authentication:** Mutual TLS (mTLS) with service certificates
- **User Context:** Delegation tokens via gRPC metadata (`x-delegation-token`)
- **Transport:** HTTP/2 with multiplexing
- **Service Discovery:** .NET Aspire service discovery
- **Error Handling:** gRPC status codes with detailed error messages
- **Related:** [ADR-001: gRPC Service Communication](../adrs/adr-001-grpc-service-communication.md)

**External Client-Facing APIs:**
- **Protocol:** REST/HTTP (via API Gateway)
- **Authentication:** JWT Bearer tokens
- **Documentation:** OpenAPI 3.0 with Scalar UI

### External Service Dependencies

**1. Sorcha.Cryptography (Library)**
- Key generation (ED25519, SECP256K1)
- Signature creation and verification
- Hash functions (SHA-256, SHA-512)
- Mnemonic generation (BIP39)
- **Interaction:** Direct library calls (synchronous)

**2. Sorcha.TransactionHandler (Library)**
- Transaction building and parsing
- Payload encryption/decryption
- Transaction validation
- **Interaction:** Direct library calls (synchronous)

**3. Tenant Service (Authentication Hub)**
- **Used For:** Token validation, delegation token verification
- **gRPC Service:** `sorcha.tenant.v1.TenantService`
- **RPCs:**
  - `IntrospectToken` - Validate JWT tokens
  - `GetDelegationToken` - Issue delegation tokens for service-to-service calls
- **Interaction:** gRPC with mTLS authentication
- **Failure Mode:** Requests fail if Tenant Service unavailable (critical dependency)
- **Protocol Buffer:** `src/Services/Sorcha.Tenant.Service/Protos/tenant.proto`

**4. Register Service (Transaction Ledger)**
- **Used For:** Retrieving transaction history when recovering wallets
- **gRPC Service:** `sorcha.register.v1.RegisterService`
- **RPCs:**
  - `QueryTransactions` - Retrieve wallet transaction history
  - `GetTransactionsByWallet` - Stream wallet transactions
- **Interaction:** gRPC with mTLS and delegation tokens
- **Failure Mode:** Wallet can be created without transaction history (graceful degradation)
- **Protocol Buffer:** `src/Services/Sorcha.Register.Service/Protos/register.proto`

**5. Event Bus (.NET Aspire Messaging)**
- **Published Events:**
  - `WalletCreated` - When new wallet created
  - `AddressGenerated` - When new address derived
  - `TransactionSigned` - When transaction signed
- **Interaction:** Async pub/sub via Aspire messaging
- **Failure Mode:** Events buffered and retried

**6. Encryption Provider (Infrastructure)**
- **Azure Key Vault**: MEK storage and cryptographic operations
- **AWS KMS**: Alternative MEK storage
- **Local DPAPI**: Fallback encryption provider
- **Interaction:** Async encryption/decryption operations
- **Failure Mode:** Service degraded if encryption unavailable

## Testing Strategy

### Unit Tests (Target: >90% Coverage)

**Core Services:**
- `WalletManagerTests` - Wallet creation, recovery, updates
- `KeyManagerTests` - Key generation, derivation, encryption
- `TransactionServiceTests` - Signing, verification, decryption
- `DelegationManagerTests` - Access control logic

### Integration Tests

**Database Tests:**
- PostgreSQL integration with real schema
- Cosmos DB integration
- MongoDB integration (if supported)
- Concurrent access and locking

**Encryption Tests:**
- Azure Key Vault integration (requires Azure resources)
- AWS KMS integration (requires AWS resources)
- Key rotation scenarios

**Event Bus Tests:**
- .NET Aspire messaging integration
- Event ordering and replay

## Success Criteria

### Functional Requirements

✅ Create wallets with BIP39 mnemonics (12/24 words)
✅ Recover wallets from mnemonics
✅ Support ED25519, SECP256K1, RSA algorithms
✅ Generate HD wallet addresses (BIP44)
✅ Sign transactions with wallet keys
✅ Decrypt transaction payloads
✅ Manage delegations (add, update, remove)
✅ Multi-tenant isolation
✅ Event publishing for state changes

### Non-Functional Requirements

✅ >90% unit test coverage
✅ <50ms transaction signing latency (p95)
✅ <100ms payload decryption latency (p95)
✅ Support 10,000+ wallets per tenant
✅ Complete XML API documentation
✅ Security audit passed

## Dependencies

### Internal Dependencies

- **Sorcha.Cryptography** - Key generation, signing, hashing
- **Sorcha.TransactionHandler** - Transaction building and payload management
- **Sorcha.ServiceDefaults** - Shared service configurations

### External Dependencies

- **.NET 10** - Runtime framework
- **Entity Framework Core 10** - ORM for SQL databases
- **Azure.Identity** - Azure authentication
- **Azure.Security.KeyVault.Keys** - Azure Key Vault integration
- **AWSSDK.KeyManagementService** - AWS KMS integration
- **.NET Aspire** - Service orchestration and messaging
- **NBitcoin** - BIP32/BIP39/BIP44 implementation
- **xUnit** - Test framework
- **Moq** - Mocking framework
- **FluentAssertions** - Test assertions

### Infrastructure Dependencies

- **PostgreSQL 14+** or **MySQL 8.0+** - Primary database
- **Azure Key Vault** or **AWS KMS** - Key management
- **.NET Aspire Messaging** - Event bus
- **Redis** - Distributed caching (via .NET Aspire)

## Glossary

- **BIP32** - Bitcoin Improvement Proposal 32: Hierarchical Deterministic Wallets
- **BIP39** - Bitcoin Improvement Proposal 39: Mnemonic code for generating deterministic keys
- **BIP44** - Bitcoin Improvement Proposal 44: Multi-Account Hierarchy for Deterministic Wallets
- **ED25519** - Edwards-curve Digital Signature Algorithm using Curve25519
- **SECP256K1** - Elliptic curve used in Bitcoin's public key cryptography
- **HD Wallet** - Hierarchical Deterministic wallet that can generate many key pairs from a single seed
- **MEK** - Master Encryption Key: Top-level key used to encrypt DEKs
- **DEK** - Data Encryption Key: Key used to encrypt actual data
- **DPAPI** - Data Protection API: Windows encryption service
- **HSM** - Hardware Security Module: Physical device for key management
- **UTXO** - Unspent Transaction Output: Blockchain accounting model

## References

- [BIP32 Specification](https://github.com/bitcoin/bips/blob/master/bip-0032.mediawiki)
- [BIP39 Specification](https://github.com/bitcoin/bips/blob/master/bip-0039.mediawiki)
- [BIP44 Specification](https://github.com/bitcoin/bips/blob/master/bip-0044.mediawiki)
- [.NET Aspire Documentation](https://learn.microsoft.com/dotnet/aspire)
- [Azure Key Vault Best Practices](https://docs.microsoft.com/en-us/azure/key-vault/general/best-practices)
- [EF Core Documentation](https://docs.microsoft.com/en-us/ef/core/)

## Related Specifications

- [sorcha-cryptography-rewrite.md](sorcha-cryptography-rewrite.md) - Cryptography library specification
- [sorcha-transaction-handler.md](sorcha-transaction-handler.md) - Transaction handler specification
- [constitution.md](../constitution.md) - Project constitution and principles

---

**Document Status:** Draft for Review
**Next Review Date:** 2025-11-20
**Approval Required From:** Architecture Team, Security Team
