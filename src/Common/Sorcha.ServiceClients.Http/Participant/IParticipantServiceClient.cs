// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Participant;

/// <summary>
/// Client interface for Participant Service operations.
/// Used by Blueprint Service and other services to query participant information.
/// </summary>
public interface IParticipantServiceClient
{
    /// <summary>
    /// Gets a participant by ID.
    /// </summary>
    /// <param name="participantId">Participant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Participant info, or null if not found</returns>
    Task<ParticipantInfo?> GetByIdAsync(
        Guid participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a participant by user ID and organization ID.
    /// </summary>
    /// <param name="userId">User ID from Tenant Service</param>
    /// <param name="organizationId">Organization ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Participant info, or null if not found</returns>
    Task<ParticipantInfo?> GetByUserAndOrgAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a participant by wallet address.
    /// </summary>
    /// <param name="walletAddress">Wallet address string</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Participant info, or null if not found</returns>
    Task<ParticipantInfo?> GetByWalletAddressAsync(
        string walletAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a participant has signing capability (linked wallet).
    /// </summary>
    /// <param name="participantId">Participant ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signing capability result with status and warnings</returns>
    Task<SigningCapabilityResult> ValidateSigningCapabilityAsync(
        Guid participantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets linked wallet addresses for a participant.
    /// </summary>
    /// <param name="participantId">Participant ID</param>
    /// <param name="activeOnly">Only return active wallet links</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of linked wallet addresses</returns>
    Task<List<LinkedWalletInfo>> GetLinkedWalletsAsync(
        Guid participantId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-registers participant and links wallet after wallet creation.
    /// Bypasses challenge/verify flow. Non-fatal — returns result indicating success or skip reason.
    /// </summary>
    /// <param name="walletAddress">Newly created wallet address</param>
    /// <param name="userId">Authenticated user ID</param>
    /// <param name="organizationId">User's current organisation</param>
    /// <param name="publicKeyBase64">Wallet public key bytes (Base64)</param>
    /// <param name="algorithm">Wallet signing algorithm</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Auto-link result</returns>
    Task<AutoLinkResultInfo> AutoLinkWalletAsync(
        string walletAddress,
        Guid userId,
        Guid organizationId,
        string? publicKeyBase64 = null,
        string? algorithm = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Participant information returned by the service client.
/// </summary>
public record ParticipantInfo
{
    /// <summary>
    /// Unique participant identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// User ID from Tenant Service.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Organization identifier.
    /// </summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// Display name for the participant.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Participant status (Active, Suspended, Inactive).
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Whether the participant has at least one active linked wallet.
    /// </summary>
    public bool HasLinkedWallet { get; init; }

    /// <summary>
    /// Registration timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last modification timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Result of signing capability validation.
/// </summary>
public record SigningCapabilityResult
{
    /// <summary>
    /// Whether the participant can sign transactions.
    /// </summary>
    public required bool CanSign { get; init; }

    /// <summary>
    /// Participant status (Active, Suspended, Inactive).
    /// </summary>
    public required string ParticipantStatus { get; init; }

    /// <summary>
    /// Number of active linked wallets.
    /// </summary>
    public int ActiveWalletCount { get; init; }

    /// <summary>
    /// Warning messages (e.g., "Participant has no linked wallet").
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Error message if participant not found or other issues.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Linked wallet address information.
/// </summary>
public record LinkedWalletInfo
{
    /// <summary>
    /// Unique wallet link identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Wallet address string.
    /// </summary>
    public required string WalletAddress { get; init; }

    /// <summary>
    /// Cryptographic algorithm (ED25519, NIST-P256, RSA-4096).
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// Link status (Active, Revoked).
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// When the wallet was linked.
    /// </summary>
    public DateTimeOffset LinkedAt { get; init; }

    /// <summary>
    /// When the link was revoked (if applicable).
    /// </summary>
    public DateTimeOffset? RevokedAt { get; init; }
}

/// <summary>
/// Result of an auto-link wallet operation.
/// </summary>
public record AutoLinkResultInfo
{
    /// <summary>Flag indicating participant created.</summary>
    public bool ParticipantCreated { get; init; }
    /// <summary>Flag indicating wallet linked.</summary>
    public bool WalletLinked { get; init; }
    /// <summary>Identifier of the participant.</summary>
    public Guid? ParticipantId { get; init; }
    /// <summary>The skip reason.</summary>
    public string? SkipReason { get; init; }
}
