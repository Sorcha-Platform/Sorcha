# OpenAPI Documentation Review & Improvement Plan

**Date:** 2025-12-11
**Status:** ✅ Decisions Made - Implementation In Progress
**Target:** Production-Ready API Documentation

**📋 See Also:** [OpenAPI Documentation Decisions](./openapi-documentation-decisions.md) - Answers to all clarification questions

## Executive Summary

The Sorcha platform currently has OpenAPI documentation implemented across all services using .NET 10 built-in OpenAPI and Scalar UI. However, the documentation lacks:
- Comprehensive service introductions
- Real-world examples for endpoints
- Authentication/authorization workflows
- Complete aggregation (missing 3 services)
- Consistent descriptions and metadata

This document outlines the clarifications needed and proposes a systematic improvement plan.

---

## Current State Analysis

### ✅ What's Working

1. **Consistent Technology Stack**
   - All services use .NET 10 built-in OpenAPI (per constitution)
   - Scalar UI deployed (Purple theme, C# examples)
   - Security headers properly configured
   - Integration tests for aggregation

2. **Service Coverage**
   - Blueprint Service: ✓ OpenAPI + Scalar
   - Register Service: ✓ OpenAPI + Scalar
   - Peer Service: ✓ OpenAPI + Scalar
   - Tenant Service: ✓ OpenAPI + Scalar
   - Wallet Service: ✓ OpenAPI (no Scalar)
   - API Gateway: ✓ OpenAPI + Scalar + Aggregation

3. **Documentation Pattern**
   - Endpoints use `.WithName()`, `.WithSummary()`, `.WithDescription()`, `.WithTags()`
   - Minimal APIs with fluent documentation
   - OpenAPI 3.0.1 standard

### ❌ What's Missing/Broken

1. **Aggregation Issues**
   - Only 2 of 5 services aggregated (Blueprint, Peer)
   - Missing: Tenant, Wallet, Register services
   - No unified introduction/overview

2. **Documentation Gaps**
   - No service-level introductions explaining purpose
   - Limited or missing examples on most endpoints
   - No request/response examples
   - No error response documentation
   - No authentication flow documentation

3. **Metadata Issues**
   - Generic/incomplete descriptions
   - Missing operation summaries on some endpoints
   - Inconsistent tag usage
   - No versioning strategy

4. **User Experience**
   - No "Getting Started" guide
   - No workflow documentation (e.g., "How to create and submit a transaction")
   - No authentication examples
   - Scattered documentation across 6+ Scalar UIs

---

## Clarifications Needed

### 1. Service Descriptions & Purpose

**Question:** What should each service's high-level description say?

**Current State:** Generic or missing descriptions

**Needed for Each Service:**

#### Tenant Service
- **Purpose:** ?
- **Primary Use Cases:** ?
- **Key Concepts:** Organizations, Users, Service Principals, RBAC?
- **Target Audience:** Administrators, system integrators?

#### Wallet Service
- **Purpose:** Cryptographic key management for signing transactions?
- **Primary Use Cases:** Create wallets, sign data, manage keys?
- **Key Concepts:** HD wallets, BIP39, ED25519/NISTP256/RSA4096?
- **Security Model:** How are private keys stored/protected?

#### Register Service
- **Purpose:** Distributed ledger for immutable transaction records?
- **Primary Use Cases:** Create registers, query transactions, manage ledgers?
- **Key Concepts:** Registers, transactions, immutability, chain verification?

#### Peer Service
- **Purpose:** P2P network coordination and monitoring?
- **Primary Use Cases:** Network health, peer discovery, gossip protocol?
- **Key Concepts:** Peer nodes, network topology, consensus?

#### Blueprint Service
- **Purpose:** Workflow definition and execution?
- **Primary Use Cases:** Define workflows, execute actions, manage state?
- **Key Concepts:** Blueprints, actions, participants, JSON-LD?

### 2. Authentication & Authorization

**Question:** How should authentication be documented in OpenAPI?

**Current State:** No authentication documentation in OpenAPI specs

**Clarifications Needed:**
- What authentication scheme is used? (OAuth2, Bearer tokens?)
- Where do users get tokens? (`/api/service-auth/token`?)
- What are the required scopes/permissions?
- Should we use OpenAPI security schemes?
- Example token request/response?

**Proposed OpenAPI Security Scheme:**
```yaml
securitySchemes:
  BearerAuth:
    type: http
    scheme: bearer
    bearerFormat: JWT
    description: OAuth2 Bearer token from Tenant Service
```

### 3. Examples Strategy

**Question:** What level of examples should we provide?

**Options:**
1. **Minimal:** Just request schema, no examples
2. **Basic:** One example per endpoint
3. **Comprehensive:** Multiple examples (success, validation errors, auth errors)

**Recommendation:** **Comprehensive** - Include:
- ✓ Successful request/response
- ✓ Validation error (400)
- ✓ Authentication error (401)
- ✓ Authorization error (403)
- ✓ Not found (404)

### 4. Workflow Documentation

**Question:** What workflows should be documented?

**Proposed Workflows:**
1. **Getting Started**
   - Authenticate with service principal
   - Create organization
   - Create first user

2. **Wallet & Transaction Flow**
   - Create wallet
   - Get wallet address
   - Sign transaction data
   - Submit signed transaction to register

3. **Blueprint Execution**
   - Create blueprint from template
   - Add participants
   - Execute blueprint actions
   - Query execution status

4. **Peer Network Monitoring**
   - Check network health
   - View peer statistics
   - Monitor transaction distribution

**Where to Document?**
- In aggregated OpenAPI spec?
- Separate markdown guide?
- Scalar UI introduction section?

### 5. Error Response Standards

**Question:** How should errors be documented?

**Current State:** Inconsistent error responses

**Proposed Standard:**
```json
{
  "type": "https://sorcha.io/errors/validation-error",
  "title": "Validation Error",
  "status": 400,
  "detail": "The request contains invalid data",
  "errors": {
    "name": ["The name field is required"],
    "email": ["The email field must be a valid email address"]
  }
}
```

**Should we use RFC 7807 Problem Details?**

### 6. Versioning Strategy

**Question:** How should API versioning be handled?

**Options:**
1. **URL versioning:** `/api/v1/wallets`
2. **Header versioning:** `Accept: application/vnd.sorcha.v1+json`
3. **No versioning:** Breaking changes deploy as new services

**Current State:** No versioning strategy

**Recommendation?**

### 7. Aggregated Documentation Structure

**Question:** How should the aggregated OpenAPI spec be organized?

**Proposed Structure:**
```yaml
openapi: 3.0.1
info:
  title: Sorcha Platform API
  version: 1.0.0
  description: |
    # Sorcha Distributed Ledger Platform

    ## Overview
    Sorcha is a distributed ledger platform...

    ## Architecture
    - **Tenant Service:** Multi-tenant organization management
    - **Wallet Service:** Cryptographic key management
    - **Register Service:** Immutable transaction ledger
    - **Peer Service:** P2P network coordination
    - **Blueprint Service:** Workflow orchestration

    ## Getting Started
    1. Authenticate...
    2. Create organization...
    3. Create wallet...

    ## Common Workflows
    ### Submit a Transaction
    ...

  contact:
    name: Sorcha Contributors
    url: https://sorcha.io
  license:
    name: MIT
    url: https://opensource.org/licenses/MIT

servers:
  - url: https://api.sorcha.io
    description: Production API Gateway
  - url: http://localhost:8080
    description: Local Development (Docker)

tags:
  - name: Organizations
    description: Organization management (Tenant Service)
  - name: Users
    description: User management (Tenant Service)
  - name: Wallets
    description: Cryptographic wallet operations (Wallet Service)
  - name: Registers
    description: Distributed ledger management (Register Service)
  - name: Transactions
    description: Transaction submission and queries (Register Service)
  - name: Peers
    description: Network monitoring (Peer Service)
  - name: Blueprints
    description: Workflow orchestration (Blueprint Service)
```

**Is this the right approach?**

### 8. Code Examples

**Question:** What programming languages should we show examples for?

**Current State:** C# HttpClient only (via Scalar)

**Should We Add:**
- ✓ C# (already configured)
- ? TypeScript/JavaScript
- ? Python
- ? cURL (most common)
- ? Go

**Recommendation?**

### 9. Service-Level Documentation Files

**Question:** Should each service have its own detailed OpenAPI documentation before aggregation?

**Proposed Approach:**
1. **Phase 1:** Enhance each service's local OpenAPI spec
   - Add comprehensive descriptions
   - Add examples to all endpoints
   - Document error responses
   - Add service introduction

2. **Phase 2:** Update aggregation service
   - Add Tenant, Wallet, Register services
   - Merge enhanced specs
   - Add platform-level introduction
   - Add workflow documentation

**Or:** Do aggregation first, then push down to services?

### 10. Deprecation Policy

**Question:** How should deprecated endpoints be marked?

**OpenAPI Approach:**
```yaml
deprecated: true
description: |
  **DEPRECATED:** This endpoint is deprecated and will be removed in v2.0.
  Use `/api/v2/wallets` instead.
```

**Should we establish a deprecation timeline?**
- Mark as deprecated in v1.x
- Remove in v2.0
- Minimum 6-month notice?

---

## Proposed Task Categories

Based on the clarifications above, here are the major work categories:

### Category 1: Service-Level Documentation Enhancement
- Update each service's OpenAPI metadata
- Add service introductions
- Add comprehensive examples
- Document error responses
- Add authentication information

### Category 2: Aggregation Service Updates
- Add missing services (Tenant, Wallet, Register)
- Implement platform-level introduction
- Add workflow documentation
- Add "Getting Started" guide
- Implement proper tag organization

### Category 3: Standards & Conventions
- Establish error response format
- Define authentication documentation standard
- Create versioning strategy
- Establish deprecation policy
- Define example requirements

### Category 4: User Experience Improvements
- Add Scalar UI to Wallet Service
- Create unified documentation landing page
- Add tutorial/walkthrough examples
- Add Postman/Insomnia collection generation
- Create SDK generation pipeline

### Category 5: Testing & Validation
- Update integration tests for all aggregated services
- Validate OpenAPI spec compliance
- Test examples in Scalar UI
- Performance test aggregation service
- Verify security headers on all documentation endpoints

---

## Questions for Product Owner / Architect

### Priority Questions

1. **What is the primary audience for the API documentation?**
   - Internal developers only?
   - External integrators?
   - Public API consumers?

2. **What is the timeline for documentation improvements?**
   - Critical for MVP/MVD release?
   - Post-launch improvement?
   - Ongoing refinement?

3. **What is the acceptable level of detail?**
   - Minimal (just enough to integrate)?
   - Standard (industry best practices)?
   - Comprehensive (enterprise-grade documentation)?

4. **Should we auto-generate client SDKs from OpenAPI specs?**
   - Yes → Need pristine, validated specs
   - No → Specs can be less strict

5. **What is the authentication model for production?**
   - OAuth2 with Tenant Service as auth server?
   - External identity provider (Azure AD, Auth0)?
   - API keys?
   - mTLS?

### Technical Questions

6. **Error response format standard?**
   - RFC 7807 Problem Details?
   - Custom format?
   - Service-specific formats?

7. **Versioning strategy?**
   - URL-based (`/v1/`, `/v2/`)?
   - Header-based?
   - No versioning (deploy breaking changes as new services)?

8. **Example data strategy?**
   - Synthetic/realistic examples?
   - Anonymized production data?
   - Standardized test dataset?

9. **Should OpenAPI specs be version controlled separately?**
   - Generate from code (current approach)?
   - Hand-written YAML files?
   - Hybrid approach?

10. **Performance considerations for aggregation?**
    - Fetch specs on every request (current)?
    - Cache specs with TTL?
    - Pre-build aggregated spec at deployment?

---

## Recommended Approach

Based on best practices, I recommend the following approach:

### Phase 1: Service Documentation (2-3 days per service)
1. Enhance each service's local OpenAPI spec
2. Add service introduction and purpose
3. Add examples to all endpoints
4. Document authentication requirements
5. Standardize error responses
6. Add Scalar UI to Wallet Service

### Phase 2: Aggregation Enhancement (3-5 days)
1. Add missing services to aggregation
2. Create platform-level introduction
3. Add "Getting Started" guide
4. Document common workflows
5. Implement proper tag organization
6. Add authentication flow documentation

### Phase 3: Standards & Tooling (2-3 days)
1. Document API standards (errors, versioning, deprecation)
2. Create API style guide
3. Add validation/linting for OpenAPI specs
4. Set up CI/CD for documentation deployment
5. Create Postman collection from OpenAPI

### Phase 4: Testing & Validation (1-2 days)
1. Test all examples in Scalar UI
2. Validate OpenAPI spec compliance
3. Performance test aggregation
4. User acceptance testing with sample integrators

**Total Estimated Effort:** 15-20 working days

---

## Next Steps

1. **Review this document** with product owner and architects
2. **Answer clarification questions** above
3. **Prioritize** which phases/tasks are critical for next release
4. **Create detailed task list** based on decisions
5. **Assign ownership** for each service's documentation
6. **Set timeline** for completion

---

## Appendix: Current OpenAPI Endpoints

### Service OpenAPI Locations

| Service | OpenAPI Spec | Scalar UI |
|---------|--------------|-----------|
| Tenant Service | `https://localhost:7080/openapi/v1.json` | `https://localhost:7080/scalar` |
| Wallet Service | `https://localhost:7081/openapi/v1.json` | ❌ Not configured |
| Register Service | `https://localhost:7082/openapi/v1.json` | `https://localhost:7082/scalar` |
| Peer Service | `http://localhost:5000/openapi/v1.json` | `http://localhost:5000/scalar` |
| Blueprint Service | `https://localhost:7083/openapi/v1.json` | `https://localhost:7083/scalar` |
| API Gateway | `https://localhost:8061/openapi/v1.json` | `https://localhost:8061/scalar` |
| **Aggregated** | `https://localhost:8061/openapi/aggregated.json` | `https://localhost:8061/scalar` |

### Documentation Index

`GET https://localhost:8061/api/docs` - JSON index of all documentation links

---

## Contact

For questions about this document, contact the Sorcha Architecture Team.
