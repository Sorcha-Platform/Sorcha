// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Authentication;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.AccountLink;

/// <summary>
/// Unit tests for <see cref="AnonymousSocialLinkClientService"/>.
/// All HTTP calls are intercepted via a mocked <see cref="HttpMessageHandler"/>
/// so these tests run without Feature 168 being present.
/// </summary>
public class AnonymousSocialLinkClientServiceTests
{
    private const string LinkPendingToken = "lp_test_opaque_token";
    private const string ChallengeToken = "ch_test_challenge_token";
    private const string AccessToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1c2VyMSIsImV4cCI6OTk5OTk5OTk5OX0.sig";
    private const string RefreshToken = "rt_test_refresh_token";

    private readonly Mock<HttpMessageHandler> _handler = new(MockBehavior.Strict);

    private AnonymousSocialLinkClientService CreateService()
    {
        var client = new HttpClient(_handler.Object) { BaseAddress = new Uri("http://localhost") };
        var logger = new Mock<ILogger<AnonymousSocialLinkClientService>>().Object;
        return new AnonymousSocialLinkClientService(client, logger);
    }

    private void SetupResponse(HttpMethod method, string url, HttpStatusCode status, object? body = null)
    {
        var content = body is null
            ? null
            : JsonContent.Create(body);

        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == method && r.RequestUri!.PathAndQuery == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = content ?? new StringContent(string.Empty)
            });
    }

    private void SetupResponseWithString(HttpMethod method, string url, HttpStatusCode status, string json)
    {
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == method && r.RequestUri!.PathAndQuery == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    // ===== InitiateAsync =====

    [Fact]
    public async Task InitiateAsync_PasskeyResult_MapsOkCorrectly()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("{\"challenge\":\"abc123\"}");
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/initiate",
            HttpStatusCode.OK,
            "{\"method\":\"Passkey\",\"payload\":{\"challenge\":\"abc123\"}}");

        var svc = CreateService();
        var result = await svc.InitiateAsync(LinkPendingToken, ChallengeMethod.Passkey);

        result.Outcome.Should().Be(InitiateOutcome.Ok);
        result.Method.Should().Be(ChallengeMethod.Passkey);
        result.Payload.Should().NotBeNull();
    }

    [Fact]
    public async Task InitiateAsync_TotpResult_MapsOkCorrectly()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/initiate",
            HttpStatusCode.OK,
            "{\"method\":\"Totp\",\"payload\":null}");

        var svc = CreateService();
        var result = await svc.InitiateAsync(LinkPendingToken);

        result.Outcome.Should().Be(InitiateOutcome.Ok);
        result.Method.Should().Be(ChallengeMethod.Totp);
        result.Payload.Should().BeNull();
    }

    [Fact]
    public async Task InitiateAsync_401_MapsToExpired()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/initiate",
            HttpStatusCode.Unauthorized,
            "{}");

        var svc = CreateService();
        var result = await svc.InitiateAsync(LinkPendingToken);

        result.Outcome.Should().Be(InitiateOutcome.Expired);
    }

    [Fact]
    public async Task InitiateAsync_400_MapsToUnsupportedV1Method()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/initiate",
            HttpStatusCode.BadRequest,
            "{}");

        var svc = CreateService();
        var result = await svc.InitiateAsync(LinkPendingToken);

        result.Outcome.Should().Be(InitiateOutcome.UnsupportedV1Method);
    }

    [Fact]
    public async Task InitiateAsync_429_MapsToRateLimited()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/initiate",
            HttpStatusCode.TooManyRequests,
            "{}");

        var svc = CreateService();
        var result = await svc.InitiateAsync(LinkPendingToken);

        result.Outcome.Should().Be(InitiateOutcome.RateLimited);
    }

    // ===== VerifyAsync =====

    [Fact]
    public async Task VerifyAsync_PasskeyAssertion_200_ReturnsChallengeToken()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/verify",
            HttpStatusCode.OK,
            $"{{\"token\":\"{ChallengeToken}\",\"expiresIn\":300}}");

        var assertion = JsonSerializer.Deserialize<JsonElement>("{\"id\":\"cred1\"}");
        var svc = CreateService();
        var result = await svc.VerifyAsync(LinkPendingToken, ChallengeMethod.Passkey, assertion);

        result.Succeeded.Should().BeTrue();
        result.ChallengeToken.Should().Be(ChallengeToken);
        result.Error.Should().Be(ChallengeVerifyError.None);
    }

    [Fact]
    public async Task VerifyAsync_TotpCode_200_ReturnsChallengeToken()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/verify",
            HttpStatusCode.OK,
            $"{{\"token\":\"{ChallengeToken}\",\"expiresIn\":300}}");

        var proof = JsonSerializer.Deserialize<JsonElement>("{\"code\":\"123456\"}");
        var svc = CreateService();
        var result = await svc.VerifyAsync(LinkPendingToken, ChallengeMethod.Totp, proof);

        result.Succeeded.Should().BeTrue();
        result.ChallengeToken.Should().Be(ChallengeToken);
    }

    [Fact]
    public async Task VerifyAsync_401_MapsToProofRejected()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/verify",
            HttpStatusCode.Unauthorized,
            "{\"code\":\"proof_rejected\"}");

        var proof = JsonSerializer.Deserialize<JsonElement>("{\"code\":\"999999\"}");
        var svc = CreateService();
        var result = await svc.VerifyAsync(LinkPendingToken, ChallengeMethod.Totp, proof);

        result.Succeeded.Should().BeFalse();
        result.ChallengeToken.Should().BeNull();
        result.Error.Should().Be(ChallengeVerifyError.ProofRejected);
    }

    [Fact]
    public async Task VerifyAsync_401_Expired_MapsToExpired()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/verify",
            HttpStatusCode.Unauthorized,
            "{\"code\":\"expired\"}");

        var proof = JsonSerializer.Deserialize<JsonElement>("{\"code\":\"123456\"}");
        var svc = CreateService();
        var result = await svc.VerifyAsync(LinkPendingToken, ChallengeMethod.Totp, proof);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(ChallengeVerifyError.Expired);
    }

    [Fact]
    public async Task VerifyAsync_403_ProofTierInsufficient_MapsTierInsufficient()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/verify",
            HttpStatusCode.Forbidden,
            "{\"code\":\"proof_tier_insufficient\"}");

        var proof = JsonSerializer.Deserialize<JsonElement>("{\"code\":\"123456\"}");
        var svc = CreateService();
        var result = await svc.VerifyAsync(LinkPendingToken, ChallengeMethod.Totp, proof);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(ChallengeVerifyError.ProofTierInsufficient);
    }

    [Fact]
    public async Task VerifyAsync_429_MapsToFailed()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/challenge/verify",
            HttpStatusCode.TooManyRequests,
            "{}");

        var proof = JsonSerializer.Deserialize<JsonElement>("{\"code\":\"123456\"}");
        var svc = CreateService();
        var result = await svc.VerifyAsync(LinkPendingToken, ChallengeMethod.Totp, proof);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(ChallengeVerifyError.Failed);
    }

    // ===== ConfirmAsync =====

    [Fact]
    public async Task ConfirmAsync_SendsXAuthChallengeHeader()
    {
        string? capturedHeader = null;
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                capturedHeader = req.Headers.TryGetValues("X-Auth-Challenge", out var vals)
                    ? vals.FirstOrDefault()
                    : null;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"accessToken\":\"{AccessToken}\",\"refreshToken\":\"{RefreshToken}\",\"expiresIn\":3600}}",
                        Encoding.UTF8, "application/json")
                };
            });

        var svc = CreateService();
        var result = await svc.ConfirmAsync(LinkPendingToken, ChallengeToken);

        capturedHeader.Should().Be(ChallengeToken);
        result.Outcome.Should().Be(ConfirmOutcome.Linked);
        result.AccessToken.Should().Be(AccessToken);
        result.RefreshToken.Should().Be(RefreshToken);
    }

    [Fact]
    public async Task ConfirmAsync_409_MapsToConflict()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/confirm",
            HttpStatusCode.Conflict,
            "{}");

        var svc = CreateService();
        var result = await svc.ConfirmAsync(LinkPendingToken, ChallengeToken);

        result.Outcome.Should().Be(ConfirmOutcome.Conflict);
        result.AccessToken.Should().BeNull();
        result.RefreshToken.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmAsync_401_MapsToExpired()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/confirm",
            HttpStatusCode.Unauthorized,
            "{}");

        var svc = CreateService();
        var result = await svc.ConfirmAsync(LinkPendingToken, ChallengeToken);

        result.Outcome.Should().Be(ConfirmOutcome.Expired);
        result.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmAsync_403_MapsToProofInvalid()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/confirm",
            HttpStatusCode.Forbidden,
            "{}");

        var svc = CreateService();
        var result = await svc.ConfirmAsync(LinkPendingToken, ChallengeToken);

        result.Outcome.Should().Be(ConfirmOutcome.ProofInvalid);
        result.AccessToken.Should().BeNull();
    }

    [Fact]
    public async Task ConfirmAsync_429_MapsToRateLimited()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/confirm",
            HttpStatusCode.TooManyRequests,
            "{}");

        var svc = CreateService();
        var result = await svc.ConfirmAsync(LinkPendingToken, ChallengeToken);

        result.Outcome.Should().Be(ConfirmOutcome.RateLimited);
    }

    [Fact]
    public async Task ConfirmAsync_AlreadyRedeemed_401_MapsToExpired()
    {
        SetupResponseWithString(
            HttpMethod.Post,
            "/api/auth/social/link/confirm",
            HttpStatusCode.Unauthorized,
            "{\"code\":\"already_redeemed\"}");

        var svc = CreateService();
        var result = await svc.ConfirmAsync(LinkPendingToken, ChallengeToken);

        result.Outcome.Should().Be(ConfirmOutcome.Expired);
        result.AccessToken.Should().BeNull();
    }
}
