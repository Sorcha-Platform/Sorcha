# Critical Issues & Next Actions

**Last Updated:** 2026-03-01

---

## Resolved Issues

### ✅ Issue #1: Register Service API Disconnection (P0)

**Problem:** Register Service API stub existed but didn't use the Phase 1-2 core implementation

**Resolution (2025-11-16):**
1. ✅ Refactored Sorcha.Register.Service/Program.cs to use core managers
2. ✅ Replaced `TransactionStore` with `IRegisterRepository`
3. ✅ Integrated RegisterManager, TransactionManager, QueryManager
4. ✅ Added .NET Aspire integration
5. ✅ Implemented 20 REST endpoints + OData + SignalR
6. ✅ Complete OpenAPI documentation

**Commit:** `f9cdc86`

---

### ✅ Issue #2: DocketManager/ChainValidator Duplication (P1)

**Problem:** DocketManager and ChainValidator existed in both Register.Core and Validator.Service

**Resolution (2025-12-09):**
1. ✅ Confirmed implementations correctly moved to Validator.Service
2. ✅ Deleted orphaned test files from Register.Core.Tests
3. ✅ Implementations now only in: src/Services/Sorcha.Validator.Service/

---

### ✅ Issue #3: Missing SignalR Integration Tests (P1)

**Problem:** SignalR hub was implemented but had no integration tests

**Resolution (2025-11-16):**
1. ✅ Created SignalRIntegrationTests.cs (520+ lines, 14 tests)
2. ✅ Hub connection/disconnection lifecycle tests
3. ✅ Wallet subscription/unsubscription tests
4. ✅ All notification types tested
5. ✅ Multi-client broadcast scenarios
6. ✅ Wallet-specific notification isolation

---

### ✅ Issue #4: Register Service Missing Automated Tests (P1)

**Problem:** ~4,150 LOC of core implementation had no unit or integration tests

**Resolution (2025-11-16):**
1. ✅ Unit tests for all core managers
2. ✅ API integration tests with in-memory repository
3. ✅ SignalR hub integration tests
4. ✅ Query API integration tests
5. ✅ 112 comprehensive test methods
6. ✅ ~2,459 lines of test code

---

## Next Recommended Actions

### Completed Actions ✅

**Fix Register Service API Integration (P0)**
- ✅ Refactored to use Phase 1-2 core
- ✅ Added SignalR and OData support

**Add Blueprint Service SignalR Integration Tests (P1)**
- ✅ 14 tests covering all hub functionality

**Add Register Service Automated Tests (P1)**
- ✅ 112 test methods, ~2,459 lines of test code

---

### Deferred / Future Work (Post-MVD)

All services have reached 100% MVD as of 2026-03-01. The following items are deferred to post-MVD phases:

| Item | Service | Priority | Notes |
|------|---------|----------|-------|
| Azure Key Vault integration | Wallet | P2 | Production key encryption provider |
| Azure AD B2C integration | Tenant | P2 | External identity provider |
| Fork detection and chain recovery | Validator | P2 | Requires multi-validator network |
| Enclave support (Intel SGX, AMD SEV) | Validator | P3 | Hardware security module integration |
| BLS threshold coordination | Peer | P3 | Multi-party signature schemes |
| Decentralized consensus | Validator | P3 | Multi-validator distributed voting |
| Production security hardening | All | P2 | TLS, secret rotation, audit logging |
| Advanced rate limiting | API Gateway | P3 | Per-client throttling |

---

**Back to:** [Development Status](../development-status.md)
