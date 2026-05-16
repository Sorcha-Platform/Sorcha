// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Handler-level tests for <see cref="EnrolSessionEndpoints"/>. Exercises the
/// platform_user_id claim resolution + DTO error-code-to-HTTP-status mapping
/// without spinning up a full WebApplicationFactory. The actual mint/redeem
/// semantics are covered by <c>EnrolSessionServiceTests</c>.
/// </summary>
public sealed class EnrolSessionEndpointsTests
{
    [Fact]
    public async Task MintAsync_Anonymous_Caller_Returns_Unauthorized()
    {
        var service = new Mock<IEnrolSessionService>(MockBehavior.Strict);

        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no platform_user_id
        var result = await EnrolSessionEndpoints.MintAsync(principal, service.Object, null, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MintAsync_With_PlatformUserId_Claim_Returns_Ok()
    {
        var pu = Guid.NewGuid();
        var minted = new MintEnrolSessionResponse(
            "jwt-string",
            $"https://strathcarron.gov/wallet/enrol?session=jwt-string",
            DateTimeOffset.UtcNow.AddMinutes(10),
            EnrolSessionMode.Gated);

        var service = new Mock<IEnrolSessionService>();
        service.Setup(s => s.MintAsync(pu, EnrolSessionMode.Gated, It.IsAny<CancellationToken>())).ReturnsAsync(minted);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("platform_user_id", pu.ToString())
        }));

        var result = await EnrolSessionEndpoints.MintAsync(principal, service.Object, null, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<MintEnrolSessionResponse>>();
        var ok = (Ok<MintEnrolSessionResponse>)result.Result;
        ok.Value!.SessionToken.Should().Be("jwt-string");
    }

    [Fact]
    public async Task MintAsync_With_Malformed_PlatformUserId_Returns_Unauthorized()
    {
        var service = new Mock<IEnrolSessionService>(MockBehavior.Strict);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("platform_user_id", "not-a-guid")
        }));

        var result = await EnrolSessionEndpoints.MintAsync(principal, service.Object, null, CancellationToken.None);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RedeemAsync_Happy_Path_Returns_Ok()
    {
        var service = new Mock<IEnrolSessionService>();
        var redeemed = new RedeemEnrolSessionResponse("access-jwt", 3600, "Sarah", "sarah@example.test", EnrolSessionMode.Gated);
        service.Setup(s => s.RedeemAsync("the-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedeemResult.Ok(redeemed));

        var result = await EnrolSessionEndpoints.RedeemAsync(
            new RedeemEnrolSessionRequest("the-token"), service.Object, CancellationToken.None);

        var ok = result.Should().BeOfType<Ok<RedeemEnrolSessionResponse>>().Subject;
        ok.Value!.AccessToken.Should().Be("access-jwt");
        ok.Value.DisplayName.Should().Be("Sarah");
    }

    [Fact]
    public async Task RedeemAsync_Replay_Maps_To_409()
    {
        var service = new Mock<IEnrolSessionService>();
        service.Setup(s => s.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedeemResult.Fail(RedeemEnrolSessionErrorCode.AlreadyUsed, "already used"));

        var result = await EnrolSessionEndpoints.RedeemAsync(
            new RedeemEnrolSessionRequest("the-token"), service.Object, CancellationToken.None);

        AssertJsonStatus(result, StatusCodes.Status409Conflict, RedeemEnrolSessionErrorCode.AlreadyUsed);
    }

    [Fact]
    public async Task RedeemAsync_Expired_Maps_To_410()
    {
        var service = new Mock<IEnrolSessionService>();
        service.Setup(s => s.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedeemResult.Fail(RedeemEnrolSessionErrorCode.Expired, "expired"));

        var result = await EnrolSessionEndpoints.RedeemAsync(
            new RedeemEnrolSessionRequest("the-token"), service.Object, CancellationToken.None);

        AssertJsonStatus(result, StatusCodes.Status410Gone, RedeemEnrolSessionErrorCode.Expired);
    }

    [Fact]
    public async Task RedeemAsync_Scope_Mismatch_Maps_To_400()
    {
        var service = new Mock<IEnrolSessionService>();
        service.Setup(s => s.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RedeemResult.Fail(RedeemEnrolSessionErrorCode.ScopeMismatch, "scope mismatch"));

        var result = await EnrolSessionEndpoints.RedeemAsync(
            new RedeemEnrolSessionRequest("the-token"), service.Object, CancellationToken.None);

        AssertJsonStatus(result, StatusCodes.Status400BadRequest, RedeemEnrolSessionErrorCode.ScopeMismatch);
    }

    [Fact]
    public async Task RedeemAsync_Empty_Token_Returns_400_Without_Calling_Service()
    {
        var service = new Mock<IEnrolSessionService>(MockBehavior.Strict);

        var result = await EnrolSessionEndpoints.RedeemAsync(
            new RedeemEnrolSessionRequest(""), service.Object, CancellationToken.None);

        result.Should().BeOfType<BadRequest<RedeemEnrolSessionErrorBody>>();
        service.VerifyNoOtherCalls();
    }

    private static void AssertJsonStatus(IResult result, int expectedStatus, RedeemEnrolSessionErrorCode expectedCode)
    {
        // Results.Json returns a JsonHttpResult<T> with the StatusCode property set.
        var status = result.GetType().GetProperty("StatusCode")?.GetValue(result) as int?;
        status.Should().Be(expectedStatus);

        var value = result.GetType().GetProperty("Value")?.GetValue(result);
        value.Should().BeOfType<RedeemEnrolSessionErrorBody>();
        ((RedeemEnrolSessionErrorBody)value!).Code.Should().Be(expectedCode);
    }
}
