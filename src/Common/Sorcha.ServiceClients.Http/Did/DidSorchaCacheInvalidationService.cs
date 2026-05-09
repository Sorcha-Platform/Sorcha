// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Affected wallet addresses for a single confirmed transaction —
/// the minimum surface needed to drive <see cref="DidResolverCache"/> invalidation.
/// </summary>
public readonly record struct DidCacheAffectedAddresses(
    string SenderWallet,
    IReadOnlyList<string> ToWallets);

/// <summary>
/// Adapter that yields confirmed-transaction notifications to the
/// <see cref="DidSorchaCacheInvalidationService"/>. Defined here (rather than
/// pulling <c>Sorcha.Register.Core.Events.IEventSubscriber</c> into
/// <c>Sorcha.ServiceClients.Http</c>) to keep this assembly free of register
/// business-logic dependencies. Service-level startup code provides the bridge
/// to the real Redis-stream subscriber.
/// </summary>
public interface IDidCacheTransactionEventSource
{
    /// <summary>
    /// Subscribes the supplied handler to every confirmed-transaction event for the
    /// lifetime of <paramref name="ct"/>. Implementations are expected to invoke the
    /// handler exactly once per event (best-effort) and to swallow handler exceptions
    /// after logging — DID-cache freshness is not a correctness boundary.
    /// </summary>
    Task SubscribeAsync(
        Func<DidCacheAffectedAddresses, CancellationToken, Task> handler,
        CancellationToken ct);
}

/// <summary>
/// Feature 120 T014 — invalidates <see cref="DidResolverCache"/> entries whose
/// canonical primary DID is affected by a confirmed Sorcha transaction.
/// </summary>
/// <remarks>
/// <para>
/// <c>did:sorcha:*</c> documents are derived from on-chain wallet/org state, so
/// every confirmed transaction that touches a wallet (sender or recipient) may
/// invalidate the cached resolution. Subscribing to the existing
/// <c>transaction:confirmed</c> Redis-stream channel keeps the cache fresh
/// without polling and without coupling cache freshness to a TTL guess.
/// </para>
/// <para>
/// Invalidation is keyed on the wallet address embedded in the DID
/// (<c>did:sorcha:w:{addr}</c>, <c>did:sorcha:org:{addr}</c>). Other DID
/// methods (<c>did:web</c>, <c>did:key</c>) are unaffected by Sorcha
/// transactions and rely on their own TTLs (or determinism).
/// </para>
/// </remarks>
public sealed class DidSorchaCacheInvalidationService : BackgroundService
{
    private readonly IDidCacheTransactionEventSource _source;
    private readonly DidResolverCache _cache;
    private readonly ILogger<DidSorchaCacheInvalidationService> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public DidSorchaCacheInvalidationService(
        IDidCacheTransactionEventSource source,
        DidResolverCache cache,
        ILogger<DidSorchaCacheInvalidationService> logger)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DidSorchaCacheInvalidationService starting");

        try
        {
            await _source.SubscribeAsync(
                (affected, _) =>
                {
                    InvalidateForWallet(affected.SenderWallet);
                    if (affected.ToWallets is not null)
                    {
                        foreach (var recipient in affected.ToWallets)
                        {
                            InvalidateForWallet(recipient);
                        }
                    }
                    return Task.CompletedTask;
                },
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DidSorchaCacheInvalidationService subscription failed");
        }
    }

    private void InvalidateForWallet(string? walletAddress)
    {
        if (string.IsNullOrEmpty(walletAddress)) return;

        // Both wallet- and org-flavoured DIDs share the same address suffix; invalidate both.
        _cache.Invalidate($"did:sorcha:w:{walletAddress}");
        _cache.Invalidate($"did:sorcha:org:{walletAddress}");
    }
}
