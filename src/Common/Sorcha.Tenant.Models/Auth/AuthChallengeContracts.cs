// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Models.Auth;

/// <summary>
/// How the user proved possession when satisfying a re-authentication challenge (Feature 116/150).
/// Selected per the ladder in the Tenant Service's <c>IAuthChallengeService</c>:
/// TOTP (when 2FA enrolled) → password (when set) → passkey step-up → re-OAuth.
/// </summary>
/// <remarks>
/// <b>The member names are a wire contract</b> — they cross the network as JSON strings, so a
/// rename silently unbinds every client. This enum previously existed twice (Tenant Service and
/// the Blazor UI) and the copies had already diverged: the UI's stopped at <c>ReOAuth</c>, missing
/// <c>EmailOtp</c> and <c>SmsOtp</c>. The moment the server's ladder selected one of those, the
/// client's deserialiser would have thrown on an unknown enum name and the entire step-up dialog
/// would have failed — not degraded, failed. One declaration makes that impossible.
/// </remarks>
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

    /// <summary>Emailed one-time code (Feature 150 US2) — Basic tier.</summary>
    EmailOtp = 4,

    /// <summary>SMS one-time code (Feature 150 US3, config-gated) — Basic tier.</summary>
    SmsOtp = 5
}

/// <summary>
/// Operation that a re-authentication challenge token authorises.
/// Tokens are scoped — a token issued for one operation cannot be replayed against another.
/// Enforced server-side by <c>RequireAuthChallengeAttribute</c>.
/// </summary>
/// <remarks>
/// Member names are a wire contract; see the remarks on <see cref="ChallengeMethod"/>. The UI's
/// former copy of this enum omitted <see cref="LinkSocial"/>, so the client could not express a
/// link-social step-up at all.
/// </remarks>
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

    /// <summary>Step-up proof for connecting a new social identity to an existing account.</summary>
    LinkSocial = 5
}

/// <summary>
/// Identifies a sign-in method kind for the last-method floor check and for the Feature 150 floor
/// rule. TOTP enrolment is intentionally absent — it is a second factor, not a sign-in method.
/// </summary>
/// <remarks>
/// Sent on challenge initiate/verify so the server can compute the required proof tier for the
/// ambiguous <see cref="ScopedOperation.RemoveAuthMethod"/> (passkey-revoke vs social-unlink).
/// A null target fails safe to the strongest tier server-side.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthMethodKind
{
    /// <summary>The account password (presence flag, not a row).</summary>
    Password = 0,

    /// <summary>A linked social provider (<c>PlatformSocialLogin</c> row).</summary>
    Social = 1,

    /// <summary>An active passkey (<c>PasskeyCredential</c> with Status=Active).</summary>
    Passkey = 2
}

/// <summary>
/// Relative strength of an authentication method or step-up proof (Feature 150).
/// </summary>
/// <remarks>
/// The numeric values are deliberately ordinal so the floor rule can compare tiers with
/// <c>&gt;=</c>: a proof authorises a destructive/downgrade operation only when its tier is greater
/// than or equal to the target's. Computed from the method kind by the Tenant Service's
/// <c>AssurancePolicy</c> — never persisted. The client only ever reflects the server's decision.
/// </remarks>
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

/// <summary>
/// Request to begin a re-authentication challenge.
/// </summary>
/// <param name="ScopedOperation">Operation the resulting token will authorise.</param>
/// <param name="PreferredMethod">
/// Optional override of the default ladder selection. The server still validates that the choice is
/// enrolled and floor-eligible for the operation.
/// </param>
/// <param name="TargetMethodKind">
/// The sign-in method the operation targets, when ambiguous. Required for
/// <see cref="Auth.ScopedOperation.RemoveAuthMethod"/> (passkey-revoke vs social-unlink) so the
/// floor rule computes the correct required proof tier. A null target fails safe to the strongest tier.
/// </param>
public sealed record ChallengeInitiateRequest(
    ScopedOperation ScopedOperation,
    ChallengeMethod? PreferredMethod,
    AuthMethodKind? TargetMethodKind = null);

/// <summary>
/// Server response after a successful initiate. The dialog uses <see cref="Method"/> to decide which
/// proof input to render and <see cref="Payload"/> for any method-specific data.
/// </summary>
/// <param name="Method">Method the user is expected to satisfy.</param>
/// <param name="Payload">Method-specific payload (e.g. WebAuthn assertion options); null for TOTP/Password.</param>
public sealed record ChallengeInitiateResponse(
    ChallengeMethod Method,
    JsonElement? Payload);

/// <summary>
/// Submit the proof produced by the user.
/// </summary>
/// <param name="Method">Method the proof corresponds to.</param>
/// <param name="ScopedOperation">Operation the resulting token must be bound to.</param>
/// <param name="Proof">
/// Method-specific proof body. <c>{ "code": "123456" }</c> for TOTP, <c>{ "password": "..." }</c>
/// for Password, WebAuthn assertion JSON for Passkey, OAuth code/state object for ReOAuth.
/// </param>
/// <param name="TargetMethodKind">
/// The sign-in method the operation targets, when ambiguous (see
/// <see cref="ChallengeInitiateRequest.TargetMethodKind"/>). The floor rule is re-checked on verify;
/// a proof tier below the required tier yields <c>403 proof_tier_insufficient</c>.
/// </param>
public sealed record ChallengeVerifyRequest(
    ChallengeMethod Method,
    ScopedOperation ScopedOperation,
    JsonElement Proof,
    AuthMethodKind? TargetMethodKind = null);

/// <summary>
/// Server response after a successful verify. The raw token is returned only here; subsequent
/// mutation calls present it in the <c>X-Auth-Challenge</c> header.
/// </summary>
/// <param name="Token">Opaque single-use token. Always begins <c>ch_</c>.</param>
/// <param name="ExpiresIn">Seconds until expiry. Always 300.</param>
public sealed record ChallengeVerifyResponse(
    string Token,
    int ExpiresIn);
