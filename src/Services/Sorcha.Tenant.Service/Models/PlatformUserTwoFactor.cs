// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Account-wide second-factor channel enablement for a <see cref="PlatformUser"/> (Feature 150).
/// 1:1 with the user (PK = FK = <see cref="PlatformUserId"/>), created lazily the first time a
/// channel is enabled. Authenticator (TOTP) enablement is intentionally absent — it is org-scoped
/// and lives in <c>TotpConfiguration</c>; this row carries only the platform-wide Email/SMS OTP flags.
/// </summary>
public class PlatformUserTwoFactor
{
    /// <summary>The owning <see cref="PlatformUser"/> — primary key and foreign key (1:1).</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>True when emailed one-time codes are enabled as a second factor (US2).</summary>
    public bool EmailOtpEnabled { get; set; }

    /// <summary>True when SMS one-time codes are enabled as a second factor (US3).</summary>
    public bool SmsOtpEnabled { get; set; }

    /// <summary>Last time any flag on this row changed (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation back to the owning user.</summary>
    public PlatformUser? PlatformUser { get; set; }
}
