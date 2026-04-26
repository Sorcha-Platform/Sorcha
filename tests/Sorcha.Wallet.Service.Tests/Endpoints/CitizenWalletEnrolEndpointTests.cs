// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.Wallet.Service.Endpoints;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Tests for the <see cref="CitizenWalletEndpoints"/> <c>EnrolDevice</c> handler
/// (Feature 114, T084-T085). Uses the existing reflection-based static-handler
/// invocation pattern from <see cref="Sorcha.Wallet.Service.Tests.Presentations.PresentationEndpointTests"/>;
/// all collaborators are mocked so the test exercises the orchestration logic
/// (claims resolution → org wallet resolve → delegation issue → tenant register
/// → response shape) without booting the full host.
/// </summary>
public sealed class CitizenWalletEnrolEndpointTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string CitizenWallet = "ws1qcitizen1";
    private const string OrgWallet = "ws1qorgsigner1";

    private readonly Mock<IValidator<DeviceEnrolmentRequest>> _validator = new();
    private readonly Mock<IHolderKeyService> _holderKeys = new();
    private readonly Mock<IDeviceDelegationIssuer> _issuer = new();
    private readonly Mock<IOrgStatusSigningWalletResolver> _resolver = new();
    private readonly Mock<IPlatformUserDeviceClient> _deviceClient = new();

    public CitizenWalletEnrolEndpointTests()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<DeviceEnrolmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());

        _resolver.Setup(r => r.ResolveAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrgWallet);
    }

    private static DeviceEnrolmentRequest BuildRequest() => new()
    {
        DeviceLabel = "Stuart's iPhone 16",
        DevicePublicJwk = new EcP256PublicJwk
        {
            X = "MKBCTNIcKUSDii11ySs3526iDZ8AiTo7Tu6KPAqv7D4",
            Y = "4Etl6SRW2YiLUrN5vfvVHuhp7x8PxltmWWlbbM4IFyM"
        },
        Platform = "iOS 19 / Safari 19",
        UserAgent = "Mozilla/5.0 ..."
    };

    private static DeviceDelegationResult BuildDelegation() => new(
        CompactJwt: "header.payload.sig",
        Jti: "jti-abc",
        ExpiresAt: DateTimeOffset.UtcNow.AddDays(365),
        StatusListUri: $"https://verify.test/api/v1/wallet/status/{OrgId}/citizen-devices/0.statuslist+jwt",
        StatusListIndex: 7,
        StatusListId: 0,
        HolderPublicJwk: JsonSerializer.SerializeToElement(new
        {
            kty = "EC",
            crv = "P-256",
            x = "holderX",
            y = "holderY"
        }));

    private static HttpContext BuildHttpContext(
        Guid? platformUserId = null,
        string? walletAddress = CitizenWallet,
        Guid? orgId = null)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        if (walletAddress is not null)
            claims.Add(new Claim("wallet_address", walletAddress));
        if (orgId is not null)
            claims.Add(new Claim("org_id", orgId.Value.ToString()));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private async Task<IResult> InvokeAsync(DeviceEnrolmentRequest body, HttpContext context)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "EnrolDevice",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("EnrolDevice handler should exist");

        var result = method.Invoke(null, [
            body,
            context,
            _validator.Object,
            _holderKeys.Object,
            _issuer.Object,
            _resolver.Object,
            _deviceClient.Object,
            NullLogger<Program>.Instance,
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }

    [Fact]
    public async Task EnrolDevice_HappyPath_ReturnsOkWithEnrolmentResponse()
    {
        var delegation = BuildDelegation();
        _issuer.Setup(i => i.IssueAsync(
                PlatformUserId, CitizenWallet, OrgId, OrgWallet,
                It.IsAny<EcP256PublicJwk>(), "Stuart's iPhone 16", "iOS 19 / Safari 19",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(delegation);

        var deviceId = Guid.NewGuid();
        _deviceClient.Setup(c => c.RegisterAsync(
                PlatformUserId, "Stuart's iPhone 16",
                It.IsAny<string>(), It.IsAny<string>(),
                "iOS 19 / Safari 19", "Mozilla/5.0 ...",
                delegation.ExpiresAt, "jti-abc", 7,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformUserDeviceRegistrationResult(deviceId, DateTimeOffset.UtcNow));

        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet, OrgId);
        var result = await InvokeAsync(BuildRequest(), ctx);

        result.GetType().Name.Should().Contain("Ok");

        // Resolver must use the org id from the JWT, not anything else.
        _resolver.Verify(r => r.ResolveAsync(OrgId, It.IsAny<CancellationToken>()), Times.Once);

        // Issuer must receive the org's signing wallet, not the citizen's.
        _issuer.Verify(i => i.IssueAsync(
            PlatformUserId, CitizenWallet, OrgId, OrgWallet,
            It.IsAny<EcP256PublicJwk>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnrolDevice_MissingPlatformUserClaim_ReturnsUnauthorized()
    {
        var ctx = BuildHttpContext(platformUserId: null, walletAddress: CitizenWallet, orgId: OrgId);
        var result = await InvokeAsync(BuildRequest(), ctx);

        result.GetType().Name.Should().Contain("Unauthorized");
        _issuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnrolDevice_MissingWalletAddressClaim_ReturnsUnauthorized()
    {
        var ctx = BuildHttpContext(platformUserId: PlatformUserId, walletAddress: null, orgId: OrgId);
        var result = await InvokeAsync(BuildRequest(), ctx);

        result.GetType().Name.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task EnrolDevice_MissingOrgIdClaim_ReturnsUnauthorized()
    {
        var ctx = BuildHttpContext(platformUserId: PlatformUserId, walletAddress: CitizenWallet, orgId: null);
        var result = await InvokeAsync(BuildRequest(), ctx);

        result.GetType().Name.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task EnrolDevice_ValidationFailure_ReturnsValidationProblem()
    {
        _validator.Setup(v => v.ValidateAsync(It.IsAny<DeviceEnrolmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("DeviceLabel", "is required")]));

        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet, OrgId);
        var result = await InvokeAsync(BuildRequest(), ctx);

        // Results.ValidationProblem returns a HttpValidationProblemDetails-bearing result.
        result.GetType().Name.Should().ContainAny("Problem", "ValidationProblem");
    }

    [Fact]
    public async Task EnrolDevice_TenantClientFails_Returns502()
    {
        _issuer.Setup(i => i.IssueAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<EcP256PublicJwk>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDelegation());

        _deviceClient.Setup(c => c.RegisterAsync(
                It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant down"));

        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet, OrgId);
        var result = await InvokeAsync(BuildRequest(), ctx);

        result.GetType().Name.Should().Contain("ProblemHttpResult");
    }
}

/// <summary>
/// xUnit assertion helper — true if the actual string contains any of the candidates.
/// </summary>
internal static class StringAssertionExtensions
{
    public static FluentAssertions.AndConstraint<FluentAssertions.Primitives.StringAssertions> ContainAny(
        this FluentAssertions.Primitives.StringAssertions assertions, params string[] candidates)
    {
        var subject = assertions.Subject;
        candidates.Should().Contain(c => subject.Contains(c, StringComparison.OrdinalIgnoreCase),
            because: $"expected '{subject}' to contain one of: {string.Join(", ", candidates)}");
        return new FluentAssertions.AndConstraint<FluentAssertions.Primitives.StringAssertions>(assertions);
    }
}
