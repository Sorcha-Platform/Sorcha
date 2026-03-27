// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for BlueprintRecoveryService: startup recovery, periodic refresh,
/// retry logic, deduplication, and error handling.
/// </summary>
public class BlueprintRecoveryServiceTests
{
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;
    private readonly Mock<IPublishedBlueprintStore> _mockPublishedStore;
    private readonly RecoveryState _recoveryState;
    private readonly RecoveryOptions _options;
    private readonly Mock<ILogger<BlueprintRecoveryService>> _mockLogger;

    public BlueprintRecoveryServiceTests()
    {
        _mockRegisterClient = new Mock<IRegisterServiceClient>();
        _mockPublishedStore = new Mock<IPublishedBlueprintStore>();
        _recoveryState = new RecoveryState();
        _options = new RecoveryOptions { RefreshIntervalSeconds = 60, MaxRetryAttempts = 3 };
        _mockLogger = new Mock<ILogger<BlueprintRecoveryService>>();
    }

    private BlueprintRecoveryService CreateService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_mockRegisterClient.Object);
        services.AddSingleton(_mockPublishedStore.Object);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        return new BlueprintRecoveryService(
            scopeFactory,
            _recoveryState,
            Options.Create(_options),
            _mockLogger.Object);
    }

    #region RunRecoveryAsync — Discovery

    [Fact]
    public async Task RunRecoveryAsync_NoRegistersDiscovered_ReturnsEarly()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        _recoveryState.RegisterStates.Should().BeEmpty();
        _mockRegisterClient.Verify(
            c => c.GetPublishedBlueprintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunRecoveryAsync_SingleRegister_RecoversBlueprintsAndUpdatesState()
    {
        var blueprint = CreateMinimalBlueprint("bp-1");
        var blueprintJson = JsonSerializer.Serialize(blueprint);

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-1", Name = "Test Register", Height = 5, Status = "Active" }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedBlueprintsResponse
            {
                RegisterId = "reg-1",
                RegisterHeight = 5,
                Blueprints =
                [
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-1",
                        TransactionId = "tx-1",
                        PublishedBy = "user-1",
                        PublishedAt = DateTimeOffset.UtcNow,
                        BlueprintJson = blueprintJson
                    }
                ]
            });

        _mockPublishedStore
            .Setup(s => s.GetVersionsAsync("bp-1"))
            .ReturnsAsync(Enumerable.Empty<PublishedBlueprint>());

        _mockPublishedStore
            .Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .ReturnsAsync((PublishedBlueprint pb) => pb);

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        _recoveryState.RegisterStates.Should().ContainKey("reg-1");
        var state = _recoveryState.RegisterStates["reg-1"];
        state.Status.Should().Be(RegisterHealthStatus.Online);
        state.Height.Should().Be(5);
        state.RecoveredBlueprintCount.Should().Be(1);
        state.ConsecutiveFailures.Should().Be(0);
        state.ErrorMessage.Should().BeNull();

        _mockPublishedStore.Verify(
            s => s.AddAsync(It.Is<PublishedBlueprint>(pb =>
                pb.BlueprintId == "bp-1" && pb.RegisterId == "reg-1")),
            Times.Once);
    }

    [Fact]
    public async Task RunRecoveryAsync_MultipleRegisters_RecoversBoth()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RegisterSummary { Id = "reg-1", Name = "Register 1", Height = 3 },
                new RegisterSummary { Id = "reg-2", Name = "Register 2", Height = 7 }
            ]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string regId, CancellationToken _) => new PublishedBlueprintsResponse
            {
                RegisterId = regId,
                RegisterHeight = regId == "reg-1" ? 3 : 7,
                Blueprints = []
            });

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        _recoveryState.RegisterStates.Should().HaveCount(2);
        _recoveryState.RegisterStates["reg-1"].Status.Should().Be(RegisterHealthStatus.Online);
        _recoveryState.RegisterStates["reg-2"].Status.Should().Be(RegisterHealthStatus.Online);
    }

    #endregion

    #region RunRecoveryAsync — Deduplication

    [Fact]
    public async Task RunRecoveryAsync_BlueprintAlreadyExistsForRegister_SkipsIt()
    {
        var blueprintJson = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-1"));

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-1", Name = "R1", Height = 2 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedBlueprintsResponse
            {
                RegisterId = "reg-1",
                RegisterHeight = 2,
                Blueprints =
                [
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-1",
                        TransactionId = "tx-1",
                        PublishedAt = DateTimeOffset.UtcNow,
                        BlueprintJson = blueprintJson
                    }
                ]
            });

        // Simulate blueprint already recovered for this register
        _mockPublishedStore
            .Setup(s => s.GetVersionsAsync("bp-1"))
            .ReturnsAsync(new[]
            {
                new PublishedBlueprint
                {
                    BlueprintId = "bp-1",
                    RegisterId = "reg-1",
                    Blueprint = CreateMinimalBlueprint("bp-1")
                }
            });

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        _mockPublishedStore.Verify(
            s => s.AddAsync(It.IsAny<PublishedBlueprint>()),
            Times.Never);

        _recoveryState.RegisterStates["reg-1"].RecoveredBlueprintCount.Should().Be(0);
    }

    [Fact]
    public async Task RunRecoveryAsync_DuplicateBlueprintIds_OnlyProcessesNewest()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow;

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-1", Name = "R1", Height = 4 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedBlueprintsResponse
            {
                RegisterId = "reg-1",
                RegisterHeight = 4,
                Blueprints =
                [
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-dup",
                        TransactionId = "tx-old",
                        PublishedAt = older,
                        BlueprintJson = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-dup"))
                    },
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-dup",
                        TransactionId = "tx-new",
                        PublishedAt = newer,
                        BlueprintJson = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-dup"))
                    }
                ]
            });

        _mockPublishedStore
            .Setup(s => s.GetVersionsAsync("bp-dup"))
            .ReturnsAsync(Enumerable.Empty<PublishedBlueprint>());

        _mockPublishedStore
            .Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .ReturnsAsync((PublishedBlueprint pb) => pb);

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        // Only the newest version should be added
        _mockPublishedStore.Verify(
            s => s.AddAsync(It.IsAny<PublishedBlueprint>()),
            Times.Once);

        _recoveryState.RegisterStates["reg-1"].RecoveredBlueprintCount.Should().Be(1);
    }

    #endregion

    #region RunRecoveryAsync — Error Handling

    [Fact]
    public async Task RunRecoveryAsync_RegisterQueryFails_SetsOfflineAndTracksFailures()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-fail", Name = "Failing", Height = 0 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-fail", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishedBlueprintsResponse?)null);

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        var state = _recoveryState.RegisterStates["reg-fail"];
        state.Status.Should().Be(RegisterHealthStatus.Offline);
        state.ConsecutiveFailures.Should().Be(1);
        state.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunRecoveryAsync_RegisterThrowsException_SetsOfflineAndTracksFailures()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-err", Name = "Error", Height = 0 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-err", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        var state = _recoveryState.RegisterStates["reg-err"];
        state.Status.Should().Be(RegisterHealthStatus.Offline);
        state.ConsecutiveFailures.Should().Be(1);
        state.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task RunRecoveryAsync_InvalidBlueprintJson_SkipsAndContinues()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-1", Name = "R1", Height = 2 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedBlueprintsResponse
            {
                RegisterId = "reg-1",
                RegisterHeight = 2,
                Blueprints =
                [
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-bad",
                        TransactionId = "tx-1",
                        PublishedAt = DateTimeOffset.UtcNow,
                        BlueprintJson = "{ not valid json!!!"
                    },
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-good",
                        TransactionId = "tx-2",
                        PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                        BlueprintJson = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-good"))
                    }
                ]
            });

        _mockPublishedStore
            .Setup(s => s.GetVersionsAsync(It.IsAny<string>()))
            .ReturnsAsync(Enumerable.Empty<PublishedBlueprint>());

        _mockPublishedStore
            .Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .ReturnsAsync((PublishedBlueprint pb) => pb);

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        // Bad JSON skipped, good one recovered
        _mockPublishedStore.Verify(
            s => s.AddAsync(It.Is<PublishedBlueprint>(pb => pb.BlueprintId == "bp-good")),
            Times.Once);

        // Register still marked online (individual blueprint failures don't fail the register)
        _recoveryState.RegisterStates["reg-1"].Status.Should().Be(RegisterHealthStatus.Online);
    }

    #endregion

    #region RunRecoveryAsync — Retry Cap

    [Fact]
    public async Task RunRecoveryAsync_ExceedsMaxRetries_SkipsRegisterOnPeriodicRefresh()
    {
        _options.MaxRetryAttempts = 2;

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-flaky", Name = "Flaky", Height = 0 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-flaky", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishedBlueprintsResponse?)null);

        var service = CreateService();

        // Initial recovery: failures 1 and 2
        await service.RunRecoveryAsync(CancellationToken.None);
        _recoveryState.RegisterStates["reg-flaky"].ConsecutiveFailures.Should().Be(1);

        await service.RunRecoveryAsync(CancellationToken.None);
        _recoveryState.RegisterStates["reg-flaky"].ConsecutiveFailures.Should().Be(2);

        // Mark initial recovery complete (simulates transition to periodic refresh)
        _recoveryState.IsComplete = true;

        // Third attempt during periodic refresh: should be skipped
        await service.RunRecoveryAsync(CancellationToken.None);

        // Failures should stay at 2 (skipped, not retried)
        _recoveryState.RegisterStates["reg-flaky"].ConsecutiveFailures.Should().Be(2);

        // Verify the published blueprints call was NOT made this third time
        _mockRegisterClient.Verify(
            c => c.GetPublishedBlueprintsAsync("reg-flaky", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunRecoveryAsync_RetryCapNotEnforced_DuringInitialRecovery()
    {
        _options.MaxRetryAttempts = 1;

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-1", Name = "R1", Height = 0 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishedBlueprintsResponse?)null);

        var service = CreateService();

        // IsComplete is false (initial recovery), so retry cap should NOT be enforced
        await service.RunRecoveryAsync(CancellationToken.None);
        await service.RunRecoveryAsync(CancellationToken.None);
        await service.RunRecoveryAsync(CancellationToken.None);

        // All 3 attempts should have been made
        _mockRegisterClient.Verify(
            c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    #endregion

    #region RunRecoveryAsync — Recovery After Failure

    [Fact]
    public async Task RunRecoveryAsync_RegisterRecovers_ResetsFailureState()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-1", Name = "R1", Height = 3 }]);

        // First call fails
        _mockRegisterClient
            .SetupSequence(c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishedBlueprintsResponse?)null)
            .ReturnsAsync(new PublishedBlueprintsResponse
            {
                RegisterId = "reg-1",
                RegisterHeight = 3,
                Blueprints = []
            });

        var service = CreateService();

        await service.RunRecoveryAsync(CancellationToken.None);
        _recoveryState.RegisterStates["reg-1"].Status.Should().Be(RegisterHealthStatus.Offline);
        _recoveryState.RegisterStates["reg-1"].ConsecutiveFailures.Should().Be(1);

        await service.RunRecoveryAsync(CancellationToken.None);
        _recoveryState.RegisterStates["reg-1"].Status.Should().Be(RegisterHealthStatus.Online);
        _recoveryState.RegisterStates["reg-1"].ConsecutiveFailures.Should().Be(0);
        _recoveryState.RegisterStates["reg-1"].ErrorMessage.Should().BeNull();
    }

    #endregion

    #region RunRecoveryAsync — Empty Blueprints

    [Fact]
    public async Task RunRecoveryAsync_RegisterHasNoBlueprintsPublished_MarksOnlineWithZeroCount()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RegisterSummary { Id = "reg-1", Name = "Empty", Height = 1 }]);

        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedBlueprintsResponse
            {
                RegisterId = "reg-1",
                RegisterHeight = 1,
                Blueprints = []
            });

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        var state = _recoveryState.RegisterStates["reg-1"];
        state.Status.Should().Be(RegisterHealthStatus.Online);
        state.RecoveredBlueprintCount.Should().Be(0);
        state.LastSuccessAt.Should().NotBeNull();

        _mockPublishedStore.Verify(
            s => s.AddAsync(It.IsAny<PublishedBlueprint>()),
            Times.Never);
    }

    #endregion

    #region Helpers

    private static BlueprintModel CreateMinimalBlueprint(string id) => new()
    {
        Id = id,
        Title = $"Test Blueprint {id}",
        Participants = [new Sorcha.Blueprint.Models.Participant { Id = "p1", Name = "Tester" }],
        Actions = [new Sorcha.Blueprint.Models.Action { Id = 1, Title = "Start", Sender = "p1" }]
    };

    #endregion
}
