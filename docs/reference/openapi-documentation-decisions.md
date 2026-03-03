# OpenAPI Documentation - Decisions, Standards & Roadmap

**Date:** 2025-12-11 | **Consolidated:** 2026-03-01
**Status:** Phase 1 Complete, Phase 2 Pending
**Purpose:** Unified OpenAPI documentation strategy for the Sorcha platform

---

## Current State

All services use .NET 10 built-in OpenAPI with Scalar UI (Purple theme, C# examples). The API Gateway aggregates specs from all 5 services.

| Service | OpenAPI | Scalar UI | Aggregated |
|---------|---------|-----------|------------|
| Tenant Service | Yes | Yes | Yes |
| Wallet Service | Yes | Yes | Yes |
| Register Service | Yes | Yes | Yes |
| Peer Service | Yes | Yes | Yes |
| Blueprint Service | Yes | Yes | Yes |

**Phase 1 (Complete):** Service introductions, aggregation, platform overview, workflow docs.

---

## Key Decisions

### Authentication
- **Scheme:** JWT Bearer tokens via Tenant Service
- **Token Endpoint:** `POST /api/tenant/api/service-auth/token`
- **OpenAPI:** `securitySchemes.BearerAuth` (type: http, scheme: bearer)

### Error Responses
- **Format:** RFC 7807 Problem Details (.NET 10 built-in `ProblemDetails`)
- **Status codes:** 400 validation, 401 auth, 403 authz, 404 not found, 409 conflict, 500 internal

### Versioning
- **v1.0:** No URL versioning (`/api/wallets`)
- **Future:** URL versioning (`/api/v2/wallets`) for breaking changes only
- **Policy:** Additive changes = no version bump; breaking changes = major version

### Examples
- **Critical endpoints:** Comprehensive (success + error examples)
- **CRUD endpoints:** Basic (success only)
- **Languages:** C# (done), cURL (next), TypeScript/Python (future)

### Deprecation
- **Minimum notice:** 6 months
- **Breaking changes:** Only in major versions
- **Process:** Mark deprecated -> communicate -> monitor -> remove in next major version

### Aggregated Spec Structure
- Platform overview, architecture, getting started guide, common workflows
- Tags organized by service (e.g., "Tenant Service/Organizations")
- Implemented in `OpenApiAggregationService.cs`

### Audience
- Internal developers (40%), external integrators (50%), public API consumers (10%)

---

## Roadmap

### Phase 2: Standards & Examples (Post-MVD, ~5 days)

| Task | Priority | Effort | Description |
|------|----------|--------|-------------|
| OA-2.1 | P0 | 1 day | Add OpenAPI security schemes to all services |
| OA-2.2 | P1 | 2 days | Add examples to 5 critical endpoints |
| OA-2.3 | P1 | 1 day | Implement RFC 7807 error responses across endpoints |
| OA-2.4 | P2 | 0.5 day | Add cURL examples to Scalar UI |
| OA-2.5 | P2 | 1 day | Create error documentation (`docs/api-error-reference.md`) |

### Phase 3: Polish & Validation (~5 days)

| Task | Priority | Effort | Description |
|------|----------|--------|-------------|
| OA-3.1 | P1 | 0.5 day | CI/CD OpenAPI spec validation (Spectral/Redocly) |
| OA-3.2 | P2 | 2 days | Comprehensive examples on 80%+ endpoints |
| OA-3.3 | P2 | 1 day | Migration guide template |
| OA-3.4 | P2 | 1 day | SDK generation scripts (C#, TypeScript, Python) |
| OA-3.5 | P2 | 0.5 day | Postman collection export |

### Phase 4: Ongoing

- Developer feedback integration (quarterly surveys)
- Tutorial creation (5 planned: getting started, timestamping, multi-party, OData, security)
- SDK maintenance and publishing (NuGet, npm, PyPI)

---

## Implementation Notes

### Security Scheme (Phase 2)
```csharp
// Add to each service's OpenAPI document transformer
document.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
{
    Type = SecuritySchemeType.Http,
    Scheme = "bearer",
    BearerFormat = "JWT",
    Description = "JWT Bearer token obtained from Tenant Service"
};
```

### cURL in Scalar (Phase 2)
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

### Example Data Standards
- **Organizations:** "Acme Corporation", "Globex Industries"
- **Wallet IDs:** `wallet-abc123`
- **Register IDs:** `my-register-001`
- **Timestamps:** ISO 8601, recent dates
- **Payloads:** Base64-encoded realistic data

---

**Last Updated:** 2026-03-01
**Next Review:** After Phase 2 completion
