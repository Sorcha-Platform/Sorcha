// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Tests for the auth foundation (Feature 114, T109): <see cref="AuthService"/>
/// happy / unhappy paths, and <see cref="BearerTokenHandler"/> stamping the
/// Authorization header from <see cref="IAccessTokenStore"/>.
/// </summary>
public sealed class AuthAndBearerTests
{
    [Fact]
    public async Task AuthService_SuccessfulLogin_PersistsTokenAndEmail()
    {
        var store = new InMemoryAccessTokenStore();
        var handler = new StubHttpHandler((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().EndWith("/api/auth/login");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"jwt-here","expires_in":3600,"requires_two_factor":false}""",
                    Encoding.UTF8, "application/json"),
            };
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var auth = new AuthService(http, store);

        var result = await auth.SignInAsync("citizen@example.com", "secret-pw");

        result.IsSuccess.Should().BeTrue();
        var record = await store.GetAsync();
        record.Should().NotBeNull();
        record!.AccessToken.Should().Be("jwt-here");
        record.Email.Should().Be("citizen@example.com");
        record.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task AuthService_401Response_ReturnsInvalidCredentials()
    {
        var store = new InMemoryAccessTokenStore();
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var auth = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

        var result = await auth.SignInAsync("citizen@example.com", "wrong");

        result.Status.Should().Be(SignInStatus.InvalidCredentials);
        (await store.GetAsync()).Should().BeNull("failed sign-in must not persist a token");
    }

    [Fact]
    public async Task AuthService_TwoFactorRequired_ReturnsTwoFactorStatus()
    {
        var store = new InMemoryAccessTokenStore();
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                // Real server always pairs requires_two_factor with a login_token.
                """{"requires_two_factor":true,"login_token":"lt-abc","available_methods":["totp"]}""",
                Encoding.UTF8, "application/json"),
        });
        var auth = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

        var result = await auth.SignInAsync("citizen@example.com", "pw");

        result.Status.Should().Be(SignInStatus.TwoFactorRequired);
        result.LoginToken.Should().Be("lt-abc");
        (await store.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AuthService_TwoFactorRequired_CarriesLoginToken()
    {
        var store = new InMemoryAccessTokenStore();
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"requires_two_factor":true,"login_token":"lt-123","available_methods":["totp"]}""",
                Encoding.UTF8, "application/json"),
        });
        var auth = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

        var result = await auth.SignInAsync("citizen@example.com", "pw");

        result.Status.Should().Be(SignInStatus.TwoFactorRequired);
        result.LoginToken.Should().Be("lt-123");
        (await store.GetAsync()).Should().BeNull("2FA-required must not persist a token yet");
    }

    [Fact]
    public async Task VerifyTwoFactor_ValidCode_PersistsToken()
    {
        var store = new InMemoryAccessTokenStore();
        HttpRequestMessage? captured = null;
        var handler = new StubHttpHandler((req, _) =>
        {
            captured = req;
            req.RequestUri!.AbsolutePath.Should().EndWith("/api/auth/verify-2fa");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"jwt-2fa","expires_in":3600,"requires_two_factor":false}""",
                    Encoding.UTF8, "application/json"),
            };
        });
        var auth = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

        var result = await auth.VerifyTwoFactorAsync("lt-123", "citizen@example.com", "123456");

        result.IsSuccess.Should().BeTrue();
        var record = await store.GetAsync();
        record!.AccessToken.Should().Be("jwt-2fa");
        record.Email.Should().Be("citizen@example.com");
        captured.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyTwoFactor_WrongCode_ReturnsTwoFactorRequiredWithRetry()
    {
        var store = new InMemoryAccessTokenStore();
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var auth = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

        var result = await auth.VerifyTwoFactorAsync("lt-123", "citizen@example.com", "000000");

        result.Status.Should().Be(SignInStatus.TwoFactorRequired);
        result.LoginToken.Should().Be("lt-123", "the login token survives a wrong-code retry");
        (await store.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task AuthService_SignOut_ClearsToken()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("jwt", DateTimeOffset.UtcNow.AddHours(1), "x@example.com"));
        var auth = new AuthService(new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage())),
            store);

        await auth.SignOutAsync();

        (await auth.IsSignedInAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task BearerTokenHandler_AddsAuthorizationHeader_WhenTokenPresent()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("the-token", DateTimeOffset.UtcNow.AddHours(1), "x@example.com"));

        string? observedAuth = null;
        var inner = new StubHttpHandler((req, _) =>
        {
            observedAuth = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var bearer = new BearerTokenHandler(store) { InnerHandler = inner };
        var http = new HttpClient(bearer) { BaseAddress = new Uri("https://localhost/") };

        await http.GetAsync("api/v1/wallet/sync");

        observedAuth.Should().Be("Bearer the-token");
    }

    [Fact]
    public async Task BearerTokenHandler_OmitsHeader_WhenSignedOut()
    {
        var store = new InMemoryAccessTokenStore();
        string? observedAuth = "sentinel";
        var inner = new StubHttpHandler((req, _) =>
        {
            observedAuth = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var bearer = new BearerTokenHandler(store) { InnerHandler = inner };
        var http = new HttpClient(bearer) { BaseAddress = new Uri("https://localhost/") };

        await http.GetAsync("api/v1/wallet/sync");

        observedAuth.Should().BeNull("requests must go out unauthenticated when no token is stored");
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public StubHttpHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_respond(request, ct));
    }
}
