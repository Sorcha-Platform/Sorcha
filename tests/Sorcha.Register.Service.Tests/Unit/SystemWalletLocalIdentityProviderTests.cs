// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Register.Core.LocalRelationship;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.SystemWallet;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Feature 108 — the node's identity must come from the system wallet it actually signs with.
/// <para>
/// Found live on n1: <c>LocalIdentity:*</c> is configured on no deployment, so the static provider
/// resolved an <b>empty</b> identity and every register derived as <c>IsSubscriber</c> — the node was
/// a "subscriber" to the system register it owns, validates and seals. The peer service then tried to
/// pull that register from peers that do not exist, leaving it permanently "Syncing" (surfaced as
/// "Recovery"), and the T017 roster-based sealer selection always answered "subscriber".
/// </para>
/// </summary>
public class SystemWalletLocalIdentityProviderTests
{
    private static readonly byte[] DocketSigningKey = [1, 2, 3, 4, 5, 6, 7, 8];
    private const string SystemWalletAddress = "ws11qsystemwallet";

    private static SystemWalletLocalIdentityProvider Create(
        Mock<ISystemWalletSigningService> signing,
        LocalIdentityOptions? configured = null)
    {
        var monitor = new Mock<IOptionsMonitor<LocalIdentityOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(configured ?? new LocalIdentityOptions());

        return new SystemWalletLocalIdentityProvider(
            signing.Object, monitor.Object, NullLogger<SystemWalletLocalIdentityProvider>.Instance);
    }

    private static Mock<ISystemWalletSigningService> SigningWallet()
    {
        var signing = new Mock<ISystemWalletSigningService>();
        signing.Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                "sorcha:docket-signing", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSignResult
            {
                Signature = [9, 9],
                PublicKey = DocketSigningKey,
                Algorithm = "ED25519",
                WalletAddress = SystemWalletAddress,
            });
        return signing;
    }

    [Fact]
    public async Task GetAsync_NothingConfigured_ResolvesTheValidatorKeyFromTheSystemWallet()
    {
        // The n1 case: no LocalIdentity config at all. The node must still know its own validator key
        // — it is the key on the roster of every register it seals.
        var provider = Create(SigningWallet());

        var identity = await provider.GetAsync();

        identity.ValidatorPublicKey.Should().Equal(DocketSigningKey);
        identity.WalletAddresses.Should().Contain(SystemWalletAddress);
    }

    [Fact]
    public async Task GetAsync_DerivesUnderTheDocketSigningContext_SoItMatchesTheRoster()
    {
        // The roster entry is populated from the sorcha:docket-signing key (RegisterCreationOrchestrator).
        // Derive under any other context and the node would fail to recognise its own roster entry.
        var signing = SigningWallet();
        var provider = Create(signing);

        await provider.GetAsync();

        signing.Verify(s => s.SignAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            "sorcha:docket-signing", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WalletUnreachable_FailsSafeToSubscriber()
    {
        // A node that cannot prove its validator key must claim nothing — never assert rights it
        // cannot demonstrate.
        var signing = new Mock<ISystemWalletSigningService>();
        signing.Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("wallet service is down"));

        var identity = await Create(signing).GetAsync();

        identity.ValidatorPublicKey.Should().BeNull();
        identity.WalletAddresses.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_ConfiguredIdentity_WinsAndSkipsTheWallet()
    {
        // The config seam stays authoritative (dev stacks / tests pin a deterministic identity).
        var signing = SigningWallet();
        var configured = new LocalIdentityOptions
        {
            WalletAddresses = ["ws11qconfigured"],
            ValidatorPublicKeyBase64 = Convert.ToBase64String(new byte[] { 42 }),
        };

        var identity = await Create(signing, configured).GetAsync();

        identity.ValidatorPublicKey.Should().Equal([42]);
        identity.WalletAddresses.Should().ContainSingle().Which.Should().Be("ws11qconfigured");
        signing.Verify(s => s.SignAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_IsCached_SoTheWalletIsHitOnce()
    {
        var signing = SigningWallet();
        var provider = Create(signing);

        await provider.GetAsync();
        await provider.GetAsync();
        await provider.GetAsync();

        signing.Verify(s => s.SignAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
