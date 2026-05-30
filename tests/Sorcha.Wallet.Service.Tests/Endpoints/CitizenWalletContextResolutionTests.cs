// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Moq;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Endpoints;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Tests for <see cref="CitizenWalletEndpoints.ResolveCitizenContextAsync"/> — the consumer-token
/// wallet resolution that repairs the Feature 136 / Feature 137 gap: consumer-tier tokens omit the
/// <c>wallet_address</c> claim (wallet binding is platform-only), so the citizen-wallet endpoints
/// must resolve the wallet from the owner (the <c>sub</c> / NameIdentifier) instead of the claim.
/// </summary>
public sealed class CitizenWalletContextResolutionTests
{
    private const string Owner = "97bb186d-e4f4-46ea-a878-4ada3f0130f8";
    private const string CitizenWallet = "ws11qr977yn3citizenwalletaddress";

    private static ClaimsPrincipal Principal(bool withWalletClaim)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Owner),
            new("platform_user_id", "e2b28602-03a4-4da9-96b3-1c43408f2640"),
            new("org_id", "00000000-0000-0000-0000-000000000002")
        };
        if (withWalletClaim) claims.Add(new Claim("wallet_address", "ws11qPLATFORMtokenwallet"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static WalletEntity TestWallet(string address, WalletStatus status = WalletStatus.Active) => new()
    {
        Address = address,
        EncryptedPrivateKey = "enc",
        EncryptionKeyId = "k1",
        Algorithm = "ED25519",
        Owner = Owner,
        Tenant = "default",
        Name = "Citizen Wallet",
        PublicKey = "pk",
        Status = status,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task ConsumerToken_NoWalletClaim_ResolvesWalletByOwner()
    {
        var repo = new Mock<IWalletRepository>();
        repo.Setup(r => r.GetByOwnerAsync(Owner, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestWallet(CitizenWallet) });

        var (pid, wallet, org) = await CitizenWalletEndpoints.ResolveCitizenContextAsync(
            Principal(withWalletClaim: false), repo.Object, CancellationToken.None);

        wallet.Should().Be(CitizenWallet, "a consumer token with no wallet_address resolves via owner lookup");
        pid.Should().NotBeNull();
        org.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000002"));
    }

    [Fact]
    public async Task WalletClaimPresent_UsesClaim_AndDoesNotQueryRepo()
    {
        var repo = new Mock<IWalletRepository>(MockBehavior.Strict);

        var (_, wallet, _) = await CitizenWalletEndpoints.ResolveCitizenContextAsync(
            Principal(withWalletClaim: true), repo.Object, CancellationToken.None);

        wallet.Should().Be("ws11qPLATFORMtokenwallet");
        repo.Verify(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NoWalletForOwner_ReturnsNullWallet()
    {
        var repo = new Mock<IWalletRepository>();
        repo.Setup(r => r.GetByOwnerAsync(Owner, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WalletEntity>());

        var (_, wallet, _) = await CitizenWalletEndpoints.ResolveCitizenContextAsync(
            Principal(withWalletClaim: false), repo.Object, CancellationToken.None);

        wallet.Should().BeNull();
    }

    [Fact]
    public async Task PrefersActiveWallet_OverArchived()
    {
        var repo = new Mock<IWalletRepository>();
        repo.Setup(r => r.GetByOwnerAsync(Owner, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                TestWallet("ws11qARCHIVED", WalletStatus.Archived),
                TestWallet(CitizenWallet, WalletStatus.Active)
            });

        var (_, wallet, _) = await CitizenWalletEndpoints.ResolveCitizenContextAsync(
            Principal(withWalletClaim: false), repo.Object, CancellationToken.None);

        wallet.Should().Be(CitizenWallet);
    }

    [Fact]
    public async Task PrefersPlatformUserIdOwnerOverNameIdentifier()
    {
        // Cleanup of the Smell-1 architectural inconsistency: post-#878 WalletEndpoints.GetCurrentUser
        // mints Wallets.Owner from the JWT platform_user_id claim (cross-org persistent identity)
        // rather than NameIdentifier (per-org). The citizen-side resolver must therefore try the
        // platform_user_id lookup FIRST so new-shape wallets are discoverable. Without this
        // preference, a citizen would have to fall through to the legacy-sub branch — fine when no
        // legacy wallet exists, but a wallet that happens to be co-owned by a wallet-under-sub
        // would be mistakenly chosen on the first hit.
        const string PlatformOwner = "e2b28602-03a4-4da9-96b3-1c43408f2640";
        const string PlatformWallet = "ws11qPLATFORMERAcitizenwallet";

        var repo = new Mock<IWalletRepository>();
        repo.Setup(r => r.GetByOwnerAsync(PlatformOwner, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestWallet(PlatformWallet) });
        // Sub-keyed lookup is a fallback path and must NOT be reached when platform_user_id hits.
        repo.Setup(r => r.GetByOwnerAsync(Owner, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestWallet("ws11qLEGACYsubwallet") });

        var (_, wallet, _) = await CitizenWalletEndpoints.ResolveCitizenContextAsync(
            Principal(withWalletClaim: false), repo.Object, CancellationToken.None);

        wallet.Should().Be(PlatformWallet, "platform_user_id-owned wallets are the new canonical shape");
        repo.Verify(r => r.GetByOwnerAsync(Owner, "default", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LegacyPath_FallsBackToNameIdentifierOwner()
    {
        // Legacy era support: any wallet that pre-dates #878 still carries Owner=sub (= NameIdentifier).
        // The resolver tries the new platform_user_id key first, then falls back to the sub-keyed
        // lookup so existing-on-disk citizen wallets stay discoverable forever.
        const string PlatformOwner = "e2b28602-03a4-4da9-96b3-1c43408f2640";
        const string LegacyWallet = "ws11qLEGACYsubwallet";

        var repo = new Mock<IWalletRepository>();
        // Platform_user_id-keyed lookup returns empty (no new-shape wallet exists for this user).
        repo.Setup(r => r.GetByOwnerAsync(PlatformOwner, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WalletEntity>());
        // Sub-keyed lookup finds the legacy wallet.
        repo.Setup(r => r.GetByOwnerAsync(Owner, "default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TestWallet(LegacyWallet) });

        var (_, wallet, _) = await CitizenWalletEndpoints.ResolveCitizenContextAsync(
            Principal(withWalletClaim: false), repo.Object, CancellationToken.None);

        wallet.Should().Be(LegacyWallet);
    }
}
