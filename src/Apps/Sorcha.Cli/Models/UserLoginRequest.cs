// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Request body for <c>POST /api/auth/login</c> on the Tenant Service — the JSON user-login
/// endpoint (issue #1402). Distinct from <see cref="PasswordGrantRequest"/>, which is the
/// FORM-ENCODED OAuth2 password grant on <c>/api/service-auth/token</c>; the two endpoints are
/// different surfaces with different bodies. Field names match the server's
/// <c>Sorcha.Tenant.Service.Models.Dtos.LoginRequest</c> exactly (verified by
/// <c>Sorcha.Cli.ContractTests</c>).
/// </summary>
public class UserLoginRequest
{
    /// <summary>User email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>User password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Optional organization subdomain. If not provided, the server looks up by email domain or
    /// uses the default organization.
    /// </summary>
    public string? OrganizationSubdomain { get; set; }

    /// <summary>
    /// Optional explicit trust-tier hint (spec 136): <c>consumer</c> or <c>platform</c>. The CLI is
    /// an operator tool, so it never sets this — left for completeness of the wire contract.
    /// </summary>
    public string? Tier { get; set; }

    /// <summary>Optional post-authentication destination used to derive the trust tier (spec 136).</summary>
    public string? ReturnTo { get; set; }
}

/// <summary>
/// Response from <c>POST /api/auth/login</c> when the account belongs to more than one
/// organisation — the server returns this instead of a token and expects a follow-up
/// <c>POST /api/auth/select-org</c> (<see cref="CompleteOrgSelectionRequest"/>). Field names and
/// JSON property names match the server's
/// <c>Sorcha.Tenant.Service.Models.Dtos.OrgSelectionResponse</c> exactly (verified by
/// <c>Sorcha.Cli.ContractTests</c>).
/// </summary>
public class OrgSelectionResponse
{
    /// <summary>Whether org selection is required. Always true when this shape is returned.</summary>
    [JsonPropertyName("requires_org_selection")]
    public bool RequiresOrgSelection { get; set; } = true;

    /// <summary>Short-lived token for completing org selection.</summary>
    [JsonPropertyName("platform_login_token")]
    public string PlatformLoginToken { get; set; } = string.Empty;

    /// <summary>Available organisations to choose from.</summary>
    [JsonPropertyName("organizations")]
    public List<OrgSelectionEntry> Organizations { get; set; } = [];

    /// <summary>Human-readable message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// An organisation entry in <see cref="OrgSelectionResponse"/>. Field names match the server's
/// <c>Sorcha.Tenant.Service.Models.Dtos.OrgSelectionEntry</c> exactly.
/// </summary>
public class OrgSelectionEntry
{
    /// <summary>Organisation ID.</summary>
    [JsonPropertyName("organization_id")]
    public Guid OrganizationId { get; set; }

    /// <summary>Organisation display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Organisation subdomain.</summary>
    [JsonPropertyName("subdomain")]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>User's role in this organisation.</summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Request body for <c>POST /api/auth/select-org</c> — completes login for a multi-org user
/// after <see cref="OrgSelectionResponse"/>. Field names match the server's
/// <c>Sorcha.Tenant.Service.Models.Dtos.CompleteOrgSelectionRequest</c> exactly.
/// </summary>
public class CompleteOrgSelectionRequest
{
    /// <summary>Platform login token from the org selection response.</summary>
    [JsonPropertyName("platform_login_token")]
    public string PlatformLoginToken { get; set; } = string.Empty;

    /// <summary>The chosen organisation ID.</summary>
    [JsonPropertyName("organization_id")]
    public Guid OrganizationId { get; set; }
}
