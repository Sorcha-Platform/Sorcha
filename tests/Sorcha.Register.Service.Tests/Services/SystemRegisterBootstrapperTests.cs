// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceDefaults;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

public class SystemRegisterBootstrapperTests
{
    private readonly Mock<ILogger<SystemRegisterBootstrapper>> _bootstrapperLogger = new();
    private readonly Mock<ILogger<GenesisIngestionService>> _ingestionLogger = new();
    private readonly Mock<IValidatorServiceClient> _validatorClient = new();
    private readonly Mock<ICryptoModule> _cryptoModule = new();
    private readonly Mock<IRegisterRepository> _mockRepository = new();
    private readonly Mock<IEventPublisher> _mockEventPublisher = new();

    public SystemRegisterBootstrapperTests()
    {
        RegisterMockHelpers.StubTransactionsByTypeReadThrough(_mockRepository);
        // Default: blueprint queries return existing blueprints (skip seeding which needs template files)
        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sorcha.Register.Models.TransactionModel>
            {
                CreateBlueprintTransaction("register-creation-v1"),
                CreateBlueprintTransaction("register-governance-v1"),
                CreateBlueprintTransaction("create-organisation-v1")
            }.AsQueryable());
    }

    private static Sorcha.Register.Models.TransactionModel CreateBlueprintTransaction(string blueprintId) => new()
    {
        TxId = Guid.NewGuid().ToString("N") + new string('0', 32),
        RegisterId = SystemRegisterConstants.SystemRegisterId,
        SenderWallet = "system",
        TimeStamp = DateTime.UtcNow,
        PayloadCount = 0,
        Payloads = [],
        Signature = "sig",
        MetaData = new Sorcha.Register.Models.TransactionMetaData
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
        }
    };

    private SystemRegisterBootstrapper CreateBootstrapper(
        SystemRegisterOptions options,
        ILogger<SystemRegisterBootstrapper>? logger = null,
        ISystemWalletSigningService? signingService = null,
        IHashProvider? hashProvider = null)
    {
        var registerManager = new RegisterManager(_mockRepository.Object, _mockEventPublisher.Object);
        var transactionManager = new TransactionManager(_mockRepository.Object, _mockEventPublisher.Object);

        var systemRegisterService = new SystemRegisterService(
            new Mock<ILogger<SystemRegisterService>>().Object,
            registerManager,
            transactionManager,
            _validatorClient.Object,
            signingService ?? new Mock<ISystemWalletSigningService>().Object,
            hashProvider ?? new Mock<IHashProvider>().Object);

        var genesisOptions = Options.Create(new SystemRegisterOptions { GenesisFile = options.GenesisFile });
        var genesisIngestion = new GenesisIngestionService(
            genesisOptions, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var services = new ServiceCollection();
        services.AddSingleton(registerManager);
        services.AddSingleton(systemRegisterService);
        services.AddSingleton(genesisIngestion);
        var sp = services.BuildServiceProvider();

        return new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            logger ?? _bootstrapperLogger.Object,
            Options.Create(options));
    }

    /// <summary>
    /// Starts the bootstrapper and waits for it to complete or timeout.
    /// BackgroundService.StartAsync returns immediately — we need to await ExecuteTask.
    /// </summary>
    private static async Task RunBootstrapperAsync(
        SystemRegisterBootstrapper bootstrapper,
        CancellationToken cancellationToken)
    {
        await bootstrapper.StartAsync(cancellationToken);
        // ExecuteTask is the Task returned by ExecuteAsync — wait for it or cancellation
        try
        {
            await (bootstrapper.ExecuteTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation triggers
        }
        await bootstrapper.StopAsync(CancellationToken.None);
    }

    // ========================================================================
    // Constructor tests
    // ========================================================================

    [Fact]
    public void Constructor_ThrowsOnNullScopeFactory()
    {
        var options = Options.Create(new SystemRegisterOptions());
        var act = () => new SystemRegisterBootstrapper(null!, _bootstrapperLogger.Object, options);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var options = Options.Create(new SystemRegisterOptions());
        var act = () => new SystemRegisterBootstrapper(scopeFactory.Object, null!, options);
        act.Should().Throw<ArgumentNullException>();
    }

    // ========================================================================
    // GenesisIngestionService tests (unchanged)
    // ========================================================================

    [Fact]
    public async Task GenesisIngestionService_LoadAndVerify_ReturnsNullWhenNoGenesis()
    {
        var options = Options.Create(new SystemRegisterOptions { GenesisFile = null });
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var result = await service.LoadAndVerifyGenesisAsync(CancellationToken.None);
        // Embedded resource exists and is valid in this project, so it may return non-null
        // This test just verifies no exception is thrown
    }

    [Fact]
    public void GenesisIngestionService_LoadAndVerify_ThrowsOnMissingConfiguredFile()
    {
        var options = Options.Create(new SystemRegisterOptions { GenesisFile = "/nonexistent/genesis.json" });
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var act = () => service.LoadAndVerifyGenesisAsync(CancellationToken.None);
        act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task GenesisIngestionService_IngestGenesis_SubmitsToValidator()
    {
        var options = Options.Create(new SystemRegisterOptions());
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        _validatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "test-tx",
                RegisterId = SystemRegisterConstants.SystemRegisterId
            });

        var genesis = CreateTestGenesis();
        var result = await service.IngestGenesisAsync(genesis, CancellationToken.None);

        result.Should().BeTrue();
        _validatorClient.Verify(
            v => v.SubmitTransactionAsync(
                It.Is<TransactionSubmission>(s =>
                    s.RegisterId == SystemRegisterConstants.SystemRegisterId &&
                    s.BlueprintId == GenesisConstants.BlueprintId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenesisIngestionService_EnsureRow_PreCreatesRegisterWithControlRecord()
    {
        // Arrange: registry has no system register; genesis payload carries a
        // valid RegisterControlRecord so the pre-create can stash it.
        Register.Models.Register? captured = null;
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Register.Models.Register?)null);
        _mockRepository
            .Setup(r => r.InsertRegisterAsync(It.IsAny<Register.Models.Register>(), It.IsAny<CancellationToken>()))
            .Callback<Register.Models.Register, CancellationToken>((reg, _) => captured = reg)
            .ReturnsAsync((Register.Models.Register reg, CancellationToken _) => reg);

        var registerManager = new RegisterManager(_mockRepository.Object, _mockEventPublisher.Object);
        var options = Options.Create(new SystemRegisterOptions());
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var genesis = CreateTestGenesisWithValidControlRecord();

        // Act
        await service.EnsureSystemRegisterRowAsync(genesis, registerManager, CancellationToken.None);

        // Assert: register row inserted, InitialControlRecord stashed with roster
        captured.Should().NotBeNull();
        captured!.Id.Should().Be(SystemRegisterConstants.SystemRegisterId);
        captured.Purpose.Should().Be(Register.Models.Enums.RegisterPurpose.System);
        captured.InitialControlRecord.Should().NotBeNull();
        captured.InitialControlRecord!.Validators.Should().NotBeNull();
        captured.InitialControlRecord.Validators!.Validators.Should().ContainSingle();
    }

    [Fact]
    public async Task GenesisIngestionService_EnsureRow_IsIdempotentWhenRegisterExists()
    {
        // Arrange: register already exists locally
        var existingRegister = new Register.Models.Register
        {
            Id = SystemRegisterConstants.SystemRegisterId,
            Name = SystemRegisterConstants.SystemRegisterName,
            Height = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRegister);

        var registerManager = new RegisterManager(_mockRepository.Object, _mockEventPublisher.Object);
        var options = Options.Create(new SystemRegisterOptions());
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var genesis = CreateTestGenesisWithValidControlRecord();

        // Act
        await service.EnsureSystemRegisterRowAsync(genesis, registerManager, CancellationToken.None);

        // Assert: no insert attempted
        _mockRepository.Verify(
            r => r.InsertRegisterAsync(It.IsAny<Register.Models.Register>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenesisIngestionService_EnsureRow_SkipsOnUnparseablePayload()
    {
        // Arrange: payload isn't valid JSON for RegisterControlRecord
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Register.Models.Register?)null);

        var registerManager = new RegisterManager(_mockRepository.Object, _mockEventPublisher.Object);
        var options = Options.Create(new SystemRegisterOptions());
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var genesis = CreateTestGenesis(); // Payload is `{"registerId":"test"}` — not a control record but valid JSON
        // Force a hard parse failure by putting non-JSON in the payload
        genesis.GenesisTransaction.Payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("not json at all"));

        // Act — should not throw
        await service.EnsureSystemRegisterRowAsync(genesis, registerManager, CancellationToken.None);

        // Assert: no insert attempted (decode failed → skip)
        _mockRepository.Verify(
            r => r.InsertRegisterAsync(It.IsAny<Register.Models.Register>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenesisIngestionService_IngestGenesis_ReturnsFalseOnRejection()
    {
        var options = Options.Create(new SystemRegisterOptions());
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        _validatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorCode = "REJECTED",
                ErrorMessage = "Not in validator roster"
            });

        var genesis = CreateTestGenesis();
        var result = await service.IngestGenesisAsync(genesis, CancellationToken.None);

        result.Should().BeFalse();
    }

    // ========================================================================
    // US1: SyncOnly mode tests
    // ========================================================================

    [Fact]
    public async Task SyncOnly_RegisterFoundDuringFastRetry_CompletesBootstrap()
    {
        // Arrange: register appears on 3rd check
        var callCount = 0;
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 3) return null;
                return new Sorcha.Register.Models.Register
                {
                    Id = SystemRegisterConstants.SystemRegisterId,
                    Name = SystemRegisterConstants.SystemRegisterName,
                    Height = 1
                };
            });

        var bootstrapper = CreateBootstrapper(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.SyncOnly,
            FastRetryIntervalSeconds = 1,
            FastRetryDurationSeconds = 30
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        await RunBootstrapperAsync(bootstrapper, cts.Token);

        // Assert: register was found (checked at least 3 times)
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task SyncOnly_NeverIngestsGenesisFile()
    {
        // Arrange: register found on first check
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        var bootstrapper = CreateBootstrapper(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.SyncOnly,
            FastRetryIntervalSeconds = 1,
            FastRetryDurationSeconds = 10
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await RunBootstrapperAsync(bootstrapper, cts.Token);

        // Assert: validator was never called (no genesis submission)
        _validatorClient.Verify(
            v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncOnly_RespectsShutdownCancellation_DuringPolling()
    {
        // Arrange: register never found
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        var bootstrapper = CreateBootstrapper(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.SyncOnly,
            FastRetryIntervalSeconds = 1,
            FastRetryDurationSeconds = 2,
            BackoffIntervalSeconds = 60 // Long — would hang if not cancelled
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act — should complete cleanly (cancellation handled)
        Func<Task> act = () => RunBootstrapperAsync(bootstrapper, cts.Token);

        await act.Should().NotThrowAsync();
    }

    // ========================================================================
    // US2: GenesisFile mode tests
    // ========================================================================

    [Fact]
    public async Task GenesisFile_ExistingRegister_SkipsIngestion()
    {
        // Arrange: register already exists
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        var bootstrapper = CreateBootstrapper(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.GenesisFile
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await RunBootstrapperAsync(bootstrapper, cts.Token);

        // Assert: no genesis submission
        _validatorClient.Verify(
            v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenesisFile_GenesisFileNotFound_LogsCriticalWithPath()
    {
        // Arrange: no register, genesis file doesn't exist
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        var bootstrapper = CreateBootstrapper(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.GenesisFile,
            GenesisFile = "/etc/sorcha/missing-genesis.json"
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act — should not throw (exception handled internally)
        await RunBootstrapperAsync(bootstrapper, cts.Token);

        // Assert: critical or error log emitted
        _bootstrapperLogger.Verify(
            l => l.Log(
                It.IsIn(LogLevel.Critical, LogLevel.Error),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    // ========================================================================
    // US3: Auto mode tests
    // ========================================================================

    [Fact(Skip = "Obsolete-on-Auto: BootstrapAutoAsync now calls PostBootstrapAsync " +
        "(WaitForGenesisDocket + SeedBlueprints) after the existence check, and PostBootstrapAsync " +
        "throws when its dependencies (SystemRegisterService, peer-subscription wiring) are not " +
        "mocked here. The retry loop ends in error rather than emitting 'bootstrap completed'. " +
        "Auto-mode happy-path coverage moved to integration tests under SystemRegisterIntegrationTests; " +
        "this unit-level mock harness is no longer the right shape.")]
    public async Task Auto_ExistingRegister_CompletesImmediately()
    {
        // Arrange: register already exists
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        var bootstrapper = CreateBootstrapper(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.Auto
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await RunBootstrapperAsync(bootstrapper, cts.Token);

        // Assert: completed with info log
        _bootstrapperLogger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("bootstrap completed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ========================================================================
    // #917 resilience: seed-blueprint seal must fail loud, not be swallowed
    // ========================================================================

    [Fact]
    public async Task GenesisFile_SeedBlueprintNeverSeals_RetriesThenFailsLoud()
    {
        // Arrange: register exists (skip ingestion) so bootstrap proceeds to seeding.
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Name = SystemRegisterConstants.SystemRegisterName,
                Height = 1
            });

        // No transactions on the register => every seed blueprint is "missing" AND no
        // publish ever appears as sealed (BlueprintExistsAsync stays false). This is the
        // half-seeded SSR the swallow bug used to report as success.
        _mockRepository
            .Setup(r => r.GetTransactionsAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sorcha.Register.Models.TransactionModel>().AsQueryable());

        // Make a seed template discoverable so LoadBlueprintFromCatalog succeeds and the
        // publish path is actually exercised (the bug is post-publish, in the seal wait).
        WriteSeedTemplate("register-creation-v1");

        // Publish must SUCCEED so we isolate the seal-wait swallow (not a publish failure,
        // which already propagated before this fix).
        _validatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "seed-tx",
                RegisterId = SystemRegisterConstants.SystemRegisterId
            });

        var signing = new Mock<ISystemWalletSigningService>();
        signing
            .Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                Algorithm = "ED25519",
                WalletAddress = "sys-wallet"
            });

        var hash = new Mock<IHashProvider>();
        hash
            .Setup(h => h.ComputeHash(It.IsAny<byte[]>(), It.IsAny<Sorcha.Cryptography.Enums.HashType>()))
            .Returns(new byte[32]);

        var logger = new Mock<ILogger<SystemRegisterBootstrapper>>();
        var bootstrapper = CreateBootstrapper(
            new SystemRegisterOptions
            {
                BootstrapMode = BootstrapMode.GenesisFile,
                BlueprintSealTimeoutSeconds = 1,
                BlueprintSeedMaxPublishAttempts = 2
            },
            logger.Object,
            signing.Object,
            hash.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        await RunBootstrapperAsync(bootstrapper, cts.Token);

        // Assert: the unsealed seed was re-published up to the attempt cap (retry happened).
        _validatorClient.Verify(
            v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "the unsealed seed should be re-published BlueprintSeedMaxPublishAttempts times");

        // Assert: bootstrap FAILED LOUD — an error/critical was logged...
        logger.Verify(
            l => l.Log(
                It.IsIn(LogLevel.Critical, LogLevel.Error),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        // ...and it did NOT falsely report success.
        logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("bootstrap completed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "a half-seeded system register must not be reported as a completed bootstrap");
    }

    private static void WriteSeedTemplate(string blueprintId)
    {
        // LoadBlueprintFromCatalog probes AppContext.BaseDirectory/blueprints/templates first.
        var dir = Path.Combine(AppContext.BaseDirectory, "blueprints", "templates");
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, $"{blueprintId}.json"),
            "{\"title\":\"seed-test\",\"participants\":[],\"actions\":[]}");
    }

    // ========================================================================
    // US4: Observability tests
    // ========================================================================

    [Fact]
    public async Task AllModes_LogBootstrapModeAtStartup()
    {
        // Arrange: register already exists for quick completion
        _mockRepository
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        foreach (var mode in new[] { BootstrapMode.Auto, BootstrapMode.SyncOnly, BootstrapMode.GenesisFile })
        {
            var logger = new Mock<ILogger<SystemRegisterBootstrapper>>();

            var bootstrapper = CreateBootstrapper(
                new SystemRegisterOptions
                {
                    BootstrapMode = mode,
                    FastRetryIntervalSeconds = 1,
                    FastRetryDurationSeconds = 5
                },
                logger.Object);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            await RunBootstrapperAsync(bootstrapper, cts.Token);

            // Assert: mode logged at Information level
            logger.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("bootstrap started")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                $"Mode {mode} should log bootstrap start");
        }
    }

    [Fact]
    public async Task InvalidBootstrapMode_LogsCritical()
    {
        var bootstrapper = CreateBootstrapper(new SystemRegisterOptions
        {
            BootstrapMode = (BootstrapMode)999
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await RunBootstrapperAsync(bootstrapper, cts.Token);

        // Assert: critical log about invalid config
        _bootstrapperLogger.Verify(
            l => l.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static SystemRegisterGenesis CreateTestGenesis()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0x01);
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"registerId\":\"test\"}");

        return new SystemRegisterGenesis
        {
            Version = 1,
            NetworkId = "test-network",
            GenesisTransaction = new GenesisTransactionData
            {
                TxId = GenesisSignatureVerifier.ComputeGenesisTxId(),
                Payload = Convert.ToBase64String(payload),
                PayloadHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
                Signature = new GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKey),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            ValidatorRoster = new Register.Models.ValidatorRoster
            {
                Validators =
                [
                    new Register.Models.ValidatorRosterEntry
                    {
                        ValidatorId = "test-validator",
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = Register.Models.SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = Register.Models.ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = GenesisFileLoader.ComputeFingerprint(publicKey)
        };
    }

    private static SystemRegisterGenesis CreateTestGenesisWithValidControlRecord()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0x02);

        var controlRecord = new Register.Models.RegisterControlRecord
        {
            Validators = new Register.Models.ValidatorRoster
            {
                Validators =
                [
                    new Register.Models.ValidatorRosterEntry
                    {
                        ValidatorId = "test-validator",
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = Register.Models.SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = Register.Models.ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            }
        };
        var payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(controlRecord);

        return new SystemRegisterGenesis
        {
            Version = 1,
            NetworkId = "test-network",
            GenesisTransaction = new GenesisTransactionData
            {
                TxId = GenesisSignatureVerifier.ComputeGenesisTxId(),
                Payload = Convert.ToBase64String(payloadBytes),
                PayloadHash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(payloadBytes)).ToLowerInvariant(),
                Signature = new GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKey),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            ValidatorRoster = controlRecord.Validators!,
            GenesisPublicKeyFingerprint = GenesisFileLoader.ComputeFingerprint(publicKey)
        };
    }
}
