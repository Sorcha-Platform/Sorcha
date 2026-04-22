// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Inbox;
using Sorcha.Agent.Models;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

/// <summary>
/// Feature 110 User Story 3 (task T039): regression tests for graceful
/// shutdown. When the shared <see cref="CancellationTokenSource"/> is
/// cancelled, both the persona loop and the reactive inbox loop must exit
/// within ≤ 1 second so <c>Ctrl+C</c> on <c>sorcha-agent</c> is responsive.
/// </summary>
public class PersonaShutdownTests
{
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task SharedCtsCancel_PersonaAndReactiveBothExit_WithinOneSecond()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        // Interval persona with a 60s cadence — guaranteed to be sitting in
        // a Task.Delay when cancellation fires; if cancellation isn't observed
        // promptly this test will time out far above the 1s budget.
        var submitter = new PersonaSubmitter(
            http, _ => Task.FromResult("tok"),
            "w", "r", NullLogger<PersonaSubmitter>.Instance);

        var definition = new PersonaDefinition
        {
            Name = "shutdown-test",
            Target = new PersonaTarget { BlueprintId = "bp", InstanceId = "i", ActionIndex = 0 },
            // StartDelaySeconds=60 puts the loop into Task.Delay before any fire,
            // so CompletedIterations stays 0 and we verify cancellation unwinds
            // the pre-fire wait (the common real-world case for Ctrl+C shortly
            // after start-up).
            Trigger = new IntervalTrigger { StartDelaySeconds = 60, EverySeconds = 60, MaxIterations = 100 },
            PayloadTemplate = JsonNode.Parse("""{ "n": "${counter}" }""")!
        };

        var host = new PersonaHost(
            definition, submitter, new PayloadTokenResolver(), new StubRandom(),
            TimeProvider.System, NullLoggerFactory.Instance);

        var listener = new BlockingInboxListener();

        using var cts = new CancellationTokenSource();

        var personaTask = host.RunAsync(cts.Token);
        var reactiveTask = ReactiveLoop(listener, cts.Token);

        // Give both sides a moment to enter their blocking waits before
        // we measure how quickly they unwind.
        await Task.Delay(200);

        var sw = Stopwatch.StartNew();
        await cts.CancelAsync();

        // Neither loop should throw — both observe the token cleanly.
        await SuppressCancellationAsync(personaTask);
        await SuppressCancellationAsync(reactiveTask);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(ShutdownBudget,
            "persona + reactive loops must unwind within 1 s of shared-CTS cancel");
        host.CompletedIterations.Should().Be(0, "no iteration had time to complete before cancel");
    }

    private static async Task ReactiveLoop(IInboxListener listener, CancellationToken ct)
    {
        await foreach (var _ in listener.ListenAsync(ct))
        {
            // never reached in this test — listener blocks until cancellation
        }
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { /* expected */ }
    }

    /// <summary>
    /// <see cref="IInboxListener"/> that waits indefinitely for its token to
    /// be cancelled — models a real listener sitting idle on a SignalR
    /// connection or polling delay.
    /// </summary>
    private sealed class BlockingInboxListener : IInboxListener
    {
        public async IAsyncEnumerable<PendingAction> ListenAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break; // unreachable — Task.Delay throws on cancel
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class StubRandom : IRandomSource
    {
        public int NextInt(int min, int max) => min;
        public decimal NextDecimal(decimal min, decimal max, int p) => min;
        public T Choose<T>(IReadOnlyList<T> options) => options[0];
    }
}
