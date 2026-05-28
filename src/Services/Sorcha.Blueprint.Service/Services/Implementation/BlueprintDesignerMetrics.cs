// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// OpenTelemetry metrics for the Feature 142 Blueprint Design Lifecycle (designer
/// workspace, rehearsal harness, and governed Go-live). Instruments are registered
/// with the <c>Sorcha.Blueprint.Designer</c> meter so they surface via the standard
/// exporter configured in ServiceDefaults (the meter name is added to the export
/// allowlist in <c>Sorcha.ServiceDefaults.Extensions</c>).
/// </summary>
/// <remarks>
/// Four instruments are exposed (T058):
/// <list type="bullet">
/// <item><c>rehearsal_run_total{mode,outcome}</c> — counter, lifetime events.</item>
/// <item><c>rehearsal_duration_seconds{outcome}</c> — histogram, run wall-clock seconds.</item>
/// <item><c>publish_override_total{register_id,reason_provided}</c> — counter, soft-gate overrides.</item>
/// <item><c>sandbox_provision_total{outcome,org_id}</c> — counter, sandbox-register provisioning attempts.</item>
/// </list>
/// Callers use the typed helper methods (<see cref="RecordRehearsalStart"/>,
/// <see cref="RecordRehearsalTerminal"/>, <see cref="RecordPublishOverride"/>,
/// <see cref="RecordSandboxProvision"/>) rather than constructing <see cref="TagList"/> at
/// every call site.
/// </remarks>
public sealed class BlueprintDesignerMetrics
{
    /// <summary>Meter name used when publishing Blueprint-designer-lifecycle metrics.</summary>
    public const string MeterName = "Sorcha.Blueprint.Designer";

    private readonly Meter _meter;
    private readonly Counter<long> _rehearsalRunTotal;
    private readonly Histogram<double> _rehearsalDurationSeconds;
    private readonly Counter<long> _publishOverrideTotal;
    private readonly Counter<long> _sandboxProvisionTotal;

    /// <summary>Initialises a new instance of the <see cref="BlueprintDesignerMetrics"/> class.</summary>
    public BlueprintDesignerMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName, "1.0.0");

        _rehearsalRunTotal = _meter.CreateCounter<long>(
            name: "rehearsal_run_total",
            unit: null,
            description: "Total rehearsal runs by mode and terminal outcome.");

        _rehearsalDurationSeconds = _meter.CreateHistogram<double>(
            name: "rehearsal_duration_seconds",
            unit: "s",
            description: "Wall-clock duration of rehearsal runs from start to terminal outcome.");

        _publishOverrideTotal = _meter.CreateCounter<long>(
            name: "publish_override_total",
            unit: null,
            description: "Publish soft-gate overrides recorded against the audit table.");

        _sandboxProvisionTotal = _meter.CreateCounter<long>(
            name: "sandbox_provision_total",
            unit: null,
            description: "Sandbox-register provisioning attempts by outcome and organisation.");
    }

    /// <summary>Counts a rehearsal start. <paramref name="mode"/> is <c>dryRun</c> or <c>full</c>.</summary>
    public void RecordRehearsalStart(RehearsalMode mode)
    {
        _rehearsalRunTotal.Add(1,
            new KeyValuePair<string, object?>("mode", ModeTag(mode)),
            new KeyValuePair<string, object?>("outcome", "InProgress"));
    }

    /// <summary>Counts a rehearsal terminal outcome and records its duration.</summary>
    public void RecordRehearsalTerminal(RehearsalMode mode, RehearsalOutcome outcome, double durationSeconds)
    {
        var outcomeTag = outcome.ToString();
        _rehearsalRunTotal.Add(1,
            new KeyValuePair<string, object?>("mode", ModeTag(mode)),
            new KeyValuePair<string, object?>("outcome", outcomeTag));
        _rehearsalDurationSeconds.Record(durationSeconds,
            new KeyValuePair<string, object?>("outcome", outcomeTag));
    }

    /// <summary>Counts a publish-override audit-row write.</summary>
    public void RecordPublishOverride(string registerId, bool reasonProvided)
    {
        _publishOverrideTotal.Add(1,
            new KeyValuePair<string, object?>("register_id", registerId ?? "—"),
            new KeyValuePair<string, object?>("reason_provided", reasonProvided));
    }

    /// <summary>
    /// Counts a sandbox-register provisioning attempt. <paramref name="outcome"/> is one of
    /// <c>Created</c>, <c>Reused</c>, or <c>Failed</c>.
    /// </summary>
    public void RecordSandboxProvision(string outcome, string? organizationId)
    {
        _sandboxProvisionTotal.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("org_id", organizationId ?? "—"));
    }

    private static string ModeTag(RehearsalMode mode) => mode switch
    {
        RehearsalMode.DryRun => "dryRun",
        RehearsalMode.Full => "full",
        _ => mode.ToString(),
    };
}
