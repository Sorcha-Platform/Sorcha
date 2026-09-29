// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Per-node cache of signed IETF status list tokens, so a list is not re-signed by the Wallet Service
/// on every fetch (#1759).
/// </summary>
/// <remarks>
/// <para>
/// A token is reused only while BOTH hold: the list's content is unchanged, and less than half the
/// token's lifetime has passed. The content check is what keeps a revocation from waiting out a
/// cached signature — the moment a bit changes, the next fetch signs afresh. The half-life bound means
/// a verifier is never handed a token about to expire, and bounds how long a token signed with a key
/// that has since rotated can still be served.
/// </para>
/// <para>One entry per list, replaced on change, so the cache cannot grow with the number of revisions.</para>
/// </remarks>
public sealed class IetfStatusListTokenCache(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <summary>A cacheable identity for a list's signed content: org, width and the packed entries.</summary>
    public static string ContentKey(Guid organizationId, int bits, byte[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return $"{organizationId:N}:{bits}:{Convert.ToHexString(SHA256.HashData(entries))}";
    }

    /// <summary>
    /// Returns a still-reusable token for this list content, with the seconds it has left to live.
    /// </summary>
    public bool TryGet(string listId, string contentKey, out string jwt, out int remainingSeconds)
    {
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue(listId, out var e)
            && string.Equals(e.ContentKey, contentKey, StringComparison.Ordinal)
            && now < e.ReuseUntil)
        {
            jwt = e.Jwt;
            remainingSeconds = (int)Math.Max(0, (e.ExpiresAt - now).TotalSeconds);
            return true;
        }

        jwt = string.Empty;
        remainingSeconds = 0;
        return false;
    }

    /// <summary>Records a freshly signed token whose lifetime is <paramref name="ttlSeconds"/> from now.</summary>
    public void Put(string listId, string contentKey, string jwt, int ttlSeconds)
    {
        var now = clock.GetUtcNow();
        _entries[listId] = new Entry(
            contentKey, jwt, now.AddSeconds(ttlSeconds), now.AddSeconds(ttlSeconds / 2.0));
    }

    private sealed record Entry(string ContentKey, string Jwt, DateTimeOffset ExpiresAt, DateTimeOffset ReuseUntil);
}
