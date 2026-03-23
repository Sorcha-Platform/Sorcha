// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// DTOs for register subscription operations in the UI.
/// </summary>
public class RegisterSubscriptionDto
{
    /// <summary>Subscription record ID.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    /// <summary>Organisation that owns the subscription.</summary>
    [JsonPropertyName("organization_id")]
    public Guid OrganizationId { get; set; }

    /// <summary>Register identifier (64-char hex).</summary>
    [JsonPropertyName("register_id")]
    public string RegisterId { get; set; } = "";

    /// <summary>Human-readable register name.</summary>
    [JsonPropertyName("register_name")]
    public string? RegisterName { get; set; }

    /// <summary>Subscription type: Owner, Public, Invited.</summary>
    [JsonPropertyName("subscription_type")]
    public string SubscriptionType { get; set; } = "";

    /// <summary>Subscription status: Active, Suspended, etc.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>When the subscription was created.</summary>
    [JsonPropertyName("subscribed_at")]
    public DateTimeOffset SubscribedAt { get; set; }
}

/// <summary>
/// Available register info from the peer network.
/// </summary>
public class AvailableRegisterDto
{
    /// <summary>Register identifier.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = "";

    /// <summary>Register display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Register description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Number of peers replicating this register.</summary>
    [JsonPropertyName("peerCount")]
    public int PeerCount { get; set; }

    /// <summary>Whether the register is publicly advertised.</summary>
    [JsonPropertyName("isPublic")]
    public bool IsPublic { get; set; }
}

/// <summary>
/// Service for managing register subscriptions at the tenant/org level.
/// </summary>
public interface IRegisterSubscriptionService
{
    /// <summary>
    /// Gets all registers the current user is subscribed to across their organisations.
    /// </summary>
    Task<List<RegisterSubscriptionDto>> GetMySubscribedRegistersAsync(CancellationToken ct = default);

    /// <summary>
    /// Subscribe an organisation to a register.
    /// </summary>
    Task<RegisterSubscriptionDto?> SubscribeAsync(Guid orgId, string registerId, CancellationToken ct = default);

    /// <summary>
    /// Unsubscribe an organisation from a register.
    /// </summary>
    Task<bool> UnsubscribeAsync(Guid orgId, string registerId, CancellationToken ct = default);

    /// <summary>
    /// Gets available public registers from the peer network.
    /// </summary>
    Task<List<AvailableRegisterDto>> GetAvailableRegistersAsync(CancellationToken ct = default);
}
