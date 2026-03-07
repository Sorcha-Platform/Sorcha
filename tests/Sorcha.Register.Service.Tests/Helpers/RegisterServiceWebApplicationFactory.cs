// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Repositories;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Storage.InMemory;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Validator;
using StackExchange.Redis;

namespace Sorcha.Register.Service.Tests.Helpers;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> that provides
/// required configuration values and test infrastructure for the Register Service test host.
/// </summary>
/// <remarks>
/// Fixes applied:
/// <list type="bullet">
///   <item><c>SystemWalletSigning:ValidatorId</c> — injected via environment variable</item>
///   <item><c>ConnectionStrings:redis</c> — dummy value to prevent Aspire startup failure</item>
///   <item><c>RegisterStorage:Type=InMemory</c> — use in-memory register storage</item>
///   <item>Authentication replaced with auto-authenticating test scheme</item>
///   <item>MongoDB dependencies (ISystemRegisterRepository) replaced with in-memory impl</item>
///   <item>Redis dependencies (IConnectionMultiplexer) replaced with mock</item>
///   <item>Redis-backed services (IPendingRegistrationStore, IEventPublisher) replaced</item>
///   <item>External service clients (Validator, Peer) replaced with mocks</item>
/// </list>
/// </remarks>
public class RegisterServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    private static readonly string[] EnvironmentVariables =
    [
        "SystemWalletSigning__ValidatorId",
        "ConnectionStrings__redis",
        "RegisterStorage__Type"
    ];

    public RegisterServiceWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable(
            "SystemWalletSigning__ValidatorId", "test-validator-001");
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__redis", "localhost:6379");
        // Force in-memory register storage (avoid MongoDB for main repo)
        Environment.SetEnvironmentVariable(
            "RegisterStorage__Type", "InMemory");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace authentication with auto-authenticating test scheme
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "TestScheme";
                options.DefaultChallengeScheme = "TestScheme";
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });

            // Replace Redis IConnectionMultiplexer with mock (prevents connection timeout)
            var mockRedis = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);
            mockRedis.Setup(r => r.IsConnected).Returns(false);
            ReplaceService<IConnectionMultiplexer>(services, _ => mockRedis.Object);

            // Replace ISystemRegisterRepository (MongoDB-dependent) with in-memory impl
            ReplaceService<ISystemRegisterRepository>(services,
                _ => new InMemorySystemRegisterRepository());

            // Replace Redis-backed pending registration store with in-memory impl
            ReplaceService<IPendingRegistrationStore>(services,
                _ => new InMemoryPendingRegistrationStore());

            // Replace event publisher/subscriber with in-memory implementations
            // that actually dispatch events end-to-end (required for SignalR tests)
            var inMemorySubscriber = new InMemoryEventSubscriber();
            var inMemoryPublisher = new InMemoryEventPublisher(inMemorySubscriber);
            ReplaceService<IEventPublisher>(services,
                _ => inMemoryPublisher);
            ReplaceService<IEventSubscriber>(services,
                _ => inMemorySubscriber);

            // Replace external service clients with mocks
            var mockValidatorClient = new Mock<IValidatorServiceClient>();
            mockValidatorClient
                .Setup(v => v.SubmitTransactionAsync(
                    It.IsAny<TransactionSubmission>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TransactionSubmissionResult
                {
                    Success = true,
                    TransactionId = "test-tx-placeholder",
                    RegisterId = "test-reg-placeholder",
                    AddedAt = DateTimeOffset.UtcNow
                });
            ReplaceService<IValidatorServiceClient>(services,
                _ => mockValidatorClient.Object);

            var mockPeerClient = new Mock<IPeerServiceClient>();
            ReplaceService<IPeerServiceClient>(services,
                _ => mockPeerClient.Object);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var envVar in EnvironmentVariables)
            {
                Environment.SetEnvironmentVariable(envVar, null);
            }
        }

        base.Dispose(disposing);
    }

    private static void ReplaceService<T>(IServiceCollection services, Func<IServiceProvider, T> factory)
        where T : class
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton(factory);
    }
}

/// <summary>
/// Test authentication handler that auto-authenticates all requests.
/// </summary>
internal class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user"),
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim("sub", "test-user"),
            new Claim("org_id", "test-org-001"),
            new Claim("token_type", "service")
        };

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// In-memory implementation of <see cref="ISystemRegisterRepository"/> for testing.
/// </summary>
internal class InMemorySystemRegisterRepository : ISystemRegisterRepository
{
    private readonly List<SystemRegisterEntry> _entries = [];
    private long _nextVersion = 1;

    public Task<List<SystemRegisterEntry>> GetAllBlueprintsAsync(CancellationToken ct = default)
        => Task.FromResult(_entries.Where(e => e.IsActive).ToList());

    public Task<SystemRegisterEntry?> GetBlueprintByIdAsync(string blueprintId, CancellationToken ct = default)
        => Task.FromResult(_entries.FirstOrDefault(e => e.BlueprintId == blueprintId && e.IsActive));

    public Task<List<SystemRegisterEntry>> GetBlueprintsSinceVersionAsync(long sinceVersion, CancellationToken ct = default)
        => Task.FromResult(_entries.Where(e => e.Version > sinceVersion).OrderBy(e => e.Version).ToList());

    public Task<SystemRegisterEntry> PublishBlueprintAsync(
        string blueprintId, BsonDocument doc, string publishedBy,
        Dictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var entry = new SystemRegisterEntry
        {
            BlueprintId = blueprintId, Document = doc, PublishedBy = publishedBy,
            Version = _nextVersion++, IsActive = true, PublishedAt = DateTime.UtcNow, Metadata = metadata
        };
        _entries.Add(entry);
        return Task.FromResult(entry);
    }

    public Task<long> GetLatestVersionAsync(CancellationToken ct = default)
        => Task.FromResult(_entries.Any() ? _entries.Max(e => e.Version) : 0L);

    public Task<bool> IsSystemRegisterInitializedAsync(CancellationToken ct = default)
        => Task.FromResult(_entries.Any());

    public Task<int> GetBlueprintCountAsync(CancellationToken ct = default)
        => Task.FromResult(_entries.Count(e => e.IsActive));

    public Task<bool> DeprecateBlueprintAsync(string blueprintId, CancellationToken ct = default)
    {
        var entry = _entries.FirstOrDefault(e => e.BlueprintId == blueprintId && e.IsActive);
        if (entry is null) return Task.FromResult(false);
        entry.IsActive = false;
        return Task.FromResult(true);
    }
}

/// <summary>
/// In-memory implementation of <see cref="IPendingRegistrationStore"/> for testing.
/// </summary>
internal class InMemoryPendingRegistrationStore : IPendingRegistrationStore
{
    private readonly ConcurrentDictionary<string, PendingRegistration> _store = new();

    public void Add(string registerId, PendingRegistration registration)
        => _store[registerId] = registration;

    public bool TryRemove(string registerId, out PendingRegistration? registration)
        => _store.TryRemove(registerId, out registration);

    public bool Exists(string registerId)
        => _store.ContainsKey(registerId);

    public void CleanupExpired()
    {
        // No-op for testing — in-memory store has no TTL mechanism
    }
}

