// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

public class OnceTriggerLoopTests
{
    private static PersonaDefinition MakeDefinition() => new()
    {
        Name = "test",
        Target = new PersonaTarget { BlueprintId = "bp-1", InstanceId = "inst-1", ActionIndex = 0 },
        Trigger = new OnceTrigger { DelaySeconds = 0 },
        PayloadTemplate = JsonNode.Parse("""{ "value": "${counter}" }""")!
    };

    private static OnceTriggerLoop MakeLoop(PersonaDefinition def, IPersonaSubmitter submitter) =>
        new(def, (OnceTrigger)def.Trigger, submitter, new PayloadTokenResolver(),
            new StubRandom(), TimeProvider.System, NullLogger<OnceTriggerLoop>.Instance);

    [Fact]
    public async Task RunAsync_Submitted_CompletesOneIteration()
    {
        var submitter = new CapturingSubmitter(PersonaSubmissionOutcome.Submitted);
        var loop = MakeLoop(MakeDefinition(), submitter);

        await loop.RunAsync(CancellationToken.None);

        loop.CompletedIterations.Should().Be(1);
        submitter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_HardFailure_DoesNotIncrementCompleted()
    {
        var submitter = new CapturingSubmitter(PersonaSubmissionOutcome.HardFailure);
        var loop = MakeLoop(MakeDefinition(), submitter);

        await loop.RunAsync(CancellationToken.None);

        loop.CompletedIterations.Should().Be(0);
        submitter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_TransientFailure_DoesNotRetryForOnceTrigger()
    {
        var submitter = new CapturingSubmitter(PersonaSubmissionOutcome.TransientFailure);
        var loop = MakeLoop(MakeDefinition(), submitter);

        await loop.RunAsync(CancellationToken.None);

        submitter.Calls.Should().Be(1);
        loop.CompletedIterations.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeFire_ThrowsOperationCanceled()
    {
        var def = MakeDefinition() with { Trigger = new OnceTrigger { DelaySeconds = 60 } };
        var submitter = new CapturingSubmitter(PersonaSubmissionOutcome.Submitted);
        var loop = new OnceTriggerLoop(def, (OnceTrigger)def.Trigger, submitter,
            new PayloadTokenResolver(), new StubRandom(), TimeProvider.System,
            NullLogger<OnceTriggerLoop>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var act = async () => await loop.RunAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        submitter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_ResolvesPayloadWithCounterOne()
    {
        var submitter = new CapturingSubmitter(PersonaSubmissionOutcome.Submitted);
        var loop = MakeLoop(MakeDefinition(), submitter);

        await loop.RunAsync(CancellationToken.None);

        submitter.LastPayload.Should().NotBeNull();
        submitter.LastPayload!["value"]!.GetValue<int>().Should().Be(1);
    }

    private sealed class CapturingSubmitter : IPersonaSubmitter
    {
        private readonly PersonaSubmissionOutcome _outcome;
        public int Calls { get; private set; }
        public JsonObject? LastPayload { get; private set; }

        public CapturingSubmitter(PersonaSubmissionOutcome outcome) { _outcome = outcome; }

        public Task<PersonaSubmissionResult> SubmitAsync(
            PersonaDefinition persona, JsonObject resolvedPayload, CancellationToken ct)
        {
            Calls++;
            LastPayload = resolvedPayload;
            return Task.FromResult(new PersonaSubmissionResult(_outcome, 10));
        }
    }

    private sealed class StubRandom : IRandomSource
    {
        public int NextInt(int min, int max) => min;
        public decimal NextDecimal(decimal min, decimal max, int p) => min;
        public T Choose<T>(IReadOnlyList<T> options) => options[0];
    }
}
