// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Issue #814 — the validator wedged after long idle: a validation-cycle await hung on a stale
/// Redis/gRPC connection, the in-memory "already processing" flag was never released, and every later
/// batch skipped the register forever (silent, healthy, no sealing). These tests assert the two guards
/// in <see cref="ValidationEngineService.ProcessRegisterAsync"/>: (1) a hung cycle is bounded by
/// ValidationTimeout so it THROWS and releases the slot rather than hanging; (2) a slot that somehow
/// stays stuck (an await that ignored cancellation) is reclaimed on a later cycle instead of skipping
/// the register forever. Metrics are asserted via a raw <see cref="MeterListener"/>.
/// </summary>
[Collection("ValidatorMempoolMetrics unregistered counter")]
public sealed class ValidationEngineServiceIdleStallTests : IDisposable
{
    private const string RegisterId = "reg-idle-814";

    private readonly Mock<ITransactionPoolPoller> _poller = new();
    private readonly ServiceProvider _meterProvider;
    private readonly ValidatorMempoolMetrics _metrics;
    private readonly List<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = new();
    private readonly MeterListener _listener;

    public ValidationEngineServiceIdleStallTests()
    {
        _meterProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _metrics = new ValidatorMempoolMetrics(_meterProvider.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ValidatorMempoolMetrics.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
            _measurements.Add((inst.Name, value, TagsToDict(tags))));
        _listener.Start();
    }

    // Short timeout so the "hung cycle" test resolves fast; StuckReclaimAfter floors at 90s so the
    // reclaim test drives the clock seam instead of waiting.
    private ValidationEngineService CreateSut(TimeSpan? timeout = null) => new(
        Mock.Of<IServiceScopeFactory>(),
        _poller.Object,
        Mock.Of<IVerifiedTransactionQueue>(),
        Mock.Of<IRegisterMonitoringRegistry>(),
        Options.Create(new ValidationEngineConfiguration { ValidationTimeout = timeout ?? TimeSpan.FromMilliseconds(300) }),
        _metrics,
        NullLogger<ValidationEngineService>.Instance);

    [Fact]
    public async Task ProcessRegisterAsync_CycleHangsRespectingCancellation_TimesOut_ReleasesSlot_RecordsMetric()
    {
        // Poll hangs but honours the token → the ValidationTimeout CTS cancels it → the cycle throws,
        // is caught, and returns 0 WITHOUT hanging. Second call proves the slot was released.
        var calls = 0;
        _poller.Setup(p => p.PollTransactionsAsync(RegisterId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, int _, CancellationToken ct) =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(Timeout.Infinite, ct); // cancelled by the timeout
                return (IReadOnlyList<Transaction>)Array.Empty<Transaction>();
            });

        var sut = CreateSut(TimeSpan.FromMilliseconds(300));

        var sw = Stopwatch.StartNew();
        var result = await sut.ProcessRegisterAsync(RegisterId).WaitAsync(TimeSpan.FromSeconds(5));
        sw.Stop();

        result.Should().Be(0);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4), "the timeout must abort the hung cycle, not hang");
        _measurements.Should().ContainSingle(m => m.Instrument == "sorcha_validator_validation_cycle_timeout_total")
            .Which.Tags.Should().Contain("register_id", RegisterId);

        // Slot released → a second cycle actually re-enters the poller (not skipped).
        var before = calls;
        await sut.ProcessRegisterAsync(RegisterId).WaitAsync(TimeSpan.FromSeconds(5));
        calls.Should().BeGreaterThan(before, "the released slot must let the next cycle run, not skip");
    }

    [Fact]
    public async Task ProcessRegisterAsync_SlotStuckPastThreshold_IsReclaimed_AndProceeds()
    {
        // First cycle IGNORES cancellation and hangs forever — the slot is never released (the exact
        // #814 wedge). A later cycle, past StuckReclaimAfter, must reclaim the slot and proceed.
        var baseTime = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateSut();
        sut.Now = () => baseTime;

        var first = true;
        _poller.Setup(p => p.PollTransactionsAsync(RegisterId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (first)
                {
                    first = false;
                    return HangForeverIgnoringCancellation();
                }
                return Task.FromResult((IReadOnlyList<Transaction>)Array.Empty<Transaction>());
            });

        // Fire the wedged cycle: runs synchronously through the admission guard (adds the slot at
        // baseTime) then hangs at the poll await. Do not await it.
        _ = sut.ProcessRegisterAsync(RegisterId);
        await Task.Delay(100); // let it reach and park on the poll

        // Advance the clock past the reclaim threshold (floors at 90s) and run a fresh cycle.
        sut.Now = () => baseTime.AddSeconds(120);
        var result = await sut.ProcessRegisterAsync(RegisterId).WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().Be(0);
        _measurements.Should().ContainSingle(m => m.Instrument == "sorcha_validator_validation_slot_reclaimed_total")
            .Which.Tags.Should().Contain("register_id", RegisterId);
    }

    [Fact]
    public async Task ProcessRegisterAsync_SlotHeldWithinThreshold_IsSkipped_NotReclaimed()
    {
        // A genuinely in-flight register (slot held only briefly) must still be skipped — no double
        // processing, no spurious reclaim.
        var baseTime = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateSut();
        sut.Now = () => baseTime;

        var pollCalls = 0;
        _poller.Setup(p => p.PollTransactionsAsync(RegisterId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() => { Interlocked.Increment(ref pollCalls); return HangForeverIgnoringCancellation(); });

        _ = sut.ProcessRegisterAsync(RegisterId);
        await Task.Delay(100);

        sut.Now = () => baseTime.AddSeconds(10); // well under the 90s threshold
        var result = await sut.ProcessRegisterAsync(RegisterId).WaitAsync(TimeSpan.FromSeconds(5));

        result.Should().Be(0, "an in-flight register is skipped");
        pollCalls.Should().Be(1, "the second cycle must skip, not re-enter the poller");
        _measurements.Should().NotContain(m => m.Instrument == "sorcha_validator_validation_slot_reclaimed_total");
    }

    private static async Task<IReadOnlyList<Transaction>> HangForeverIgnoringCancellation()
    {
        await Task.Delay(Timeout.Infinite, CancellationToken.None);
        return Array.Empty<Transaction>();
    }

    private static Dictionary<string, object?> TagsToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kv in tags) dict[kv.Key] = kv.Value;
        return dict;
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meterProvider.Dispose();
    }
}
