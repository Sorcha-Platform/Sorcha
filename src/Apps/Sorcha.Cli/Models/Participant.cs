// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Participant identity record.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.ParticipantResponse</c> (returned by the
/// participant get / create / update endpoints); the pairing is asserted by
/// <c>CliWireContractTests</c>. This previously carried <c>updatedAt</c> and a nested
/// <c>walletLinks</c> list — neither of which the response sends — and omitted <c>email</c> and the
/// <c>hasLinkedWallet</c> flag. The linked addresses come from the separate wallet-links list
/// endpoint, not this record.
/// </remarks>
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

    /// <summary>The participant's email.</summary>
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Whether the participant has at least one verified linked wallet.</summary>
    [JsonPropertyName("hasLinkedWallet")]
    public bool HasLinkedWallet { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Linked wallet address for a participant.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.LinkedWalletAddressResponse</c> (returned by
/// verify-wallet-link and the wallet-links list); the pairing is asserted by
/// <c>CliWireContractTests</c>. This previously carried <c>verifiedAt</c> (never sent) and omitted
/// algorithm / linkedAt / revokedAt.
/// </remarks>
public class LinkedWalletAddress
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;

    /// <summary>The wallet's signing algorithm.</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>When the link was established.</summary>
    [JsonPropertyName("linkedAt")]
    public DateTimeOffset LinkedAt { get; set; }

    /// <summary>When the link was revoked, if it has been.</summary>
    [JsonPropertyName("revokedAt")]
    public DateTimeOffset? RevokedAt { get; set; }
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
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.WalletLinkChallengeResponse</c>; the pairing is
/// asserted by <c>CliWireContractTests</c>. This previously read <c>nonce</c> (the server sends
/// <c>challenge</c>) and omitted walletAddress / algorithm / status — so the challenge to sign came
/// back blank and the operator had nothing to sign.
/// </remarks>
public class WalletLinkChallengeResponse
{
    [JsonPropertyName("challengeId")]
    public string ChallengeId { get; set; } = string.Empty;

    /// <summary>The challenge string the wallet must sign.</summary>
    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = string.Empty;

    /// <summary>The wallet address being linked.</summary>
    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;

    /// <summary>The signing algorithm the wallet must use.</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The challenge's current status.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Request to publish a participant to a register.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Services.PublishParticipantRequest</c> exactly; the pairing is
/// asserted by <c>CliWireContractTests</c>. This previously sent <c>name</c> and a bare
/// <c>walletAddresses</c> string list, while the server requires <c>participantName</c> and a
/// structured <c>addresses</c> list — every field <c>required</c>, so the request 400'd.
/// </remarks>
public class PublishParticipantRequest
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("participantName")]
    public string ParticipantName { get; set; } = string.Empty;

    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = string.Empty;

    /// <summary>
    /// The participant's on-register addresses. Each carries the public key and algorithm the
    /// register needs in order to encrypt to this participant, so an address cannot be published
    /// as a bare string.
    /// </summary>
    [JsonPropertyName("addresses")]
    public List<ParticipantAddressRequest> Addresses { get; set; } = new();

    [JsonPropertyName("signerWalletAddress")]
    public string SignerWalletAddress { get; set; } = string.Empty;

    /// <summary>Optional free-form metadata published alongside the participant.</summary>
    [JsonPropertyName("metadata")]
    public JsonElement? Metadata { get; set; }
}

/// <summary>
/// One address entry in a participant publish request.
/// </summary>
public class ParticipantAddressRequest
{
    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
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
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.InitiateWalletLinkRequest</c>. The server requires
/// <c>algorithm</c> (the wallet's signing algorithm); the CLI omitted it, so the request relied on
/// the server's default and could bind the wrong scheme for a non-default wallet.
/// </remarks>
public class InitiateWalletLinkRequest
{
    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;

    /// <summary>The wallet's signing algorithm, e.g. ED25519 or NISTP256.</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = string.Empty;
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
