// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

/// <summary>
/// End-to-end wiring test for Feature 110 User Story 1 (task T023).
/// Exercises <see cref="PersonaHost"/> + <see cref="OnceTriggerLoop"/> + real
/// <see cref="PersonaSubmitter"/> + a stub <see cref="HttpMessageHandler"/>.
/// Asserts exactly one POST lands on the expected endpoint with the resolved payload.
/// </summary>
public class PersonaHostOneShotIntegrationTests
{
    [Fact]
    public async Task PersonaHost_OnceTrigger_ProducesExactlyOneSubmissionWithResolvedPayload()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var submitter = new PersonaSubmitter(
            http,
            _ => Task.FromResult("test-token"),
            walletAddress: "wallet-abc",
            registerId: "register-xyz",
            NullLogger<PersonaSubmitter>.Instance);

        var definition = new PersonaDefinition
        {
            Name = "procurement-mgr-kickoff",
            Target = new PersonaTarget
            {
                BlueprintId = "bp-procurement-to-pay",
                InstanceId = "inst-99",
                ActionIndex = 0
            },
            Trigger = new OnceTrigger { DelaySeconds = 0 },
            PayloadTemplate = JsonNode.Parse("""
                {
                  "poReference": "PO-CAIRN-2026-00142",
                  "lineItem": "Structural Timber",
                  "iteration": "${counter}"
                }
                """)!
        };

        var host = new PersonaHost(
            definition,
            submitter,
            new PayloadTokenResolver(),
            new DeterministicRandom(),
            TimeProvider.System,
            NullLoggerFactory.Instance);

        await host.RunAsync(CancellationToken.None);

        handler.Requests.Should().HaveCount(1, "once-trigger must fire exactly once");
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Post);
        request.Url.Should().Be("http://localhost/api/instances/inst-99/actions/0/execute");

        // Headers propagate the token via both Authorization and X-Delegation-Token.
        request.AuthHeader.Should().Be("test-token");
        request.DelegationHeader.Should().Be("test-token");

        // Body carries the resolved payload with counter=1 (integer, not string).
        var body = JsonNode.Parse(request.Body)!.AsObject();
        body["senderWallet"]!.GetValue<string>().Should().Be("wallet-abc");
        body["registerAddress"]!.GetValue<string>().Should().Be("register-xyz");
        body["instanceId"]!.GetValue<string>().Should().Be("inst-99");
        body["blueprintId"]!.GetValue<string>().Should().Be("bp-procurement-to-pay");
        body["actionId"]!.GetValue<string>().Should().Be("0");

        var payload = body["payloadData"]!.AsObject();
        payload["poReference"]!.GetValue<string>().Should().Be("PO-CAIRN-2026-00142");
        payload["iteration"]!.GetValue<int>().Should().Be(1);

        host.CompletedIterations.Should().Be(1);
    }

    [Fact]
    public async Task PersonaHost_OnceTrigger_CancelledBeforeDelayElapses_ProducesNoSubmission()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var submitter = new PersonaSubmitter(
            http, _ => Task.FromResult("test-token"),
            "w", "r", NullLogger<PersonaSubmitter>.Instance);

        var definition = new PersonaDefinition
        {
            Name = "kickoff-slow",
            Target = new PersonaTarget { BlueprintId = "bp", InstanceId = "i", ActionIndex = 0 },
            Trigger = new OnceTrigger { DelaySeconds = 30 },
            PayloadTemplate = JsonNode.Parse("{}")!
        };

        var host = new PersonaHost(definition, submitter, new PayloadTokenResolver(),
            new DeterministicRandom(), TimeProvider.System, NullLoggerFactory.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await host.RunAsync(cts.Token);

        handler.Requests.Should().BeEmpty();
        host.CompletedIterations.Should().Be(0);
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

    private sealed class DeterministicRandom : IRandomSource
    {
        public int NextInt(int min, int max) => min;
        public decimal NextDecimal(decimal min, decimal max, int p) => min;
        public T Choose<T>(IReadOnlyList<T> options) => options[0];
    }
}
