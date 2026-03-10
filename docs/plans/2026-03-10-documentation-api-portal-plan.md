# Documentation & API Portal Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add config-gated OpenAPI portal, Aspire dashboard proxy, enrich all 127 endpoints with `.Produces<T>()`, fix 6 missing descriptions, and write System Admin + Onboarding documentation.

**Architecture:** API Gateway gets two new proxy routes (`/openapi`, `/admin/dashboard`) with config-driven auth. All service endpoints enriched with response type annotations. Prose documentation in `docs/admin/` and `docs/onboarding/`.

**Tech Stack:** YARP, Scalar, .NET 10 Minimal APIs, Blazor WASM

**Design Doc:** `docs/plans/2026-03-10-documentation-api-portal-design.md`

---

## Phase 1: Infrastructure — Gateway Routes & Config

### Task 1: Add OpenApi__RequireAuth config and conditional auth policy

**Files:**
- Modify: `src/Services/Sorcha.ApiGateway/Program.cs:55-59`
- Modify: `.env`
- Modify: `docker-compose.yml` (api-gateway environment section)

**Step 1: Add config-driven OpenAPI auth policy to Program.cs**

After the existing `builder.AddSorchaAuthorizationPolicies()` call (line 56), add the conditional OpenAPI policy:

```csharp
// Add conditional OpenAPI authorization policy
var requireOpenApiAuth = builder.Configuration.GetValue<bool>("OpenApi:RequireAuth", true);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OpenApiAccess", policy =>
    {
        if (requireOpenApiAuth)
            policy.RequireAuthenticatedUser();
        else
            policy.RequireAssertion(_ => true); // Allow anonymous
    });
});
```

**Step 2: Add `OPENAPI_REQUIRE_AUTH=false` to `.env`**

```
# OpenAPI Documentation Access (set true for production)
OPENAPI_REQUIRE_AUTH=false
```

**Step 3: Add env var mapping in docker-compose.yml api-gateway service**

In the api-gateway environment section, add:
```yaml
- OpenApi__RequireAuth=${OPENAPI_REQUIRE_AUTH:-true}
```

**Step 4: Run build to verify**

Run: `dotnet build src/Services/Sorcha.ApiGateway/Sorcha.ApiGateway.csproj`
Expected: Build succeeded. 0 Warning(s)

**Step 5: Commit**

```bash
git add src/Services/Sorcha.ApiGateway/Program.cs .env docker-compose.yml
git commit -m "feat: add OpenApi__RequireAuth config for conditional API docs access"
```

---

### Task 2: Rename /scalar to /openapi and remove IsDevelopment gate

**Files:**
- Modify: `src/Services/Sorcha.ApiGateway/Program.cs:516-544`
- Modify: `src/Common/Sorcha.ServiceDefaults/OpenApiExtensions.cs:65-80`

**Step 1: Update Gateway Program.cs OpenAPI section**

Replace lines 510-544 with:

```csharp
// ===========================
// OpenAPI Documentation
// ===========================

// Gateway's own OpenAPI spec
app.MapOpenApi();

// Aggregated OpenAPI from all services
app.MapGet("/openapi/aggregated.json", async (OpenApiAggregationService openApiService) =>
{
    var aggregatedSpec = await openApiService.GetAggregatedOpenApiAsync();
    return Results.Json(aggregatedSpec);
})
.WithName("AggregatedOpenApi")
.WithSummary("Get aggregated OpenAPI documentation from all backend services")
.WithTags("Documentation")
.ExcludeFromDescription();

// Scalar UI at /openapi — access controlled by OpenApi:RequireAuth config
app.MapScalarApiReference("/openapi", options =>
{
    options
        .WithTitle("Sorcha API Gateway - All Services")
        .WithTheme(ScalarTheme.Purple)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .WithOpenApiRoutePattern("/openapi/aggregated.json");
})
.RequireAuthorization("OpenApiAccess");
```

**Step 2: Update OpenApiExtensions.cs — remove IsDevelopment gate**

Replace the `MapSorchaOpenApiUi` method (lines 65-80) with:

```csharp
/// <summary>
/// Maps the OpenAPI endpoint and Scalar interactive API documentation UI.
/// Access control is handled by the API Gateway's OpenApi:RequireAuth configuration.
/// Individual services expose their own Scalar UI for development convenience.
/// </summary>
/// <param name="app">The web application.</param>
/// <param name="title">The title displayed in the Scalar UI.</param>
/// <param name="theme">The Scalar UI theme. Defaults to <see cref="ScalarTheme.Purple"/>.</param>
/// <returns>The web application for chaining.</returns>
public static WebApplication MapSorchaOpenApiUi(this WebApplication app, string title, ScalarTheme theme = ScalarTheme.Purple)
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle(title)
            .WithTheme(theme)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });

    return app;
}
```

**Step 3: Run build**

Run: `dotnet build Sorcha.sln`
Expected: Build succeeded. 0 Warning(s)

**Step 4: Commit**

```bash
git add src/Services/Sorcha.ApiGateway/Program.cs src/Common/Sorcha.ServiceDefaults/OpenApiExtensions.cs
git commit -m "feat: rename /scalar to /openapi, remove IsDevelopment gate for Scalar UI"
```

---

### Task 3: Add Aspire Dashboard proxy route

**Files:**
- Modify: `src/Services/Sorcha.ApiGateway/appsettings.json:1343-1395`
- Modify: `docker-compose.yml` (api-gateway environment section)

**Step 1: Add dashboard cluster to YARP Clusters section**

In `appsettings.json`, add a new cluster after the existing `validator-cluster` (inside the `Clusters` object):

```json
"aspire-dashboard-cluster": {
    "Destinations": {
        "destination1": {
            "Address": "http://aspire-dashboard:18888"
        }
    }
}
```

**Step 2: Add dashboard route to YARP Routes section**

Add a new route in the `Routes` object:

```json
"dashboard-route": {
    "ClusterId": "aspire-dashboard-cluster",
    "AuthorizationPolicy": "RequireAdministrator",
    "Match": {
        "Path": "/admin/dashboard/{**catch-all}"
    },
    "Transforms": [
        { "PathRemovePrefix": "/admin/dashboard" }
    ]
}
```

**Step 3: Add Dashboard__Url to docker-compose.yml**

In the api-gateway environment section, add:
```yaml
- Dashboard__Url=${DASHBOARD_URL:-http://aspire-dashboard:18888}
```

**Step 4: Run build**

Run: `dotnet build src/Services/Sorcha.ApiGateway/Sorcha.ApiGateway.csproj`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add src/Services/Sorcha.ApiGateway/appsettings.json docker-compose.yml
git commit -m "feat: add /admin/dashboard proxy route to Aspire dashboard with RequireAdministrator auth"
```

---

## Phase 2: OpenAPI Endpoint Enrichment

> **Parallelizable:** Tasks 4-12 can be dispatched to parallel subagents as they modify different files in different services.

### Task 4: Enrich Tenant EventEndpoints — add descriptions and .Produces

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/EventEndpoints.cs:22-44`

**Step 1: Add `.WithDescription()` and `.Produces<T>()` to all 6 endpoints**

Replace lines 22-44 with:

```csharp
group.MapGet("/", GetEvents)
    .WithName("GetEvents")
    .WithSummary("Get activity events for the authenticated user")
    .WithDescription(
        "Returns paginated activity events for the authenticated user. " +
        "Supports filtering by severity (Info, Warning, Error, Critical), unread-only flag, " +
        "and events since a specific timestamp. Default page size is 50.")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

group.MapGet("/unread-count", GetUnreadCount)
    .WithName("GetUnreadCount")
    .WithSummary("Get unread event count")
    .WithDescription(
        "Returns the count of unread activity events for the authenticated user. " +
        "Useful for badge/notification indicators.")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

group.MapPost("/mark-read", MarkRead)
    .WithName("MarkEventsRead")
    .WithSummary("Mark events as read")
    .WithDescription(
        "Marks activity events as read. If EventIds array is provided, marks only those events. " +
        "If EventIds is null or empty, marks all events as read for the authenticated user.")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

group.MapPost("/", CreateEvent)
    .WithName("CreateEvent")
    .WithSummary("Create an activity event (service-to-service)")
    .WithDescription(
        "Creates a new activity event targeted at a specific user. " +
        "Intended for service-to-service calls to notify users of system events. " +
        "Requires service token authentication.")
    .Produces<object>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .Produces(StatusCodes.Status401Unauthorized);

group.MapGet("/admin", GetAdminEvents)
    .WithName("GetAdminEvents")
    .WithSummary("Get events for all users in organisation (admin only)")
    .WithDescription(
        "Returns paginated activity events for all users in the authenticated admin's organisation. " +
        "Supports filtering by user ID, severity, and timestamp. Requires Administrator or SystemAdmin role.")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status403Forbidden);

group.MapDelete("/{id:guid}", DeleteEvent)
    .WithName("DeleteEvent")
    .WithSummary("Delete a specific event")
    .WithDescription(
        "Deletes an activity event by ID. Only the event owner can delete their own events. " +
        "Returns 404 if the event does not exist or belongs to another user.")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status401Unauthorized)
    .Produces(StatusCodes.Status404NotFound);
```

**Step 2: Run build**

Run: `dotnet build src/Services/Sorcha.Tenant.Service/Sorcha.Tenant.Service.csproj`
Expected: Build succeeded

**Step 3: Run tests**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests/`
Expected: All tests pass

**Step 4: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/Endpoints/EventEndpoints.cs
git commit -m "docs: add descriptions and .Produces to Tenant EventEndpoints (6 endpoints)"
```

---

### Task 5: Enrich Wallet WalletEndpoints — add .Produces to 20 endpoints

**Files:**
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs`

**Step 1: Read the file and identify all endpoint definitions**

Read `WalletEndpoints.cs` to understand the existing endpoint patterns and return types.

**Step 2: Add `.Produces<T>()` annotations to each endpoint**

For each `MapGet/MapPost/MapPut/MapDelete` call, add the appropriate response type annotations based on what the handler method returns. Follow this pattern:

- Endpoints returning data: `.Produces<ResponseType>(StatusCodes.Status200OK)`
- Creation endpoints: `.Produces<ResponseType>(StatusCodes.Status201Created)`
- Endpoints accepting body: `.ProducesValidationProblem()`
- All authenticated endpoints: `.Produces(StatusCodes.Status401Unauthorized)`
- Endpoints with authorization checks: `.Produces(StatusCodes.Status403Forbidden)`
- Endpoints with lookups: `.Produces(StatusCodes.Status404NotFound)`

**Step 3: Run build and tests**

Run: `dotnet build src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj && dotnet test tests/Sorcha.Wallet.Service.Tests/`
Expected: Build succeeded, all tests pass

**Step 4: Commit**

```bash
git add src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs
git commit -m "docs: add .Produces to Wallet WalletEndpoints (20 endpoints)"
```

---

### Task 6: Enrich Wallet CredentialEndpoints — add .Produces to 8 endpoints

**Files:**
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs`

Follow the same pattern as Task 5. Read the file, identify return types, add `.Produces<T>()` annotations.

**Commit:**
```bash
git add src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs
git commit -m "docs: add .Produces to Wallet CredentialEndpoints (8 endpoints)"
```

---

### Task 7: Enrich Wallet PresentationEndpoints + DelegationEndpoints — add .Produces

**Files:**
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/PresentationEndpoints.cs`
- Modify: `src/Services/Sorcha.Wallet.Service/Endpoints/DelegationEndpoints.cs`

Follow the same pattern. PresentationEndpoints has 5 endpoints, DelegationEndpoints has 4.

**Commit:**
```bash
git add src/Services/Sorcha.Wallet.Service/Endpoints/PresentationEndpoints.cs src/Services/Sorcha.Wallet.Service/Endpoints/DelegationEndpoints.cs
git commit -m "docs: add .Produces to Wallet Presentation + Delegation endpoints (9 endpoints)"
```

---

### Task 8: Enrich Validator endpoints — add .Produces to remaining 8 endpoints

**Files:**
- Modify: `src/Services/Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs`
- Modify: `src/Services/Sorcha.Validator.Service/Endpoints/ValidationEndpoints.cs`
- Modify: `src/Services/Sorcha.Validator.Service/Endpoints/ThresholdEndpoints.cs`

Note: `MetricsEndpoints.cs` and `AdminEndpoints.cs` already have `.Produces<T>()` — skip those.

**Commit:**
```bash
git add src/Services/Sorcha.Validator.Service/Endpoints/
git commit -m "docs: add .Produces to Validator Registration, Validation, Threshold endpoints (13 endpoints)"
```

---

### Task 9: Enrich Register Service endpoint files — add .Produces

**Files:**
- Modify: `src/Services/Sorcha.Register.Service/Endpoints/SystemRegisterEndpoints.cs`
- Modify: `src/Services/Sorcha.Register.Service/Endpoints/RegisterPolicyEndpoints.cs`
- Modify: `src/Services/Sorcha.Register.Service/Endpoints/RecoveryHealthEndpoints.cs`

Add `.Produces<T>()` to all 10 endpoints across these 3 files.

**Commit:**
```bash
git add src/Services/Sorcha.Register.Service/Endpoints/
git commit -m "docs: add .Produces to Register endpoint files (10 endpoints)"
```

---

### Task 10: Enrich Register Service Program.cs — add .Produces to register/transaction endpoints

**Files:**
- Modify: `src/Services/Sorcha.Register.Service/Program.cs`

This is the largest single file with 38 endpoints defined inline. Work through each endpoint group:

1. **Register CRUD** (lines ~276-400): `GET /`, `GET /{id}`, `PUT /{id}`, `DELETE /{id}`, `GET /stats/count`
2. **Register creation** (lines ~408-548): `POST /initiate`, `POST /finalize`
3. **Transactions** (lines ~549-750): `POST /`, `GET /{txId}`, `GET /`
4. **Queries** (lines ~648-770): wallet/sender/blueprint transaction lookups, stats
5. **Dockets** (lines ~771-940): docket CRUD, blueprint publish
6. **Governance** (lines ~1083-1530): roster, history, proposals, crypto-policy
7. **Participants** (lines ~1531-1700): published participant queries
8. **Proofs** (lines ~1697-1860): inclusion proofs
9. **Admin** (lines ~1803-1970): orphan transactions, rebuild index

For each endpoint, add `.Produces<T>()` matching the handler's return types.

**Note:** This task is large. Consider splitting into sub-tasks by endpoint group if needed.

**Commit:**
```bash
git add src/Services/Sorcha.Register.Service/Program.cs
git commit -m "docs: add .Produces to Register Service Program.cs (38 endpoints)"
```

---

### Task 11: Enrich Blueprint Service endpoints — add .Produces

**Files:**
- Modify: `src/Services/Sorcha.Blueprint.Service/Endpoints/SchemaEndpoints.cs` (11 endpoints)
- Modify: `src/Services/Sorcha.Blueprint.Service/Endpoints/SchemaLibraryEndpoints.cs` (8 endpoints)
- Modify: `src/Services/Sorcha.Blueprint.Service/Endpoints/CredentialEndpoints.cs` (4 endpoints)
- Modify: `src/Services/Sorcha.Blueprint.Service/Endpoints/OperationsEndpoints.cs` (2 endpoints)
- Modify: `src/Services/Sorcha.Blueprint.Service/Endpoints/StatusListEndpoints.cs` (3 endpoints)

Add `.Produces<T>()` to all 28 endpoints.

**Commit:**
```bash
git add src/Services/Sorcha.Blueprint.Service/Endpoints/
git commit -m "docs: add .Produces to Blueprint Service endpoints (28 endpoints)"
```

---

### Task 12: Enrich Tenant Participant + Auth endpoints — verify and add missing .Produces

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/ParticipantEndpoints.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/InvitationEndpoints.cs`

These endpoints may already have `.Produces<T>()` (the earlier audit suggested ParticipantEndpoints does). Read each file, verify coverage, and fill any gaps.

**Commit:**
```bash
git add src/Services/Sorcha.Tenant.Service/Endpoints/
git commit -m "docs: verify and complete .Produces on Tenant Participant, Auth, Invitation endpoints"
```

---

## Phase 3: Documentation

### Task 13: Create docs/admin/ System Admin Guide

**Files:**
- Create: `docs/admin/README.md`
- Create: `docs/admin/prerequisites-sizing.md`
- Create: `docs/admin/installation-first-run.md`
- Create: `docs/admin/configuration-reference.md`
- Create: `docs/admin/scaling-high-availability.md`
- Create: `docs/admin/monitoring-observability.md`
- Create: `docs/admin/administration.md`
- Create: `docs/admin/troubleshooting.md`
- Create: `docs/admin/upgrade-migration.md`

**Step 1: Create `docs/admin/README.md`**

System Admin Guide overview with navigation to all sub-pages. Include audience description and prerequisites summary.

**Step 2: Create `docs/admin/prerequisites-sizing.md`**

Include:
- Hardware sizing table (Small/Medium/Large with CPU, RAM, Disk, User counts)
- Per-service resource breakdown
- Software prerequisites (.NET 10, Docker Desktop, PostgreSQL 17, MongoDB 8, Redis 8)
- Network requirements diagram (ports, firewall rules, DNS)
- TLS certificate requirements

**Step 3: Create `docs/admin/installation-first-run.md`**

Include:
- Step-by-step Docker Compose deployment with commands
- `.env` file configuration walkthrough
- Bootstrap process (first admin account creation)
- Verification checklist (health endpoints, Scalar UI, dashboard access)
- Aspire AppHost alternative for development

**Step 4: Create `docs/admin/configuration-reference.md`**

Complete table of ALL environment variables from docker-compose.yml:
- Grouped by concern (JWT, Database, Redis, OTEL, Feature Flags, Service URLs)
- Each variable: name, default value, description, which service(s) use it
- Cross-reference to relevant guide sections

Read `docker-compose.yml` fully to extract every environment variable.

**Step 5: Create `docs/admin/scaling-high-availability.md`**

Include:
- Per-service scaling characteristics (stateless vs stateful)
- Horizontal scaling diagram
- PostgreSQL streaming replication setup
- MongoDB replica set configuration
- Redis clustering
- Load balancer configuration patterns

**Step 6: Create `docs/admin/monitoring-observability.md`**

Include:
- Aspire dashboard access via `/admin/dashboard` (with auth)
- OTLP architecture diagram (services → dashboard)
- Health check endpoint table (per-service URLs)
- Log level configuration
- Structured logging with Serilog
- External OTEL collector integration (Datadog, Grafana)

**Step 7: Create `docs/admin/administration.md`**

Include:
- Organization lifecycle management (create, configure, deactivate)
- User management (roles, activation/deactivation)
- API documentation portal configuration (`OpenApi__RequireAuth`)
- Backup procedures (pg_dump, mongodump, Redis RDB)
- Restore procedures
- Security hardening checklist

**Step 8: Create `docs/admin/troubleshooting.md`**

Include:
- Common issues table (symptom → cause → fix)
- Debug mode configuration (`ASPNETCORE_ENVIRONMENT=Development`)
- Service-specific diagnostic commands
- Log analysis patterns with examples
- Docker troubleshooting (container logs, restart policies)

**Step 9: Create `docs/admin/upgrade-migration.md`**

Include:
- Version upgrade process (pull images, migrate DBs, restart)
- EF Core migration commands
- MongoDB schema migration notes
- Rollback procedures
- Breaking changes checklist

**Step 10: Commit**

```bash
git add docs/admin/
git commit -m "docs: add System Administrator Guide (8 files)"
```

---

### Task 14: Create docs/onboarding/ guides

**Files:**
- Create: `docs/onboarding/organization-integration.md`
- Create: `docs/onboarding/public-user-setup.md`

**Step 1: Create `docs/onboarding/organization-integration.md`**

Include with curl AND C# code examples at each step:
1. Prerequisites (instance URL, admin credentials)
2. Create Organization — POST example with request/response JSON
3. Configure Organization settings
4. Add Users to Organization
5. Register Participants (admin-created) — with flow diagram
6. Wallet Linking (challenge/verify flow) — with sequence diagram
7. Publishing Participants to Registers
8. Service-to-Service Auth — JWT client credentials with C# code
9. API Client Setup — HttpClient and ServiceClient patterns
10. Event Subscriptions — SignalR connection example
11. End-to-end onboarding script example

Reference actual API endpoints from CLAUDE.md Participant Identity API section.

**Step 2: Create `docs/onboarding/public-user-setup.md`**

Include:
1. Authentication Options overview — Passkey vs Social providers
2. Sysadmin Configuration — enabling/disabling providers, policies
3. Registration flow diagram (annotated)
4. Self-Registration — POST /me/register-participant with example
5. Wallet Creation — automatic vs manual linking
6. Login flow — passkey authentication sequence
7. Troubleshooting common user issues

**Step 3: Commit**

```bash
git add docs/onboarding/
git commit -m "docs: add Organization Integration and Public User Setup onboarding guides"
```

---

### Task 15: Update docs/README.md with audience-based navigation

**Files:**
- Modify: `docs/README.md`

**Step 1: Read current docs/README.md**

**Step 2: Add audience-based navigation section at the top**

```markdown
## Documentation by Audience

### System Administrators
Deploy, configure, scale, and manage a Sorcha instance.
- [System Admin Guide](admin/README.md) — Complete operations manual

### Organization Administrators
Integrate your organization with a running Sorcha instance.
- [Organization Integration Guide](onboarding/organization-integration.md)

### End Users
Set up your account using passkeys or social login.
- [Public User Setup Guide](onboarding/public-user-setup.md)

### Developers
Build on the Sorcha platform.
- [API Documentation](/openapi) — Interactive Scalar API explorer
- [Getting Started](getting-started/) — Development environment setup
- [Guides](guides/) — Feature-specific integration guides
- [Reference](reference/) — Architecture, status, and specifications
```

**Step 3: Commit**

```bash
git add docs/README.md
git commit -m "docs: add audience-based navigation to docs README"
```

---

## Phase 4: UI Changes

### Task 16: Add conditional API docs links to login page

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Login.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/AuthLayout.razor`

**Step 1: Read the full Login.razor file**

Understand the current layout and where to add the links section.

**Step 2: Add developer links section to AuthLayout.razor**

Below the `@Body` in the auth-container, add a conditional footer:

```razor
@inherits LayoutComponentBase
@inject IConfiguration Configuration

<div class="auth-layout">
    <div class="auth-container">
        @Body
    </div>
    @if (!RequireOpenApiAuth)
    {
        <div class="auth-footer">
            <a href="/openapi" target="_blank">API Documentation</a>
            <span class="separator">|</span>
            <a href="https://github.com/sorcha-platform/sorcha/tree/master/docs" target="_blank">Developer Docs</a>
        </div>
    }
</div>

@code {
    private bool RequireOpenApiAuth =>
        Configuration.GetValue<bool>("OpenApi:RequireAuth", true);
}
```

Add CSS for the footer:

```css
.auth-footer {
    text-align: center;
    margin-top: 1.5rem;
    font-size: 0.875rem;
}

.auth-footer a {
    color: #6366f1;
    text-decoration: none;
}

.auth-footer a:hover {
    text-decoration: underline;
}

.auth-footer .separator {
    margin: 0 0.5rem;
    color: #94a3b8;
}
```

**Step 3: Run build**

Run: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Sorcha.UI.Web.Client.csproj`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/AuthLayout.razor
git commit -m "feat: add conditional API docs links to login page when OpenApi auth is disabled"
```

---

## Phase 5: Final Verification & Cleanup

### Task 17: Build, test, and verify all changes

**Step 1: Full solution build**

Run: `dotnet build Sorcha.sln`
Expected: Build succeeded. 0 Warning(s)

**Step 2: Run all tests**

Run: `dotnet test Sorcha.sln --filter "Category!=Integration"`
Expected: All tests pass

**Step 3: Docker build verification**

Run: `docker-compose build --no-cache api-gateway`
Expected: Build succeeds

**Step 4: Verify OpenAPI aggregation works**

Start services and check:
- `http://localhost/openapi` loads Scalar UI
- `http://localhost/openapi/aggregated.json` returns valid JSON
- All services show response schemas in the aggregated spec

**Step 5: Verify dashboard proxy**

With a SystemAdmin JWT:
- `http://localhost/admin/dashboard` proxies to Aspire dashboard
- Without auth: returns 401

---

### Task 18: Update MASTER-TASKS.md and CLAUDE.md

**Files:**
- Modify: `.specify/MASTER-TASKS.md`
- Modify: `CLAUDE.md` (if route patterns changed)

**Step 1: Mark completed tasks in MASTER-TASKS.md**

Update relevant tasks to ✅ status. Add any new tasks discovered during implementation.

**Step 2: Update CLAUDE.md if needed**

If the `/openapi` route or dashboard proxy represent a convention change, update the Quick Start or Architecture sections.

**Step 3: Commit**

```bash
git add .specify/MASTER-TASKS.md CLAUDE.md
git commit -m "docs: update MASTER-TASKS and CLAUDE.md for documentation portal work"
```

---

## Task Dependency Map

```
Phase 1 (sequential):
  Task 1 → Task 2 → Task 3

Phase 2 (parallel — all independent):
  Task 4  (Tenant Events)        ─┐
  Task 5  (Wallet Main)          ─┤
  Task 6  (Wallet Credentials)   ─┤
  Task 7  (Wallet Pres+Deleg)    ─┤
  Task 8  (Validator)            ─┼─→ All merge
  Task 9  (Register Endpoints)   ─┤
  Task 10 (Register Program.cs)  ─┤
  Task 11 (Blueprint)            ─┤
  Task 12 (Tenant verify)        ─┘

Phase 3 (parallel with Phase 2):
  Task 13 (Admin Guide)          ─┐
  Task 14 (Onboarding Guides)    ─┼─→ Both merge
  Task 15 (docs README)          ─┘

Phase 4 (after Phase 1):
  Task 16 (UI links)

Phase 5 (after all):
  Task 17 → Task 18
```

## Endpoint Enrichment Summary

| Task | Service | File(s) | Endpoints | Type |
|------|---------|---------|-----------|------|
| 4 | Tenant | EventEndpoints.cs | 6 | Description + Produces |
| 5 | Wallet | WalletEndpoints.cs | 20 | Produces |
| 6 | Wallet | CredentialEndpoints.cs | 8 | Produces |
| 7 | Wallet | Presentation + Delegation | 9 | Produces |
| 8 | Validator | Registration + Validation + Threshold | 13 | Produces |
| 9 | Register | 3 endpoint files | 10 | Produces |
| 10 | Register | Program.cs | 38 | Produces |
| 11 | Blueprint | 5 endpoint files | 28 | Produces |
| 12 | Tenant | Participant + Auth + Invitation | ~34 | Verify + fill gaps |
| **Total** | | | **~166** | |
