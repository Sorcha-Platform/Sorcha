// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.McpServer.Services;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// Spec 139 US5 (T038) — <see cref="ToolAuditService.RecordOutcome"/> records the privacy-safe
/// per-invocation outcome and drives the <c>Sorcha.Mcp</c> metrics. Verified via a real
/// <see cref="McpMetrics"/> + <see cref="MeterListener"/> (the audit log itself is fire-and-forget
/// structured logging with no token material).
/// <para>In the <c>Sorcha.Mcp metrics</c> collection so it never runs in parallel with another
/// class listening on the same (name-shared) meter — that would let measurements cross over.</para>
/// </summary>
[Collection("Sorcha.Mcp metrics")]
public sealed class ToolAuditServiceTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ToolAuditService _audit;
    private readonly List<(string Instrument, Dictionary<string, object?> Tags)> _measurements = new();
    private readonly MeterListener _listener;

    public ToolAuditServiceTests()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var metrics = new McpMetrics(_provider.GetRequiredService<IMeterFactory>());
        _audit = new ToolAuditService(NullLogger<ToolAuditService>.Instance, metrics);

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == McpMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            _measurements.Add((instrument.Name, TagsToDict(tags))));
        _listener.Start();
    }

    [Fact]
    public void RecordOutcome_EmitsToolInvokedMetric_TaggedByTierToolOutcome()
    {
        _audit.RecordOutcome(new ToolOutcomeRecord
        {
            CallerTier = Tier.Consumer,
            ToolName = "sorcha_inbox_list",
            Outcome = "Success",
            Duration = TimeSpan.FromMilliseconds(5),
        });

        var counter = _measurements.Should()
            .ContainSingle(m => m.Instrument == "sorcha_mcp_tool_invoked_total").Subject;
        counter.Tags.Should().Contain("tool", "sorcha_inbox_list");
        counter.Tags.Should().Contain("tier", "consumer");
        counter.Tags.Should().Contain("outcome", "Success");
    }

    [Fact]
    public void RecordOutcome_WithBackendStatus_EmitsBackendCallMetric()
    {
        _audit.RecordOutcome(new ToolOutcomeRecord
        {
            CallerTier = Tier.Platform,
            ToolName = "sorcha_register_stats",
            Outcome = "Unavailable",
            BackendStatus = "Unavailable",
            Duration = TimeSpan.FromMilliseconds(12),
        });

        _measurements.Should().Contain(m => m.Instrument == "sorcha_mcp_backend_call_total");
    }

    [Fact]
    public void RecordOutcome_NoBackendStatus_DoesNotEmitBackendCallMetric()
    {
        _audit.RecordOutcome(new ToolOutcomeRecord
        {
            CallerTier = Tier.Platform,
            ToolName = "sorcha_jsonlogic_test",
            Outcome = "Success",
            Duration = TimeSpan.FromMilliseconds(1),
        });

        _measurements.Should().NotContain(m => m.Instrument == "sorcha_mcp_backend_call_total");
    }

    private static Dictionary<string, object?> TagsToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            dict[tag.Key] = tag.Value;
        }
        return dict;
    }

    public void Dispose()
    {
        _listener.Dispose();
        _provider.Dispose();
    }
}
