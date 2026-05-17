// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Tests for the <see cref="CitizenWalletEndpoints"/> <c>RevokeDevice</c> handler
/// (Feature 114, T116/T117 — US3 PR2b). Mirrors the reflection-based static-handler
/// invocation pattern from <see cref="CitizenWalletEnrolEndpointTests"/>.
/// </summary>
public sealed class CitizenWalletRevokeEndpointTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();

    private readonly Mock<IPlatformUserDeviceClient> _deviceClient = new();
    private readonly Mock<IDeviceRevocationService> _revocation = new();
    // Phase 2c — RevokeDevice now drops a Category=Security inbox entry via
    // ICitizenDeviceInboxWriter. The mock lets the existing assertions pass
    // untouched (writer is fail-safe and irrelevant to status-code paths).
    private readonly Mock<Sorcha.Wallet.Service.Services.Implementation.ICitizenDeviceInboxWriter> _inboxWriter = new();

    private static HttpContext BuildHttpContext(
        Guid? platformUserId = null,
        Guid? orgId = null)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        if (orgId is not null)
            claims.Add(new Claim("org_id", orgId.Value.ToString()));
        // Wallet address claim is required by ResolveCitizenContext but unused for revoke.
        claims.Add(new Claim("wallet_address", "ws1qcitizen1"));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private async Task<IResult> InvokeAsync(Guid deviceId, HttpContext context)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "RevokeDevice",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("RevokeDevice handler should exist");

        var result = method.Invoke(null, [
            deviceId,
            context,
            _deviceClient.Object,
            _revocation.Object,
            _inboxWriter.Object,
            NullLogger<Program>.Instance,
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }

    private static PlatformUserDeviceLookupResult Lookup(int statusListId = 2, int statusListIndex = 9876) => new(
        DeviceId: Guid.NewGuid(),
        PlatformUserId: PlatformUserId,
        Label: "Stuart's iPhone",
        DevicePublicJwkThumbprint: "thumb",
        DevicePublicJwkJson: """{"kty":"EC","crv":"P-256","x":"x","y":"y"}""",
        Platform: "iOS 19",
        Status: "Active",
        EnrolledAt: DateTimeOffset.UtcNow.AddDays(-30),
        DelegationExpiresAt: DateTimeOffset.UtcNow.AddDays(335),
        DelegationCredentialJti: "jti-1",
        StatusListId: statusListId,
        StatusListIndex: statusListIndex);

    [Fact]
    public async Task Revoke_NoPlatformUserClaim_ReturnsUnauthorized()
    {
        var ctx = BuildHttpContext(platformUserId: null, orgId: OrgId);
        var result = await InvokeAsync(Guid.NewGuid(), ctx);
        result.GetType().Name.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task Revoke_NoOrgClaim_ReturnsUnauthorized()
    {
        var ctx = BuildHttpContext(platformUserId: PlatformUserId, orgId: null);
        var result = await InvokeAsync(Guid.NewGuid(), ctx);
        result.GetType().Name.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task Revoke_TenantLookupReturnsNull_ReturnsNotFound()
    {
        _deviceClient.Setup(c => c.GetByIdAsync(It.IsAny<Guid>(), PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformUserDeviceLookupResult?)null);

        var ctx = BuildHttpContext(PlatformUserId, OrgId);
        var result = await InvokeAsync(Guid.NewGuid(), ctx);

        result.GetType().Name.Should().Contain("NotFound");
        _revocation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Revoke_HappyPath_FlipsStatusListAndUpdatesTenantAndReturnsNoContent()
    {
        var deviceId = Guid.NewGuid();
        var lookup = Lookup(statusListId: 2, statusListIndex: 9876);
        _deviceClient.Setup(c => c.GetByIdAsync(deviceId, PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lookup);
        _deviceClient.Setup(c => c.RevokeAsync(deviceId, PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctx = BuildHttpContext(PlatformUserId, OrgId);
        var result = await InvokeAsync(deviceId, ctx);

        result.GetType().Name.Should().Contain("NoContent");

        // Status-list flip uses (orgId from JWT, listId/index from Tenant lookup,
        // deviceId for the SignalR payload, platformUserId for the group key).
        _revocation.Verify(r => r.RevokeAsync(
                OrgId, 2, 9876, deviceId, PlatformUserId, It.IsAny<CancellationToken>()),
            Times.Once);

        // Tenant row revoke must follow.
        _deviceClient.Verify(c => c.RevokeAsync(deviceId, PlatformUserId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Revoke_TenantRevokeReturnsFalse_StillReturnsNoContent()
    {
        // Concurrent revoke: lookup found the device but the follow-up revoke
        // 404s because someone else revoked it in between. End-state is correct.
        var deviceId = Guid.NewGuid();
        _deviceClient.Setup(c => c.GetByIdAsync(deviceId, PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Lookup());
        _deviceClient.Setup(c => c.RevokeAsync(deviceId, PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ctx = BuildHttpContext(PlatformUserId, OrgId);
        var result = await InvokeAsync(deviceId, ctx);

        result.GetType().Name.Should().Contain("NoContent");
    }
}
