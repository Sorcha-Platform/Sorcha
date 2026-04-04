// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// Client interface for acquiring delegation tokens from the Tenant Service.
/// Delegation tokens allow a service to act on behalf of a user.
/// </summary>
/// <remarks>
/// The delegation token contains both the service identity (token_type=service, service_name)
/// and the delegated user identity (delegated_user_id, delegated_org_id).
/// Tokens are short-lived (5 minutes) and are NOT cached — each delegation
/// request creates a fresh token scoped to the specific user and operation.
/// </remarks>
public interface IDelegationTokenClient
{
    /// <summary>
    /// Acquires a delegation token to act on behalf of a user.
    /// </summary>
    /// <param name="userAccessToken">The user's valid JWT access token.</param>
    /// <param name="scopes">The scopes requested for the delegation (e.g., "wallets:sign").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A delegation JWT token, or null if acquisition failed.</returns>
    Task<string?> GetDelegationTokenAsync(
        string userAccessToken,
        string[] scopes,
        CancellationToken cancellationToken = default);
}
