// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.AtomicCache;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Tests.Reactions;

/// <summary>
/// Feature 145 US2 (T026): reaction side effects (notification + durable inbox) must be
/// <b>entitlement-gated</b> (only the node hosting the target wallet fires them — cross-node dedup)
/// and <b>idempotent</b> (replay/restart/rebuild → fire exactly once). Credential mint is NOT a
/// reaction — it stays inline on the submit path by design; these tests cover notifications only.
/// </summary>
public class ReactionDispatcherTests
{
    private readonly Mock<IWalletServiceClient> _walletClient = new();
    private readonly Mock<INotificationService> _notifier = new();
    private readonly Mock<IActionResolverService> _actionResolver = new();
    private readonly IAtomicDistributedCache _claims = new InMemoryAtomicDistributedCache();
    private readonly ReactionDispatcherMetrics _metrics;
    private readonly ServiceProvider _provider;

    private const string CitizenWallet = "ws-citizen-1";
    private const string SealedTxId = "tx-abc-123";

    public ReactionDispatcherTests()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _metrics = new ReactionDispatcherMetrics(_provider.GetRequiredService<IMeterFactory>());
    }

    private ReactionDispatcher CreateDispatcher() => new(
        _walletClient.Object,
        _claims,
        _metrics,
        NullLogger<ReactionDispatcher>.Instance,
        _actionResolver.Object,
        _notifier.Object);

    /// <summary>
    /// A sealed transaction carrying no routing decision — the notification/inbox reactions covered
    /// here key off the folded instance, not the decision. (The Feature 184 decision-notice reaction,
    /// which does read the carried decision, is covered in
    /// <see cref="ReactionDispatcherDecisionNoticeTests"/>.)
    /// </summary>
    private static TransactionModel SealedTx(string txId) => new()
    {
        TxId = txId,
        MetaData = new TransactionMetaData { BlueprintId = "bp-1", InstanceId = "inst-1" },
    };

    private void HostsWallet(string address) =>
        _walletClient.Setup(w => w.GetWalletAsync(address, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = address,
                Name = "Citizen",
                PublicKey = "pk",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "owner",
                Tenant = "tenant-1",
            });

    private void DoesNotHostWallet(string address) =>
        _walletClient.Setup(w => w.GetWalletAsync(address, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

    private static Instance ActiveInstance(string assigneeWallet, params int[] currentActions) => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "tenant-1",
        CurrentActionIds = currentActions.ToList(),
        ParticipantWallets = new Dictionary<string, string> { ["citizen"] = assigneeWallet },
    };

    private static Instance CompletedInstance(params string[] participantWallets) => new()
    {
        Id = "inst-1",
        BlueprintId = "bp-1",
        BlueprintDefinitionTxId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", // Feature 195: execution resolves and chains by the pin
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "tenant-1",
        CurrentActionIds = [],
        ParticipantWallets = participantWallets
            .Select((w, i) => (w, i))
            .ToDictionary(x => $"p{x.i}", x => x.w),
    };

    [Fact]
    public async Task DispatchAsync_EntitledFirstTime_FiresActionAvailableOnce()
    {
        HostsWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();

        await dispatcher.DispatchAsync(ActiveInstance(CitizenWallet, 2), SealedTx(SealedTxId), default);

        _notifier.Verify(n => n.NotifyActionAvailableAsync("inst-1", CitizenWallet, "2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_NotEntitled_DoesNotFire()
    {
        DoesNotHostWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();

        await dispatcher.DispatchAsync(ActiveInstance(CitizenWallet, 2), SealedTx(SealedTxId), default);

        _notifier.Verify(n => n.NotifyActionAvailableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_ReplaySameSealedTx_FiresExactlyOnce()
    {
        HostsWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();

        // Same sealed tx folded/dispatched twice (replay, restart, or rebuild).
        await dispatcher.DispatchAsync(ActiveInstance(CitizenWallet, 2), SealedTx(SealedTxId), default);
        await dispatcher.DispatchAsync(ActiveInstance(CitizenWallet, 2), SealedTx(SealedTxId), default);

        _notifier.Verify(n => n.NotifyActionAvailableAsync("inst-1", CitizenWallet, "2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_DifferentSealedTx_FiresAgain()
    {
        HostsWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();

        await dispatcher.DispatchAsync(ActiveInstance(CitizenWallet, 2), SealedTx("tx-1"), default);
        await dispatcher.DispatchAsync(ActiveInstance(CitizenWallet, 3), SealedTx("tx-2"), default);

        _notifier.Verify(n => n.NotifyActionAvailableAsync("inst-1", CitizenWallet, "2", It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(n => n.NotifyActionAvailableAsync("inst-1", CitizenWallet, "3", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_AmbiguousAssignee_DoesNotFire()
    {
        HostsWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();
        // Two participant wallets ⇒ assignee ambiguous ⇒ no per-wallet signal (unchanged from pre-US2).
        var instance = ActiveInstance(CitizenWallet, 2);
        instance.ParticipantWallets["agent"] = "ws-agent-2";

        await dispatcher.DispatchAsync(instance, SealedTx(SealedTxId), default);

        _notifier.Verify(n => n.NotifyActionAvailableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchAsync_WorkflowCompleted_NotifiesEntitledParticipantsOnce()
    {
        HostsWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();

        await dispatcher.DispatchAsync(CompletedInstance(CitizenWallet), SealedTx(SealedTxId), default);

        _notifier.Verify(n => n.NotifyWorkflowCompletedAsync(
            "inst-1", It.Is<IEnumerable<string>>(w => w.Contains(CitizenWallet)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WorkflowCompleted_Replay_NotifiesOnce()
    {
        HostsWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();

        await dispatcher.DispatchAsync(CompletedInstance(CitizenWallet), SealedTx(SealedTxId), default);
        await dispatcher.DispatchAsync(CompletedInstance(CitizenWallet), SealedTx(SealedTxId), default);

        _notifier.Verify(n => n.NotifyWorkflowCompletedAsync(
            "inst-1", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WorkflowCompleted_NotEntitled_DoesNotNotify()
    {
        DoesNotHostWallet(CitizenWallet);
        var dispatcher = CreateDispatcher();

        await dispatcher.DispatchAsync(CompletedInstance(CitizenWallet), SealedTx(SealedTxId), default);

        _notifier.Verify(n => n.NotifyWorkflowCompletedAsync(
            It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
