// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.ServiceClients.Register.Models;

namespace Sorcha.Blueprint.Service.Models.Requests;

/// <summary>
/// Request to submit an action for execution
/// </summary>
public record ActionSubmissionRequest
{
    /// <summary>
    /// The blueprint ID
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// The action ID within the blueprint
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// The workflow instance ID (optional, will be generated if not provided for first action)
    /// </summary>
    public string? InstanceId { get; init; }

    /// <summary>
    /// Hash of the previous transaction in the workflow (optional, for action chaining)
    /// </summary>
    public string? PreviousTransactionHash { get; init; }

    /// <summary>
    /// The wallet address of the submitter
    /// </summary>
    public required string SenderWallet { get; init; }

    /// <summary>
    /// The register address where the transaction will be submitted
    /// </summary>
    public required string RegisterAddress { get; init; }

    /// <summary>
    /// The action payload data
    /// </summary>
    public required Dictionary<string, object> PayloadData { get; init; }

    /// <summary>
    /// Credential presentations to satisfy action credential requirements.
    /// Required when the action has credential requirements defined.
    /// </summary>
    public List<CredentialPresentation>? CredentialPresentations { get; init; }

    /// <summary>
    /// Externally-provided public keys for recipients not on the register.
    /// Maps wallet address to key info. Used to bypass register lookup (FR-010).
    /// When null or empty, all recipient keys will be resolved from the register in US4.
    /// For US1 MVP, this is the primary key source until automatic register resolution is implemented.
    /// </summary>
    public Dictionary<string, ExternalKeyInfo>? ExternalRecipientKeys { get; init; }

    /// <summary>
    /// Optional file attachments
    /// </summary>
    public List<FileAttachment>? Files { get; init; }
}

/// <summary>
/// Represents a file attachment for an action
/// </summary>
public record FileAttachment
{
    /// <summary>
    /// The file name
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The content type (MIME type)
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Base64-encoded file content
    /// </summary>
    public required string ContentBase64 { get; init; }
}
