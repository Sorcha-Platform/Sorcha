// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.Wallet.Service.Endpoints;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Tests for the <see cref="CitizenWalletEndpoints"/> <c>ListDevices</c> and
/// <c>UpdateDeviceLabel</c> handlers (Feature 114, T118 — US3 PR3). Mirrors the
/// reflection-based static-handler invocation pattern from
/// <see cref="CitizenWalletEnrolEndpointTests"/>.
/// </summary>
public sealed class CitizenWalletDeviceListAndRenameTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();

    private readonly Mock<IPlatformUserDeviceClient> _deviceClient = new();

    private static HttpContext BuildHttpContext(Guid? platformUserId = null)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        // ResolveCitizenContext also reads wallet_address; not required for these endpoints.
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private async Task<IResult> InvokeListAsync(HttpContext context)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "ListDevices",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("ListDevices handler should exist");
        var result = method.Invoke(null, [context, _deviceClient.Object, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private async Task<IResult> InvokeRenameAsync(Guid deviceId, DeviceLabelUpdateRequest body, HttpContext context)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "UpdateDeviceLabel",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("UpdateDeviceLabel handler should exist");
        var result = method.Invoke(null, [deviceId, body, context, _deviceClient.Object, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    private static PlatformUserDeviceLookupResult Lookup(Guid deviceId, string label, string status = "Active") => new(
        DeviceId: deviceId,
        PlatformUserId: PlatformUserId,
        Label: label,
        DevicePublicJwkThumbprint: "thumb",
        DevicePublicJwkJson: """{"kty":"EC","crv":"P-256","x":"x","y":"y"}""",
        Platform: "iOS 19",
        Status: status,
        EnrolledAt: DateTimeOffset.UtcNow.AddDays(-30),
        DelegationExpiresAt: DateTimeOffset.UtcNow.AddDays(335),
        DelegationCredentialJti: "jti",
        StatusListId: 0,
        StatusListIndex: 1);

    // ----- ListDevices -----

    [Fact]
    public async Task List_NoPlatformUserClaim_ReturnsUnauthorized()
    {
        var ctx = BuildHttpContext(platformUserId: null);
        var result = await InvokeListAsync(ctx);
        result.GetType().Name.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task List_HappyPath_ReturnsMappedSummariesIncludingRevoked()
    {
        var active = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        _deviceClient.Setup(c => c.ListAsync(PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformUserDeviceLookupResult>
            {
                Lookup(active, "Active phone", status: "Active"),
                Lookup(revoked, "Lost phone", status: "Revoked")
            });

        var result = await InvokeListAsync(BuildHttpContext(PlatformUserId));

        result.GetType().Name.Should().Contain("Ok");
        var value = result.GetType().GetProperty("Value")!.GetValue(result) as DeviceListResponse;
        value.Should().NotBeNull();
        value!.Devices.Should().HaveCount(2);
        value.Devices.Single(d => d.DeviceId == active).Status.Should().Be(DeviceStatus.Active);
        value.Devices.Single(d => d.DeviceId == revoked).Status.Should().Be(DeviceStatus.Revoked);
    }

    // ----- UpdateDeviceLabel -----

    [Fact]
    public async Task Rename_NoPlatformUserClaim_ReturnsUnauthorized()
    {
        var ctx = BuildHttpContext(platformUserId: null);
        var result = await InvokeRenameAsync(Guid.NewGuid(), new DeviceLabelUpdateRequest { Label = "x" }, ctx);
        result.GetType().Name.Should().Contain("Unauthorized");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_BlankLabel_ReturnsValidationProblem(string label)
    {
        var result = await InvokeRenameAsync(
            Guid.NewGuid(),
            new DeviceLabelUpdateRequest { Label = label },
            BuildHttpContext(PlatformUserId));
        result.GetType().Name.Should().Contain("ProblemHttpResult");
    }

    [Fact]
    public async Task Rename_LabelTooLong_ReturnsValidationProblem()
    {
        var result = await InvokeRenameAsync(
            Guid.NewGuid(),
            new DeviceLabelUpdateRequest { Label = new string('x', 121) },
            BuildHttpContext(PlatformUserId));
        result.GetType().Name.Should().Contain("ProblemHttpResult");
    }

    [Fact]
    public async Task Rename_HappyPath_DispatchesToTenantAndReturnsNoContent()
    {
        var deviceId = Guid.NewGuid();
        _deviceClient.Setup(c => c.UpdateLabelAsync(deviceId, PlatformUserId, "Stuart's NEW phone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await InvokeRenameAsync(
            deviceId,
            new DeviceLabelUpdateRequest { Label = "Stuart's NEW phone" },
            BuildHttpContext(PlatformUserId));

        result.GetType().Name.Should().Contain("NoContent");
        _deviceClient.Verify(c => c.UpdateLabelAsync(deviceId, PlatformUserId, "Stuart's NEW phone", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rename_TenantReturnsFalse_ReturnsNotFound()
    {
        var deviceId = Guid.NewGuid();
        _deviceClient.Setup(c => c.UpdateLabelAsync(deviceId, PlatformUserId, "x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await InvokeRenameAsync(
            deviceId,
            new DeviceLabelUpdateRequest { Label = "x" },
            BuildHttpContext(PlatformUserId));

        result.GetType().Name.Should().Contain("NotFound");
    }
}
