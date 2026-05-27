// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services.Enrolment;

/// <summary>
/// Discriminated error codes mirroring the Tenant Service short-code
/// redeem endpoint. The PairingTakeover sub-affordance maps these to
/// citizen-facing copy.
/// </summary>
public enum PairingShortCodeRedeemErrorCode
{
    MalformedCode,
    ExpiredCode,
    AlreadyUsedCode,
    RateLimited,
    Network,
}

/// <summary>
/// Outcome of a short-code redeem. Success carries the underlying
/// enrol-session redeem payload (the same shape as
/// <see cref="EnrolRedeemResult"/>'s success branch); failure carries
/// a structured code + copy.
/// </summary>
public sealed record PairingShortCodeRedeemResult(
    bool IsSuccess,
    string? AccessToken,
    int ExpiresInSeconds,
    string? DisplayName,
    string? Email,
    PairingShortCodeRedeemErrorCode? ErrorCode,
    string? ErrorMessage)
{
    public static PairingShortCodeRedeemResult Ok(string accessToken, int expiresIn, string displayName, string email) =>
        new(true, accessToken, expiresIn, displayName, email, null, null);

    public static PairingShortCodeRedeemResult Fail(PairingShortCodeRedeemErrorCode code, string message) =>
        new(false, null, 0, null, null, code, message);
}

/// <summary>
/// PWA-side client for <c>POST /api/auth/enrol-session/redeem-short-code</c>
/// (Feature 128). Anonymous — the code is the credential for this single call.
/// </summary>
public interface IPairingShortCodeRedeemer
{
    Task<PairingShortCodeRedeemResult> RedeemAsync(string code, CancellationToken ct = default);
}
