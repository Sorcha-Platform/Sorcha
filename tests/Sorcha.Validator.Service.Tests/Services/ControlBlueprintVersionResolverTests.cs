// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit tests for ControlBlueprintVersionResolver
/// </summary>
public class ControlBlueprintVersionResolverTests
{
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IGenesisConfigService> _genesisConfigServiceMock;
    private readonly Mock<ILogger<ControlBlueprintVersionResolver>> _loggerMock;
    private readonly ControlBlueprintVersionResolver _resolver;

    public ControlBlueprintVersionResolverTests()
    {
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _genesisConfigServiceMock = new Mock<IGenesisConfigService>();
        _loggerMock = new Mock<ILogger<ControlBlueprintVersionResolver>>();

        _resolver = new ControlBlueprintVersionResolver(
            _registerClientMock.Object,
            _genesisConfigServiceMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullRegisterClient_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ControlBlueprintVersionResolver(
            null!,
            _genesisConfigServiceMock.Object,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("registerClient");
    }

    [Fact]
    public void Constructor_WithNullGenesisConfigService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ControlBlueprintVersionResolver(
            _registerClientMock.Object,
            null!,
            _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("genesisConfigService");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ControlBlueprintVersionResolver(
            _registerClientMock.Object,
            _genesisConfigServiceMock.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region GetActiveVersionAsync Tests

    [Fact]
    public async Task GetActiveVersionAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _resolver.GetActiveVersionAsync("");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetActiveVersionAsync_WithGenesisOnly_ReturnsVersion1()
    {
        // Arrange
        var registerId = "test-register";
        var genesisConfig = CreateTestGenesisConfig(registerId);
        var genesisDocket = CreateGenesisDocket(registerId);

        _registerClientMock.Setup(r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        _registerClientMock.Setup(r => r.GetTransactionsAsync(
                registerId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Transactions = new List<TransactionModel>(),
                Total = 0,
                Page = 1,
                PageSize = 100
            });

        _genesisConfigServiceMock.Setup(g => g.GetFullConfigAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisConfig);

        // Act
        var result = await _resolver.GetActiveVersionAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.VersionNumber.Should().Be(1);
        result.RegisterId.Should().Be(registerId);
        result.IsActive.Should().BeTrue();
        result.ChangeDescription.Should().Contain("genesis");
    }

    [Fact]
    public async Task GetActiveVersionAsync_WithNoGenesisDocket_ReturnsFromGenesisConfig()
    {
        // Arrange
        var registerId = "test-register";
        var genesisConfig = CreateTestGenesisConfig(registerId);

        _registerClientMock.Setup(r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocketModel?)null);

        _registerClientMock.Setup(r => r.GetTransactionsAsync(
                registerId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Transactions = new List<TransactionModel>(),
                Total = 0,
                Page = 1,
                PageSize = 100
            });

        _genesisConfigServiceMock.Setup(g => g.GetFullConfigAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisConfig);

        // Act
        var result = await _resolver.GetActiveVersionAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.VersionNumber.Should().Be(1);
        result.Configuration.Should().Be(genesisConfig);
    }

    [Fact]
    public async Task GetActiveVersionAsync_CachesResult()
    {
        // Arrange
        var registerId = "test-register";
        var genesisConfig = CreateTestGenesisConfig(registerId);
        var genesisDocket = CreateGenesisDocket(registerId);

        _registerClientMock.Setup(r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        _registerClientMock.Setup(r => r.GetTransactionsAsync(
                registerId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Transactions = new List<TransactionModel>(),
                Total = 0,
                Page = 1,
                PageSize = 100
            });

        _genesisConfigServiceMock.Setup(g => g.GetFullConfigAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisConfig);

        // Act - First call
        var result1 = await _resolver.GetActiveVersionAsync(registerId);

        // Act - Second call (should use cache)
        var result2 = await _resolver.GetActiveVersionAsync(registerId);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();

        // Register client should only be called once (for first call)
        _registerClientMock.Verify(
            r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region GetVersionHistoryAsync Tests

    [Fact]
    public async Task GetVersionHistoryAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _resolver.GetVersionHistoryAsync("");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetVersionHistoryAsync_WithGenesisOnly_ReturnsSingleVersion()
    {
        // Arrange
        var registerId = "test-register";
        var genesisDocket = CreateGenesisDocket(registerId);

        _registerClientMock.Setup(r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        _registerClientMock.Setup(r => r.GetTransactionsAsync(
                registerId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Transactions = new List<TransactionModel>(),
                Total = 0,
                Page = 1,
                PageSize = 100
            });

        // Act
        var result = await _resolver.GetVersionHistoryAsync(registerId);

        // Assert
        result.Should().HaveCount(1);
        result[0].VersionNumber.Should().Be(1);
        result[0].ChangeType.Should().Be("Genesis");
        result[0].IsActive.Should().BeTrue();
    }

    #endregion

    #region GetVersionAsOfAsync Tests

    [Fact]
    public async Task GetVersionAsOfAsync_WithEmptyRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _resolver.GetVersionAsOfAsync("", DateTimeOffset.UtcNow);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetVersionAsOfAsync_BeforeGenesis_ReturnsNull()
    {
        // Arrange
        var registerId = "test-register";
        var genesisTime = DateTimeOffset.UtcNow;
        var genesisDocket = CreateGenesisDocket(registerId, genesisTime);

        _registerClientMock.Setup(r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        _registerClientMock.Setup(r => r.GetTransactionsAsync(
                registerId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Transactions = new List<TransactionModel>(),
                Total = 0,
                Page = 1,
                PageSize = 100
            });

        // Act
        var result = await _resolver.GetVersionAsOfAsync(registerId, genesisTime.AddDays(-1));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetVersionAsOfAsync_AfterGenesis_ReturnsGenesisVersion()
    {
        // Arrange
        var registerId = "test-register";
        var genesisTime = DateTimeOffset.UtcNow.AddHours(-1);
        var genesisDocket = CreateGenesisDocket(registerId, genesisTime);
        var genesisConfig = CreateTestGenesisConfig(registerId);

        _registerClientMock.Setup(r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        _registerClientMock.Setup(r => r.GetTransactionsAsync(
                registerId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Transactions = new List<TransactionModel>(),
                Total = 0,
                Page = 1,
                PageSize = 100
            });

        _genesisConfigServiceMock.Setup(g => g.GetFullConfigAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisConfig);

        // Act
        var result = await _resolver.GetVersionAsOfAsync(registerId, DateTimeOffset.UtcNow);

        // Assert
        result.Should().NotBeNull();
        result!.VersionNumber.Should().Be(1);
    }

    #endregion

    #region Historical config fail-loud (#4)

    // BuildResolvedVersionAsync is private, and control-transaction detection is currently a no-op
    // (TransactionMetaData.ActionId is a uint, so its ToString never matches the "control.*" action
    // ids — filed separately), so no multi-version history can be built through the public API. These
    // tests reach the method directly to pin the fail-loud contract regardless of that.

    private System.Threading.Tasks.Task<ResolvedControlBlueprintVersion?> InvokeBuildResolvedVersion(
        string registerId, ControlBlueprintVersionInfo versionInfo)
    {
        var method = typeof(ControlBlueprintVersionResolver).GetMethod(
            "BuildResolvedVersionAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();
        return (System.Threading.Tasks.Task<ResolvedControlBlueprintVersion?>)method!.Invoke(
            _resolver, [registerId, versionInfo, CancellationToken.None])!;
    }

    [Fact]
    public async Task BuildResolvedVersion_SupersededHistoricalVersion_FailsClosed_NotAlie()
    {
        // The whole point of #4: only the CURRENT config is retrievable, so a superseded historical
        // version cannot be resolved without reconstruction. It used to return today's config stamped
        // with the old version's number — a confident false answer. It must now fail loud instead.
        var registerId = "test-register";
        _genesisConfigServiceMock.Setup(g => g.GetFullConfigAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestGenesisConfig(registerId));

        var supersededV2 = new ControlBlueprintVersionInfo
        {
            RegisterId = registerId,
            VersionNumber = 2,
            TransactionId = "tx-2",
            ActiveFrom = DateTimeOffset.UtcNow.AddHours(-2),
            ChangeType = "ConfigUpdate",
            IsActive = false,   // superseded — this is the case that used to lie
        };

        var act = () => InvokeBuildResolvedVersion(registerId, supersededV2);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .WithMessage("*version 2*")
            .WithMessage("*not implemented*");
    }

    [Fact]
    public async Task BuildResolvedVersion_ActiveVersion_ReturnsCurrentConfig_Truthfully()
    {
        // The active version's config genuinely IS the current config, so this stays honest and must
        // NOT throw — this is what GetActiveVersionAsync relies on.
        var registerId = "test-register";
        var config = CreateTestGenesisConfig(registerId);
        _genesisConfigServiceMock.Setup(g => g.GetFullConfigAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var activeV2 = new ControlBlueprintVersionInfo
        {
            RegisterId = registerId,
            VersionNumber = 2,
            TransactionId = "tx-2",
            ActiveFrom = DateTimeOffset.UtcNow.AddHours(-1),
            ChangeType = "ConfigUpdate",
            IsActive = true,
        };

        var result = await InvokeBuildResolvedVersion(registerId, activeV2);

        result.Should().NotBeNull();
        result!.VersionNumber.Should().Be(2);
        result.Configuration.Should().Be(config);
        result.IsActive.Should().BeTrue();
    }

    #endregion

    #region InvalidateCache Tests

    [Fact]
    public async Task InvalidateCache_ClearsVersionHistory()
    {
        // Arrange
        var registerId = "test-register";
        var genesisConfig = CreateTestGenesisConfig(registerId);
        var genesisDocket = CreateGenesisDocket(registerId);

        _registerClientMock.Setup(r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisDocket);

        _registerClientMock.Setup(r => r.GetTransactionsAsync(
                registerId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Transactions = new List<TransactionModel>(),
                Total = 0,
                Page = 1,
                PageSize = 100
            });

        _genesisConfigServiceMock.Setup(g => g.GetFullConfigAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(genesisConfig);

        // First call to populate cache
        await _resolver.GetActiveVersionAsync(registerId);

        // Act
        _resolver.InvalidateCache(registerId);

        // Second call should hit the client again
        await _resolver.GetActiveVersionAsync(registerId);

        // Assert
        _registerClientMock.Verify(
            r => r.ReadDocketAsync(registerId, 0, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region VersionChanged Event Tests

    [Fact]
    public void VersionChanged_EventCanBeSubscribed()
    {
        // Arrange
        var registerId = "test-register";
        ControlBlueprintVersionChangedEventArgs? receivedArgs = null;

        _resolver.VersionChanged += (sender, args) => receivedArgs = args;

        // Act - Invalidate cache to trigger internal notification
        // Note: The actual event firing would happen through internal processing
        // This test just validates the event subscription works
        _resolver.InvalidateCache(registerId);

        // Assert - Event hasn't fired yet (no version change occurred)
        receivedArgs.Should().BeNull();
    }

    #endregion

    #region Helper Methods

    private static GenesisConfiguration CreateTestGenesisConfig(string registerId)
    {
        return new GenesisConfiguration
        {
            RegisterId = registerId,
            GenesisTransactionId = $"genesis-{registerId}",
            ControlBlueprintVersionId = $"control-v1-{registerId}",
            Consensus = new ConsensusConfig
            {
                SignatureThresholdMin = 2,
                SignatureThresholdMax = 10,
                DocketTimeout = TimeSpan.FromSeconds(30),
                MaxSignaturesPerDocket = 100,
                MaxTransactionsPerDocket = 1000,
                DocketBuildInterval = TimeSpan.FromMilliseconds(100)
            },
            Validators = new ValidatorConfig
            {
                RegistrationMode = "public",
                MinValidators = 1,
                MaxValidators = 100,
                RequireStake = false,
                StakeAmount = null
            },
            LeaderElection = new LeaderElectionConfig
            {
                Mechanism = "rotating",
                HeartbeatInterval = TimeSpan.FromSeconds(1),
                LeaderTimeout = TimeSpan.FromSeconds(5),
                TermDuration = TimeSpan.FromMinutes(1)
            },
            LoadedAt = DateTimeOffset.UtcNow,
            CacheTtl = TimeSpan.FromMinutes(30)
        };
    }

    private static DocketModel CreateGenesisDocket(string registerId, DateTimeOffset? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.AddHours(-1);
        return new DocketModel
        {
            DocketId = $"genesis-docket-{registerId}",
            DocketNumber = 0,
            RegisterId = registerId,
            DocketHash = "genesis-hash",
            CreatedAt = ts.DateTime,
            ProposerValidatorId = "genesis-proposer",
            MerkleRoot = "genesis-merkle-root",
            Transactions = new List<TransactionModel>
            {
                new TransactionModel
                {
                    Id = $"genesis-tx-{registerId}",
                    TxId = $"genesis-tx-{registerId}",
                    TimeStamp = ts.DateTime,
                    Payloads = Array.Empty<PayloadModel>()
                }
            }
        };
    }

    #endregion
}
