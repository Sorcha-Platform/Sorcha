// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Auth;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Single-use, scoped, short-lived authorisation issued after a successful
/// re-authentication challenge. Presented in the <c>X-Auth-Challenge</c> header
/// on the subsequent mutation call. The raw token is never stored — only the
/// SHA-256 hash. See Feature 116 design §6 for the full lifecycle.
/// </summary>
public class AuthChallengeToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning <see cref="PlatformUser"/>. FK with cascade delete.</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the opaque token string. Stored as 64 hex characters.
    /// The raw token is returned to the caller only once (in the verify response)
    /// and is never persisted server-side.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>How the user proved possession.</summary>
    public ChallengeMethod Method { get; set; }

    /// <summary>
    /// Operation this token authorises. A token issued for
    /// <see cref="ScopedOperation.ChangePassword"/> cannot be replayed
    /// against a remove endpoint.
    /// </summary>
    public ScopedOperation ScopedOperation { get; set; }

    /// <summary>Issue timestamp (UTC).</summary>
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Expiry timestamp (UTC). Always <see cref="IssuedAt"/> + 5 minutes.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// First-consume timestamp (UTC). Null until the token is presented to a
    /// gated endpoint. The atomic <c>UPDATE … WHERE consumed_at IS NULL</c>
    /// in <see cref="Data.Repositories.IAuthChallengeRepository.TryConsumeAsync"/>
    /// makes consume a one-shot operation safe under concurrent presentation.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Navigation back to the owning user.</summary>
    public PlatformUser PlatformUser { get; set; } = null!;
}
