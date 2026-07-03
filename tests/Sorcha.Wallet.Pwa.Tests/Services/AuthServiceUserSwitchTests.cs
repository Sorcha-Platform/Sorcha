// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Guards the cross-user data-leak fix: signing in as a DIFFERENT user (different JWT <c>sub</c>) must
/// wipe every per-device store first, while re-authenticating as the SAME user must keep the cache.
/// </summary>
public sealed class AuthServiceUserSwitchTests
{
    private static string B64Url(string s)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string TokenFor(string sub)
        => $"{B64Url("{\"alg\":\"none\"}")}.{B64Url($"{{\"sub\":\"{sub}\"}}")}.sig";

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json")
            });
    }

    private static string LoginJson(string sub)
        => $"{{\"access_token\":\"{TokenFor(sub)}\",\"expires_in\":3600,\"requires_two_factor\":false}}";

    private static (AuthService svc, StubHandler handler, Mock<ILocalDataPurge> purge, IAccessTokenStore store) Build()
    {
        var handler = new StubHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var store = new InMemoryAccessTokenStore();
        var purge = new Mock<ILocalDataPurge>();
        var passkey = new Mock<IPasskeyInterop>();
        var svc = new AuthService(http, store, purge.Object, passkey.Object);
        return (svc, handler, purge, store);
    }

    [Fact]
    public async Task SignIn_DifferentUser_PurgesLocalDataOnce()
    {
        var (svc, handler, purge, store) = Build();

        handler.Body = LoginJson("user-A");
        (await svc.SignInAsync("a@x.test", "pw")).Status.Should().Be(SignInStatus.Success);
        purge.Verify(p => p.PurgeAsync(It.IsAny<CancellationToken>()), Times.Never,
            "first sign-in on a fresh device has no prior owner to purge");
        (await store.GetDataOwnerAsync()).Should().Be("user-A");

        handler.Body = LoginJson("user-B");
        (await svc.SignInAsync("b@x.test", "pw")).Status.Should().Be(SignInStatus.Success);

        purge.Verify(p => p.PurgeAsync(It.IsAny<CancellationToken>()), Times.Once,
            "signing in as a different user must wipe the previous user's per-device cache");
        (await store.GetDataOwnerAsync()).Should().Be("user-B");
    }

    [Fact]
    public async Task SignIn_SameUserReauth_DoesNotPurge()
    {
        var (svc, handler, purge, _) = Build();

        handler.Body = LoginJson("user-A");
        await svc.SignInAsync("a@x.test", "pw");
        await svc.SignInAsync("a@x.test", "pw"); // same sub — e.g. re-login after expiry

        purge.Verify(p => p.PurgeAsync(It.IsAny<CancellationToken>()), Times.Never,
            "re-authenticating as the same user must keep their cached credentials");
    }
}
