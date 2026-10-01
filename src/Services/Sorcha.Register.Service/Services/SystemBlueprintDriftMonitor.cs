// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>Options for the system blueprint lifecycle, bound from <c>SystemBlueprints</c>.</summary>
public sealed class SystemBlueprintOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SystemBlueprints";

    /// <summary>Minutes between drift recomputations. Values below 1 are treated as 1.</summary>
    public int DriftIntervalMinutes { get; set; } = 10;
}

/// <summary>The latest drift report held by <see cref="SystemBlueprintDriftMonitor"/>.</summary>
public interface ISystemBlueprintDriftSnapshot
{
    /// <summary>Entries from the most recent successful compute; null until the first one completes.</summary>
    IReadOnlyList<SystemBlueprintDriftEntry>? Entries { get; }
}

/// <summary>
/// Computes system blueprint drift once the system register exists, then every
/// <see cref="SystemBlueprintOptions.DriftIntervalMinutes"/>; logs a warning per drifted entry and
/// publishes the <c>sorcha_system_blueprint_drift</c> gauge (Feature 197, #1466).
/// </summary>
public sealed class SystemBlueprintDriftMonitor : BackgroundService, ISystemBlueprintDriftSnapshot
{
    private static readonly TimeSpan BootstrapPollInterval = TimeSpan.FromSeconds(5);

    private readonly ISystemBlueprintDriftReporter _reporter;
    private readonly ISystemRegisterBootstrapStatus _bootstrap;
    private readonly IOptions<SystemBlueprintOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<SystemBlueprintDriftMonitor> _logger;
    private readonly Meter _meter;
    private volatile IReadOnlyList<SystemBlueprintDriftEntry>? _entries;

    /// <summary>Creates the monitor.</summary>
    public SystemBlueprintDriftMonitor(
        ISystemBlueprintDriftReporter reporter,
        ISystemRegisterBootstrapStatus bootstrap,
        IOptions<SystemBlueprintOptions> options,
        TimeProvider time,
        ILogger<SystemBlueprintDriftMonitor> logger)
    {
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _meter = new Meter(SystemBlueprintMetrics.MeterName);
        _meter.CreateObservableGauge(
            SystemBlueprintMetrics.DriftGaugeName,
            ObserveDrift,
            description: "1 for each system blueprint's current drift state against the image");
    }

    /// <inheritdoc />
    public IReadOnlyList<SystemBlueprintDriftEntry>? Entries => _entries;

    private IEnumerable<Measurement<int>> ObserveDrift()
    {
        var entries = _entries;
        if (entries is null)
            yield break;

        foreach (var e in entries)
        {
            yield return new Measurement<int>(
                1,
                new KeyValuePair<string, object?>("blueprint", e.BlueprintId),
                new KeyValuePair<string, object?>("state", StateName(e.State)));
        }
    }

    private static string StateName(SystemBlueprintDriftState state)
        => JsonNamingPolicy.CamelCase.ConvertName(state.ToString());

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!_bootstrap.IsCompleted)
                await Task.Delay(BootstrapPollInterval, _time, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken);
                var minutes = Math.Max(1, _options.Value.DriftIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(minutes), _time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // host shutdown
        }
    }

    /// <summary>Runs one compute cycle: updates the snapshot and logs a warning per drifted entry.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SystemBlueprintDriftEntry> entries;
        try
        {
            entries = await _reporter.ComputeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never keep a previous (possibly InSync) report: an unreadable register is Unknown.
            _logger.LogError(ex, "System blueprint drift computation failed; reporting every blueprint as unknown");
            var checkedAt = _time.GetUtcNow();
            entries = SystemBlueprintCatalog.Ids
                .Select(id => new SystemBlueprintDriftEntry(
                    id, SystemBlueprintDriftState.Unknown, null, null, null, null, checkedAt))
                .ToList();
        }

        _entries = entries;

        foreach (var e in entries)
        {
            if (e.State == SystemBlueprintDriftState.InSync)
                continue;

            _logger.LogWarning(
                "System blueprint {BlueprintId} drift: {DriftState} (register version {CurrentVersion}, " +
                "register publication {CurrentPublicationTxId}, image publication {ImagePublicationTxId}, " +
                "image matches version {ImageMatchesVersion})",
                e.BlueprintId, e.State, e.CurrentVersion, e.CurrentPublicationTxId,
                e.ImagePublicationTxId, e.ImageMatchesVersion);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}
