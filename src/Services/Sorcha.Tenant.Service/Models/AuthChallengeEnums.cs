// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// How the user proved possession when satisfying a re-authentication challenge.
/// Selected per the ladder in <see cref="Services.IAuthChallengeService"/>:
/// TOTP (when 2FA enrolled) → password (when set) → passkey step-up → re-OAuth.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChallengeMethod
{
    /// <summary>Time-based one-time code from the user's authenticator app.</summary>
    Totp = 0,

    /// <summary>Current account password.</summary>
    Password = 1,

    /// <summary>WebAuthn assertion against an existing active passkey.</summary>
    Passkey = 2,

    /// <summary>OAuth round-trip on a still-linked social provider.</summary>
    ReOAuth = 3
}

/// <summary>
/// Operation that a re-authentication challenge token authorises.
/// Tokens are scoped — a token issued for one operation cannot be replayed
/// against another. Enforced by <see cref="Filters.RequireAuthChallengeAttribute"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScopedOperation
{
    /// <summary>Unlink a social provider or revoke an active passkey.</summary>
    RemoveAuthMethod = 0,

    /// <summary>Rotate the existing account password.</summary>
    ChangePassword = 1,

    /// <summary>Set an initial password (when one or more other methods exist).</summary>
    SetPassword = 2,

    /// <summary>Clear the account password.</summary>
    RemovePassword = 3,

    /// <summary>Disable the user's enrolled time-based two-factor authentication.</summary>
    Disable2Fa = 4
}

/// <summary>
/// Relative strength of an authentication method or step-up proof (Feature 150).
/// The numeric values are deliberately ordinal so the floor rule can compare tiers
/// with <c>&gt;=</c>: a proof authorises a destructive/downgrade operation only when
/// its tier is greater than or equal to the target's. Computed from the method kind —
/// never persisted — by <see cref="Services.AssurancePolicy"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthAssuranceTier
{
    /// <summary>Email/SMS one-time codes, backup codes — phishable, lowest assurance.</summary>
    Basic = 1,

    /// <summary>Authenticator (TOTP), account password, re-OAuth — solid but not phishing-resistant.</summary>
    Strong = 2,

    /// <summary>Passkey (WebAuthn) — phishing-resistant, highest assurance.</summary>
    Strongest = 3
}
