// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Issue #787 Gap A — <see cref="DocketBuildTriggerService.UnmonitorAfterGenesisFailureAsync"/> must make
/// the orphaning of pending transactions on a genesis-failure un-monitor observable (LogCritical + metric)
/// rather than silent, while ALWAYS releasing the register from monitoring even if the pool count query
/// fails. The metric is asserted deterministically via a raw <see cref="MeterListener"/> over the real
/// <see cref="ValidatorMempoolMetrics"/> meter.
/// </summary>
[Collection("ValidatorMempoolMetrics unregistered counter")]
public sealed class DocketBuildTriggerGenesisUnmonitorTests : IDisposable
{
    private const string RegisterId = "reg-genesis-787";

    private readonly Mock<IRegisterMonitoringRegistry> _registry = new(MockBehavior.Strict);
    private readonly Mock<ITransactionPoolPoller> _poller = new();
    private readonly Mock<ILogger<DocketBuildTriggerService>> _logger = new();
    private readonly ServiceProvider _meterProvider;
    private readonly ValidatorMempoolMetrics _metrics;
    private readonly List<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = new();
    private readonly MeterListener _listener;

    public DocketBuildTriggerGenesisUnmonitorTests()
    {
        _meterProvider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        _metrics = new ValidatorMempoolMetrics(_meterProvider.GetRequiredService<IMeterFactory>());

        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ValidatorMempoolMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>((inst, value, tags, _) =>
            _measurements.Add((inst.Name, value, TagsToDict(tags))));
        _listener.Start();

        _registry.Setup(r => r.UnregisterFromMonitoring(It.IsAny<string>()));
    }

    private DocketBuildTriggerService CreateSut() => new(
        Mock.Of<IServiceScopeFactory>(),
        _registry.Object,
        Options.Create(new DocketBuildConfiguration()),
        Options.Create(new ValidatorConfiguration { SystemWalletAddress = "test-addr" }),
        Mock.Of<ISystemWalletProvider>(),
        _metrics,
        _logger.Object);

    /// <summary>Real scope whose provider resolves the mocked <see cref="ITransactionPoolPoller"/>.</summary>
    private IServiceScope CreatePollerScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_poller.Object);
        var provider = services.BuildServiceProvider();
        return provider.CreateScope();
    }

    private const string UnregisteredMetric = "sorcha_validator_monitoring_unregistered_with_pending_total";

    [Fact]
    public async Task UnmonitorAfterGenesisFailure_PendingGreaterThanZero_Unregisters_LogsCritical_IncrementsMetric()
    {
        _poller.Setup(p => p.GetUnverifiedCountAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var sut = CreateSut();
        using var scope = CreatePollerScope();

        await sut.UnmonitorAfterGenesisFailureAsync(scope, RegisterId, retryCount: 3, error: "boom", CancellationToken.None);

        _registry.Verify(r => r.UnregisterFromMonitoring(RegisterId), Times.Once);

        var metric = _measurements.Should().ContainSingle(m => m.Instrument == UnregisteredMetric).Subject;
        metric.Value.Should().Be(1);
        metric.Tags.Should().Contain("reason", "genesis-failure");
        metric.Tags.Should().Contain("register_id", RegisterId);
        metric.Tags.Should().Contain("pending_count", 7L);

        VerifyLog(LogLevel.Critical, Times.AtLeastOnce());
    }

    [Fact]
    public async Task UnmonitorAfterGenesisFailure_PendingZero_Unregisters_NoMetric_NoCritical()
    {
        _poller.Setup(p => p.GetUnverifiedCountAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var sut = CreateSut();
        using var scope = CreatePollerScope();

        await sut.UnmonitorAfterGenesisFailureAsync(scope, RegisterId, retryCount: 3, error: "boom", CancellationToken.None);

        _registry.Verify(r => r.UnregisterFromMonitoring(RegisterId), Times.Once);
        _measurements.Should().NotContain(m => m.Instrument == UnregisteredMetric);
        VerifyLog(LogLevel.Critical, Times.Never());
    }

    [Fact]
    public async Task UnmonitorAfterGenesisFailure_CountQueryThrows_StillUnregisters_NoMetric_NoThrow()
    {
        _poller.Setup(p => p.GetUnverifiedCountAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var sut = CreateSut();
        using var scope = CreatePollerScope();

        var act = async () => await sut.UnmonitorAfterGenesisFailureAsync(
            scope, RegisterId, retryCount: 3, error: "boom", CancellationToken.None);

        await act.Should().NotThrowAsync();
        _registry.Verify(r => r.UnregisterFromMonitoring(RegisterId), Times.Once);
        _measurements.Should().NotContain(m => m.Instrument == UnregisteredMetric);
    }

    private void VerifyLog(LogLevel level, Times times) =>
        _logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    private static Dictionary<string, object?> TagsToDict(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var kvp in tags)
        {
            dict[kvp.Key] = kvp.Value;
        }
        return dict;
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meterProvider.Dispose();
    }
}
