# ADR-005: Validator Service Security Boundary

**Date:** 2025-11-16
**Status:** Accepted
**Deciders:** Sorcha Architecture Team
**Tags:** security, architecture, validator, blockchain

---

## Context and Problem Statement

During the implementation of the Register Service Phase 1 & 2, we discovered that `DocketManager` and `ChainValidator` were initially placed in `Sorcha.Register.Core`. However, these components have critical security requirements that necessitate execution in a secured environment with access to cryptographic keys.

**Key Security Requirements:**
1. **Cryptographic Operations** - Docket building requires access to signing keys for blockchain integrity
2. **Chain Validation** - Hash calculations and signature verification need cryptographic key access
3. **Consensus Participation** - Validators need to sign attestations and validate peer signatures
4. **Enclave Execution** - Production deployment requires Intel SGX/AMD SEV/HSM support
5. **Key Isolation** - Validation logic must run in environment with controlled key access

**Problem:** The current placement in Register.Core violates the principle of least privilege and creates a security boundary violation, as Register.Core is a general-purpose library that should not have access to cryptographic keys.

---

## Decision Drivers

1. **Security First** - Cryptographic operations must be isolated in secure execution environments
2. **Principle of Least Privilege** - Only components that absolutely need key access should have it
3. **Audit and Compliance** - Security-sensitive operations must be in auditable, controlled services
4. **Enclave Compatibility** - Design must support Intel SGX, AMD SEV, and HSM deployment
5. **Clear Service Boundaries** - Each service should have well-defined security perimeters

---

## Considered Options

### Option 1: Keep in Register.Core (Current - Rejected)
**Pros:**
- No code movement required
- Tests already exist

**Cons:**
- ❌ Violates security boundaries
- ❌ Cannot deploy in secure enclaves
- ❌ Gives unnecessary key access to general library
- ❌ Fails compliance requirements
- ❌ Contradicts existing Validator Service design

### Option 2: Create Validator.Core Library (Rejected)
**Pros:**
- Separates validation logic
- Portable library approach

**Cons:**
- ❌ Still doesn't enforce secure execution environment
- ❌ Doesn't address key access control
- ❌ Misses the service boundary aspect

### Option 3: Move to Validator Service with Secure Boundary (Selected)
**Pros:**
- ✅ Enforces secure execution environment
- ✅ Aligns with existing Validator Service design
- ✅ Enables enclave deployment (Intel SGX, AMD SEV, HSM)
- ✅ Properly isolates cryptographic operations
- ✅ Supports audit and compliance requirements
- ✅ Clear service-to-service communication boundaries
- ✅ Wallet Service integration for key operations

**Cons:**
- Requires code movement and test updates
- Adds service dependency (mitigated by proper API design)

---

## Decision

**We will move `DocketManager` and `ChainValidator` from `Sorcha.Register.Core` to a new `Sorcha.Validator.Service` with the following structure:**

```
src/
├── Core/
│   ├── Sorcha.Validator.Core/          # Enclave-safe validation logic
│   │   ├── Managers/
│   │   │   └── DocketManager.cs        # Moved from Register.Core
│   │   └── Validators/
│   │       └── ChainValidator.cs       # Moved from Register.Core
│   │
│   └── Sorcha.Validator.Models/        # Validation-specific models
│
└── Services/
    └── Sorcha.Validator.Service/       # Secured API service
        ├── Controllers/
        ├── Services/
        └── Program.cs
```

**Security Enhancements:**
1. **Enclave-Safe Core** - `Validator.Core` designed for Intel SGX/AMD SEV
2. **Wallet Service Integration** - All key operations delegated to Wallet Service
3. **Minimal API Surface** - Only essential validation endpoints exposed
4. **Rate Limiting** - DoS protection on validation endpoints
5. **Audit Logging** - Comprehensive security event logging

---

## Consequences

### Positive

1. **✅ Security Compliance** - Proper isolation of cryptographic operations
2. **✅ Enclave Ready** - Can deploy in Intel SGX, AMD SEV, or HSM
3. **✅ Clear Boundaries** - Well-defined service responsibilities
4. **✅ Auditable** - All validation operations in controlled service
5. **✅ Scalable** - Can scale validation independently of register operations
6. **✅ Future-Proof** - Aligns with planned Validator Service features

### Negative

1. **Additional Service** - One more service to deploy and manage
   - *Mitigation:* Use Aspire for orchestration, service defaults for consistency
2. **Service Dependency** - Register Service depends on Validator Service
   - *Mitigation:* Well-defined API contract, resilience patterns
3. **Test Updates** - Need to update integration tests
   - *Mitigation:* Use in-memory implementations for unit tests

### Neutral

1. **Code Movement** - Requires moving classes and updating namespaces
2. **Documentation Updates** - Need to update all architecture docs
3. **Learning Curve** - Team needs to understand new service boundary

---

## Implementation Plan

### Phase 1: Project Creation (Immediate)
1. Create `Sorcha.Validator.Core` class library
2. Create `Sorcha.Validator.Models` class library (if needed)
3. Create `Sorcha.Validator.Service` API project
4. Add projects to solution

### Phase 2: Code Movement (Immediate)
1. Move `DocketManager.cs` to `Sorcha.Validator.Core/Managers/`
2. Move `ChainValidator.cs` to `Sorcha.Validator.Core/Validators/`
3. Update namespaces
4. Update dependencies

### Phase 3: Test Updates (Immediate)
1. Move `DocketManagerTests.cs` to new test project
2. Move `ChainValidatorTests.cs` to new test project
3. Update test dependencies
4. Verify all tests pass

### Phase 4: Documentation (Immediate)
1. Update `UNIFIED-DESIGN-SUMMARY.md`
2. Update `validator-service-design.md`
3. Create this ADR
4. Update development status
5. Create learnings document

### Phase 5: API Design (Next Sprint)
1. Define Validator Service API contracts
2. Implement validation endpoints
3. Add Wallet Service integration
4. Add Register Service integration

---

## Compliance and Security Notes

**Security Posture Improvements:**
- **Before:** Validation logic in general-purpose library (Register.Core)
- **After:** Validation logic in secured service with controlled key access

**Enclave Deployment Path:**
1. **Development:** Standard .NET runtime
2. **Staging:** Azure Confidential Computing (AMD SEV-SNP)
3. **Production:** Intel SGX enclaves or HSM integration

**Audit Trail:**
- All docket building operations logged
- All validation decisions recorded
- Cryptographic operations tracked
- Failed validations investigated

---

## References

- [Validator Service Design](../validator-service-design.md)
- [Alpha Validator Analysis](../alpha-validator-service-analysis.md)
- [Unified Design Summary](../../.specify/UNIFIED-DESIGN-SUMMARY.md)
- [Register Service Phase 1-2 Completion](../register-service-phase1-2-completion.md)

---

## Learnings Captured

This decision surfaced several important learnings:

1. **Security boundaries must be established early** in service design
2. **Cryptographic operations** require special architectural consideration
3. **Test-first development** helped us discover the boundary violation
4. **Service decomposition** should follow security requirements, not just functional grouping

See: [Learnings Document](../learnings/2025-11-16-validator-service-refactoring.md)
