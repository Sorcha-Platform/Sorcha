// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Pwa.Services.Enrolment;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Enrolment;

/// <summary>
/// Tests for the PWA-side <see cref="EnrolSessionRedeemer"/>. Verifies wire
/// decoding of the success body and the four documented error mappings
/// from <see cref="RedeemEnrolSessionErrorCode"/> on the Tenant Service.
/// </summary>
public sealed class EnrolSessionRedeemerTests
{
    [Fact]
    public async Task RedeemAsync_Success_Parses_Access_Token_Email_DisplayName()
    {
        var handler = new StubHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/auth/enrol-session/redeem");
            return Ok("""
                {
                  "accessToken": "citizen-jwt",
                  "expiresIn": 3600,
                  "displayName": "Sarah Example",
                  "email": "sarah@example.test"
                }
                """);
        });

        var result = await Create(handler).RedeemAsync("session-jwt");

        result.IsSuccess.Should().BeTrue();
        result.AccessToken.Should().Be("citizen-jwt");
        result.ExpiresInSeconds.Should().Be(3600);
        result.DisplayName.Should().Be("Sarah Example");
        result.Email.Should().Be("sarah@example.test");
    }

    [Fact]
    public async Task RedeemAsync_409_AlreadyUsed_Maps_To_AlreadyUsed()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Conflict, "already_used", "Token has already been used."));

        var result = await Create(handler).RedeemAsync("session-jwt");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(EnrolRedeemErrorCode.AlreadyUsed);
    }

    [Fact]
    public async Task RedeemAsync_410_Expired_Maps_To_Expired()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Gone, "expired", "Expired."));

        var result = await Create(handler).RedeemAsync("session-jwt");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(EnrolRedeemErrorCode.Expired);
    }

    [Fact]
    public async Task RedeemAsync_400_Scope_Mismatch_Maps_To_ScopeMismatch()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.BadRequest, "scope_mismatch", "Wrong scope."));

        var result = await Create(handler).RedeemAsync("session-jwt");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(EnrolRedeemErrorCode.ScopeMismatch);
    }

    [Fact]
    public async Task RedeemAsync_400_Malformed_Maps_To_MalformedToken()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.BadRequest, "malformed_token", "Bad token."));

        var result = await Create(handler).RedeemAsync("session-jwt");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(EnrolRedeemErrorCode.MalformedToken);
    }

    [Fact]
    public async Task RedeemAsync_Network_Failure_Maps_To_Network()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));

        var result = await Create(handler).RedeemAsync("session-jwt");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(EnrolRedeemErrorCode.Network);
    }

    [Fact]
    public async Task RedeemAsync_Empty_Token_Returns_MalformedToken_Without_Calling_Server()
    {
        var called = false;
        var handler = new StubHandler((_, _) => { called = true; return Ok("{}"); });

        var result = await Create(handler).RedeemAsync("");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(EnrolRedeemErrorCode.MalformedToken);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task RedeemAsync_Success_Missing_AccessToken_Maps_To_MalformedToken()
    {
        var handler = new StubHandler((_, _) => Ok("""{"expiresIn":3600,"displayName":"X","email":"x@y.test"}"""));

        var result = await Create(handler).RedeemAsync("session-jwt");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(EnrolRedeemErrorCode.MalformedToken);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Json(HttpStatusCode status, string code, string message) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"code":"{{code}}","message":"{{message}}"}""",
                System.Text.Encoding.UTF8, "application/json")
        };

    private static EnrolSessionRedeemer Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test.local") },
            NullLogger<EnrolSessionRedeemer>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) =>
            _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request, cancellationToken));
    }
}
