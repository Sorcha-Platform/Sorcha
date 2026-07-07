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
using Sorcha.ServiceClients.Wallet;

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
}
