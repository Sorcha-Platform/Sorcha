// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// EF Core-backed implementation of <see cref="IHolderAddressLookup"/>.
/// </summary>
/// <remarks>
/// Read path is a primary-key lookup against <c>CitizenHolderIndex</c>; the
/// expected query pattern is one read per inbound-credential detection event,
/// so the table stays small and an in-memory cache layer would add complexity
/// without measurable gain. Add Redis caching in a follow-up only if profiling
/// shows the read becomes a hot path.
/// <para>
/// Write path is idempotent on the <c>WalletAddress</c> primary key — citizens
/// who re-enrol or rotate devices hit the same row. A unique-violation on
/// concurrent first-time enrolments is treated as a benign race (the existing
/// row is correct).
/// </para>
/// </remarks>
public sealed class EfCoreHolderAddressLookup : IHolderAddressLookup
{
    private readonly WalletDbContext _db;
    private readonly ILogger<EfCoreHolderAddressLookup> _logger;

    /// <summary>Initialises a new instance.</summary>
    public EfCoreHolderAddressLookup(WalletDbContext db, ILogger<EfCoreHolderAddressLookup> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolvePlatformUserIdAsync(string walletAddress, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var row = await _db.CitizenHolderIndex
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.WalletAddress == walletAddress, ct);

        return row?.PlatformUserId;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(string walletAddress, Guid platformUserId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var existing = await _db.CitizenHolderIndex
            .FirstOrDefaultAsync(e => e.WalletAddress == walletAddress, ct);

        if (existing is not null)
        {
            if (existing.PlatformUserId != platformUserId)
            {
                _logger.LogWarning(
                    "CitizenHolderIndex conflict for wallet {Address}: existing PlatformUserId={Existing} != requested {Requested}. " +
                    "Existing row left unchanged.",
                    walletAddress, existing.PlatformUserId, platformUserId);
            }
            return;
        }

        _db.CitizenHolderIndex.Add(new CitizenHolderIndex
        {
            WalletAddress = walletAddress,
            PlatformUserId = platformUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "CitizenHolderIndex registered: wallet={Address} platformUserId={PlatformUserId}",
                walletAddress, platformUserId);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Concurrent first-time enrolment of the same citizen — the other writer won.
            // The existing row is correct (same WalletAddress → same PlatformUserId).
            _logger.LogDebug(
                "CitizenHolderIndex.RegisterAsync race for {Address} — existing row prevailed",
                walletAddress);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // PostgreSQL: 23505 unique_violation. Match on either the SQLSTATE or the
        // generic "duplicate key" string — provider-specific exception types are
        // not always available where this check runs.
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("23505", StringComparison.Ordinal)
            || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase);
    }
}
