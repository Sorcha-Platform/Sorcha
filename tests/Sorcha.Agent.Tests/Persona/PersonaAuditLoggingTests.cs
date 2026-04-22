// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Agent.Execution;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

/// <summary>
/// Feature 110 research.md R-007: persona fires must land in the same
/// JSONL audit stream <see cref="Execution.ActionExecutor"/> uses for
/// reactive decisions, so operators have a single greppable log that
/// captures both agent behaviours.
/// </summary>
public class PersonaAuditLoggingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _auditPath;

    public PersonaAuditLoggingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sorcha-persona-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _auditPath = Path.Combine(_tempDir, "agent-actions.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task OnceTrigger_SuccessfulFire_WritesSubmittedAuditEntry()
    {
        using var auditLogger = new AuditLogger(_auditPath);
        var host = BuildHost(
            trigger: new OnceTrigger { DelaySeconds = 0 },
            outcome: PersonaSubmissionOutcome.Submitted,
            auditLogger: auditLogger);

        await host.RunAsync(CancellationToken.None);
        auditLogger.Dispose(); // flush

        var entries = ReadAuditEntries();
        entries.Should().HaveCount(1);
        entries[0].GetProperty("decision").GetString().Should().Be("persona-fire");
        entries[0].GetProperty("actionId").GetString().Should().Be("persona:kickoff-test#1");
        entries[0].GetProperty("actionName").GetString().Should().Be("kickoff-test");
        entries[0].GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task OnceTrigger_HardFailure_WritesFailedAuditEntryWithError()
    {
        using var auditLogger = new AuditLogger(_auditPath);
        var host = BuildHost(
            trigger: new OnceTrigger { DelaySeconds = 0 },
            outcome: PersonaSubmissionOutcome.HardFailure,
            error: "400 BadRequest: schema validation failed",
            auditLogger: auditLogger);

        await host.RunAsync(CancellationToken.None);
        auditLogger.Dispose();

        var entries = ReadAuditEntries();
        entries.Should().HaveCount(1);
        entries[0].GetProperty("success").GetBoolean().Should().BeFalse();
        entries[0].GetProperty("error").GetString().Should().Contain("schema validation failed");
    }

    [Fact]
    public async Task IntervalTrigger_ThreeIterations_WritesOneEntryPerFire()
    {
        using var auditLogger = new AuditLogger(_auditPath);
        var host = BuildHost(
            trigger: new IntervalTrigger { EverySeconds = 1, MaxIterations = 3 },
            outcome: PersonaSubmissionOutcome.Submitted,
            auditLogger: auditLogger);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.RunAsync(cts.Token);
        auditLogger.Dispose();

        var entries = ReadAuditEntries();
        entries.Should().HaveCount(3, "one audit entry per successful persona fire");
        for (int i = 0; i < 3; i++)
        {
            entries[i].GetProperty("actionId").GetString()
                .Should().Be($"persona:invoice-test#{i + 1}",
                    "each fire's counter appears in the synthetic ActionId");
        }
    }

    [Fact]
    public async Task NullAuditLogger_IsTolerated_NoCrash()
    {
        // Regression: preserving the "construct PersonaHost without an AuditLogger"
        // path keeps tests and scripts ergonomic.
        var host = BuildHost(
            trigger: new OnceTrigger { DelaySeconds = 0 },
            outcome: PersonaSubmissionOutcome.Submitted,
            auditLogger: null);

        await host.RunAsync(CancellationToken.None);
        host.CompletedIterations.Should().Be(1);
    }

    private PersonaHost BuildHost(
        PersonaTrigger trigger,
        PersonaSubmissionOutcome outcome,
        AuditLogger? auditLogger,
        string? error = null)
    {
        var name = trigger is OnceTrigger ? "kickoff-test" : "invoice-test";
        var definition = new PersonaDefinition
        {
            Name = name,
            Target = new PersonaTarget { BlueprintId = "bp", InstanceId = "i", ActionIndex = 0 },
            Trigger = trigger,
            PayloadTemplate = JsonNode.Parse("""{ "n": "${counter}" }""")!
        };

        return new PersonaHost(
            definition,
            new StubSubmitter(outcome, error),
            new PayloadTokenResolver(),
            new StubRandom(),
            TimeProvider.System,
            NullLoggerFactory.Instance,
            auditLogger);
    }

    private List<JsonElement> ReadAuditEntries()
    {
        if (!File.Exists(_auditPath)) return new();
        return File.ReadAllLines(_auditPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    private sealed class StubSubmitter : IPersonaSubmitter
    {
        private readonly PersonaSubmissionOutcome _outcome;
        private readonly string? _error;

        public StubSubmitter(PersonaSubmissionOutcome outcome, string? error)
        {
            _outcome = outcome;
            _error = error;
        }

        public Task<PersonaSubmissionResult> SubmitAsync(
            PersonaDefinition persona, JsonObject payload, CancellationToken ct) =>
            Task.FromResult(new PersonaSubmissionResult(_outcome, DurationMs: 42, _error));
    }

    private sealed class StubRandom : IRandomSource
    {
        public int NextInt(int min, int max) => min;
        public decimal NextDecimal(decimal min, decimal max, int p) => min;
        public T Choose<T>(IReadOnlyList<T> options) => options[0];
    }
}
