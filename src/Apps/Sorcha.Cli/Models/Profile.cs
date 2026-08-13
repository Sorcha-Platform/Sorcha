// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Cli.Models;

/// <summary>
/// Represents a configuration profile for connecting to Sorcha services.
/// </summary>
public class Profile
{
    /// <summary>
    /// Profile name (e.g., "dev", "staging", "production").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for all Sorcha services (e.g., "http://localhost" or "https://api.sorcha.io").
    /// Individual service URLs are derived from this unless explicitly overridden.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Base URL for the Tenant Service API.
    /// If not specified, derived from ServiceUrl.
    /// </summary>
    public string? TenantServiceUrl { get; set; }

    /// <summary>
    /// Base URL for the Register Service API.
    /// If not specified, derived from ServiceUrl.
    /// </summary>
    public string? RegisterServiceUrl { get; set; }

    /// <summary>
    /// Base URL for the Peer Service API.
    /// If not specified, derived from ServiceUrl.
    /// </summary>
    public string? PeerServiceUrl { get; set; }

    /// <summary>
    /// Base URL for the Wallet Service API.
    /// If not specified, derived from ServiceUrl.
    /// </summary>
    public string? WalletServiceUrl { get; set; }

    /// <summary>
    /// Base URL for the Blueprint Service API.
    /// If not specified, derived from ServiceUrl.
    /// </summary>
    public string? BlueprintServiceUrl { get; set; }

    /// <summary>
    /// Base URL for the Validator Service API.
    /// If not specified, derived from ServiceUrl.
    /// </summary>
    public string? ValidatorServiceUrl { get; set; }

    /// <summary>
    /// Base URL for the API Gateway.
    /// If not specified, derived from ServiceUrl.
    /// </summary>
    public string? GatewayUrl { get; set; }

    /// <summary>
    /// OAuth2 token endpoint for the BROWSER/HUMAN grants (<c>password</c>, <c>refresh_token</c>) —
    /// the public gateway route <c>/api/service-auth/token</c>. Used only for token refresh; user
    /// login itself goes through <see cref="GetUserLoginUrl"/> instead (issue #1402). If not
    /// specified, derived from ServiceUrl as {ServiceUrl}/api/service-auth/token.
    /// </summary>
    public string? AuthTokenUrl { get; set; }

    /// <summary>
    /// OAuth2 token endpoint for the SERVICE-PRINCIPAL (<c>client_credentials</c>) grant — the
    /// internal-only Tenant Service route <c>/api/internal/service-auth/token</c> (#1397 / #1406).
    /// The public API Gateway does not route <c>/api/internal/*</c>, so this only succeeds when the
    /// CLI runs inside the Sorcha trust network (e.g. co-located in the docker-compose network) or
    /// points directly at the Tenant Service. If not specified, derived from the effective Tenant
    /// Service URL as {TenantServiceUrl}/api/internal/service-auth/token.
    /// </summary>
    public string? ServiceAuthTokenUrl { get; set; }

    /// <summary>
    /// Default client ID for user authentication.
    /// </summary>
    public string? DefaultClientId { get; set; }

    /// <summary>
    /// Whether to verify SSL certificates (disable for local dev).
    /// </summary>
    public bool VerifySsl { get; set; } = true;

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Additional custom settings for this profile.
    /// </summary>
    public Dictionary<string, string> CustomSettings { get; set; } = new();

    /// <summary>
    /// Gets the effective Tenant Service URL, deriving from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetTenantServiceUrl() => TenantServiceUrl ?? ServiceUrl ?? string.Empty;

    /// <summary>
    /// Gets the effective Register Service URL, deriving from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetRegisterServiceUrl() => RegisterServiceUrl ?? ServiceUrl ?? string.Empty;

    /// <summary>
    /// Gets the effective Peer Service URL, deriving from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetPeerServiceUrl() => PeerServiceUrl ?? ServiceUrl ?? string.Empty;

    /// <summary>
    /// Gets the effective Wallet Service URL, deriving from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetWalletServiceUrl() => WalletServiceUrl ?? ServiceUrl ?? string.Empty;

    /// <summary>
    /// Gets the effective Blueprint Service URL, deriving from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetBlueprintServiceUrl() => BlueprintServiceUrl ?? ServiceUrl ?? string.Empty;

    /// <summary>
    /// Gets the effective Validator Service URL, deriving from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetValidatorServiceUrl() => ValidatorServiceUrl ?? ServiceUrl ?? string.Empty;

    /// <summary>
    /// Gets the effective API Gateway URL, deriving from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetGatewayUrl() => GatewayUrl ?? ServiceUrl ?? string.Empty;

    /// <summary>
    /// Gets the effective Auth Token URL (browser/human grants — password, refresh_token), deriving
    /// from ServiceUrl if not explicitly set.
    /// </summary>
    public string GetAuthTokenUrl() => AuthTokenUrl ?? (string.IsNullOrEmpty(ServiceUrl) ? string.Empty : $"{ServiceUrl}/api/service-auth/token");

    /// <summary>
    /// Gets the effective service-principal (client_credentials) token URL — the internal-only
    /// Tenant Service route, not routed by the public API Gateway (#1397 / #1406). Deriving from the
    /// effective Tenant Service URL if not explicitly set.
    /// </summary>
    public string GetServiceAuthTokenUrl() =>
        ServiceAuthTokenUrl ?? (string.IsNullOrEmpty(GetTenantServiceUrl()) ? string.Empty : $"{GetTenantServiceUrl()}/api/internal/service-auth/token");

    /// <summary>
    /// Gets the user-login URL — <c>POST /api/auth/login</c> on the Tenant Service, taking a JSON
    /// <c>{email, password}</c> body (issue #1402). Distinct from <see cref="GetAuthTokenUrl"/>,
    /// which is the OAuth2 token endpoint used only for refresh.
    /// </summary>
    public string GetUserLoginUrl() =>
        string.IsNullOrEmpty(GetTenantServiceUrl()) ? string.Empty : $"{GetTenantServiceUrl()}/api/auth/login";

    /// <summary>
    /// Gets the org-selection completion URL — <c>POST /api/auth/select-org</c> on the Tenant
    /// Service, used to complete login for a multi-org user (see <see cref="GetUserLoginUrl"/>).
    /// </summary>
    public string GetOrgSelectUrl() =>
        string.IsNullOrEmpty(GetTenantServiceUrl()) ? string.Empty : $"{GetTenantServiceUrl()}/api/auth/select-org";
}
