// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Background service that deletes expired activity events daily.
/// Events expire after 90 days (set on CreatedAt + 90 days).
/// </summary>
public class EventCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    public EventCleanupService(IServiceProvider serviceProvider, ILogger<EventCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first cleanup to let the app start up
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredEventsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during activity event cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupExpiredEventsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var deleted = await db.ActivityEvents
            .Where(e => e.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired activity events", deleted);
        }
    }
}
