// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceDefaults.Storage;

namespace Sorcha.ServiceDefaults.Tests.Storage;

/// <summary>
/// Tests for the OpenTelemetry observable gauges emitted by
/// <see cref="StorageRegistrationMetrics"/>. Uses <see cref="MeterListener"/>
/// to capture observations without standing up the full OTel SDK.
/// </summary>
public sealed class StorageRegistrationMetricsTests : IDisposable
{
    private const string AuditedInterface = "Sorcha.Wallet.Core.Repositories.IWalletRepository";
    private const string CacheInterface = "Sorcha.Blueprint.Service.Storage.IBlueprintStore";

    /// <summary>
    /// Captured measurement: instrument name + value + tag dictionary.
    /// </summary>
    private sealed record Measurement(string Name, long Value, Dictionary<string, object?> Tags);

    private readonly ServiceProvider _sp;
    private readonly IMeterFactory _meterFactory;
    private readonly StorageRegistrationLog _log;
    private readonly StorageRegistrationMetrics _metrics;

    public StorageRegistrationMetricsTests()
    {
        // Each test gets its own ServiceProvider + IMeterFactory + StorageRegistrationMetrics.
        // The IMeterFactory becomes the Meter.Scope for instruments created from it, so
        // RecordObservations can filter to only this test's meter via reference equality
        // and ignore any leftover meters from other test instances or framework infrastructure.
        _sp = new ServiceCollection()
            .AddMetrics()
            .BuildServiceProvider();

        _meterFactory = _sp.GetRequiredService<IMeterFactory>();
        _log = new StorageRegistrationLog();
        _metrics = new StorageRegistrationMetrics(
            _meterFactory,
            _log,
            new FakeHostEnvironment { ApplicationName = "Sorcha.Test.Service" });
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _sp.Dispose();
    }

    private List<Measurement> RecordObservations()
    {
        var captured = new List<Measurement>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                // Filter to instruments on this test's IMeterFactory-scoped meter.
                // ReferenceEquals against _meterFactory isolates per-test.
                if (instrument.Meter.Name == StorageRegistrationMetrics.MeterName
                    && ReferenceEquals(instrument.Meter.Scope, _meterFactory))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < tags.Length; i++)
            {
                dict[tags[i].Key] = tags[i].Value;
            }
            captured.Add(new Measurement(instrument.Name, value, dict));
        });

        listener.Start();
        listener.RecordObservableInstruments();

        return captured;
    }

    [Fact]
    public void ProviderInfo_OnlyEmittedForAuditedInterfaces()
    {
        _log.RegisterPersistent(AuditedInterface, "EfCoreWalletRepository", "postgres");
        _log.RegisterInMemory(CacheInterface, "InMemoryBlueprintStore", "rebuilds on cold start");

        var observations = RecordObservations();

        var infoObservations = observations.Where(o => o.Name == "sorcha_storage_provider_info").ToArray();
        infoObservations.Should().HaveCount(1);
        infoObservations[0].Tags["interface"].Should().Be(AuditedInterface);
    }

    [Fact]
    public void ProviderInfo_TagsIncludeServiceImplementationBackend()
    {
        _log.RegisterPersistent(AuditedInterface, "EfCoreWalletRepository", "postgres");

        var observations = RecordObservations();

        var info = observations.Single(o => o.Name == "sorcha_storage_provider_info");
        info.Value.Should().Be(1);
        info.Tags.Should().ContainKey("service");
        info.Tags.Should().ContainKey("interface").WhoseValue.Should().Be(AuditedInterface);
        info.Tags.Should().ContainKey("implementation").WhoseValue.Should().Be("EfCoreWalletRepository");
        info.Tags.Should().ContainKey("backend").WhoseValue.Should().Be("postgres");
    }

    [Fact]
    public void FallbackActive_IsZero_ForPersistent()
    {
        _log.RegisterPersistent(AuditedInterface, "EfCoreWalletRepository", "postgres");

        var observations = RecordObservations();

        var fallback = observations.Single(o => o.Name == "sorcha_storage_fallback_active");
        fallback.Value.Should().Be(0);
        fallback.Tags["interface"].Should().Be(AuditedInterface);
    }

    [Fact]
    public void FallbackActive_IsOne_ForAuditedInMemory()
    {
        _log.RegisterInMemory(AuditedInterface, "InMemoryWalletRepository", "no postgres");

        var observations = RecordObservations();

        var fallback = observations.Single(o => o.Name == "sorcha_storage_fallback_active");
        fallback.Value.Should().Be(1);
    }

    [Fact]
    public void FallbackActive_NotEmitted_ForCacheStores()
    {
        // Cache-style stores are not on the audited list — they shouldn't generate fallback observations.
        _log.RegisterInMemory(CacheInterface, "InMemoryBlueprintStore", "rebuilds on cold start");

        var observations = RecordObservations();

        observations.Where(o => o.Name == "sorcha_storage_fallback_active").Should().BeEmpty();
    }

    [Fact]
    public void Observations_ReflectFullRegistrationLog()
    {
        _log.RegisterPersistent(
            "Sorcha.Wallet.Core.Repositories.IWalletRepository",
            "EfCoreWalletRepository",
            "postgres");
        _log.RegisterInMemory(
            "Sorcha.Blueprint.Service.Storage.IInstanceStore",
            "InMemoryInstanceStore",
            "no postgres");
        _log.RegisterInMemory(
            CacheInterface,
            "InMemoryBlueprintStore",
            "rebuilds on cold start");

        var observations = RecordObservations();

        // Only audited interfaces produce observations — 2 of 3 records are audited.
        observations.Where(o => o.Name == "sorcha_storage_provider_info").Should().HaveCount(2);
        observations.Where(o => o.Name == "sorcha_storage_fallback_active").Should().HaveCount(2);

        // Among the fallback observations: one persistent (=0), one in-memory (=1).
        var fallbackObservations = observations
            .Where(o => o.Name == "sorcha_storage_fallback_active")
            .ToArray();
        fallbackObservations.Sum(o => o.Value).Should().Be(1L);
    }

    [Fact]
    public void EmptyLog_ProducesNoObservations()
    {
        var observations = RecordObservations();

        observations.Should().BeEmpty();
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Sorcha.Test.Service";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
