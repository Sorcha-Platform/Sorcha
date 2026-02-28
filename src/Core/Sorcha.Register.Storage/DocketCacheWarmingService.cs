// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Register.Models;
using Sorcha.Storage.Abstractions.Caching;

namespace Sorcha.Register.Storage;

/// <summary>
/// Hosted service that warms the docket cache on startup.
/// </summary>
public class DocketCacheWarmingService : IHostedService
{
    private readonly IVerifiedCache<Docket, ulong>? _docketCache;
    private readonly RegisterStorageConfiguration _configuration;
    private readonly ILogger<DocketCacheWarmingService> _logger;
    private Task? _backgroundWarmingTask;
    private CancellationTokenSource? _backgroundCts;

    /// <summary>
    /// Initializes a new instance of the DocketCacheWarmingService.
    /// </summary>
    public DocketCacheWarmingService(
        IVerifiedCache<Docket, ulong>? docketCache,
        IOptions<RegisterStorageConfiguration> options,
        ILogger<DocketCacheWarmingService> logger)
    {
        _docketCache = docketCache;
        _configuration = options?.Value ?? new RegisterStorageConfiguration();
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.EnableCacheWarming)
        {
            _logger.LogInformation("Cache warming is disabled, skipping");
            return;
        }

        if (_docketCache is null)
        {
            _logger.LogWarning("Docket cache is not available, skipping cache warming");
            return;
        }

        var cacheConfig = _configuration.DocketCacheConfiguration;
        _logger.LogInformation(
            "Starting docket cache warming with strategy: {Strategy}",
            cacheConfig.StartupStrategy);

        try
        {
            var currentSequence = await _docketCache.GetCurrentSequenceAsync(cancellationToken);

            if (currentSequence == 0)
            {
                _logger.LogInformation("No dockets in WORM store, skipping cache warming");
                return;
            }

            var targetSequence = cacheConfig.StartupStrategy == CacheStartupStrategy.Blocking
                ? Math.Min(currentSequence, (ulong)cacheConfig.BlockingThreshold)
                : currentSequence;

            var progress = new Progress<CacheWarmingProgress>(p =>
            {
                if (p.DocumentsLoaded % 100 == 0 || p.PercentComplete >= 99)
                {
                    _logger.LogInformation(
                        "Docket cache warming progress: {Loaded}/{Total} ({Percent:F1}%) - Elapsed: {Elapsed}",
                        p.DocumentsLoaded, p.TotalDocuments, p.PercentComplete, p.Elapsed);
                }
            });

            if (cacheConfig.StartupStrategy == CacheStartupStrategy.Blocking)
            {
                // Block until cache is warmed
                await _docketCache.WarmCacheAsync(targetSequence, progress, cancellationToken);
                _logger.LogInformation(
                    "Blocking docket cache warming completed for {Count} dockets",
                    targetSequence);
            }
            else
            {
                // Start progressive warming in background with linked cancellation
                _backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var bgToken = _backgroundCts.Token;

                _backgroundWarmingTask = Task.Run(async () =>
                {
                    try
                    {
                        await _docketCache.WarmCacheAsync(targetSequence, progress, bgToken);
                        _logger.LogInformation(
                            "Progressive docket cache warming completed for {Count} dockets",
                            targetSequence);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Progressive docket cache warming was cancelled");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Progressive docket cache warming failed");
                    }
                }, bgToken);

                _logger.LogInformation(
                    "Progressive docket cache warming started for {Count} dockets",
                    targetSequence);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Docket cache warming failed during startup");

            if (cacheConfig.StartupStrategy == CacheStartupStrategy.Blocking)
            {
                throw; // Re-throw for blocking strategy
            }
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Docket cache warming service stopping");

        // Cancel the background warming task if running
        if (_backgroundCts is not null)
        {
            await _backgroundCts.CancelAsync();

            if (_backgroundWarmingTask is not null)
            {
                try
                {
                    // Wait for the background task to complete, but respect the stop timeout
                    await _backgroundWarmingTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected — either our cancellation or the host's stop timeout
                }
            }

            _backgroundCts.Dispose();
        }
    }
}
