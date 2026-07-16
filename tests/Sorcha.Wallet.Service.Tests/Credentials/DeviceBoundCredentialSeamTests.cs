// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Cryptography;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Service.Tests.Services;

using Xunit;

namespace Sorcha.Wallet.Service.Tests.Credentials;

/// <summary>
/// Tests the two concrete policy seams (Feature 1195, Phase 2, Task 5):
/// <see cref="EfCoreDeviceBoundCredentialLookup"/> (excludes the holder-bound root by cnf
/// thumbprint) and <see cref="DeviceBoundCredentialRevoker"/> (flips the F114 status bit
/// and marks the copy Revoked).
/// </summary>
public sealed class DeviceBoundCredentialSeamTests : IDisposable
{
    private const string Wallet = "ws1qcitizen1";
    private const string Vct = "https://credentials.sorcha.dev/assured-identity";
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OrgId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly JsonElement HolderJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"OKP","crv":"Ed25519","x":"holder-key-x-value-00000000000000000000000"}""");
    private static readonly JsonElement DeviceJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"EC","crv":"P-256","x":"device-key-x-0000000000000000000000000000000","y":"device-key-y-0000000000000000000000000000000"}""");

    private readonly TestCitizenWalletDbContext _db;

    public DeviceBoundCredentialSeamTests()
    {
        var options = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"seam-{Guid.NewGuid()}")
            .Options;
        _db = new TestCitizenWalletDbContext(options);
        _db.CitizenHolderIndex.Add(new CitizenHolderIndex
        {
            WalletAddress = Wallet,
            PlatformUserId = UserId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Builds a minimal SD-JWT VC whose issuer-signed body carries the given cnf JWK.</summary>
    private static string RawTokenWithCnf(JsonElement cnfJwk)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"EdDSA","typ":"vc+sd-jwt"}"""));
        var body = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new Dictionary<string, object> { ["vct"] = Vct, ["cnf"] = new { jwk = cnfJwk } })));
        return $"{header}.{body}.signature~";
    }

    /// <summary>Builds an SD-JWT VC body carrying the IETF status.status_list claim (register-copy shape).</summary>
    private static string RawTokenWithIetfStatus(string uri, int idx)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"EdDSA","typ":"vc+sd-jwt"}"""));
        var body = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new Dictionary<string, object>
            {
                ["vct"] = Vct,
                ["cnf"] = new { jwk = DeviceJwk },
                ["status"] = new { status_list = new { uri, idx } },
            })));
        return $"{header}.{body}.signature~";
    }

    [Fact]
    public async Task Lookup_ExcludesHolderBoundRoot_ReturnsOnlyDeviceCopies()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialEntity>
            {
                Credential("root", RawTokenWithCnf(HolderJwk), CredentialStatus.Active),
                Credential("device", RawTokenWithCnf(DeviceJwk), CredentialStatus.Active),
                Credential("revoked-device", RawTokenWithCnf(DeviceJwk), CredentialStatus.Revoked),
            });

        var holderKey = new Mock<IHolderKeyService>();
        holderKey.Setup(k => k.GetHolderJwkThumbprintAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonWebKeyThumbprint.Compute(HolderJwk));

        var lookup = new EfCoreDeviceBoundCredentialLookup(
            _db, store.Object, holderKey.Object, NullLogger<EfCoreDeviceBoundCredentialLookup>.Instance);

        var copies = await lookup.GetLiveCopiesAsync(UserId, Vct, default);

        copies.Should().ContainSingle("only the live device copy counts (root excluded, revoked excluded)");
        copies[0].CredentialId.Should().Be("device");
        copies[0].DeviceKeyThumbprint.Should().Be(JsonWebKeyThumbprint.Compute(DeviceJwk));
    }

    [Fact]
    public async Task Revoker_FlipsF114StatusBitAndMarksCopyRevoked()
    {
        var statusUrl = $"https://n1.sorcha.dev/api/v1/wallet/status/{OrgId:N}/citizen-devices/2.statuslist+jwt";
        var entity = Credential("device", RawTokenWithCnf(DeviceJwk), CredentialStatus.Active);
        entity.StatusListUrl = statusUrl;
        entity.StatusListIndex = 42;

        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByIdForWalletAsync("device", Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        store.Setup(s => s.UpdateStatusAsync("device", Wallet, CredentialStatus.Revoked, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var statusList = new Mock<ICitizenStatusListPublisher>();
        statusList.Setup(s => s.FlipAsync(OrgId, 2, 42, "ws1qorgstatus", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var orgResolver = new Mock<IOrgStatusSigningWalletResolver>();
        orgResolver.Setup(r => r.ResolveAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ws1qorgstatus");

        var revoker = new DeviceBoundCredentialRevoker(
            _db, store.Object, statusList.Object, orgResolver.Object,
            NullLogger<DeviceBoundCredentialRevoker>.Instance);

        var copy = new DeviceBoundCredentialCopy("device", JsonWebKeyThumbprint.Compute(DeviceJwk),
            DateTimeOffset.UtcNow, Guid.Empty, null);

        await revoker.RevokeAsync(UserId, copy, default);

        statusList.Verify(s => s.FlipAsync(OrgId, 2, 42, "ws1qorgstatus", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.UpdateStatusAsync("device", Wallet, CredentialStatus.Revoked, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoker_RegisterDeliveredCopy_ReadsIetfStatusClaimFromToken()
    {
        // A citizen copy delivered via the register path has NO StatusListUrl/Index columns
        // (InboundCredentialDetector does not set them) — the allocation must be read from the
        // signed IETF status claim in the token.
        var statusUrl = $"https://n1.sorcha.dev/api/v1/wallet/status/{OrgId:N}/citizen-devices/3.statuslist+jwt";
        var entity = Credential("device", RawTokenWithIetfStatus(statusUrl, 17), CredentialStatus.Active);
        // Deliberately leave StatusListUrl/StatusListIndex null.

        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByIdForWalletAsync("device", Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        store.Setup(s => s.UpdateStatusAsync("device", Wallet, CredentialStatus.Revoked, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var statusList = new Mock<ICitizenStatusListPublisher>();
        statusList.Setup(s => s.FlipAsync(OrgId, 3, 17, "ws1qorgstatus", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var orgResolver = new Mock<IOrgStatusSigningWalletResolver>();
        orgResolver.Setup(r => r.ResolveAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ws1qorgstatus");

        var revoker = new DeviceBoundCredentialRevoker(
            _db, store.Object, statusList.Object, orgResolver.Object,
            NullLogger<DeviceBoundCredentialRevoker>.Instance);

        var copy = new DeviceBoundCredentialCopy("device", JsonWebKeyThumbprint.Compute(DeviceJwk),
            DateTimeOffset.UtcNow, Guid.Empty, null);

        await revoker.RevokeAsync(UserId, copy, default);

        statusList.Verify(s => s.FlipAsync(OrgId, 3, 17, "ws1qorgstatus", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoker_NoStatusAllocation_Throws()
    {
        var entity = Credential("device", RawTokenWithCnf(DeviceJwk), CredentialStatus.Active); // no StatusListUrl/Index
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByIdForWalletAsync("device", Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var revoker = new DeviceBoundCredentialRevoker(
            _db, store.Object, Mock.Of<ICitizenStatusListPublisher>(), Mock.Of<IOrgStatusSigningWalletResolver>(),
            NullLogger<DeviceBoundCredentialRevoker>.Instance);

        var copy = new DeviceBoundCredentialCopy("device", "thumb", DateTimeOffset.UtcNow, Guid.Empty, null);

        var act = async () => await revoker.RevokeAsync(UserId, copy, default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static CredentialEntity Credential(string id, string rawToken, CredentialStatus status) => new()
    {
        Id = id,
        Type = Vct,
        IssuerDid = "did:sorcha:org:issuer",
        SubjectDid = Wallet,
        ClaimsJson = "{}",
        RawToken = rawToken,
        Status = status,
        WalletAddress = Wallet,
        IssuedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
