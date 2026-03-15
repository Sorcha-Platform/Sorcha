// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for SystemRegisterBootstrapper with the ledger-backed SystemRegisterService.
/// </summary>
public class SystemRegisterBootstrapTests
{
    private readonly Mock<IRegisterRepository> _mockRepository;
    private readonly Mock<IValidatorServiceClient> _mockValidatorClient;
    private readonly Mock<ISystemWalletSigningService> _mockSigningService;
    private readonly Mock<IHashProvider> _mockHashProvider;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<IRegisterCreationOrchestrator> _mockOrchestrator;
    private readonly Mock<ILogger<SystemRegisterService>> _mockServiceLogger;
    private readonly Mock<ILogger<SystemRegisterBootstrapper>> _mockBootstrapLogger;
    private readonly Mock<IEventPublisher> _mockEventPublisher;

    private const string TestWalletAddress = "test-system-wallet-address";
    private const string TestValidatorId = "test-validator-001";

    public SystemRegisterBootstrapTests()
    {
        _mockRepository = new Mock<IRegisterRepository>();
        _mockValidatorClient = new Mock<IValidatorServiceClient>();
        _mockSigningService = new Mock<ISystemWalletSigningService>();
        _mockHashProvider = new Mock<IHashProvider>();
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockOrchestrator = new Mock<IRegisterCreationOrchestrator>();
        _mockServiceLogger = new Mock<ILogger<SystemRegisterService>>();
        _mockBootstrapLogger = new Mock<ILogger<SystemRegisterBootstrapper>>();
        _mockEventPublisher = new Mock<IEventPublisher>();

        // Default: system register does NOT exist (not initialized)
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        // Default: no transactions
        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionModel>().AsQueryable());

        // Default: wallet client returns a known address
        _mockWalletClient
            .Setup(w => w.CreateOrRetrieveSystemWalletAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestWalletAddress);
    }

    private SystemRegisterService CreateService()
    {
        var registerManager = new RegisterManager(_mockRepository.Object, _mockEventPublisher.Object);
        var transactionManager = new TransactionManager(_mockRepository.Object, _mockEventPublisher.Object);

        return new SystemRegisterService(
            _mockServiceLogger.Object,
            registerManager,
            transactionManager,
            _mockValidatorClient.Object,
            _mockSigningService.Object,
            _mockHashProvider.Object);
    }

    private SystemRegisterBootstrapper CreateBootstrapper()
    {
        var registerManager = new RegisterManager(_mockRepository.Object, _mockEventPublisher.Object);
        var systemRegisterService = CreateService();
        var signingOptions = new SystemWalletSigningOptions { ValidatorId = TestValidatorId };

        // Build a service collection for the IServiceScopeFactory
        var services = new ServiceCollection();
        services.AddSingleton(registerManager);
        services.AddSingleton<IRegisterCreationOrchestrator>(_mockOrchestrator.Object);
        services.AddSingleton<ISystemWalletSigningService>(_mockSigningService.Object);
        services.AddSingleton<IWalletServiceClient>(_mockWalletClient.Object);
        services.AddSingleton(systemRegisterService);
        services.AddSingleton(signingOptions);

        var serviceProvider = services.BuildServiceProvider();

        return new SystemRegisterBootstrapper(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _mockBootstrapLogger.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRegisterExistsWithBlueprints_SkipsCreationAndSeeding()
    {
        // Arrange — register exists with height > 0 and blueprint transactions
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Name = SystemRegisterConstants.SystemRegisterName,
                Height = 2,
                TenantId = "system"
            });

        var transactions = new List<TransactionModel>
        {
            CreateBlueprintTransaction("register-creation-v1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CreateBlueprintTransaction("register-governance-v1", new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc))
        };

        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        var bootstrapper = CreateBootstrapper();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert — no register creation attempted
        _mockOrchestrator.Verify(o => o.InitiateAsync(
            It.IsAny<InitiateRegisterCreationRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OnException_DoesNotCrashHost()
    {
        // Arrange — GetRegisterAsync throws on every call
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("MongoDB connection failed"));

        var bootstrapper = CreateBootstrapper();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        // Act — should NOT throw
        Func<Task> act = async () =>
        {
            await bootstrapper.StartAsync(cts.Token);
            await bootstrapper.StopAsync(cts.Token);
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_WhenRegisterMissing_InitiatesAndFinalizes()
    {
        // Arrange — register does not exist
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        // Set up orchestrator to return a valid initiation response
        var attestationData = new AttestationSigningData
        {
            Role = RegisterRole.Owner,
            Subject = $"did:sorcha:w:{TestWalletAddress}",
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            RegisterName = SystemRegisterConstants.SystemRegisterName,
            GrantedAt = DateTimeOffset.UtcNow
        };

        var initiateResponse = new InitiateRegisterCreationResponse
        {
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            AttestationsToSign = new List<AttestationToSign>
            {
                new AttestationToSign
                {
                    UserId = "system",
                    WalletId = TestWalletAddress,
                    Role = RegisterRole.Owner,
                    AttestationData = attestationData,
                    DataToSign = new string('a', 64) // hex hash
                }
            },
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            Nonce = "test-nonce"
        };

        _mockOrchestrator
            .Setup(o => o.InitiateAsync(It.IsAny<InitiateRegisterCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(initiateResponse);

        // Signing returns a valid result
        _mockSigningService
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[] { 1, 2, 3, 4 },
                PublicKey = new byte[] { 5, 6, 7, 8 },
                Algorithm = "ED25519",
                WalletAddress = TestWalletAddress
            });

        // Finalize returns success
        _mockOrchestrator
            .Setup(o => o.FinalizeAsync(It.IsAny<FinalizeRegisterCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinalizeRegisterCreationResponse
            {
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                Status = "created",
                GenesisTransactionId = new string('b', 64),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // After creation, register exists with height > 0 (genesis docket processed)
        // Use a sequence: first call returns null (check), subsequent calls return the register
        var callCount = 0;
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount <= 1)
                    return null; // First call: register doesn't exist yet
                return new Sorcha.Register.Models.Register
                {
                    Id = SystemRegisterConstants.SystemRegisterId,
                    Name = SystemRegisterConstants.SystemRegisterName,
                    Height = 1,
                    TenantId = "system"
                };
            });

        var bootstrapper = CreateBootstrapper();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert — initiation was called with correct register ID
        _mockOrchestrator.Verify(o => o.InitiateAsync(
            It.Is<InitiateRegisterCreationRequest>(r =>
                r.RegisterId == SystemRegisterConstants.SystemRegisterId &&
                r.Name == SystemRegisterConstants.SystemRegisterName &&
                r.TenantId == "system"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Finalization was called
        _mockOrchestrator.Verify(o => o.FinalizeAsync(
            It.Is<FinalizeRegisterCreationRequest>(r =>
                r.RegisterId == SystemRegisterConstants.SystemRegisterId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSystemRegisterInfoAsync_ReturnsCorrectInfo()
    {
        // Arrange — register exists with blueprints
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Name = SystemRegisterConstants.SystemRegisterName,
                Height = 5,
                TenantId = "system",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        var transactions = new List<TransactionModel>
        {
            CreateBlueprintTransaction("bp-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CreateBlueprintTransaction("bp-2", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc))
        };

        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());

        var service = CreateService();

        // Act
        var info = await service.GetSystemRegisterInfoAsync();

        // Assert
        info.RegisterId.Should().Be("aebf26362e079087571ac0932d4db973");
        info.Name.Should().Be("Sorcha System Register");
        info.Status.Should().Be("initialized");
        info.BlueprintCount.Should().Be(2);
        info.CurrentVersion.Should().Be(2);
        info.Height.Should().Be(5);
        info.CreatedAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetSystemRegisterInfoAsync_WhenNotInitialized_ReturnsNotInitializedStatus()
    {
        // Arrange — register does not exist
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        var service = CreateService();

        // Act
        var info = await service.GetSystemRegisterInfoAsync();

        // Assert
        info.Status.Should().Be("not_initialized");
        info.BlueprintCount.Should().Be(0);
        info.CurrentVersion.Should().Be(0);
        info.CreatedAt.Should().BeNull();
    }

    /// <summary>
    /// Creates a mock blueprint publish transaction for testing
    /// </summary>
    private static TransactionModel CreateBlueprintTransaction(string blueprintId, DateTime timestamp)
    {
        return new TransactionModel
        {
            TxId = $"tx-{blueprintId}-{timestamp.Ticks}".PadRight(64, '0')[..64],
            RegisterId = SystemRegisterConstants.SystemRegisterId,
            SenderWallet = "system",
            TimeStamp = timestamp,
            MetaData = new TransactionMetaData
            {
                RegisterId = SystemRegisterConstants.SystemRegisterId,
                TransactionType = Sorcha.Register.Models.Enums.TransactionType.Control,
                BlueprintId = blueprintId,
                TrackingData = new Dictionary<string, string>
                {
                    ["transactionType"] = "BlueprintPublish",
                    ["BlueprintId"] = blueprintId,
                    ["publishedBy"] = "system"
                }
            },
            PayloadCount = 0,
            Payloads = [],
            Signature = "system-signature"
        };
    }
}
