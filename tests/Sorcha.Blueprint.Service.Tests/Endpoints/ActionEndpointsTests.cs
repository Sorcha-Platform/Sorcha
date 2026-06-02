// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Feature 145 / #912 — the pending-actions endpoint must resolve the caller's wallet by
/// <c>platform_user_id</c> (the cross-org Owner key per #878), not just <c>sub</c>. A consumer-tier
/// token (e.g. the verification analyst running the rules agent) carries no <c>wallet_address</c>
/// claim and its wallet is owned by <c>platform_user_id</c>, which differs from <c>sub</c> for an
/// org-scoped user — so a sub-only lookup silently misses the wallet and the agent never discovers
/// the action it must approve.
/// </summary>
public class ActionEndpointsTests
{
    private static HttpContext ContextWith(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static WalletInfo Wallet(string address) =>
        new()
        {
            Address = address, Name = "w", PublicKey = "pk", Algorithm = "ED25519",
            Status = "Active", Owner = "owner", Tenant = "tenant",
        };

    private static Mock<IWalletServiceClient> WalletClientReturning(
        params (string Owner, string[] Addresses)[] byOwner)
    {
        var mock = new Mock<IWalletServiceClient>();
        mock.Setup(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string owner, CancellationToken _) =>
            {
                var match = byOwner.FirstOrDefault(b => b.Owner == owner);
                return (match.Addresses ?? Array.Empty<string>()).Select(Wallet).ToList();
            });
        return mock;
    }

    [Fact]
    public async Task Resolve_WalletAddressClaimPresent_UsesFastPath_NoWalletServiceCall()
    {
        var wallet = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await ActionEndpoints.ResolveUserWalletAddressesAsync(
            ContextWith(("wallet_address", "ws1-fast"), ("sub", "sub-1")),
            wallet.Object, NullLogger.Instance, CancellationToken.None);

        result.Should().Equal("ws1-fast");
        wallet.Verify(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_NoWalletAddressClaim_PrefersPlatformUserIdOwner()
    {
        // The wallet is owned by platform_user_id (the #878 alignment). A sub-keyed lookup would
        // miss it; the platform_user_id-keyed lookup finds it — the #912 agent-discovery fix.
        var wallet = WalletClientReturning(
            ("platform-1", new[] { "ws1-analyst" }),
            ("sub-1", Array.Empty<string>()));

        var result = await ActionEndpoints.ResolveUserWalletAddressesAsync(
            ContextWith(("platform_user_id", "platform-1"), (ClaimTypes.NameIdentifier, "sub-1")),
            wallet.Object, NullLogger.Instance, CancellationToken.None);

        result.Should().Equal("ws1-analyst");
        wallet.Verify(c => c.GetWalletsByOwnerAsync("platform-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_PlatformUserIdHasNoWallet_FallsBackToSubOwner()
    {
        // Legacy / pre-#878 wallet owned by sub: platform_user_id misses, sub hits.
        var wallet = WalletClientReturning(
            ("platform-1", Array.Empty<string>()),
            ("sub-1", new[] { "ws1-legacy" }));

        var result = await ActionEndpoints.ResolveUserWalletAddressesAsync(
            ContextWith(("platform_user_id", "platform-1"), (ClaimTypes.NameIdentifier, "sub-1")),
            wallet.Object, NullLogger.Instance, CancellationToken.None);

        result.Should().Equal("ws1-legacy");
        wallet.Verify(c => c.GetWalletsByOwnerAsync("platform-1", It.IsAny<CancellationToken>()), Times.Once);
        wallet.Verify(c => c.GetWalletsByOwnerAsync("sub-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_NoIdentifyingClaims_ReturnsEmpty()
    {
        var wallet = new Mock<IWalletServiceClient>(MockBehavior.Strict);

        var result = await ActionEndpoints.ResolveUserWalletAddressesAsync(
            ContextWith(("some-other-claim", "x")),
            wallet.Object, NullLogger.Instance, CancellationToken.None);

        result.Should().BeEmpty();
        wallet.Verify(c => c.GetWalletsByOwnerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
