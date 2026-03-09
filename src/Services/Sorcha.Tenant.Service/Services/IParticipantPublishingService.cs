// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Publishes participant identity records as transactions on a register.
/// </summary>
public interface IParticipantPublishingService
{
    /// <summary>
    /// Publishes a new participant record to a register.
    /// Generates a UUID participantId, builds the transaction, signs with the signer's wallet,
    /// and submits via the validator pipeline.
    /// </summary>
    Task<ParticipantPublishResult> PublishParticipantAsync(
        PublishParticipantRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing participant record by publishing a new version.
    /// Increments the version, chains from the previous version's TxId,
    /// and submits via the validator pipeline.
    /// </summary>
    Task<ParticipantPublishResult> UpdateParticipantAsync(
        UpdatePublishedParticipantRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a participant record by publishing a new version with status "Revoked".
    /// </summary>
    Task<ParticipantPublishResult> RevokeParticipantAsync(
        RevokeParticipantRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to publish a participant record to a register
/// </summary>
public record PublishParticipantRequest
{
    public required string RegisterId { get; init; }
    public required string ParticipantName { get; init; }
    public required string OrganizationName { get; init; }
    public required List<ParticipantAddressRequest> Addresses { get; init; }
    public required string SignerWalletAddress { get; init; }
    public JsonElement? Metadata { get; init; }
}

/// <summary>
/// Address entry in a publish request
/// </summary>
public record ParticipantAddressRequest
{
    public required string WalletAddress { get; init; }
    public required string PublicKey { get; init; }
    public required string Algorithm { get; init; }
    public bool Primary { get; init; }
}

/// <summary>
/// Request to update an existing participant record
/// </summary>
public record UpdatePublishedParticipantRequest
{
    public required string RegisterId { get; init; }
    public required string ParticipantId { get; init; }
    public required string ParticipantName { get; init; }
    public required string OrganizationName { get; init; }
    public required List<ParticipantAddressRequest> Addresses { get; init; }
    public required string SignerWalletAddress { get; init; }
    public string? Status { get; init; }
    public JsonElement? Metadata { get; init; }
}

/// <summary>
/// Request to revoke a participant record
/// </summary>
public record RevokeParticipantRequest
{
    public required string RegisterId { get; init; }
    public required string ParticipantId { get; init; }
    public required string SignerWalletAddress { get; init; }
}

/// <summary>
/// Result of publishing a participant record
/// </summary>
public record ParticipantPublishResult
{
    /// <summary>Transaction ID of the published record.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Participant identifier.</summary>
    public required string ParticipantId { get; init; }

    /// <summary>Register ID where the record was published.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Version number of the published record.</summary>
    public required int Version { get; init; }

    /// <summary>Publication status.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable result message.</summary>
    public required string Message { get; init; }
}
