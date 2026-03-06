// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Service interface for interacting with the Wallet Service credential endpoints.
/// </summary>
public interface ICredentialApiService
{
    /// <summary>
    /// Gets all credentials for a wallet address.
    /// </summary>
    Task<List<CredentialCardViewModel>> GetCredentialsAsync(
        string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Gets detailed credential information by ID.
    /// </summary>
    Task<CredentialDetailViewModel?> GetCredentialDetailAsync(
        string walletAddress, string credentialId, CancellationToken ct = default);

    /// <summary>
    /// Updates a credential's status (e.g., "Active" → "Revoked").
    /// </summary>
    Task<bool> UpdateCredentialStatusAsync(
        string walletAddress, string credentialId, string newStatus, CancellationToken ct = default);

    /// <summary>
    /// Deletes a credential from the wallet.
    /// </summary>
    Task<bool> DeleteCredentialAsync(
        string walletAddress, string credentialId, CancellationToken ct = default);

    /// <summary>
    /// Gets pending presentation requests targeting a wallet address.
    /// </summary>
    Task<List<PresentationRequestViewModel>> GetPresentationRequestsAsync(
        string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Gets a specific presentation request with matching credentials.
    /// </summary>
    Task<PresentationRequestViewModel?> GetPresentationRequestDetailAsync(
        string requestId, CancellationToken ct = default);

    /// <summary>
    /// Submits a presentation (approve) for a request.
    /// </summary>
    Task<PresentationSubmitResult> SubmitPresentationAsync(
        string requestId, string credentialId, List<string> disclosedClaims,
        string vpToken, CancellationToken ct = default);

    /// <summary>
    /// Denies a presentation request.
    /// </summary>
    Task<bool> DenyPresentationAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Suspends an active credential. Returns typed result with error details.
    /// </summary>
    Task<CredentialOperationResult> SuspendCredentialAsync(
        string credentialId, string issuerWallet, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Reinstates a suspended credential. Returns typed result with error details.
    /// </summary>
    Task<CredentialOperationResult> ReinstateCredentialAsync(
        string credentialId, string issuerWallet, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Refreshes an expired credential with a new expiry. Returns typed result with error details.
    /// </summary>
    Task<CredentialOperationResult> RefreshCredentialAsync(
        string credentialId, string issuerWallet, string? newExpiryDuration = null, CancellationToken ct = default);
}
