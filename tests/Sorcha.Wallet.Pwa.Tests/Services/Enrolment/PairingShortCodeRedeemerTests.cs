// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Pwa.Services.Enrolment;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Enrolment;

/// <summary>
/// Feature 128 US1 — covers PWA-side <see cref="PairingShortCodeRedeemer"/>
/// HTTP-shape handling: status-code-to-error-code mapping, success payload
/// parsing, malformed-input rejection.
/// </summary>
public sealed class PairingShortCodeRedeemerTests
{
    [Fact]
    public async Task RedeemAsync_Happy_Path_Returns_Token_And_Identity()
    {
        var handler = new StubHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/auth/enrol-session/redeem-short-code");
            return Ok("""
                {
                  "accessToken": "citizen-jwt",
                  "expiresIn": 3600,
                  "displayName": "Sarah Example",
                  "email": "sarah@example.test"
                }
                """);
        });

        var result = await Create(handler).RedeemAsync("123456");

        result.IsSuccess.Should().BeTrue();
        result.AccessToken.Should().Be("citizen-jwt");
        result.ExpiresInSeconds.Should().Be(3600);
        result.DisplayName.Should().Be("Sarah Example");
        result.Email.Should().Be("sarah@example.test");
    }

    [Fact]
    public async Task RedeemAsync_409_AlreadyUsed_Maps_To_AlreadyUsedCode()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Conflict, "already_used_code", "already used"));

        var result = await Create(handler).RedeemAsync("123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PairingShortCodeRedeemErrorCode.AlreadyUsedCode);
    }

    [Fact]
    public async Task RedeemAsync_410_Expired_Maps_To_ExpiredCode()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.Gone, "expired_code", "expired"));

        var result = await Create(handler).RedeemAsync("123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PairingShortCodeRedeemErrorCode.ExpiredCode);
    }

    [Fact]
    public async Task RedeemAsync_429_RateLimit_Maps_To_RateLimited()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.TooManyRequests, "rate_limited", "throttled"));

        var result = await Create(handler).RedeemAsync("123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PairingShortCodeRedeemErrorCode.RateLimited);
    }

    [Fact]
    public async Task RedeemAsync_400_Maps_To_MalformedCode()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.BadRequest, "malformed_code", "bad shape"));

        var result = await Create(handler).RedeemAsync("123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PairingShortCodeRedeemErrorCode.MalformedCode);
    }

    [Fact]
    public async Task RedeemAsync_Network_Error_Maps_To_Network()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("offline"));

        var result = await Create(handler).RedeemAsync("123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PairingShortCodeRedeemErrorCode.Network);
    }

    [Fact]
    public async Task RedeemAsync_Empty_Code_Rejects_Without_Calling_Server()
    {
        var called = 0;
        var handler = new StubHandler((_, _) =>
        {
            called++;
            return Ok("{}");
        });

        var result = await Create(handler).RedeemAsync("");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PairingShortCodeRedeemErrorCode.MalformedCode);
        called.Should().Be(0);
    }

    [Fact]
    public async Task RedeemAsync_Success_Payload_Missing_AccessToken_Returns_MalformedCode()
    {
        var handler = new StubHandler((_, _) => Ok("""
            { "expiresIn": 3600, "displayName": "S", "email": "s@e.test" }
            """));

        var result = await Create(handler).RedeemAsync("123456");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(PairingShortCodeRedeemErrorCode.MalformedCode);
    }

    private static PairingShortCodeRedeemer Create(StubHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };
        return new PairingShortCodeRedeemer(http, NullLogger<PairingShortCodeRedeemer>.Instance);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Json(HttpStatusCode status, string code, string message)
    {
        var body = $"{{\"code\":\"{code}\",\"message\":\"{message}\"}}";
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request, ct));
    }
}
