// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Services;

/// <summary>
/// Records audit logs for tool invocations.
/// </summary>
public interface IToolAuditService
{
    /// <summary>
    /// Records a tool invocation.
    /// </summary>
    /// <param name="record">The audit record to log.</param>
    void RecordInvocation(ToolInvocationRecord record);

    /// <summary>
    /// Records the outcome of a tool invocation for observability (spec 139 US5).
    /// Captures the caller's trust tier, the tool name, the caller-facing outcome, and any backend
    /// status — never token material, arguments, or subject identifiers. Also emits the
    /// <c>Sorcha.Mcp</c> metrics. Safe to call from the central call-tool filter for every tool.
    /// </summary>
    /// <param name="outcome">The per-invocation outcome to record.</param>
    void RecordOutcome(ToolOutcomeRecord outcome);
}

/// <summary>
/// Represents an audit record for a tool invocation.
/// </summary>
public sealed record ToolInvocationRecord
{
    /// <summary>
    /// Unique identifier for this invocation.
    /// </summary>
    public required string InvocationId { get; init; }

    /// <summary>
    /// The session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// The user who invoked the tool.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// The name of the tool invoked.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Hash of the input parameters (for privacy).
    /// </summary>
    public string? InputHash { get; init; }

    /// <summary>
    /// Whether the invocation succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the invocation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Duration of the invocation.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// When the invocation started.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// A privacy-safe per-invocation outcome record (spec 139 US5). Carries only the trust tier,
/// tool name, caller-facing outcome, optional backend status, and duration — no token material,
/// arguments, or subject identifiers.
/// </summary>
public sealed record ToolOutcomeRecord
{
    /// <summary>The caller's trust tier (null when unresolved/unauthenticated).</summary>
    public Tier? CallerTier { get; init; }

    /// <summary>The MCP tool name (e.g. <c>sorcha_register_stats</c>).</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// The caller-facing outcome. Mirrors the tool result <c>Status</c> where one exists
    /// (e.g. <c>Success</c> / <c>Healthy</c> / <c>Unauthorized</c> / <c>Unavailable</c> /
    /// <c>Error</c>), or a filter-derived value (<c>error</c>) when the protocol result is an error.
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>
    /// Optional backend (platform) status the tool reported for its backend call, when distinct
    /// from the overall outcome. Null when the tool made no backend call or did not surface one.
    /// </summary>
    public string? BackendStatus { get; init; }

    /// <summary>Wall-clock duration of the invocation.</summary>
    public TimeSpan Duration { get; init; }
}
