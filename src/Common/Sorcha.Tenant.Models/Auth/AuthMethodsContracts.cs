// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Auth;

/// <summary>
/// Aggregate read for the Security home (Feature 116 US4 / Feature 150) — every sign-in method
/// attached to the signed-in PlatformUser in a single round-trip.
/// </summary>
/// <remarks>
/// Each row carries its own <c>CanRemove</c> and <c>RequiredProofTier</c>, derived server-side from
/// the same floor helper the mutation endpoints enforce. <b>The client reflects these; it never
/// decides them.</b>
/// </remarks>
/// <param name="Email">Email address.</param>
/// <param name="EmailVerified">Flag indicating email verified.</param>
/// <param name="Password">Password-section view model.</param>
/// <param name="Socials">Linked social providers.</param>
/// <param name="Passkeys">Registered passkeys.</param>
/// <param name="SmsAvailable">True only when an operator has configured an SMS provider (Feature 150); gates whether the SMS 2FA option is offered.</param>
/// <param name="EmailOtpEnabled">True when emailed one-time codes are enabled as a second factor (Feature 150 US2).</param>
/// <param name="SmsOtpEnabled">True when SMS one-time codes are enabled as a second factor (Feature 150 US3).</param>
public sealed record AuthMethodsResponse(
    string Email,
    bool EmailVerified,
    AuthMethodsPassword Password,
    IReadOnlyList<AuthMethodsSocial> Socials,
    IReadOnlyList<AuthMethodsPasskey> Passkeys,
    bool SmsAvailable,
    bool EmailOtpEnabled = false,
    bool SmsOtpEnabled = false);

/// <summary>Password-section view model.</summary>
/// <param name="IsSet">True when <c>PasswordHash</c> is non-null.</param>
/// <param name="LastChangedAt">Best-effort last-changed timestamp; null if unknown.</param>
/// <param name="CanRemove">False iff removing the password would leave zero sign-in methods.</param>
/// <param name="AssuranceTier">Assurance tier of the password as a sign-in method (Feature 150) — Strong.</param>
/// <param name="RequiredProofTier">Minimum step-up proof tier required to change or remove the password (Feature 150) — Basic, per T061.</param>
public sealed record AuthMethodsPassword(
    bool IsSet,
    DateTimeOffset? LastChangedAt,
    bool CanRemove,
    AuthAssuranceTier AssuranceTier,
    AuthAssuranceTier RequiredProofTier);

/// <summary>Linked-social-provider row.</summary>
/// <param name="LinkId">Identifier of the link.</param>
/// <param name="Provider">The provider (<c>google</c>, <c>github</c>, <c>microsoft</c>, <c>apple</c>).</param>
/// <param name="Email">Email address reported by the provider.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="LinkedAt">Timestamp at which linking occurred (UTC).</param>
/// <param name="LastUsedAt">Timestamp at which the link was last used to sign in (UTC).</param>
/// <param name="CanRemove">False iff unlinking would leave zero sign-in methods.</param>
/// <param name="AssuranceTier">Assurance tier of a linked social as a sign-in method (Feature 150) — Basic.</param>
/// <param name="RequiredProofTier">Minimum step-up proof tier required to unlink this social (Feature 150).</param>
public sealed record AuthMethodsSocial(
    Guid LinkId,
    string Provider,
    string? Email,
    string? DisplayName,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastUsedAt,
    bool CanRemove,
    AuthAssuranceTier AssuranceTier,
    AuthAssuranceTier RequiredProofTier);

/// <summary>Registered-passkey row. Excludes Revoked passkeys; includes Disabled.</summary>
/// <remarks>
/// <paramref name="Status"/> is a <see cref="string"/> rather than the Tenant Service's
/// <c>CredentialStatus</c> enum, deliberately. That enum is EF-mapped on <c>PasskeyCredential</c>
/// with ~40 service-internal usages, so hoisting it into this zero-dependency leaf to satisfy one
/// response property would drag persistence concerns along with it. The wire shape is unchanged:
/// <c>CredentialStatus</c> carries <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> and so
/// has always serialised as its PascalCase name — the string the client has always read.
/// </remarks>
/// <param name="Id">Unique identifier for the credential.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="DeviceType">The authenticator device type, when reported.</param>
/// <param name="Status">Credential status name: <c>Active</c>, <c>Disabled</c> or <c>Revoked</c>.</param>
/// <param name="DisabledReason">Why the credential was disabled (e.g. cloned-authenticator detection).</param>
/// <param name="CreatedAt">Server timestamp when the credential was registered (UTC).</param>
/// <param name="LastUsedAt">Timestamp at which the credential was last used (UTC).</param>
/// <param name="CanRemove">False iff revoking would leave zero sign-in methods.</param>
/// <param name="CanRename">Whether the display name may be changed.</param>
/// <param name="AssuranceTier">Assurance tier of a passkey as a sign-in method (Feature 150) — Strongest.</param>
/// <param name="RequiredProofTier">Minimum step-up proof tier required to revoke this passkey (Feature 150) — Strongest.</param>
public sealed record AuthMethodsPasskey(
    Guid Id,
    string DisplayName,
    string? DeviceType,
    string Status,
    string? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    bool CanRemove,
    bool CanRename,
    AuthAssuranceTier AssuranceTier,
    AuthAssuranceTier RequiredProofTier);
