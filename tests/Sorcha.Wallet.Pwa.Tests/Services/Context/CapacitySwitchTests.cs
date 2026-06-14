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
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Context;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Context;

/// <summary>
/// Feature 153 (D, US1) — capacity transitions in <see cref="ManagedUserContext"/>: switching into
/// an org snapshots the personal/home token and activates the org token; returning to Personal
/// restores the home token (so consumer-gated surfaces keep working); a declined switch is a no-op.
/// </summary>
public sealed class CapacitySwitchTests
{
    private static readonly Guid Org = Guid.NewGuid();

    private static AccessTokenRecord Consumer() =>
        new("consumer.jwt", DateTimeOffset.UtcNow.AddHours(1), Email: "c@x");

    private static ManagedUserContext Create(IAccessTokenStore tokens, StubHandler handler) =>
        new(new InMemoryActiveContextStore(),
            tokens,
            new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") },
            NullLogger<ManagedUserContext>.Instance);

    [Fact]
    public async Task SwitchToOrg_SnapshotsHome_AndActivatesOrgToken()
    {
        var tokens = new InMemoryAccessTokenStore();
        await tokens.SetAsync(Consumer()); // signed in personally
        var ctx = Create(tokens, OkSwitch("org.jwt"));

        var ok = await ctx.SetActiveContextAsync(Org);

        ok.Should().BeTrue();
        ctx.ActiveContextOrgId.Should().Be(Org);
        (await tokens.GetAsync())!.AccessToken.Should().Be("org.jwt", "the org token is now active");
        (await tokens.GetHomeAsync())!.AccessToken.Should().Be("consumer.jwt", "the personal token was snapshotted");
    }

    [Fact]
    public async Task SwitchBackToPersonal_RestoresHomeToken()
    {
        var tokens = new InMemoryAccessTokenStore();
        await tokens.SetAsync(Consumer());
        var ctx = Create(tokens, OkSwitch("org.jwt"));
        await ctx.SetActiveContextAsync(Org);          // → org
        (await tokens.GetAsync())!.AccessToken.Should().Be("org.jwt");

        var ok = await ctx.SetActiveContextAsync(null); // → Personal

        ok.Should().BeTrue();
        ctx.ActiveContextOrgId.Should().BeNull();
        (await tokens.GetAsync())!.AccessToken.Should().Be("consumer.jwt",
            "returning to Personal must restore the consumer token (no residual platform token)");
    }

    [Fact]
    public async Task SwitchToOrg_ServerDeclines_IsNoOp()
    {
        var tokens = new InMemoryAccessTokenStore();
        await tokens.SetAsync(Consumer());
        var ctx = Create(tokens, new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var ok = await ctx.SetActiveContextAsync(Org);

        ok.Should().BeFalse();
        ctx.ActiveContextOrgId.Should().BeNull("a declined switch leaves the capacity unchanged");
        (await tokens.GetAsync())!.AccessToken.Should().Be("consumer.jwt", "token unchanged on a declined switch");
    }

    private static StubHandler OkSwitch(string jwt) => new((_, _) =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"accessToken":"{{jwt}}","expiresIn":3600}""", Encoding.UTF8, "application/json"),
        });

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request, ct));
    }
}
