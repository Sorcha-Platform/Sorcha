// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// OAuth2 Client Credentials request for service-to-service authentication.
/// </summary>
public record ClientCredentialsRequest
{
    /// <summary>
    /// Client ID (service principal).
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Client secret.
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Requested scope (space-separated).
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// Grant type (must be "client_credentials").
    /// </summary>
    public string GrantType { get; init; } = "client_credentials";
}

/// <summary>
/// OAuth2 unified token request supporting multiple grant types.
/// </summary>
public record OAuth2TokenRequest
{
    /// <summary>
    /// Grant type: "password", "client_credentials", or "refresh_token".
    /// </summary>
    public string GrantType { get; init; } = string.Empty;

    /// <summary>
    /// Username (for password grant).
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// Password (for password grant).
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Client ID.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Client secret (for client_credentials grant).
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// Refresh token (for refresh_token grant).
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Requested scope (space-separated).
    /// </summary>
    public string? Scope { get; init; }

    // ---------------------------------------------------------------------------------------
    // OAuth2 snake_case aliases (issue #1443)
    // ---------------------------------------------------------------------------------------
    //
    // System.Text.Json binds this record with the default web naming policy, so a JSON body has to
    // spell these camelCase — while the form-urlencoded path on the very same endpoints reads the
    // OAuth2 spelling (grant_type, client_id, client_secret). The two accepted content types
    // therefore disagreed on field names, and the spelling any caller reaches for first bound
    // NOTHING.
    //
    // It did not fail as "field ignored". Every field came back empty, so the request fell through
    // to whichever guard fired first: a valid snake_case client_credentials body against a node with
    // ServiceAuth:DisableSharedSecrets=true answered "Client ID is required" when the truthful answer
    // was "this deployment has retired shared secrets" — sending an operator to debug their request
    // instead of their credential class.
    //
    // Both spellings are accepted rather than swapping to the OAuth2 one, because in-tree callers
    // and the deploy runbooks already post camelCase JSON; breaking them to gain spec-conformance we
    // can have anyway would be a poor trade. Only fields whose OAuth2 name actually differs need an
    // alias — username, password and scope are spelled identically either way.

    /// <summary>OAuth2 spelling of <see cref="GrantType"/>. Prefer <see cref="ResolvedGrantType"/> when reading.</summary>
    [JsonPropertyName("grant_type")]
    public string? GrantTypeAlias { get; init; }

    /// <summary>OAuth2 spelling of <see cref="ClientId"/>. Prefer <see cref="ResolvedClientId"/> when reading.</summary>
    [JsonPropertyName("client_id")]
    public string? ClientIdAlias { get; init; }

    /// <summary>OAuth2 spelling of <see cref="ClientSecret"/>. Prefer <see cref="ResolvedClientSecret"/> when reading.</summary>
    [JsonPropertyName("client_secret")]
    public string? ClientSecretAlias { get; init; }

    /// <summary>OAuth2 spelling of <see cref="RefreshToken"/>. Prefer <see cref="ResolvedRefreshToken"/> when reading.</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshTokenAlias { get; init; }

    /// <summary>The grant type, in whichever spelling the caller used.</summary>
    [JsonIgnore]
    public string ResolvedGrantType => Prefer(GrantType, GrantTypeAlias);

    /// <summary>The client id, in whichever spelling the caller used.</summary>
    [JsonIgnore]
    public string ResolvedClientId => Prefer(ClientId, ClientIdAlias);

    /// <summary>The client secret, in whichever spelling the caller used.</summary>
    [JsonIgnore]
    public string ResolvedClientSecret => Prefer(ClientSecret, ClientSecretAlias);

    /// <summary>The refresh token, in whichever spelling the caller used.</summary>
    [JsonIgnore]
    public string ResolvedRefreshToken => Prefer(RefreshToken, RefreshTokenAlias);

    private static string Prefer(string? camel, string? snake) =>
        !string.IsNullOrWhiteSpace(camel) ? camel : (snake ?? string.Empty);
}

/// <summary>
/// Request for delegated authority token.
/// Service acting on behalf of a user.
/// </summary>
public record DelegatedTokenRequest
{
    /// <summary>
    /// Client ID (service principal).
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Client secret. Optional since F191 (#1420): a request arriving over the workload mTLS
    /// listener with a valid workload certificate needs no secret — the chain-validated
    /// certificate is the credential. Secretless requests on any other listener are refused.
    /// </summary>
    public string? ClientSecret { get; init; }

    /// <summary>
    /// User ID to delegate authority for.
    /// </summary>
    public required Guid DelegatedUserId { get; init; }

    /// <summary>
    /// Organization ID for delegated authority.
    /// </summary>
    public Guid? DelegatedOrganizationId { get; init; }

    /// <summary>
    /// Requested scope (space-separated).
    /// </summary>
    public string? Scope { get; init; }
}

/// <summary>
/// Request to register a new service principal.
/// </summary>
public record RegisterServicePrincipalRequest
{
    /// <summary>
    /// Unique service name.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Allowed OAuth2 scopes.
    /// </summary>
    public string[] Scopes { get; init; } = [];
}

/// <summary>
/// Response after registering a service principal.
/// Contains the generated credentials (only shown once).
/// </summary>
public record ServicePrincipalRegistrationResponse
{
    /// <summary>
    /// Service principal ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Service name.
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Generated client ID.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Generated client secret (only shown once, store securely).
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Allowed scopes.
    /// </summary>
    public string[] Scopes { get; init; } = [];

    /// <summary>
    /// Warning message about storing the secret.
    /// </summary>
    public string Warning { get; init; } = "Store the client secret securely. It cannot be retrieved after this response.";
}

/// <summary>
/// Service principal information (without secrets).
/// </summary>
public record ServicePrincipalResponse
{
    /// <summary>
    /// Service principal ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Service name.
    /// </summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>
    /// Client ID.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Allowed scopes.
    /// </summary>
    public string[] Scopes { get; init; } = [];

    /// <summary>
    /// Service principal status.
    /// </summary>
    public string Status { get; init; } = "Active";

    /// <summary>
    /// Registration timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Creates a response from a ServicePrincipal entity.
    /// </summary>
    public static ServicePrincipalResponse FromEntity(Models.ServicePrincipal sp) => new()
    {
        Id = sp.Id,
        ServiceName = sp.ServiceName,
        ClientId = sp.ClientId,
        Scopes = sp.Scopes,
        Status = sp.Status.ToString(),
        CreatedAt = sp.CreatedAt
    };
}

/// <summary>
/// Service principal list response.
/// </summary>
public record ServicePrincipalListResponse
{
    /// <summary>
    /// List of service principals.
    /// </summary>
    public IReadOnlyList<ServicePrincipalResponse> ServicePrincipals { get; init; } = [];

    /// <summary>
    /// Total count.
    /// </summary>
    public int TotalCount { get; init; }
}

/// <summary>
/// Request to rotate service principal secret.
/// </summary>
public record RotateSecretRequest
{
    /// <summary>
    /// Current client secret for verification.
    /// </summary>
    public required string CurrentSecret { get; init; }
}

/// <summary>
/// Response after rotating service principal secret.
/// </summary>
public record RotateSecretResponse
{
    /// <summary>
    /// New client secret (only shown once).
    /// </summary>
    public string NewClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Warning message.
    /// </summary>
    public string Warning { get; init; } = "Store the new client secret securely. The old secret is no longer valid.";
}
