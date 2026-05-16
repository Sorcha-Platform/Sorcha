// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Mints and redeems Feature 128 "Email me a link" resumption tokens.
/// Each token wraps a single user identity; redeem returns the bound
/// PlatformUser so the caller can issue a fresh login token + 302 to
/// /setup/add-device.
/// </summary>
public interface IPairingResumptionTokenService
{
    /// <summary>
    /// Mints a single-use resumption token bound to <paramref name="platformUserId"/>.
    /// 24-hour TTL.
    /// </summary>
    Task<MintedResumptionToken> MintAsync(Guid platformUserId, CancellationToken ct);

    /// <summary>
    /// Validates and consumes <paramref name="token"/>. Returns the bound
    /// platform user id on first redeem; subsequent attempts return null.
    /// </summary>
    Task<Guid?> RedeemAsync(string token, CancellationToken ct);
}

/// <summary>The minted token + its absolute expiry.</summary>
public sealed record MintedResumptionToken(string Token, DateTimeOffset ExpiresAt);
