// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Issue #787 Gap A — <see cref="RegisterMonitoringBootstrap.ReleaseDerosteredRegisterAsync"/> must make
/// the release of a de-rostered register that still has pending transactions observable (LogWarning +
/// metric) rather than silent, while ALWAYS releasing it from monitoring even if the pool count query
/// fails. The metric is asserted deterministically via a raw <see cref="MeterListener"/> over the real
/// <see cref="ValidatorMempoolMetrics"/> meter.
/// </summary>
[Collection("ValidatorMempoolMetrics unregistered counter")]
public sealed class RegisterMonitoringBootstrapReleaseTests : IDisposable
{
    private const string RegisterId = "reg-roster-787";
    private const string UnregisteredMetric = "sorcha_validator_monitoring_unregistered_with_pending_total";

    private readonly Mock<IRegisterMonitoringRegistry> _registry = new(MockBehavior.Strict);
    private readonly Mock<ITransactionPoolPoller> _poller = new();
    private readonly Mock<IValidatorKeyProvider> _keyProvider = new();
    private readonly Mock<ILogger<RegisterMonitoringBootstrap>> _logger = new();
    private readonly ServiceProvider _provider;
    private readonly ValidatorMempoolMetrics _metrics;
    private readonly List<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = new();
    private readonly MeterListener _listener;

    public RegisterMonitoringBootstrapReleaseTests()
    {
        // Real scope factory whose scoped provider resolves the mocked poller — the SUT calls
        // _scopeFactory.CreateScope() internally, so we must NOT hand-mock IServiceScopeFactory.
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddSingleton(_poller.Object);
        _provider = services.BuildServiceProvider();

        _metrics = new ValidatorMempoolMetrics(_provider.GetRequiredService<IMeterFactory>());

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

    private RegisterMonitoringBootstrap CreateSut() => new(
        _provider.GetRequiredService<IServiceScopeFactory>(),
        _registry.Object,
        _keyProvider.Object,
        _metrics,
        _logger.Object);

    [Fact]
    public async Task ReleaseDerostered_PendingGreaterThanZero_Releases_LogsWarning_IncrementsMetric()
    {
        _poller.Setup(p => p.GetUnverifiedCountAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        var sut = CreateSut();

        await sut.ReleaseDerosteredRegisterAsync(RegisterId, CancellationToken.None);

        _registry.Verify(r => r.UnregisterFromMonitoring(RegisterId), Times.Once);

        var metric = _measurements.Should().ContainSingle(m => m.Instrument == UnregisteredMetric).Subject;
        metric.Value.Should().Be(1);
        metric.Tags.Should().Contain("reason", "roster-change");
        metric.Tags.Should().Contain("register_id", RegisterId);
        metric.Tags.Should().Contain("pending_count", 4L);

        VerifyLog(LogLevel.Warning, Times.AtLeastOnce());
    }

    [Fact]
    public async Task ReleaseDerostered_PendingZero_ReleasesQuietly_NoMetric()
    {
        _poller.Setup(p => p.GetUnverifiedCountAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        var sut = CreateSut();

        await sut.ReleaseDerosteredRegisterAsync(RegisterId, CancellationToken.None);

        _registry.Verify(r => r.UnregisterFromMonitoring(RegisterId), Times.Once);
        _measurements.Should().NotContain(m => m.Instrument == UnregisteredMetric);
    }

    [Fact]
    public async Task ReleaseDerostered_CountQueryThrows_StillReleases_NoMetric_NoThrow()
    {
        _poller.Setup(p => p.GetUnverifiedCountAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var sut = CreateSut();

        var act = async () => await sut.ReleaseDerosteredRegisterAsync(RegisterId, CancellationToken.None);

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
        _provider.Dispose();
    }
}
