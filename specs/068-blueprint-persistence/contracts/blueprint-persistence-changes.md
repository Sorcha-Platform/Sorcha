# Contract Changes: Blueprint Service Persistence

**Feature**: 068-blueprint-persistence | **Date**: 2026-03-24

## No API Endpoint Changes

All existing Blueprint Service endpoints remain unchanged. This feature changes the storage layer behind the existing interfaces — callers see no difference.

## DI Registration Changes

### Blueprint Service Program.cs

```diff
- // Add in-memory storage (later: replace with EF Core + PostgreSQL)
- builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();
- builder.Services.AddSingleton<IPublishedBlueprintStore, InMemoryPublishedBlueprintStore>();
- builder.Services.AddSingleton<IActionStore, InMemoryActionStore>();
- builder.Services.AddSingleton<IInstanceStore, InMemoryInstanceStore>();
- builder.Services.AddSingleton<IDocumentStore<BlueprintTemplate, string>>(
-     new InMemoryDocumentStore<BlueprintTemplate, string>(t => t.Id));
+ // Add durable storage (PostgreSQL + Redis cache)
+ var blueprintDbConn = builder.Configuration.GetConnectionString("BlueprintDb");
+ if (!string.IsNullOrEmpty(blueprintDbConn))
+ {
+     builder.Services.AddDbContextFactory<BlueprintDbContext>(options =>
+         options.UseNpgsql(blueprintDbConn));
+     builder.Services.AddSingleton<IBlueprintStore, EfCoreBlueprintStore>();
+     builder.Services.AddSingleton<IActionStore, EfCoreActionStore>();
+     builder.Services.AddSingleton<IInstanceStore, EfCoreInstanceStore>();
+     builder.Services.AddSingleton<IDocumentStore<BlueprintTemplate, string>, EfCoreTemplateStore>();
+ }
+ else
+ {
+     builder.Services.AddSingleton<IBlueprintStore, InMemoryBlueprintStore>();
+     builder.Services.AddSingleton<IActionStore, InMemoryActionStore>();
+     builder.Services.AddSingleton<IInstanceStore, InMemoryInstanceStore>();
+     builder.Services.AddSingleton<IDocumentStore<BlueprintTemplate, string>>(
+         new InMemoryDocumentStore<BlueprintTemplate, string>(t => t.Id));
+ }
+ // Published blueprints always use Redis cache (register is source of truth)
+ builder.Services.AddSingleton<IPublishedBlueprintStore, RedisCachedPublishedBlueprintStore>();
```

## Infrastructure Changes

### AppHost.cs

```diff
  var tenantDb = postgres.AddDatabase("tenant-db", "sorcha_tenant");
  var walletDb = postgres.AddDatabase("wallet-db", "sorcha_wallet");
  var peerDb = postgres.AddDatabase("PeerDb", "sorcha_peer");
+ var blueprintDb = postgres.AddDatabase("BlueprintDb", "sorcha_blueprint");

  var blueprintService = builder.AddProject<Projects.Sorcha_Blueprint_Service>("blueprint-service")
+     .WithReference(blueprintDb)
      .WithReference(redis)
```

### docker-compose.yml

```diff
  blueprint-service:
    environment:
+     ConnectionStrings__BlueprintDb: Host=postgres;Database=sorcha_blueprint;Username=sorcha;Password=sorcha_dev_password
```

### docker/postgres-init.sql

```diff
+ SELECT 'CREATE DATABASE sorcha_blueprint'
+ WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_blueprint')\gexec
+ GRANT ALL PRIVILEGES ON DATABASE sorcha_blueprint TO sorcha;
```

## Validator Service Changes

### Startup Reconciliation

No API or interface changes. Internal enhancement to `DocketBuildTriggerService`:

```diff
  private async Task ReconcileGenesisStateAsync(CancellationToken cancellationToken)
  {
      // existing genesis height reconciliation...
+     // After height reconciliation, drain unverified pool
+     await ReconcileUnverifiedPoolAsync(cancellationToken);
  }

+ private async Task ReconcileUnverifiedPoolAsync(CancellationToken cancellationToken)
+ {
+     // For each monitored register, trigger immediate validation
+     // of any pending transactions in the unverified pool
+ }
```
