// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Background service that periodically removes orphaned file-chunk metadata records
/// from the action store.
/// </summary>
/// <remarks>
/// An "orphan chunk" is a <c>FileMetadata</c> record whose parent action transaction was
/// never confirmed on the ledger — for example because the upload was interrupted before
/// the transaction was signed and submitted, or the transaction was rejected by the
/// validator.  Without cleanup these records accumulate indefinitely, consuming storage
/// and potentially leaking sensitive filenames or content types.
///
/// On each timer tick the service:
/// <list type="number">
///   <item>Creates a new DI scope so it can resolve scoped or singleton stores safely.</item>
///   <item>Queries the action store for orphaned file metadata older than the configured
///         <see cref="OrphanChunkCleanupOptions.OrphanAgeThreshold"/>.</item>
///   <item>Deletes each orphaned record individually, logging the outcome.</item>
/// </list>
///
/// The default interval is 5 minutes and the default age threshold is 30 minutes.
/// Both values are configurable via <see cref="OrphanChunkCleanupOptions"/> in
/// <c>appsettings.json</c> under the key <c>"OrphanChunkCleanup"</c>.
/// </remarks>
public sealed class OrphanChunkCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrphanChunkCleanupOptions _options;
    private readonly ILogger<OrphanChunkCleanupService> _logger;

    /// <summary>
    /// Initialises a new instance of the <see cref="OrphanChunkCleanupService"/> class.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create DI scopes per cleanup cycle.</param>
    /// <param name="options">Cleanup configuration (interval and age threshold).</param>
    /// <param name="logger">Structured logger.</param>
    public OrphanChunkCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<OrphanChunkCleanupOptions> options,
        ILogger<OrphanChunkCleanupService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OrphanChunkCleanupService started (interval: {Interval}, age threshold: {AgeThreshold})",
            _options.CleanupInterval, _options.OrphanAgeThreshold);

        // Initial delay so the service does not run immediately on startup during
        // hot-reload or rapid restart scenarios.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        using var timer = new PeriodicTimer(_options.CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — break out of the loop.
                break;
            }
            catch (Exception ex)
            {
                // Never let a transient error crash the background service.
                _logger.LogError(ex, "Unhandled error during orphan chunk cleanup cycle");
            }

            // Wait for the next tick (or cancellation).
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OrphanChunkCleanupService stopped");
    }

    /// <summary>
    /// Executes a single cleanup cycle: queries for orphans and deletes them.
    /// </summary>
    internal async Task RunCleanupCycleAsync(CancellationToken ct)
    {
        var threshold = DateTimeOffset.UtcNow - _options.OrphanAgeThreshold;

        _logger.LogDebug(
            "Starting orphan chunk cleanup cycle (threshold: {Threshold:u})", threshold);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var actionStore = scope.ServiceProvider.GetRequiredService<IActionStore>();

        IReadOnlyList<(string FileId, string TransactionHash)> orphans;
        try
        {
            orphans = await actionStore.GetOrphanedFileMetadataAsync(threshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to query orphaned file metadata — skipping cleanup cycle");
            return;
        }

        if (orphans.Count == 0)
        {
            _logger.LogDebug("Orphan chunk cleanup: no orphans found");
            return;
        }

        _logger.LogInformation(
            "Orphan chunk cleanup: found {Count} orphaned file record(s) older than {Threshold:u}",
            orphans.Count, threshold);

        var deleted = 0;
        var failed = 0;

        foreach (var (fileId, txHash) in orphans)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Orphan chunk cleanup interrupted by cancellation after {Deleted} deletion(s)",
                    deleted);
                break;
            }

            try
            {
                await actionStore.DeleteFileMetadataAsync(fileId, txHash);
                deleted++;

                _logger.LogDebug(
                    "Deleted orphan file {FileId} (transaction hash: {TransactionHash})",
                    fileId, txHash);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "Failed to delete orphan file {FileId} (transaction hash: {TransactionHash})",
                    fileId, txHash);
            }
        }

        _logger.LogInformation(
            "Orphan chunk cleanup cycle complete: {Deleted} deleted, {Failed} failed",
            deleted, failed);
    }
}
