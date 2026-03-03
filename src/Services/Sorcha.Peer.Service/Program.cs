// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Sorcha.Peer.Service;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Distribution;
using Sorcha.Peer.Service.Extensions;
using Sorcha.Peer.Service.Monitoring;
using Sorcha.Peer.Service.Network;
using Sorcha.Peer.Service.Observability;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Peer.Service.Models;
using Sorcha.Peer.Service.Replication;
using Sorcha.ServiceClients.Peer;
using Sorcha.Peer.Service.GrpcServices;
using Sorcha.Peer.Service.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults (OpenTelemetry, health checks, service discovery)
builder.AddServiceDefaults();

// Add structured logging with Serilog (OPS-001)
builder.AddSerilogLogging();

// Add JWT authentication and authorization
builder.AddJwtAuthentication();
builder.Services.AddPeerServiceAuthorization();

// Add rate limiting (SEC-002)
builder.AddRateLimiting();

// Add input validation (SEC-003)
builder.AddInputValidation();

// Configure Kestrel for gRPC with HTTP/1.1 fallback for health checks
// Listen on port 8080/8052 for HTTP/health checks (Aspire/Docker standard)
// Listen on port 5000/5003 for gRPC peer-to-peer communication
var healthCheckPort = int.TryParse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), out var envPort) ? envPort : 8080;
var grpcPort = builder.Configuration.GetValue<int>("PeerService:Port", 5000);
var enableTls = builder.Configuration.GetValue<bool>("PeerService:EnableTls", false);

builder.WebHost.ConfigureKestrel(options =>
{
    // Disable minimum protocol version to allow HTTP/2 without TLS
    options.ConfigureEndpointDefaults(listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // Port 8080/8052: HTTP/1.1 + HTTP/2 for health checks and REST API
    options.ListenAnyIP(healthCheckPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });

    // Port 5000/5003: HTTP/2 for gRPC peer communication (only if different from health port)
    if (grpcPort != healthCheckPort)
    {
        options.ListenAnyIP(grpcPort, listenOptions =>
        {
            // Enable HTTP/2 without TLS for development (cleartext HTTP/2)
            // This allows gRPC to work without certificates
            listenOptions.Protocols = HttpProtocols.Http2;
        });
    }
});

// Add Redis for advertisement persistence
builder.AddRedisClient("redis");

// Add PeerDbContext with PostgreSQL (falls back to InMemory if no connection string)
var peerDbConnectionString = builder.Configuration.GetConnectionString("PeerDb");
if (!string.IsNullOrEmpty(peerDbConnectionString))
{
    builder.Services.AddDbContextFactory<Sorcha.Peer.Service.Data.PeerDbContext>(options =>
        options.UseNpgsql(peerDbConnectionString));
}
else
{
    builder.Services.AddDbContextFactory<Sorcha.Peer.Service.Data.PeerDbContext>(options =>
        options.UseInMemoryDatabase("PeerServiceDb"));
}

// Add services
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16 MB
    options.MaxSendMessageSize = 16 * 1024 * 1024;    // 16 MB
    options.Interceptors.Add<Sorcha.Peer.Service.GrpcServices.PeerAuthInterceptor>();
});
builder.Services.AddGrpcReflection();

// Configure AppContext to allow HTTP/2 without TLS
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

// Add OpenAPI services with standard Sorcha metadata
builder.AddSorchaOpenApi("Sorcha Peer Service API", "P2P networking, peer discovery, gossip protocol, register replication, and connection quality monitoring for the Sorcha distributed ledger platform.");

// Configure peer service
builder.Services.Configure<PeerServiceConfiguration>(
    builder.Configuration.GetSection("PeerService"));

// Register gRPC auth interceptor (FR-014: authenticated peers get higher trust)
builder.Services.AddSingleton<Sorcha.Peer.Service.GrpcServices.PeerAuthInterceptor>();

// Register core services
builder.Services.AddSingleton<StunClient>();
builder.Services.AddSingleton<PeerListManager>();
builder.Services.AddSingleton<NetworkAddressService>();
builder.Services.AddSingleton<PeerDiscoveryService>();
builder.Services.AddSingleton<HealthMonitorService>();
builder.Services.AddSingleton<CommunicationProtocolManager>();
builder.Services.AddSingleton<ConnectionQualityTracker>();
builder.Services.AddSingleton<ConnectionTestingService>();
builder.Services.AddSingleton<GossipProtocolEngine>();
builder.Services.AddSingleton<TransactionQueueManager>();
builder.Services.AddSingleton<TransactionDistributionService>();
builder.Services.AddSingleton<StatisticsAggregator>();

// Register observability services (OpenTelemetry)
builder.Services.AddSingleton<PeerServiceMetrics>();
builder.Services.AddSingleton<PeerServiceActivitySource>();

// Register P2P replication services
builder.Services.AddSingleton<RegisterCache>();
builder.Services.AddSingleton<RegisterReplicationService>();
builder.Services.AddSingleton<IRedisAdvertisementStore, RedisAdvertisementStore>();
builder.Services.AddSingleton<RegisterAdvertisementService>();

// Register P2P connection pool (Phase 4)
builder.Services.AddSingleton<PeerConnectionPool>();

// Register P2P discovery (Phase 5)
builder.Services.AddSingleton<PeerExchangeService>();

// Register gRPC service implementations
builder.Services.AddSingleton<PeerDiscoveryServiceImpl>();
builder.Services.AddSingleton<PeerHeartbeatGrpcService>();
builder.Services.AddSingleton<RegisterSyncGrpcService>();
builder.Services.AddSingleton<TransactionDistributionGrpcService>();
builder.Services.AddSingleton<DocketSyncGrpcService>();

// Register background services
builder.Services.AddHostedService<PeerService>();
builder.Services.AddHostedService<PeerHeartbeatBackgroundService>();
// RegisterSyncBackgroundService is also resolved by concrete type in REST endpoints,
// so register as singleton first, then wire up as hosted service.
builder.Services.AddSingleton<RegisterSyncBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RegisterSyncBackgroundService>());

// Add HttpClient
builder.Services.AddHttpClient();

var app = builder.Build();

// Load persisted advertisements from Redis on startup (FR-002)
var advertisementService = app.Services.GetRequiredService<RegisterAdvertisementService>();
await advertisementService.LoadFromRedisAsync();

// Map default endpoints (health checks)
app.MapDefaultEndpoints();

// Add Serilog HTTP request logging (OPS-001)
app.UseSerilogLogging();

// Add OWASP security headers (SEC-004)
app.UseApiSecurityHeaders();

// Enable HTTPS enforcement with HSTS (SEC-001)
app.UseHttpsEnforcement();

// Enable input validation (SEC-003)
app.UseInputValidation();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Enable rate limiting (SEC-002)
app.UseRateLimiting();

// Configure OpenAPI and Scalar API documentation UI (development only)
app.MapSorchaOpenApiUi("Peer Service");

// Map gRPC services
app.MapGrpcService<PeerDiscoveryServiceImpl>();
app.MapGrpcService<PeerHeartbeatGrpcService>();
app.MapGrpcService<RegisterSyncGrpcService>();
app.MapGrpcService<TransactionDistributionGrpcService>();
app.MapGrpcService<DocketSyncGrpcService>();

// Enable gRPC reflection for development
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

// Root endpoint
app.MapGet("/", (IConfiguration config) =>
{
    var httpPort = int.TryParse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), out var envHttpPort) ? envHttpPort : 8080;
    var grpcPort = config.GetValue<int>("PeerService:Port", 5000);
    return $"Sorcha Peer Service - HTTP API: port {httpPort}, gRPC: port {grpcPort}";
})
    .WithName("GetServiceInfo")
    .WithSummary("Get service information")
    .WithDescription("Returns basic information about the Peer Service and its available ports")
    .WithTags("Info");

// REST monitoring endpoints for CLI and admin tools
// These endpoints provide read-only access to peer network status

app.MapGet("/api/peers", (PeerListManager peerListManager, ConnectionQualityTracker qualityTracker) =>
{
    var peers = peerListManager.GetAllPeers();
    var qualities = qualityTracker.GetAllQualities();

    return Results.Ok(peers.Select(p =>
    {
        qualities.TryGetValue(p.PeerId, out var quality);
        return new
        {
            p.PeerId,
            p.Address,
            p.Port,
            p.SupportedProtocols,
            p.FirstSeen,
            p.LastSeen,
            p.FailureCount,
            p.IsSeedNode,
            p.AverageLatencyMs,
            p.IsBanned,
            p.BannedAt,
            p.BanReason,
            QualityScore = quality?.QualityScore ?? 0,
            QualityRating = quality?.QualityRating ?? "Unknown",
            AdvertisedRegisterCount = p.AdvertisedRegisters.Count,
            AdvertisedRegisters = p.AdvertisedRegisters.Select(r => new
            {
                r.RegisterId,
                SyncState = r.SyncState.ToString(),
                r.LatestVersion,
                r.IsPublic
            })
        };
    }));
})
    .WithName("GetAllPeers")
    .WithSummary("List all known peers in the network")
    .WithDescription(@"Returns a comprehensive list of all peer nodes currently known to this node, including connection metadata, latency metrics, quality scores, ban status, and advertised registers.")
    .WithTags("Peers");

app.MapGet("/api/peers/quality", (ConnectionQualityTracker qualityTracker) =>
{
    var qualities = qualityTracker.GetAllQualities();
    return Results.Ok(qualities.Values.Select(q => new
    {
        q.PeerId,
        q.AverageLatencyMs,
        q.MinLatencyMs,
        q.MaxLatencyMs,
        q.SuccessRate,
        q.TotalRequests,
        q.SuccessfulRequests,
        q.FailedRequests,
        q.QualityScore,
        q.QualityRating,
        q.LastUpdated
    }));
})
    .WithName("GetPeerQuality")
    .WithSummary("Get connection quality metrics for all tracked peers")
    .WithDescription("Returns quality scores, latency breakdown, and success rates for all peers with tracked connection metrics.")
    .WithTags("Monitoring");

app.MapGet("/api/peers/{peerId}", (string peerId, PeerListManager peerListManager, ConnectionQualityTracker qualityTracker) =>
{
    var peer = peerListManager.GetPeer(peerId);
    if (peer == null)
    {
        return Results.NotFound(new { error = $"Peer '{peerId}' not found" });
    }

    var quality = qualityTracker.GetQuality(peerId);

    return Results.Ok(new
    {
        peer.PeerId,
        peer.Address,
        peer.Port,
        peer.SupportedProtocols,
        peer.FirstSeen,
        peer.LastSeen,
        peer.FailureCount,
        peer.IsSeedNode,
        peer.AverageLatencyMs,
        peer.IsBanned,
        peer.BannedAt,
        peer.BanReason,
        QualityScore = quality?.QualityScore ?? 0,
        QualityRating = quality?.QualityRating ?? "Unknown",
        QualityDetails = quality != null ? new
        {
            quality.AverageLatencyMs,
            quality.MinLatencyMs,
            quality.MaxLatencyMs,
            quality.SuccessRate,
            quality.TotalRequests,
            quality.SuccessfulRequests,
            quality.FailedRequests,
            quality.LastUpdated
        } : null,
        AdvertisedRegisterCount = peer.AdvertisedRegisters.Count,
        AdvertisedRegisters = peer.AdvertisedRegisters.Select(r => new
        {
            r.RegisterId,
            SyncState = r.SyncState.ToString(),
            r.LatestVersion,
            r.IsPublic
        })
    });
})
    .WithName("GetPeerById")
    .WithSummary("Get detailed information about a specific peer")
    .WithDescription(@"Retrieves comprehensive details for a single peer node including connection history, latency statistics, quality metrics, ban status, and advertised registers.")
    .WithTags("Peers");

app.MapGet("/api/peers/stats", (StatisticsAggregator statisticsAggregator) =>
{
    var stats = statisticsAggregator.GetStatistics();
    return Results.Ok(stats);
})
    .WithName("GetPeerStatistics")
    .WithSummary("Get aggregated peer network statistics")
    .WithDescription(@"Returns comprehensive statistics about the peer-to-peer network including total peer count, connection quality metrics, throughput statistics, and network health indicators. Useful for monitoring and diagnostics.")
    .WithTags("Monitoring");

app.MapGet("/api/peers/health", (PeerListManager peerListManager) =>
{
    var healthyPeers = peerListManager.GetHealthyPeers();
    var allPeers = peerListManager.GetAllPeers();

    return Results.Ok(new
    {
        TotalPeers = allPeers.Count,
        HealthyPeers = healthyPeers.Count,
        UnhealthyPeers = allPeers.Count - healthyPeers.Count,
        HealthPercentage = allPeers.Count > 0 ? (double)healthyPeers.Count / allPeers.Count * 100 : 0,
        Peers = healthyPeers.Select(p => new
        {
            p.PeerId,
            p.Address,
            p.Port,
            p.LastSeen,
            p.AverageLatencyMs
        })
    });
})
    .WithName("GetPeerHealth")
    .WithSummary("Get peer network health status")
    .WithDescription(@"Returns health status analysis of all peer nodes in the network, categorizing peers as healthy or unhealthy based on connectivity and responsiveness metrics. Includes overall health percentage and detailed metrics for healthy peers.")
    .WithTags("Monitoring");

// Get count of connected peers (anonymous) and optional list (authenticated)
app.MapGet("/api/peers/connected", (PeerListManager peerListManager, HttpContext context) =>
{
    var healthyPeers = peerListManager.GetHealthyPeers();
    var count = healthyPeers.Count;

    // Check if user is authenticated
    var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;

    if (isAuthenticated)
    {
        // Return count and full list for authenticated users
        return Results.Ok(new
        {
            ConnectedPeerCount = count,
            Peers = healthyPeers.Select(p => new
            {
                p.PeerId,
                p.Address,
                p.Port,
                p.SupportedProtocols,
                p.LastSeen,
                p.AverageLatencyMs,
                p.IsSeedNode
            })
        });
    }
    else
    {
        // Return only count for anonymous users
        return Results.Ok(new
        {
            ConnectedPeerCount = count
        });
    }
})
    .WithName("GetConnectedPeers")
    .WithSummary("Get count of connected peers")
    .WithDescription(@"Returns the count of currently connected (healthy) peers. Anonymous users receive only the count, while authenticated users also receive the full list of connected peers with their details.")
    .WithTags("Peers")
    .AllowAnonymous();

// Register subscription endpoints (P2P replication)
app.MapGet("/api/registers/subscriptions", ([FromServices] RegisterSyncBackgroundService syncService) =>
{
    var subs = syncService.GetSubscriptions();
    return Results.Ok(subs.Select(s => new
    {
        s.RegisterId,
        Mode = s.Mode.ToString(),
        SyncState = s.SyncState.ToString(),
        s.LastSyncedDocketVersion,
        s.LastSyncedTransactionVersion,
        s.TotalDocketsInChain,
        s.SyncProgressPercent,
        s.CanParticipateInValidation,
        s.IsReceiving,
        s.LastSyncAt,
        s.ConsecutiveFailures,
        s.ErrorMessage
    }));
})
    .WithName("GetRegisterSubscriptions")
    .WithSummary("List all register subscriptions")
    .WithDescription("Returns all per-register replication subscriptions with their sync state and progress.")
    .WithTags("Registers");

app.MapGet("/api/registers/cache", ([FromServices] RegisterCache registerCache) =>
{
    var stats = registerCache.GetAllStatistics();
    return Results.Ok(stats.Select(s => new
    {
        s.Value.RegisterId,
        s.Value.TransactionCount,
        s.Value.DocketCount,
        s.Value.LatestTransactionVersion,
        s.Value.LatestDocketVersion,
        s.Value.LastUpdateTime
    }));
})
    .WithName("GetRegisterCacheStats")
    .WithSummary("Get register cache statistics")
    .WithDescription("Returns cache statistics for all locally cached registers.")
    .WithTags("Registers");

// Available registers endpoint (aggregated from peer advertisements)
app.MapGet("/api/registers/available", ([FromServices] RegisterAdvertisementService advertisementService) =>
{
    var registers = advertisementService.GetNetworkAdvertisedRegisters();
    return Results.Ok(registers);
})
    .WithName("GetAvailableRegisters")
    .WithSummary("List registers advertised across the peer network")
    .WithDescription("Returns aggregated register information from all known peer advertisements. Only public registers are included.")
    .WithTags("Registers");

// Bulk advertise registers (called by Register Service on startup/resync)
app.MapPost("/api/registers/bulk-advertise", async (
    [FromBody] BulkAdvertiseRequest request,
    [FromServices] RegisterAdvertisementService advertisementService,
    [FromServices] IRedisAdvertisementStore store,
    ILogger<Sorcha.Peer.Service.Program> logger) =>
{
    var added = 0;
    var updated = 0;
    var removed = 0;

    var advertisedIds = new HashSet<string>();

    foreach (var item in request.Advertisements)
    {
        advertisedIds.Add(item.RegisterId);

        var existing = advertisementService.GetAdvertisement(item.RegisterId);
        if (existing != null)
        {
            advertisementService.UpdateRegisterVersion(item.RegisterId, item.LatestVersion, item.LatestDocketVersion);
            updated++;
        }
        else
        {
            advertisementService.AdvertiseRegister(
                item.RegisterId,
                RegisterSyncState.Active,
                latestVersion: item.LatestVersion,
                latestDocketVersion: item.LatestDocketVersion,
                isPublic: item.IsPublic,
                name: item.Name,
                description: item.Description);
            added++;
        }
    }

    // Full-sync mode: remove stale local ads not in the request
    if (request.FullSync)
    {
        try
        {
            removed = await store.RemoveLocalExceptAsync(advertisedIds);
            // Also remove from in-memory state
            foreach (var ad in advertisementService.GetLocalAdvertisements())
            {
                if (!advertisedIds.Contains(ad.RegisterId))
                {
                    advertisementService.RemoveAdvertisement(ad.RegisterId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove stale advertisements during full-sync");
        }
    }

    logger.LogInformation(
        "Bulk advertise processed: {Processed} total, {Added} added, {Updated} updated, {Removed} removed (FullSync={FullSync})",
        request.Advertisements.Count, added, updated, removed, request.FullSync);

    return Results.Ok(new BulkAdvertiseResponse
    {
        Processed = request.Advertisements.Count,
        Added = added,
        Updated = updated,
        Removed = removed
    });
})
    .WithName("BulkAdvertiseRegisters")
    .WithSummary("Bulk advertise or sync register advertisements")
    .WithDescription("Called by Register Service on startup and during periodic resync. When FullSync is true, removes any local advertisements not included in the request.")
    .WithTags("Registers");

// Advertise or remove advertisement for a register
app.MapPost("/api/registers/{registerId}/advertise", (
    string registerId,
    [FromBody] AdvertiseRegisterRequest request,
    [FromServices] RegisterAdvertisementService advertisementService) =>
{
    if (request.IsPublic)
    {
        advertisementService.AdvertiseRegister(registerId, RegisterSyncState.Active, isPublic: true);
    }
    else
    {
        advertisementService.RemoveAdvertisement(registerId);
    }

    return Results.Ok(new { registerId, isPublic = request.IsPublic });
})
    .WithName("AdvertiseRegister")
    .WithSummary("Advertise or remove advertisement for a register")
    .WithDescription("Called by Register Service when a register's advertise flag changes. Sets or removes the register's public advertisement in the peer network.")
    .WithTags("Registers");

// Subscribe to a register
app.MapPost("/api/registers/{registerId}/subscribe", async (
    string registerId,
    SubscribeRequest request,
    [FromServices] RegisterSyncBackgroundService syncService,
    [FromServices] RegisterAdvertisementService advertisementService) =>
{
    // Validate mode
    if (!Enum.TryParse<ReplicationMode>(request.Mode.Replace("-", ""), ignoreCase: true, out var mode))
    {
        return Results.BadRequest(new { error = "Invalid mode. Use 'forward-only' or 'full-replica'." });
    }

    // Check if already subscribed
    var existing = syncService.GetSubscription(registerId);
    if (existing != null)
    {
        return Results.Conflict(new { error = $"Already subscribed to register '{registerId}'." });
    }

    // Check if register exists in network advertisements
    var available = advertisementService.GetNetworkAdvertisedRegisters();
    if (!available.Any(r => r.RegisterId == registerId))
    {
        return Results.NotFound(new { error = $"Register '{registerId}' not found in network advertisements." });
    }

    var subscription = await syncService.SubscribeToRegisterAsync(registerId, mode);

    return Results.Created($"/api/registers/{registerId}/subscribe", new SubscribeResponse
    {
        RegisterId = subscription.RegisterId,
        Mode = subscription.Mode.ToString(),
        SyncState = subscription.SyncState.ToString(),
        LastSyncedDocketVersion = subscription.LastSyncedDocketVersion,
        LastSyncedTransactionVersion = subscription.LastSyncedTransactionVersion,
        SyncProgressPercent = subscription.SyncProgressPercent
    });
})
    .WithName("SubscribeToRegister")
    .WithSummary("Subscribe to a register for replication")
    .WithDescription("Creates a new subscription to replicate a register. Mode can be 'forward-only' (new transactions only) or 'full-replica' (complete docket chain pull).")
    .WithTags("Registers")
    .RequireAuthorization("RequireAuthenticated");

// Unsubscribe from a register
app.MapDelete("/api/registers/{registerId}/subscribe", async (
    string registerId,
    bool? purge,
    [FromServices] RegisterSyncBackgroundService syncService,
    [FromServices] RegisterCache registerCache) =>
{
    var existing = syncService.GetSubscription(registerId);
    if (existing == null)
    {
        return Results.NotFound(new { error = $"No subscription found for register '{registerId}'." });
    }

    await syncService.UnsubscribeFromRegisterAsync(registerId);

    var cacheRetained = true;
    if (purge == true)
    {
        registerCache.Remove(registerId);
        cacheRetained = false;
    }

    return Results.Ok(new UnsubscribeResponse
    {
        RegisterId = registerId,
        Unsubscribed = true,
        CacheRetained = cacheRetained
    });
})
    .WithName("UnsubscribeFromRegister")
    .WithSummary("Unsubscribe from a register")
    .WithDescription("Stops replication for a register. Cached data is retained unless ?purge=true is specified.")
    .WithTags("Registers")
    .RequireAuthorization("RequireAuthenticated");

// Purge cached data for a register
app.MapDelete("/api/registers/{registerId}/cache", (string registerId, [FromServices] RegisterCache registerCache) =>
{
    var entry = registerCache.Get(registerId);
    if (entry == null)
    {
        return Results.NotFound(new { error = $"No cached data for register '{registerId}'." });
    }

    var stats = entry.GetStatistics();
    entry.Clear();
    registerCache.Remove(registerId);

    return Results.Ok(new PurgeResponse
    {
        RegisterId = registerId,
        Purged = true,
        TransactionsRemoved = stats.TransactionCount,
        DocketsRemoved = stats.DocketCount
    });
})
    .WithName("PurgeRegisterCache")
    .WithSummary("Purge cached data for a register")
    .WithDescription("Deletes all locally cached transactions and dockets for a register.")
    .WithTags("Registers")
    .RequireAuthorization("RequireAuthenticated");

// Peer management endpoints (ban/unban/reset)
app.MapPost("/api/peers/{peerId}/ban", async (string peerId, BanRequest? request, PeerListManager peerListManager) =>
{
    var peer = peerListManager.GetPeer(peerId);
    if (peer == null)
    {
        return Results.NotFound(new { error = $"Peer '{peerId}' not found." });
    }

    if (peer.IsBanned)
    {
        return Results.Conflict(new { error = $"Peer '{peerId}' is already banned." });
    }

    await peerListManager.BanPeerAsync(peerId, request?.Reason);

    peer = peerListManager.GetPeer(peerId)!;
    return Results.Ok(new BanResponse
    {
        PeerId = peer.PeerId,
        IsBanned = peer.IsBanned,
        BannedAt = peer.BannedAt,
        BanReason = peer.BanReason
    });
})
    .WithName("BanPeer")
    .WithSummary("Ban a peer from communication")
    .WithDescription("Bans a peer, preventing all gossip, sync, and heartbeat communication. Ban persists across restarts.")
    .WithTags("Management")
    .RequireAuthorization("CanManagePeers");

app.MapDelete("/api/peers/{peerId}/ban", async (string peerId, PeerListManager peerListManager) =>
{
    var peer = peerListManager.GetPeer(peerId);
    if (peer == null)
    {
        return Results.NotFound(new { error = $"Peer '{peerId}' not found." });
    }

    if (!peer.IsBanned)
    {
        return Results.Conflict(new { error = $"Peer '{peerId}' is not currently banned." });
    }

    await peerListManager.UnbanPeerAsync(peerId);

    return Results.Ok(new BanResponse
    {
        PeerId = peerId,
        IsBanned = false
    });
})
    .WithName("UnbanPeer")
    .WithSummary("Unban a peer, restoring communication")
    .WithDescription("Removes the ban on a peer. The peer's failure count is preserved (not reset).")
    .WithTags("Management")
    .RequireAuthorization("CanManagePeers");

app.MapPost("/api/peers/{peerId}/reset", async (string peerId, PeerListManager peerListManager) =>
{
    var peer = peerListManager.GetPeer(peerId);
    if (peer == null)
    {
        return Results.NotFound(new { error = $"Peer '{peerId}' not found." });
    }

    var previousCount = await peerListManager.ResetFailureCountAsync(peerId);

    return Results.Ok(new ResetResponse
    {
        PeerId = peerId,
        FailureCount = 0,
        PreviousFailureCount = previousCount
    });
})
    .WithName("ResetPeerFailures")
    .WithSummary("Reset a peer's failure count")
    .WithDescription("Resets the consecutive failure count for a peer to zero, making it eligible for normal communication.")
    .WithTags("Management")
    .RequireAuthorization("CanManagePeers");

// Service health check with metrics (for admin dashboard)
app.MapGet("/api/health", (PeerListManager peerListManager, StatisticsAggregator statisticsAggregator) =>
{
    try
    {
        var allPeers = peerListManager.GetAllPeers();
        var healthyPeers = peerListManager.GetHealthyPeers();
        var stats = statisticsAggregator.GetStatistics();

        return Results.Ok(new
        {
            status = "healthy",
            service = "peer-service",
            timestamp = DateTimeOffset.UtcNow,
            version = "1.0.0",
            uptime = TimeSpan.FromMilliseconds(Environment.TickCount64).ToString(@"dd\.hh\:mm\:ss"),
            metrics = new
            {
                totalPeers = stats.PeerStats.TotalPeers,
                healthyPeers = stats.PeerStats.HealthyPeers,
                unhealthyPeers = stats.PeerStats.UnhealthyPeers,
                averageLatencyMs = stats.PeerStats.AverageLatencyMs,
                queueSize = stats.QueueStats.QueueSize
            }
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "unhealthy",
            service = "peer-service",
            timestamp = DateTimeOffset.UtcNow,
            error = ex.Message
        }, statusCode: 503);
    }
})
.WithName("HealthCheck")
.WithSummary("Service health check with metrics")
.WithTags("Health");

app.Run();

// Make the implicit Program class accessible to integration tests
namespace Sorcha.Peer.Service
{
    public partial class Program { }
}
