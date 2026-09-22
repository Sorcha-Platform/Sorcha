// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Middleware;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Feature 176 — the disclosed-data endpoint handler. Focuses on caller-wallet resolution (fast-path
/// claim + Wallet-Service fallback), forwarding of the resolved wallets and delegation token to the
/// shared <see cref="IActionDisclosureResolver"/>, and the recipient-resolved semantics that drive the
/// agent's fail-closed hold. The disclosure filtering itself is covered by ActionDisclosureResolverTests.
/// </summary>
public class WorkflowDisclosureEndpointsTests
{
    private const string InstanceId = "inst-1";
    private const string AnalystWallet = "wsAnalyst";
    private const int ActionId = 2;

    private static HttpContext ContextWith(
        (string Type, string Value)[] claims, string? delegationToken = null)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test");
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        if (delegationToken is not null)
        {
            ctx.Items[DelegationTokenMiddleware.DelegationTokenKey] = delegationToken;
        }

        return ctx;
    }

    private static WalletInfo Wallet(string address) => new()
    {
        Address = address, Name = "w", PublicKey = "pk", Algorithm = "ED25519",
        Status = "Active", Owner = "owner", Tenant = "tenant",
    };

    private static DisclosedActionData Resolved(bool recipientResolved) => new()
    {
        InstanceId = InstanceId,
        ActionId = ActionId,
        RegisterId = "reg-1",
        RecipientResolved = recipientResolved,
        DisclosedFields = recipientResolved
            ? new Dictionary<string, object> { ["address"] = "SW1A 1AA" }
            : new Dictionary<string, object>(),
    };

    [Fact]
    public async Task GetDisclosures_RecipientCaller_FastPathClaim_ReturnsResolverData()
    {
        var resolver = new Mock<IActionDisclosureResolver>();
        resolver.Setup(r => r.ResolveDisclosedDataAsync(
                InstanceId, ActionId,
                It.Is<IReadOnlyCollection<string>>(w => w.Contains(AnalystWallet)),
                "delg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(recipientResolved: true));

        var wallet = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await WorkflowDisclosureEndpoints.GetDisclosuresAsync(
            ContextWith([("wallet_address", AnalystWallet)], delegationToken: "delg"),
            InstanceId, ActionId, resolver.Object, wallet.Object, NullLogger.Instance);

        var ok = result.Should().BeOfType<Ok<DisclosedActionData>>().Subject;
        ok.Value!.RecipientResolved.Should().BeTrue();
        ok.Value.DisclosedFields.Should().ContainKey("address");
        // Fast path — no Wallet Service lookup needed.
        wallet.Verify(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDisclosures_NonRecipient_ReturnsRecipientNotResolved()
    {
        var resolver = new Mock<IActionDisclosureResolver>();
        resolver.Setup(r => r.ResolveDisclosedDataAsync(
                InstanceId, ActionId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(recipientResolved: false));

        var result = await WorkflowDisclosureEndpoints.GetDisclosuresAsync(
            ContextWith([("wallet_address", "wsStranger")]),
            InstanceId, ActionId, resolver.Object, new Mock<IWalletServiceClient>().Object, NullLogger.Instance);

        var ok = result.Should().BeOfType<Ok<DisclosedActionData>>().Subject;
        ok.Value!.RecipientResolved.Should().BeFalse();
        ok.Value.DisclosedFields.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDisclosures_NoWalletClaim_ResolvesCallerViaWalletServiceFallback()
    {
        // Consumer-tier token: no wallet_address claim; wallet is owned by platform_user_id (#912).
        var wallet = new Mock<IWalletServiceClient>();
        wallet.Setup(c => c.GetWalletsByOwnerAsync("platform-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Wallet(AnalystWallet)]);

        var resolver = new Mock<IActionDisclosureResolver>();
        resolver.Setup(r => r.ResolveDisclosedDataAsync(
                InstanceId, ActionId,
                It.Is<IReadOnlyCollection<string>>(w => w.Contains(AnalystWallet)),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Resolved(recipientResolved: true));

        var result = await WorkflowDisclosureEndpoints.GetDisclosuresAsync(
            ContextWith([("platform_user_id", "platform-1"), (ClaimTypes.NameIdentifier, "sub-1")]),
            InstanceId, ActionId, resolver.Object, wallet.Object, NullLogger.Instance);

        result.Should().BeOfType<Ok<DisclosedActionData>>()
            .Which.Value!.RecipientResolved.Should().BeTrue();
        wallet.Verify(c => c.GetWalletsByOwnerAsync("platform-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDisclosures_NoWalletsResolvable_ShortCircuitsWithoutCallingResolver()
    {
        var resolver = new Mock<IActionDisclosureResolver>(MockBehavior.Strict);
        var wallet = new Mock<IWalletServiceClient>();
        wallet.Setup(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await WorkflowDisclosureEndpoints.GetDisclosuresAsync(
            ContextWith([("platform_user_id", "nobody")]),
            InstanceId, ActionId, resolver.Object, wallet.Object, NullLogger.Instance);

        var ok = result.Should().BeOfType<Ok<DisclosedActionData>>().Subject;
        ok.Value!.RecipientResolved.Should().BeFalse();
        resolver.Verify(r => r.ResolveDisclosedDataAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Instance-wide route (#1678) -----------------------------------------------------------

    private const int CompletedActionId = 2;
    private const int OtherCompletedActionId = 1;
    private const int BranchActionA = 2;
    private const int BranchActionB = 3;

    private static DisclosedActionData ResolvedFor(int actionId, string field, string value) => new()
    {
        InstanceId = InstanceId,
        ActionId = actionId,
        RegisterId = "reg-1",
        RecipientResolved = true,
        Disclosures =
        [
            new DisclosedActionEntry
            {
                ActionId = actionId,
                ActionTitle = $"action-{actionId}",
                Data = new Dictionary<string, object> { [field] = value },
            },
        ],
        DisclosedFields = new Dictionary<string, object> { [field] = value },
    };

    private static DisclosedActionData NothingFor(int actionId) => new()
    {
        InstanceId = InstanceId,
        ActionId = actionId,
        RegisterId = "reg-1",
        RecipientResolved = false,
    };

    private static Instance CompletedInstance() => new()
    {
        Id = InstanceId,
        BlueprintId = "bp-1",
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "t-1",
        State = InstanceState.Completed,
        CurrentActionIds = [], // terminal — the #1678 defect
    };

    private static Instance ActiveInstanceWithBranches() => new()
    {
        Id = InstanceId,
        BlueprintId = "bp-1",
        BlueprintVersion = 1,
        RegisterId = "reg-1",
        TenantId = "t-1",
        State = InstanceState.Active,
        CurrentActionIds = [BranchActionA, BranchActionB],
    };

    private static BlueprintModel BlueprintWithActions(params int[] actionIds) => new()
    {
        Id = "bp-1",
        Title = "Test Blueprint",
        Actions = actionIds.Select(id => new ActionModel { Id = id, Title = $"action-{id}" }).ToList(),
    };

    /// <summary>
    /// #1678 — a COMPLETED instance has an empty <c>CurrentActionIds</c>. The endpoint must still
    /// surface the disclosures the caller could read before completion, not silently answer empty.
    /// </summary>
    [Fact]
    public async Task GetInstanceDisclosures_CompletedInstance_ReturnsDisclosuresFromCompletedActions()
    {
        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CompletedInstance());

        var blueprintStore = new Mock<IBlueprintStore>();
        blueprintStore.Setup(s => s.GetAsync("bp-1"))
            .ReturnsAsync(BlueprintWithActions(OtherCompletedActionId, CompletedActionId));

        // Strict: the buggy code anchors on the sentinel 0, which has no setup here and throws.
        var resolver = new Mock<IActionDisclosureResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveDisclosedDataAsync(
                InstanceId, CompletedActionId, It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedFor(CompletedActionId, "address", "SW1A 1AA"));
        resolver.Setup(r => r.ResolveDisclosedDataAsync(
                InstanceId, OtherCompletedActionId, It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NothingFor(OtherCompletedActionId));

        var wallet = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await WorkflowDisclosureEndpoints.GetInstanceDisclosuresAsync(
            ContextWith([("wallet_address", AnalystWallet)], delegationToken: "delg"),
            InstanceId, resolver.Object, wallet.Object, instanceStore.Object, blueprintStore.Object,
            NullLogger.Instance);

        var ok = result.Should().BeOfType<Ok<DisclosedActionData>>().Subject;
        ok.Value!.RecipientResolved.Should().BeTrue(
            "the same disclosures visible before completion must still be visible after it");
        ok.Value.DisclosedFields.Should().ContainKey("address");
        ok.Value.ActionId.Should().BeNull("instance-wide queries never anchor on one action id");
    }

    /// <summary>
    /// #1678 second defect — an ACTIVE instance sitting on two parallel current actions must not have
    /// the second one silently ignored.
    /// </summary>
    [Fact]
    public async Task GetInstanceDisclosures_ActiveInstanceWithParallelBranches_MergesBothBranches()
    {
        var instanceStore = new Mock<IInstanceStore>();
        instanceStore.Setup(s => s.GetAsync(InstanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveInstanceWithBranches());

        var blueprintStore = new Mock<IBlueprintStore>(MockBehavior.Strict); // must not be consulted — CurrentActionIds is non-empty

        var resolver = new Mock<IActionDisclosureResolver>(MockBehavior.Strict);
        resolver.Setup(r => r.ResolveDisclosedDataAsync(
                InstanceId, BranchActionA, It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedFor(BranchActionA, "branchAField", "from-a"));
        resolver.Setup(r => r.ResolveDisclosedDataAsync(
                InstanceId, BranchActionB, It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedFor(BranchActionB, "branchBField", "from-b"));

        var wallet = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await WorkflowDisclosureEndpoints.GetInstanceDisclosuresAsync(
            ContextWith([("wallet_address", AnalystWallet)], delegationToken: "delg"),
            InstanceId, resolver.Object, wallet.Object, instanceStore.Object, blueprintStore.Object,
            NullLogger.Instance);

        var ok = result.Should().BeOfType<Ok<DisclosedActionData>>().Subject;
        ok.Value!.DisclosedFields.Should().ContainKey("branchAField", "the first branch must still be visible");
        ok.Value.DisclosedFields.Should().ContainKey("branchBField", "the second branch must not be silently dropped");
        resolver.Verify(r => r.ResolveDisclosedDataAsync(
            InstanceId, BranchActionB, It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
