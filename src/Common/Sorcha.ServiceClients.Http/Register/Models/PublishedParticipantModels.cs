// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.ServiceClients.Register.Models;

/// <summary>
/// A published participant record on a register (latest version)
/// </summary>
public class PublishedParticipantRecord
{
    /// <summary>Identifier of the participant.</summary>
    public required string ParticipantId { get; init; }
    /// <summary>The organization name.</summary>
    public required string OrganizationName { get; init; }
    /// <summary>The participant name.</summary>
    public required string ParticipantName { get; init; }
    /// <summary>Current status of the resource.</summary>
    public required string Status { get; init; }
    /// <summary>Version identifier.</summary>
    public required int Version { get; init; }
    /// <summary>Identifier of the latest tx.</summary>
    public required string LatestTxId { get; init; }
    /// <summary>Collection of addresses associated with this resource.</summary>
    public required List<ParticipantAddressInfo> Addresses { get; init; }
    /// <summary>Free-form metadata associated with the resource.</summary>
    public JsonElement? Metadata { get; init; }
    /// <summary>Timestamp at which published occurred (UTC).</summary>
    public DateTimeOffset PublishedAt { get; init; }
    /// <summary>Collection of history associated with this resource.</summary>
    public List<ParticipantVersionSummary>? History { get; init; }
}

/// <summary>
/// Address entry in a published participant record
/// </summary>
public class ParticipantAddressInfo
{
    /// <summary>The wallet address.</summary>
    public required string WalletAddress { get; init; }
    /// <summary>Public key material.</summary>
    public required string PublicKey { get; init; }
    /// <summary>Cryptographic algorithm identifier.</summary>
    public required string Algorithm { get; init; }
    /// <summary>Flag indicating primary.</summary>
    public bool Primary { get; init; }
}

/// <summary>
/// Paginated list of published participants
/// </summary>
public class ParticipantPage
{
    /// <summary>One-based page number for paginated results.</summary>
    public int Page { get; init; }
    /// <summary>Number of items per page.</summary>
    public int PageSize { get; init; }
    /// <summary>Total number of items available.</summary>
    public int Total { get; init; }
    /// <summary>Collection of participants associated with this resource.</summary>
    public List<PublishedParticipantRecord> Participants { get; init; } = [];
}

/// <summary>
/// Summary of a participant version (for history)
/// </summary>
public class ParticipantVersionSummary
{
    /// <summary>Version identifier.</summary>
    public required int Version { get; init; }
    /// <summary>Identifier of the tx.</summary>
    public required string TxId { get; init; }
    /// <summary>Current status of the resource.</summary>
    public required string Status { get; init; }
    /// <summary>The participant name.</summary>
    public required string ParticipantName { get; init; }
    /// <summary>Timestamp associated with this record (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Public key resolution result
/// </summary>
public class PublicKeyResolution
{
    /// <summary>Identifier of the participant.</summary>
    public required string ParticipantId { get; init; }
    /// <summary>The participant name.</summary>
    public required string ParticipantName { get; init; }
    /// <summary>The wallet address.</summary>
    public required string WalletAddress { get; init; }
    /// <summary>Public key material.</summary>
    public required string PublicKey { get; init; }
    /// <summary>Cryptographic algorithm identifier.</summary>
    public required string Algorithm { get; init; }
    /// <summary>Current status of the resource.</summary>
    public required string Status { get; init; }
}

/// <summary>
/// Request for batch public key resolution (FR-008).
/// </summary>
public class BatchPublicKeyRequest
{
    /// <summary>Wallet addresses to resolve (1-200).</summary>
    public required string[] WalletAddresses { get; init; }

    /// <summary>Optional algorithm filter.</summary>
    public string? Algorithm { get; init; }
}

/// <summary>
/// Response from batch public key resolution.
/// Handles mixed results: some found, some not found, some revoked.
/// </summary>
public class BatchPublicKeyResponse
{
    /// <summary>Successfully resolved keys (address → resolution).</summary>
    public Dictionary<string, PublicKeyResolution> Resolved { get; init; } = new();

    /// <summary>Addresses with no published participant record.</summary>
    public string[] NotFound { get; init; } = [];

    /// <summary>Addresses whose participant record is revoked.</summary>
    public string[] Revoked { get; init; } = [];
}

/// <summary>
/// Externally-provided public key for recipients not on the register.
/// Used in action submission to bypass register lookup (FR-010).
/// </summary>
public class ExternalKeyInfo
{
    /// <summary>Base64-encoded public key.</summary>
    public required string PublicKey { get; init; }

    /// <summary>Algorithm identifier: ED25519, NISTP256, RSA4096, ML_KEM_768.</summary>
    public required string Algorithm { get; init; }
}
