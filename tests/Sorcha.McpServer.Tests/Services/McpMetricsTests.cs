// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.McpServer.Services;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// Spec 139 US5 (T039) — asserts the <c>Sorcha.Mcp</c> meter instruments actually record. Uses a
/// raw <see cref="MeterListener"/> (no extra test dependency) over a real <see cref="IMeterFactory"/>
/// obtained from a service collection — the same way the host resolves <see cref="McpMetrics"/>.
/// <para>In the <c>Sorcha.Mcp metrics</c> collection so it never runs in parallel with another
/// class listening on the same (name-shared) meter — that would let measurements cross over.</para>
/// </summary>
[Collection("Sorcha.Mcp metrics")]
public sealed class McpMetricsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly McpMetrics _metrics;
    private readonly List<(string Instrument, long Value, Dictionary<string, object?> Tags)> _longMeasurements = new();
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _doubleMeasurements = new();
    private readonly MeterListener _listener;

    public McpMetricsTests()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _metrics = new McpMetrics(_provider.GetRequiredService<IMeterFactory>());

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

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _longMeasurements.Add((instrument.Name, value, TagsToDict(tags))));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            _doubleMeasurements.Add((instrument.Name, value, TagsToDict(tags))));

        _listener.Start();
    }

    [Fact]
    public void ToolInvoked_RecordsCounterAndHistogram_WithToolTierOutcomeTags()
    {
        _metrics.ToolInvoked("sorcha_register_stats", Tier.Platform, "Healthy", 42.5);

        var counter = _longMeasurements.Should()
            .ContainSingle(m => m.Instrument == "sorcha_mcp_tool_invoked_total").Subject;
        counter.Value.Should().Be(1);
        counter.Tags.Should().Contain("tool", "sorcha_register_stats");
        counter.Tags.Should().Contain("tier", "platform");
        counter.Tags.Should().Contain("outcome", "Healthy");

        var histogram = _doubleMeasurements.Should()
            .ContainSingle(m => m.Instrument == "sorcha_mcp_tool_duration_ms").Subject;
        histogram.Value.Should().Be(42.5);
        histogram.Tags.Should().Contain("tool", "sorcha_register_stats");
    }

    [Fact]
    public void ToolInvoked_NullTier_RecordsUnknownTierTag()
    {
        _metrics.ToolInvoked("sorcha_inbox_list", tier: null, outcome: "Success", durationMs: 1);

        var counter = _longMeasurements.Should()
            .ContainSingle(m => m.Instrument == "sorcha_mcp_tool_invoked_total").Subject;
        counter.Tags.Should().Contain("tier", "unknown");
    }

    [Fact]
    public void BackendCall_RecordsCounter_WithServiceAndStatusTags()
    {
        _metrics.BackendCall("gateway", "Unavailable");

        var counter = _longMeasurements.Should()
            .ContainSingle(m => m.Instrument == "sorcha_mcp_backend_call_total").Subject;
        counter.Value.Should().Be(1);
        counter.Tags.Should().Contain("service", "gateway");
        counter.Tags.Should().Contain("status", "Unavailable");
    }

    [Fact]
    public void BackendCall_BlankService_IsIgnored()
    {
        _metrics.BackendCall("", "Success");

        _longMeasurements.Should().NotContain(m => m.Instrument == "sorcha_mcp_backend_call_total");
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
