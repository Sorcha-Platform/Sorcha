# OpenAPI Documentation - Decisions & Standards

**Date:** 2025-12-11
**Status:** Approved
**Version:** 1.0
**Purpose:** Answers to clarification questions from the OpenAPI documentation review

---

## Decision Summary

This document provides definitive answers to the 10 clarification questions raised during the OpenAPI documentation review, along with the rationale behind each decision.

---

## 1. Service Descriptions & Purpose ✅ IMPLEMENTED

**Decision:** Each service has a comprehensive introduction covering purpose, use cases, key concepts, security model, and target audience.

### Implementation Status
✅ **Tenant Service** - Multi-tenant organization management and authentication
✅ **Wallet Service** - Cryptographic wallet management and transaction signing
✅ **Register Service** - Distributed ledger for immutable transaction storage
✅ **Blueprint Service** - Workflow orchestration and execution
✅ **Peer Service** - P2P network monitoring and coordination

**Rationale:**
- Comprehensive service descriptions reduce developer onboarding time
- Clear purpose statements help developers choose the right service
- Security model documentation builds trust
- Examples accelerate integration

**Files Modified:**
- `src/Services/Sorcha.Tenant.Service/Program.cs`
- `src/Services/Sorcha.Wallet.Service/Program.cs`
- `src/Services/Sorcha.Register.Service/Program.cs`

---

## 2. Authentication & Authorization

**Decision:** Use JWT Bearer tokens with documented OpenAPI security schemes.

### Authentication Model
- **Scheme:** HTTP Bearer (JWT)
- **Token Endpoint:** `/api/tenant/api/service-auth/token`
- **Token Format:** JWT with configurable expiry
- **Refresh:** Token refresh supported (future enhancement)
- **Revocation:** Token revocation via Redis cache

### OpenAPI Security Scheme
```yaml
components:
  securitySchemes:
    BearerAuth:
      type: http
      scheme: bearer
      bearerFormat: JWT
      description: |
        JWT Bearer token obtained from Tenant Service.
        Authenticate via POST /api/tenant/api/service-auth/token
        with clientId and clientSecret.
```

### Token Request Example
```http
POST /api/tenant/api/service-auth/token
Content-Type: application/json

{
  "clientId": "your-client-id",
  "clientSecret": "your-client-secret"
}
```

### Token Response
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "expiresAt": "2025-12-11T11:30:00Z",
  "refreshToken": "..."
}
```

**Rationale:**
- JWT is industry-standard for stateless authentication
- HTTP Bearer scheme is natively supported by OpenAPI tooling
- Service principal model suits machine-to-machine communication
- Documented in service introductions (already implemented)

**Next Steps:**
- Add `securitySchemes` to each service's OpenAPI document transformer
- Add `security` requirement to protected endpoints
- Document scopes/permissions (future)

---

## 3. Examples Strategy

**Decision:** **Comprehensive examples** for critical endpoints, **Basic examples** for standard CRUD operations.

### Example Coverage Levels

#### Critical Endpoints (Comprehensive)
Endpoints requiring comprehensive examples (success + errors):
- `/api/service-auth/token` (authentication)
- `/api/wallets` POST (wallet creation - shows mnemonic)
- `/api/wallets/{id}/sign` POST (transaction signing)
- `/api/registers/{id}/transactions` POST (transaction submission)
- `/api/blueprints/{id}/execute` POST (blueprint execution)

**Include:**
- ✅ Successful request/response (200/201)
- ✅ Validation error (400)
- ✅ Authentication error (401)
- ✅ Not found (404)

#### Standard Endpoints (Basic)
CRUD operations need one success example:
- List endpoints (GET /api/wallets)
- Get by ID (GET /api/wallets/{id})
- Update (PUT /api/wallets/{id})
- Delete (DELETE /api/wallets/{id})

**Include:**
- ✅ Successful request/response only

**Rationale:**
- Balance between completeness and maintainability
- Critical paths need error handling examples
- CRUD operations follow predictable patterns
- Reduces documentation overhead

**Implementation Priority:**
- Phase 1 (current): Service introductions with workflow examples ✅ DONE
- Phase 2 (next): Add examples to critical endpoints
- Phase 3 (future): Add examples to all endpoints

---

## 4. Workflow Documentation

**Decision:** Document workflows in **aggregated OpenAPI** introduction and create separate **markdown guides** for detailed tutorials.

### Workflows Documented in Aggregated OpenAPI ✅ IMPLEMENTED

1. **Getting Started** (5 steps)
   - Authenticate with service principal
   - Create organization
   - Create wallet
   - Create register
   - Submit first transaction

2. **Document Timestamping**
   - Create organization and wallet
   - Create register for document management
   - Hash document and submit as transaction

3. **Multi-Party Workflow**
   - Create blueprint defining workflow steps
   - Each participant creates a wallet
   - Actions execute in sequence, creating transactions
   - Immutable audit trail in register

4. **Audit Trail Creation**
   - System events logged as transactions
   - Each event signed by system wallet
   - Transactions chained for integrity

### Location
- ✅ **Aggregated OpenAPI**: High-level workflow overview (implemented)
- ⏳ **Separate Markdown Guides**: Detailed step-by-step tutorials (future)
  - `docs/workflows/getting-started.md`
  - `docs/workflows/document-timestamping.md`
  - `docs/workflows/multi-party-collaboration.md`
  - `docs/workflows/audit-trail.md`

**Rationale:**
- OpenAPI provides quick reference
- Markdown guides allow richer content (screenshots, diagrams)
- Separation of concerns: API reference vs tutorials
- Markdown guides can be versioned independently

---

## 5. Error Response Standards

**Decision:** Use **RFC 7807 Problem Details** format with .NET 10 built-in support.

### Standard Error Format

```json
{
  "type": "https://sorcha.io/errors/validation-error",
  "title": "Validation Error",
  "status": 400,
  "detail": "The request contains invalid data",
  "errors": {
    "name": ["The name field is required"],
    "email": ["The email field must be a valid email address"]
  },
  "traceId": "00-abc123-def456-01"
}
```

### Error Types by Status Code

| Status | Type | Example |
|--------|------|---------|
| 400 | `validation-error` | Invalid request data |
| 401 | `authentication-error` | Missing or invalid token |
| 403 | `authorization-error` | Insufficient permissions |
| 404 | `not-found` | Resource doesn't exist |
| 409 | `conflict-error` | Duplicate resource |
| 500 | `internal-error` | Server error |

### Implementation

.NET 10 provides built-in support via `ProblemDetails`:

```csharp
app.MapPost("/api/wallets", async (CreateWalletRequest request) =>
{
    if (!IsValid(request))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = ["The name field is required"]
        });
    }

    // ... implementation
})
.ProducesValidationProblem()
.ProducesProblem(401)
.ProducesProblem(500);
```

**Rationale:**
- RFC 7807 is IETF standard for HTTP API errors
- .NET 10 has built-in `ProblemDetails` support
- Consistent error format across all services
- Includes `traceId` for debugging

**Next Steps:**
- Update all endpoints to return `ProblemDetails`
- Add `.ProducesValidationProblem()` and `.ProducesProblem()` to endpoints
- Document error responses in OpenAPI

---

## 6. Versioning Strategy

**Decision:** **No URL versioning for v1.0**, with option to add URL versioning (`/api/v2/...`) when breaking changes are needed.

### Versioning Approach

**Current (v1.0):**
- No version in URL: `/api/wallets`
- Version in OpenAPI spec: `version: 1.0.0`
- Breaking changes avoided via additive changes

**Future (v2.0+ if needed):**
- URL versioning: `/api/v2/wallets`
- Old version maintained: `/api/v1/wallets` (deprecated)
- OpenAPI spec per version: `/openapi/v1.json`, `/openapi/v2.json`

### Deprecation Strategy
- Mark endpoints as `deprecated: true` in OpenAPI
- Minimum 6-month notice before removal
- Include migration guide in description
- Log deprecation warnings

### Example Deprecation
```yaml
/api/wallets/legacy:
  post:
    deprecated: true
    summary: Create Wallet (Legacy)
    description: |
      **DEPRECATED:** This endpoint will be removed in v2.0 (2026-06-01).
      Use `POST /api/v2/wallets` instead.

      Migration guide: https://docs.sorcha.io/migration/v1-to-v2
```

**Rationale:**
- Avoids `/v1/` noise in all URLs for initial release
- URL versioning is clearest for consumers
- Additive changes don't require versioning
- Follows REST best practices (resource-oriented URLs)

**Policy:**
- ✅ Additive changes: No version bump (add optional fields, new endpoints)
- ⚠️ Breaking changes: New version (`/api/v2/...`)
- ❌ Avoid breaking changes when possible

---

## 7. Aggregated Documentation Structure ✅ IMPLEMENTED

**Decision:** Implement the proposed structure with platform overview, service architecture, getting started guide, and common workflows.

### Implemented Structure
```yaml
openapi: 3.0.1
info:
  title: Sorcha Platform API
  version: 1.0.0
  description: |
    # Sorcha Distributed Ledger Platform
    ## Overview
    ## Platform Architecture (5 services)
    ## Getting Started (5-step guide)
    ## Common Workflows (3 workflows)
    ## Key Features (Security, Privacy, Integrity, Auditability)
    ## API Standards
    ## Client Libraries
    ## Support & Resources
  contact:
    name: Sorcha Contributors
    url: https://github.com/sorcha-platform/sorcha
  license:
    name: MIT License
    url: https://opensource.org/licenses/MIT

servers:
  - url: http://localhost:8080
    description: API Gateway (Docker)
  - url: /
    description: Relative URL

tags:
  - name: Tenant Service/Organizations
  - name: Tenant Service/Users
  - name: Wallet Service/Wallets
  - name: Register Service/Registers
  - name: Register Service/Transactions
  - name: Peer Service/Peers
  - name: Blueprint Service/Blueprints
```

**Status:** ✅ Fully implemented in `OpenApiAggregationService.cs`

**Rationale:**
- Provides complete platform picture
- Getting Started reduces onboarding friction
- Workflows demonstrate real-world usage
- Clear service boundaries via tags

---

## 8. Code Examples

**Decision:** Provide **C#** and **cURL** examples via Scalar configuration.

### Language Support

**Tier 1 (Implemented):**
- ✅ **C# HttpClient** - Primary language for .NET developers

**Tier 2 (Next Priority):**
- ⏳ **cURL** - Universal command-line tool
- ⏳ **TypeScript/JavaScript** - Web developers, Node.js integrations

**Tier 3 (Future):**
- ⏳ **Python** - Data science, scripting
- ⏳ **Go** - Cloud-native applications

### Scalar Configuration

Current (C# only):
```csharp
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Service Name")
        .WithTheme(ScalarTheme.Purple)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});
```

Next (C# + cURL):
```csharp
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Service Name")
        .WithTheme(ScalarTheme.Purple)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithHttpClient(ScalarTarget.Shell, ScalarClient.Curl);
});
```

**Rationale:**
- C# is primary development language
- cURL is universal and scriptable
- Scalar UI handles code generation automatically
- Additional languages can be added without OpenAPI changes

**Implementation:**
- Phase 1: C# only ✅ DONE
- Phase 2: Add cURL support
- Phase 3: Add TypeScript and Python

---

## 9. Service-Level Documentation Files

**Decision:** **Bottom-up approach** - Enhance each service's OpenAPI spec first, then aggregate.

### Implementation Phases ✅ COMPLETED

**Phase 1: Service-Level Enhancement** ✅ DONE
- ✅ Add service introductions (Tenant, Wallet, Register)
- ✅ Add Scalar UI to all services
- ⏳ Add examples to critical endpoints (next)
- ⏳ Document error responses (next)

**Phase 2: Aggregation Enhancement** ✅ DONE
- ✅ Add missing services (Tenant, Wallet, Register)
- ✅ Add platform-level introduction
- ✅ Add workflow documentation
- ✅ Add "Getting Started" guide

**Phase 3: Standards & Polish** ⏳ NEXT
- Define and implement error response standards
- Add authentication security schemes
- Add comprehensive examples
- Add OpenAPI validation

**Rationale:**
- Service-level specs are source of truth
- Aggregation automatically merges enhanced specs
- Allows independent service documentation updates
- Follows microservices best practices

**Current Status:**
- Service introductions: ✅ 100% complete
- Aggregation: ✅ 100% complete (all 5 services)
- Examples: 🟡 In service descriptions only
- Error docs: 🟡 Described in introductions only

---

## 10. Deprecation Policy

**Decision:** Establish a **6-month minimum notice** period with clear migration paths.

### Deprecation Workflow

1. **Mark as Deprecated** (Month 0)
   - Set `deprecated: true` in OpenAPI
   - Add deprecation notice to description
   - Include sunset date (6 months minimum)
   - Provide migration path
   - Log warning when endpoint is called

2. **Communication** (Months 0-3)
   - Update documentation with deprecation notice
   - Send email to registered API consumers
   - Add banner to Scalar UI for deprecated endpoints
   - Create migration guide

3. **Monitor Usage** (Months 3-6)
   - Track deprecated endpoint usage
   - Contact heavy users directly
   - Provide migration assistance
   - Extend timeline if needed

4. **Removal** (Month 6+)
   - Remove endpoint in next major version
   - Return 410 Gone for old endpoints
   - Redirect to new endpoint if applicable

### OpenAPI Deprecation Format

```yaml
/api/wallets/create:
  post:
    deprecated: true
    summary: "[DEPRECATED] Create Wallet (Legacy)"
    description: |
      ⚠️ **DEPRECATED - Scheduled for removal on 2026-06-11**

      This endpoint is deprecated and will be removed in v2.0.
      Please use `POST /api/wallets` instead.

      **Migration Guide:**
      - Old: `POST /api/wallets/create` with `name` field
      - New: `POST /api/wallets` with `name` and `algorithm` fields

      **Why deprecated:** The new endpoint supports multiple cryptographic algorithms.

      **Support:** Contact support@sorcha.io if you need assistance migrating.
    tags:
      - Wallets (Deprecated)
```

### Deprecation Timeline

| Version | Deprecated | Removed | Notice Period |
|---------|------------|---------|---------------|
| v1.0 → v1.5 | Mark deprecated | Still available | 6 months min |
| v1.5 → v2.0 | Still deprecated | Remove endpoint | Major version |

**Rationale:**
- 6 months gives consumers time to migrate
- Clear communication reduces friction
- Migration guides reduce support burden
- Major version bumps signal breaking changes

**Policy:**
- **Minimum notice:** 6 months
- **Breaking changes:** Only in major versions (v1.x → v2.0)
- **Security fixes:** Can break compatibility with shorter notice
- **Documentation:** Migration guide required for all deprecations

---

## Priority Questions Answered

### 1. Primary Audience

**Answer:** **Mixed audience** with emphasis on external integrators.

- **Internal Developers** (40%) - Building Sorcha platform features
- **External Integrators** (50%) - Building applications on Sorcha
- **Public API Consumers** (10%) - Potential future SaaS offering

**Impact on Documentation:**
- Comprehensive examples required
- Security and authentication well-documented
- Workflow guides for common use cases
- Assume moderate technical expertise

---

### 2. Timeline for Documentation Improvements

**Answer:** **Iterative approach** with MVD baseline completed.

- ✅ **MVD Baseline** (Current Sprint) - Service introductions, aggregation ✅ COMPLETE
- ⏳ **Post-MVD** (Next 2 sprints) - Examples, error docs, security schemes
- 🔄 **Ongoing** - Refinement based on developer feedback

**Milestone:**
- **MVD Release:** Comprehensive introductions ✅ DONE
- **v1.0 Release:** Full examples and error documentation
- **Post-launch:** Tutorials, SDK generation, Postman collections

---

### 3. Acceptable Level of Detail

**Answer:** **Comprehensive** (enterprise-grade documentation).

**Justification:**
- Distributed ledger platforms require high trust
- Cryptographic operations need clear documentation
- Multi-service architecture needs workflow guidance
- Target enterprise customers expect thorough docs

**Standards:**
- ✅ Service purpose and architecture
- ✅ Getting started guides
- ✅ Security model documentation
- ✅ Code examples (C# + cURL)
- ⏳ Comprehensive error documentation
- ⏳ Migration guides for breaking changes

---

### 4. Auto-Generate Client SDKs

**Answer:** **Yes** - OpenAPI specs designed for SDK generation.

**Supported Tools:**
- **NSwag** - C# client generation (recommended)
- **OpenAPI Generator** - Multi-language support
- **Kiota** - Microsoft's API client generator

**Quality Requirements:**
- ✅ Valid OpenAPI 3.0.1 specs
- ✅ Complete request/response schemas
- ⏳ Comprehensive examples for SDK documentation
- ⏳ Validated specs (openapi-validator)

**SDK Languages (Priority Order):**
1. **C#** - Primary language
2. **TypeScript** - Web/Node.js developers
3. **Python** - Data science, automation
4. **Go** - Cloud-native deployments

---

### 5. Authentication Model for Production

**Answer:** **JWT Bearer tokens via Tenant Service** (OAuth2-style).

**Production Authentication:**
- **Service-to-Service:** Client credentials (clientId + clientSecret)
- **User Authentication:** Email/password → JWT token
- **Token Storage:** Redis for revocation, stateless JWT validation
- **Token Expiry:** Configurable (default 1 hour)
- **Refresh Tokens:** Supported (future enhancement)

**Future Enhancements:**
- Azure AD integration (enterprise customers)
- OAuth2 authorization code flow (user consent)
- API key authentication (webhook callbacks)
- mTLS for high-security environments

---

### 6. Error Response Format Standard

**Answer:** **RFC 7807 Problem Details** with .NET 10 built-in support.

(See Question 5 above for full details)

---

### 7. Versioning Strategy

**Answer:** **No versioning for v1.0**, URL versioning (`/api/v2/...`) for breaking changes.

(See Question 6 above for full details)

---

### 8. Example Data Strategy

**Answer:** **Synthetic, realistic examples** with consistent test dataset.

**Example Data Standards:**
- **Organizations:** "Acme Corporation", "Globex Industries"
- **Wallet IDs:** `wallet-abc123`, `wallet-def456`
- **Register IDs:** `my-register-001`, `audit-ledger-002`
- **Transaction Hashes:** Realistic hex strings
- **Timestamps:** ISO 8601 format, recent dates
- **Payloads:** Base64-encoded realistic data

**Test Dataset:**
- Create `docs/examples/test-dataset.md`
- Consistent across all documentation
- Anonymized, no real customer data
- Covers common and edge cases

**Rationale:**
- Synthetic data avoids privacy issues
- Realistic examples feel authentic
- Consistent dataset aids comprehension
- Enables copy-paste testing

---

### 9. OpenAPI Specs Version Control

**Answer:** **Generate from code** (current approach).

**Approach:**
- ✅ **Source of Truth:** Code annotations and DocumentTransformers
- ✅ **Generation:** Built-in .NET 10 OpenAPI at runtime
- ✅ **Aggregation:** Dynamic via OpenApiAggregationService
- ⏳ **Validation:** CI/CD pipeline checks (future)
- ⏳ **Snapshot:** Export specs to `docs/openapi/` for versioning (future)

**Benefits:**
- Code and docs stay in sync automatically
- Changes require code changes (prevents drift)
- Deployment generates latest specs

**Future Enhancements:**
- Export specs to git during CI/CD
- Compare specs for breaking changes
- Generate SDK when specs change

**Hybrid Approach (Future):**
- Generated specs for structure
- Markdown files for rich descriptions
- Merge at build time

---

### 10. Performance Considerations for Aggregation

**Answer:** **Cache specs with TTL** for production, fetch on-demand for development.

**Implementation:**

```csharp
public class OpenApiAggregationService
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    public async Task<JsonObject> GetAggregatedOpenApiAsync()
    {
        if (_cache.TryGetValue("aggregated-openapi", out JsonObject cached))
        {
            return cached;
        }

        var aggregated = await BuildAggregatedSpecAsync();

        _cache.Set("aggregated-openapi", aggregated, _cacheTtl);

        return aggregated;
    }
}
```

**Caching Strategy:**
- **Development:** 30-second TTL (fast iterations)
- **Staging:** 5-minute TTL (balance freshness/performance)
- **Production:** 15-minute TTL (performance priority)
- **Manual Refresh:** Admin endpoint to invalidate cache

**Performance Targets:**
- Aggregated spec generation: <500ms
- Cached spec retrieval: <10ms
- Service fetch timeout: 5 seconds

**Rationale:**
- Caching reduces load on backend services
- TTL ensures specs stay reasonably fresh
- Development mode supports rapid iterations
- Timeout prevents slow services blocking aggregation

---

## Implementation Roadmap

### ✅ Phase 1: Foundation (Current Sprint) - COMPLETE
- ✅ Add Scalar UI to all services
- ✅ Fix API Gateway aggregation (all 5 services)
- ✅ Add comprehensive service introductions
- ✅ Add platform-level introduction
- ✅ Document workflows in aggregated spec

### ⏳ Phase 2: Standards & Examples (Next Sprint)
- Add OpenAPI security schemes to all services
- Add examples to critical endpoints
- Implement RFC 7807 error responses
- Add cURL examples to Scalar UI
- Create error documentation

### ⏳ Phase 3: Polish & Validation (Sprint +2)
- Validate OpenAPI specs with linter
- Add comprehensive endpoint examples
- Create migration guides
- Generate SDK samples
- Create Postman collection

### 🔄 Phase 4: Ongoing Refinement
- Developer feedback integration
- Example expansion
- Tutorial creation
- SDK maintenance
- Deprecation management

---

## Success Metrics

### Documentation Quality
- ✅ All 5 services have comprehensive introductions
- ✅ Aggregated spec includes platform overview
- ✅ Workflows documented with examples
- ⏳ 80%+ endpoints have examples
- ⏳ 100% endpoints have error documentation
- ⏳ OpenAPI specs validate without errors

### Developer Experience
- ⏳ Time to first API call: <15 minutes
- ⏳ SDK generation success rate: 100%
- ⏳ Developer satisfaction score: >4.0/5.0
- ⏳ Documentation bug reports: <5/month

### Technical Compliance
- ✅ OpenAPI 3.0.1 compliance
- ✅ .NET 10 built-in OpenAPI
- ✅ Scalar UI on all services
- ⏳ RFC 7807 error responses
- ⏳ Security schemes documented
- ⏳ Deprecation policy enforced

---

## Conclusion

These decisions establish a comprehensive, enterprise-grade API documentation strategy for the Sorcha platform. The foundation has been laid with service introductions and aggregation. The next phases will add examples, error documentation, and tooling to create a world-class developer experience.

**Key Principles:**
1. **Comprehensive but maintainable** - Generate from code, enhance with descriptions
2. **Developer-first** - Clear examples, workflows, and error handling
3. **Enterprise-ready** - Security documentation, deprecation policy, SDKs
4. **Iterative** - Continuous improvement based on feedback

---

**Document Version:** 1.0
**Last Updated:** 2025-12-11
**Next Review:** After Phase 2 completion
