// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Wallet;

using Xunit;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// Cold-start run #5 — an agent must be able to discover its own signing identity.
/// </summary>
/// <remarks>
/// <c>sorcha_wallet_info</c> is a lookup BY ADDRESS, so it can only confirm a wallet you can
/// already name. Both agents concluded they held none, bound their organisation's wallet to their
/// role instead, and were refused when they tried to sign. One of them put the gap exactly: "the
/// only way to find it would be to publish without a wallet address and let it default, which
/// writes to the register." Discovering your own identity should not require a ledger write.
/// </remarks>
public sealed class MyWalletsToolTests
{
    private const string Personal = "ws11qqzq42w3y80d367f6a2ft0szr0tdttm97alxrsvrwtq7zxtrax0dsq07pez";
    private const string Org = "ws11qr3tj8tm6dtxh8fn5aw3qu0kz2x8ty7llat9t9sdnfru5eyh2asrsjgh32w";

    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();
    private readonly Mock<IWalletServiceClient> _wallet = new();

    private MyWalletsTool Tool() => new(
        _auth.Object, _availability.Object, _wallet.Object, Mock.Of<ILogger<MyWalletsTool>>());

    private void Allow()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_my_wallets")).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Wallet")).Returns(true);
    }

    private void Holds(params string[] addresses) =>
        _wallet.Setup(w => w.GetMyWalletsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerWalletLookup.Answered(addresses.Select(a => new Sorcha.ServiceClients.Wallet.WalletInfo
            {
                Address = a, Name = "w-" + a, PublicKey = "pk-" + a, Algorithm = "ED25519",
                Status = "Active", Owner = "user-1", Tenant = "t",
            }).ToList()));

    [Fact]
    public async Task ListsTheAddressesTheCallerCanSignWith()
    {
        Allow();
        Holds(Personal, Org);

        var result = await Tool().GetMyWalletsAsync();

        result.Status.Should().Be("Success");
        result.Wallets.Select(w => w.Address).Should().Equal(Personal, Org);
        // The public key matters: it is what a participant record carries.
        result.Wallets[0].PublicKey.Should().Be("pk-" + Personal);
        result.Wallets[0].Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task WhenTheCallerGenuinelyHoldsNone_SaysSoAndWhatToDo()
    {
        Allow();
        Holds();

        var result = await Tool().GetMyWalletsAsync();

        result.Status.Should().Be("Success");
        result.Wallets.Should().BeEmpty();
        result.Message.Should().Contain("You hold no wallet").And.Contain("Create one");
    }

    [Fact]
    public async Task WhenTheLookupFails_SaysUnknownRatherThanNone()
    {
        // The whole point. Reporting "you hold no wallet" on the strength of a failed read is what
        // sent both of run #5's agents to bind the wrong wallet.
        Allow();
        _wallet.Setup(w => w.GetMyWalletsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerWalletLookup.Unavailable("the wallet service answered 403"));

        var result = await Tool().GetMyWalletsAsync();

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("could not be read").And.Contain("not the same as holding none");
        result.Message.Should().NotContain("You hold no wallet");
        result.Wallets.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenUnauthorized_ReturnsUnauthorizedAndAsksTheWalletServiceNothing()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_my_wallets")).Returns(false);

        var result = await Tool().GetMyWalletsAsync();

        result.Status.Should().Be("Unauthorized");
        _wallet.Verify(w => w.GetMyWalletsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenTheWalletServiceIsDown_SaysUnavailableWithoutCalling()
    {
        _auth.Setup(x => x.CanInvokeTool("sorcha_my_wallets")).Returns(true);
        _availability.Setup(x => x.IsServiceAvailable("Wallet")).Returns(false);

        var result = await Tool().GetMyWalletsAsync();

        result.Status.Should().Be("Unavailable");
        _wallet.Verify(w => w.GetMyWalletsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
