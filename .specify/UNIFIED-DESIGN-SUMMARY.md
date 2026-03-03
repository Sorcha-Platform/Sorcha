# Unified Blueprint-Action Service - Design Summary

**Date:** 2025-11-15
**Status:** Approved for Implementation
**Author:** Sorcha Architecture Team

---

## Executive Summary

The Sorcha Blueprint Service has been redesigned as a **unified service** that combines blueprint management with action execution capabilities. The centerpiece of this redesign is a **portable Blueprint Execution Engine** that can run both client-side (in Blazor WASM) and server-side (in the Blueprint Service) for validation and verification.

This design **supersedes and merges** the previously separate:
- Blueprint Service (90% complete)
- Action Service (fully designed, not implemented)

---

## Key Architectural Changes

### 1. New Portable Execution Engine (`Sorcha.Blueprint.Engine`)

**What Changed:**
- **Before:** Blueprint.Engine was planned as a REST API service
- **After:** Blueprint.Engine is now a **standalone class library** with zero ASP.NET dependencies

**Why:**
- Enables client-side validation in Blazor WASM
- Allows users to test blueprints before publishing
- Provides instant feedback without server round-trip
- Same validation logic runs client-side and server-side (no discrepancies)

**Key Characteristics:**
- **Stateless** - No internal state, all context passed as parameters
- **Portable** - Runs in browser (WASM) and server (.NET)
- **Zero External Dependencies** - Only JSON processing libraries
- **Pure Functions** - Deterministic results for same inputs
- **Highly Testable** - Easy to unit test in isolation

**Capabilities:**
- JSON Schema validation (Draft 2020-12)
- JSON Logic evaluation for calculations and conditions
- Selective disclosure processing using JSON Pointers
- Routing determination based on conditions
- Complete action processing orchestration

---

### 2. Unified Blueprint-Action Service

**What Changed:**
- **Before:** Separate Blueprint Service (CRUD) and Action Service (execution)
- **After:** Single unified service combining both capabilities

**Why:**
- Reduces operational complexity (fewer services to deploy/manage)
- Better cohesion (blueprint and action operations are tightly related)
- Shared caching and state management
- Simplified service discovery and integration

**New Responsibilities:**
1. **Blueprint Management** (existing)
   - CRUD operations
   - Publishing and versioning
   - Validation

2. **Action Operations** (new)
   - Action retrieval (starting actions, pending actions)
   - Action submission
   - Action rejection
   - File handling

3. **Execution Coordination** (new)
   - Uses portable execution engine
   - Payload encryption/decryption (via Wallet Service)
   - Transaction building and submission (via Register Service)

4. **Real-Time Notifications** (new)
   - SignalR hub for action updates
   - Redis backplane for scale-out
   - WebSocket support

---

### 3. Blueprint Designer Integration

**What Changed:**
- **Before:** Designer only created and viewed blueprints
- **After:** Designer can validate and test blueprints using execution engine

**New Capabilities:**
- Client-side validation using portable execution engine
- Real-time validation as users enter data
- Blueprint "Test Mode" to simulate execution
- Shows calculation results, routing decisions, and disclosure filtering
- No server round-trip needed for validation

---

## Blueprint Execution Flow

### Client-Side (Blazor WASM)

```
User enters action data
    ↓
Blazor component uses IExecutionEngine (WASM)
    ↓
Validates against schema
    ↓
Applies calculations
    ↓
Shows preview of results
    ↓
User confirms and submits
```

**Benefits:**
- Instant feedback (<100ms)
- Works offline
- Reduces server load
- Better user experience

### Server-Side (Blueprint Service)

```
Action submission received
    ↓
Service uses IExecutionEngine (Server)
    ↓
Validates against schema
    ↓
Applies calculations
    ↓
Determines routing
    ↓
Creates disclosures
    ↓
Encrypts payloads (Wallet Service)
    ↓
Builds transaction
    ↓
Submits to Register Service
    ↓
Notifies participants (SignalR)
```

**Benefits:**
- Authoritative validation
- Integration with external services
- Transaction coordination
- Audit logging

---

## New API Endpoints

### Blueprint Management (Existing - Enhanced)
- `GET/POST/PUT/DELETE /api/blueprints` - Blueprint CRUD
- `POST /api/blueprints/{id}/publish` - Publish blueprint
- `GET /api/blueprints/{id}/versions` - Version history
- `POST /api/blueprints/validate` - Validate blueprint

### Action Operations (NEW)
- `GET /api/actions/{wallet}/{register}/blueprints` - Get starting actions
- `GET /api/actions/{wallet}/{register}` - Get pending actions (paginated)
- `GET /api/actions/{wallet}/{register}/{tx}` - Get action details
- `POST /api/actions` - Submit action
- `POST /api/actions/reject` - Reject action

### Execution Helpers (NEW - for client-side)
- `POST /api/execution/validate` - Validate action data only
- `POST /api/execution/calculate` - Apply calculations only
- `POST /api/execution/route` - Determine routing only
- `POST /api/execution/disclose` - Apply disclosure rules

### File Operations (NEW)
- `GET /api/files/{wallet}/{register}/{tx}/{fileId}` - Download file

### SignalR Hub (NEW)
- `/actionshub` - Real-time notifications
  - `ActionAvailable(notification)` - New action available
  - `ActionConfirmed(notification)` - Action confirmed
  - `ActionRejected(notification)` - Action rejected

---

## Technology Stack Updates

### New Dependencies

**Sorcha.Blueprint.Engine:**
```xml
<PackageReference Include="JsonSchema.Net" Version="7.2.4" />
<PackageReference Include="JsonLogic.Net" Version="2.0.0" />
<PackageReference Include="JsonPath.Net" Version="1.1.3" />
```

**Sorcha.Blueprint.Service:**
```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.0.0" />
<PackageReference Include="FluentValidation.AspNetCore" Version="11.3.0" />
<!-- Existing: Aspire.StackExchange.Redis 13.0.0 (now also for SignalR backplane) -->
```

---

## Implementation Plan Summary

**Timeline:** 16 weeks (8 sprints of 2 weeks each)

| Sprint | Weeks | Focus | Deliverable |
|--------|-------|-------|-------------|
| 1 | 1-2 | Engine foundation | Schema validation, JSON Logic |
| 2 | 3-4 | Engine complete | Disclosure, routing, full engine |
| 3 | 5-6 | Service layer | Action resolver, payload, transactions |
| 4 | 7-8 | API endpoints (1) | Action & file endpoints |
| 5 | 9-10 | API endpoints (2) | Execution helpers, SignalR |
| 6 | 11-12 | Integration | Wallet, Register, Redis |
| 7 | 13-14 | Testing & client | E2E tests, Blazor integration |
| 8 | 15-16 | Production | Performance, security, deploy |

**Total Tasks:** 138
**Total Effort:** ~378 hours

---

## Key Benefits

### For Developers
1. **Single execution logic** - Same code runs client and server
2. **Better testing** - Portable engine is easy to unit test
3. **Faster development** - Reuse logic across client and server
4. **Type safety** - Strong typing throughout

### For Users
1. **Instant validation** - No server round-trip needed
2. **Offline testing** - Test blueprints in browser
3. **Better UX** - Immediate feedback on data entry
4. **Transparency** - See exactly what will happen before submitting

### For Operations
1. **Fewer services** - Single unified service vs two separate
2. **Simpler deployment** - Fewer containers to manage
3. **Better observability** - Centralized logging and tracing
4. **Easier scaling** - Single service to scale

---

## Migration from Separate Services

**No migration needed** - The Action Service was never implemented.

**Blueprint Service Updates:**
- Existing endpoints remain unchanged
- New endpoints added for action operations
- Execution engine reference added
- SignalR hub added

**Breaking Changes:**
- None - This is a new design, not a redesign of existing functionality

---

## Success Criteria

### Functional
- [ ] Execution engine validates data against JSON Schema
- [ ] JSON Logic calculations execute correctly
- [ ] Conditional routing determines next participant
- [ ] Selective disclosure filters data correctly
- [ ] Client-side validation works in Blazor WASM
- [ ] Server-side execution matches client-side validation
- [ ] SignalR notifications delivered in real-time
- [ ] Encrypted payloads created correctly

### Non-Functional
- [ ] Engine test coverage >90%
- [ ] Service test coverage >85%
- [ ] API response time <200ms (p95) for GET
- [ ] API response time <500ms (p95) for POST
- [ ] Support 1000 req/s per instance
- [ ] Support 10,000 concurrent SignalR connections

---

## Documentation

**Design Documents:**
- [BLUEPRINT-SERVICE-UNIFIED-DESIGN.md](.specify/BLUEPRINT-SERVICE-UNIFIED-DESIGN.md) - Complete design specification
- [BLUEPRINT-SERVICE-IMPLEMENTATION-PLAN.md](.specify/BLUEPRINT-SERVICE-IMPLEMENTATION-PLAN.md) - Detailed implementation plan (138 tasks)

**Updated Architecture:**
- [docs/architecture.md](../docs/reference/architecture.md) - Updated architecture documentation

**Action Service Design (SUPERSEDED):**
- [ACTION-SERVICE-DESIGN.md](.specify/ACTION-SERVICE-DESIGN.md) - Original action service design (for reference)
- **Status:** Merged into unified design, no longer a separate service

---

## Next Steps

1. ✅ **Design Review** - Completed (2025-11-15)
2. ⏭️ **Sprint 1 Start** - Week of 2025-11-18
3. ⏭️ **Create Sorcha.Blueprint.Engine project**
4. ⏭️ **Implement core interfaces and models**
5. ⏭️ **Begin schema validator implementation**

---

## Questions & Answers

**Q: Why make the engine portable instead of a service?**
A: Client-side validation provides instant feedback without server round-trip, enabling a better user experience and offline testing capabilities.

**Q: Why merge Blueprint and Action services?**
A: They are tightly related and share state. A single service reduces operational complexity and improves cohesion.

**Q: Will this work with large payloads?**
A: Yes, the engine is stateless and processes data in memory. For very large payloads (>10MB), streaming can be added.

**Q: How does caching work?**
A: Redis caching at the service layer (blueprints, actions). The engine itself is stateless and doesn't cache.

**Q: How does the engine handle errors?**
A: Validation errors are returned in the result object. Runtime errors throw exceptions.

**Q: Can the engine be extended with custom logic?**
A: Yes, JSON Logic supports custom operators. The engine can be extended via dependency injection.

---

---

## Service Boundaries and Security Architecture

**Date Added:** 2025-11-16
**Related ADR:** [ADR-005: Validator Service Security Boundary](../docs/reference/architecture/ADR-005-Validator-Service-Security-Boundary.md)

### Register Service vs. Validator Service

Sorcha maintains strict service boundaries based on security requirements:

#### Register Service (Data Layer)
**Responsibilities:**
- CRUD operations for Registers, Transactions, Dockets
- Query operations (get by ID, list, filter, paginate)
- Data storage abstraction (MongoDB, PostgreSQL, InMemory)
- Read-only blockchain access

**Security Posture:**
- ❌ No access to cryptographic signing keys
- ❌ No blockchain modification capabilities
- ✅ Read/write permissions for data storage
- ✅ Query optimization and caching

**Components:**
- `Sorcha.Register.Models` - Domain models
- `Sorcha.Register.Core` - Business logic (RegisterManager, TransactionManager, QueryManager)
- `Sorcha.Register.Service` - REST API
- `Sorcha.Register.Storage.*` - Repository implementations

---

#### Validator Service (Security Layer)
**Responsibilities:**
- **Docket Building** - Assembling validated Transactions into blocks
- **Chain Validation** - Verifying cryptographic signatures and chain integrity
- **Consensus Participation** - Multi-peer validation coordination
- **Genesis Block Creation** - Initializing new Registers
- **Cryptographic Operations** - Via Wallet Service integration

**Security Posture:**
- ✅ **Secured execution environment** (Intel SGX, AMD SEV, HSM)
- ✅ **Access to cryptographic keys** (via Wallet Service)
- ✅ **Audit logging** for all validation operations
- ✅ **Enclave-compatible** core logic
- ✅ **Rate limiting** to prevent DoS attacks

**Components:**
- `Sorcha.Validator.Models` - Validation-specific models
- `Sorcha.Validator.Core` - **Enclave-safe** validation logic
  - **DocketManager** - Builds and seals Dockets (moved from Register.Core)
  - **ChainValidator** - Validates blockchain integrity (moved from Register.Core)
  - **ConsensusEngine** - Coordinates distributed validation
- `Sorcha.Validator.Service` - **Secured** REST API

---

### Why the Separation?

**Security Principle:** *Principle of Least Privilege*

| Operation | Needs Signing Keys? | Needs Enclave? | Service |
|-----------|---------------------|----------------|---------|
| Get Register by ID | ❌ No | ❌ No | Register Service |
| List Transactions | ❌ No | ❌ No | Register Service |
| Query blockchain | ❌ No | ❌ No | Register Service |
| **Build Docket** | ✅ **Yes** | ✅ **Yes** | **Validator Service** |
| **Seal Docket** | ✅ **Yes** | ✅ **Yes** | **Validator Service** |
| **Validate chain** | ✅ **Yes** | ✅ **Yes** | **Validator Service** |
| **Consensus vote** | ✅ **Yes** | ✅ **Yes** | **Validator Service** |

**Key Insight:** General blockchain read/write operations don't need cryptographic keys, but consensus and validation operations do. This separation enables:
1. Register Service to scale horizontally without key management
2. Validator Service to run in secure enclaves with controlled key access
3. Clear audit trail for all cryptographic operations
4. Compliance with security standards (SOC 2, ISO 27001)

---

### Service Communication

```
┌─────────────────┐
│ Blueprint       │
│ Service         │
└────────┬────────┘
         │ Creates Transactions
         ↓
┌─────────────────┐      ┌─────────────────┐
│ Validator       │◄────►│ Wallet Service  │
│ Service         │      │ (Keys)          │
│ (Secured)       │      └─────────────────┘
└────────┬────────┘
         │ Stores validated Dockets
         ↓
┌─────────────────┐
│ Register        │
│ Service         │
│ (Data Layer)    │
└─────────────────┘
```

**Flow:**
1. Blueprint Service creates Transactions
2. **Validator Service** builds Dockets (needs keys for signing)
3. **Validator Service** validates chain integrity (cryptographic verification)
4. Validator Service stores validated Dockets in Register Service
5. Register Service provides read access to blockchain data

---

**Document Control**
- **Created:** 2025-11-15
- **Updated:** 2025-11-16 (Added Validator Service boundary)
- **Status:** Approved
- **Review Frequency:** As needed during implementation
