// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using MongoDB.Driver;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Peer;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Validator.Service.Services;
using StackExchange.Redis;

namespace Sorcha.Validator.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Validator Service integration tests.
/// Uses mock external services for isolated testing.
/// </summary>
public class ValidatorServiceWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly ConcurrentDictionary<string, object> _testData = new();

    /// <summary>
    /// Mock for the Register Service client
    /// </summary>
    public Mock<IRegisterServiceClient> RegisterClientMock { get; private set; } = null!;

    /// <summary>
    /// Mock for the Blueprint Service client
    /// </summary>
    public Mock<IBlueprintServiceClient> BlueprintClientMock { get; private set; } = null!;

    /// <summary>
    /// Mock for the Peer Service client
    /// </summary>
    public Mock<IPeerServiceClient> PeerClientMock { get; private set; } = null!;

    /// <summary>
    /// Mock for the Wallet Service client
    /// </summary>
    public Mock<IWalletServiceClient> WalletClientMock { get; private set; } = null!;

    /// <summary>
    /// In-memory transaction storage for tests
    /// </summary>
    public ConcurrentDictionary<string, List<TransactionModel>> TransactionStore { get; } = new();

    /// <summary>
    /// In-memory docket storage for tests
    /// </summary>
    public ConcurrentDictionary<string, List<DocketModel>> DocketStore { get; } = new();

    public ValidatorServiceWebApplicationFactory()
    {
        InitializeMocks();
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var testConfig = new Dictionary<string, string?>
            {
                // JWT Settings
                ["JwtSettings:Issuer"] = "https://test.sorcha.io",
                ["JwtSettings:Audiences:0"] = "https://test-api.sorcha.io",
                ["JwtSettings:SigningKey"] = "test-signing-key-for-integration-tests-minimum-32-characters-required",
                ["JwtSettings:ValidateIssuer"] = "false",
                ["JwtSettings:ValidateAudience"] = "false",
                ["JwtSettings:ValidateIssuerSigningKey"] = "false",
                ["JwtSettings:ValidateLifetime"] = "false",

                // Validator Configuration
                ["Validator:ValidatorId"] = "test-validator-id",
                ["Validator:SystemWalletAddress"] = "test-system-wallet",

                // Consensus Configuration
                ["Consensus:MinSignatures"] = "1",
                ["Consensus:MaxSignatures"] = "10",
                ["Consensus:Timeout"] = "00:00:30",

                // MemPool Configuration
                ["MemPool:MaxSize"] = "1000",
                ["MemPool:CleanupInterval"] = "00:01:00",

                // DocketBuild Configuration
                ["DocketBuild:TimeThreshold"] = "00:00:10",
                ["DocketBuild:SizeThreshold"] = "50",
                ["DocketBuild:MaxTransactionsPerDocket"] = "100",
                ["DocketBuild:AllowEmptyDockets"] = "false",

                // WalletService Configuration
                ["WalletService:Endpoint"] = "http://localhost:5001",

                // Genesis Config Cache
                ["GenesisConfigCache:KeyPrefix"] = "test:genesis:",
                ["GenesisConfigCache:DefaultTtl"] = "00:30:00",
                ["GenesisConfigCache:EnableLocalCache"] = "true",

                // Validator Registry
                ["ValidatorRegistry:KeyPrefix"] = "test:validators:",
                ["ValidatorRegistry:CacheTtl"] = "00:05:00",

                // MongoDB (mocked, but config must parse)
                ["RegisterStorage:MongoDB:ConnectionString"] = "mongodb://localhost:27017",
                ["RegisterStorage:MongoDB:DatabaseName"] = "sorcha_test"
            };

            config.AddInMemoryCollection(testConfig);
        });

        builder.ConfigureServices(services =>
        {
            // Remove Redis and use mock
            services.RemoveAll<IConnectionMultiplexer>();
            var mockMultiplexer = CreateMockRedis();
            services.AddSingleton(mockMultiplexer);

            // Remove gRPC channel and use mock
            services.RemoveAll<GrpcChannel>();

            // Replace service clients with mocks
            services.RemoveAll<IRegisterServiceClient>();
            services.AddScoped(_ => RegisterClientMock.Object);

            services.RemoveAll<IBlueprintServiceClient>();
            services.AddScoped(_ => BlueprintClientMock.Object);

            services.RemoveAll<IPeerServiceClient>();
            services.AddScoped(_ => PeerClientMock.Object);

            services.RemoveAll<IWalletServiceClient>();
            services.AddScoped(_ => WalletClientMock.Object);

            // Replace MongoDB with mocks to avoid requiring a real MongoDB connection
            services.RemoveAll<IMongoClient>();
            services.AddSingleton(new Mock<IMongoClient>().Object);
            services.RemoveAll<IReadOnlyRegisterRepository>();
            services.AddSingleton(new Mock<IReadOnlyRegisterRepository>().Object);

            // Remove all existing authentication
            services.RemoveAll<IAuthenticationService>();
            services.RemoveAll<IAuthenticationHandlerProvider>();
            services.RemoveAll<IAuthenticationSchemeProvider>();

            // Add test authentication
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            // Remove hosted services that might cause issues in tests
            services.RemoveAll<IHostedService>();
        });
    }

    private void InitializeMocks()
    {
        RegisterClientMock = new Mock<IRegisterServiceClient>();
        BlueprintClientMock = new Mock<IBlueprintServiceClient>();
        PeerClientMock = new Mock<IPeerServiceClient>();
        WalletClientMock = new Mock<IWalletServiceClient>();

        SetupDefaultMockBehavior();
    }

    private void SetupDefaultMockBehavior()
    {
        // Register client defaults
        RegisterClientMock
            .Setup(r => r.GetRegisterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string registerId, CancellationToken _) => new Sorcha.Register.Models.Register
            {
                Id = registerId,
                Name = $"Test Register {registerId}",
                TenantId = "test-tenant",
                CreatedAt = DateTime.UtcNow,
                Status = Sorcha.Register.Models.Enums.RegisterStatus.Online
            });

        RegisterClientMock
            .Setup(r => r.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string registerId, int page, int pageSize, CancellationToken _) =>
            {
                var transactions = TransactionStore.GetValueOrDefault(registerId, new List<TransactionModel>());
                return new TransactionPage
                {
                    Transactions = transactions.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                    Page = page,
                    PageSize = pageSize,
                    Total = transactions.Count
                };
            });

        RegisterClientMock
            .Setup(r => r.ReadDocketAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string registerId, int docketNumber, CancellationToken _) =>
            {
                var dockets = DocketStore.GetValueOrDefault(registerId, new List<DocketModel>());
                return dockets.FirstOrDefault(d => d.DocketNumber == docketNumber);
            });

        RegisterClientMock
            .Setup(r => r.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocketModel docket, CancellationToken _) =>
            {
                var dockets = DocketStore.GetOrAdd(docket.RegisterId, _ => new List<DocketModel>());
                dockets.Add(docket);
                return true;
            });

        // Blueprint client defaults - returns JSON string
        BlueprintClientMock
            .Setup(b => b.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string blueprintId, CancellationToken _) =>
                $"{{\"id\":\"{blueprintId}\",\"title\":\"Test Blueprint {blueprintId}\",\"version\":\"1.0.0\"}}");

        BlueprintClientMock
            .Setup(b => b.ValidatePayloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Peer client defaults
        PeerClientMock
            .Setup(p => p.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sorcha.ServiceClients.Peer.ValidatorInfo>
            {
                new()
                {
                    ValidatorId = "test-validator-id",
                    GrpcEndpoint = "http://localhost:7004",
                    ReputationScore = 1.0,
                    IsActive = true
                }
            });

        PeerClientMock
            .Setup(p => p.PublishProposedDocketAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        PeerClientMock
            .Setup(p => p.BroadcastConfirmedDocketAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Wallet client defaults
        WalletClientMock
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string walletAddress, string data, CancellationToken _) => new WalletSignResult
            {
                Signature = System.Text.Encoding.UTF8.GetBytes("test-signature"),
                PublicKey = System.Text.Encoding.UTF8.GetBytes(walletAddress),
                SignedBy = walletAddress,
                Algorithm = "ED25519"
            });

        WalletClientMock
            .Setup(w => w.CreateOrRetrieveSystemWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-system-wallet");
    }

    private IConnectionMultiplexer CreateMockRedis()
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
    /// Creates an HttpClient configured for a validator.
    /// </summary>
    public HttpClient CreateValidatorClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }

    /// <summary>
    /// Creates an HttpClient configured for an administrator.
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
    /// Seeds test data for a register.
    /// </summary>
    public void SeedRegisterData(string registerId, List<TransactionModel>? transactions = null, List<DocketModel>? dockets = null)
    {
        if (transactions != null)
        {
            TransactionStore[registerId] = transactions;
        }

        if (dockets != null)
        {
            DocketStore[registerId] = dockets;
        }
    }

    /// <summary>
    /// Clears all test data.
    /// </summary>
    public void ClearTestData()
    {
        TransactionStore.Clear();
        DocketStore.Clear();
        _testData.Clear();
    }
}

/// <summary>
/// Collection definition for shared test context.
/// </summary>
[CollectionDefinition("ValidatorService")]
public class ValidatorServiceCollection : ICollectionFixture<ValidatorServiceWebApplicationFactory>
{
}
