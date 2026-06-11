// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models;

/// <summary>
/// Relative strength of a sign-in method or step-up proof (Feature 150). Mirror of the Tenant
/// Service <c>AuthAssuranceTier</c> — serialised as a JSON string enum, so the names MUST match
/// the server's. Drives the assurance badge and the client-side reflection of the floor rule
/// (the client never decides; it reflects the server's <c>RequiredProofTier</c>/<c>CanRemove</c>).
/// </summary>
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
/// Aggregate read of the signed-in user's sign-in methods (Feature 116 US4 / Feature 150).
/// Hand-maintained mirror of the Tenant Service <c>AuthMethodsResponse</c> wire shape — keep the
/// property names and the assurance-enum names in lockstep with the server DTO.
/// </summary>
public sealed record AuthMethodsResponse
{
    public string Email { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
    public AuthMethodsPassword Password { get; init; } = new();
    public IReadOnlyList<AuthMethodsSocial> Socials { get; init; } = [];
    public IReadOnlyList<AuthMethodsPasskey> Passkeys { get; init; } = [];

    /// <summary>True only when an operator has configured an SMS provider (Feature 150 US3).</summary>
    public bool SmsAvailable { get; init; }
}

/// <summary>Password-section view model.</summary>
public sealed record AuthMethodsPassword
{
    public bool IsSet { get; init; }
    public DateTimeOffset? LastChangedAt { get; init; }
    public bool CanRemove { get; init; }

    /// <summary>Assurance tier of the password as a sign-in method (Feature 150) — Strong.</summary>
    public AuthAssuranceTier AssuranceTier { get; init; } = AuthAssuranceTier.Strong;

    /// <summary>Minimum step-up proof tier required to change or remove the password (Feature 150).</summary>
    public AuthAssuranceTier RequiredProofTier { get; init; } = AuthAssuranceTier.Strong;
}

/// <summary>Linked-social-provider row.</summary>
public sealed record AuthMethodsSocial
{
    public Guid LinkId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset LinkedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool CanRemove { get; init; }

    /// <summary>Assurance tier of a linked social as a sign-in method (Feature 150) — Basic.</summary>
    public AuthAssuranceTier AssuranceTier { get; init; } = AuthAssuranceTier.Basic;

    /// <summary>Minimum step-up proof tier required to unlink this social (Feature 150) — Basic.</summary>
    public AuthAssuranceTier RequiredProofTier { get; init; } = AuthAssuranceTier.Basic;
}

/// <summary>Registered-passkey row. Excludes Revoked passkeys; includes Disabled.</summary>
public sealed record AuthMethodsPasskey
{
    public Guid Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? DeviceType { get; init; }
    public string Status { get; init; } = "Active";
    public string? DisabledReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
    public bool CanRemove { get; init; }
    public bool CanRename { get; init; }

    /// <summary>Assurance tier of a passkey as a sign-in method (Feature 150) — Strongest.</summary>
    public AuthAssuranceTier AssuranceTier { get; init; } = AuthAssuranceTier.Strongest;

    /// <summary>Minimum step-up proof tier required to revoke this passkey (Feature 150) — Strongest.</summary>
    public AuthAssuranceTier RequiredProofTier { get; init; } = AuthAssuranceTier.Strongest;
}
