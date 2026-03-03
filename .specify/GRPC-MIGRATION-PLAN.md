# gRPC Migration Plan - Sorcha Platform

**Version:** 1.0
**Created:** 2025-12-13
**Status:** Planning
**Related ADR:** [ADR-001: Adopt gRPC for Internal Service Communication](adrs/adr-001-grpc-service-communication.md)
**Related Constitution:** [Constitution v1.3](constitution.md) - Service Communication Standards

---

## Executive Summary

This document provides a detailed, task-level migration plan for transitioning Sorcha's internal service communication from HTTP REST to gRPC. The migration follows a phased approach to minimize risk and maintain system stability.

**Total Effort:** 144 hours (~18 business days)
**Team Size:** 2-3 developers
**Timeline:** 8 weeks (part-time effort)
**Risk Level:** Medium (mitigated by dual-stack approach)

---

## Table of Contents

1. [Current State Assessment](#current-state-assessment)
2. [Migration Phases](#migration-phases)
3. [Detailed Task Breakdown](#detailed-task-breakdown)
4. [Service-Specific Migration Plans](#service-specific-migration-plans)
5. [Testing Strategy](#testing-strategy)
6. [Rollback Plan](#rollback-plan)
7. [Success Metrics](#success-metrics)

---

## Current State Assessment

### Services to Migrate

| Service | Current State | Lines of Code | Integration Tests | Priority |
|---------|---------------|---------------|-------------------|----------|
| **Tenant Service** | 85% complete, JWT auth | ~5,150 LOC | 67 tests (91% passing) | P0 - Auth hub |
| **Blueprint Service** | 100% complete | ~2,250 LOC | 123 tests | P1 - Orchestrator |
| **Wallet Service** | 95% complete | ~2,400 LOC | 67 tests | P1 - Crypto ops |
| **Register Service** | 100% complete | ~4,150 LOC | 112 tests | P1 - Ledger |
| **TOTAL** | 98% platform complete | **~13,950 LOC** | **369 tests** | |

### Current Service Communication Patterns

```
Blueprint Service (Orchestrator)
├─ HTTP POST → Tenant Service (introspect token)
├─ HTTP POST → Tenant Service (get delegation token)
├─ HTTP POST → Wallet Service (sign transaction)
├─ HTTP POST → Wallet Service (decrypt payload)
└─ HTTP POST → Register Service (submit transaction)

Wallet Service
└─ HTTP POST → Tenant Service (validate token)

Register Service
└─ HTTP POST → Tenant Service (validate token)
```

### Current Authentication Flow

1. Client sends request with JWT to Blueprint Service
2. Blueprint extracts `X-Delegation-Token` header
3. Blueprint calls Tenant.IntrospectToken(JWT) via HTTP POST
4. Blueprint calls Tenant.GetDelegationToken() via HTTP POST
5. Blueprint calls Wallet.SignTransaction() with delegation token via HTTP POST
6. Blueprint calls Register.SubmitTransaction() via HTTP POST

**This entire flow will be converted to gRPC.**

---

## Migration Phases

### Phase 1: Infrastructure Setup (Week 1-2, 24 hours)

**Goal:** Establish gRPC infrastructure without touching service logic

**Deliverables:**
- mTLS certificate infrastructure
- .proto file definitions for all services
- gRPC NuGet packages added
- .NET Aspire gRPC configuration

**No service downtime required.**

---

### Phase 2: Dual-Stack Implementation (Week 3-4, 40 hours)

**Goal:** Implement gRPC services alongside existing REST endpoints

**Deliverables:**
- gRPC services running parallel to REST
- gRPC interceptors for auth and delegation
- Both protocols functional

**No service downtime required.**

---

### Phase 3: Service Migration (Week 5-7, 64 hours)

**Goal:** Migrate service-to-service calls from REST to gRPC

**Deliverables:**
- Blueprint Service uses gRPC clients
- Wallet/Register services communicate via gRPC
- Integration tests migrated

**Requires rolling deployments.**

---

### Phase 4: Validation and Cleanup (Week 8, 16 hours)

**Goal:** Remove dual-stack, validate performance, update docs

**Deliverables:**
- Internal REST endpoints removed
- Performance benchmarks validated
- Documentation updated

**Final cutover.**

---

## Detailed Task Breakdown

### Phase 1: Infrastructure Setup (24 hours)

#### Task 1.1: Certificate Infrastructure (8 hours)
**Priority:** P0
**Assignee:** DevOps/Infrastructure

**Subtasks:**
- [ ] Create Azure Key Vault for certificate storage (1h)
- [ ] Generate root CA certificate for mTLS (1h)
- [ ] Generate service certificates for each service (2h):
  - `tenant-service.sorcha.io`
  - `blueprint-service.sorcha.io`
  - `wallet-service.sorcha.io`
  - `register-service.sorcha.io`
- [ ] Configure certificate rotation automation (2h)
- [ ] Document certificate management procedures (1h)
- [ ] Test certificate validation locally (1h)

**Acceptance Criteria:**
- All service certificates stored in Azure Key Vault
- Certificates expire in 90 days with auto-renewal
- Local development uses self-signed certs from Key Vault

**Files to Create:**
- `infrastructure/certificates/generate-certs.ps1`
- `infrastructure/certificates/README.md`
- `docs/CERTIFICATE-MANAGEMENT.md`

---

#### Task 1.2: Protocol Buffer Definitions (10 hours)
**Priority:** P0
**Assignee:** Backend Developer

**Subtasks:**

**1.2.1: Tenant Service Proto (3h)**
- [ ] Create `src/Services/Sorcha.Tenant.Service/Protos/tenant.proto`
- [ ] Define `TenantService` with RPCs:
  ```protobuf
  service TenantService {
    rpc IntrospectToken(IntrospectTokenRequest) returns (IntrospectTokenResponse);
    rpc GetDelegationToken(GetDelegationTokenRequest) returns (GetDelegationTokenResponse);
    rpc GetServiceToken(GetServiceTokenRequest) returns (GetServiceTokenResponse);
  }
  ```
- [ ] Define all message types with delegation token support
- [ ] Add inline documentation to all fields
- [ ] Version as `sorcha.tenant.v1`

**1.2.2: Wallet Service Proto (3h)**
- [ ] Create `src/Services/Sorcha.Wallet.Service/Protos/wallet.proto`
- [ ] Define `WalletService` with RPCs:
  ```protobuf
  service WalletService {
    rpc SignTransaction(SignTransactionRequest) returns (SignTransactionResponse);
    rpc DecryptPayload(DecryptPayloadRequest) returns (DecryptPayloadResponse);
    rpc EncryptPayload(EncryptPayloadRequest) returns (EncryptPayloadResponse);
  }
  ```
- [ ] Define message types matching existing REST DTOs
- [ ] Add delegation token metadata support
- [ ] Version as `sorcha.wallet.v1`

**1.2.3: Register Service Proto (3h)**
- [ ] Create `src/Services/Sorcha.Register.Service/Protos/register.proto`
- [ ] Define `RegisterService` with RPCs:
  ```protobuf
  service RegisterService {
    rpc SubmitTransaction(SubmitTransactionRequest) returns (SubmitTransactionResponse);
    rpc QueryTransactions(QueryTransactionsRequest) returns (QueryTransactionsResponse);
    rpc GetTransactionsByInstanceId(GetTransactionsByInstanceIdRequest) returns (stream Transaction);
  }
  ```
- [ ] Define message types matching existing models
- [ ] Add streaming support for transaction queries
- [ ] Version as `sorcha.register.v1`

**1.2.4: Blueprint Service Proto (1h)**
- [ ] Create `src/Services/Sorcha.Blueprint.Service/Protos/blueprint.proto` (for future use)
- [ ] Document internal use only (not exposed via API Gateway)

**Acceptance Criteria:**
- All .proto files compile without errors
- All message types documented with inline comments
- Version namespaces follow convention: `sorcha.<service>.v1`
- All RPCs support delegation token via metadata

**Files to Create:**
- `src/Services/Sorcha.Tenant.Service/Protos/tenant.proto`
- `src/Services/Sorcha.Wallet.Service/Protos/wallet.proto`
- `src/Services/Sorcha.Register.Service/Protos/register.proto`
- `src/Services/Sorcha.Blueprint.Service/Protos/blueprint.proto`

---

#### Task 1.3: Add gRPC NuGet Packages (2 hours)
**Priority:** P0
**Assignee:** Backend Developer

**Subtasks:**
- [ ] Add to all service projects:
  - `Grpc.AspNetCore` v2.60.0
  - `Grpc.Tools` v2.60.0
  - `Google.Protobuf` v3.25.0
- [ ] Add to Blueprint Service (client):
  - `Grpc.Net.Client` v2.60.0
  - `Grpc.Net.ClientFactory` v2.60.0
- [ ] Configure .csproj to generate C# from .proto files
- [ ] Verify all packages restore successfully
- [ ] Document package versions in CONTRIBUTING.md

**Acceptance Criteria:**
- All projects build successfully with new packages
- .proto files generate C# classes automatically on build
- No package version conflicts

**Files to Modify:**
- `src/Services/Sorcha.Tenant.Service/Sorcha.Tenant.Service.csproj`
- `src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj`
- `src/Services/Sorcha.Register.Service/Sorcha.Register.Service.csproj`
- `src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj`

---

#### Task 1.4: Configure .NET Aspire for gRPC (4 hours)
**Priority:** P0
**Assignee:** Infrastructure Developer

**Subtasks:**
- [ ] Update `Sorcha.AppHost` to register gRPC endpoints
- [ ] Configure service discovery for gRPC ports
- [ ] Add gRPC health checks to all services
- [ ] Configure HTTP/2 in .NET Aspire
- [ ] Test service discovery with gRPC clients
- [ ] Document Aspire gRPC configuration

**Acceptance Criteria:**
- Services discoverable via gRPC service discovery
- Health checks use gRPC health protocol
- HTTP/2 enabled for all internal services
- Local development works with Aspire Dashboard

**Files to Modify:**
- `src/Apps/Sorcha.AppHost/Program.cs`
- `src/Apps/Sorcha.AppHost/appsettings.json`

**Files to Create:**
- `docs/ASPIRE-GRPC-CONFIGURATION.md`

---

### Phase 2: Dual-Stack Implementation (40 hours)

#### Task 2.1: Implement Tenant Service gRPC (10 hours)
**Priority:** P0
**Assignee:** Backend Developer

**Subtasks:**

**2.1.1: Create gRPC Service Implementation (5h)**
- [ ] Create `src/Services/Sorcha.Tenant.Service/Grpc/TenantGrpcService.cs`
- [ ] Implement `IntrospectToken` RPC (map from existing logic)
- [ ] Implement `GetDelegationToken` RPC
- [ ] Implement `GetServiceToken` RPC
- [ ] Add gRPC error handling (map HTTP status codes to gRPC status codes)
- [ ] Add logging for all gRPC calls

**2.1.2: Add mTLS and Authentication Interceptor (3h)**
- [ ] Create `src/Services/Sorcha.Tenant.Service/Grpc/Interceptors/AuthenticationInterceptor.cs`
- [ ] Validate client certificates (mTLS)
- [ ] Extract JWT from gRPC metadata
- [ ] Validate JWT signature and claims
- [ ] Inject user context into `ServerCallContext`

**2.1.3: Register gRPC Service in Program.cs (2h)**
- [ ] Add `builder.Services.AddGrpc()` configuration
- [ ] Register `TenantGrpcService`
- [ ] Configure mTLS certificate
- [ ] Map gRPC service: `app.MapGrpcService<TenantGrpcService>()`
- [ ] Enable gRPC reflection for development

**Acceptance Criteria:**
- Tenant Service responds to gRPC calls on port 5001
- mTLS validates client certificates
- JWT tokens validated via metadata
- All three RPCs functional
- REST endpoints still functional (dual-stack)

**Files to Create:**
- `src/Services/Sorcha.Tenant.Service/Grpc/TenantGrpcService.cs`
- `src/Services/Sorcha.Tenant.Service/Grpc/Interceptors/AuthenticationInterceptor.cs`

**Files to Modify:**
- `src/Services/Sorcha.Tenant.Service/Program.cs`

---

#### Task 2.2: Implement Wallet Service gRPC (10 hours)
**Priority:** P1
**Assignee:** Backend Developer

**Subtasks:**

**2.2.1: Create gRPC Service Implementation (5h)**
- [ ] Create `src/Services/Sorcha.Wallet.Service/Grpc/WalletGrpcService.cs`
- [ ] Implement `SignTransaction` RPC
- [ ] Implement `DecryptPayload` RPC
- [ ] Implement `EncryptPayload` RPC
- [ ] Add gRPC error handling
- [ ] Add logging for all gRPC calls

**2.2.2: Add Delegation Token Interceptor (3h)**
- [ ] Create `src/Services/Sorcha.Wallet.Service/Grpc/Interceptors/DelegationTokenInterceptor.cs`
- [ ] Extract `x-delegation-token` from gRPC metadata
- [ ] Validate delegation token via Tenant Service gRPC
- [ ] Inject delegated user context
- [ ] Log delegation events

**2.2.3: Register gRPC Service (2h)**
- [ ] Add gRPC configuration to Program.cs
- [ ] Register `WalletGrpcService`
- [ ] Configure mTLS
- [ ] Map gRPC service

**Acceptance Criteria:**
- Wallet Service responds to gRPC calls on port 5002
- Delegation tokens validated via metadata
- All three RPCs functional
- REST endpoints still functional (dual-stack)

**Files to Create:**
- `src/Services/Sorcha.Wallet.Service/Grpc/WalletGrpcService.cs`
- `src/Services/Sorcha.Wallet.Service/Grpc/Interceptors/DelegationTokenInterceptor.cs`

**Files to Modify:**
- `src/Services/Sorcha.Wallet.Service/Program.cs`

---

#### Task 2.3: Implement Register Service gRPC (10 hours)
**Priority:** P1
**Assignee:** Backend Developer

**Subtasks:**

**2.3.1: Create gRPC Service Implementation (6h)**
- [ ] Create `src/Services/Sorcha.Register.Service/Grpc/RegisterGrpcService.cs`
- [ ] Implement `SubmitTransaction` RPC
- [ ] Implement `QueryTransactions` RPC (with pagination)
- [ ] Implement `GetTransactionsByInstanceId` RPC (server streaming)
- [ ] Add gRPC error handling
- [ ] Add logging

**2.3.2: Add Delegation Token Interceptor (2h)**
- [ ] Create `src/Services/Sorcha.Register.Service/Grpc/Interceptors/DelegationTokenInterceptor.cs`
- [ ] Similar to Wallet Service interceptor

**2.3.3: Register gRPC Service (2h)**
- [ ] Add gRPC configuration to Program.cs
- [ ] Register `RegisterGrpcService`
- [ ] Configure mTLS
- [ ] Map gRPC service

**Acceptance Criteria:**
- Register Service responds to gRPC calls on port 5003
- Server streaming works for transaction queries
- Delegation tokens validated
- REST endpoints still functional (dual-stack)

**Files to Create:**
- `src/Services/Sorcha.Register.Service/Grpc/RegisterGrpcService.cs`
- `src/Services/Sorcha.Register.Service/Grpc/Interceptors/DelegationTokenInterceptor.cs`

**Files to Modify:**
- `src/Services/Sorcha.Register.Service/Program.cs`

---

#### Task 2.4: Implement Blueprint Service gRPC Clients (10 hours)
**Priority:** P1
**Assignee:** Backend Developer

**Subtasks:**

**2.4.1: Create Tenant Service gRPC Client (3h)**
- [ ] Create `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/TenantServiceClient.cs`
- [ ] Implement `IntrospectTokenAsync()`
- [ ] Implement `GetDelegationTokenAsync()`
- [ ] Add mTLS client certificate configuration
- [ ] Add retry policies (Polly)
- [ ] Add circuit breaker

**2.4.2: Create Wallet Service gRPC Client (3h)**
- [ ] Create `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/WalletServiceClient.cs`
- [ ] Implement `SignTransactionAsync()`
- [ ] Implement `DecryptPayloadAsync()`
- [ ] Add delegation token to metadata
- [ ] Add retry policies
- [ ] Add circuit breaker

**2.4.3: Create Register Service gRPC Client (3h)**
- [ ] Create `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/RegisterServiceClient.cs`
- [ ] Implement `SubmitTransactionAsync()`
- [ ] Implement `GetTransactionsByInstanceIdAsync()` (streaming client)
- [ ] Add delegation token to metadata
- [ ] Add retry policies
- [ ] Add circuit breaker

**2.4.4: Register gRPC Clients in DI (1h)**
- [ ] Add `builder.Services.AddGrpcClient<TenantService.TenantServiceClient>()`
- [ ] Configure service discovery URLs
- [ ] Configure client certificates
- [ ] Configure channel options (HTTP/2, compression)

**Acceptance Criteria:**
- Blueprint Service can call all three services via gRPC
- Clients use mTLS for authentication
- Delegation tokens sent via metadata
- Retry policies and circuit breakers functional
- REST clients still functional (dual-stack)

**Files to Create:**
- `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/TenantServiceClient.cs`
- `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/WalletServiceClient.cs`
- `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/RegisterServiceClient.cs`

**Files to Modify:**
- `src/Services/Sorcha.Blueprint.Service/Program.cs`

---

### Phase 3: Service Migration (64 hours)

#### Task 3.1: Migrate Blueprint Service to gRPC Clients (16 hours)
**Priority:** P0
**Assignee:** Backend Developer

**Subtasks:**

**3.1.1: Update ActionExecutionService (6h)**
- [ ] Replace `ITenantServiceClient` HTTP calls with gRPC client
- [ ] Replace `IWalletServiceClient` HTTP calls with gRPC client
- [ ] Replace `IRegisterServiceClient` HTTP calls with gRPC client
- [ ] Update error handling (HTTP status codes → gRPC status codes)
- [ ] Test delegation token flow end-to-end

**3.1.2: Update StateReconstructionService (4h)**
- [ ] Replace Wallet Service decrypt calls with gRPC
- [ ] Replace Register Service query calls with gRPC streaming
- [ ] Handle streaming responses

**3.1.3: Update Service Clients (4h)**
- [ ] Deprecate HTTP client implementations
- [ ] Update all call sites to use gRPC clients
- [ ] Remove HTTP client dependencies

**3.1.4: Integration Testing (2h)**
- [ ] Test full Blueprint → Wallet → Register flow via gRPC
- [ ] Test delegation token propagation
- [ ] Test error scenarios

**Acceptance Criteria:**
- Blueprint Service exclusively uses gRPC for internal calls
- All 123 tests still passing (with gRPC mocks)
- No HTTP calls to Wallet/Register/Tenant from Blueprint

**Files to Modify:**
- `src/Services/Sorcha.Blueprint.Service/Services/ActionExecutionService.cs`
- `src/Services/Sorcha.Blueprint.Service/Services/StateReconstructionService.cs`

---

#### Task 3.2: Migrate Integration Tests (32 hours)
**Priority:** P0
**Assignee:** QA/Test Developer

**Subtasks:**

**3.2.1: Create gRPC Test Infrastructure (8h)**
- [ ] Create `tests/Sorcha.IntegrationTests/Grpc/GrpcTestServer.cs`
- [ ] Create mock gRPC services for testing
- [ ] Create helper methods for gRPC client setup
- [ ] Create helpers for mTLS test certificates
- [ ] Document test infrastructure usage

**3.2.2: Migrate Tenant Service Tests (6h)**
- [ ] Update 67 Tenant Service integration tests
- [ ] Replace HTTP client calls with gRPC clients
- [ ] Test mTLS authentication
- [ ] Test delegation token issuance via gRPC

**3.2.3: Migrate Wallet Service Tests (6h)**
- [ ] Update 67 Wallet Service integration tests
- [ ] Replace HTTP client calls with gRPC clients
- [ ] Test delegation token validation
- [ ] Test signing/encryption via gRPC

**3.2.4: Migrate Register Service Tests (6h)**
- [ ] Update 112 Register Service integration tests
- [ ] Replace HTTP client calls with gRPC clients
- [ ] Test server streaming responses
- [ ] Test transaction queries via gRPC

**3.2.5: Migrate Blueprint Service Tests (6h)**
- [ ] Update 123 Blueprint Service integration tests
- [ ] Update orchestration tests to use gRPC mocks
- [ ] Test end-to-end workflow via gRPC
- [ ] Test SignalR integration with gRPC backend

**Acceptance Criteria:**
- All 369 integration tests migrated to gRPC
- 100% test pass rate
- Tests run faster with gRPC (binary serialization)
- Test infrastructure reusable for future tests

**Files to Create:**
- `tests/Sorcha.IntegrationTests/Grpc/GrpcTestServer.cs`
- `tests/Sorcha.IntegrationTests/Grpc/MockTenantGrpcService.cs`
- `tests/Sorcha.IntegrationTests/Grpc/MockWalletGrpcService.cs`
- `tests/Sorcha.IntegrationTests/Grpc/MockRegisterGrpcService.cs`

**Files to Modify:**
- All test files in `tests/Sorcha.Tenant.Service.IntegrationTests/`
- All test files in `tests/Sorcha.Wallet.Service.Tests/`
- All test files in `tests/Sorcha.Register.Service.Tests/`
- All test files in `tests/Sorcha.Blueprint.Service.Tests/`

---

#### Task 3.3: Update API Gateway (8 hours)
**Priority:** P1
**Assignee:** Infrastructure Developer

**Subtasks:**

**3.3.1: Add gRPC-JSON Transcoding (4h)**
- [ ] Add `Microsoft.AspNetCore.Grpc.JsonTranscoding` package
- [ ] Configure API Gateway to translate REST → gRPC
- [ ] Map external REST routes to internal gRPC services
- [ ] Test transcoding with Postman/curl

**3.3.2: Update YARP Configuration (2h)**
- [ ] Update routes to point to gRPC endpoints
- [ ] Configure HTTP/2 for upstream connections
- [ ] Test load balancing with gRPC

**3.3.3: Update Health Checks (2h)**
- [ ] Replace HTTP health checks with gRPC health checks
- [ ] Aggregate gRPC health status
- [ ] Test health endpoint

**Acceptance Criteria:**
- External clients continue using REST (no breaking changes)
- API Gateway translates REST → gRPC internally
- Health checks use gRPC health protocol
- Load balancing works with gRPC

**Files to Modify:**
- `src/Apps/Sorcha.ApiGateway/Program.cs`
- `src/Apps/Sorcha.ApiGateway/appsettings.json`

---

#### Task 3.4: Performance Benchmarking (8 hours)
**Priority:** P1
**Assignee:** Performance Engineer

**Subtasks:**

**3.4.1: Create Benchmark Suite (4h)**
- [ ] Create `benchmarks/Sorcha.Benchmarks/GrpcVsRestBenchmark.cs`
- [ ] Benchmark Tenant.IntrospectToken (REST vs gRPC)
- [ ] Benchmark Wallet.SignTransaction (REST vs gRPC)
- [ ] Benchmark Register.QueryTransactions (REST vs gRPC)
- [ ] Use BenchmarkDotNet for accurate measurements

**3.4.2: Run Baseline Performance Tests (2h)**
- [ ] Run benchmarks on current REST implementation
- [ ] Document baseline metrics (latency, throughput)

**3.4.3: Run gRPC Performance Tests (2h)**
- [ ] Run benchmarks on gRPC implementation
- [ ] Compare results to baseline
- [ ] Validate 4-5x improvement target
- [ ] Document results

**Acceptance Criteria:**
- Benchmark results show 4-5x latency improvement
- Throughput increased by 3-4x
- Results documented in `benchmarks/RESULTS.md`

**Files to Create:**
- `benchmarks/Sorcha.Benchmarks/GrpcVsRestBenchmark.cs`
- `benchmarks/RESULTS.md`

---

### Phase 4: Validation and Cleanup (16 hours)

#### Task 4.1: Remove Dual-Stack REST Endpoints (6 hours)
**Priority:** P1
**Assignee:** Backend Developer

**Subtasks:**

**4.1.1: Remove Internal REST Endpoints (3h)**
- [ ] Remove HTTP endpoints from Tenant Service (keep external auth endpoints)
- [ ] Remove HTTP endpoints from Wallet Service (keep external client endpoints)
- [ ] Remove HTTP endpoints from Register Service (keep external query endpoints)
- [ ] Verify no internal HTTP calls remain

**4.1.2: Clean Up HTTP Client Code (2h)**
- [ ] Remove HTTP client implementations
- [ ] Remove HTTP client NuGet packages where appropriate
- [ ] Update DI registration

**4.1.3: Update Configuration (1h)**
- [ ] Remove HTTP URL configuration for internal services
- [ ] Keep gRPC URLs only
- [ ] Update appsettings.json

**Acceptance Criteria:**
- Only gRPC endpoints exposed for internal communication
- External REST APIs still functional (via API Gateway)
- No dead code remaining

**Files to Modify:**
- All service `Program.cs` files
- All `appsettings.json` files

---

#### Task 4.2: Update Documentation (6 hours)
**Priority:** P0
**Assignee:** Technical Writer / Developer

**Subtasks:**

**4.2.1: Update Service Documentation (2h)**
- [ ] Update `.specify/specs/sorcha-tenant-service.md` with gRPC details
- [ ] Update `.specify/specs/sorcha-wallet-service.md` with gRPC details
- [ ] Update `.specify/specs/sorcha-register-service.md` with gRPC details
- [ ] Document .proto file locations and structure

**4.2.2: Update Architecture Documentation (2h)**
- [ ] Update `docs/reference/architecture.md` with gRPC service diagram
- [ ] Update `docs/guides/AUTHENTICATION-SETUP.md` for gRPC auth flow
- [ ] Create `docs/GRPC-GUIDE.md` for developers
- [ ] Document mTLS certificate management

**4.2.3: Update API Documentation (2h)**
- [ ] Generate .proto documentation (protoc-gen-doc)
- [ ] Update `docs/reference/API-DOCUMENTATION.md`
- [ ] Create gRPC client examples
- [ ] Document error handling patterns

**Acceptance Criteria:**
- All documentation references gRPC (not REST) for internal calls
- .proto files documented and published
- Developer guide available for gRPC usage
- Certificate management documented

**Files to Create:**
- `docs/GRPC-GUIDE.md`
- `docs/GRPC-PROTO-REFERENCE.md`

**Files to Modify:**
- `.specify/specs/sorcha-tenant-service.md`
- `.specify/specs/sorcha-wallet-service.md`
- `.specify/specs/sorcha-register-service.md`
- `docs/reference/architecture.md`
- `docs/guides/AUTHENTICATION-SETUP.md`
- `docs/reference/API-DOCUMENTATION.md`

---

#### Task 4.3: Final Validation (4 hours)
**Priority:** P0
**Assignee:** QA Lead

**Subtasks:**

**4.3.1: End-to-End Testing (2h)**
- [ ] Test full Blueprint execution flow via gRPC
- [ ] Test delegation token propagation
- [ ] Test error scenarios (network failures, invalid tokens)
- [ ] Test streaming responses
- [ ] Verify SignalR notifications still work

**4.3.2: Performance Validation (1h)**
- [ ] Run production-like load tests
- [ ] Verify latency improvements
- [ ] Monitor CPU/memory usage
- [ ] Check for memory leaks

**4.3.3: Security Audit (1h)**
- [ ] Verify all services use mTLS
- [ ] Test certificate validation
- [ ] Test delegation token security
- [ ] Scan for vulnerabilities

**Acceptance Criteria:**
- All tests passing in production-like environment
- Performance targets met (4-5x improvement)
- Security audit passed
- No regressions detected

---

## Service-Specific Migration Plans

### Tenant Service Migration

**Current:** HTTP REST with JWT Bearer authentication
**Target:** gRPC with mTLS + JWT metadata

**Key Changes:**
1. Add `tenant.proto` with 3 RPCs (IntrospectToken, GetDelegationToken, GetServiceToken)
2. Implement `TenantGrpcService` mapping to existing `TokenService`
3. Add `AuthenticationInterceptor` for mTLS + JWT validation
4. Keep external auth endpoints as REST (for clients)

**Risks:**
- Token introspection is on critical path - must be fast (< 2ms)
- Certificate validation overhead
- Redis connection must be maintained

**Mitigation:**
- Use connection pooling for gRPC channels
- Cache certificate validation results
- Monitor latency closely

---

### Wallet Service Migration

**Current:** HTTP REST with delegation tokens in headers
**Target:** gRPC with delegation tokens in metadata

**Key Changes:**
1. Add `wallet.proto` with 3 RPCs (SignTransaction, DecryptPayload, EncryptPayload)
2. Implement `WalletGrpcService` mapping to `WalletManager`
3. Add `DelegationTokenInterceptor` for metadata extraction
4. Keep external wallet APIs as REST (for clients)

**Risks:**
- Signing operations are security-critical
- Large payloads (encryption/decryption)
- Private key access must remain secure

**Mitigation:**
- Enable gRPC compression for large payloads
- Maintain existing `KeyManagementService` (no changes)
- Add comprehensive logging for security events

---

### Register Service Migration

**Current:** HTTP REST with pagination
**Target:** gRPC with server streaming

**Key Changes:**
1. Add `register.proto` with 3 RPCs (SubmitTransaction, QueryTransactions, GetTransactionsByInstanceId)
2. Implement `RegisterGrpcService` mapping to `RegisterManager`
3. Use server streaming for transaction queries
4. Keep external query APIs as REST (for clients)

**Risks:**
- Large transaction history queries
- Streaming response handling
- MongoDB connection pooling

**Mitigation:**
- Use gRPC streaming for efficient large responses
- Implement pagination in streaming responses
- Test with large datasets

---

### Blueprint Service Migration

**Current:** HTTP client calls to Tenant/Wallet/Register
**Target:** gRPC client calls

**Key Changes:**
1. Implement 3 gRPC clients (TenantServiceClient, WalletServiceClient, RegisterServiceClient)
2. Update `ActionExecutionService` to use gRPC clients
3. Update `StateReconstructionService` to use gRPC streaming
4. Add retry policies and circuit breakers

**Risks:**
- Orchestration is complex - many service calls
- Error handling changes (HTTP → gRPC status codes)
- SignalR notifications still use HTTP

**Mitigation:**
- Implement comprehensive retry policies (Polly)
- Map gRPC status codes to meaningful errors
- Keep SignalR on HTTP (not migrated)

---

## Testing Strategy

### Unit Tests

**No changes required** - business logic remains the same.

### Integration Tests

**369 tests need migration:**
- Tenant Service: 67 tests → gRPC client calls
- Wallet Service: 67 tests → gRPC client calls
- Register Service: 112 tests → gRPC client calls
- Blueprint Service: 123 tests → gRPC mocks

**Test Infrastructure Changes:**
- Create `GrpcTestServer` helper
- Create mock gRPC services
- Generate test certificates for mTLS

### End-to-End Tests

**Create new E2E test suite:**
- Full workflow: Client → API Gateway → Blueprint → Wallet/Register via gRPC
- Test delegation token flow end-to-end
- Test SignalR notifications with gRPC backend

### Performance Tests

**Benchmark suite using BenchmarkDotNet:**
- Measure latency: REST vs gRPC
- Measure throughput: requests/second
- Measure resource usage: CPU, memory
- Validate 4-5x improvement target

---

## Rollback Plan

### Rollback Triggers

- gRPC performance < 2x REST (not worth effort)
- Security vulnerabilities in mTLS implementation
- > 5% error rate in production
- Certificate management becomes unmanageable

### Rollback Steps

**Phase 2 Rollback (Dual-Stack Active):**
1. Switch Blueprint Service clients back to HTTP (1 hour)
2. Disable gRPC endpoints (configuration change)
3. All services continue with REST

**Phase 3/4 Rollback (gRPC Only):**
1. Deploy previous version with dual-stack support (2 hours)
2. Switch clients to HTTP (configuration change)
3. Investigate issues
4. Re-plan migration

**Data Impact:** None (no data migration, only protocol change)

---

## Success Metrics

### Performance Metrics

| Metric | REST Baseline | gRPC Target | Actual | Status |
|--------|---------------|-------------|--------|--------|
| Token Introspection Latency | 8ms | 2ms (4x) | TBD | 🟡 Pending |
| Wallet Sign Latency | 50ms | 10ms (5x) | TBD | 🟡 Pending |
| Register Query Latency | 20ms | 5ms (4x) | TBD | 🟡 Pending |
| Throughput (req/s) | 1000 | 4000 (4x) | TBD | 🟡 Pending |
| CPU Usage | 100% | 80% (-20%) | TBD | 🟡 Pending |
| Memory Usage | 100% | 90% (-10%) | TBD | 🟡 Pending |

### Quality Metrics

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Integration Tests Passing | 100% (369 tests) | TBD | 🟡 Pending |
| Code Coverage | Maintain 85%+ | TBD | 🟡 Pending |
| Security Audit | Pass | TBD | 🟡 Pending |
| Documentation Complete | 100% | TBD | 🟡 Pending |

### Business Metrics

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Zero Downtime Deployment | Yes | TBD | 🟡 Pending |
| No Breaking Changes for Clients | Yes | TBD | 🟡 Pending |
| Migration Timeline | 8 weeks | TBD | 🟡 Pending |
| Team Training Complete | 100% | TBD | 🟡 Pending |

---

## Risk Register

| Risk | Probability | Impact | Mitigation | Owner |
|------|-------------|--------|------------|-------|
| Certificate management complexity | MEDIUM | HIGH | Use Azure Key Vault, automate rotation | DevOps |
| Performance doesn't meet targets | LOW | MEDIUM | Benchmark early, validate assumptions | Backend |
| Team struggles with gRPC | LOW | MEDIUM | Training, pair programming, docs | Tech Lead |
| Migration breaks existing services | MEDIUM | HIGH | Dual-stack, phased migration, rollback plan | Backend |
| External clients confused | LOW | LOW | No changes to external APIs, clear docs | Product |
| Integration tests fail | MEDIUM | MEDIUM | Migrate tests incrementally, validate early | QA |
| Production issues | LOW | HIGH | Canary deployments, monitoring, rollback | DevOps |

---

## Appendix A: Task Summary

**Total Tasks:** 39
**Total Effort:** 144 hours

### By Phase

| Phase | Tasks | Hours | % of Total |
|-------|-------|-------|------------|
| Phase 1: Infrastructure | 4 | 24 | 17% |
| Phase 2: Dual-Stack | 4 | 40 | 28% |
| Phase 3: Migration | 4 | 64 | 44% |
| Phase 4: Cleanup | 3 | 16 | 11% |

### By Priority

| Priority | Tasks | Hours |
|----------|-------|-------|
| P0 (Critical) | 8 | 80 |
| P1 (High) | 7 | 64 |

### By Skillset

| Role | Tasks | Hours |
|------|-------|-------|
| Backend Developer | 14 | 80 |
| DevOps/Infrastructure | 3 | 24 |
| QA/Test Engineer | 2 | 32 |
| Performance Engineer | 1 | 8 |
| Technical Writer | 1 | 6 |

---

## Appendix B: File Changes Summary

### New Files Created (35)

**Proto Files (4):**
- `src/Services/Sorcha.Tenant.Service/Protos/tenant.proto`
- `src/Services/Sorcha.Wallet.Service/Protos/wallet.proto`
- `src/Services/Sorcha.Register.Service/Protos/register.proto`
- `src/Services/Sorcha.Blueprint.Service/Protos/blueprint.proto`

**gRPC Service Implementations (3):**
- `src/Services/Sorcha.Tenant.Service/Grpc/TenantGrpcService.cs`
- `src/Services/Sorcha.Wallet.Service/Grpc/WalletGrpcService.cs`
- `src/Services/Sorcha.Register.Service/Grpc/RegisterGrpcService.cs`

**gRPC Interceptors (3):**
- `src/Services/Sorcha.Tenant.Service/Grpc/Interceptors/AuthenticationInterceptor.cs`
- `src/Services/Sorcha.Wallet.Service/Grpc/Interceptors/DelegationTokenInterceptor.cs`
- `src/Services/Sorcha.Register.Service/Grpc/Interceptors/DelegationTokenInterceptor.cs`

**gRPC Clients (3):**
- `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/TenantServiceClient.cs`
- `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/WalletServiceClient.cs`
- `src/Services/Sorcha.Blueprint.Service/Grpc/Clients/RegisterServiceClient.cs`

**Test Infrastructure (5):**
- `tests/Sorcha.IntegrationTests/Grpc/GrpcTestServer.cs`
- `tests/Sorcha.IntegrationTests/Grpc/MockTenantGrpcService.cs`
- `tests/Sorcha.IntegrationTests/Grpc/MockWalletGrpcService.cs`
- `tests/Sorcha.IntegrationTests/Grpc/MockRegisterGrpcService.cs`
- `benchmarks/Sorcha.Benchmarks/GrpcVsRestBenchmark.cs`

**Documentation (9):**
- `infrastructure/certificates/generate-certs.ps1`
- `infrastructure/certificates/README.md`
- `docs/CERTIFICATE-MANAGEMENT.md`
- `docs/ASPIRE-GRPC-CONFIGURATION.md`
- `docs/GRPC-GUIDE.md`
- `docs/GRPC-PROTO-REFERENCE.md`
- `benchmarks/RESULTS.md`
- `.specify/adrs/adr-001-grpc-service-communication.md`
- `.specify/GRPC-MIGRATION-PLAN.md` (this file)

### Files Modified (20+)

**Service Projects:**
- All 4 service `.csproj` files (NuGet packages)
- All 4 service `Program.cs` files (gRPC registration)
- All 4 service `appsettings.json` files (configuration)

**Service Implementation:**
- `src/Services/Sorcha.Blueprint.Service/Services/ActionExecutionService.cs`
- `src/Services/Sorcha.Blueprint.Service/Services/StateReconstructionService.cs`

**Infrastructure:**
- `src/Apps/Sorcha.AppHost/Program.cs`
- `src/Apps/Sorcha.ApiGateway/Program.cs`
- `src/Apps/Sorcha.ApiGateway/appsettings.json`

**Documentation:**
- `.specify/constitution.md` (updated with gRPC requirements)
- `.specify/specs/sorcha-tenant-service.md`
- `.specify/specs/sorcha-wallet-service.md`
- `.specify/specs/sorcha-register-service.md`
- `docs/reference/architecture.md`
- `docs/guides/AUTHENTICATION-SETUP.md`
- `docs/reference/API-DOCUMENTATION.md`

**Tests:**
- All 369 integration test files (spread across 4 test projects)

---

## Appendix C: gRPC Service Ports

| Service | gRPC Port | REST Port (External) | Purpose |
|---------|-----------|---------------------|---------|
| Tenant Service | 5001 | 5000 (Auth endpoints only) | Token management |
| Wallet Service | 5002 | 5003 (Client endpoints only) | Crypto operations |
| Register Service | 5004 | 5005 (Query endpoints only) | Ledger operations |
| Blueprint Service | 5006 | 5007 (Orchestration) | Workflow execution |

---

## Appendix D: Next Steps

1. **Review this plan** with the architecture team
2. **Allocate resources** (2-3 developers for 8 weeks part-time)
3. **Set up project tracking** (create tasks in MASTER-TASKS.md)
4. **Begin Phase 1** with certificate infrastructure
5. **Schedule weekly checkpoints** to track progress

---

**Document Version:** 1.0
**Last Updated:** 2025-12-13
**Next Review:** End of Phase 1 (Week 2)
**Owner:** Sorcha Architecture Team
