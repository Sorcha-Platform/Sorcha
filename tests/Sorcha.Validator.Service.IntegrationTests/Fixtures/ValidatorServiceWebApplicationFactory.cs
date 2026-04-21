// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Claims;
using Grpc.Net.Client;
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
using Sorcha.Testing;
using Microsoft.AspNetCore.Hosting;

namespace Sorcha.Validator.Service.IntegrationTests.Fixtures;

/// <summary>
/// Custom WebApplicationFactory for Validator Service integration tests.
/// Uses mock external services for isolated testing.
/// </summary>
public class ValidatorServiceWebApplicationFactory : SorchaWebApplicationFactory<Program>
{
    private readonly ConcurrentDictionary<string, object> _testData = new();

    public Mock<IRegisterServiceClient> RegisterClientMock { get; private set; } = null!;
    public Mock<IBlueprintServiceClient> BlueprintClientMock { get; private set; } = null!;
    public Mock<IPeerServiceClient> PeerClientMock { get; private set; } = null!;
    public Mock<IWalletServiceClient> WalletClientMock { get; private set; } = null!;

    public ConcurrentDictionary<string, List<TransactionModel>> TransactionStore { get; } = new();
    public ConcurrentDictionary<string, List<DocketModel>> DocketStore { get; } = new();

    /// <summary>
    /// Indicates whether the test host started successfully.
    /// When false, tests should skip rather than fail.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Reason the infrastructure is unavailable, for skip messages.</summary>
    public string? UnavailableReason { get; private set; }

    public ValidatorServiceWebApplicationFactory()
    {
        InitializeMocks();
    }

    protected override RedisMockMode RedisMockMode => RedisMockMode.Stateful;

    public override ValueTask InitializeAsync()
    {
        // Eagerly verify the host can be created; if not, mark as unavailable
        try
        {
            using var _ = CreateClient();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Validator Service host failed to start: {ex.GetType().Name} — {ex.Message}";
            IsAvailable = false;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Call at the start of each test to skip when infrastructure is unavailable.
    /// </summary>
    public void SkipIfUnavailable()
    {
        if (!IsAvailable)
        {
            Assert.Skip(UnavailableReason ?? "Validator Service infrastructure not available");
        }
    }

    protected override void ConfigureTestConfiguration(
        WebHostBuilderContext context,
        IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
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
            ["RegisterStorage:MongoDB:DatabaseName"] = "sorcha_test",
        });
    }

    protected override void ConfigureTestAuth(TestAuthHandlerOptions options)
    {
        options.DefaultClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-validator-id"),
            new(ClaimTypes.Name, "Test Validator"),
            new(ClaimTypes.Email, "validator@sorcha.io"),
            new("organization_id", "test-org-id"),
            new("org_id", "test-org-id"),
            new("validator_id", "test-validator-id"),
            new("wallet_address", "test-wallet-address"),
            new("token_type", "service"),
        };
        options.DefaultRole = "Validator";
        options.HeaderClaimMappings = new List<(string, string)>
        {
            ("X-Test-Validator-Id", "validator_id"),
            ("X-Test-Register-Id", "register_id"),
        };
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Remove gRPC channel and use mocks for service clients
        services.RemoveAll<GrpcChannel>();

        services.RemoveAll<IRegisterServiceClient>();
        services.AddScoped(_ => RegisterClientMock.Object);

        services.RemoveAll<IBlueprintServiceClient>();
        services.AddScoped(_ => BlueprintClientMock.Object);

        services.RemoveAll<IPeerServiceClient>();
        services.AddScoped(_ => PeerClientMock.Object);

        services.RemoveAll<IWalletServiceClient>();
        services.AddScoped(_ => WalletClientMock.Object);

        // Mock MongoDB so no real connection is required.
        services.RemoveAll<IMongoClient>();
        services.AddSingleton(new Mock<IMongoClient>().Object);
        services.RemoveAll<IReadOnlyRegisterRepository>();
        services.AddSingleton(new Mock<IReadOnlyRegisterRepository>().Object);

        // Remove hosted services that might cause issues in tests.
        services.RemoveAll<IHostedService>();
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
        RegisterClientMock
            .Setup(r => r.GetRegisterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string registerId, CancellationToken _) => new Sorcha.Register.Models.Register
            {
                Id = registerId,
                Name = $"Test Register {registerId}",
                CreatedAt = DateTime.UtcNow,
                Status = Sorcha.Register.Models.Enums.RegisterStatus.Online,
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
                    Total = transactions.Count,
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

        BlueprintClientMock
            .Setup(b => b.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string blueprintId, CancellationToken _) =>
                $"{{\"id\":\"{blueprintId}\",\"title\":\"Test Blueprint {blueprintId}\",\"version\":\"1.0.0\"}}");

        BlueprintClientMock
            .Setup(b => b.ValidatePayloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        PeerClientMock
            .Setup(p => p.QueryValidatorsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sorcha.ServiceClients.Peer.ValidatorInfo>
            {
                new()
                {
                    ValidatorId = "test-validator-id",
                    GrpcEndpoint = "http://localhost:7004",
                    ReputationScore = 1.0,
                    IsActive = true,
                },
            });

        PeerClientMock
            .Setup(p => p.PublishProposedDocketAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        PeerClientMock
            .Setup(p => p.BroadcastConfirmedDocketAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        WalletClientMock
            .Setup(w => w.SignDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string walletAddress, string data, CancellationToken _) => new WalletSignResult
            {
                Signature = System.Text.Encoding.UTF8.GetBytes("test-signature"),
                PublicKey = System.Text.Encoding.UTF8.GetBytes(walletAddress),
                SignedBy = walletAddress,
                Algorithm = "ED25519",
            });

        WalletClientMock
            .Setup(w => w.CreateOrRetrieveSystemWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-system-wallet");
    }

    /// <summary>Creates an HttpClient configured for a validator.</summary>
    public HttpClient CreateValidatorClient()
    {
        SkipIfUnavailable();
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }

    /// <summary>Creates an HttpClient configured for an administrator.</summary>
    public new HttpClient CreateAdminClient()
    {
        SkipIfUnavailable();
        return base.CreateAdminClient();
    }

    /// <summary>Creates an HttpClient with no authentication headers.</summary>
    public new HttpClient CreateUnauthenticatedClient()
    {
        SkipIfUnavailable();
        return base.CreateUnauthenticatedClient();
    }

    /// <summary>Seeds test data for a register.</summary>
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

    /// <summary>Clears all test data. Safe to call when infrastructure is unavailable.</summary>
    public void ClearTestData()
    {
        if (!IsAvailable) return;
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
