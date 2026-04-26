// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// PWA-side mirror of the verifier <c>IStatusListCache</c>. Used by the wallet
/// during pre-flight to refuse presentations from credentials it knows are
/// already revoked. Production impl (T062) backs IndexedDB store <c>statusLists</c>.
/// </summary>
public interface IStatusListService
{
    /// <summary>Returns true if bit <paramref name="index"/> in the list at <paramref name="uri"/> is set.</summary>
    Task<bool> IsRevokedAsync(string uri, int index, CancellationToken ct = default);
}

/// <summary>Always-active demo impl — no revocation in v1 MVP.</summary>
public sealed class NoopStatusListService : IStatusListService
{
    /// <inheritdoc />
    public Task<bool> IsRevokedAsync(string uri, int index, CancellationToken ct = default)
        => Task.FromResult(false);
}
