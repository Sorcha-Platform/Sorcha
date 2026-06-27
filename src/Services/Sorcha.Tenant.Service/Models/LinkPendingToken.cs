// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Stateless, signed credential returned by the social callback on the LinkRequired
/// branch. Carries the social identity claims and the target account id. Not persisted —
/// integrity is protected by an HMAC-SHA256 signature (see <see cref="Services.ILinkPendingTokenService"/>).
/// </summary>
/// <param name="Provider">Social provider key, e.g. <c>google</c>, <c>github</c>.</param>
/// <param name="Subject">Provider's stable subject id for the social identity.</param>
/// <param name="SocialEmail">Verified email asserted by the provider.</param>
/// <param name="DisplayName">Display name from the social profile (may be null).</param>
/// <param name="TargetAccountId"><see cref="PlatformUser.Id"/> of the existing account the email matched.</param>
/// <param name="ExpiresAt">UTC expiry (~5 minutes after mint). Enforced server-side.</param>
/// <param name="Surface">Originating surface key (<c>wallet</c> for the citizen PWA, <c>null</c> for the web platform).
/// Used at link-confirm to issue the correct tier token (Consumer vs Platform).</param>
public record LinkPendingToken(
    string Provider,
    string Subject,
    string SocialEmail,
    string? DisplayName,
    Guid TargetAccountId,
    DateTimeOffset ExpiresAt,
    string? Surface = null);

/// <summary>
/// Outcome of a <see cref="Services.ILinkPendingTokenService.TryVerify"/> call.
/// </summary>
public enum LinkPendingTokenError
{
    /// <summary>Token is valid — no error.</summary>
    None = 0,

    /// <summary>Token signature does not match, payload is malformed, or token is absent.</summary>
    Invalid = 1,

    /// <summary>Token signature is valid but <see cref="LinkPendingToken.ExpiresAt"/> is in the past.</summary>
    Expired = 2,
}
