// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Wallet Service integration tests.
/// Configures test authentication and in-memory services.
/// </summary>
public class WalletServiceWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                // JWT Settings for authentication
                ["JwtSettings:Issuer"] = "https://test.sorcha.io",
                ["JwtSettings:Audiences:0"] = "https://test-api.sorcha.io",
                ["JwtSettings:SigningKey"] = "test-signing-key-for-integration-tests-minimum-32-characters-required",
                ["JwtSettings:AccessTokenLifetimeMinutes"] = "60",
                ["JwtSettings:RefreshTokenLifetimeHours"] = "24",
                ["JwtSettings:ServiceTokenLifetimeHours"] = "8",
                ["JwtSettings:ClockSkewMinutes"] = "5",
                ["JwtSettings:ValidateIssuer"] = "false",
                ["JwtSettings:ValidateAudience"] = "false",
                ["JwtSettings:ValidateIssuerSigningKey"] = "false",
                ["JwtSettings:ValidateLifetime"] = "false",
                // Disable PostgreSQL for in-memory testing
                ["ConnectionStrings:WalletDatabase"] = "",
                // Redis connection string (mocked, but config must parse)
                ["ConnectionStrings:redis"] = "localhost:6379"
            };

            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            // Remove Redis and use mock
            services.RemoveAll<IConnectionMultiplexer>();
            var mockMultiplexer = CreateMockRedis();
            services.AddSingleton(mockMultiplexer);

            // Remove all existing authentication schemes and handlers
            services.RemoveAll<IAuthenticationService>();
            services.RemoveAll<IAuthenticationHandlerProvider>();
            services.RemoveAll<IAuthenticationSchemeProvider>();

            // Add test authentication as the default scheme
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    private static IConnectionMultiplexer CreateMockRedis()
    {
        var mockDatabase = new Mock<IDatabase>();

        // String operations
        var stringStore = new ConcurrentDictionary<string, RedisValue>();

        mockDatabase
            .Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? _, When _, CommandFlags _) =>
            {
                stringStore[key.ToString()] = value;
                return true;
            });

        mockDatabase
            .Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) =>
                stringStore.TryGetValue(key.ToString(), out var value) ? value : RedisValue.Null);

        mockDatabase
            .Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags flags) =>
            {
                return stringStore.TryRemove(key.ToString(), out RedisValue _);
            });

        mockDatabase
            .Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) => stringStore.ContainsKey(key.ToString()));

        // Set operations
        var setStore = new ConcurrentDictionary<string, HashSet<RedisValue>>();

        mockDatabase
            .Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, CommandFlags _) =>
            {
                var set = setStore.GetOrAdd(key.ToString(), _ => new HashSet<RedisValue>());
                return set.Add(value);
            });

        mockDatabase
            .Setup(d => d.SetContainsAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, CommandFlags _) =>
            {
                return setStore.TryGetValue(key.ToString(), out var set) && set.Contains(value);
            });

        mockDatabase
            .Setup(d => d.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, CommandFlags _) =>
                setStore.TryGetValue(key.ToString(), out var set)
                    ? set.ToArray()
                    : Array.Empty<RedisValue>());

        // Sorted set operations
        var sortedSetStore = new ConcurrentDictionary<string, SortedDictionary<double, RedisValue>>();

        mockDatabase
            .Setup(d => d.SortedSetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<double>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisKey key, RedisValue value, double score, When _, CommandFlags _) =>
            {
                var sortedSet = sortedSetStore.GetOrAdd(key.ToString(), _ => new SortedDictionary<double, RedisValue>());
                sortedSet[score] = value;
                return true;
            });

        // Pub/Sub
        var mockSubscriber = new Mock<ISubscriber>();
        mockSubscriber
            .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        mockDatabase
            .Setup(d => d.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        // Multiplexer
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        mockMultiplexer.Setup(m => m.IsConnected).Returns(true);
        mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
        mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>())).Returns(mockSubscriber.Object);

        return mockMultiplexer.Object;
    }

    /// <summary>
    /// Creates an HttpClient configured for a regular authenticated user.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }

    /// <summary>
    /// Creates an HttpClient configured for an administrator user.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with no authentication headers.
    /// </summary>
    public HttpClient CreateUnauthenticatedClient()
    {
        return CreateClient();
    }

    /// <summary>
    /// Creates an HttpClient configured for a specific user.
    /// </summary>
    public HttpClient CreateClientForUser(string userId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", userId);
        return client;
    }
}

/// <summary>
/// Collection definition for shared test context.
/// </summary>
[CollectionDefinition("WalletService")]
public class WalletServiceCollection : ICollectionFixture<WalletServiceWebApplicationFactory>
{
}
