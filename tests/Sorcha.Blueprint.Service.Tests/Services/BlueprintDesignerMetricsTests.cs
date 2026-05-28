// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 142 T058 — asserts the <c>Sorcha.Blueprint.Designer</c> meter instruments record. Uses a
/// raw <see cref="MeterListener"/> over a real <see cref="IMeterFactory"/> obtained from a service
/// collection (the same way the host resolves <see cref="BlueprintDesignerMetrics"/>).
/// Single-collection so concurrent listeners don't cross over.
/// </summary>
[Collection("Sorcha.Blueprint.Designer metrics")]
public sealed class BlueprintDesignerMetricsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly BlueprintDesignerMetrics _metrics;
    private readonly List<(string Instrument, long Value, Dictionary<string, object?> Tags)> _longs = new();
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _doubles = new();
    private readonly MeterListener _listener;

    public BlueprintDesignerMetricsTests()
    {
        _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _metrics = new BlueprintDesignerMetrics(_provider.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BlueprintDesignerMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
            _longs.Add((inst.Name, value, TagsToDict(tags))));
        _listener.SetMeasurementEventCallback<double>((inst, value, tags, _) =>
            _doubles.Add((inst.Name, value, TagsToDict(tags))));
        _listener.Start();
    }

    [Fact]
    public void RecordRehearsalStart_WritesCounter_WithModeAndInProgressOutcome()
    {
        _metrics.RecordRehearsalStart(RehearsalMode.Full);

        var m = _longs.Should().ContainSingle(x => x.Instrument == "rehearsal_run_total").Subject;
        m.Value.Should().Be(1);
        m.Tags.Should().Contain("mode", "full");
        m.Tags.Should().Contain("outcome", "InProgress");
    }

    [Fact]
    public void RecordRehearsalTerminal_WritesCounterAndHistogram_WithOutcomeTag()
    {
        _metrics.RecordRehearsalTerminal(RehearsalMode.Full, RehearsalOutcome.Passed, 12.5);

        _longs.Should().ContainSingle(x => x.Instrument == "rehearsal_run_total"
            && (string?)x.Tags["outcome"] == "Passed");
        var hist = _doubles.Should().ContainSingle(x => x.Instrument == "rehearsal_duration_seconds").Subject;
        hist.Value.Should().Be(12.5);
        hist.Tags.Should().Contain("outcome", "Passed");
    }

    [Fact]
    public void RecordPublishOverride_WritesCounter_WithRegisterIdAndReasonProvided()
    {
        _metrics.RecordPublishOverride("reg-xyz", reasonProvided: true);

        var m = _longs.Should().ContainSingle(x => x.Instrument == "publish_override_total").Subject;
        m.Value.Should().Be(1);
        m.Tags.Should().Contain("register_id", "reg-xyz");
        m.Tags.Should().Contain("reason_provided", true);
    }

    [Fact]
    public void RecordSandboxProvision_WritesCounter_WithOutcomeAndOrgTags()
    {
        _metrics.RecordSandboxProvision("Created", "org-1");

        var m = _longs.Should().ContainSingle(x => x.Instrument == "sandbox_provision_total").Subject;
        m.Tags.Should().Contain("outcome", "Created");
        m.Tags.Should().Contain("org_id", "org-1");
    }

    [Fact]
    public void Metrics_ResolvedViaDependencyInjection_AsSingleton()
    {
        // Spot-check: the host registers BlueprintDesignerMetrics as a singleton, so a service-
        // provider built with AddMetrics() + AddSingleton<BlueprintDesignerMetrics>() must resolve.
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton<BlueprintDesignerMetrics>();
        using var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<BlueprintDesignerMetrics>();
        resolved.Should().NotBeNull();
        // Same instance on second resolve (singleton).
        sp.GetRequiredService<BlueprintDesignerMetrics>().Should().BeSameAs(resolved);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _provider.Dispose();
    }

    private static Dictionary<string, object?> TagsToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kvp in tags)
        {
            dict[kvp.Key] = kvp.Value;
        }
        return dict;
    }
}
