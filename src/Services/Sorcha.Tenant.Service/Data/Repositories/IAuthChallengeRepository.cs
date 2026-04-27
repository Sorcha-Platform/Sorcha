// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Data.Repositories;

/// <summary>
/// Persistence boundary for <see cref="AuthChallengeToken"/>. Atomic-consume
/// semantics in <see cref="TryConsumeAsync"/> are load-bearing for the
/// re-authentication challenge primitive (Feature 116).
/// </summary>
public interface IAuthChallengeRepository
{
    /// <summary>Persist a freshly-issued challenge token.</summary>
    Task InsertAsync(AuthChallengeToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Look up a challenge token by the SHA-256 hash of the raw header value.
    /// Returns null if not found. The raw token is never persisted.
    /// </summary>
    Task<AuthChallengeToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic consume: marks the token as consumed iff it has not been
    /// consumed before. Returns true on the unique winning consume, false
    /// when the token was already consumed (replay) or has been deleted.
    /// Implemented as <c>UPDATE … SET consumed_at = now() WHERE id = X
    /// AND consumed_at IS NULL</c> so it remains correct under concurrent
    /// presentation of the same token from two requests.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid tokenId, DateTimeOffset consumedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete tokens whose <see cref="AuthChallengeToken.ExpiresAt"/> is older
    /// than <paramref name="olderThan"/>. Returns the number of rows deleted.
    /// Called from <see cref="Services.AuthChallengeTokenCleanupService"/> on
    /// a daily tick with a 7-day retention window.
    /// </summary>
    Task<int> PruneExpiredOlderThanAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}
