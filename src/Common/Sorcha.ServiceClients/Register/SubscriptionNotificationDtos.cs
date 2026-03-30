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
    [JsonPropertyName("organizationId")]
    public Guid OrganizationId { get; set; }

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("registerName")]
    public string? RegisterName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;
}

/// <summary>
/// Response from Register Service after processing a subscription notification.
/// </summary>
public class SubscriptionNotificationResponse
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("syncState")]
    public string? SyncState { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
