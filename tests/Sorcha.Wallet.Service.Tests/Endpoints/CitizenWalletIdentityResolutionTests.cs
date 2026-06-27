// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.Wallet.Service.Endpoints;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Tests for the Feature 165 identity-resolution hardening in
/// <see cref="CitizenWalletEndpoints"/>. Covers the three-step precedence defined in
/// <c>contracts/citizen-identity-resolution.md</c>:
/// (1) <c>platform_user_id</c> claim → parse GUID → use (M-1);
/// (2) <c>sub</c> → identity-registry lookup → use recovered id (M-2);
/// (3) unresolvable → guidance/Unauthorized, not 500 (M-3).
/// </summary>
public sealed class CitizenWalletIdentityResolutionTests
{
    private static readonly Guid PlatformUserId = Guid.Parse("e2b28602-03a4-4da9-96b3-1c43408f2640");
    private static readonly Guid UserIdentityId = Guid.Parse("97bb186d-e4f4-46ea-a878-4ada3f0130f8");

    private static HttpContext BuildContext(
        bool withPlatformUserIdClaim,
        bool withSubClaim,
        Mock<IPlatformInboxClient>? inboxClient = null)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (withPlatformUserIdClaim)
            claims.Add(new Claim("platform_user_id", PlatformUserId.ToString()));
        if (withSubClaim)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, UserIdentityId.ToString()));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        if (inboxClient is not null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(inboxClient.Object);
            services.AddLogging();
            ctx.RequestServices = services.BuildServiceProvider();
        }

        return ctx;
    }

    private static async Task<IResult> InvokeListDevices(
        HttpContext context, IPlatformUserDeviceClient deviceClient)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "ListDevices", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("ListDevices handler must exist");
        return await (Task<IResult>)method.Invoke(null, [context, deviceClient, CancellationToken.None])!;
    }

    private static Mock<IPlatformUserDeviceClient> EmptyDeviceClient(Guid forPlatformUserId)
    {
        var mock = new Mock<IPlatformUserDeviceClient>();
        mock.Setup(c => c.ListAsync(forPlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformUserDeviceLookupResult>());
        return mock;
    }

    // ── M-1: platform_user_id claim present ──────────────────────────────────

    [Fact]
    public async Task ListDevices_WithPlatformUserIdClaim_UsesClaimDirectly_NoRegistryCall()
    {
        // M-1: when the claim is present, resolution is byte-for-byte unchanged.
        // The inbox client must NOT be called (strict mock enforces this).
        var inboxMock = new Mock<IPlatformInboxClient>(MockBehavior.Strict);
        var ctx = BuildContext(withPlatformUserIdClaim: true, withSubClaim: false, inboxClient: inboxMock);
        var deviceClient = EmptyDeviceClient(PlatformUserId);

        var result = await InvokeListDevices(ctx, deviceClient.Object);

        result.GetType().Name.Should().Contain("Ok");
        deviceClient.Verify(c => c.ListAsync(PlatformUserId, It.IsAny<CancellationToken>()), Times.Once,
            "the resolved platform user id must be used for the device list call");
        inboxMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListDevices_WithPlatformUserIdClaim_ReturnsEmptyList_NotUnauthorized()
    {
        // A citizen with no devices yet — empty list is success (M-3: wallet-less is not 401).
        var ctx = BuildContext(withPlatformUserIdClaim: true, withSubClaim: false);
        var deviceClient = EmptyDeviceClient(PlatformUserId);

        var result = await InvokeListDevices(ctx, deviceClient.Object);

        result.GetType().Name.Should().Contain("Ok", "an empty device list is a valid response, not an auth failure");
    }

    // ── M-2: legacy-token recovery via identity registry ─────────────────────

    [Fact]
    public async Task ListDevices_LegacyToken_SubMapsToKnownIdentity_RecoversPlatformUserId()
    {
        // M-2: token lacks platform_user_id but sub resolves to a known UserIdentity.
        // The handler MUST use the recovered PlatformUserId, never the raw sub.
        var inboxMock = new Mock<IPlatformInboxClient>();
        inboxMock.Setup(i => i.ResolvePlatformUserIdAsync(UserIdentityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlatformUserId);

        var ctx = BuildContext(withPlatformUserIdClaim: false, withSubClaim: true, inboxClient: inboxMock);
        var deviceClient = EmptyDeviceClient(PlatformUserId);

        var result = await InvokeListDevices(ctx, deviceClient.Object);

        result.GetType().Name.Should().Contain("Ok");
        // The device list call must use the RECOVERED platform user id, not the raw sub.
        deviceClient.Verify(c => c.ListAsync(PlatformUserId, It.IsAny<CancellationToken>()), Times.Once,
            "the recovered PlatformUserId must be the lookup key — the raw sub (UserIdentity.Id) is wrong");
        inboxMock.Verify(i => i.ResolvePlatformUserIdAsync(UserIdentityId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListDevices_LegacyToken_SubUnknownToRegistry_ReturnsUnauthorized()
    {
        // Step 3 (M-3): sub present but not mapped in the identity registry → unresolvable.
        // Must return Unauthorized (not 500).
        var inboxMock = new Mock<IPlatformInboxClient>();
        inboxMock.Setup(i => i.ResolvePlatformUserIdAsync(UserIdentityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var ctx = BuildContext(withPlatformUserIdClaim: false, withSubClaim: true, inboxClient: inboxMock);
        var deviceClient = new Mock<IPlatformUserDeviceClient>();

        var result = await InvokeListDevices(ctx, deviceClient.Object);

        result.GetType().Name.Should().Contain("Unauthorized");
        deviceClient.VerifyNoOtherCalls();
    }

    // ── M-3: unidentifiable principal ────────────────────────────────────────

    [Fact]
    public async Task ListDevices_NoClaimAndNoSub_ReturnsUnauthorized()
    {
        // M-3: no platform_user_id and no sub → completely unidentifiable → not 500.
        // RequestServices is not set up; the code must not reach it.
        var ctx = BuildContext(withPlatformUserIdClaim: false, withSubClaim: false);
        var deviceClient = new Mock<IPlatformUserDeviceClient>();

        var result = await InvokeListDevices(ctx, deviceClient.Object);

        result.GetType().Name.Should().Contain("Unauthorized");
        deviceClient.VerifyNoOtherCalls();
    }

    // ── MN-1/MN-2: tier-boundary enforcement ─────────────────────────────────

    [Fact]
    public async Task ListDevices_LegacyToken_DoesNotUseSubDirectlyAsDeviceKey()
    {
        // Regression for the original defect: a legacy token without platform_user_id used to
        // silently resolve to the org-scoped sub (UserIdentity.Id), which is the wrong key for
        // device lookups keyed by PlatformUser.Id — causing a silent empty/mis-bound list.
        // After the fix, the registry lookup must be made (and if it returns a value, it is used;
        // if the sub happens to equal a platform user id, the registry is still the correct gate).
        var inboxMock = new Mock<IPlatformInboxClient>();
        inboxMock.Setup(i => i.ResolvePlatformUserIdAsync(UserIdentityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlatformUserId);

        var ctx = BuildContext(withPlatformUserIdClaim: false, withSubClaim: true, inboxClient: inboxMock);

        var deviceClient = new Mock<IPlatformUserDeviceClient>();
        // Only set up ListAsync for the CORRECT key (recovered PlatformUserId).
        deviceClient.Setup(c => c.ListAsync(PlatformUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlatformUserDeviceLookupResult>());
        // If the handler incorrectly uses UserIdentityId (the raw sub), this mock would
        // return nothing, but the assertion below would catch it.

        await InvokeListDevices(ctx, deviceClient.Object);

        // The raw sub MUST NOT have been used as the device-lookup key.
        deviceClient.Verify(c => c.ListAsync(UserIdentityId, It.IsAny<CancellationToken>()), Times.Never,
            "the raw sub (UserIdentity.Id) must never be used as the device-lookup key — it is org-scoped");
        deviceClient.Verify(c => c.ListAsync(PlatformUserId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
