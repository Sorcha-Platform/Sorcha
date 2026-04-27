// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models;

/// <summary>
/// Aggregate read of the signed-in user's sign-in methods (Feature 116 US4).
/// Mirror of the Tenant Service <c>AuthMethodsResponse</c> wire shape.
/// </summary>
public sealed record AuthMethodsResponse
{
    public string Email { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
    public AuthMethodsPassword Password { get; init; } = new();
    public IReadOnlyList<AuthMethodsSocial> Socials { get; init; } = [];
    public IReadOnlyList<AuthMethodsPasskey> Passkeys { get; init; } = [];
}

/// <summary>Password-section view model.</summary>
public sealed record AuthMethodsPassword
{
    public bool IsSet { get; init; }
    public DateTimeOffset? LastChangedAt { get; init; }
    public bool CanRemove { get; init; }
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
}
