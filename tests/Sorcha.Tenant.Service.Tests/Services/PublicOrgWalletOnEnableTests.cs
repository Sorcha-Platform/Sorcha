// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// The public organisation gets its signing wallet when it is first enabled, and its recovery
/// phrase goes to the system administrator doing the enabling (#1530).
/// </summary>
/// <remarks>
/// <para>
/// Every other organisation's wallet is created by an administrator OF that organisation, because
/// the BIP39 phrase is shown once, never stored and cannot be reissued (#1525). The public
/// organisation has no administrator of its own, so nobody could do that for it — and it was left
/// with no wallet at all, which is why callers resorted to auto-subscribing it with someone else's
/// credentials and got a 403.
/// </para>
/// <para>
/// The two properties that matter, and the reason both are tested: the phrase must actually come
/// back (a create that discards it is the original defect), and a second enable must never re-mint
/// (replacing the wallet orphans anything already issued under the old one).
/// </para>
/// </remarks>
public sealed class PublicOrgWalletOnEnableTests : IDisposable
{
    private readonly TenantDbContext _db;
    private readonly Mock<IWalletServiceClient> _wallet = new();

    public PublicOrgWalletOnEnableTests()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();

        _db.PlatformSettings.Add(new PlatformSettings { PublicOrgEnabled = false });
        _db.Organizations.Add(new Organization
        {
            Id = WellKnownIds.PublicOrgId,
            Name = "Sorcha Public",
            Subdomain = "public",
            Status = OrganizationStatus.Suspended
        });
        _db.SaveChanges();

        _wallet.Setup(w => w.CreatePlatformOrgWalletAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orgId, string? _, string? __, CancellationToken ___) =>
                new PlatformOrgWalletResult(
                    orgId, "ws11qpublicorgwallet", "pk-public", "ED25519",
                    ["abandon", "ability", "able", "about", "above", "absent",
                     "absorb", "abstract", "absurd", "abuse", "access", "accident"]));
    }

    private PlatformSettingsService Sut() =>
        new(_db, NullLogger<PlatformSettingsService>.Instance, _wallet.Object);

    [Fact]
    public async Task FirstEnable_CreatesTheWalletAndReturnsTheRecoveryPhrase()
    {
        var result = await Sut().UpdatePublicOrgEnabledAsync(enabled: true, updatedBy: Guid.NewGuid());

        result.WalletAddress.Should().Be("ws11qpublicorgwallet");
        result.MnemonicWords.Should().NotBeNull().And.HaveCount(12,
            "the phrase must reach the administrator — a create that discards it is the defect #1525 exists to stop");

        var org = await _db.Organizations.FirstAsync(o => o.Id == WellKnownIds.PublicOrgId);
        org.WalletAddress.Should().Be("ws11qpublicorgwallet");
        org.PublicKey.Should().Be("pk-public");
        org.SigningAlgorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task SecondEnable_DoesNotMintAgainAndReturnsNoPhrase()
    {
        var sut = Sut();
        await sut.UpdatePublicOrgEnabledAsync(enabled: true, updatedBy: Guid.NewGuid());

        var again = await sut.UpdatePublicOrgEnabledAsync(enabled: true, updatedBy: Guid.NewGuid());

        again.WalletAddress.Should().Be("ws11qpublicorgwallet", "it keeps the wallet it already had");
        again.MnemonicWords.Should().BeNull(
            "the phrase exists once; re-issuing one would mean a different wallet");
        _wallet.Verify(w => w.CreatePlatformOrgWalletAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "replacing the canonical wallet would orphan anything already issued under the old one");
    }

    [Fact]
    public async Task Disabling_NeverCreatesAWallet()
    {
        var result = await Sut().UpdatePublicOrgEnabledAsync(enabled: false, updatedBy: Guid.NewGuid());

        result.MnemonicWords.Should().BeNull();
        _wallet.Verify(w => w.CreatePlatformOrgWalletAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WalletServiceUnavailable_StillEnables_ButReportsNoWallet()
    {
        // Enabling must not fail because the wallet could not be minted — the organisation being
        // open for self-registration is the point of the call. It reports the gap instead.
        _wallet.Setup(w => w.CreatePlatformOrgWalletAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformOrgWalletResult?)null);

        var result = await Sut().UpdatePublicOrgEnabledAsync(enabled: true, updatedBy: Guid.NewGuid());

        result.Settings.PublicOrgEnabled.Should().BeTrue();
        result.WalletAddress.Should().BeNull();
        result.MnemonicWords.Should().BeNull();
    }

    public void Dispose() => _db.Dispose();
}
