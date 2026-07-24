// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

using Refit;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client for the Blueprint Service's internal instance-repair endpoints (Feature 145 US4).
/// </summary>
/// <remarks>
/// These are <c>/api/internal/*</c> routes gated by <c>RequireService</c>, so they need a
/// service-tier token — obtained by logging in as a service principal
/// (<c>sorcha auth login --client-id … --client-secret …</c>). A normal user token gets a 403.
/// The client is deliberately CLI-local: instance repair is an operator diagnostic with no second
/// consumer, so a shared client would be indirection for its own sake. Its DTOs are still covered
/// by <c>Sorcha.Cli.ContractTests</c>.
/// </remarks>
public interface IInstanceServiceClient
{
    /// <summary>
    /// Rebuilds the instance from the register's sealed transactions and reports whether the result
    /// matches the materialized view — a read-only self-check that writes nothing.
    /// </summary>
    [Get("/api/internal/instances/{registerId}/{instanceId}/parity")]
    Task<InstanceParityResult> CheckParityAsync(
        string registerId,
        string instanceId,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Operator repair: reconstructs the instance projection from the register's sealed
    /// transactions and OVERWRITES the materialized view. Returns the rebuilt instance, or 404 when
    /// no sealed transactions exist for it.
    /// </summary>
    [Post("/api/internal/instances/{registerId}/{instanceId}/rebuild")]
    Task<RebuiltInstance> RebuildAsync(
        string registerId,
        string instanceId,
        [Header("Authorization")] string authorization);
}

/// <summary>
/// Result of an instance parity check. Mirrors the endpoint's anonymous response
/// <c>{ instanceId, registerId, inSync, detail, rebuiltState, materializedState }</c> exactly.
/// </summary>
public class InstanceParityResult
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>True when the ledger-rebuilt projection matches the materialized view.</summary>
    [JsonPropertyName("inSync")]
    public bool InSync { get; set; }

    /// <summary>Human-readable description of any divergence.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    /// <summary>State the instance would have if rebuilt from the ledger now.</summary>
    [JsonPropertyName("rebuiltState")]
    public string? RebuiltState { get; set; }

    /// <summary>State currently stored in the materialized view.</summary>
    [JsonPropertyName("materializedState")]
    public string? MaterializedState { get; set; }
}

/// <summary>
/// The stable core of a rebuilt instance, for CLI display. Deliberately a subset of the server's
/// <c>Instance</c> (and named distinctly so it is not treated as that wire type) — the repair
/// command shows identity and projection state, not the full branch/participant graph.
/// </summary>
public class RebuiltInstance
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; set; } = string.Empty;

    [JsonPropertyName("blueprintVersion")]
    public int BlueprintVersion { get; set; }

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("currentActionIds")]
    public List<int> CurrentActionIds { get; set; } = new();

    [JsonPropertyName("completedActionCount")]
    public int CompletedActionCount { get; set; }

    [JsonPropertyName("firstTransactionId")]
    public string? FirstTransactionId { get; set; }

    [JsonPropertyName("lastTransactionId")]
    public string? LastTransactionId { get; set; }
}
