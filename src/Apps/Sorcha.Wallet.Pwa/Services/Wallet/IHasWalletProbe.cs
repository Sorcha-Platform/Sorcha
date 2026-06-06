// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services.Wallet;

/// <summary>
/// One-shot "does the signed-in citizen have a wallet?" signal (Feature 149).
/// Drives the wallet-aware branch of the F128 pairing takeover: a walletless
/// citizen is routed to web wallet creation instead of dead-ending at the
/// device-enrol 404. Calls <c>GET /api/v1/wallet/exists</c>.
/// </summary>
/// <remarks>
/// Deliberately has no <c>Changed</c> / <c>EnsureLoadedAsync</c> / <c>Refresh</c>
/// contract (unlike <c>IHasPairedDeviceProbe</c>, where devices can be revoked).
/// "Walletless" is a terminal cold-start state: creating a wallet is essentially
/// the first thing a citizen does, and once a wallet exists it can never drop
/// back to zero. The signal transitions <c>false → true</c> exactly once, so a
/// single check is sufficient and correct.
/// </remarks>
public interface IHasWalletProbe
{
    /// <summary>
    /// Resolves whether the signed-in citizen has a wallet. On a transient
    /// failure (network error, timeout, non-success status, empty body) this
    /// returns <c>true</c> (fail-safe) so the takeover falls through to the
    /// existing pair flow — never falsely routing a wallet owner to create a
    /// second wallet.
    /// </summary>
    Task<bool> HasWalletAsync(CancellationToken ct = default);
}
