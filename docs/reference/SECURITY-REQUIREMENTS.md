# Sorcha Security Requirements

**Document Type:** Security Requirements Specification
**Version:** 1.0
**Date:** 2025-11-16
**Status:** Active
**Last Updated:** 2025-11-16

## Overview

This document defines the permanent security requirements for the Sorcha platform, capturing architectural decisions, component placement rules, and security best practices that must be maintained across all development activities.

## Table of Contents

1. [Functional Security Requirements](#functional-security-requirements)
2. [Non-Functional Security Requirements](#non-functional-security-requirements)
3. [Component Placement Requirements](#component-placement-requirements)
4. [Cryptographic Requirements](#cryptographic-requirements)
5. [Testing Requirements](#testing-requirements)
6. [Compliance and Audit](#compliance-and-audit)

---

## Functional Security Requirements

### FR-SEC-001: Secured Environment for Cryptographic Operations

**Requirement:** Components performing cryptographic operations MUST run in secured environments with proper access to encryption keys.

**Rationale:**
- Cryptographic operations (hashing, signing, verification) are security-critical
- Encryption keys must be protected and only accessible in secured environments
- Supports future deployment in hardware security modules (HSMs) or secure enclaves

**Implementation:**
- DocketManager: Runs in Validator.Service (secured environment)
- ChainValidator: Runs in Validator.Service (secured environment)
- Future cryptographic components: Must be evaluated for secured placement

**Verification:**
- Code review: Verify component location matches security requirements
- Architecture review: Confirm secured environment configuration
- Deployment review: Validate production security controls

**Status:** Implemented (2025-11-16)

---

### FR-SEC-002: Separation of Storage and Validation

**Requirement:** Storage operations and validation/consensus operations MUST be separated into different services.

**Rationale:**
- Storage services handle data persistence (non-sensitive operations)
- Validation services handle cryptographic verification (sensitive operations)
- Separation enables different security postures and deployment models

**Implementation:**
- Register.Service: Handles storage, retrieval, querying (non-cryptographic)
- Validator.Service: Handles validation, consensus, cryptographic operations (secured)

**Verification:**
- Dependency analysis: No cryptographic operations in Register.Service
- Interface review: Clear boundaries between storage and validation APIs
- Integration tests: Verify correct service responsibilities

**Status:** Implemented (2025-11-16)

---

### FR-SEC-003: Zero-Trust Component Boundaries

**Requirement:** Security-sensitive components MUST have explicit trust boundaries and access controls.

**Rationale:**
- Zero-trust architecture assumes no implicit trust
- Explicit boundaries enable security monitoring and access control
- Supports compliance requirements for cryptographic operations

**Implementation:**
- Validator.Service: Explicit security boundary for cryptographic operations
- Future: mTLS for service-to-service authentication
- Future: API gateway with authentication/authorization

**Verification:**
- Security testing: Verify access controls at service boundaries
- Penetration testing: Validate trust boundaries cannot be bypassed
- Compliance audit: Document trust model

**Status:** Partially Implemented (service boundary established)

---

## Non-Functional Security Requirements

### NFR-SEC-001: Enclave Deployment Support

**Requirement:** Cryptographic validation components MUST support deployment in secure enclaves (Intel SGX, AMD SEV, or equivalent).

**Rationale:**
- Secure enclaves provide hardware-based isolation
- Critical for high-security deployments
- Protects cryptographic operations from system-level attacks

**Implementation:**
- Sorcha.Validator.Core: Pure library with no I/O dependencies (enclave-safe)
- DocketManager: Uses only standard cryptography APIs compatible with enclaves
- ChainValidator: Stateless validation logic suitable for enclave execution

**Verification:**
- Code review: Confirm no I/O operations in validation logic
- Architecture review: Validate enclave compatibility
- Performance testing: Measure enclave overhead

**Status:** Design Complete (enclave deployment not yet tested)

---

### NFR-SEC-002: Security Isolation for Cryptographic Validation

**Requirement:** Cryptographic validation components MUST be isolated from general application logic.

**Rationale:**
- Reduces attack surface for critical security operations
- Enables focused security hardening
- Simplifies security audits and compliance

**Implementation:**
- DocketManager: Isolated in Validator.Service
- ChainValidator: Isolated in Validator.Service
- No shared state with non-security components

**Verification:**
- Dependency graph analysis: Verify no circular dependencies
- Code review: Confirm isolation maintained
- Security audit: Review component boundaries

**Status:** Implemented (2025-11-16)

---

### NFR-SEC-003: Deterministic Cryptographic Operations

**Requirement:** All cryptographic operations MUST produce deterministic results for identical inputs.

**Rationale:**
- Blockchain requires deterministic hashing for consensus
- Non-deterministic operations cause chain forks
- Critical for multi-node validation agreement

**Implementation:**
- DocketManager.CalculateDocketHash(): Deterministic serialization with sorted collections
- Timestamp formatting: ISO 8601 format for consistency
- Hash comparison: Case-insensitive hex string comparison

**Verification:**
- Unit tests: Verify identical inputs produce identical hashes
- Consensus tests: Verify multiple nodes agree on hash values
- Property-based tests: Random input testing for determinism

**Status:** Implemented (2025-11-16)

---

## Component Placement Requirements

### CPR-001: Cryptographic Component Placement

**Decision Criteria for Component Placement:**

Components MUST be placed in **Validator.Service** if they:
- ✅ Perform cryptographic operations (hashing, signing, verification)
- ✅ Require access to encryption keys
- ✅ Validate chain integrity
- ✅ Coordinate consensus
- ✅ Need hardware security module (HSM) access

Components MUST be placed in **Register.Core** if they:
- ✅ Define repository interfaces
- ✅ Implement storage abstractions
- ✅ Define data models (non-cryptographic)
- ✅ Implement business logic (non-cryptographic)

Components MUST be placed in **Validator.Core** if they:
- ✅ Provide stateless validation functions
- ✅ Have no I/O operations
- ✅ Have no network calls
- ✅ Must be enclave-safe

**Examples:**
- ✅ DocketManager → Validator.Service (performs SHA256 hashing)
- ✅ ChainValidator → Validator.Service (validates cryptographic chain integrity)
- ✅ IRegisterRepository → Register.Core (storage abstraction)
- ✅ DocketValidation (pure functions) → Validator.Core (enclave-safe)

**Status:** Active Guideline (2025-11-16)

---

## Cryptographic Requirements

### CR-001: Standard Cryptography Libraries

**Requirement:** MUST use .NET standard cryptography libraries (System.Security.Cryptography).

**Rationale:**
- Avoid custom crypto implementations (high risk of vulnerabilities)
- Leverage Microsoft security updates
- Industry-standard implementations

**Implementation:**
- SHA256.HashData() for docket hashing
- No custom hash implementations
- No deprecated algorithms (MD5, SHA1)

**Verification:**
- Code review: Verify System.Security.Cryptography usage
- Security scan: Flag custom crypto implementations
- Dependency audit: No third-party crypto libraries without review

**Status:** Implemented (2025-11-16)

---

### CR-002: Deterministic Serialization for Hashing

**Requirement:** Object serialization for hashing MUST be deterministic.

**Rationale:**
- Non-deterministic serialization causes different hashes for same data
- Critical for blockchain consensus
- Prevents accidental chain forks

**Implementation:**
- Sort collections before serialization (TransactionIds.OrderBy())
- Use ISO 8601 timestamp format (TimeStamp.ToString("O"))
- Explicit property ordering in serialization

**Verification:**
- Unit tests: Verify deterministic serialization
- Property-based tests: Random order inputs produce same hash
- Integration tests: Multi-node consensus verification

**Status:** Implemented (2025-11-16)

---

### CR-003: Case-Insensitive Hash Comparison

**Requirement:** Hexadecimal hash strings MUST be compared case-insensitively.

**Rationale:**
- Hex encoding may produce uppercase or lowercase
- Case sensitivity causes false validation failures
- Standard practice for hex string comparison

**Implementation:**
```csharp
return calculatedHash.Equals(docket.Hash, StringComparison.OrdinalIgnoreCase);
```

**Verification:**
- Unit tests: Test both uppercase and lowercase hash variants
- Integration tests: Verify cross-platform compatibility

**Status:** Implemented (2025-11-16)

---

## Testing Requirements

### TR-SEC-001: Security Component Unit Tests

**Requirement:** All cryptographic components MUST have comprehensive unit tests (90%+ coverage).

**Rationale:**
- Security bugs in crypto code are critical
- High test coverage reduces vulnerability risk
- Regression testing for security fixes

**Implementation:**
- DocketManager: 90%+ test coverage
- ChainValidator: 90%+ test coverage
- Test cases for edge conditions and error paths

**Verification:**
- Code coverage reports
- CI/CD enforcement of coverage thresholds
- Security review of test completeness

**Status:** In Progress

---

### TR-SEC-002: Cryptographic Operation Tests

**Requirement:** All cryptographic operations MUST have dedicated tests verifying correctness and determinism.

**Rationale:**
- Crypto bugs can cause data loss or security breaches
- Determinism critical for blockchain consensus
- Explicit testing of security properties

**Implementation:**
- Hash calculation tests (determinism, correctness)
- Hash verification tests (valid and invalid cases)
- Chain integrity tests (valid chains, broken chains)

**Verification:**
- Test review: Verify all crypto operations tested
- Property-based testing: Random input validation
- Cross-validation: Compare with reference implementations

**Status:** In Progress

---

### TR-SEC-003: Security Boundary Tests

**Requirement:** Service boundaries MUST be tested for proper isolation and access control.

**Rationale:**
- Verify security architecture is correctly implemented
- Prevent unauthorized access to cryptographic operations
- Validate zero-trust boundaries

**Implementation:**
- Integration tests: Verify Register.Service cannot perform cryptographic operations
- Integration tests: Verify Validator.Service requires proper authentication
- Security tests: Attempt boundary violations

**Verification:**
- Security testing suite execution
- Penetration testing results
- Architecture review

**Status:** Planned

---

## Compliance and Audit

### Audit Trail Requirements

All architectural decisions affecting security MUST be documented in:
1. **architecture.md** - Architectural notes and rationale
2. **LEARNINGS.md** - Decision criteria and best practices
3. **SECURITY-REQUIREMENTS.md** (this document) - Formal requirements
4. **Component specifications** - Individual component security requirements

### Change Management

Security requirements changes require:
1. Security review and approval
2. Impact analysis on existing components
3. Update to all affected documentation
4. Verification through testing
5. Commit message linking to security requirement

### Periodic Review

This document MUST be reviewed:
- Quarterly for relevance and completeness
- After any security incident
- Before major architecture changes
- During compliance audits

---

## Related Documentation

- [Architecture](architecture.md) - Overall system architecture
- [Validator Service Design](validator-service-design.md) - Validator service specification
- [Register Service Specification](../.specify/specs/sorcha-register-service.md) - Register service specification
- [Learnings](LEARNINGS.md) - Development best practices and lessons learned

---

## Document Control

**Version History:**
- 1.0 (2025-11-16): Initial creation capturing DocketManager/ChainValidator placement requirements

**Approvals:**
- Architecture Review: Pending
- Security Review: Pending
- Compliance Review: Pending

**Next Review Date:** 2025-02-16 (3 months)

---

**Document Owner:** Sorcha Architecture Team
**Last Updated By:** Claude Code (Anthropic)
**Status:** Active - Requires periodic review
