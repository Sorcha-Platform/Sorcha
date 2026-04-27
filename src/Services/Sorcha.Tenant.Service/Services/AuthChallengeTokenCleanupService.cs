// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Data.Repositories;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Background service that prunes expired <see cref="Models.AuthChallengeToken"/>
/// rows on a daily tick. Retains 7 days of history for forensics, then deletes.
/// Single-process safe; multi-instance safe (concurrent prune is harmless —
/// the second writer simply finds nothing to delete).
/// </summary>
public sealed class AuthChallengeTokenCleanupService : BackgroundService
{
    /// <summary>Default retention for consumed/expired tokens.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthChallengeTokenCleanupService> _logger;

    /// <summary>Tick interval; internal for tests.</summary>
    internal TimeSpan TickInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Retention window; internal for tests.</summary>
    internal TimeSpan Retention { get; set; } = DefaultRetention;

    /// <summary>Creates a new <see cref="AuthChallengeTokenCleanupService"/>.</summary>
    public AuthChallengeTokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthChallengeTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AuthChallengeToken cleanup service starting interval={Interval} retention={Retention}",
            TickInterval, Retention);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await RunOnceAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "AuthChallengeToken cleanup deleted {Count} expired tokens", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuthChallengeToken cleanup failed");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Single prune sweep. Internal so unit tests can drive a deterministic
    /// run without spinning the background loop.
    /// </summary>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAuthChallengeRepository>();

        var cutoff = DateTimeOffset.UtcNow - Retention;
        return await repository.PruneExpiredOlderThanAsync(cutoff, cancellationToken);
    }
}
