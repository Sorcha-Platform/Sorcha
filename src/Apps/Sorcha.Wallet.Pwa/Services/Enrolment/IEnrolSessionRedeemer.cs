// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services.Enrolment;

/// <summary>
/// Discriminated error codes mirroring the Tenant Service redeem endpoint's
/// <c>RedeemEnrolSessionErrorCode</c>. PWA confirmation dialog + error
/// surfaces switch on these.
/// </summary>
public enum EnrolRedeemErrorCode
{
    MalformedToken,
    InvalidSignature,
    ScopeMismatch,
    AlreadyUsed,
    Expired,
    Network,
}

/// <summary>
/// Discriminates the intent behind a redeemed enrol-session, echoed from the
/// server. F128 — drives the redeem page's copy + post-pair destination.
/// F126 tokens (and any in-flight token at deployment time) default to
/// <see cref="Gated"/>.
/// </summary>
public enum EnrolRedeemMode
{
    Gated = 0,
    Standalone = 1,
}

/// <summary>
/// Outcome of a redeem call — success carries the citizen access token +
/// the bound user's identifying details (which feed the confirmation
/// dialog); failure carries a structured error code.
/// </summary>
public sealed record EnrolRedeemResult(
    bool IsSuccess,
    string? AccessToken,
    int ExpiresInSeconds,
    string? DisplayName,
    string? Email,
    EnrolRedeemMode Mode,
    EnrolRedeemErrorCode? ErrorCode,
    string? ErrorMessage)
{
    public static EnrolRedeemResult Ok(
        string accessToken,
        int expiresIn,
        string displayName,
        string email,
        EnrolRedeemMode mode) =>
        new(true, accessToken, expiresIn, displayName, email, mode, null, null);

    public static EnrolRedeemResult Fail(EnrolRedeemErrorCode code, string message) =>
        new(false, null, 0, null, null, EnrolRedeemMode.Gated, code, message);
}

/// <summary>
/// PWA-side client for <c>POST /api/auth/enrol-session/redeem</c>. Feature 126.
/// Anonymous — the session token IS the credential for this single call.
/// </summary>
public interface IEnrolSessionRedeemer
{
    Task<EnrolRedeemResult> RedeemAsync(string sessionToken, CancellationToken ct = default);
}
