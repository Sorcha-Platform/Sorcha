// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of a <see cref="IPairingShortCodeService.RedeemAsync"/> attempt.
/// On success, mirrors the underlying enrol-session redeem result (same
/// access token, same display name, etc. — short codes are a transport
/// wrapper, not a separate auth concept).
/// </summary>
public sealed record RedeemShortCodeResult(
    RedeemEnrolSessionResponse? Success,
    RedeemPairingShortCodeErrorBody? Error)
{
    public static RedeemShortCodeResult Ok(RedeemEnrolSessionResponse response) => new(response, null);

    public static RedeemShortCodeResult Fail(RedeemPairingShortCodeErrorCode code, string message) =>
        new(null, new RedeemPairingShortCodeErrorBody(code, message));

    public bool IsSuccess => Success is not null;
}

/// <summary>
/// Mints and redeems 6-digit human-typeable pairing short codes. Each code
/// wraps an underlying <see cref="EnrolSessionMode.Standalone"/>
/// enrol-session token; redeem unwraps to the standard redeem flow.
/// Feature 128 — see <c>specs/128-cold-start-onboarding/data-model.md</c>
/// §"PairingShortCode" and <c>research.md §R3</c>.
/// </summary>
public interface IPairingShortCodeService
{
    /// <summary>
    /// Mints a 6-digit numeric short code bound to
    /// <paramref name="platformUserId"/>. 5-minute TTL, single-use,
    /// rate-limited at redeem (5 attempts per code per minute).
    /// </summary>
    Task<MintPairingShortCodeResponse> MintAsync(
        Guid platformUserId,
        PairingShortCodeRoute route,
        CancellationToken ct);

    /// <summary>
    /// Validates and consumes <paramref name="code"/>. Returns the underlying
    /// enrol-session redeem result on first redeem; subsequent attempts return
    /// <see cref="RedeemPairingShortCodeErrorCode.AlreadyUsedCode"/>.
    /// </summary>
    Task<RedeemShortCodeResult> RedeemAsync(string code, CancellationToken ct);
}
