// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// Shared JWT claim name constants used across all Sorcha services.
/// Replaces hardcoded claim strings to ensure consistency.
/// </summary>
public static class TokenClaimConstants
{
    /// <summary>
    /// Identifies the token type: "user", "service"
    /// </summary>
    public const string TokenType = "token_type";

    /// <summary>
    /// User ID when a service acts on behalf of a user (delegation tokens)
    /// </summary>
    public const string DelegatedUserId = "delegated_user_id";

    /// <summary>
    /// Organization ID of the delegated user
    /// </summary>
    public const string DelegatedOrgId = "delegated_org_id";

    /// <summary>
    /// Organization ID claim for user tokens
    /// </summary>
    public const string OrgId = "org_id";

    /// <summary>
    /// Human-readable service name for service tokens
    /// </summary>
    public const string ServiceName = "service_name";

    /// <summary>
    /// Space-delimited scopes granted to the token
    /// </summary>
    public const string Scope = "scope";

    /// <summary>
    /// Token type value for service tokens
    /// </summary>
    public const string TokenTypeService = "service";

    /// <summary>
    /// Token type value for user tokens
    /// </summary>
    public const string TokenTypeUser = "user";

    /// <summary>
    /// Cross-org stable identifier for the platform user (PlatformUser.Id).
    /// Present on every human-tier token (consumer and platform) since Feature 136.
    /// Use this constant everywhere this claim name is referenced — never a bare string literal.
    /// </summary>
    public const string PlatformUserId = "platform_user_id";
}
