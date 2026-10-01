// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceDefaults;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>Feature 197: the periodic drift monitor, its snapshot, logging, gauge and readiness gate.</summary>
public class SystemBlueprintDriftMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static SystemBlueprintDriftEntry Entry(string id, SystemBlueprintDriftState state)
        => new(id, state, "cur", 2, "img", null, Now);

    private sealed class FakeReporter(params SystemBlueprintDriftEntry[] entries) : ISystemBlueprintDriftReporter
    {
        public int Calls;

        public Task<IReadOnlyList<SystemBlueprintDriftEntry>> ComputeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<IReadOnlyList<SystemBlueprintDriftEntry>>(entries);
        }
    }

    private sealed class ListLogger : ILogger<SystemBlueprintDriftMonitor>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>Every timer fires immediately, so delays complete without waiting.</summary>
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ImmediateTimer();
            if (dueTime != Timeout.InfiniteTimeSpan)
                Task.Run(() => callback(state));
            return timer;
        }

        private sealed class ImmediateTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static SystemBlueprintDriftMonitor Create(
        ISystemBlueprintDriftReporter reporter,
        ListLogger? logger = null,
        ISystemRegisterBootstrapStatus? status = null,
        TimeProvider? time = null)
    {
        var s = status ?? new SystemRegisterBootstrapStatus();
        if (status is null) s.MarkCompleted(BootstrapMode.Auto);
        return new SystemBlueprintDriftMonitor(
            reporter, s, Options.Create(new SystemBlueprintOptions()),
            time ?? new ImmediateTimeProvider(), logger ?? new ListLogger());
    }

    [Fact]
    public void MeterName_IsPinned()
    {
        SystemBlueprintMetrics.MeterName.Should().Be("Sorcha.SystemBlueprints");
        SystemBlueprintMetrics.DriftGaugeName.Should().Be("sorcha_system_blueprint_drift");
    }

    [Fact]
    public async Task RunOnceAsync_Populates_Snapshot()
    {
        using var monitor = Create(new FakeReporter(Entry("a", SystemBlueprintDriftState.InSync)));
        ((ISystemBlueprintDriftSnapshot)monitor).Entries.Should().BeNull();

        await monitor.RunOnceAsync(CancellationToken.None);

        ((ISystemBlueprintDriftSnapshot)monitor).Entries.Should().ContainSingle(e => e.BlueprintId == "a");
    }

    [Theory]
    [InlineData(SystemBlueprintDriftState.ImageBehind)]
    [InlineData(SystemBlueprintDriftState.ImageAhead)]
    [InlineData(SystemBlueprintDriftState.Missing)]
    [InlineData(SystemBlueprintDriftState.Unknown)]
    public async Task RunOnceAsync_DriftedEntry_LogsWarning(SystemBlueprintDriftState state)
    {
        var logger = new ListLogger();
        using var monitor = Create(new FakeReporter(Entry("drifted-bp", state)), logger);

        await monitor.RunOnceAsync(CancellationToken.None);

        logger.Entries.Should().ContainSingle(l => l.Level == LogLevel.Warning
            && l.Message.Contains("drifted-bp") && l.Message.Contains(state.ToString()));
    }

    [Fact]
    public async Task RunOnceAsync_InSync_LogsNoWarning()
    {
        var logger = new ListLogger();
        using var monitor = Create(new FakeReporter(Entry("a", SystemBlueprintDriftState.InSync)), logger);

        await monitor.RunOnceAsync(CancellationToken.None);

        logger.Entries.Should().NotContain(l => l.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Gauge_EmitsOnePerBlueprintWithCurrentState()
    {
        using var monitor = Create(new FakeReporter(
            Entry("a", SystemBlueprintDriftState.InSync),
            Entry("b", SystemBlueprintDriftState.ImageBehind)));
        await monitor.RunOnceAsync(CancellationToken.None);

        var seen = new List<(int Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == SystemBlueprintMetrics.MeterName
                && inst.Name == SystemBlueprintMetrics.DriftGaugeName)
                l.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<int>((inst, value, tags, _) =>
            seen.Add((value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
        listener.Start();
        listener.RecordObservableInstruments();

        seen.Should().HaveCount(2);
        seen.Should().OnlyContain(m => m.Value == 1);
        seen.Should().Contain(m => (string)m.Tags["blueprint"]! == "a" && (string)m.Tags["state"]! == "inSync");
        seen.Should().Contain(m => (string)m.Tags["blueprint"]! == "b" && (string)m.Tags["state"]! == "imageBehind");
    }

    [Fact]
    public async Task ExecuteAsync_WaitsForBootstrap_ThenComputes()
    {
        var reporter = new FakeReporter(Entry("a", SystemBlueprintDriftState.InSync));
        var status = new SystemRegisterBootstrapStatus();
        using var monitor = Create(reporter, status: status);

        await monitor.StartAsync(CancellationToken.None);
        await Task.Delay(150);
        reporter.Calls.Should().Be(0, "the system register does not exist until bootstrap completes");

        status.MarkCompleted(BootstrapMode.Auto);
        await WaitUntilAsync(() => reporter.Calls >= 2); // first compute, then recompute after the interval

        await monitor.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }
}
