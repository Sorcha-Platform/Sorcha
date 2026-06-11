// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models;

/// <summary>
/// Proof method selected by the server's re-authentication ladder
/// (Feature 116). Wire-compatible with the Tenant Service enum of the same name.
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
    ReOAuth = 3,
}

/// <summary>
/// The sign-in method a step-up operation targets (Feature 150). Wire-compatible with the Tenant
/// Service <c>AuthMethodKind</c>. Sent on challenge initiate/verify so the server can compute the
/// floor's required proof tier for the ambiguous <see cref="ScopedOperation.RemoveAuthMethod"/>
/// (passkey-revoke vs social-unlink). A null target fails safe to the strongest tier server-side.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthMethodKind
{
    /// <summary>The account password.</summary>
    Password = 0,

    /// <summary>A linked social provider.</summary>
    Social = 1,

    /// <summary>An active passkey.</summary>
    Passkey = 2,
}

/// <summary>
/// Operation that a re-authentication challenge token authorises (Feature 116).
/// Tokens are scoped — a token issued for one operation cannot be replayed
/// against another.
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
    Disable2Fa = 4,
}

/// <summary>
/// Successful response from <c>POST /api/auth/challenge/initiate</c>. The
/// dialog renders the proof input matching <see cref="Method"/>.
/// </summary>
public sealed record ChallengeInitiateResult(ChallengeMethod Method, JsonElement? Payload);

/// <summary>
/// Outcome of <c>POST /api/auth/challenge/verify</c>. On <see cref="Succeeded"/>
/// the caller receives the opaque <see cref="Token"/> for the subsequent
/// mutation call (presented via the <c>X-Auth-Challenge</c> header).
/// </summary>
public sealed record ChallengeVerifyResult(bool Succeeded, string? Token, ChallengeVerifyError Error);

/// <summary>Failure reasons surfaced to the dialog.</summary>
public enum ChallengeVerifyError
{
    /// <summary>No failure — the call succeeded.</summary>
    None = 0,

    /// <summary>The proof was rejected (wrong TOTP code, wrong password, etc.).</summary>
    ProofRejected = 1,

    /// <summary>The challenge has already been verified or expired (5-minute lifetime).</summary>
    Expired = 2,

    /// <summary>Transport or unexpected status — caller should offer retry.</summary>
    Failed = 3,
}
