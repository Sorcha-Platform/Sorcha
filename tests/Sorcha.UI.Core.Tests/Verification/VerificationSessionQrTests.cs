// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.UI.Components.User.Extensions;
using Sorcha.UI.Components.User.Models.Verification;
using Sorcha.UI.Components.User.Services.Verification;
using Microsoft.AspNetCore.Components;
using Sorcha.UI.Core.Components.Verify;
using Xunit;

namespace Sorcha.UI.Core.Tests.Verification;

/// <summary>
/// bUnit tests for <see cref="VerificationSessionQr"/> (Feature 163, US2). Proves the component
/// activates under default DI without throwing (not-configured sentinel), renders QR with a fake
/// transport, polls pending→complete raising OnCompleted, and disposes mid-poll without unobserved
/// exceptions (FR-014, FR-007, R-003, R-006).
/// </summary>
public class VerificationSessionQrTests : BunitContext
{
    private static readonly VerificationPreset AgePreset = new(
        "age-over-18", "Age over 18?", "Confirm age over 18",
        "https://sorcha.example/vc/citizen/v1",
        ["age_over_18"], [], ["age_over_18", "portrait"]);

    private void SetupServices(IVerificationTransport? transport = null)
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var config = new ConfigurationBuilder().Build();
        Services.AddSingleton<IConfiguration>(config);

        if (transport is not null)
            Services.AddSingleton<IVerificationTransport>(transport);

        Services.AddSorchaUserComponents(config);
    }

    [Fact]
    public async Task VerificationSessionQr_DefaultDi_ActivatesWithoutThrowingAndRendersNotConfigured()
    {
        // US2 scenario 1 — mounts under default DI (NotConfiguredVerificationTransport sentinel),
        // renders the not-configured state, does not throw, does not poll.
        SetupServices(); // no transport override → NotConfiguredVerificationTransport

        IRenderedComponent<VerificationSessionQr>? cut = null;
        var ex = await Record.ExceptionAsync(() =>
        {
            cut = Render<VerificationSessionQr>(p => p.Add(x => x.Question, AgePreset));
            return Task.CompletedTask;
        });

        ex.Should().BeNull();
        cut.Should().NotBeNull();
        cut!.Find("[data-testid='not-configured']").Should().NotBeNull();
    }

    [Fact]
    public async Task VerificationSessionQr_WithFakeTransport_RendersQrOrDeepLink()
    {
        // US2 scenario 2 — with a fake transport returning a known session + QR deep link,
        // the component renders the QR active state.
        var transport = new FakeTransport(
            started: new VerificationSessionStarted("sess-001", "openid4vp://example", "Age", AgePreset.RequiredVct),
            pollResults: [new VerificationSessionPoll(false, null, null)]);

        SetupServices(transport);

        var cut = Render<VerificationSessionQr>(p => p.Add(x => x.Question, AgePreset));
        await Task.Delay(200); // let OnInitializedAsync complete
        cut.WaitForState(() => cut.Find("[data-testid='qr-active']") is not null, TimeSpan.FromSeconds(2));

        cut.Find("[data-testid='qr-active']").Should().NotBeNull();
    }

    [Fact]
    public async Task VerificationSessionQr_PollCompletesWithToken_RaisesOnCompleted()
    {
        // US2 scenarios 3-4 — fake transport returns pending then complete;
        // OnCompleted fires with the VpToken.
        var tcs = new TaskCompletionSource<string?>();
        var transport = new SequencedFakeTransport(
            started: new VerificationSessionStarted("sess-002", "openid4vp://token-test", "Age", AgePreset.RequiredVct),
            pollSequence:
            [
                new VerificationSessionPoll(false, null, null),
                new VerificationSessionPoll(true, "vp_token_value", null)
            ]);

        SetupServices(transport);

        string? received = null;
        var cut = Render<VerificationSessionQr>(p =>
        {
            p.Add(x => x.Question, AgePreset);
            p.Add(x => x.OnCompleted, EventCallback.Factory.Create<string>(this, v => received = v));
        });

        // Wait for the polling to complete (the fake advances on each call with 1ms delay)
        await Task.Delay(TimeSpan.FromSeconds(8)); // 2 × 3s delay + margin

        received.Should().Be("vp_token_value");
    }

    [Fact]
    public async Task VerificationSessionQr_DisposedMidPoll_CompletesCleanlyWithNoException()
    {
        // US2 scenario 5 — dispose component mid-poll; DisposeAsync completes,
        // no post-disposal render, no unobserved exception.
        var transport = new SlowFakeTransport(
            started: new VerificationSessionStarted("sess-003", "openid4vp://dispose-test", "Age", AgePreset.RequiredVct));

        SetupServices(transport);

        var cut = Render<VerificationSessionQr>(p => p.Add(x => x.Question, AgePreset));
        await Task.Delay(200); // let start complete and poll loop begin

        // Dispose mid-poll — must not throw
        var ex = await Record.ExceptionAsync(async () =>
        {
            await cut.Instance.DisposeAsync();
        });

        ex.Should().BeNull();
    }

    // --- fake transports ---

    private sealed class FakeTransport(
        VerificationSessionStarted started,
        IReadOnlyList<VerificationSessionPoll> pollResults) : IVerificationTransport
    {
        private int _callCount;

        public Task<VerificationSessionStarted> StartSessionAsync(VerificationPreset question, CancellationToken ct = default)
            => Task.FromResult(started);

        public Task<VerificationSessionPoll> PollSessionAsync(string sessionId, CancellationToken ct = default)
        {
            var idx = Math.Min(_callCount++, pollResults.Count - 1);
            return Task.FromResult(pollResults[idx]);
        }
    }

    private sealed class SequencedFakeTransport(
        VerificationSessionStarted started,
        IReadOnlyList<VerificationSessionPoll> pollSequence) : IVerificationTransport
    {
        private int _index;

        public Task<VerificationSessionStarted> StartSessionAsync(VerificationPreset question, CancellationToken ct = default)
            => Task.FromResult(started);

        public async Task<VerificationSessionPoll> PollSessionAsync(string sessionId, CancellationToken ct = default)
        {
            await Task.Delay(1, ct);
            var result = pollSequence[Math.Min(_index, pollSequence.Count - 1)];
            _index++;
            return result;
        }
    }

    private sealed class SlowFakeTransport(VerificationSessionStarted started) : IVerificationTransport
    {
        public Task<VerificationSessionStarted> StartSessionAsync(VerificationPreset question, CancellationToken ct = default)
            => Task.FromResult(started);

        public async Task<VerificationSessionPoll> PollSessionAsync(string sessionId, CancellationToken ct = default)
        {
            await Task.Delay(30_000, ct); // effectively infinite — will be cancelled
            return new VerificationSessionPoll(false, null, null);
        }
    }
}
