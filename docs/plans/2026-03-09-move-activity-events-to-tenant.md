# Move Activity Events from Blueprint to Tenant Service

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove SQLite from Blueprint Service by moving the ActivityEvent system to Tenant Service where it belongs (user/org-scoped data alongside identity).

**Architecture:** ActivityEvent model + IEventService + EventEndpoints + EventCleanupService move from Blueprint Service to Tenant Service. The ActivityEvents table is added to TenantDbContext and integrated into the existing InitialCreate migration. Blueprint Service's EncryptionBackgroundService switches to HTTP calls via a new IEventServiceClient in Sorcha.ServiceClients. API Gateway routes for `/api/events` re-point from blueprint-cluster to tenant-cluster.

**Tech Stack:** EF Core (PostgreSQL), Minimal APIs, xUnit + FluentAssertions, Sorcha.ServiceClients HTTP client pattern

---

## Task 1: Add ActivityEvent Model to Tenant.Models

**Files:**
- Create: `src/Common/Sorcha.Tenant.Models/ActivityEvent.cs`

**Step 1: Create the ActivityEvent model and EventSeverity enum in Tenant.Models**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Represents a user or system event captured for the activity log.
/// Stored in PostgreSQL via TenantDbContext.
/// </summary>
public class ActivityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public required string EventType { get; set; }
    public EventSeverity Severity { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public required string SourceService { get; set; }
    public string? EntityId { get; set; }
    public string? EntityType { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Severity level for activity events.
/// </summary>
public enum EventSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}
```

Note: Check if Tenant.Models has its own project or if models live inside the Tenant.Service project. Current models are in `src/Services/Sorcha.Tenant.Service/Models/`. Place the file there.

**Step 2: Commit**
```bash
git add src/Services/Sorcha.Tenant.Service/Models/ActivityEvent.cs
git commit -m "feat: add ActivityEvent model to Tenant Service"
```

---

## Task 2: Add ActivityEvents to TenantDbContext

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs`

**Step 1: Add DbSet and configuration to TenantDbContext**

Add to the DbSet declarations (around line 53, after PushSubscriptions):
```csharp
// Public schema entities for activity event log
public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
```

Add a call in `OnModelCreating` (around line 118, after ConfigureCustomDomainMapping):
```csharp
// Configure ActivityEvent entity (public schema)
ConfigureActivityEvent(modelBuilder);
```

Add the configuration method (after `ConfigureCustomDomainMapping` method):
```csharp
private void ConfigureActivityEvent(ModelBuilder modelBuilder)
{
    var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";

    modelBuilder.Entity<ActivityEvent>(entity =>
    {
        if (isInMemory)
            entity.ToTable("ActivityEvents");
        else
            entity.ToTable("ActivityEvents", "public");

        entity.HasKey(e => e.Id);

        entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        entity.Property(e => e.Severity).IsRequired()
            .HasConversion<string>().HasMaxLength(20);
        entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
        entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
        entity.Property(e => e.SourceService).IsRequired().HasMaxLength(50);
        entity.Property(e => e.EntityId).HasMaxLength(200);
        entity.Property(e => e.EntityType).HasMaxLength(50);

        entity.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("IX_ActivityEvent_UserId_CreatedAt")
            .IsDescending(false, true);
        entity.HasIndex(e => new { e.OrganizationId, e.CreatedAt })
            .HasDatabaseName("IX_ActivityEvent_OrgId_CreatedAt")
            .IsDescending(false, true);
        entity.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("IX_ActivityEvent_ExpiresAt");

        if (!isInMemory)
        {
            entity.HasIndex(e => new { e.UserId, e.IsRead })
                .HasDatabaseName("IX_ActivityEvent_UserId_IsRead")
                .HasFilter("\"IsRead\" = false");
        }
        else
        {
            entity.HasIndex(e => new { e.UserId, e.IsRead })
                .HasDatabaseName("IX_ActivityEvent_UserId_IsRead");
        }
    });
}
```

**Step 2: Verify build**
```bash
dotnet build src/Services/Sorcha.Tenant.Service/
```

**Step 3: Commit**
```bash
git add src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs
git commit -m "feat: add ActivityEvents table to TenantDbContext"
```

---

## Task 3: Integrate ActivityEvents into Existing InitialCreate Migration

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Migrations/20260309100751_InitialCreate.cs`
- Regenerate: `src/Services/Sorcha.Tenant.Service/Migrations/20260309100751_InitialCreate.Designer.cs`
- Regenerate: `src/Services/Sorcha.Tenant.Service/Migrations/TenantDbContextModelSnapshot.cs`

**Step 1: Add CreateTable for ActivityEvents in InitialCreate.Up()**

Add after the existing CreateTable calls (but before the CreateIndex calls), inside the `Up()` method:
```csharp
migrationBuilder.CreateTable(
    name: "ActivityEvents",
    schema: "public",
    columns: table => new
    {
        Id = table.Column<Guid>(type: "uuid", nullable: false),
        OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
        UserId = table.Column<Guid>(type: "uuid", nullable: false),
        EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
        Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
        Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
        Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
        SourceService = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
        EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
        EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
        IsRead = table.Column<bool>(type: "boolean", nullable: false),
        CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_ActivityEvents", x => x.Id);
    });

migrationBuilder.CreateIndex(
    name: "IX_ActivityEvent_UserId_CreatedAt",
    schema: "public",
    table: "ActivityEvents",
    columns: new[] { "UserId", "CreatedAt" },
    descending: new[] { false, true });

migrationBuilder.CreateIndex(
    name: "IX_ActivityEvent_OrgId_CreatedAt",
    schema: "public",
    table: "ActivityEvents",
    columns: new[] { "OrganizationId", "CreatedAt" },
    descending: new[] { false, true });

migrationBuilder.CreateIndex(
    name: "IX_ActivityEvent_ExpiresAt",
    schema: "public",
    table: "ActivityEvents",
    column: "ExpiresAt");

migrationBuilder.CreateIndex(
    name: "IX_ActivityEvent_UserId_IsRead",
    schema: "public",
    table: "ActivityEvents",
    columns: new[] { "UserId", "IsRead" },
    filter: "\"IsRead\" = false");
```

Add DropTable in `Down()`:
```csharp
migrationBuilder.DropTable(
    name: "ActivityEvents",
    schema: "public");
```

**Step 2: Regenerate Designer and Snapshot files**

Delete and regenerate the Designer.cs and snapshot to match:
```bash
cd src/Services/Sorcha.Tenant.Service
dotnet ef migrations remove --force
dotnet ef migrations add InitialCreate
```

Wait — this approach is fragile. Instead, since we're pre-production with a single InitialCreate, the safest approach is:

```bash
cd src/Services/Sorcha.Tenant.Service
# Remove the existing migration entirely
dotnet ef migrations remove --force
# Re-add it (EF will scaffold from the updated TenantDbContext)
dotnet ef migrations add InitialCreate
```

This regenerates all three migration files from scratch based on the current DbContext, which now includes ActivityEvents.

**Step 3: Verify the regenerated migration includes ActivityEvents**
```bash
grep -c "ActivityEvents" src/Services/Sorcha.Tenant.Service/Migrations/20*_InitialCreate.cs
```
Expected: Multiple matches showing CreateTable and CreateIndex for ActivityEvents.

**Step 4: Commit**
```bash
git add src/Services/Sorcha.Tenant.Service/Migrations/
git commit -m "feat: regenerate InitialCreate migration with ActivityEvents table"
```

---

## Task 4: Move IEventService Interface and EventService to Tenant Service

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Services/Interfaces/IEventService.cs`
- Create: `src/Services/Sorcha.Tenant.Service/Services/EventService.cs`

**Step 1: Create IEventService in Tenant Service**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services.Interfaces;

/// <summary>
/// Service for managing activity events (activity log).
/// </summary>
public interface IEventService
{
    Task<(IReadOnlyList<ActivityEvent> Items, int TotalCount)> GetEventsAsync(
        Guid userId, int page, int pageSize, bool unreadOnly = false,
        EventSeverity? severity = null, DateTime? since = null,
        CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default);

    Task<int> MarkReadAsync(Guid userId, Guid[]? eventIds = null, CancellationToken ct = default);

    Task<ActivityEvent> CreateEventAsync(ActivityEvent activityEvent, CancellationToken ct = default);

    Task<(IReadOnlyList<ActivityEvent> Items, int TotalCount)> GetAdminEventsAsync(
        Guid organizationId, int page, int pageSize, Guid? userId = null,
        EventSeverity? severity = null, DateTime? since = null,
        CancellationToken ct = default);

    Task<bool> DeleteEventAsync(Guid eventId, Guid userId, CancellationToken ct = default);
}
```

Note: Tenant Service already has `src/Services/Sorcha.Tenant.Service/Services/Interfaces/` — check if this directory exists. If not, some interfaces may be inline. Adapt accordingly.

**Step 2: Create EventService in Tenant Service**

Copy from `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventService.cs`, updating namespaces:
- `Sorcha.Blueprint.Service.Data` → `Sorcha.Tenant.Service.Data`
- `Sorcha.Blueprint.Service.Models` → `Sorcha.Tenant.Service.Models`
- `Sorcha.Blueprint.Service.Services.Interfaces` → `Sorcha.Tenant.Service.Services.Interfaces`
- `Sorcha.Blueprint.Service.Services.Implementation` → `Sorcha.Tenant.Service.Services`
- `BlueprintEventsDbContext` → `TenantDbContext`

The key change: replace `_db` (BlueprintEventsDbContext) with TenantDbContext. The LINQ queries remain identical since we're using the same `DbSet<ActivityEvent>`.

**Step 3: Verify build**
```bash
dotnet build src/Services/Sorcha.Tenant.Service/
```

**Step 4: Commit**
```bash
git add src/Services/Sorcha.Tenant.Service/Services/
git commit -m "feat: add IEventService and EventService to Tenant Service"
```

---

## Task 5: Move EventEndpoints to Tenant Service

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Endpoints/EventEndpoints.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Program.cs` (add `app.MapEventEndpoints()`)

**Step 1: Create EventEndpoints in Tenant Service**

Copy from `src/Services/Sorcha.Blueprint.Service/Endpoints/EventEndpoints.cs`, updating namespace to `Sorcha.Tenant.Service.Endpoints` and model imports to `Sorcha.Tenant.Service.Models` and `Sorcha.Tenant.Service.Services.Interfaces`.

**Step 2: Register in Tenant's Program.cs**

Add after the last `Map*Endpoints()` call (around line 139):
```csharp
app.MapEventEndpoints();
```

Also register the DI services. Find where services are registered and add:
```csharp
builder.Services.AddScoped<Sorcha.Tenant.Service.Services.Interfaces.IEventService,
    Sorcha.Tenant.Service.Services.EventService>();
```

**Step 3: Verify build**
```bash
dotnet build src/Services/Sorcha.Tenant.Service/
```

**Step 4: Commit**
```bash
git add src/Services/Sorcha.Tenant.Service/Endpoints/EventEndpoints.cs
git add src/Services/Sorcha.Tenant.Service/Program.cs
git commit -m "feat: add EventEndpoints to Tenant Service"
```

---

## Task 6: Move EventCleanupService to Tenant Service

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Services/EventCleanupService.cs`

**Step 1: Create EventCleanupService**

Copy from Blueprint's `EventCleanupService.cs`, updating:
- Namespace: `Sorcha.Tenant.Service.Services`
- DbContext: `TenantDbContext` instead of `BlueprintEventsDbContext`
- Imports: `Sorcha.Tenant.Service.Data` and `Sorcha.Tenant.Service.Models`

**Step 2: Register in Tenant's Program.cs**

Add alongside the IEventService registration:
```csharp
builder.Services.AddHostedService<Sorcha.Tenant.Service.Services.EventCleanupService>();
```

**Step 3: Verify build**
```bash
dotnet build src/Services/Sorcha.Tenant.Service/
```

**Step 4: Commit**
```bash
git add src/Services/Sorcha.Tenant.Service/Services/EventCleanupService.cs
git add src/Services/Sorcha.Tenant.Service/Program.cs
git commit -m "feat: add EventCleanupService to Tenant Service"
```

---

## Task 7: Create IEventServiceClient in Sorcha.ServiceClients

**Files:**
- Create: `src/Common/Sorcha.ServiceClients/Events/IEventServiceClient.cs`
- Create: `src/Common/Sorcha.ServiceClients/Events/EventServiceClient.cs`
- Create: `src/Common/Sorcha.ServiceClients/Events/Models/CreateEventRequest.cs`
- Modify: `src/Common/Sorcha.ServiceClients/Extensions/ServiceCollectionExtensions.cs`

**Purpose:** Blueprint Service's `EncryptionBackgroundService` currently calls `IEventService` directly. After moving events to Tenant, it needs an HTTP client to POST events.

**Step 1: Create the request model**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Events.Models;

public record CreateActivityEventRequest(
    Guid OrganizationId,
    Guid UserId,
    string EventType,
    string Severity,
    string Title,
    string Message,
    string SourceService,
    string? EntityId = null,
    string? EntityType = null);
```

**Step 2: Create IEventServiceClient interface**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Events.Models;

namespace Sorcha.ServiceClients.Events;

/// <summary>
/// HTTP client for creating activity events on the Tenant Service.
/// </summary>
public interface IEventServiceClient
{
    Task<bool> CreateEventAsync(CreateActivityEventRequest request, CancellationToken ct = default);
}
```

**Step 3: Create EventServiceClient implementation**

Follow existing client patterns in `Sorcha.ServiceClients` (e.g., `BlueprintServiceClient.cs`):
- Use HttpClient with service-to-service auth
- POST to `/api/events`
- Best-effort (return bool, don't throw on failure)

**Step 4: Register in ServiceCollectionExtensions**

Add to `AddServiceClients()` method, following the pattern used for other clients.

**Step 5: Verify build**
```bash
dotnet build src/Common/Sorcha.ServiceClients/
```

**Step 6: Commit**
```bash
git add src/Common/Sorcha.ServiceClients/Events/
git add src/Common/Sorcha.ServiceClients/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: add IEventServiceClient for cross-service event creation"
```

---

## Task 8: Update Blueprint Service's EncryptionBackgroundService

**Files:**
- Modify: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`

**Step 1: Replace IEventService with IEventServiceClient**

In `StoreActivityEventAsync` (line ~313-347):
- Change `serviceProvider.GetService<IEventService>()` → `serviceProvider.GetService<IEventServiceClient>()`
- Replace `new ActivityEvent { ... }` with `new CreateActivityEventRequest(...)`
- Replace `await eventService.CreateEventAsync(activityEvent)` with `await client.CreateEventAsync(request)`
- Update the `using` import at line 6 from `using ActivityEvent = Sorcha.Blueprint.Service.Models.ActivityEvent;`

**Step 2: Verify build**
```bash
dotnet build src/Services/Sorcha.Blueprint.Service/
```

**Step 3: Commit**
```bash
git add src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs
git commit -m "refactor: use IEventServiceClient for cross-service event creation"
```

---

## Task 9: Update Blueprint OperationsEndpoints to Use Service Client

**Files:**
- Modify: `src/Services/Sorcha.Blueprint.Service/Endpoints/OperationsEndpoints.cs`

**Step 1: Examine how OperationsEndpoints uses IEventService**

Currently `OperationsEndpoints.cs` injects `IEventService` to read encryption history (line 49, 92-97). This reads events for a user. Since events now live in Tenant Service, this endpoint needs either:
- **Option A:** Call Tenant's `/api/events` via IEventServiceClient (add a read method)
- **Option B:** Remove the event history from this endpoint (the UI can query `/api/events` directly)

**Recommended: Option B** — The operations endpoint returns active in-memory operations. Historical events should be queried directly from `/api/events`. This is cleaner separation. Remove the `IEventService` injection and the event history code from the operations endpoint. The UI already has access to `/api/events` for the activity log.

**Step 2: Remove IEventService usage from OperationsEndpoints**

Remove the `IEventService eventService` parameter and the "Get completed/failed operations from activity events" block (lines ~88-115). Keep only the in-memory active operation lookup.

**Step 3: Verify build**
```bash
dotnet build src/Services/Sorcha.Blueprint.Service/
```

**Step 4: Commit**
```bash
git add src/Services/Sorcha.Blueprint.Service/Endpoints/OperationsEndpoints.cs
git commit -m "refactor: remove IEventService from OperationsEndpoints (events now in Tenant)"
```

---

## Task 10: Remove SQLite and Event Code from Blueprint Service

**Files:**
- Delete: `src/Services/Sorcha.Blueprint.Service/Data/BlueprintEventsDbContext.cs`
- Delete: `src/Services/Sorcha.Blueprint.Service/Models/ActivityEvent.cs`
- Delete: `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IEventService.cs`
- Delete: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventService.cs`
- Delete: `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventCleanupService.cs`
- Delete: `src/Services/Sorcha.Blueprint.Service/Endpoints/EventEndpoints.cs`
- Delete: `src/Services/Sorcha.Blueprint.Service/BlueprintEvents.db` (stale SQLite file)
- Modify: `src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj` (remove SQLite package)
- Modify: `src/Services/Sorcha.Blueprint.Service/Program.cs` (remove BlueprintEventsDbContext, IEventService, EventCleanupService registrations)

**Step 1: Delete files**
```bash
rm src/Services/Sorcha.Blueprint.Service/Data/BlueprintEventsDbContext.cs
rm src/Services/Sorcha.Blueprint.Service/Models/ActivityEvent.cs
rm src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IEventService.cs
rm src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventService.cs
rm src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventCleanupService.cs
rm src/Services/Sorcha.Blueprint.Service/Endpoints/EventEndpoints.cs
rm -f src/Services/Sorcha.Blueprint.Service/BlueprintEvents.db
```

**Step 2: Remove SQLite NuGet from .csproj**

Remove line 14 from `Sorcha.Blueprint.Service.csproj`:
```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
```

Also remove `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Design` if no longer needed (check if anything else uses EF in Blueprint Service).

**Step 3: Remove registrations from Program.cs**

Remove lines 126-137 from `src/Services/Sorcha.Blueprint.Service/Program.cs`:
```csharp
// Add Activity Events PostgreSQL context (043)
builder.Services.AddDbContext<Sorcha.Blueprint.Service.Data.BlueprintEventsDbContext>(options =>
{
    var eventsConnStr = builder.Configuration.GetConnectionString("EventsDb");
    if (!string.IsNullOrEmpty(eventsConnStr))
        options.UseNpgsql(eventsConnStr, npgsql => npgsql.EnableRetryOnFailure(3));
    else
        options.UseSqlite("DataSource=BlueprintEvents.db");
});
builder.Services.AddScoped<Sorcha.Blueprint.Service.Services.Interfaces.IEventService,
    Sorcha.Blueprint.Service.Services.Implementation.EventService>();
builder.Services.AddHostedService<Sorcha.Blueprint.Service.Services.Implementation.EventCleanupService>();
```

Also remove line 289: `app.MapEventEndpoints();`

**Step 4: Verify build**
```bash
dotnet build src/Services/Sorcha.Blueprint.Service/
```

**Step 5: Commit**
```bash
git add -A src/Services/Sorcha.Blueprint.Service/
git commit -m "refactor: remove SQLite and ActivityEvent code from Blueprint Service"
```

---

## Task 11: Update API Gateway Routes

**Files:**
- Modify: `src/Services/Sorcha.ApiGateway/appsettings.json`

**Step 1: Change events routes from blueprint-cluster to tenant-cluster**

In `appsettings.json`, find the `events-direct-route` (line ~1106) and `events-base-route` (line ~1118):
- Change `"ClusterId": "blueprint-cluster"` → `"ClusterId": "tenant-cluster"`

Leave the SignalR EventsHub routes (`eventshub-signalr-route`, `eventshub-signalr-negotiate`) pointing to blueprint-cluster — those are for Blueprint's real-time action notifications, not activity events.

**Step 2: Verify route structure**
```bash
grep -A3 "events-direct-route\|events-base-route" src/Services/Sorcha.ApiGateway/appsettings.json
```

**Step 3: Commit**
```bash
git add src/Services/Sorcha.ApiGateway/appsettings.json
git commit -m "fix: route /api/events to tenant-cluster after event migration"
```

---

## Task 12: Update Tests

**Files:**
- Modify: `tests/Sorcha.Blueprint.Service.Tests/Services/EventServiceTests.cs` → Move to Tenant tests
- Create: `tests/Sorcha.Tenant.Service.Tests/Services/EventServiceTests.cs`
- Modify: `tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj` (remove SQLite)
- Verify: Any Blueprint integration tests that reference IEventService

**Step 1: Move EventServiceTests to Tenant test project**

Copy `tests/Sorcha.Blueprint.Service.Tests/Services/EventServiceTests.cs` to `tests/Sorcha.Tenant.Service.Tests/Services/EventServiceTests.cs`.

Update:
- Namespace: `Sorcha.Tenant.Service.Tests.Services`
- Model imports: `Sorcha.Tenant.Service.Models`
- Service imports: `Sorcha.Tenant.Service.Services`
- DbContext: `TenantDbContext` instead of `BlueprintEventsDbContext`
- Keep using SQLite in-memory for tests — this is the correct test pattern

Ensure the Tenant test project has `Microsoft.EntityFrameworkCore.Sqlite` in its `.csproj` (for test-only use).

**Step 2: Delete the old test file from Blueprint tests**
```bash
rm tests/Sorcha.Blueprint.Service.Tests/Services/EventServiceTests.cs
```

**Step 3: Remove SQLite from Blueprint test project if no longer needed**

Check `tests/Sorcha.Blueprint.Service.Tests/Sorcha.Blueprint.Service.Tests.csproj` — remove `Microsoft.EntityFrameworkCore.Sqlite` if no other tests use it.

**Step 4: Run all tests**
```bash
dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "EventService"
dotnet test tests/Sorcha.Blueprint.Service.Tests/
```

**Step 5: Commit**
```bash
git add tests/Sorcha.Tenant.Service.Tests/Services/EventServiceTests.cs
git add tests/Sorcha.Blueprint.Service.Tests/
git commit -m "test: move EventServiceTests to Tenant Service test project"
```

---

## Task 13: Remove SQLite from Directory.Packages.props (if unused)

**Files:**
- Modify: `Directory.Packages.props`

**Step 1: Check if any project still references SQLite**
```bash
grep -r "Microsoft.EntityFrameworkCore.Sqlite\|Microsoft.Data.Sqlite" --include="*.csproj" .
```

If only the Tenant test project uses it, keep it in `Directory.Packages.props`. If nothing uses it, remove both:
- `<PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.3" />`
- `<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.3" />`

**Step 2: Verify build**
```bash
dotnet build
```

**Step 3: Commit**
```bash
git add Directory.Packages.props
git commit -m "chore: clean up SQLite package references"
```

---

## Task 14: Add Multi-Tenant Isolation to Deferred Tasks

**Files:**
- Modify: `.specify/tasks/deferred-tasks.md`

**Step 1: Add new deferred task**

Under the "Tenant Service Full Implementation" section, add:

```markdown
| TENANT-4 | Activity event multi-tenant isolation | P3 | 8h | 📋 Deferred | Events currently in public schema; consider per-org schema isolation when TENANT-1 is implemented |
```

**Step 2: Commit**
```bash
git add .specify/tasks/deferred-tasks.md
git commit -m "docs: add multi-tenant event isolation to deferred tasks"
```

---

## Task 15: Docker Verification

**Step 1: Rebuild and verify clean startup**
```bash
docker-compose down -v
docker-compose build --no-cache blueprint-service tenant-service api-gateway
docker-compose up -d
```

**Step 2: Monitor logs**
```bash
# Verify no SQLite errors in Blueprint
docker-compose logs blueprint-service 2>&1 | grep -i sqlite
# Should return nothing

# Verify Tenant creates ActivityEvents table
docker-compose logs tenant-service 2>&1 | grep -i "ActivityEvent\|activity"

# Verify events endpoint routes to Tenant
curl -s http://localhost/api/events/ -H "Authorization: Bearer <token>"
```

**Step 3: Run full test suite**
```bash
dotnet test
```
