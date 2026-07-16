// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.Cryptography;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

using Xunit;

namespace Sorcha.Wallet.Service.Tests.Credentials;

/// <summary>
/// Tests the <see cref="DeviceBoundCopyIssuanceCoordinator"/> mint-path wiring
/// (Feature 1195, Phase 2, Task 5): the device-vs-root discriminator, the cap/eviction
/// policy invocation, the F114 status-slot allocation, and the fail-closed abort.
/// </summary>
public class DeviceBoundCopyIssuanceCoordinatorTests
{
    private const string Recipient = "ws1qcitizen1";
    private const string Vct = "https://credentials.sorcha.dev/assured-identity";
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrgId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // A device cnf JWK whose thumbprint is deterministic via the production helper.
    private static readonly JsonElement DeviceJwk = JsonSerializer.Deserialize<JsonElement>(
        """{"kty":"EC","crv":"P-256","x":"device-x-coordinate-value-000000000000000000","y":"device-y-coordinate-value-000000000000000000"}""");

    private static string DeviceThumbprint => JsonWebKeyThumbprint.Compute(DeviceJwk);

    private readonly Mock<IHolderAddressLookup> _holderAddress = new();
    private readonly Mock<IHolderKeyService> _holderKey = new();
    private readonly Mock<IDeviceBoundCredentialPolicy> _policy = new();
    private readonly Mock<IDeviceBoundCredentialLookup> _lookup = new();
    private readonly Mock<IDeviceBoundCredentialRevoker> _revoker = new();
    private readonly Mock<ICitizenStatusListPublisher> _statusList = new();
    private readonly Mock<IOrgStatusSigningWalletResolver> _orgResolver = new();

    private DeviceBoundCopyIssuanceCoordinator CreateCoordinator() =>
        new(_holderAddress.Object, _holderKey.Object, _policy.Object, _lookup.Object,
            _revoker.Object, _statusList.Object, _orgResolver.Object,
            NullLogger<DeviceBoundCopyIssuanceCoordinator>.Instance);

    private void ArrangeCitizenWithDeviceCnf()
    {
        _holderAddress.Setup(l => l.ResolvePlatformUserIdAsync(Recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId);
        // Holder key thumbprint differs from the device cnf → it's a device copy.
        _holderKey.Setup(k => k.GetHolderJwkThumbprintAsync(Recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync("holder-thumbprint-not-the-device");
    }

    private void ArrangeStatusAllocation(int listId, int index)
    {
        _orgResolver.Setup(r => r.ResolveAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ws1qorgstatus");
        _statusList.Setup(s => s.AllocateIndexAsync(OrgId, "ws1qorgstatus", It.IsAny<CancellationToken>()))
            .ReturnsAsync((listId, index));
        _statusList.Setup(s => s.BuildStatusListUri(OrgId, listId))
            .Returns($"https://n1.sorcha.dev/api/v1/wallet/status/{OrgId:N}/citizen-devices/{listId}.statuslist+jwt");
    }

    [Fact]
    public async Task PrepareAsync_DeviceBoundCopy_InvokesReconcileWithUserTypeAndThumbprint()
    {
        ArrangeCitizenWithDeviceCnf();
        _policy.Setup(p => p.ReconcileAsync(UserId, Vct, DeviceThumbprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceBindDisposition(DeviceBindKind.NewWithinCap, null));
        ArrangeStatusAllocation(listId: 0, index: 5);

        var plan = await CreateCoordinator().PrepareAsync(Recipient, Vct, DeviceJwk, OrgId, default);

        plan.Should().NotBeNull();
        plan!.StatusListIndex.Should().Be(5);
        plan.StatusListUrl.Should().Contain("citizen-devices/0.statuslist+jwt");
        _policy.Verify(p => p.ReconcileAsync(UserId, Vct, DeviceThumbprint, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrepareAsync_HolderBoundWebRoot_ReturnsNullAndDoesNotCallPolicy()
    {
        _holderAddress.Setup(l => l.ResolvePlatformUserIdAsync(Recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId);
        // Holder key thumbprint EQUALS the incoming cnf → this is the web root.
        _holderKey.Setup(k => k.GetHolderJwkThumbprintAsync(Recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeviceThumbprint);

        var plan = await CreateCoordinator().PrepareAsync(Recipient, Vct, DeviceJwk, OrgId, default);

        plan.Should().BeNull();
        _policy.Verify(
            p => p.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _statusList.Verify(
            s => s.AllocateIndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PrepareAsync_NonCitizenRecipient_ReturnsNullAndDoesNotCallPolicy()
    {
        _holderAddress.Setup(l => l.ResolvePlatformUserIdAsync(Recipient, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var plan = await CreateCoordinator().PrepareAsync(Recipient, Vct, DeviceJwk, OrgId, default);

        plan.Should().BeNull();
        _policy.Verify(
            p => p.ReconcileAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PrepareAsync_FourthDistinctDevice_EvictsOldestAndReturnsPlanForNewCopy()
    {
        ArrangeCitizenWithDeviceCnf();

        // Use the REAL policy so eviction happens end-to-end: three distinct live copies,
        // a fourth distinct device → the oldest is revoked and a plan is returned so the
        // new copy is issued.
        var now = DateTimeOffset.UtcNow;
        var oldest = new DeviceBoundCredentialCopy("cred-oldest", "thumb-A", now.AddDays(-10), Guid.NewGuid(), "old");
        _lookup.Setup(l => l.GetLiveCopiesAsync(UserId, Vct, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeviceBoundCredentialCopy>
            {
                new("cred-2", "thumb-B", now.AddDays(-1), Guid.NewGuid(), "b"),
                oldest,
                new("cred-3", "thumb-C", now.AddDays(-5), Guid.NewGuid(), "c"),
            });
        _revoker.Setup(r => r.RevokeAsync(UserId, It.Is<DeviceBoundCredentialCopy>(c => c.CredentialId == "cred-oldest"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var inbox = new Mock<ICitizenDeviceInboxWriter>();
        inbox.Setup(i => i.WriteDeviceRevokedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var realPolicy = new DeviceBoundCredentialPolicy(
            _lookup.Object, _revoker.Object, inbox.Object, NullLogger<DeviceBoundCredentialPolicy>.Instance);
        ArrangeStatusAllocation(listId: 1, index: 9);

        var coordinator = new DeviceBoundCopyIssuanceCoordinator(
            _holderAddress.Object, _holderKey.Object, realPolicy, _lookup.Object,
            _revoker.Object, _statusList.Object, _orgResolver.Object,
            NullLogger<DeviceBoundCopyIssuanceCoordinator>.Instance);

        var plan = await coordinator.PrepareAsync(Recipient, Vct, DeviceJwk, OrgId, default);

        plan.Should().NotBeNull("the new (4th) copy must still be issued after eviction");
        plan!.StatusListIndex.Should().Be(9);
        _revoker.Verify(
            r => r.RevokeAsync(UserId, It.Is<DeviceBoundCredentialCopy>(c => c.CredentialId == "cred-oldest"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PrepareAsync_ReconcileThrows_PropagatesAndAllocatesNoSlot()
    {
        ArrangeCitizenWithDeviceCnf();
        _policy.Setup(p => p.ReconcileAsync(UserId, Vct, DeviceThumbprint, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("status list unavailable"));

        var act = async () => await CreateCoordinator().PrepareAsync(Recipient, Vct, DeviceJwk, OrgId, default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("status list unavailable");
        // No partial state: a slot is never allocated when the policy aborts.
        _statusList.Verify(
            s => s.AllocateIndexAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PrepareAsync_ReplaceExisting_RevokesPriorSameThumbprintCopyThenPlans()
    {
        ArrangeCitizenWithDeviceCnf();
        _policy.Setup(p => p.ReconcileAsync(UserId, Vct, DeviceThumbprint, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceBindDisposition(DeviceBindKind.ReplaceExisting, null));

        var prior = new DeviceBoundCredentialCopy("cred-prior", DeviceThumbprint, DateTimeOffset.UtcNow.AddDays(-3), Guid.NewGuid(), "same-device");
        _lookup.Setup(l => l.GetLiveCopiesAsync(UserId, Vct, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeviceBoundCredentialCopy> { prior });
        _revoker.Setup(r => r.RevokeAsync(UserId, It.Is<DeviceBoundCredentialCopy>(c => c.CredentialId == "cred-prior"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ArrangeStatusAllocation(listId: 0, index: 2);

        var plan = await CreateCoordinator().PrepareAsync(Recipient, Vct, DeviceJwk, OrgId, default);

        plan.Should().NotBeNull();
        _revoker.Verify(
            r => r.RevokeAsync(UserId, It.Is<DeviceBoundCredentialCopy>(c => c.CredentialId == "cred-prior"), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
