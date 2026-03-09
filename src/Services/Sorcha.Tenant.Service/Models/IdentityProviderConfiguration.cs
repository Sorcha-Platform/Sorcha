// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// External identity provider configuration for an organization.
/// Supports Azure Entra ID, AWS Cognito, and generic OIDC providers.
/// </summary>
public class IdentityProviderConfiguration
{
    /// <summary>
    /// Unique configuration identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Associated organization ID.
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Provider preset for configuration shortcuts.
    /// </summary>
    public IdentityProviderType ProviderPreset { get; set; }

    /// <summary>
    /// OIDC issuer URL (e.g., https://login.microsoftonline.com/{tenant-id}/v2.0).
    /// </summary>
    public string IssuerUrl { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// AES-256-GCM encrypted client secret.
    /// Encrypted using Sorcha.Cryptography library.
    /// </summary>
    public byte[] ClientSecretEncrypted { get; set; } = [];

    /// <summary>
    /// OAuth2 scopes (e.g., openid, profile, email).
    /// Must include at least "openid" scope.
    /// </summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>
    /// Authorization endpoint URL. Auto-discovered from OIDC discovery document.
    /// </summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// Token endpoint URL. Auto-discovered from OIDC discovery document.
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// UserInfo endpoint URL. Auto-discovered from OIDC discovery document.
    /// </summary>
    public string? UserInfoEndpoint { get; set; }

    /// <summary>
    /// JSON Web Key Set URI for token signature validation.
    /// </summary>
    public string? JwksUri { get; set; }

    /// <summary>
    /// OIDC discovery URL (/.well-known/openid-configuration).
    /// </summary>
    public string? MetadataUrl { get; set; }

    /// <summary>
    /// Whether this IDP is currently enabled for user authentication.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// UI display name for this provider (e.g., "Google Workspace", "Corporate SSO").
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Cached raw OIDC discovery document JSON.
    /// </summary>
    public string? DiscoveryDocumentJson { get; set; }

    /// <summary>
    /// Timestamp when the discovery document was last fetched.
    /// </summary>
    public DateTimeOffset? DiscoveryFetchedAt { get; set; }

    /// <summary>
    /// Configuration creation timestamp (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to owning organization.
    /// </summary>
    public Organization Organization { get; set; } = null!;
}

/// <summary>
/// Supported external identity provider types with configuration presets.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdentityProviderType
{
    /// <summary>
    /// Microsoft Entra ID (formerly Azure AD).
    /// </summary>
    MicrosoftEntra,

    /// <summary>
    /// Google Workspace / Google Cloud Identity.
    /// </summary>
    Google,

    /// <summary>
    /// Okta Identity Platform.
    /// </summary>
    Okta,

    /// <summary>
    /// Apple Sign In.
    /// </summary>
    Apple,

    /// <summary>
    /// Amazon Cognito User Pools.
    /// </summary>
    AmazonCognito,

    /// <summary>
    /// Generic OpenID Connect compliant provider.
    /// </summary>
    GenericOidc
}
