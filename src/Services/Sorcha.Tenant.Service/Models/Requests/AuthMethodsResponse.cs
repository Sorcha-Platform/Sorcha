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
public sealed record AuthMethodsResponse(
    string Email,
    bool EmailVerified,
    AuthMethodsPassword Password,
    IReadOnlyList<AuthMethodsSocial> Socials,
    IReadOnlyList<AuthMethodsPasskey> Passkeys);

/// <summary>Password-section view model.</summary>
/// <param name="IsSet">True when <c>PasswordHash</c> is non-null.</param>
/// <param name="LastChangedAt">Best-effort last-changed timestamp; null if unknown.</param>
/// <param name="CanRemove">False iff removing the password would leave zero sign-in methods.</param>
public sealed record AuthMethodsPassword(
    bool IsSet,
    DateTimeOffset? LastChangedAt,
    bool CanRemove);

/// <summary>Linked-social-provider row.</summary>
public sealed record AuthMethodsSocial(
    Guid LinkId,
    string Provider,
    string? Email,
    string? DisplayName,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastUsedAt,
    bool CanRemove);

/// <summary>Registered-passkey row. Excludes Revoked passkeys; includes Disabled.</summary>
public sealed record AuthMethodsPasskey(
    Guid Id,
    string DisplayName,
    string? DeviceType,
    CredentialStatus Status,
    string? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    bool CanRemove,
    bool CanRename);
