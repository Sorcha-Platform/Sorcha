// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Inbox;
using Sorcha.Agent.Models;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

/// <summary>
/// Feature 110 User Story 3 (task T036): proves the persona loop and the reactive
/// inbox loop coexist without blocking each other when they share the same
/// <see cref="HttpClient"/> and <see cref="CancellationTokenSource"/>. Both
/// code paths must emit their submissions regardless of which fires first.
///
/// Rather than re-wire the full <see cref="Commands.RunCommand"/>
/// (which loads config from disk, builds AgentAuthService, etc.), this test
/// exercises the shape RunCommand uses: a <see cref="PersonaHost"/> task
/// started first, followed by <c>await foreach</c> over an
/// <see cref="IInboxListener"/>. That is the concurrency surface SC-004 cares
/// about — config wiring has its own tests.
/// </summary>
public class PersonaReactiveCoexistenceTests
{
    [Fact]
    public async Task PersonaFiresBeforeReactiveYields_BothPostsLand()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var reactiveReleased = new TaskCompletionSource();
        var listener = new GatedInboxListener(reactiveReleased.Task, ReactiveAction("reactive-inst-a"));

        var host = BuildPersonaHost(http, instanceId: "persona-inst-a", delaySeconds: 0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var personaTask = host.RunAsync(cts.Token);
        var reactiveTask = ReactiveLoop(listener, http, cts.Token);

        await personaTask; // once-trigger completes, guaranteeing its POST landed
        reactiveReleased.TrySetResult(); // now let reactive side proceed
        await WaitForRequestsAsync(handler, count: 2, timeout: TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await SuppressCancellationAsync(reactiveTask);

        AssertBothCodePathsEmitted(handler, personaInst: "persona-inst-a", reactiveInst: "reactive-inst-a");
    }

    [Fact]
    public async Task ReactiveProcessesBeforePersonaFires_BothPostsLand()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var reactiveReleased = new TaskCompletionSource();
        var listener = new GatedInboxListener(reactiveReleased.Task, ReactiveAction("reactive-inst-b"));

        // Persona has a generous delay so reactive is guaranteed to fire first.
        var host = BuildPersonaHost(http, instanceId: "persona-inst-b", delaySeconds: 2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var personaTask = host.RunAsync(cts.Token);
        var reactiveTask = ReactiveLoop(listener, http, cts.Token);

        reactiveReleased.SetResult(); // reactive yields immediately
        await WaitForRequestsAsync(handler, count: 1, timeout: TimeSpan.FromSeconds(3));
        handler.Requests[0].Url.Should().Contain("reactive-inst-b",
            "reactive path must emit its POST before persona finishes its delay");

        await personaTask; // now wait for persona to complete its delayed fire
        await WaitForRequestsAsync(handler, count: 2, timeout: TimeSpan.FromSeconds(3));
        await cts.CancelAsync();
        await SuppressCancellationAsync(reactiveTask);

        AssertBothCodePathsEmitted(handler, personaInst: "persona-inst-b", reactiveInst: "reactive-inst-b");
    }

    private static PersonaHost BuildPersonaHost(HttpClient http, string instanceId, int delaySeconds)
    {
        var submitter = new PersonaSubmitter(
            http, _ => Task.FromResult("test-token"),
            walletAddress: "persona-wallet", registerId: "persona-register",
            NullLogger<PersonaSubmitter>.Instance);

        var definition = new PersonaDefinition
        {
            Name = "coex-persona",
            Target = new PersonaTarget { BlueprintId = "bp-coex", InstanceId = instanceId, ActionIndex = 0 },
            Trigger = new OnceTrigger { DelaySeconds = delaySeconds },
            PayloadTemplate = JsonNode.Parse("""{ "k": "persona" }""")!
        };

        return new PersonaHost(
            definition,
            submitter,
            new PayloadTokenResolver(),
            new StubRandom(),
            TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    private static PendingAction ReactiveAction(string instanceId) => new()
    {
        ActionId = "reactive-action-1",
        ActionName = "Reactive",
        ActionIndex = 0,
        BlueprintId = "bp-coex",
        InstanceId = instanceId,
        RegisterId = "persona-register",
        TransactionId = "tx-stub"
    };

    /// <summary>
    /// Mimics the <c>await foreach</c> shape in <see cref="Commands.RunCommand"/>
    /// without importing its full decision/execution pipeline. Each pending
    /// action triggers a distinctive POST so it can be told apart from persona
    /// submissions in captured requests.
    /// </summary>
    private static async Task ReactiveLoop(IInboxListener listener, HttpClient http, CancellationToken ct)
    {
        await foreach (var action in listener.ListenAsync(ct))
        {
            var url = $"/api/instances/{action.InstanceId}/actions/{action.ActionIndex}/execute";
            var body = new StringContent("""{ "source": "reactive" }""", System.Text.Encoding.UTF8, "application/json");
            await http.PostAsync(url, body, ct);
        }
    }

    private static async Task WaitForRequestsAsync(CapturingHandler handler, int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (handler.Requests.Count >= count) return;
            await Task.Delay(20);
        }
        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(count,
            $"expected at least {count} captured requests within {timeout}");
    }

    private static async Task SuppressCancellationAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { /* expected on cts.Cancel */ }
    }

    private static void AssertBothCodePathsEmitted(CapturingHandler handler, string personaInst, string reactiveInst)
    {
        var urls = handler.Requests.Select(r => r.Url).ToList();
        urls.Should().Contain(u => u.Contains(personaInst), "persona path must emit its POST");
        urls.Should().Contain(u => u.Contains(reactiveInst), "reactive path must emit its POST");

        // Distinct bodies confirm each POST originated from its own code path.
        var bodies = handler.Requests.Select(r => r.Body).ToList();
        bodies.Should().Contain(b => b.Contains("payloadData"), "persona submitter wraps payload under 'payloadData'");
        bodies.Should().Contain(b => b.Contains("\"source\": \"reactive\""), "reactive loop sends its own distinctive body");
    }

    private sealed record CapturedRequest(HttpMethod Method, string Url, string Body);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _requests = new();
        private readonly object _lock = new();

        public IReadOnlyList<CapturedRequest> Requests
        {
            get { lock (_lock) return _requests.ToArray(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_lock)
            {
                _requests.Add(new CapturedRequest(
                    request.Method,
                    request.RequestUri!.ToString(),
                    body));
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// <see cref="IInboxListener"/> that waits on a caller-supplied
    /// <see cref="Task"/> before yielding its single pending action, allowing
    /// tests to gate reactive/persona ordering via <see cref="TaskCompletionSource"/>.
    /// </summary>
    private sealed class GatedInboxListener : IInboxListener
    {
        private readonly Task _gate;
        private readonly PendingAction _action;

        public GatedInboxListener(Task gate, PendingAction action)
        {
            _gate = gate;
            _action = action;
        }

        public async IAsyncEnumerable<PendingAction> ListenAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            yield return _action;
        }
    }

    private sealed class StubRandom : IRandomSource
    {
        public int NextInt(int min, int max) => min;
        public decimal NextDecimal(decimal min, decimal max, int p) => min;
        public T Choose<T>(IReadOnlyList<T> options) => options[0];
    }
}
