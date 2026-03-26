// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Service interface for admin-level issued credential management.
/// </summary>
public interface IIssuedCredentialService
{
    /// <summary>
    /// Gets all credentials issued by the current organisation, with optional filtering.
    /// </summary>
    /// <param name="statusFilter">Filter by credential status (e.g. Active, Suspended, Revoked).</param>
    /// <param name="typeFilter">Filter by credential type.</param>
    Task<List<IssuedCredentialListItem>> GetIssuedCredentialsAsync(
        string? statusFilter = null, string? typeFilter = null);

    /// <summary>
    /// Suspends an active credential, preventing it from being presented.
    /// </summary>
    /// <param name="credentialId">The credential to suspend.</param>
    /// <param name="reason">Reason for the suspension.</param>
    Task<CredentialOperationResult> SuspendCredentialAsync(string credentialId, string reason);

    /// <summary>
    /// Permanently revokes a credential. This action cannot be undone.
    /// </summary>
    /// <param name="credentialId">The credential to revoke.</param>
    /// <param name="reason">Reason for revocation.</param>
    Task<CredentialOperationResult> RevokeCredentialAsync(string credentialId, string reason);

    /// <summary>
    /// Reinstates a previously suspended credential.
    /// </summary>
    /// <param name="credentialId">The credential to reinstate.</param>
    Task<CredentialOperationResult> ReinstateCredentialAsync(string credentialId);

    /// <summary>
    /// Refreshes an expired credential with a new expiry date.
    /// </summary>
    /// <param name="credentialId">The credential to refresh.</param>
    Task<CredentialOperationResult> RefreshCredentialAsync(string credentialId);
}
