// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Models.Requests;

/// <summary>
/// Aggregate read for the Accounts tab — every sign-in method attached to
/// the signed-in PlatformUser in a single round-trip. Powers the read-only
/// view in US4 and the per-row Remove gating in US1/US2/US3 (each row
/// carries its own <c>CanRemove</c> flag derived from the same floor
/// helper used by the mutation endpoints).
/// </summary>
/// <param name="Email">Email address.</param>
/// <param name="EmailVerified">Flag indicating email verified.</param>
/// <param name="Password">The password.</param>
/// <param name="Socials">Collection of socials associated with this resource.</param>
/// <param name="Passkeys">Collection of passkeys associated with this resource.</param>
/// <param name="SmsAvailable">True only when an operator has configured an SMS provider (Feature 150); gates whether the SMS 2FA option is offered.</param>
public sealed record AuthMethodsResponse(
    string Email,
    bool EmailVerified,
    AuthMethodsPassword Password,
    IReadOnlyList<AuthMethodsSocial> Socials,
    IReadOnlyList<AuthMethodsPasskey> Passkeys,
    bool SmsAvailable);

/// <summary>Password-section view model.</summary>
/// <param name="IsSet">True when <c>PasswordHash</c> is non-null.</param>
/// <param name="LastChangedAt">Best-effort last-changed timestamp; null if unknown.</param>
/// <param name="CanRemove">False iff removing the password would leave zero sign-in methods.</param>
/// <param name="AssuranceTier">Assurance tier of the password as a sign-in method (Feature 150) — Strong.</param>
/// <param name="RequiredProofTier">Minimum step-up proof tier required to change or remove the password (Feature 150).</param>
public sealed record AuthMethodsPassword(
    bool IsSet,
    DateTimeOffset? LastChangedAt,
    bool CanRemove,
    AuthAssuranceTier AssuranceTier,
    AuthAssuranceTier RequiredProofTier);

/// <summary>Linked-social-provider row.</summary>
/// <param name="LinkId">Identifier of the link.</param>
/// <param name="Provider">The provider.</param>
/// <param name="Email">Email address.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="LinkedAt">Timestamp at which linked occurred (UTC).</param>
/// <param name="LastUsedAt">Timestamp at which last used occurred (UTC).</param>
/// <param name="CanRemove">Indicates whether remove.</param>
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
/// <param name="Id">Unique identifier for the resource.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="DeviceType">The device type.</param>
/// <param name="Status">Current status of the resource.</param>
/// <param name="DisabledReason">The disabled reason.</param>
/// <param name="CreatedAt">Server timestamp when the record was created (UTC).</param>
/// <param name="LastUsedAt">Timestamp at which last used occurred (UTC).</param>
/// <param name="CanRemove">Indicates whether remove.</param>
/// <param name="CanRename">Indicates whether rename.</param>
/// <param name="AssuranceTier">Assurance tier of a passkey as a sign-in method (Feature 150) — Strongest.</param>
/// <param name="RequiredProofTier">Minimum step-up proof tier required to revoke this passkey (Feature 150) — Strongest.</param>
public sealed record AuthMethodsPasskey(
    Guid Id,
    string DisplayName,
    string? DeviceType,
    CredentialStatus Status,
    string? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    bool CanRemove,
    bool CanRename,
    AuthAssuranceTier AssuranceTier,
    AuthAssuranceTier RequiredProofTier);
