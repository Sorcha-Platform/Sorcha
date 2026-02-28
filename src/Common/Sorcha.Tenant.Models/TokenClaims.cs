// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models;

/// <summary>
/// Standard JWT claims included in access tokens issued by the Tenant Service.
/// Used by all Sorcha services for authorization decisions.
/// </summary>
/// <remarks>
/// Tokens are deployment-aware to support multiple deployment topologies:
/// - SaaS (multi-tenant at sorcha.io)
/// - Enterprise (self-hosted at customer domain)
/// - HostedTenant (subdomain on SaaS)
/// - Federated (cross-deployment authentication)
/// </remarks>
public class TokenClaims
{
    /// <summary>
    /// Subject (user or service principal ID).
    /// Standard JWT "sub" claim.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// Issuer (Tenant Service URL for the deployment that issued this token).
    /// Standard JWT "iss" claim.
    /// Example: "https://tenant.sorcha.io" or "https://auth.big-corporate.com"
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// Audience (which services can consume this token).
    /// Standard JWT "aud" claim.
    /// </summary>
    public string[] Audience { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Token expiration time (Unix timestamp).
    /// Standard JWT "exp" claim.
    /// </summary>
    public long ExpiresAt { get; init; }

    /// <summary>
    /// Token issued at time (Unix timestamp).
    /// Standard JWT "iat" claim.
    /// </summary>
    public long IssuedAt { get; init; }

    /// <summary>
    /// JWT ID (unique token identifier for revocation tracking).
    /// Standard JWT "jti" claim.
    /// </summary>
    public required string TokenId { get; init; }

    /// <summary>
    /// User email (null for service principals).
    /// Standard JWT "email" claim.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// User display name (null for service principals).
    /// Standard JWT "name" claim.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Unique identifier for the Sorcha deployment that issued this token.
    /// Custom claim: "deployment_id".
    /// Used to identify the source installation in federated scenarios.
    /// </summary>
    public required Guid DeploymentId { get; init; }

    /// <summary>
    /// Human-readable name of the deployment.
    /// Custom claim: "deployment_name".
    /// Example: "Sorcha SaaS", "Big Corp Production"
    /// </summary>
    public string? DeploymentName { get; init; }

    /// <summary>
    /// Indicates this token was issued by a federated (remote) deployment.
    /// Custom claim: "federated".
    /// When true, additional validation against federated deployment trust is required.
    /// </summary>
    public bool Federated { get; init; } = false;

    /// <summary>
    /// Organization ID (null for public identities and service principals).
    /// Custom claim: "org_id".
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Organization subdomain (for URL construction).
    /// Custom claim: "org_subdomain".
    /// Example: "acme" for acme.sorcha.io
    /// </summary>
    public string? OrganizationSubdomain { get; init; }

    /// <summary>
    /// Token type ("user", "service", "public").
    /// Custom claim: "token_type".
    /// </summary>
    public required string TokenType { get; init; }

    /// <summary>
    /// User roles within organization (e.g., "Administrator", "Auditor", "Member").
    /// Empty array for public identities.
    /// Custom claim: "roles".
    /// </summary>
    public string[] Roles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Blockchain IDs this user/organization can access.
    /// Empty array if no blockchain access.
    /// Custom claim: "permitted_blockchains".
    /// </summary>
    public Guid[] PermittedBlockchains { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// Whether this identity can create new blockchains.
    /// Custom claim: "can_create_blockchain".
    /// </summary>
    public bool CanCreateBlockchain { get; init; }

    /// <summary>
    /// Whether this identity can publish blueprints.
    /// Custom claim: "can_publish_blueprint".
    /// </summary>
    public bool CanPublishBlueprint { get; init; }

    /// <summary>
    /// JWT claim name constants for consistent claim handling.
    /// </summary>
    public static class ClaimNames
    {
        // Standard JWT claims
        public const string Subject = "sub";
        public const string Issuer = "iss";
        public const string Audience = "aud";
        public const string ExpiresAt = "exp";
        public const string IssuedAt = "iat";
        public const string TokenId = "jti";
        public const string Email = "email";
        public const string Name = "name";

        // Deployment claims
        public const string DeploymentId = "deployment_id";
        public const string DeploymentName = "deployment_name";
        public const string Federated = "federated";

        // Organization claims
        public const string OrganizationId = "org_id";
        public const string OrganizationSubdomain = "org_subdomain";

        // Identity & role claims
        public const string TokenType = "token_type";
        public const string Roles = "roles";

        // Permission claims
        public const string PermittedBlockchains = "permitted_blockchains";
        public const string CanCreateBlockchain = "can_create_blockchain";
        public const string CanPublishBlueprint = "can_publish_blueprint";
    }

    /// <summary>
    /// Token type constants.
    /// </summary>
    public static class TokenTypes
    {
        public const string User = "user";
        public const string Service = "service";
        public const string Public = "public";
    }

    /// <summary>
    /// Role constants.
    /// </summary>
    public static class RoleNames
    {
        public const string Administrator = "Administrator";
        public const string Auditor = "Auditor";
        public const string Member = "Member";
    }

}
