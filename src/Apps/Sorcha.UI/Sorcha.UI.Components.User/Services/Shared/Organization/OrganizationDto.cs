// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Organization DTO for client-side use.
/// </summary>
/// <remarks>
/// Extracted from <c>IOrganizationAdminService.cs</c> as part of Feature 123
/// so user-facing components (e.g., <c>PublishParticipantDialog</c>) can
/// declare it as a parameter type without inheriting the admin service
/// surface. The namespace is preserved (<c>Sorcha.UI.Core.Services</c>) so
/// consumer <c>using</c> directives are unchanged.
/// </remarks>
public record OrganizationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string Status { get; init; } = "Active";
    public DateTimeOffset CreatedAt { get; init; }
    public BrandingDto? Branding { get; init; }

    /// <summary>
    /// Feature 181 US5 — the org's signing wallet address (holds the P-256 issuing key). Null until the
    /// wallet is provisioned. Used by the admin certificates panel to address the org-cert endpoints.
    /// </summary>
    public string? WalletAddress { get; init; }
}
