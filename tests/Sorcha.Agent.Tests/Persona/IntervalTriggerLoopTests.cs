// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

public class IntervalTriggerLoopTests
{
    // Tests use 50-ms intervals to stay fast (each test < 1 s) while still
    // exercising real-time scheduling; determinism of counter/random values
    // is provided by the stub random source and a counter-based submitter.
    private const int IntervalSeconds = 0; // 0 maps to zero-delay; we use EverySeconds=1 with immediate evaluation
    private const int FastInterval = 1;    // 1-second interval keeps tests small

    private static PersonaDefinition MakeDefinition(IntervalTrigger trigger) => new()
    {
        Name = "interval-test",
        Target = new PersonaTarget { BlueprintId = "bp-1", InstanceId = "inst-1", ActionIndex = 0 },
        Trigger = trigger,
        PayloadTemplate = JsonNode.Parse("""{ "n": "${counter}" }""")!
    };

    private static IntervalTriggerLoop MakeLoop(PersonaDefinition def, IPersonaSubmitter submitter) =>
        new(def, (IntervalTrigger)def.Trigger, submitter, new PayloadTokenResolver(),
            new StubRandom(), TimeProvider.System, NullLogger<IntervalTriggerLoop>.Instance);

    [Fact]
    public async Task RunAsync_MaxIterations_FiresExactlyThatMany()
    {
        var trigger = new IntervalTrigger { EverySeconds = FastInterval, MaxIterations = 3 };
        var def = MakeDefinition(trigger);
        var submitter = new CountingSubmitter(PersonaSubmissionOutcome.Submitted);
        var loop = MakeLoop(def, submitter);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await loop.RunAsync(cts.Token);

        loop.CompletedIterations.Should().Be(3);
        submitter.Calls.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_UntilInPast_ExitsWithoutFiring()
    {
        var trigger = new IntervalTrigger
        {
            EverySeconds = FastInterval,
            Until = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var def = MakeDefinition(trigger);
        var submitter = new CountingSubmitter(PersonaSubmissionOutcome.Submitted);
        var loop = MakeLoop(def, submitter);

        await loop.RunAsync(CancellationToken.None);

        loop.CompletedIterations.Should().Be(0);
        submitter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_UntilSoon_StopsBeforeMaxIterations()
    {
        var trigger = new IntervalTrigger
        {
            EverySeconds = FastInterval,
            MaxIterations = 1000,
            Until = DateTimeOffset.UtcNow.AddMilliseconds(1500)
        };
        var def = MakeDefinition(trigger);
        var submitter = new CountingSubmitter(PersonaSubmissionOutcome.Submitted);
        var loop = MakeLoop(def, submitter);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await loop.RunAsync(cts.Token);

        // At 1-second intervals and a ~1.5-second window we expect 1-2 fires.
        loop.CompletedIterations.Should().BeInRange(1, 2);
    }

    [Fact]
    public async Task RunAsync_TransientFailure_DoesNotAdvanceCounter()
    {
        var trigger = new IntervalTrigger { EverySeconds = FastInterval, MaxIterations = 2 };
        var def = MakeDefinition(trigger);

        // First call transient, rest submitted.
        var submitter = new SequenceSubmitter(
            PersonaSubmissionOutcome.TransientFailure,
            PersonaSubmissionOutcome.Submitted,
            PersonaSubmissionOutcome.Submitted);
        var loop = MakeLoop(def, submitter);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await loop.RunAsync(cts.Token);

        loop.CompletedIterations.Should().Be(2);
        submitter.Calls.Should().Be(3);

        // Counter resets via decrement on transient, so iteration 1 appears twice.
        submitter.CounterValues.Should().BeEquivalentTo(new[] { 1, 1, 2 }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task RunAsync_ThreeConsecutiveHardFailures_ExitsLoop()
    {
        var trigger = new IntervalTrigger { EverySeconds = FastInterval, MaxIterations = 100 };
        var def = MakeDefinition(trigger);
        var submitter = new CountingSubmitter(PersonaSubmissionOutcome.HardFailure);
        var loop = MakeLoop(def, submitter);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await loop.RunAsync(cts.Token);

        loop.CompletedIterations.Should().Be(0);
        submitter.Calls.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsLoopImmediately()
    {
        var trigger = new IntervalTrigger { EverySeconds = 60, MaxIterations = 100 };
        var def = MakeDefinition(trigger);
        var submitter = new CountingSubmitter(PersonaSubmissionOutcome.Submitted);
        var loop = MakeLoop(def, submitter);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await loop.RunAsync(cts.Token);

        // First fire happens at t=0, then we wait 60s — cancellation hits before the next fire.
        loop.CompletedIterations.Should().Be(1);
    }

    private sealed class CountingSubmitter : IPersonaSubmitter
    {
        private readonly PersonaSubmissionOutcome _outcome;
        public int Calls { get; private set; }

        public CountingSubmitter(PersonaSubmissionOutcome outcome) { _outcome = outcome; }

        public Task<PersonaSubmissionResult> SubmitAsync(
            PersonaDefinition persona, JsonObject resolvedPayload, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new PersonaSubmissionResult(_outcome, 1));
        }
    }

    private sealed class SequenceSubmitter : IPersonaSubmitter
    {
        private readonly Queue<PersonaSubmissionOutcome> _outcomes;
        public int Calls { get; private set; }
        public List<int> CounterValues { get; } = new();

        public SequenceSubmitter(params PersonaSubmissionOutcome[] outcomes)
        {
            _outcomes = new Queue<PersonaSubmissionOutcome>(outcomes);
        }

        public Task<PersonaSubmissionResult> SubmitAsync(
            PersonaDefinition persona, JsonObject resolvedPayload, CancellationToken ct)
        {
            Calls++;
            CounterValues.Add(resolvedPayload["n"]!.GetValue<int>());
            var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : PersonaSubmissionOutcome.Submitted;
            return Task.FromResult(new PersonaSubmissionResult(outcome, 1));
        }
    }

    private sealed class StubRandom : IRandomSource
    {
        public int NextInt(int min, int max) => min;
        public decimal NextDecimal(decimal min, decimal max, int p) => min;
        public T Choose<T>(IReadOnlyList<T> options) => options[0];
    }
}
