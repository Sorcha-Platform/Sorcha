// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Moq;
using Sorcha.Register.Service.Services;
using StackExchange.Redis;

namespace Sorcha.Register.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Register Service integration tests.
/// Uses in-memory storage and mocked Redis/MongoDB for fast testing.
/// </summary>
public class RegisterServiceWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting applies configuration before Program.cs builder code runs,
        // which is critical for settings read during the builder phase (e.g. AddSystemWalletSigning)
        builder.UseSetting("RegisterStorage:Type", "InMemory");
        builder.UseSetting("ConnectionStrings:MongoDB", "");
        builder.UseSetting("SystemWalletSigning:ValidatorId", "test-validator-id");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                // Use InMemory storage (default)
                ["RegisterStorage:Type"] = "InMemory",

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

                // Disable MongoDB connection string (use mock)
                ["ConnectionStrings:MongoDB"] = "",

                // System wallet signing configuration (required by AddSystemWalletSigning)
                ["SystemWalletSigning:ValidatorId"] = "test-validator-id",
            };

            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            // === Mock Redis ===
            // Register Service uses Redis for event streams, pending registrations, and caching
            services.RemoveAll<IConnectionMultiplexer>();

            var mockDatabase = new Mock<IDatabase>();
            mockDatabase.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            mockDatabase.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisValue.Null);
            mockDatabase.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            mockDatabase.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            mockDatabase.Setup(d => d.SetContainsAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(false);
            mockDatabase.Setup(d => d.PublishAsync(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(0);

            var mockSubscriber = new Mock<ISubscriber>();
            mockSubscriber.Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Returns(Task.CompletedTask);

            var mockMultiplexer = new Mock<IConnectionMultiplexer>();
            mockMultiplexer.Setup(m => m.IsConnected).Returns(true);
            mockMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);
            mockMultiplexer.Setup(m => m.GetSubscriber(It.IsAny<object>()))
                .Returns(mockSubscriber.Object);

            services.AddSingleton(mockMultiplexer.Object);

            // === Mock MongoDB ===
            // SystemRegisterService now uses RegisterManager/TransactionManager (backed by IRegisterRepository)
            // so no MongoDB mocking is needed for the system register.
            // Provide mock IMongoClient/IMongoDatabase so any remaining DI doesn't fail.
            services.RemoveAll<IMongoClient>();
            services.RemoveAll<IMongoDatabase>();

            var mockMongoDatabase = new Mock<IMongoDatabase>();
            var mockMongoClient = new Mock<IMongoClient>();
            mockMongoClient.Setup(c => c.GetDatabase(It.IsAny<string>(), It.IsAny<MongoDatabaseSettings>()))
                .Returns(mockMongoDatabase.Object);

            services.AddSingleton<IMongoClient>(mockMongoClient.Object);
            services.AddSingleton<IMongoDatabase>(mockMongoDatabase.Object);

            // === Replace Authentication ===
            services.RemoveAll<IAuthenticationService>();
            services.RemoveAll<IAuthenticationHandlerProvider>();
            services.RemoveAll<IAuthenticationSchemeProvider>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>
    /// Creates an HttpClient configured for a regular authenticated user.
    /// The test user has an org_id claim, which satisfies the CanManageRegisters policy.
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
    /// Creates an HttpClient configured as a service token (for CanWriteDockets policy).
    /// </summary>
    public HttpClient CreateServiceClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        client.DefaultRequestHeaders.Add("X-Test-Token-Type", "service");
        return client;
    }
}

/// <summary>
/// Collection definition for shared test context.
/// </summary>
[CollectionDefinition("RegisterService")]
public class RegisterServiceCollection : ICollectionFixture<RegisterServiceWebApplicationFactory>
{
}
