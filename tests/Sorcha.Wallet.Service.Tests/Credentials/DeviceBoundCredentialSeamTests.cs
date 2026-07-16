// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Cryptography;
using Sorcha.ServiceClients.PlatformUserDevice;
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

        var deviceClient = new Mock<IPlatformUserDeviceClient>();
        deviceClient.Setup(c => c.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformUserDeviceLookupResult>());

        var lookup = new EfCoreDeviceBoundCredentialLookup(
            _db, store.Object, holderKey.Object, deviceClient.Object,
            NullLogger<EfCoreDeviceBoundCredentialLookup>.Instance);

        var copies = await lookup.GetLiveCopiesAsync(UserId, Vct, default);

        copies.Should().ContainSingle("only the live device copy counts (root excluded, revoked excluded)");
        copies[0].CredentialId.Should().Be("device");
        copies[0].DeviceKeyThumbprint.Should().Be(JsonWebKeyThumbprint.Compute(DeviceJwk));
    }

    private static PlatformUserDeviceLookupResult RegisteredDevice(Guid deviceId, string label, string thumbprint) =>
        new(DeviceId: deviceId,
            PlatformUserId: UserId,
            Label: label,
            DevicePublicJwkThumbprint: thumbprint,
            DevicePublicJwkJson: "{}",
            Platform: "iOS",
            Status: "Active",
            EnrolledAt: DateTimeOffset.UtcNow.AddDays(-30),
            DelegationExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
            DelegationCredentialJti: "jti-1",
            StatusListId: 0,
            StatusListIndex: 0);

    [Fact]
    public async Task Lookup_ThumbprintMatchesRegisteredDevice_PopulatesDeviceIdAndLabel()
    {
        // Fix round 1 (eviction notify): the copy's cnf thumbprint IS the Tenant registry's
        // DevicePublicJwkThumbprint, so the lookup resolves DeviceId/Label for the F118 notice.
        var deviceThumbprint = JsonWebKeyThumbprint.Compute(DeviceJwk);
        var registeredDeviceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialEntity>
            {
                Credential("device", RawTokenWithCnf(DeviceJwk), CredentialStatus.Active),
            });
        var holderKey = new Mock<IHolderKeyService>();
        holderKey.Setup(k => k.GetHolderJwkThumbprintAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonWebKeyThumbprint.Compute(HolderJwk));
        var deviceClient = new Mock<IPlatformUserDeviceClient>();
        deviceClient.Setup(c => c.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformUserDeviceLookupResult>
            {
                RegisteredDevice(Guid.NewGuid(), "Other phone", "some-other-thumbprint"),
                RegisteredDevice(registeredDeviceId, "Stuart's iPhone", deviceThumbprint),
            });

        var lookup = new EfCoreDeviceBoundCredentialLookup(
            _db, store.Object, holderKey.Object, deviceClient.Object,
            NullLogger<EfCoreDeviceBoundCredentialLookup>.Instance);

        var copies = await lookup.GetLiveCopiesAsync(UserId, Vct, default);

        var copy = copies.Should().ContainSingle().Subject;
        copy.DeviceId.Should().Be(registeredDeviceId, "the registry device with the matching thumbprint is the bound device");
        copy.DeviceLabel.Should().Be("Stuart's iPhone");
    }

    [Fact]
    public async Task Lookup_RegistryUnavailable_DegradesToDeterministicDeviceIdWithNullLabel()
    {
        // Fix round 1: a Tenant outage must be non-fatal to the mint. The copy is still
        // returned (the cap still counts it); the DeviceId degrades to a deterministic
        // thumbprint-derived Guid so the F118 notice still fires (never Guid.Empty, which
        // CitizenDeviceInboxWriter short-circuits into silence).
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialEntity>
            {
                Credential("device", RawTokenWithCnf(DeviceJwk), CredentialStatus.Active),
            });
        var holderKey = new Mock<IHolderKeyService>();
        holderKey.Setup(k => k.GetHolderJwkThumbprintAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonWebKeyThumbprint.Compute(HolderJwk));
        var deviceClient = new Mock<IPlatformUserDeviceClient>();
        deviceClient.Setup(c => c.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("tenant unreachable"));

        var lookup = new EfCoreDeviceBoundCredentialLookup(
            _db, store.Object, holderKey.Object, deviceClient.Object,
            NullLogger<EfCoreDeviceBoundCredentialLookup>.Instance);

        var copies = await lookup.GetLiveCopiesAsync(UserId, Vct, default);

        var copy = copies.Should().ContainSingle("registry outage must not hide live copies from the cap").Subject;
        copy.DeviceId.Should().NotBe(Guid.Empty, "the eviction notice must still fire (writer no-ops on Guid.Empty)");
        copy.DeviceLabel.Should().BeNull();
    }

    [Fact]
    public async Task Eviction_EndToEnd_NotifiesInboxWithRegistryResolvedDeviceIdAndLabel()
    {
        // Fix round 1, end-to-end: REAL lookup (thumbprints from stored tokens + faked Tenant
        // registry) feeding the REAL policy — a 4th distinct device evicts the oldest copy and
        // the F118 inbox notice carries the registry-resolved DeviceId + label.
        var oldestJwk = JsonSerializer.Deserialize<JsonElement>(
            """{"kty":"EC","crv":"P-256","x":"oldest-x-00000000000000000000000000000000000","y":"oldest-y-00000000000000000000000000000000000"}""");
        var secondJwk = JsonSerializer.Deserialize<JsonElement>(
            """{"kty":"EC","crv":"P-256","x":"second-x-00000000000000000000000000000000000","y":"second-y-00000000000000000000000000000000000"}""");
        var thirdJwk = JsonSerializer.Deserialize<JsonElement>(
            """{"kty":"EC","crv":"P-256","x":"third-x-000000000000000000000000000000000000","y":"third-y-000000000000000000000000000000000000"}""");

        var now = DateTimeOffset.UtcNow;
        var oldest = Credential("cred-oldest", RawTokenWithCnf(oldestJwk), CredentialStatus.Active);
        oldest.IssuedAt = now.AddDays(-10);
        var second = Credential("cred-2", RawTokenWithCnf(secondJwk), CredentialStatus.Active);
        second.IssuedAt = now.AddDays(-5);
        var third = Credential("cred-3", RawTokenWithCnf(thirdJwk), CredentialStatus.Active);
        third.IssuedAt = now.AddDays(-1);

        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialEntity> { second, oldest, third });

        var holderKey = new Mock<IHolderKeyService>();
        holderKey.Setup(k => k.GetHolderJwkThumbprintAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonWebKeyThumbprint.Compute(HolderJwk));

        var oldestDeviceId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var deviceClient = new Mock<IPlatformUserDeviceClient>();
        deviceClient.Setup(c => c.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformUserDeviceLookupResult>
            {
                RegisteredDevice(oldestDeviceId, "Old iPad", JsonWebKeyThumbprint.Compute(oldestJwk)),
                RegisteredDevice(Guid.NewGuid(), "Pixel", JsonWebKeyThumbprint.Compute(secondJwk)),
                RegisteredDevice(Guid.NewGuid(), "iPhone", JsonWebKeyThumbprint.Compute(thirdJwk)),
            });

        var lookup = new EfCoreDeviceBoundCredentialLookup(
            _db, store.Object, holderKey.Object, deviceClient.Object,
            NullLogger<EfCoreDeviceBoundCredentialLookup>.Instance);

        var revoker = new Mock<IDeviceBoundCredentialRevoker>();
        revoker.Setup(r => r.RevokeAsync(UserId, It.IsAny<DeviceBoundCredentialCopy>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var inbox = new Mock<ICitizenDeviceInboxWriter>();
        inbox.Setup(i => i.WriteDeviceRevokedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var policy = new DeviceBoundCredentialPolicy(
            lookup, revoker.Object, inbox.Object, NullLogger<DeviceBoundCredentialPolicy>.Instance);

        var result = await policy.ReconcileAsync(UserId, Vct, "thumbprint-of-a-4th-device", default);

        result.Kind.Should().Be(DeviceBindKind.NewWithEviction);
        result.EvictedCredentialId.Should().Be("cred-oldest");
        inbox.Verify(
            i => i.WriteDeviceRevokedAsync(UserId, oldestDeviceId, "Old iPad", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Eviction_RegistryUnavailable_StillEvictsAndNotifiesWithDegradedDeviceId()
    {
        // Fix round 1: Tenant outage is non-fatal — eviction (revoke) still happens, the
        // mint still proceeds (NewWithEviction returned), and the notice degrades to the
        // deterministic thumbprint-derived DeviceId with no label rather than going silent.
        var oldestJwk = JsonSerializer.Deserialize<JsonElement>(
            """{"kty":"EC","crv":"P-256","x":"oldest-x-00000000000000000000000000000000000","y":"oldest-y-00000000000000000000000000000000000"}""");
        var secondJwk = JsonSerializer.Deserialize<JsonElement>(
            """{"kty":"EC","crv":"P-256","x":"second-x-00000000000000000000000000000000000","y":"second-y-00000000000000000000000000000000000"}""");
        var thirdJwk = JsonSerializer.Deserialize<JsonElement>(
            """{"kty":"EC","crv":"P-256","x":"third-x-000000000000000000000000000000000000","y":"third-y-000000000000000000000000000000000000"}""");

        var now = DateTimeOffset.UtcNow;
        var oldest = Credential("cred-oldest", RawTokenWithCnf(oldestJwk), CredentialStatus.Active);
        oldest.IssuedAt = now.AddDays(-10);
        var second = Credential("cred-2", RawTokenWithCnf(secondJwk), CredentialStatus.Active);
        second.IssuedAt = now.AddDays(-5);
        var third = Credential("cred-3", RawTokenWithCnf(thirdJwk), CredentialStatus.Active);
        third.IssuedAt = now.AddDays(-1);

        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CredentialEntity> { second, oldest, third });
        var holderKey = new Mock<IHolderKeyService>();
        holderKey.Setup(k => k.GetHolderJwkThumbprintAsync(Wallet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonWebKeyThumbprint.Compute(HolderJwk));
        var deviceClient = new Mock<IPlatformUserDeviceClient>();
        deviceClient.Setup(c => c.ListAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("tenant unreachable"));

        var lookup = new EfCoreDeviceBoundCredentialLookup(
            _db, store.Object, holderKey.Object, deviceClient.Object,
            NullLogger<EfCoreDeviceBoundCredentialLookup>.Instance);

        var revoker = new Mock<IDeviceBoundCredentialRevoker>();
        revoker.Setup(r => r.RevokeAsync(UserId, It.IsAny<DeviceBoundCredentialCopy>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var inbox = new Mock<ICitizenDeviceInboxWriter>();
        inbox.Setup(i => i.WriteDeviceRevokedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var policy = new DeviceBoundCredentialPolicy(
            lookup, revoker.Object, inbox.Object, NullLogger<DeviceBoundCredentialPolicy>.Instance);

        var result = await policy.ReconcileAsync(UserId, Vct, "thumbprint-of-a-4th-device", default);

        result.Kind.Should().Be(DeviceBindKind.NewWithEviction, "the mint proceeds despite the registry outage");
        revoker.Verify(
            r => r.RevokeAsync(UserId, It.Is<DeviceBoundCredentialCopy>(c => c.CredentialId == "cred-oldest"), It.IsAny<CancellationToken>()),
            Times.Once);
        inbox.Verify(
            i => i.WriteDeviceRevokedAsync(UserId, It.Is<Guid>(g => g != Guid.Empty), null, It.IsAny<CancellationToken>()),
            Times.Once);
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
