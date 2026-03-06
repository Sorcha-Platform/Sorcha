// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// CLI request for submitting an action execution.
/// </summary>
public class ActionExecuteCliRequest
{
    /// <summary>
    /// The blueprint ID that defines this action.
    /// </summary>
    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; set; } = string.Empty;

    /// <summary>
    /// The action ID within the blueprint to execute.
    /// </summary>
    [JsonPropertyName("actionId")]
    public string ActionId { get; set; } = string.Empty;

    /// <summary>
    /// The instance ID of the running blueprint instance.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// The sender's wallet address for signing the transaction.
    /// </summary>
    [JsonPropertyName("senderWallet")]
    public string SenderWallet { get; set; } = string.Empty;

    /// <summary>
    /// The register address where the transaction will be recorded.
    /// </summary>
    [JsonPropertyName("registerAddress")]
    public string RegisterAddress { get; set; } = string.Empty;

    /// <summary>
    /// Optional payload data for the action.
    /// </summary>
    [JsonPropertyName("payloadData")]
    public Dictionary<string, object>? PayloadData { get; set; }
}

/// <summary>
/// CLI response from action execution.
/// </summary>
public class ActionExecuteCliResponse
{
    /// <summary>
    /// The transaction ID assigned to this execution.
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// The instance ID of the blueprint instance.
    /// </summary>
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// The operation ID for async encryption operations, if applicable.
    /// </summary>
    [JsonPropertyName("operationId")]
    public string? OperationId { get; set; }

    /// <summary>
    /// Whether this action triggered an asynchronous operation.
    /// </summary>
    [JsonPropertyName("isAsync")]
    public bool IsAsync { get; set; }

    /// <summary>
    /// Whether the action completed synchronously.
    /// </summary>
    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }
}
