// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using Sorcha.Peer.Service.Replication;
using StackExchange.Redis;

namespace Sorcha.Peer.Service.Integration.Tests.Infrastructure;

/// <summary>
/// Factory for creating test instances of the Peer Service.
/// Replaces Redis and external dependencies with in-memory alternatives.
/// </summary>
public class PeerServiceFactory : WebApplicationFactory<Sorcha.Peer.Service.Program>
{
    public string PeerId { get; set; } = Guid.NewGuid().ToString();
    public int Port { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Use random port if not specified
        if (Port == 0)
        {
            Port = Random.Shared.Next(5000, 6000);
        }

        builder.UseUrls($"http://localhost:{Port}");

        builder.ConfigureTestServices(services =>
        {
            // Replace Redis output cache with in-memory
            var outputCacheDescriptor = services.FirstOrDefault(d =>
                d.ServiceType.Name.Contains("OutputCache"));
            if (outputCacheDescriptor != null)
            {
                services.Remove(outputCacheDescriptor);
            }
            services.AddOutputCache();

            // Replace Redis distributed cache with in-memory
            services.AddDistributedMemoryCache();

            // Replace IRedisAdvertisementStore with a no-op implementation for tests
            services.RemoveAll<IRedisAdvertisementStore>();
            services.AddSingleton<IRedisAdvertisementStore, NoOpRedisAdvertisementStore>();

            // Replace PeerDbContext with InMemory EF Core provider
            services.RemoveAll<DbContextOptions<PeerDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<PeerDbContext>(options =>
                options.UseInMemoryDatabase($"PeerTestDb-{PeerId}"));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Disable Aspire service defaults and replace Redis for testing
        builder.ConfigureServices(services =>
        {
            // Remove any services that require external Aspire infrastructure
            var aspireDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("Aspire") == true)
                .ToList();

            foreach (var descriptor in aspireDescriptors)
            {
                services.Remove(descriptor);
            }

            // Replace IConnectionMultiplexer with a stub that satisfies
            // OpenTelemetry's StackExchangeRedis instrumentation callbacks.
            // The Aspire AddRedisClient() registers OTel tracing callbacks that
            // call GetRequiredService<StackExchangeRedisInstrumentation>(), which
            // in turn needs IConnectionMultiplexer. Instead of removing OTel
            // (which breaks logging), we provide a stub Redis connection.
            services.RemoveAll<IConnectionMultiplexer>();
            services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect("localhost:1,abortConnect=false,connectTimeout=1"));
        });

        return base.CreateHost(builder);
    }
}

/// <summary>
/// No-op implementation of IRedisAdvertisementStore for testing.
/// All operations succeed but do not persist to Redis.
/// </summary>
internal class NoOpRedisAdvertisementStore : IRedisAdvertisementStore
{
    public Task SetLocalAsync(LocalRegisterAdvertisement advertisement, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SetRemoteAsync(string peerId, PeerRegisterInfo advertisement, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<LocalRegisterAdvertisement>> GetAllLocalAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LocalRegisterAdvertisement>>(new List<LocalRegisterAdvertisement>());

    public Task<IReadOnlyDictionary<string, List<PeerRegisterInfo>>> GetAllRemoteAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, List<PeerRegisterInfo>>>(
            new Dictionary<string, List<PeerRegisterInfo>>());

    public Task RemoveLocalAsync(string registerId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<int> RemoveLocalExceptAsync(HashSet<string> registerIdsToKeep, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task RemoveRemoteByPeerAsync(string peerId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
