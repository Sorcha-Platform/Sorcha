// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.User.Presentation;

/// <summary>
/// The Feature 127 gate transport. Waiting is delegated to <see cref="IPresentationSignal"/>,
/// which already races the hub against a status poll, so these tests drive that signal directly
/// and assert the mapping onto <see cref="GateOutcome"/> plus the claims-fetch rules.
/// </summary>
public class SorchaWalletGateTransportTests
{
    /// <summary>Raises signal events on demand so the wait can be driven from a test.</summary>
    private sealed class FakeSignal : IPresentationSignal
    {
        public event Func<PresentationSignalOutcome, Task>? OnOutcomeReady;
        public event Action? OnFallbackEngaged;
        public event Action? OnManualRecoveryRequired;
        public event Action? OnRequestUnreachable;

        public Guid Started { get; private set; }
        public bool Stopped { get; private set; }

        public Task StartAsync(Guid id, CancellationToken ct)
        {
            Started = id;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public Task RaiseOutcomeAsync(Guid id, string kind)
            => OnOutcomeReady?.Invoke(new PresentationSignalOutcome(id, kind)) ?? Task.CompletedTask;

        public void RaiseUnreachable() => OnRequestUnreachable?.Invoke();
        public void RaiseManualRecovery() => OnManualRecoveryRequired?.Invoke();
        public void RaiseFallback() => OnFallbackEngaged?.Invoke();
    }

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private static SorchaWalletGateTransport Build(IPresentationSignal signal, Stub? stub = null)
        => new(signal,
               new HttpClient(stub ?? new Stub(HttpStatusCode.OK, "{}"))
               {
                   BaseAddress = new Uri("https://n1.test/")
               },
               NullLogger<SorchaWalletGateTransport>.Instance);

    [Theory]
    [InlineData("success", GateOutcome.Success)]
    [InlineData("abandoned-with-late-outcome", GateOutcome.Success)]
    [InlineData("decline", GateOutcome.Declined)]
    [InlineData("expired", GateOutcome.Expired)]
    [InlineData("abandoned", GateOutcome.Abandoned)]
    public async Task SignalKindMapsOntoGateOutcome(string kind, GateOutcome expected)
    {
        var signal = new FakeSignal();
        var sut = Build(signal);
        var id = Guid.NewGuid();

        var waiting = sut.WaitForOutcomeAsync(id);
        await signal.RaiseOutcomeAsync(id, kind);

        (await waiting).Should().Be(expected);
    }

    [Fact]
    public async Task OutcomeForADifferentRequestIsIgnored()
    {
        var signal = new FakeSignal();
        var sut = Build(signal);
        var mine = Guid.NewGuid();

        var waiting = sut.WaitForOutcomeAsync(mine);
        await signal.RaiseOutcomeAsync(Guid.NewGuid(), "success");

        waiting.IsCompleted.Should().BeFalse("another request's outcome must not clear my gate");

        await signal.RaiseOutcomeAsync(mine, "decline");
        (await waiting).Should().Be(GateOutcome.Declined);
    }

    [Fact]
    public async Task UnreachableSignalEndsTheWait()
    {
        var signal = new FakeSignal();
        var sut = Build(signal);

        var waiting = sut.WaitForOutcomeAsync(Guid.NewGuid());
        signal.RaiseUnreachable();

        (await waiting).Should().Be(GateOutcome.Unreachable);
    }

    [Fact]
    public async Task ManualRecoveryDoesNotEndTheWait()
    {
        // Observed live on n1 (2026-07-28): a request valid for TEN MINUTES was reported to the
        // citizen as "nothing was sent from your wallet" after 60 s, while it sat in
        // awaiting-presentation waiting for them to scan. Sixty seconds is how long it takes to
        // pick up a phone — not evidence of failure.
        var signal = new FakeSignal();
        var sut = Build(signal);
        var id = Guid.NewGuid();

        var waiting = sut.WaitForOutcomeAsync(id);
        signal.RaiseManualRecovery();

        waiting.IsCompleted.Should().BeFalse(
            "the request is still valid; only a terminal outcome or expiry may end the wait");

        await signal.RaiseOutcomeAsync(id, "success");
        (await waiting).Should().Be(GateOutcome.Success,
            "a late scan must still succeed");
    }

    [Fact]
    public async Task ExpiryStillEndsTheWait()
    {
        // Expiry is the lifecycle's call, surfaced through /status — not a client-side timer.
        var signal = new FakeSignal();
        var sut = Build(signal);
        var id = Guid.NewGuid();

        var waiting = sut.WaitForOutcomeAsync(id);
        signal.RaiseManualRecovery();
        await signal.RaiseOutcomeAsync(id, "expired");

        (await waiting).Should().Be(GateOutcome.Expired);
    }

    [Fact]
    public async Task CancellationEndsTheWaitAndStopsTheSignal()
    {
        var signal = new FakeSignal();
        var sut = Build(signal);
        using var cts = new CancellationTokenSource();

        var waiting = sut.WaitForOutcomeAsync(Guid.NewGuid(), null, cts.Token);
        await cts.CancelAsync();

        (await waiting).Should().Be(GateOutcome.Abandoned);
        signal.Stopped.Should().BeTrue("the signal must not outlive the wait");
    }

    [Fact]
    public async Task TheSignalIsStartedForTheRequestBeingWaitedOn()
    {
        var signal = new FakeSignal();
        var sut = Build(signal);
        var id = Guid.NewGuid();

        var waiting = sut.WaitForOutcomeAsync(id);
        await signal.RaiseOutcomeAsync(id, "success");
        await waiting;

        signal.Started.Should().Be(id);
    }

    [Fact]
    public async Task ClaimsFetchWithoutTokenDoesNotCallTheEndpoint()
    {
        // The token is single-use and consumed even on a pending response, so a call without one
        // can never succeed and only risks burning state.
        var stub = new Stub(HttpStatusCode.OK, "{}");
        var sut = Build(new FakeSignal(), stub);

        var claims = await sut.FetchClaimsAsync(Guid.NewGuid(), claimsFetchToken: null);

        claims.Should().BeNull();
        stub.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ClaimsFetchReturnsClaimsOnSuccess()
    {
        var id = Guid.NewGuid();
        // Built by concatenation: a raw literal ending in "}}" would be read as an interpolation
        // terminator.
        var stub = new Stub(HttpStatusCode.OK,
            "{\"presentationRequestId\":\"" + id + "\",\"status\":\"success\","
            + "\"claims\":{\"givenName\":\"Stuart\",\"age_over_18\":true}}");
        var sut = Build(new FakeSignal(), stub);

        var claims = await sut.FetchClaimsAsync(id, "tok-abc");

        claims.Should().NotBeNull();
        claims!.Should().ContainKey("givenName");
        claims.Should().ContainKey("age_over_18", "a non-string claim must survive the fetch");
        stub.LastUri.Should().Contain("tok-abc");
        stub.LastUri.Should().Contain("disclosed-claims");
    }

    [Fact]
    public async Task ClaimsFetchReturnsNullWhenStatusIsNotSuccess()
    {
        var id = Guid.NewGuid();
        var stub = new Stub(HttpStatusCode.OK,
            $$"""
            {"presentationRequestId":"{{id}}","status":"pending"}
            """);
        var sut = Build(new FakeSignal(), stub);

        (await sut.FetchClaimsAsync(id, "tok-abc")).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ClaimsFetchReturnsNullRatherThanThrowing(HttpStatusCode status)
    {
        var sut = Build(new FakeSignal(), new Stub(status, "{}"));

        (await sut.FetchClaimsAsync(Guid.NewGuid(), "tok-abc")).Should().BeNull();
    }

    [Fact]
    public void SourceIsSorchaWallet()
        => Build(new FakeSignal()).Source.Should().Be(PresentationSource.SorchaWallet);
}
