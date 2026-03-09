// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Background service that purges audit log entries older than each organization's
/// configured retention period. Runs daily at 2 AM UTC.
/// </summary>
public class AuditCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditCleanupService> _logger;

    /// <summary>
    /// Interval between cleanup runs. Defaults to 24 hours.
    /// Internal for testing.
    /// </summary>
    internal TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(24);

    public AuditCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until 2 AM UTC on first run
        var now = DateTimeOffset.UtcNow;
        var nextRun = now.Date.AddHours(2);
        if (nextRun <= now)
            nextRun = nextRun.AddDays(1);

        var initialDelay = nextRun - now;
        _logger.LogInformation(
            "Audit cleanup service starting. First run in {Delay:hh\\:mm\\:ss}",
            initialDelay);

        try
        {
            await Task.Delay(initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredEntriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audit log cleanup");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Purges audit entries older than each organization's retention period.
    /// Internal for testing.
    /// </summary>
    internal async Task PurgeExpiredEntriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        // Get all organizations with their retention settings
        var organizations = await dbContext.Organizations
            .Select(o => new { o.Id, o.AuditRetentionMonths })
            .ToListAsync(cancellationToken);

        var totalPurged = 0;

        foreach (var org in organizations)
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow.AddMonths(-org.AuditRetentionMonths);

                var expiredEntries = await dbContext.AuditLogEntries
                    .Where(a => a.OrganizationId == org.Id && a.Timestamp < cutoff)
                    .ToListAsync(cancellationToken);

                if (expiredEntries.Count > 0)
                {
                    dbContext.AuditLogEntries.RemoveRange(expiredEntries);
                    await dbContext.SaveChangesAsync(cancellationToken);

                    totalPurged += expiredEntries.Count;
                    _logger.LogInformation(
                        "Purged {Count} expired audit entries for org {OrgId} (retention: {Months}m)",
                        expiredEntries.Count, org.Id, org.AuditRetentionMonths);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to purge audit entries for org {OrgId}", org.Id);
            }
        }

        if (totalPurged > 0)
        {
            _logger.LogInformation("Audit cleanup complete: {Total} entries purged across {OrgCount} organizations",
                totalPurged, organizations.Count);
        }
    }
}
