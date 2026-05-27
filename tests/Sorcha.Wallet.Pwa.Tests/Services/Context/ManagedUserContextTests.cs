// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Context;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Context;

/// <summary>
/// Unit tests for <see cref="ManagedUserContext"/> (Feature 125, T080).
/// Verifies the v1 managed-mode context-switch contract: it persists state
/// across reads, drives <c>/api/auth/switch-org</c> on non-Personal switches,
/// rotates the access token, fires <c>OnContextChanged</c> with the right
/// payload, and falls back gracefully on server rejection.
/// </summary>
public sealed class ManagedUserContextTests
{
    private static readonly Guid OrgA = Guid.NewGuid();
    private static readonly Guid OrgB = Guid.NewGuid();

    [Fact]
    public async Task InitializeAsync_NoStoredRecord_DefaultsToPersonal()
    {
        var sut = NewSut(out _, out _, out _);
        await sut.InitializeAsync();
        sut.ActiveContextOrgId.Should().BeNull();
    }

    [Fact]
    public async Task InitializeAsync_HydratesFromPersistedRecord()
    {
        var store = new InMemoryActiveContextStore();
        await store.SetAsync(new ActiveContextRecord(OrgA, DateTimeOffset.UtcNow));
        var sut = NewSut(store, out _, out _);

        await sut.InitializeAsync();

        sut.ActiveContextOrgId.Should().Be(OrgA);
    }

    [Fact]
    public async Task SetActiveContextAsync_ToSameContext_IsNoOp()
    {
        var sut = NewSut(out _, out _, out var raised);
        await sut.InitializeAsync();

        var ok = await sut.SetActiveContextAsync(null);

        ok.Should().BeTrue();
        raised.Count.Should().Be(0, "switching to the current context must not raise the event.");
    }

    [Fact]
    public async Task SetActiveContextAsync_NonPersonal_PostsSwitchOrg_StoresToken_FiresEvent()
    {
        var handler = new RecordingHandler(req =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/auth/switch-org");
            req.Method.Should().Be(HttpMethod.Post);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { accessToken = "new-jwt", expiresIn = 3600 })
            };
        });
        var sut = NewSut(handler, out var store, out var tokenStore, out var raised);
        await sut.InitializeAsync();

        var ok = await sut.SetActiveContextAsync(OrgA);

        ok.Should().BeTrue();
        sut.ActiveContextOrgId.Should().Be(OrgA);
        handler.CallCount.Should().Be(1, "exactly one switch-org POST per non-Personal switch.");
        (await tokenStore.GetAsync())!.AccessToken.Should().Be("new-jwt");
        (await store.GetAsync())!.ContextOrgId.Should().Be(OrgA);
        raised.Should().ContainSingle();
        raised[0].FromContextOrgId.Should().BeNull();
        raised[0].ToContextOrgId.Should().Be(OrgA);
    }

    [Fact]
    public async Task SetActiveContextAsync_ServerRejects_LeavesStateUnchanged_DoesNotFireEvent()
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var sut = NewSut(handler, out var store, out _, out var raised);
        await sut.InitializeAsync();

        var ok = await sut.SetActiveContextAsync(OrgA);

        ok.Should().BeFalse();
        sut.ActiveContextOrgId.Should().BeNull("a rejected switch must not move the active context.");
        (await store.GetAsync()).Should().BeNull("a rejected switch must not persist the new context.");
        raised.Should().BeEmpty();
    }

    [Fact]
    public async Task SetActiveContextAsync_BackToPersonal_DoesNotCallServer_FiresEvent()
    {
        // Start in OrgA — switch-org once, then back to Personal.
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { accessToken = "new-jwt", expiresIn = 3600 })
        });
        var sut = NewSut(handler, out _, out _, out var raised);
        await sut.InitializeAsync();
        await sut.SetActiveContextAsync(OrgA);
        handler.CallCount.Should().Be(1);

        var ok = await sut.SetActiveContextAsync(null);

        ok.Should().BeTrue();
        sut.ActiveContextOrgId.Should().BeNull();
        handler.CallCount.Should().Be(1, "switching back to Personal in v1 keeps the existing token — no new switch-org call.");
        raised.Should().HaveCount(2);
        raised[1].FromContextOrgId.Should().Be(OrgA);
        raised[1].ToContextOrgId.Should().BeNull();
    }

    [Fact]
    public async Task SetActiveContextAsync_BetweenTwoOrgs_RotatesTokenEachTime()
    {
        var handler = new RecordingHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { accessToken = $"jwt-{Guid.NewGuid():N}", expiresIn = 3600 })
        });
        var sut = NewSut(handler, out _, out var tokenStore, out var raised);
        await sut.InitializeAsync();

        await sut.SetActiveContextAsync(OrgA);
        var tokenA = (await tokenStore.GetAsync())!.AccessToken;
        await sut.SetActiveContextAsync(OrgB);
        var tokenB = (await tokenStore.GetAsync())!.AccessToken;

        handler.CallCount.Should().Be(2);
        tokenA.Should().NotBe(tokenB);
        raised.Should().HaveCount(2);
    }

    // ---- helpers ----

    private static ManagedUserContext NewSut(out InMemoryActiveContextStore store,
                                              out InMemoryAccessTokenStore tokenStore,
                                              out List<UserContextChangedEventArgs> raised)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented));
        return NewSut(handler, out store, out tokenStore, out raised);
    }

    private static ManagedUserContext NewSut(InMemoryActiveContextStore store, out InMemoryAccessTokenStore tokenStore,
                                              out List<UserContextChangedEventArgs> raised)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotImplemented));
        tokenStore = new InMemoryAccessTokenStore();
        raised = new List<UserContextChangedEventArgs>();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var sut = new ManagedUserContext(store, tokenStore, http, NullLogger<ManagedUserContext>.Instance);
        var captured = raised;
        sut.OnContextChanged += args => { captured.Add(args); return Task.CompletedTask; };
        return sut;
    }

    private static ManagedUserContext NewSut(RecordingHandler handler,
                                              out InMemoryActiveContextStore store,
                                              out InMemoryAccessTokenStore tokenStore,
                                              out List<UserContextChangedEventArgs> raised)
    {
        store = new InMemoryActiveContextStore();
        tokenStore = new InMemoryAccessTokenStore();
        raised = new List<UserContextChangedEventArgs>();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var sut = new ManagedUserContext(store, tokenStore, http, NullLogger<ManagedUserContext>.Instance);
        var captured = raised;
        sut.OnContextChanged += args => { captured.Add(args); return Task.CompletedTask; };
        return sut;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int CallCount { get; private set; }
        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_respond(request));
        }
    }
}
