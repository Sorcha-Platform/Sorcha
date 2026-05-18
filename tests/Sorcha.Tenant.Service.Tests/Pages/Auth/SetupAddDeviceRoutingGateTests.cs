// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Sorcha.Tenant.Service.Pages.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Pages.Auth;

/// <summary>
/// Tests for <see cref="SetupAddDeviceRoutingGate"/>. The gate is the shared
/// source of truth for the F128 FR-020/FR-026 routing decision across three
/// post-login fragment-redirect sites (Login, OidcCallback, SocialCallback).
/// </summary>
public sealed class SetupAddDeviceRoutingGateTests
{
    private readonly Mock<IPlatformUserDeviceService> _deviceService = new();

    [Fact]
    public async Task ShouldRouteAsync_Returns_True_When_Token_Has_Pid_And_Zero_Devices()
    {
        var pid = Guid.NewGuid();
        _deviceService
            .Setup(d => d.HasAnyAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (DateTimeOffset?)null));

        var result = await SetupAddDeviceRoutingGate.ShouldRouteAsync(
            BuildToken(pid), _deviceService.Object, NullLogger.Instance, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRouteAsync_Returns_False_When_Citizen_Has_At_Least_One_Device()
    {
        var pid = Guid.NewGuid();
        _deviceService
            .Setup(d => d.HasAnyAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, DateTimeOffset.UtcNow));

        var result = await SetupAddDeviceRoutingGate.ShouldRouteAsync(
            BuildToken(pid), _deviceService.Object, NullLogger.Instance, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRouteAsync_Returns_False_When_Token_Has_No_PlatformUserId_Claim()
    {
        var result = await SetupAddDeviceRoutingGate.ShouldRouteAsync(
            BuildToken(platformUserId: null),
            _deviceService.Object,
            NullLogger.Instance,
            CancellationToken.None);

        result.Should().BeFalse();
        _deviceService.Verify(
            d => d.HasAnyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ShouldRouteAsync_Returns_False_And_Logs_When_Token_Is_Garbage()
    {
        var result = await SetupAddDeviceRoutingGate.ShouldRouteAsync(
            "not-a-valid-jwt",
            _deviceService.Object,
            NullLogger.Instance,
            CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRouteAsync_Returns_False_When_DeviceService_Throws()
    {
        var pid = Guid.NewGuid();
        _deviceService
            .Setup(d => d.HasAnyAsync(pid, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await SetupAddDeviceRoutingGate.ShouldRouteAsync(
            BuildToken(pid), _deviceService.Object, NullLogger.Instance, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public void SetupAddDevicePath_Carries_The_Load_Bearing_App_Prefix()
    {
        // Pinning constant — F128 #775 root cause was the missing /app/ prefix
        // causing NavigationManager.NavigateTo to resolve as origin-absolute and
        // bypass the WASM client's base href. Pin the prefix here so a careless
        // edit can't silently regress it.
        SetupAddDeviceRoutingGate.SetupAddDevicePath.Should().Be("/app/setup/add-device");
    }

    private static string BuildToken(Guid? platformUserId)
    {
        var claims = new List<Claim>();
        if (platformUserId is { } pid)
        {
            claims.Add(new Claim("platform_user_id", pid.ToString()));
        }
        var token = new JwtSecurityToken(
            issuer: "test",
            audience: "test",
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(5));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
