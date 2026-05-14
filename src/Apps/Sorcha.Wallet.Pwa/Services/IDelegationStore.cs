// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Holds the wallet's current device delegation credential. Production impl (T061)
/// is a singleton row in IndexedDB store <c>delegation</c>; v1 demo is in-memory.
/// </summary>
public interface IDelegationStore
{
    /// <summary>Returns the current compact delegation JWT, or null if not yet enrolled.</summary>
    Task<string?> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Replace the current delegation (e.g. after enrolment or renewal).</summary>
    Task SetAsync(string compactJwt, CancellationToken ct = default);
}

/// <summary>Demo-grade in-memory <see cref="IDelegationStore"/>.</summary>
public sealed class InMemoryDelegationStore : IDelegationStore
{
    private string? _jwt;

    /// <inheritdoc />
    public Task<string?> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(_jwt);

    /// <inheritdoc />
    public Task SetAsync(string compactJwt, CancellationToken ct = default)
    {
        _jwt = compactJwt;
        return Task.CompletedTask;
    }
}
