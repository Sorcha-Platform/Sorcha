// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Fido2NetLib;
using Fido2NetLib.Objects;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of creating passkey registration options, containing a transaction ID
/// and the <see cref="CredentialCreateOptions"/> to send to the browser/authenticator.
/// </summary>
/// <param name="TransactionId">Unique identifier for this registration ceremony. Used to correlate the verification step.</param>
/// <param name="Options">FIDO2 credential creation options to send to the client.</param>
public record RegistrationOptionsResult(string TransactionId, CredentialCreateOptions Options);

/// <summary>
/// Result of creating passkey assertion (login) options, containing a transaction ID
/// and the <see cref="AssertionOptions"/> to send to the browser/authenticator.
/// </summary>
/// <param name="TransactionId">Unique identifier for this assertion ceremony. Used to correlate the verification step.</param>
/// <param name="Options">FIDO2 assertion options to send to the client.</param>
public record AssertionOptionsResult(string TransactionId, AssertionOptions Options);

/// <summary>
/// Result of a successful assertion (login) verification, identifying the credential owner.
/// </summary>
/// <param name="Credential">The matched passkey credential that was used for authentication.</param>
/// <param name="PlatformUserId">The platform user ID that owns the credential.</param>
public record AssertionVerificationResult(PasskeyCredential Credential, Guid PlatformUserId);

/// <summary>
/// Service interface for FIDO2/WebAuthn passkey operations.
/// Manages passkey registration, authentication, credential retrieval, and revocation.
/// </summary>
public interface IPasskeyService
{
    /// <summary>
    /// Creates registration options for a new passkey credential.
    /// Generates a challenge and stores it in the distributed cache for verification.
    /// </summary>
    /// <param name="platformUserId">The platform user ID that will own the credential.</param>
    /// <param name="displayName">Human-readable name for the credential.</param>
    /// <param name="existingCredentialIds">Existing credential IDs to exclude (prevent duplicate registration).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Registration options result containing a transaction ID and credential creation options.</returns>
    Task<RegistrationOptionsResult> CreateRegistrationOptionsAsync(
        Guid platformUserId,
        string displayName,
        IEnumerable<byte[]>? existingCredentialIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the attestation response from the authenticator and creates a new passkey credential.
    /// The challenge is consumed (one-time use) from the distributed cache.
    /// </summary>
    /// <param name="transactionId">Transaction ID from the registration options step.</param>
    /// <param name="attestationResponse">Raw attestation response from the browser/authenticator.</param>
    /// <param name="persist">When true, persists the credential to the database. When false, returns an in-memory credential object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created passkey credential.</returns>
    Task<PasskeyCredential> VerifyRegistrationAsync(
        string transactionId,
        AuthenticatorAttestationRawResponse attestationResponse,
        bool persist = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates assertion (login) options for an existing passkey credential.
    /// Generates a challenge and stores it in the distributed cache for verification.
    /// </summary>
    /// <param name="email">Optional email to help the client find matching credentials.</param>
    /// <param name="allowedCredentialIds">Optional list of credential IDs to allow for this assertion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assertion options result containing a transaction ID and assertion options.</returns>
    Task<AssertionOptionsResult> CreateAssertionOptionsAsync(
        string? email = null,
        IEnumerable<byte[]>? allowedCredentialIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the assertion response from the authenticator, authenticating the user.
    /// Performs cloned authenticator detection via signature counter regression.
    /// The challenge is consumed (one-time use) from the distributed cache.
    /// </summary>
    /// <param name="transactionId">Transaction ID from the assertion options step.</param>
    /// <param name="assertionResponse">Raw assertion response from the browser/authenticator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Assertion verification result identifying the credential owner.</returns>
    Task<AssertionVerificationResult> VerifyAssertionAsync(
        string transactionId,
        AuthenticatorAssertionRawResponse assertionResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all passkey credentials for a specific platform user.
    /// </summary>
    /// <param name="platformUserId">The platform user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of passkey credentials belonging to the platform user.</returns>
    Task<IReadOnlyList<PasskeyCredential>> GetCredentialsByOwnerAsync(
        Guid platformUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a passkey credential, preventing its future use for authentication.
    /// The credential must belong to the specified platform user.
    /// </summary>
    /// <param name="credentialId">ID of the credential to revoke.</param>
    /// <param name="platformUserId">The platform user ID that owns the credential.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the credential was found and revoked; false if not found.</returns>
    Task<bool> RevokeCredentialAsync(
        Guid credentialId,
        Guid platformUserId,
        CancellationToken cancellationToken = default);
}
