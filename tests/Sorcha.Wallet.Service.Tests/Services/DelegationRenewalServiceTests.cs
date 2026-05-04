// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="DelegationRenewalService"/> (Feature 114, T106).
/// Verifies device-not-found / not-active rejection paths, the happy renewal
/// path (re-issue + Tenant record refresh), and that the Tenant record refresh
/// reuses the existing JWK thumbprint so the row is updated in place rather
/// than duplicated.
/// </summary>
public sealed class DelegationRenewalServiceTests
{
    private static readonly Guid PlatformUserId = Guid.Parse("d8b04e5a-0000-0000-0000-000000000001");
    private static readonly Guid OrgId = Guid.Parse("0a000000-0000-0000-0000-000000000001");
    private static readonly Guid DeviceId = Guid.Parse("0d000000-0000-0000-0000-000000000001");
    private const string CitizenWallet = "ws1qcitizenexamplewalletaddress";
    private const string OrgSigningWallet = "ws1qorgsigningwallet";

    private readonly Mock<IPlatformUserDeviceClient> _devices = new();
    private readonly Mock<IDeviceDelegationIssuer> _issuer = new();
    private readonly Mock<IOrgStatusSigningWalletResolver> _orgResolver = new();
    private readonly DelegationRenewalService _sut;

    public DelegationRenewalServiceTests()
    {
        _orgResolver.Setup(o => o.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrgSigningWallet);
        _sut = new DelegationRenewalService(
            _devices.Object, _issuer.Object, _orgResolver.Object,
            NullLogger<DelegationRenewalService>.Instance);
    }

    [Fact]
    public async Task RenewAsync_DeviceNotFound_ReturnsNull()
    {
        _devices.Setup(c => c.GetByIdAsync(DeviceId, PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserDeviceLookupResult?)null);

        var result = await _sut.RenewAsync(DeviceId, PlatformUserId, CitizenWallet, OrgId);

        result.Should().BeNull();
        _issuer.Verify(i => i.IssueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<string>(), It.IsAny<EcP256PublicJwk>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RenewAsync_DeviceRevoked_ReturnsNull()
    {
        _devices.Setup(c => c.GetByIdAsync(DeviceId, PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LookupResult(status: "Revoked"));

        var result = await _sut.RenewAsync(DeviceId, PlatformUserId, CitizenWallet, OrgId);

        result.Should().BeNull("revoked devices must not have their delegations renewed");
    }

    [Fact]
    public async Task RenewAsync_HappyPath_ReissuesAndRefreshesTenantRecord()
    {
        var lookup = LookupResult();
        _devices.Setup(c => c.GetByIdAsync(DeviceId, PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lookup);

        var newExp = DateTimeOffset.UtcNow.AddYears(1);
        var newJti = "fresh-jti-" + Guid.NewGuid().ToString("N")[..8];
        EcP256PublicJwk? sentJwk = null;
        _issuer.Setup(i => i.IssueAsync(
                PlatformUserId, CitizenWallet, OrgId, OrgSigningWallet,
                It.IsAny<EcP256PublicJwk>(), lookup.Label, lookup.Platform, It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Guid, string, EcP256PublicJwk, string, string, CancellationToken>(
                (_, _, _, _, jwk, _, _, _) => sentJwk = jwk)
            .ReturnsAsync(new DeviceDelegationResult(
                CompactJwt: "eyJ.fresh.delegation",
                Jti: newJti,
                ExpiresAt: newExp,
                StatusListUri: "https://verify.test/status/0.statuslist+jwt",
                StatusListIndex: lookup.StatusListIndex,
                StatusListId: 0,
                HolderPublicJwk: JsonSerializer.Deserialize<JsonElement>(
                    """{"kty":"EC","crv":"P-256","x":"hx","y":"hy"}""")));

        _devices.Setup(c => c.RegisterAsync(
                PlatformUserId, lookup.Label, lookup.DevicePublicJwkThumbprint,
                lookup.DevicePublicJwkJson, lookup.Platform, string.Empty,
                newExp, newJti, lookup.StatusListId, lookup.StatusListIndex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserDeviceRegistrationResult(DeviceId, lookup.EnrolledAt));

        var result = await _sut.RenewAsync(DeviceId, PlatformUserId, CitizenWallet, OrgId);

        result.Should().NotBeNull();
        result!.DelegationCredential.Should().Be("eyJ.fresh.delegation");
        result.ExpiresAt.Should().Be(newExp);

        sentJwk.Should().NotBeNull();
        sentJwk!.X.Should().Be("test-x", "the issuer must be called with the device JWK from the Tenant lookup");

        // Tenant record refresh must reuse the existing thumbprint so it lands in the
        // existing row (idempotent on (PlatformUserId, Thumbprint)).
        _devices.Verify(c => c.RegisterAsync(
            PlatformUserId, lookup.Label, lookup.DevicePublicJwkThumbprint,
            lookup.DevicePublicJwkJson, lookup.Platform, string.Empty,
            newExp, newJti, lookup.StatusListId, lookup.StatusListIndex, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PlatformUserDeviceLookupResult LookupResult(string status = "Active") => new(
        DeviceId: DeviceId,
        PlatformUserId: PlatformUserId,
        Label: "Stuart's iPhone",
        DevicePublicJwkThumbprint: "thumbprint-1234567890abcdef0123456789abcdef0123456",
        DevicePublicJwkJson: """{"kty":"EC","crv":"P-256","x":"test-x","y":"test-y"}""",
        Platform: "iOS 19 / Safari 19",
        Status: status,
        EnrolledAt: DateTimeOffset.UtcNow.AddDays(-300),
        DelegationExpiresAt: DateTimeOffset.UtcNow.AddDays(20), // 20 days from expiry → renewal due
        DelegationCredentialJti: "old-jti",
        StatusListId: 0,
        StatusListIndex: 7);
}
