// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Models.Canonical;
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
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "Test Register", Height = 5, Status = "Active" }]);

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
                        // Feature 195 — the transaction id IS the definition's identity, so it must
                        // be the publication id of this very JSON on this very register or recovery
                        // rejects it. A placeholder like "tx-1" would fail provenance, which is the
                        // check doing its job.
                        TransactionId = BlueprintPublicationId.ComputeFromDefinition(
                            "reg-1", "bp-1", blueprintJson),
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
                new InternalRegisterInfo { Id = "reg-1", Name = "Register 1", Height = 3 },
                new InternalRegisterInfo { Id = "reg-2", Name = "Register 2", Height = 7 }
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
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "R1", Height = 2 }]);

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
                        TransactionId = BlueprintPublicationId.ComputeFromDefinition(
                            "reg-1", "bp-1", blueprintJson),
                        PublishedAt = DateTimeOffset.UtcNow,
                        BlueprintJson = blueprintJson
                    }
                ]
            });

        // Simulate this PUBLICATION already recovered for this register.
        //
        // Feature 194 changed the idempotency key from "this blueprint id on this register" to "this
        // executable definition on this register". Feature 195 narrows it once more, to "this
        // PUBLICATION" — because two publications can share an executable definition (a
        // presentational-only republish) while being distinct, independently pinnable definitions,
        // and dropping one strands any instance pinned to it.
        //
        // So the pre-existing entry must carry the publication id it represents; an entry with an
        // empty id genuinely IS a different (unidentified) publication.
        var recoveredDefinition = JsonSerializer.Deserialize<Sorcha.Blueprint.Models.Blueprint>(blueprintJson)!;

        _mockPublishedStore
            .Setup(s => s.GetVersionsAsync("bp-1"))
            .ReturnsAsync(new[]
            {
                new PublishedBlueprint
                {
                    BlueprintId = "bp-1",
                    RegisterId = "reg-1",
                    PublicationTxId = BlueprintPublicationId.ComputeFromDefinition(
                        "reg-1", "bp-1", blueprintJson),
                    Blueprint = recoveredDefinition
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
    public async Task RunRecoveryAsync_TwoPublicationsOfTheSameDefinition_RecoverOnce()
    {
        // Feature 194 REPLACED this test's predecessor, which asserted "only the newest version is
        // added". That assertion encoded the defect: collapsing to newest-per-id is precisely what
        // strands an instance pinned to an earlier definition once the process restarts.
        //
        // What is true now is narrower and content-based: two publications whose EXECUTABLE
        // DEFINITIONS are identical (a presentational-only republish) are one definition, and
        // recover once. The sibling test below covers the case that actually changed.
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow;
        // Serialize once — the blueprint model has per-instance default fields (CreatedAt/UpdatedAt
        // default to UtcNow), so a fresh serialization would be a DIFFERENT definition.
        var dupJson = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-dup"));
        // Feature 195 — identical content on one register is identical IDENTITY. The two entries
        // below are therefore literally the same publication arriving twice, which is a stronger
        // statement of the property under test than "two publications sharing an exec-def hash".
        var dupTxId = BlueprintPublicationId.ComputeFromDefinition("reg-1", "bp-dup", dupJson);

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "R1", Height = 4 }]);

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
                        BlueprintId = "bp-dup", TransactionId = dupTxId,
                        PublishedAt = older, BlueprintJson = dupJson
                    },
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-dup", TransactionId = dupTxId,
                        PublishedAt = newer, BlueprintJson = dupJson
                    }
                ]
            });

        // A STATEFUL store stand-in. The previous fixture returned a fixed empty list from
        // GetVersionsAsync no matter what had been added, so the recovery loop's own idempotency
        // check could never see its first write — the test could not have observed dedup at all.
        var stored = new List<PublishedBlueprint>();
        _mockPublishedStore
            .Setup(s => s.GetVersionsAsync("bp-dup"))
            .ReturnsAsync(() => stored.ToList());
        _mockPublishedStore
            .Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .ReturnsAsync((PublishedBlueprint pb) => { stored.Add(pb); return pb; });

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        stored.Should().HaveCount(1,
            "two publications of an identical executable definition are ONE definition");
        _recoveryState.RegisterStates["reg-1"].RecoveredBlueprintCount.Should().Be(1);
    }

    [Fact]
    public async Task RunRecoveryAsync_TwoDifferentDefinitionsOfOneBlueprint_RecoverBoth()
    {
        // THE Feature 194 recovery guarantee, and the one that makes step 6 of the live acceptance
        // test pass: an instance pinned to the FIRST definition must still resolve it after the
        // service restarts. Recover only the newest and that instance is permanently stuck, with the
        // symptom appearing as a transaction that never seals.
        var v1Json = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-two"));

        var v2Model = CreateMinimalBlueprint("bp-two");
        // A BEHAVIOURAL change — a second action the first definition does not have.
        v2Model.Actions.Add(new Sorcha.Blueprint.Models.Action
        {
            Id = 2, Title = "Review", Sender = "p2"
        });
        var v2Json = JsonSerializer.Serialize(v2Model);

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "R1", Height = 4 }]);

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
                        BlueprintId = "bp-two",
                        TransactionId = BlueprintPublicationId.ComputeFromDefinition("reg-1", "bp-two", v1Json),
                        PublishedAt = DateTimeOffset.UtcNow.AddHours(-2),
                        BlueprintJson = v1Json
                    },
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-two",
                        TransactionId = BlueprintPublicationId.ComputeFromDefinition("reg-1", "bp-two", v2Json),
                        PublishedAt = DateTimeOffset.UtcNow,
                        BlueprintJson = v2Json
                    }
                ]
            });

        var stored = new List<PublishedBlueprint>();
        _mockPublishedStore
            .Setup(s => s.GetVersionsAsync("bp-two"))
            .ReturnsAsync(() => stored.ToList());
        _mockPublishedStore
            .Setup(s => s.AddAsync(It.IsAny<PublishedBlueprint>()))
            .ReturnsAsync((PublishedBlueprint pb) => { stored.Add(pb); return pb; });

        var service = CreateService();
        await service.RunRecoveryAsync(CancellationToken.None);

        stored.Should().HaveCount(2,
            "an instance pinned to EITHER definition must resolve it after a restart");
        stored.Select(p => p.ExecDefHash).Distinct().Should().HaveCount(2);
        stored.Should().AllSatisfy(p => p.ExecDefHash.Should().NotBeNullOrWhiteSpace(
            "a recovered definition with no hash can never be resolved by a pinned instance"));
    }

    #endregion

    #region RunRecoveryAsync — Error Handling

    [Fact]
    public async Task RunRecoveryAsync_RegisterQueryFails_SetsOfflineAndTracksFailures()
    {
        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-fail", Name = "Failing", Height = 0 }]);

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
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-err", Name = "Error", Height = 0 }]);

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
        var goodJson = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-good"));

        _mockRegisterClient
            .Setup(c => c.GetInternalRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "R1", Height = 2 }]);

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
                        TransactionId = BlueprintPublicationId.ComputeFromDefinition(
                            "reg-1", "bp-good", goodJson),
                        PublishedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                        BlueprintJson = goodJson
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
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-flaky", Name = "Flaky", Height = 0 }]);

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
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "R1", Height = 0 }]);

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
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "R1", Height = 3 }]);

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
            .ReturnsAsync([new InternalRegisterInfo { Id = "reg-1", Name = "Empty", Height = 1 }]);

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

    #region RecoverRegisterAsync — event-driven (Feature 137 / C2)

    [Fact]
    public async Task RecoverRegisterAsync_RecoversBlueprintsAndMarksOnline()
    {
        var blueprintJson = JsonSerializer.Serialize(CreateMinimalBlueprint("bp-1"));

        // Note: NO GetInternalRegistersAsync setup — the event-driven path targets one register
        // directly (a register that replicated after boot, possibly not yet in discovery).
        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishedBlueprintsResponse
            {
                RegisterId = "reg-new",
                RegisterHeight = 9,
                Blueprints =
                [
                    new PublishedBlueprintEntry
                    {
                        BlueprintId = "bp-1",
                        TransactionId = BlueprintPublicationId.ComputeFromDefinition(
                            "reg-new", "bp-1", blueprintJson),
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
        await service.RecoverRegisterAsync("reg-new", "Newly Synced Register", CancellationToken.None);

        _recoveryState.RegisterStates.Should().ContainKey("reg-new");
        var state = _recoveryState.RegisterStates["reg-new"];
        state.Status.Should().Be(RegisterHealthStatus.Online);
        state.RegisterName.Should().Be("Newly Synced Register");
        state.RecoveredBlueprintCount.Should().Be(1);

        _mockPublishedStore.Verify(
            s => s.AddAsync(It.Is<PublishedBlueprint>(pb => pb.BlueprintId == "bp-1" && pb.RegisterId == "reg-new")),
            Times.Once);
    }

    [Fact]
    public async Task RecoverRegisterAsync_ClientReturnsNull_MarksOffline()
    {
        _mockRegisterClient
            .Setup(c => c.GetPublishedBlueprintsAsync("reg-down", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PublishedBlueprintsResponse?)null);

        var service = CreateService();
        await service.RecoverRegisterAsync("reg-down", registerName: null, CancellationToken.None);

        var state = _recoveryState.RegisterStates["reg-down"];
        state.Status.Should().Be(RegisterHealthStatus.Offline);
        state.ConsecutiveFailures.Should().Be(1);
        state.ErrorMessage.Should().NotBeNullOrEmpty();
        // Falls back to registerId as the display name when none supplied.
        state.RegisterName.Should().Be("reg-down");
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
