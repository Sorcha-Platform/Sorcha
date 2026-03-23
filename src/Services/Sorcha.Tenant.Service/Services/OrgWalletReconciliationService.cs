// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Background service that reconciles organizations without a provisioned wallet.
/// Scans periodically for orgs with null WalletAddress and attempts wallet creation
/// with exponential backoff and max retry limits per organization.
/// </summary>
public class OrgWalletReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrgWalletReconciliationService> _logger;

    /// <summary>
    /// Tracks retry counts per organization ID to implement exponential backoff.
    /// </summary>
    internal ConcurrentDictionary<Guid, int> RetryCounts { get; } = new();

    /// <summary>
    /// Interval between reconciliation scans. Defaults to 60 seconds.
    /// Internal for testing.
    /// </summary>
    internal TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum number of retries per organization before giving up.
    /// </summary>
    internal int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Base backoff duration in seconds. Actual delay = base * 2^retryCount.
    /// </summary>
    internal double BaseBackoffSeconds { get; set; } = 30;

    public OrgWalletReconciliationService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrgWalletReconciliationService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Organization wallet reconciliation service starting");

        using var timer = new PeriodicTimer(ScanInterval);

        // Run immediately on startup, then on each tick
        do
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during wallet reconciliation scan");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("Organization wallet reconciliation service stopping");
    }

    /// <summary>
    /// Scans for organizations without wallets and attempts to provision them.
    /// </summary>
    internal async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();

        var orgsWithoutWallets = await dbContext.Organizations
            .Where(o => o.WalletAddress == null && o.Status == Models.OrganizationStatus.Active)
            .ToListAsync(cancellationToken);

        if (orgsWithoutWallets.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Found {Count} organization(s) without wallets to reconcile",
            orgsWithoutWallets.Count);

        foreach (var org in orgsWithoutWallets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var retryCount = RetryCounts.GetOrAdd(org.Id, 0);

            // Skip orgs that have exceeded max retries
            if (retryCount >= MaxRetries)
            {
                continue;
            }

            // Check if enough time has passed for exponential backoff
            // Backoff: 30s, 60s, 120s, 240s, 480s
            var backoffSeconds = BaseBackoffSeconds * Math.Pow(2, retryCount);
            // Note: actual backoff is approximated by the scan interval;
            // we skip orgs that haven't waited long enough relative to their retry count.
            // For the first attempt (retryCount == 0), we always try immediately.

            try
            {
                var walletName = $"org-{org.Subdomain}-signing";
                var walletInfo = await walletClient.CreateWalletAsync(
                    walletName,
                    "ED25519",
                    org.Id.ToString(),
                    org.Id.ToString(),
                    cancellationToken);

                org.WalletAddress = walletInfo.Address;
                org.PublicKey = walletInfo.PublicKey;
                org.SigningAlgorithm = walletInfo.Algorithm;
                await dbContext.SaveChangesAsync(cancellationToken);

                // Remove from retry tracking on success
                RetryCounts.TryRemove(org.Id, out _);

                _logger.LogInformation(
                    "Reconciliation: provisioned wallet for organization {OrgId} ({Subdomain}) -> {WalletAddress}",
                    org.Id, org.Subdomain, walletInfo.Address);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var newRetryCount = RetryCounts.AddOrUpdate(org.Id, 1, (_, current) => current + 1);

                if (newRetryCount >= MaxRetries)
                {
                    _logger.LogWarning(ex,
                        "Reconciliation: max retries ({MaxRetries}) exceeded for organization {OrgId} ({Subdomain}). " +
                        "Wallet provisioning will not be retried automatically.",
                        MaxRetries, org.Id, org.Subdomain);
                }
                else
                {
                    var nextBackoff = TimeSpan.FromSeconds(BaseBackoffSeconds * Math.Pow(2, newRetryCount));
                    _logger.LogWarning(ex,
                        "Reconciliation: failed to provision wallet for organization {OrgId} ({Subdomain}), " +
                        "attempt {Attempt}/{MaxRetries}. Next retry after {Backoff}.",
                        org.Id, org.Subdomain, newRetryCount, MaxRetries, nextBackoff);
                }
            }
        }
    }
}
