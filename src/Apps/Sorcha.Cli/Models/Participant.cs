// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Participant identity record.
/// </summary>
public class ParticipantIdentity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("organizationId")]
    public string OrganizationId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("walletLinks")]
    public List<LinkedWalletAddress> WalletLinks { get; set; } = new();
}

/// <summary>
/// Linked wallet address for a participant.
/// </summary>
public class LinkedWalletAddress
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("verifiedAt")]
    public DateTimeOffset? VerifiedAt { get; set; }
}

/// <summary>
/// Request to register a new participant.
/// </summary>
public class RegisterParticipantRequest
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Request to update a participant.
/// </summary>
public class UpdateParticipantRequest
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>
/// Request to search for participants.
/// </summary>
public class SearchParticipantsRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>
/// Response containing a wallet link challenge.
/// </summary>
public class WalletLinkChallengeResponse
{
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Request to publish a participant to a register.
/// </summary>
public class PublishParticipantRequest
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = string.Empty;

    [JsonPropertyName("walletAddresses")]
    public List<string> WalletAddresses { get; set; } = new();

    [JsonPropertyName("signerWalletAddress")]
    public string SignerWalletAddress { get; set; } = string.Empty;
}

/// <summary>
/// Result from publishing or unpublishing a participant.
/// </summary>
public class PublishParticipantResult
{
    [JsonPropertyName("participantId")]
    public string ParticipantId { get; set; } = string.Empty;

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }
}

/// <summary>
/// Request to initiate a wallet link.
/// </summary>
public class InitiateWalletLinkRequest
{
    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;
}

/// <summary>
/// Request to verify a wallet link challenge.
/// </summary>
public class VerifyWalletLinkRequest
{
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;
}
