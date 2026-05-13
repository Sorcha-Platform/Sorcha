// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;

namespace Sorcha.Validator.Service.Diagnostics;

/// <summary>
/// Periodically flushes the <see cref="RuleTelemetry"/> snapshot to disk when
/// <see cref="BenchmarkSettings.FlushIntervalSeconds"/> &gt; 0. Always present
/// in the host, but no-ops when telemetry is disabled or no flush path is set.
/// </summary>
public sealed class TelemetryFlusherService : BackgroundService
{
    private readonly BenchmarkSettings _settings;
    private readonly ILogger<TelemetryFlusherService> _logger;

    public TelemetryFlusherService(IOptions<BenchmarkSettings> settings, ILogger<TelemetryFlusherService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || _settings.FlushIntervalSeconds <= 0 || string.IsNullOrWhiteSpace(_settings.FlushPath))
        {
            return;
        }

        Directory.CreateDirectory(_settings.FlushPath);
        _logger.LogInformation(
            "Telemetry flusher running every {Seconds}s → {Path}",
            _settings.FlushIntervalSeconds, _settings.FlushPath);

        var period = TimeSpan.FromSeconds(_settings.FlushIntervalSeconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(period, stoppingToken);
                await FlushOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown — emit a final snapshot
            await FlushOnceAsync(CancellationToken.None);
        }
    }

    private async Task FlushOnceAsync(CancellationToken ct)
    {
        try
        {
            var json = RuleTelemetry.SnapshotJson();
            var ts = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            var path = Path.Combine(_settings.FlushPath!, $"validator-telemetry-{ts}.json");
            await File.WriteAllTextAsync(path, json, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telemetry flush failed");
        }
    }
}
