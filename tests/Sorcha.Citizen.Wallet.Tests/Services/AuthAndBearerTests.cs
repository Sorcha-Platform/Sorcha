// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Citizen.Wallet.Services;
using Xunit;

namespace Sorcha.Citizen.Wallet.Tests.Services;

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
                """{"access_token":null,"expires_in":0,"requires_two_factor":true}""",
                Encoding.UTF8, "application/json"),
        });
        var auth = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

        var result = await auth.SignInAsync("citizen@example.com", "pw");

        result.Status.Should().Be(SignInStatus.TwoFactorRequired);
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
