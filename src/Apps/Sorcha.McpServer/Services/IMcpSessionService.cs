// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.McpServer.Services;

/// <summary>
/// Manages the current MCP session context derived from JWT authentication.
/// </summary>
public interface IMcpSessionService
{
    /// <summary>
    /// Gets the current session information.
    /// </summary>
    McpSession? CurrentSession { get; }

    /// <summary>
    /// Initializes the session from a JWT token.
    /// </summary>
    /// <param name="jwtToken">The JWT token to parse and validate.</param>
    /// <exception cref="InvalidOperationException">Thrown when the token is invalid.</exception>
    void InitializeFromToken(string jwtToken);

    /// <summary>
    /// Checks if the current session token has expired.
    /// </summary>
    /// <returns>True if expired or no session exists, false otherwise.</returns>
    bool IsTokenExpired();
}

/// <summary>
/// Represents an authenticated MCP session derived from a JWT token.
/// </summary>
public sealed record McpSession
{
    /// <summary>
    /// The unique user identifier (sub claim).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// The tenant/organization identifier (org_id claim).
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The organization name (org_name claim).
    /// </summary>
    public string? OrganizationName { get; init; }

    /// <summary>
    /// The user's assigned roles in Sorcha MCP format (e.g., sorcha:admin, sorcha:designer, sorcha:participant).
    /// </summary>
    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>
    /// The caller's F136 trust tier, derived from the token audience (spec 139). Null when the
    /// audience is not a recognised tier audience. Drives tier-based tool entitlement.
    /// </summary>
    public Sorcha.ServiceDefaults.Auth.Tier? Tier { get; init; }

    /// <summary>
    /// The user's wallet address, if linked.
    /// </summary>
    public string? WalletAddress { get; init; }

    /// <summary>
    /// The user's email address (email claim).
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// The user's display name (name claim).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The token type: "user" or "service".
    /// </summary>
    public string TokenType { get; init; } = "user";

    /// <summary>
    /// The service client ID (for service tokens only).
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// The service name (for service tokens only).
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>
    /// The allowed scopes (for service tokens).
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// When the session token expires.
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// When the session token was issued.
    /// </summary>
    public DateTime IssuedAt { get; init; }

    /// <summary>
    /// The unique token identifier (jti claim).
    /// </summary>
    public string? TokenId { get; init; }

    /// <summary>
    /// Checks if the session has a specific role.
    /// </summary>
    /// <param name="role">The role to check (e.g., "sorcha:admin").</param>
    /// <returns>True if the session has the role.</returns>
    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks if this is an administrator session.
    /// </summary>
    public bool IsAdmin => HasRole("sorcha:admin");

    /// <summary>
    /// Checks if this is a designer session.
    /// </summary>
    public bool IsDesigner => HasRole("sorcha:designer");

    /// <summary>
    /// Checks if this is a participant session.
    /// </summary>
    public bool IsParticipant => HasRole("sorcha:participant");

    /// <summary>
    /// Checks if this is a service token (as opposed to user token).
    /// </summary>
    public bool IsServiceToken => TokenType == "service";
}
