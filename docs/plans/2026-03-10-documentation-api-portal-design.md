# Documentation & API Portal Design

**Date:** 2026-03-10
**Status:** Approved
**Scope:** Infrastructure changes, OpenAPI enrichment, operator documentation

---

## Problem

Sorcha's APIs are functionally complete (100% MVD) but lack operator-facing documentation. The Scalar API documentation is gated behind `IsDevelopment()`, making it invisible in Docker/production. There's no way for system administrators to access the Aspire observability dashboard through the API Gateway. External developers and integration partners have no entry point to discover the API.

## Decision Summary

| Decision | Choice |
|----------|--------|
| Dashboard proxy | Config-driven (`DashboardUrl`), `RequireAdministrator` auth |
| OpenAPI route | `/openapi` (renamed from `/scalar`), aggregated view only |
| OpenAPI access | `OpenApi__RequireAuth` — defaults `true`, `.env` override to `false` |
| Doc structure | Two-tier: System Admin + parallel onboarding guides (Org / Public User) |
| Doc hosting | `docs/` in repo (no wiki), future upgrade to DocFX/static site |
| Anonymous page | Conditional links to `/openapi` and docs when auth not required |

---

## 1. Infrastructure Changes

### 1.1 API Gateway — New Proxy Routes

```
/openapi              → Aggregated Scalar UI (config-gated auth)
/admin/dashboard/**   → Aspire Dashboard (RequireAdministrator)
```

```
┌──────────┐    /openapi         ┌─────────────────┐
│  Browser  │───────────────────▶│   API Gateway    │──▶ Local Scalar UI
│           │    /admin/dashboard │   (YARP)         │──▶ Aspire Dashboard
│           │───────────────────▶│                   │    (http://aspire-dashboard:18889)
└──────────┘                     └─────────────────┘
                                        │
                              ┌─────────┴──────────┐
                              │ OpenApi__RequireAuth │
                              │ = true (default)     │
                              │ = false (.env)       │
                              └──────────────────────┘
```

**Configuration:**

| Variable | Default | Purpose |
|----------|---------|---------|
| `OpenApi__RequireAuth` | `true` | Require authenticated user to access `/openapi` |
| `Dashboard__Url` | `http://aspire-dashboard:18889` | Aspire dashboard backend URL |

**YARP Routes to Add:**

```json
{
  "openapi-scalar-route": {
    "ClusterId": "local",
    "Match": { "Path": "/openapi/{**catch-all}" },
    "AuthorizationPolicy": "conditional-openapi"
  },
  "dashboard-route": {
    "ClusterId": "aspire-dashboard-cluster",
    "Match": { "Path": "/admin/dashboard/{**catch-all}" },
    "AuthorizationPolicy": "RequireAdministrator"
  }
}
```

**Note:** The `/openapi` route serves the Scalar UI from the gateway itself (not proxied to a backend). The authorization is applied conditionally based on `OpenApi__RequireAuth`. When `false`, the route has no auth policy. When `true`, it requires `RequireAuthenticated`.

### 1.2 Blazor Anonymous Page

When `OpenApi__RequireAuth` is `false`, the login/anonymous page displays:
- **"API Documentation"** → links to `/openapi`
- **"Developer Docs"** → links to GitHub `docs/` or hosted docs URL

Implemented as a conditional section in the anonymous layout component.

### 1.3 Scalar Route Rename

- Remove existing `/scalar` route
- Map `/openapi` to serve the aggregated Scalar UI
- Update `OpenApiExtensions.cs` to use `/openapi` as the route pattern
- Remove `IsDevelopment()` gate — access control handled by config-driven auth policy instead

---

## 2. OpenAPI Endpoint Enrichment

### Current State

| Metric | Coverage |
|--------|----------|
| `.WithName()` | 100% (222/222) |
| `.WithSummary()` | 100% (222/222) |
| `.WithDescription()` | 97% (216/222) — 6 missing in EventEndpoints |
| `.Produces<T>()` | ~63% (~140/222) — ~80 missing |
| XML DTO comments | ~95% |
| Request/response examples | 0% |

### Target State

All endpoints must have:
1. `.WithSummary()` — short one-liner
2. `.WithDescription()` — detailed description including auth requirements, pagination, caching behavior
3. `.Produces<T>(statusCode)` — for all response types (success, validation, auth errors)
4. `.ProducesValidationProblem()` — for endpoints accepting request bodies
5. XML comments on all request/response DTOs

### Enrichment by Service

| Service | File | Work Required |
|---------|------|---------------|
| **Tenant** | `EventEndpoints.cs` | Add `.WithDescription()` to 6 endpoints |
| **Tenant** | `ParticipantEndpoints.cs` | Add auth context to descriptions |
| **Wallet** | `WalletEndpoints.cs` | Add `.Produces<T>()` to ~25 endpoints |
| **Validator** | `ValidatorRegistrationEndpoints.cs` | Add `.Produces<T>()` to 9 endpoints |
| **Validator** | `ValidationEndpoints.cs` | Add `.Produces<T>()` to 2 endpoints |
| **Register** | Various endpoint files | Add `.Produces<T>()` to ~10 endpoints |
| **Blueprint** | Minor touch-ups | Review and enhance descriptions |

### Description Pattern

All endpoint descriptions should follow this pattern:
```
[What it does]. [Key behavior/constraints]. [Auth requirement].
```

Example:
```csharp
.WithDescription(
    "Registers a user as a participant in the organization. " +
    "The user must be an existing member of the organization. " +
    "Requires Administrator role.")
```

---

## 3. Documentation Structure

```
docs/
├── admin/                              ← NEW
│   ├── README.md                       # System Admin Guide overview
│   ├── prerequisites-sizing.md         # Hardware, software, network
│   ├── installation-first-run.md       # Docker deploy, .env, bootstrap
│   ├── configuration-reference.md      # Complete env var reference
│   ├── scaling-high-availability.md    # Per-service scaling guidance
│   ├── monitoring-observability.md     # Dashboard, health, OTEL, logging
│   ├── administration.md              # User/org mgmt, backup, security
│   ├── troubleshooting.md             # Debug mode, diagnostics
│   └── upgrade-migration.md           # Version upgrades, DB migrations
│
├── onboarding/                         ← NEW
│   ├── organization-integration.md     # Org admin guide with examples
│   └── public-user-setup.md          # Passkey/social auth guide
│
├── getting-started/                    # Existing (keep as developer docs)
├── guides/                             # Existing
├── reference/                          # Existing
└── README.md                           # Updated with audience navigation
```

### 3.1 System Admin Guide Content

#### prerequisites-sizing.md

| Deployment | CPU | RAM | Disk | Users |
|-----------|-----|-----|------|-------|
| Small (dev/pilot) | 2 cores | 4 GB | 20 GB | <50 |
| Medium (team) | 4 cores | 8 GB | 50 GB | 50-500 |
| Large (production) | 8+ cores | 16+ GB | 100+ GB | 500+ |

Per-service resource breakdown, software prerequisites, network/port requirements.

#### installation-first-run.md

Step-by-step Docker Compose deployment:
1. Clone repository
2. Configure `.env`
3. `docker-compose up -d`
4. Bootstrap verification
5. First admin login
6. Verification checklist (health endpoints, Scalar UI, dashboard)

#### configuration-reference.md

Complete table of all environment variables grouped by concern:
- JWT & Authentication
- Database connections (PostgreSQL, MongoDB)
- Redis
- OpenTelemetry
- Service client URLs
- Feature flags (`OpenApi__RequireAuth`, etc.)

Each variable: name, default, description, which service(s) use it.

#### scaling-high-availability.md

Per-service scaling characteristics:
- Stateless services (Blueprint, Tenant, Validator) — horizontal scale freely
- Stateful considerations (Wallet — encryption keys, Register — MongoDB)
- Database replication (PostgreSQL streaming, MongoDB replica sets)
- Redis clustering
- Load balancer configuration

#### monitoring-observability.md

```
┌─────────────┐     OTLP/gRPC      ┌──────────────────┐
│  All Sorcha  │──────────────────▶│  Aspire Dashboard  │
│  Services    │                    │  (traces, logs,    │
└─────────────┘                    │   metrics)         │
                                    └────────┬───────────┘
                                             │ proxied via
                                    ┌────────▼───────────┐
                                    │  /admin/dashboard   │
                                    │  (SystemAdmin JWT)  │
                                    └────────────────────┘
```

- Accessing the dashboard via gateway
- Health check endpoints per service
- Log levels and structured logging configuration
- OTEL collector integration for external monitoring (Datadog, Grafana)

#### administration.md

- Organization lifecycle (create, configure, deactivate)
- User management (roles, deactivation)
- API documentation portal (`/openapi`) configuration
- Backup procedures (PostgreSQL pg_dump, MongoDB mongodump, Redis RDB)
- Security hardening checklist

#### troubleshooting.md

- Common issues table (symptom → cause → fix)
- Debug mode (`ASPNETCORE_ENVIRONMENT=Development`)
- Service-specific diagnostics
- Log analysis patterns

#### upgrade-migration.md

- Version upgrade process (pull images, migrate DBs, restart)
- EF Core migration commands
- MongoDB schema migration patterns
- Rollback procedures

### 3.2 Onboarding Guide Content

#### organization-integration.md

With curl and C# examples at each step:

1. **Prerequisites** — instance URL, admin credentials
2. **Create Organization** — POST with example request/response
3. **Configure Organization** — settings, policies
4. **Register Participants** — admin-created with wallet linking flow
5. **Service-to-Service Auth** — JWT client credentials with code example
6. **API Client Setup** — HttpClient and ServiceClient patterns
7. **Event Subscriptions** — SignalR connection for real-time events
8. **End-to-End Example** — Complete onboarding script

Flow diagram:
```
┌─────────┐  Create Org  ┌─────────┐  Register   ┌─────────┐
│  Admin   │────────────▶│ Tenant   │────────────▶│ Wallet   │
│  Client  │  Add Users   │ Service  │  Link Keys  │ Service  │
└─────────┘◀─────────────└─────────┘◀────────────└─────────┘
             JWT Tokens                 Verified
```

#### public-user-setup.md

1. **Authentication Options** — Passkey vs social providers
2. **Sysadmin Configuration** — Enabling providers, policies
3. **Self-Registration Flow** — with annotated diagram
4. **Wallet Creation** — automatic vs manual linking

Flow diagram:
```
┌──────────┐  Register    ┌──────────┐  Challenge  ┌──────────┐
│   User    │────────────▶│  Tenant   │───────────▶│  Wallet   │
│  Browser  │  Passkey     │  Service  │  Sign Nonce │  Service  │
└──────────┘◀─────────────└──────────┘◀────────────└──────────┘
              JWT Token                  Verified
```

### 3.3 docs/README.md Update

Audience-based navigation:

```markdown
## For System Administrators
Deploy, configure, and manage a Sorcha instance.
→ [System Admin Guide](admin/README.md)

## For Organization Administrators
Integrate your organization with a Sorcha instance.
→ [Organization Integration Guide](onboarding/organization-integration.md)

## For End Users
Set up your account using passkeys or social login.
→ [Public User Setup](onboarding/public-user-setup.md)

## For Developers
Build on the Sorcha platform.
→ [API Documentation](/openapi) | [Getting Started](getting-started/) | [Guides](guides/)
```

---

## 4. Implementation Sequence

### Phase 1: Infrastructure (Gateway + Config)
1. Add `OpenApi__RequireAuth` config support
2. Rename `/scalar` route to `/openapi`, remove `IsDevelopment()` gate
3. Add `/admin/dashboard` YARP route with `RequireAdministrator`
4. Add `Dashboard__Url` config to docker-compose
5. Update `.env` with `OpenApi__RequireAuth=false`

### Phase 2: OpenAPI Enrichment
1. Fix 6 missing descriptions in EventEndpoints
2. Add `.Produces<T>()` to Wallet Service endpoints
3. Add `.Produces<T>()` to Validator Service endpoints
4. Add `.Produces<T>()` to Register Service endpoints
5. Review and enhance Blueprint Service descriptions
6. Add XML comments to any undocumented DTOs

### Phase 3: Documentation
1. Create `docs/admin/` structure with all 8 files
2. Create `docs/onboarding/` with both guides
3. Update `docs/README.md` with audience navigation
4. Add examples, diagrams, and configuration tables

### Phase 4: UI Changes
1. Add conditional links to anonymous/login page
2. Test with `OpenApi__RequireAuth=true` and `false`

---

## 5. Out of Scope

- Blueprint authoring documentation (separate effort)
- GitHub Wiki setup (deferred — `docs/` is source of truth)
- DocFX/static site generator (future upgrade)
- Per-service Scalar routes through gateway (aggregated view only)
- OpenAPI request/response examples (future enhancement)
