// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Background service that keeps citizen-device status lists fresh (Feature 114, T080).
/// On every tick (default 1h), scans for any list whose <c>ExpiresAt</c> falls inside
/// the look-ahead window and re-signs it via <see cref="ICitizenStatusListPublisher.RegenerateAsync"/>.
/// </summary>
/// <remarks>
/// Without this worker, a tenant whose citizens never revoke a device would still see
/// their signed list go stale at the 24h mark — verifiers would refuse the cached JWT
/// and bombard the public endpoint trying to refresh. The publisher's on-flip + on-allocate
/// regeneration covers the eventful path; this worker covers the quiet path.
///
/// Singleton lifetime per <c>BackgroundService</c> conventions, so all scoped dependencies
/// (<see cref="WalletDbContext"/>, <see cref="ICitizenStatusListPublisher"/>,
/// <see cref="IOrgStatusSigningWalletResolver"/>) are resolved per-tick via
/// <see cref="IServiceScopeFactory"/> per CLAUDE.md singleton-DI guidance.
/// </remarks>
public sealed class CitizenStatusListPublisherService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan LookAheadWindow = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CitizenStatusListPublisherService> _logger;

    /// <summary>Initialises a new instance of the <see cref="CitizenStatusListPublisherService"/> class.</summary>
    public CitizenStatusListPublisherService(
        IServiceScopeFactory scopeFactory,
        ILogger<CitizenStatusListPublisherService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CitizenStatusListPublisherService starting — tick interval {Tick}, look-ahead {Look}",
            TickInterval, LookAheadWindow);

        // First tick fires after the interval, not immediately, to give the rest of
        // the host a moment to come up cleanly.
        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "CitizenStatusListPublisherService tick failed — will retry next interval");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — swallow.
        }
    }

    /// <summary>
    /// Single regeneration pass — exposed internal for direct invocation in tests.
    /// </summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<ICitizenStatusListPublisher>();
        var resolver = scope.ServiceProvider.GetRequiredService<IOrgStatusSigningWalletResolver>();

        var threshold = DateTimeOffset.UtcNow.Add(LookAheadWindow);

        // Pull a slim projection — we only need the org/list pair to know what to regenerate;
        // the publisher reloads the full row inside RegenerateAsync.
        var due = await db.CitizenDeviceStatusLists
            .AsNoTracking()
            .Where(l => l.ExpiresAt <= threshold)
            .Select(l => new { l.OrganizationId, l.ListId })
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            _logger.LogDebug("CitizenStatusListPublisherService tick — no lists due for regeneration");
            return;
        }

        _logger.LogInformation(
            "CitizenStatusListPublisherService tick — {Count} list(s) due for regeneration",
            due.Count);

        // Cache org → signing wallet to avoid re-resolving for every list of the same org.
        var signerByOrg = new Dictionary<Guid, string>();
        foreach (var item in due)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                if (!signerByOrg.TryGetValue(item.OrganizationId, out var signingWallet))
                {
                    signingWallet = await resolver.ResolveAsync(item.OrganizationId, ct);
                    signerByOrg[item.OrganizationId] = signingWallet;
                }

                await publisher.RegenerateAsync(item.OrganizationId, item.ListId, signingWallet, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Failed to regenerate citizen status list org={OrgId} listId={ListId} — will retry next tick",
                    item.OrganizationId, item.ListId);
            }
        }
    }
}
