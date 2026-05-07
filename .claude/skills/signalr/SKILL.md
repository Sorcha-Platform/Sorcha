---
name: signalr
description: |
  Implements real-time WebSocket communication using SignalR for action notifications and register events.
  Use when: Adding real-time notifications, creating hub endpoints, broadcasting to groups, or testing WebSocket communication.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# SignalR Skill

ASP.NET Core SignalR implementation for real-time client-server communication. Sorcha runs **five hubs** post-Feature 118: `BlueprintHub` (workflow signals; `/hubs/blueprint`), `WalletHub` (wallet-domain events; `/hubs/wallet`), `RegisterHub` (register-domain events; `/hubs/register`), `TenantHub` (identity / membership / inbox; `/hubs/tenant`), and `ChatHub` (the deliberate exception — RPC-streaming AI Designer; `/hubs/chat`). The legacy `EventsHub` and `/actionshub` alias retired in T121 / T122.

Every notification hub registers through `services.AddSorchaHub<THub, TClient>(IConfiguration, routePath, serviceShortName)` from `Sorcha.ServiceDefaults.Hubs`. The extension wires JWT Bearer auth, the SignalR Redis backplane (with per-service `ChannelPrefix=sorcha:signalr:{service}` for cross-service isolation), reconnect-with-jitter, OpenTelemetry instrumentation, and the storage-providers fail-fast audit. ChatHub is exempt — its streaming wire shape doesn't fit the notification-hub contract.

## Quick Start

### Hub Implementation

```csharp
// Every notification hub: typed client interface + Hub<TClient> + group builder.
public sealed class TenantHub : Hub<ITenantHubClient>
{
    public override async Task OnConnectedAsync()
    {
        var pid = Context.User?.FindFirst("platform_user_id")?.Value;
        if (Guid.TryParse(pid, out var platformUserId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantHubGroups.User(platformUserId));
        }
        await base.OnConnectedAsync();
    }
}

// Group strings come from the *HubGroups builder, never from inline interpolation.
public static class TenantHubGroups
{
    public static string User(Guid platformUserId) => $"user:{platformUserId:N}";
    public static string Org(Guid orgId) => $"org:{orgId:N}";
    public const string SystemAll = "system:all";
}
```

### Service Registration

```csharp
// One AddSorchaHub call per notification hub. Idempotent across hubs in the same service.
builder.Services.AddSorchaHub<TenantHub, ITenantHubClient>(
    builder.Configuration, "/hubs/tenant", "tenant");
// ...
app.MapSorchaHubs();   // maps every AddSorchaHub registration
```

### Sending from Services (thin-signal contract)

```csharp
// Hub events carry opaque IDs only — no claim values, descriptions, or balances.
// Detail fetch via REST is the contract.
public class InboxService
{
    private readonly IHubContext<TenantHub> _hub;

    public async Task EmitInboxEntryAddedAsync(InboxEntry entry, CancellationToken ct)
    {
        await _hub.Clients
            .Group(TenantHubGroups.User(entry.PlatformUserId))
            .SendAsync(
                "InboxEntryAdded",
                entry.Id.ToString("N"),
                entry.OccurredAt,
                Activity.Current?.TraceId.ToString() ?? "",
                ct);
    }
}
```

The thin-signal contract is enforced by `tests/Sorcha.Integration.Tests/Hubs/ThinSignalContractTests.cs` — adding an event method with a non-ID parameter type fails the suite. The CI grep gate at `scripts/check-no-inline-group-strings.ps1` (workflow `group-name-builder-check.yml`) enforces the builder rule.

### Client Connection (Testing)

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl($"{baseUrl}/hubs/tenant?access_token={jwt}")
    .Build();

connection.On<string, DateTimeOffset, string>("InboxEntryAdded", (entryId, occurredAt, traceId) =>
{
    // Fetch full entry detail via authenticated REST.
});
await connection.StartAsync();
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| Hub-per-service topology | One notification hub per service; ChatHub is the exception | TenantHub, BlueprintHub, WalletHub, RegisterHub, ChatHub |
| `AddSorchaHub<THub, TClient>` | Single-call DI wiring — auth + backplane + jitter + tracing + audit | `services.AddSorchaHub<TenantHub, ITenantHubClient>(cfg, "/hubs/tenant", "tenant")` |
| Group builders | All group strings constructed via `*HubGroups` static helpers | `TenantHubGroups.User(pid)` not `$"user:{pid:N}"` |
| Thin-signal contract | Events carry IDs + timestamps + trace tokens only — no descriptive payload | `InboxEntryAdded(string entryId, DateTimeOffset occurredAt, string traceId)` |
| Redis backplane isolation | `ChannelPrefix = sorcha:signalr:{serviceShortName}` per service | Each service's pub/sub keyspace is isolated |
| Multi-node fail-fast | Production refuses to start without Redis backplane | Audited via `IStorageRegistrationLog` (Feature 113 pattern) |
| JWT Auth | Bearer token; `platform_user_id` claim required on every notification hub | `?access_token={jwt}` |

## Common Patterns

### Service Abstraction Over Hub

**When:** Decoupling business logic from SignalR implementation

```csharp
// Interface in Services/Interfaces/
public interface INotificationService
{
    Task NotifyActionAvailableAsync(ActionNotification notification, CancellationToken ct = default);
}

// Register in DI
builder.Services.AddScoped<INotificationService, NotificationService>();
```

### Hub Registration in Program.cs

```csharp
// Feature 118 — every notification hub goes through AddSorchaHub.
builder.Services.AddSorchaHub<TenantHub, ITenantHubClient>(
    builder.Configuration, "/hubs/tenant", "tenant");

// Map after authentication middleware
app.MapSorchaHubs();
// ChatHub is the deliberate exception (FR-019) — explicit mapping.
app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();
```

## See Also

- [patterns](references/patterns.md) - Hub patterns, group routing, typed clients
- [workflows](references/workflows.md) - Testing, scaling, authentication setup

## Related Skills

- **aspire** - Service orchestration and configuration
- **jwt** - Authentication token setup for hub connections
- **redis** - Backplane configuration for scaling
- **xunit** - Integration testing patterns
- **fluent-assertions** - Test assertions for hub tests

## Documentation Resources

> Fetch latest SignalR documentation with Context7.

**How to use Context7:**
1. Use `mcp__context7__resolve-library-id` to search for "signalr aspnetcore"
2. **Prefer website documentation** (IDs starting with `/websites/`) over source code
3. Query with `mcp__context7__query-docs` using the resolved library ID

**Library ID:** `/websites/learn_microsoft_en-us_aspnet_core` _(ASP.NET Core docs including SignalR)_

**Recommended Queries:**
- "SignalR hub groups authentication"
- "SignalR Redis backplane scaling"
- "SignalR strongly typed hubs"