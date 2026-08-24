// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Tests.Projection;

/// <summary>
/// Feature 145 US4 (T031): the materialized instance view is a cache of the ledger projection — a
/// fresh rebuild from the sealed transactions must equal it (parity), and rebuilding repairs a
/// corrupted/missing view. The rebuild shares <see cref="InstanceProjectionResolver"/> with the
/// online projector, so the two agree by construction.
/// </summary>
public class InstanceRebuildServiceTests
{
    private const string RegisterId = "reg-1";
    private const string BlueprintId = "bp-1";
    private const string InstanceId = "inst-1";
    private const string W1 = "ws-applicant";
    private const string W2 = "ws-analyst";
    private const string W3 = "ws-issuer";

    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private readonly Mock<IActionResolverService> _actionResolver = new();
    private readonly Mock<IInstanceStore> _instanceStore = new();

    public InstanceRebuildServiceTests()
    {
        var blueprint = new BlueprintModel
        {
            Id = BlueprintId,
            Title = "Rebuild Blueprint",
            Participants =
            [
                new Sorcha.Blueprint.Models.Participant { Id = "p1", Name = "Applicant" },
                new Sorcha.Blueprint.Models.Participant { Id = "p2", Name = "Analyst" },
                new Sorcha.Blueprint.Models.Participant { Id = "p3", Name = "Issuer" },
            ],
            Actions =
            [
                new ActionModel { Id = 1, Title = "Apply", Sender = "p1", IsStartingAction = true, Routes = [new RouteModel { NextActionIds = [2] }] },
                new ActionModel { Id = 2, Title = "Review", Sender = "p2", Routes = [new RouteModel { NextActionIds = [3] }] },
                new ActionModel { Id = 3, Title = "Issue", Sender = "p3" },
            ],
        };
        _actionResolver.Setup(r => r.GetBlueprintAsync(BlueprintId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprint);
        _actionResolver.Setup(r => r.GetActionDefinition(It.IsAny<BlueprintModel>(), It.IsAny<string>()))
            .Returns((BlueprintModel bp, string id) => bp.Actions.FirstOrDefault(a => a.Id.ToString() == id));
    }

    private static TransactionModel Tx(string txId, string prevTxId, int completedActionId, int[] next, string sender, string? recipient)
        => new()
        {
            TxId = txId,
            PrevTxId = prevTxId,
            RegisterId = RegisterId,
            SenderWallet = sender,
            RecipientsWallets = recipient is null ? [] : [recipient],
            MetaData = new TransactionMetaData
            {
                RegisterId = RegisterId,
                BlueprintId = BlueprintId,
                InstanceId = InstanceId,
                ActionId = (uint)completedActionId,
                RoutingDecision = new RoutingDecision
                {
                    CompletedActionId = completedActionId,
                    BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: the decision carries the definition it was computed against
                    NextActions = next.Select(n => new ActionRef { ActionId = n }).ToList(),
                    Attestation = new Attestation { Kind = AttestationKind.SenderSigned, Signature = "sig" },
                },
                TrackingData = new Dictionary<string, string> { ["tenantId"] = "tenant-1" },
            },
        };

    private List<TransactionModel> SealedChain() =>
    [
        Tx("tx-1", "", 1, [2], W1, W2),   // applicant completes action 1 → action 2 (analyst)
        Tx("tx-2", "tx-1", 2, [3], W2, W3), // analyst completes action 2 → action 3 (issuer)
    ];

    private void GivenSealedChain(List<TransactionModel> txs) =>
        _registerClient.Setup(r => r.GetTransactionsByInstanceIdAsync(RegisterId, InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(txs);

    private InstanceRebuildService CreateService() =>
        new(_registerClient.Object, _actionResolver.Object, _instanceStore.Object,
            NullLogger<InstanceRebuildService>.Instance);

    [Fact]
    public async Task RebuildAsync_FromLedger_ProducesExpectedControlState()
    {
        GivenSealedChain(SealedChain());
        var service = CreateService();

        var rebuilt = await service.RebuildAsync(RegisterId, InstanceId);

        rebuilt.Should().NotBeNull();
        rebuilt!.CurrentActionIds.Should().Equal(3);
        rebuilt.State.Should().Be(InstanceState.Active);
        rebuilt.ParticipantWallets.Should().Contain("p3", W3);
    }

    [Fact]
    public async Task RebuildAsync_NoSealedTransactions_ReturnsNull()
    {
        GivenSealedChain([]);
        var service = CreateService();

        var rebuilt = await service.RebuildAsync(RegisterId, InstanceId);

        rebuilt.Should().BeNull();
    }

    [Fact]
    public async Task CheckParityAsync_MaterializedEqualsRebuild_ReportsInSync()
    {
        GivenSealedChain(SealedChain());
        var service = CreateService();
        // The materialized view is itself a projection — use a fresh rebuild as the stored view.
        var materialized = await service.RebuildAsync(RegisterId, InstanceId);
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(materialized);

        var parity = await service.CheckParityAsync(RegisterId, InstanceId);

        parity.InSync.Should().BeTrue();
        parity.Detail.Should().BeNull();
    }

    [Fact]
    public async Task CheckParityAsync_CorruptMaterialized_ReportsDivergence()
    {
        GivenSealedChain(SealedChain());
        var service = CreateService();
        // Corrupt view: stuck on action 2 instead of the projected action 3.
        var corrupt = new Instance
        {
            Id = InstanceId,
            BlueprintId = BlueprintId,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = RegisterId,
            TenantId = "tenant-1",
            CurrentActionIds = [2],
        };
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(corrupt);

        var parity = await service.CheckParityAsync(RegisterId, InstanceId);

        parity.InSync.Should().BeFalse();
        parity.Detail.Should().Contain("currentActionIds");
    }

    [Fact]
    public async Task RebuildAndPersistAsync_CorruptView_RestoresFromLedger()
    {
        GivenSealedChain(SealedChain());
        var service = CreateService();
        var corrupt = new Instance
        {
            Id = InstanceId,
            BlueprintId = BlueprintId,
            BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: an instance must carry its definition pin, or execution has nothing to resolve or chain from
            BlueprintVersion = 1,
            RegisterId = RegisterId,
            TenantId = "tenant-1",
            CurrentActionIds = [2],
        };
        _instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>())).ReturnsAsync(corrupt);
        Instance? persisted = null;
        _instanceStore.Setup(s => s.UpdateAsync(It.IsAny<Instance>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Instance i, CancellationToken _) => { persisted = i; return i; });

        var rebuilt = await service.RebuildAndPersistAsync(RegisterId, InstanceId);

        rebuilt.Should().NotBeNull();
        persisted.Should().NotBeNull();
        persisted!.CurrentActionIds.Should().Equal(3);
    }
}
