// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Models.Auth;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service interface for service-to-service authentication operations.
/// </summary>
public interface IServiceAuthService
{
    /// <summary>
    /// Authenticates a service using client credentials.
    /// </summary>
    /// <param name="clientId">Client ID.</param>
    /// <param name="clientSecret">Client secret.</param>
    /// <param name="requestedScopes">Requested scopes (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token response or null if authentication fails.</returns>
    Task<TokenResponse?> AuthenticateServiceAsync(
        string clientId,
        string clientSecret,
        string? requestedScopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a service using its workload certificate as the client credential
    /// (F191 / #1420). The certificate is expected to be chain-validated at the TLS layer
    /// (mTLS listener + workload trust bundle); this method verifies the certificate's SPIFFE
    /// identity names exactly the requested client id, then applies the same principal-status
    /// and scope-intersection gating as the secret path. No shared secret is involved.
    /// </summary>
    /// <param name="clientId">Client ID the caller claims.</param>
    /// <param name="clientCertificate">TLS-layer-validated client certificate.</param>
    /// <param name="requestedScopes">Requested scopes (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token response or null if authentication fails.</returns>
    Task<TokenResponse?> AuthenticateServiceWithCertificateAsync(
        string clientId,
        System.Security.Cryptography.X509Certificates.X509Certificate2 clientCertificate,
        string? requestedScopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a service with delegated authority using its workload certificate as the
    /// client credential (F191). Same identity verification as
    /// <see cref="AuthenticateServiceWithCertificateAsync"/>, same delegation gating
    /// (<c>tenant:delegate</c> scope) as the secret path.
    /// </summary>
    /// <param name="clientId">Client ID the caller claims.</param>
    /// <param name="clientCertificate">TLS-layer-validated client certificate.</param>
    /// <param name="delegatedUserId">User ID to delegate authority for.</param>
    /// <param name="delegatedOrgId">Organization ID (optional).</param>
    /// <param name="requestedScopes">Requested scopes (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token response or null if authentication fails.</returns>
    Task<TokenResponse?> AuthenticateWithDelegationByCertificateAsync(
        string clientId,
        System.Security.Cryptography.X509Certificates.X509Certificate2 clientCertificate,
        Guid delegatedUserId,
        Guid? delegatedOrgId,
        string? requestedScopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a service with delegated authority.
    /// </summary>
    /// <param name="clientId">Client ID.</param>
    /// <param name="clientSecret">Client secret.</param>
    /// <param name="delegatedUserId">User ID to delegate authority for.</param>
    /// <param name="delegatedOrgId">Organization ID (optional).</param>
    /// <param name="requestedScopes">Requested scopes (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token response or null if authentication fails.</returns>
    Task<TokenResponse?> AuthenticateWithDelegationAsync(
        string clientId,
        string clientSecret,
        Guid delegatedUserId,
        Guid? delegatedOrgId,
        string? requestedScopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new service principal.
    /// </summary>
    /// <param name="serviceName">Unique service name.</param>
    /// <param name="scopes">Allowed scopes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Registration response with credentials (client secret shown only once).</returns>
    Task<ServicePrincipalRegistrationResponse> RegisterServicePrincipalAsync(
        string serviceName,
        string[] scopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a service principal by ID.
    /// </summary>
    /// <param name="id">Service principal ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service principal response or null if not found.</returns>
    Task<ServicePrincipalResponse?> GetServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a service principal by client ID.
    /// </summary>
    /// <param name="clientId">Client ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service principal response or null if not found.</returns>
    Task<ServicePrincipalResponse?> GetServicePrincipalByClientIdAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all service principals.
    /// </summary>
    /// <param name="includeInactive">Whether to include suspended/revoked principals.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of service principals.</returns>
    Task<ServicePrincipalListResponse> ListServicePrincipalsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a service principal's scopes.
    /// </summary>
    /// <param name="id">Service principal ID.</param>
    /// <param name="scopes">New scopes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated service principal response or null if not found.</returns>
    Task<ServicePrincipalResponse?> UpdateServicePrincipalScopesAsync(
        Guid id,
        string[] scopes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a service principal's client secret.
    /// </summary>
    /// <param name="clientId">Client ID.</param>
    /// <param name="currentSecret">Current secret for verification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New secret or null if authentication fails.</returns>
    Task<RotateSecretResponse?> RotateSecretAsync(
        string clientId,
        string currentSecret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends a service principal.
    /// </summary>
    /// <param name="id">Service principal ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> SuspendServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactivates a suspended service principal.
    /// </summary>
    /// <param name="id">Service principal ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> ReactivateServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a service principal permanently.
    /// </summary>
    /// <param name="id">Service principal ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful.</returns>
    Task<bool> RevokeServicePrincipalAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
