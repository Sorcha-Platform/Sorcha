// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Sorcha.ServiceDefaults.Hubs;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Hubs;

public class SignalRMetricsTests
{
    [Fact]
    public void MeterName_IsStable()
    {
        SignalRMetrics.MeterName.Should().Be("Sorcha.SignalR");
    }

    [Fact]
    public void ConnectionsTotal_IsRegisteredWithExpectedName()
    {
        using var metrics = new SignalRMetrics();
        metrics.ConnectionsTotal.Name.Should().Be("sorcha_signalr_connections_total");
        metrics.MessagesSentTotal.Name.Should().Be("sorcha_signalr_messages_sent_total");
        metrics.ReconnectsTotal.Name.Should().Be("sorcha_signalr_reconnects_total");
        metrics.BackplaneState.Name.Should().Be("sorcha_signalr_backplane_state");
        metrics.EventsHubSubscribers.Name.Should().Be("sorcha_signalr_events_hub_subscribers");
    }

    [Fact]
    public void EventsHubSubscribers_TracksIncrementsAndDecrements()
    {
        using var metrics = new SignalRMetrics();
        using var listener = new MeterListener();
        long observed = -1;
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SignalRMetrics.MeterName &&
                instrument.Name == "sorcha_signalr_events_hub_subscribers")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => observed = value);
        listener.Start();

        metrics.IncrementEventsHubSubscribers();
        metrics.IncrementEventsHubSubscribers();
        metrics.IncrementEventsHubSubscribers();
        metrics.DecrementEventsHubSubscribers();

        listener.RecordObservableInstruments();
        observed.Should().Be(2);
    }

    [Fact]
    public void BackplaneState_RecordsPerServiceState()
    {
        using var metrics = new SignalRMetrics();
        using var listener = new MeterListener();
        var observed = new Dictionary<string, int>(StringComparer.Ordinal);
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SignalRMetrics.MeterName &&
                instrument.Name == "sorcha_signalr_backplane_state")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "service" && tag.Value is string svc)
                {
                    observed[svc] = value;
                }
            }
        });
        listener.Start();

        metrics.RecordBackplaneState("tenant", BackplaneStateValues.Up);
        metrics.RecordBackplaneState("blueprint", BackplaneStateValues.Degraded);
        metrics.RecordBackplaneState("wallet", BackplaneStateValues.Down);

        listener.RecordObservableInstruments();

        observed.Should().Contain(new KeyValuePair<string, int>("tenant", BackplaneStateValues.Up));
        observed.Should().Contain(new KeyValuePair<string, int>("blueprint", BackplaneStateValues.Degraded));
        observed.Should().Contain(new KeyValuePair<string, int>("wallet", BackplaneStateValues.Down));
    }

    [Fact]
    public void RecordBackplaneState_RejectsNullOrWhitespaceServiceName()
    {
        using var metrics = new SignalRMetrics();
        var act = () => metrics.RecordBackplaneState("  ", BackplaneStateValues.Up);
        act.Should().Throw<ArgumentException>();
    }
}
