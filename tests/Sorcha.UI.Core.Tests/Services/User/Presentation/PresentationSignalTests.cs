// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.User.Presentation;

/// <summary>
/// Feature 127 — verifies PresentationSignal's transport composition: a 2 s
/// hub-connect window, 3 s polling cadence against F111's status endpoint,
/// 60 s manual-recovery ceiling. Uses <see cref="FakeTimeProvider"/> so the
/// 60-second wait is instantaneous. The hub connect runs against an invalid
/// URL so it fails fast and the polling fallback engages.
/// </summary>
public sealed class PresentationSignalTests : IAsyncDisposable
{
    private readonly Guid _requestId = Guid.NewGuid();
    private readonly FakeTimeProvider _time = new();
    private readonly StubHandler _handler = new();
    private readonly HttpClient _http;
    private readonly PresentationHubConnection _hub;
    private readonly PresentationSignal _sut;

    public PresentationSignalTests()
    {
        _http = new HttpClient(_handler) { BaseAddress = new Uri("http://test.local") };
        // URL is unreachable on purpose — the hub fails fast so the polling
        // fallback engages quickly in the tests below.
        _hub = new PresentationHubConnection(
            "http://127.0.0.1:1",
            accessTokenProvider: null,
            logger: NullLogger<PresentationHubConnection>.Instance);
        _sut = new PresentationSignal(_hub, _http, _time, NullLogger<PresentationSignal>.Instance);
    }

    [Fact]
    public async Task StartAsync_RejectsEmptyRequestId()
    {
        Func<Task> act = () => _sut.StartAsync(Guid.Empty, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Polling_FiresOnOutcomeReady_WhenStatusReachesTerminal()
    {
        _handler.NextState = "success";
        PresentationSignalOutcome? captured = null;
        var done = new TaskCompletionSource();
        _sut.OnOutcomeReady += outcome =>
        {
            captured = outcome;
            done.TrySetResult();
            return Task.CompletedTask;
        };

        await _sut.StartAsync(_requestId, CancellationToken.None);

        // Advance past the 3 s polling cadence so the first poll fires.
        _time.Advance(TimeSpan.FromSeconds(4));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        captured.Should().NotBeNull();
        captured!.PresentationRequestId.Should().Be(_requestId);
        captured.Kind.Should().Be("success");
    }

    [Fact]
    public async Task Polling_IgnoresPendingState_AndKeepsLooping()
    {
        // First poll: not yet terminal. Second poll: success.
        _handler.NextState = "awaiting-presentation";
        PresentationSignalOutcome? captured = null;
        var done = new TaskCompletionSource();
        _sut.OnOutcomeReady += outcome =>
        {
            captured = outcome;
            done.TrySetResult();
            return Task.CompletedTask;
        };

        await _sut.StartAsync(_requestId, CancellationToken.None);

        // First tick — pending, no outcome.
        _time.Advance(TimeSpan.FromSeconds(4));
        await Task.Yield();
        captured.Should().BeNull();

        // Flip the stub, second tick — success.
        _handler.NextState = "success";
        _time.Advance(TimeSpan.FromSeconds(3));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        captured.Should().NotBeNull();
        captured!.Kind.Should().Be("success");
    }

    [Fact]
    public async Task ManualRecovery_Fires_When_NoSignal_Within60Seconds()
    {
        _handler.NextState = "awaiting-presentation"; // never terminal
        var manualRecoveryFired = false;
        _sut.OnManualRecoveryRequired += () => manualRecoveryFired = true;

        await _sut.StartAsync(_requestId, CancellationToken.None);

        // Advance past the 60 s ceiling.
        _time.Advance(TimeSpan.FromSeconds(61));
        await Task.Yield();
        await Task.Delay(50); // give async continuations a chance

        manualRecoveryFired.Should().BeTrue();
    }

    [Fact]
    public async Task ManualRecovery_DoesNotFire_AfterTerminalOutcome()
    {
        _handler.NextState = "success";
        var manualRecoveryFired = false;
        var outcomeFired = new TaskCompletionSource();
        _sut.OnManualRecoveryRequired += () => manualRecoveryFired = true;
        _sut.OnOutcomeReady += _ => { outcomeFired.TrySetResult(); return Task.CompletedTask; };

        await _sut.StartAsync(_requestId, CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(4));
        await outcomeFired.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Now advance past 60 s. The signal-received flag must suppress the
        // manual-recovery callback.
        _time.Advance(TimeSpan.FromSeconds(120));
        await Task.Yield();
        await Task.Delay(50);

        manualRecoveryFired.Should().BeFalse();
    }

    [Fact]
    public async Task FallbackEngaged_Fires_WhenHubFailsToConnect()
    {
        var fallbackEngaged = false;
        _sut.OnFallbackEngaged += () => fallbackEngaged = true;
        _handler.NextState = "awaiting-presentation";

        await _sut.StartAsync(_requestId, CancellationToken.None);

        // The hub URL is unreachable; the 2 s hub-connect window elapses on
        // the FakeTimeProvider before the (real-time) hub.StartAsync call
        // completes, so the polling loop is created with engageReason="hub-timeout".
        _time.Advance(TimeSpan.FromSeconds(3));
        await Task.Yield();
        await Task.Delay(50);

        fallbackEngaged.Should().BeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.DisposeAsync();
        await _hub.DisposeAsync();
        _http.Dispose();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string NextState { get; set; } = "awaiting-presentation";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = $"{{\"presentationRequestId\":\"00000000-0000-0000-0000-000000000000\",\"state\":\"{NextState}\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
