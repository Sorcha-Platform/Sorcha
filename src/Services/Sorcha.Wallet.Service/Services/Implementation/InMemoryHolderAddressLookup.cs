// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// In-memory implementation of <see cref="IHolderAddressLookup"/> for hosts running without a
/// Postgres connection string (development, CI, and the integration-test host).
/// </summary>
/// <remarks>
/// Pattern #13 supports an in-memory storage path outside Production, but
/// <see cref="EfCoreHolderAddressLookup"/> was the interface's only implementation and it was
/// registered unconditionally, so it could not be activated without a <c>WalletDbContext</c>. Every
/// endpoint that touched the lookup — including <c>POST /api/v1/wallets</c> — returned 500, which is
/// what kept <c>Sorcha.Wallet.Service.IntegrationTests</c> at 5/33 after the host-startup fix in
/// #1339, leaving the service with no HTTP-level authorization coverage at all.
/// <para>
/// Semantics mirror the EF Core implementation exactly, because the integration suite is only
/// meaningful if the two behave the same: ordinal address matching (a Postgres <c>text</c> primary
/// key is case-sensitive), idempotent registration, and a conflicting <c>PlatformUserId</c> logged
/// as a warning with the existing entry left untouched rather than overwritten.
/// </para>
/// <para>
/// Registered as a singleton so the map survives across request scopes, and backed by a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> so concurrent first-time enrolments behave like
/// the EF Core path's unique-violation race: one writer wins and the existing entry prevails.
/// </para>
/// </remarks>
public sealed class InMemoryHolderAddressLookup : IHolderAddressLookup
{
    private readonly ConcurrentDictionary<string, Guid> _index = new(StringComparer.Ordinal);
    private readonly ILogger<InMemoryHolderAddressLookup> _logger;

    /// <summary>Initialises a new instance.</summary>
    public InMemoryHolderAddressLookup(ILogger<InMemoryHolderAddressLookup> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<Guid?> ResolvePlatformUserIdAsync(string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        return Task.FromResult(
            _index.TryGetValue(walletAddress, out var platformUserId) ? platformUserId : (Guid?)null);
    }

    /// <inheritdoc />
    public Task RegisterAsync(string walletAddress, Guid platformUserId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        // GetOrAdd is the in-memory analogue of "insert, and treat a unique violation as benign":
        // the first writer wins and the existing entry is never overwritten.
        var winner = _index.GetOrAdd(walletAddress, platformUserId);

        if (winner != platformUserId)
        {
            _logger.LogWarning(
                "CitizenHolderIndex conflict for wallet {Address}: existing PlatformUserId={Existing} != requested {Requested}. " +
                "Existing entry left unchanged.",
                walletAddress, winner, platformUserId);
        }
        else
        {
            _logger.LogInformation(
                "CitizenHolderIndex registered (in-memory): wallet={Address} platformUserId={PlatformUserId}",
                walletAddress, platformUserId);
        }

        return Task.CompletedTask;
    }
}
