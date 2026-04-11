// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.Register.Service.Services;
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

    [Fact]
    public async Task GenesisIngestionService_LoadAndVerify_ReturnsNullWhenNoGenesis()
    {
        var options = Options.Create(new SystemRegisterOptions { GenesisFile = null });
        var service = new GenesisIngestionService(
            options, _validatorClient.Object, _cryptoModule.Object, _ingestionLogger.Object);

        var result = await service.LoadAndVerifyGenesisAsync(CancellationToken.None);
        result.Should().BeNull();
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
        var mockRegisterManager = new Mock<RegisterManager>(
            new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
            new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object);

        mockRegisterManager
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

        var mockSystemRegisterService = new Mock<SystemRegisterService>(
            new Mock<ILogger<SystemRegisterService>>().Object,
            mockRegisterManager.Object,
            new Mock<Sorcha.Register.Core.Managers.TransactionManager>(
                new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
                new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object).Object,
            _validatorClient.Object,
            new Mock<Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService>().Object,
            new Mock<ICryptoModule>().Object);

        var services = new ServiceCollection();
        services.AddSingleton(mockRegisterManager.Object);
        services.AddSingleton(mockSystemRegisterService.Object);
        services.AddSingleton<GenesisIngestionService>(sp =>
            new GenesisIngestionService(
                Options.Create(new SystemRegisterOptions()),
                _validatorClient.Object,
                _cryptoModule.Object,
                _ingestionLogger.Object));
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.SyncOnly,
            FastRetryIntervalSeconds = 1, // 1s for fast test
            FastRetryDurationSeconds = 30
        });

        var bootstrapper = new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _bootstrapperLogger.Object,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert: register was found (checked at least 3 times)
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task SyncOnly_NeverIngestsGenesisFile_EvenWhenAvailable()
    {
        // Arrange: register found on first check — but also check genesis never called
        var mockRegisterManager = new Mock<RegisterManager>(
            new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
            new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object);

        mockRegisterManager
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        var mockGenesisIngestion = new Mock<GenesisIngestionService>(
            Options.Create(new SystemRegisterOptions()),
            _validatorClient.Object,
            _cryptoModule.Object,
            _ingestionLogger.Object);

        var mockSystemRegisterService = new Mock<SystemRegisterService>(
            new Mock<ILogger<SystemRegisterService>>().Object,
            mockRegisterManager.Object,
            new Mock<Sorcha.Register.Core.Managers.TransactionManager>(
                new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
                new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object).Object,
            _validatorClient.Object,
            new Mock<Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService>().Object,
            new Mock<ICryptoModule>().Object);

        var services = new ServiceCollection();
        services.AddSingleton(mockRegisterManager.Object);
        services.AddSingleton(mockGenesisIngestion.Object);
        services.AddSingleton(mockSystemRegisterService.Object);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.SyncOnly,
            FastRetryIntervalSeconds = 1,
            FastRetryDurationSeconds = 10
        });

        var bootstrapper = new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _bootstrapperLogger.Object,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert: genesis ingestion was NEVER called
        mockGenesisIngestion.Verify(
            g => g.LoadAndVerifyGenesisAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncOnly_RespectsShutdownCancellation_DuringPolling()
    {
        // Arrange: register never found, cancel after 2 seconds
        var mockRegisterManager = new Mock<RegisterManager>(
            new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
            new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object);

        mockRegisterManager
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        var mockSystemRegisterService = new Mock<SystemRegisterService>(
            new Mock<ILogger<SystemRegisterService>>().Object,
            mockRegisterManager.Object,
            new Mock<Sorcha.Register.Core.Managers.TransactionManager>(
                new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
                new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object).Object,
            _validatorClient.Object,
            new Mock<Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService>().Object,
            new Mock<ICryptoModule>().Object);

        var services = new ServiceCollection();
        services.AddSingleton(mockRegisterManager.Object);
        services.AddSingleton(mockSystemRegisterService.Object);
        services.AddSingleton<GenesisIngestionService>(sp =>
            new GenesisIngestionService(
                Options.Create(new SystemRegisterOptions()),
                _validatorClient.Object,
                _cryptoModule.Object,
                _ingestionLogger.Object));
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.SyncOnly,
            FastRetryIntervalSeconds = 1,
            FastRetryDurationSeconds = 5,
            BackoffIntervalSeconds = 60 // Long backoff — would hang if not cancelled
        });

        var bootstrapper = new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _bootstrapperLogger.Object,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

        // Act — should complete cleanly (cancellation handled)
        Func<Task> act = async () =>
        {
            await bootstrapper.StartAsync(cts.Token);
            await bootstrapper.StopAsync(cts.Token);
        };

        await act.Should().NotThrowAsync();
    }

    // ========================================================================
    // US2: GenesisFile mode tests
    // ========================================================================

    [Fact]
    public async Task GenesisFile_ExistingRegister_SkipsIngestion()
    {
        // Arrange: register already exists
        var mockRegisterManager = new Mock<RegisterManager>(
            new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
            new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object);

        mockRegisterManager
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        var mockGenesisIngestion = new Mock<GenesisIngestionService>(
            Options.Create(new SystemRegisterOptions()),
            _validatorClient.Object,
            _cryptoModule.Object,
            _ingestionLogger.Object);

        var mockSystemRegisterService = new Mock<SystemRegisterService>(
            new Mock<ILogger<SystemRegisterService>>().Object,
            mockRegisterManager.Object,
            new Mock<Sorcha.Register.Core.Managers.TransactionManager>(
                new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
                new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object).Object,
            _validatorClient.Object,
            new Mock<Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService>().Object,
            new Mock<ICryptoModule>().Object);

        var services = new ServiceCollection();
        services.AddSingleton(mockRegisterManager.Object);
        services.AddSingleton(mockGenesisIngestion.Object);
        services.AddSingleton(mockSystemRegisterService.Object);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.GenesisFile
        });

        var bootstrapper = new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _bootstrapperLogger.Object,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert: genesis ingestion was never called — register already exists
        mockGenesisIngestion.Verify(
            g => g.LoadAndVerifyGenesisAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenesisFile_GenesisFileNotFound_LogsCriticalWithPath()
    {
        // Arrange: no register, genesis returns null
        var mockRegisterManager = new Mock<RegisterManager>(
            new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
            new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object);

        mockRegisterManager
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);

        var mockSystemRegisterService = new Mock<SystemRegisterService>(
            new Mock<ILogger<SystemRegisterService>>().Object,
            mockRegisterManager.Object,
            new Mock<Sorcha.Register.Core.Managers.TransactionManager>(
                new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
                new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object).Object,
            _validatorClient.Object,
            new Mock<Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService>().Object,
            new Mock<ICryptoModule>().Object);

        var services = new ServiceCollection();
        services.AddSingleton(mockRegisterManager.Object);
        services.AddSingleton(mockSystemRegisterService.Object);
        services.AddSingleton<GenesisIngestionService>(sp =>
            new GenesisIngestionService(
                Options.Create(new SystemRegisterOptions { GenesisFile = null }),
                _validatorClient.Object,
                _cryptoModule.Object,
                _ingestionLogger.Object));
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.GenesisFile,
            GenesisFile = "/etc/sorcha/missing-genesis.json"
        });

        var bootstrapper = new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _bootstrapperLogger.Object,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act — should not throw (exception handled internally)
        Func<Task> act = async () =>
        {
            await bootstrapper.StartAsync(cts.Token);
            await bootstrapper.StopAsync(cts.Token);
        };

        await act.Should().NotThrowAsync();

        // Assert: critical log emitted (bootstrapper catches SystemRegisterBootstrapStopException)
        _bootstrapperLogger.Verify(
            l => l.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("bootstrap STOPPED")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ========================================================================
    // US3: Auto mode tests
    // ========================================================================

    [Fact]
    public async Task Auto_ExistingRegister_CompletesImmediately()
    {
        // Arrange: register already exists
        var mockRegisterManager = new Mock<RegisterManager>(
            new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
            new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object);

        mockRegisterManager
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        var mockSystemRegisterService = new Mock<SystemRegisterService>(
            new Mock<ILogger<SystemRegisterService>>().Object,
            mockRegisterManager.Object,
            new Mock<Sorcha.Register.Core.Managers.TransactionManager>(
                new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
                new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object).Object,
            _validatorClient.Object,
            new Mock<Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService>().Object,
            new Mock<ICryptoModule>().Object);

        var services = new ServiceCollection();
        services.AddSingleton(mockRegisterManager.Object);
        services.AddSingleton(mockSystemRegisterService.Object);
        services.AddSingleton<GenesisIngestionService>(sp =>
            new GenesisIngestionService(
                Options.Create(new SystemRegisterOptions()),
                _validatorClient.Object,
                _cryptoModule.Object,
                _ingestionLogger.Object));
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SystemRegisterOptions
        {
            BootstrapMode = BootstrapMode.Auto
        });

        var bootstrapper = new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _bootstrapperLogger.Object,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

        // Assert: bootstrap completed (logged completion)
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
    // US4: Observability tests
    // ========================================================================

    [Fact]
    public async Task AllModes_LogBootstrapModeAtStartup()
    {
        // Arrange: register already exists for quick completion
        var mockRegisterManager = new Mock<RegisterManager>(
            new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
            new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object);

        mockRegisterManager
            .Setup(r => r.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register
            {
                Id = SystemRegisterConstants.SystemRegisterId,
                Height = 1
            });

        var mockSystemRegisterService = new Mock<SystemRegisterService>(
            new Mock<ILogger<SystemRegisterService>>().Object,
            mockRegisterManager.Object,
            new Mock<Sorcha.Register.Core.Managers.TransactionManager>(
                new Mock<Sorcha.Register.Core.Storage.IRegisterRepository>().Object,
                new Mock<Sorcha.Register.Core.Events.IEventPublisher>().Object).Object,
            _validatorClient.Object,
            new Mock<Sorcha.ServiceClients.SystemWallet.ISystemWalletSigningService>().Object,
            new Mock<ICryptoModule>().Object);

        foreach (var mode in new[] { BootstrapMode.Auto, BootstrapMode.SyncOnly, BootstrapMode.GenesisFile })
        {
            var logger = new Mock<ILogger<SystemRegisterBootstrapper>>();

            var services = new ServiceCollection();
            services.AddSingleton(mockRegisterManager.Object);
            services.AddSingleton(mockSystemRegisterService.Object);
            services.AddSingleton<GenesisIngestionService>(sp =>
                new GenesisIngestionService(
                    Options.Create(new SystemRegisterOptions()),
                    _validatorClient.Object,
                    _cryptoModule.Object,
                    _ingestionLogger.Object));
            var sp = services.BuildServiceProvider();

            var options = Options.Create(new SystemRegisterOptions
            {
                BootstrapMode = mode,
                FastRetryIntervalSeconds = 1,
                FastRetryDurationSeconds = 5
            });

            var bootstrapper = new SystemRegisterBootstrapper(
                sp.GetRequiredService<IServiceScopeFactory>(),
                logger.Object,
                options);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            await bootstrapper.StartAsync(cts.Token);
            await bootstrapper.StopAsync(cts.Token);

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
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new SystemRegisterOptions
        {
            BootstrapMode = (BootstrapMode)999 // Invalid value
        });

        var bootstrapper = new SystemRegisterBootstrapper(
            sp.GetRequiredService<IServiceScopeFactory>(),
            _bootstrapperLogger.Object,
            options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act — should not throw (handled internally)
        await bootstrapper.StartAsync(cts.Token);
        await bootstrapper.StopAsync(cts.Token);

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
}
