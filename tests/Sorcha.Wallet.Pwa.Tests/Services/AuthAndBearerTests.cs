// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
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
        var auth = NewAuth(http, store);

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
        var auth = NewAuth(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

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
        var auth = NewAuth(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

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
        var auth = NewAuth(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

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
        var auth = NewAuth(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

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
        var auth = NewAuth(new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") }, store);

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
        var auth = NewAuth(new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage())), store);

        await auth.SignOutAsync();

        (await auth.IsSignedInAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AuthService_SignOut_PurgesAllLocalData()
    {
        // Regression: sign-out used to clear ONLY the access token, leaving
        // credentials, personas, delegation, history and the welcome/tour
        // flags in IndexedDB for the next citizen who signs in on the same
        // device. Sign-out MUST wipe every per-device store.
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("jwt", DateTimeOffset.UtcNow.AddHours(1), "x@example.com"));
        var purge = new SpyLocalDataPurge();
        var auth = NewAuth(
            new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage())), store, purge);

        await auth.SignOutAsync();

        purge.PurgeCount.Should().Be(1, "sign-out must wipe all per-device wallet data, not just the token");
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
        var noRefreshHttp = new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage()))
            { BaseAddress = new Uri("https://localhost/") };
        var bearer = new BearerTokenHandler(store, noRefreshHttp) { InnerHandler = inner };
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
        var noRefreshHttp = new HttpClient(new StubHttpHandler((_, _) => new HttpResponseMessage()))
            { BaseAddress = new Uri("https://localhost/") };
        var bearer = new BearerTokenHandler(store, noRefreshHttp) { InnerHandler = inner };
        var http = new HttpClient(bearer) { BaseAddress = new Uri("https://localhost/") };

        await http.GetAsync("api/v1/wallet/sync");

        observedAuth.Should().BeNull("requests must go out unauthenticated when no token is stored");
    }

    [Fact]
    public async Task InMemoryAccessTokenStore_RoundTripsRefreshToken()
    {
        var store = new InMemoryAccessTokenStore();
        var record = new AccessTokenRecord("at", DateTimeOffset.UtcNow.AddHours(1), "a@b.test", "rt");
        await store.SetAsync(record);

        var loaded = await store.GetAsync();
        loaded!.RefreshToken.Should().Be("rt");
    }

    [Fact]
    public async Task SignInWithPasskeyAsync_HappyPath_PersistsConsumerToken()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            JsonOk("{\"transaction_id\":\"tx1\",\"options\":{\"challenge\":\"AA\"}}"),
            JsonOk("{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}")
        });
        var handler = new CapturingHandler(_ => Task.FromResult(responses.Dequeue()));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };
        var store = new InMemoryAccessTokenStore();
        var auth = new AuthService(http, store, new NoopLocalDataPurge(), new FakePasskeyInterop());

        var result = await auth.SignInWithPasskeyAsync();

        result.IsSuccess.Should().BeTrue();
        (await store.GetAsync())!.RefreshToken.Should().Be("rt");
    }

    [Fact]
    public async Task SignInWithPasskeyAsync_UnrecognisedPasskey_ReturnsInvalidCredentials()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            JsonOk("{\"transaction_id\":\"tx1\",\"options\":{\"challenge\":\"AA\"}}"),
            new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
        });
        var handler = new CapturingHandler(_ => Task.FromResult(responses.Dequeue()));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };
        var auth = new AuthService(http, new InMemoryAccessTokenStore(), new NoopLocalDataPurge(), new FakePasskeyInterop());

        var result = await auth.SignInWithPasskeyAsync();

        result.Status.Should().Be(SignInStatus.InvalidCredentials);
    }

    [Fact]
    public async Task SignInWithPasskeyAsync_Unsupported_ReturnsServerError()
    {
        var http = new HttpClient(new CapturingHandler(_ => Task.FromResult(JsonOk("{}"))))
            { BaseAddress = new Uri("https://gw.test/") };
        var auth = new AuthService(http, new InMemoryAccessTokenStore(), new NoopLocalDataPurge(),
            new FakePasskeyInterop { Supported = false });

        var result = await auth.SignInWithPasskeyAsync();

        result.Status.Should().Be(SignInStatus.ServerError);
    }

    // Constructs an AuthService with a throwaway purge for the tests that
    // don't assert on the sign-out cascade; AuthService_SignOut_PurgesAllLocalData
    // passes its own spy to inspect the call.
    private static AuthService NewAuth(HttpClient http, IAccessTokenStore store, ILocalDataPurge? purge = null)
        => new(http, store, purge ?? new SpyLocalDataPurge(), new FakePasskeyInterop());

    private sealed class SpyLocalDataPurge : ILocalDataPurge
    {
        public int PurgeCount { get; private set; }
        public Task PurgeAsync(CancellationToken ct = default)
        {
            PurgeCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public StubHttpHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
            => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_respond(request, ct));
    }

    [Fact]
    public async Task SignInAsync_SendsConsumerTierHint()
    {
        string? capturedBody = null;
        var handler = new CapturingHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return JsonOk("{\"access_token\":\"at\",\"expires_in\":3600,\"requires_two_factor\":false}");
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };
        var auth = new AuthService(http, new InMemoryAccessTokenStore(), new NoopLocalDataPurge(), new FakePasskeyInterop());

        await auth.SignInAsync("a@b.test", "pw");

        capturedBody.Should().Contain("\"tier\":\"consumer\"");
    }

    [Fact]
    public async Task BeginSocialSignInAsync_ReturnsAuthorizationUrl_AndSendsWalletSurface()
    {
        string? capturedBody = null;
        var handler = new CapturingHandler(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return JsonOk("{\"authorizationUrl\":\"https://idp/auth?x=1\",\"state\":\"st\"}");
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };
        var auth = new AuthService(http, new InMemoryAccessTokenStore(), new NoopLocalDataPurge(), new FakePasskeyInterop());

        var url = await auth.BeginSocialSignInAsync("Google");

        url.Should().Be("https://idp/auth?x=1");
        capturedBody.Should().Contain("\"surface\":\"wallet\"");
        capturedBody.Should().Contain("\"intent\":\"login\"");
        capturedBody.Should().Contain("\"provider\":\"Google\"");
    }

    [Fact]
    public async Task TryConsumeSocialReturnAsync_NoFragment_ReturnsFalse_And_StaysSignedOut()
    {
        var js = new NullConsumeJsRuntime();
        var store = new InMemoryAccessTokenStore();
        var auth = new AuthService(
            new HttpClient(new CapturingHandler(_ => Task.FromResult(JsonOk("{}")))) { BaseAddress = new Uri("https://gw.test/") },
            store, new NoopLocalDataPurge(), new FakePasskeyInterop());

        var consumed = await auth.TryConsumeSocialReturnAsync(js);

        consumed.Should().BeFalse();
        (await store.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task BearerTokenHandler_On401_RefreshesAndRetries()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("old", DateTimeOffset.UtcNow.AddHours(1), "a@b.test", "rt"));

        // Inner handler: request with stale "old" token → 401; with "new" token → 200.
        var inner = new SequencedHandler(req =>
        {
            var auth = req.Headers.Authorization?.Parameter;
            return auth == "new"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                : new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
        });

        // Refresh endpoint returns a fresh token "new".
        var refreshHttp = new HttpClient(new CapturingHandler(_ =>
            Task.FromResult(JsonOk("{\"access_token\":\"new\",\"refresh_token\":\"rt2\",\"expires_in\":3600}"))))
            { BaseAddress = new Uri("https://gw.test/") };

        var handler = new BearerTokenHandler(store, refreshHttp) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };

        var resp = await client.GetAsync("api/v1/wallet/credentials");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await store.GetAsync())!.AccessToken.Should().Be("new");
    }

    [Fact]
    public async Task BearerTokenHandler_On401_WithNoRefreshToken_Returns401Untouched()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("old", DateTimeOffset.UtcNow.AddHours(1), "a@b.test", null));

        var inner = new SequencedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        var refreshHttp = new HttpClient(new CapturingHandler(_ =>
            Task.FromResult(JsonOk("{\"access_token\":\"new\",\"refresh_token\":\"rt2\",\"expires_in\":3600}"))))
            { BaseAddress = new Uri("https://gw.test/") };

        var handler = new BearerTokenHandler(store, refreshHttp) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };

        var resp = await client.GetAsync("api/v1/wallet/credentials");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        // Token should be unchanged (no refresh attempted, no clear)
        (await store.GetAsync())!.AccessToken.Should().Be("old");
    }

    [Fact]
    public async Task BearerTokenHandler_On401_FailedRefresh_ClearsSession()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("old", DateTimeOffset.UtcNow.AddHours(1), "a@b.test", "rt"));

        var inner = new SequencedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
        // Refresh endpoint returns 401 (refresh token invalid/expired)
        var refreshHttp = new HttpClient(new CapturingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized))))
            { BaseAddress = new Uri("https://gw.test/") };

        var handler = new BearerTokenHandler(store, refreshHttp) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };

        var resp = await client.GetAsync("api/v1/wallet/credentials");

        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        (await store.GetAsync()).Should().BeNull("failed refresh must clear the session");
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => responder(request);
    }

    private sealed class NoopLocalDataPurge : ILocalDataPurge
    {
        public Task PurgeAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static HttpResponseMessage JsonOk(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}

internal sealed class SequencedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(responder(request));
}

internal sealed class NullConsumeJsRuntime : Microsoft.JSInterop.IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => new((TValue)(object?)default!);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        => new((TValue)(object?)default!);
}
