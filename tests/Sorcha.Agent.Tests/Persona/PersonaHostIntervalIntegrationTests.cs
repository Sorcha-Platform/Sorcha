// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

/// <summary>
/// End-to-end wiring test for Feature 110 User Story 2 (task T031).
/// Exercises <see cref="PersonaHost"/> + <see cref="IntervalTriggerLoop"/> + real
/// <see cref="PersonaSubmitter"/> + a stub <see cref="HttpMessageHandler"/>.
/// Asserts exactly <c>maxIterations</c> POSTs land with distinct <c>${counter}</c>
/// and <c>${random.decimal}</c> values in each captured request body — guarding
/// against regressions where the payload is resolved once and reused.
/// </summary>
public class PersonaHostIntervalIntegrationTests
{
    [Fact]
    public async Task PersonaHost_IntervalTrigger_EmitsMaxIterationsPostsWithDistinctTokens()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var submitter = new PersonaSubmitter(
            http,
            _ => Task.FromResult("test-token"),
            walletAddress: "wallet-seed",
            registerId: "register-seed",
            NullLogger<PersonaSubmitter>.Instance);

        var definition = new PersonaDefinition
        {
            Name = "invoice-generator",
            Target = new PersonaTarget
            {
                BlueprintId = "bp-invoice-seed",
                InstanceId = "inst-seed-1",
                ActionIndex = 0
            },
            Trigger = new IntervalTrigger
            {
                EverySeconds = 1,
                MaxIterations = 3
            },
            PayloadTemplate = JsonNode.Parse("""
                {
                  "iteration": "${counter}",
                  "amount": "${random.decimal(100, 999.99, 2)}",
                  "currency": "${random.choice([\"EUR\", \"GBP\", \"USD\"])}"
                }
                """)!
        };

        var host = new PersonaHost(
            definition,
            submitter,
            new PayloadTokenResolver(),
            new SequenceRandom(),
            TimeProvider.System,
            NullLoggerFactory.Instance);

        // Generous outer cancellation so a stuck loop can't hang CI, but the
        // loop should exit on MaxIterations well before this trips.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await host.RunAsync(cts.Token);

        host.CompletedIterations.Should().Be(3);
        handler.Requests.Should().HaveCount(3, "interval trigger must fire exactly maxIterations times");

        var iterations = new List<int>();
        var amounts = new List<decimal>();
        var currencies = new List<string>();

        foreach (var req in handler.Requests)
        {
            req.Method.Should().Be(HttpMethod.Post);
            req.Url.Should().Be("http://localhost/api/instances/inst-seed-1/actions/0/execute");

            var body = JsonNode.Parse(req.Body)!.AsObject();
            body["senderWallet"]!.GetValue<string>().Should().Be("wallet-seed");
            body["registerAddress"]!.GetValue<string>().Should().Be("register-seed");
            body["blueprintId"]!.GetValue<string>().Should().Be("bp-invoice-seed");
            body["actionId"]!.GetValue<string>().Should().Be("0");

            var payload = body["payloadData"]!.AsObject();
            iterations.Add(payload["iteration"]!.GetValue<int>());
            amounts.Add(payload["amount"]!.GetValue<decimal>());
            currencies.Add(payload["currency"]!.GetValue<string>());
        }

        // Counter must advance monotonically 1..3 — this is the core invariant
        // that IntervalTriggerLoopTests (which mocks IPersonaSubmitter) cannot
        // observe at wire level.
        iterations.Should().Equal(1, 2, 3);

        // Random tokens must resolve per-iteration, not once-and-reused.
        amounts.Should().OnlyHaveUniqueItems("${random.decimal} must be re-evaluated each fire");
        amounts.Should().AllSatisfy(a => a.Should().BeInRange(100m, 999.99m));
        currencies.Should().AllSatisfy(c => c.Should().BeOneOf("EUR", "GBP", "USD"));
    }

    private sealed record CapturedRequest(
        HttpMethod Method, string Url, string Body, string? AuthHeader, string? DelegationHeader);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                body,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("X-Delegation-Token", out var v) ? v.FirstOrDefault() : null));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// Deterministic but distinct-per-call random source: each call returns a
    /// different value so the assertion that tokens re-resolve per iteration
    /// is meaningful.
    /// </summary>
    private sealed class SequenceRandom : IRandomSource
    {
        private int _ints;
        private int _decs;
        private int _choices;

        public int NextInt(int min, int max) => min + (++_ints);

        public decimal NextDecimal(decimal min, decimal max, int precision)
        {
            var step = (++_decs) * 1.11m;
            return decimal.Round(min + step, precision);
        }

        public T Choose<T>(IReadOnlyList<T> options) => options[(_choices++) % options.Count];
    }
}
