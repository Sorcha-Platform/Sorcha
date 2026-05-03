// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ServiceClients.Register;

/// <summary>
/// Request sent from Tenant Service to Register Service when an organisation
/// subscribes to or unsubscribes from a register.
/// </summary>
public class SubscriptionNotificationRequest
{
    /// <summary>Identifier of the organization that owns this resource.</summary>
    [JsonPropertyName("organizationId")]
    public Guid OrganizationId { get; set; }

    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>The register name.</summary>
    [JsonPropertyName("registerName")]
    public string? RegisterName { get; set; }

    /// <summary>Free-text description of the resource.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The action.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
}

/// <summary>
/// Report from Peer Service to Register Service when sync state changes.
/// Used to update RegisterStatus based on peer sync lifecycle.
/// </summary>
public class SyncStatusReport
{
    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>The sync state.</summary>
    [JsonPropertyName("syncState")]
    public string SyncState { get; set; } = string.Empty;

    /// <summary>Flag indicating peer connection active.</summary>
    [JsonPropertyName("peerConnectionActive")]
    public bool PeerConnectionActive { get; set; } = true;
}

/// <summary>
/// Response from Register Service after processing a subscription notification.
/// </summary>
public class SubscriptionNotificationResponse
{
    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>The action.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>The sync state.</summary>
    [JsonPropertyName("syncState")]
    public string? SyncState { get; set; }

    /// <summary>Human-readable message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
