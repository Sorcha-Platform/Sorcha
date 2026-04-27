// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAuthChallengeRepository"/>.
/// </summary>
public sealed class AuthChallengeRepository : IAuthChallengeRepository
{
    private readonly TenantDbContext _db;

    /// <summary>Creates a new <see cref="AuthChallengeRepository"/>.</summary>
    public AuthChallengeRepository(TenantDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task InsertAsync(AuthChallengeToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        _db.AuthChallengeTokens.Add(token);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuthChallengeToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenHash);
        return _db.AuthChallengeTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(Guid tokenId, DateTimeOffset consumedAt, CancellationToken cancellationToken = default)
    {
        // Single atomic UPDATE — the WHERE consumed_at IS NULL clause ensures
        // exactly one of two concurrent presentations wins. A 0-row result
        // means the token was already consumed (or deleted) and the caller
        // must reject the request as a replay.
        var rowsAffected = await _db.AuthChallengeTokens
            .Where(t => t.Id == tokenId && t.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.ConsumedAt, consumedAt),
                cancellationToken);

        return rowsAffected == 1;
    }

    /// <inheritdoc />
    public async Task<int> PruneExpiredOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        // Daily-cadence cleanup of a small N (consumed/expired auth challenge
        // tokens). Materialising the full table and filtering in memory is
        // portable across providers — SQLite test provider can't translate
        // a parameterised DateTimeOffset comparison in a Where clause, while
        // production Npgsql can. The performance hit is irrelevant at this
        // table's expected size.
        var all = await _db.AuthChallengeTokens.ToListAsync(cancellationToken);
        var stale = all.Where(t => t.ExpiresAt < olderThan).ToList();
        if (stale.Count == 0) return 0;

        _db.AuthChallengeTokens.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}
