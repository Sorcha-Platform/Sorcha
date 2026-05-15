// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of an <see cref="IEnrolSessionService.RedeemAsync"/> attempt.
/// Discriminated union: either <see cref="Success"/> is non-null (HTTP 200)
/// or <see cref="Error"/> is non-null (HTTP 400/409/410).
/// </summary>
public sealed record RedeemResult(
    RedeemEnrolSessionResponse? Success,
    RedeemEnrolSessionErrorBody? Error)
{
    public static RedeemResult Ok(RedeemEnrolSessionResponse response) => new(response, null);
    public static RedeemResult Fail(RedeemEnrolSessionErrorCode code, string message) =>
        new(null, new RedeemEnrolSessionErrorBody(code, message));

    public bool IsSuccess => Success is not null;
}

/// <summary>
/// Mints and redeems one-time enrolment session tokens binding a Sorcha
/// account to an upcoming wallet device-pairing call. Feature 126.
/// </summary>
public interface IEnrolSessionService
{
    /// <summary>
    /// Mints a fresh session token bound to <paramref name="platformUserId"/>.
    /// 10-minute lifetime, single-use, scope <c>"enrol"</c>.
    /// </summary>
    Task<MintEnrolSessionResponse> MintAsync(Guid platformUserId, CancellationToken ct);

    /// <summary>
    /// Validates and consumes <paramref name="sessionToken"/>. Returns the bound
    /// user's identifying details + a freshly minted full citizen access token
    /// on first redeem; subsequent redeems return <see cref="RedeemEnrolSessionErrorCode.AlreadyUsed"/>.
    /// </summary>
    Task<RedeemResult> RedeemAsync(string sessionToken, CancellationToken ct);
}
