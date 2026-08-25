// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.Agent.Configuration;

/// <summary>
/// Root configuration model for an actor definition JSON file.
/// </summary>
public record ActorDefinition
{
    public required ActorIdentity Actor { get; init; }
    public required ConnectionConfig Connection { get; init; }
    public required InboxConfig Inbox { get; init; }
    public required string Mode { get; init; } // "rules" or "ai"
    public ActorRule[]? Rules { get; init; }
    public AiConfig? Ai { get; init; }
    public ResilienceConfig? Resilience { get; init; }
    public LoggingConfig? Logging { get; init; }

    /// <summary>
    /// Optional path (relative to the actor config file) to a persona definition JSON file.
    /// When set, the agent runs a persona loop alongside its reactive inbox loop.
    /// </summary>
    public string? PersonaFile { get; init; }

    /// <summary>
    /// Optional path (relative to the actor config file) to an external-checks config JSON file
    /// (e.g. <c>assure-id.checks.json</c>). When set in rules mode, the declared checks run before
    /// rule evaluation and their results are merged into the rules context under the <c>checks</c> key.
    /// </summary>
    public string? ChecksFile { get; init; }
}

/// <summary>
/// Identity metadata for the actor.
/// </summary>
public record ActorIdentity
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Connection settings for the Sorcha platform.
/// </summary>
public record ConnectionConfig
{
    public required string GatewayUrl { get; init; }
    public required string RegisterId { get; init; }
    public required CredentialsConfig Credentials { get; init; }
    public required string WalletAddress { get; init; }
}

/// <summary>
/// Authentication credentials for the actor.
/// </summary>
public record CredentialsConfig
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required string OrganizationId { get; init; }
}

/// <summary>
/// Inbox configuration for receiving pending actions.
/// </summary>
public record InboxConfig
{
    public SignalRConfig? SignalR { get; init; }
    public PollingConfig? Polling { get; init; }

    /// <summary>
    /// Opt-in: also poll for Feature 103 OPEN starting actions of a named blueprint, so this agent
    /// can START a workflow rather than only respond to work already assigned to it (issue #1446).
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Polling"/>, and off unless configured. An open starting
    /// action has no participant bound to it yet, so it appears in nobody's pending list — including
    /// the agent that plays the open role. It is asked for by blueprint id, which is what keeps the
    /// question bounded ("which instances of the service I operate are waiting to be started?").
    /// </remarks>
    public OpenStartingConfig? OpenStarting { get; init; }
}

/// <summary>
/// Settings for polling <c>GET /api/actions/open-starting</c>.
/// </summary>
public record OpenStartingConfig
{
    /// <summary>
    /// The one place this rule is worded. Both <c>ActorDefinitionLoader.Validate</c> (so
    /// <c>sorcha-agent validate</c> catches it before a run) and the run loop report it.
    /// </summary>
    public const string BlueprintIdRequiredError =
        "inbox.openStarting.enabled is true but inbox.openStarting.blueprintId is missing — "
        + "an open-starting watch must name the blueprint this agent starts";

    public bool Enabled { get; init; }

    /// <summary>
    /// The blueprint whose open starting actions this agent will start. Required when
    /// <see cref="Enabled"/> — the endpoint refuses an unscoped query, and an agent that started
    /// arbitrary workflows would be a worse defect than the one this fixes.
    /// </summary>
    public string? BlueprintId { get; init; }

    /// <summary>
    /// Reuses the polling interval when unset, so the two loops do not drift apart by accident.
    /// </summary>
    public int? IntervalSeconds { get; init; }
}

/// <summary>
/// SignalR real-time notification settings.
/// </summary>
public record SignalRConfig
{
    public bool Enabled { get; init; }
}

/// <summary>
/// Polling-based notification settings.
/// </summary>
public record PollingConfig
{
    public bool Enabled { get; init; }
    public int IntervalSeconds { get; init; } = 60;
}

/// <summary>
/// A rule-based decision for a specific action.
/// </summary>
public record ActorRule
{
    public required string ActionName { get; init; }
    public JsonNode? Condition { get; init; }
    public required string Decision { get; init; } // "approve", "reject", "skip"
    public PreAction[]? PreActions { get; init; }
    public JsonObject? Payload { get; init; }
}

/// <summary>
/// A pre-action hook that runs before payload submission.
/// </summary>
public record PreAction
{
    public required string Type { get; init; } // "file-upload"
    public required FileUploadConfig Config { get; init; }
}

/// <summary>
/// Configuration for the file-upload pre-action.
/// </summary>
public record FileUploadConfig
{
    public required string FieldName { get; init; }
    public string? FilePath { get; init; }
    public int? SizeBytes { get; init; }
    public int? Seed { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
}

/// <summary>
/// AI decision engine configuration.
/// </summary>
public record AiConfig
{
    public required string PromptFile { get; init; }
    public string Model { get; init; } = "claude-sonnet-4-6";
    public double Temperature { get; init; } = 0.3;
}

/// <summary>
/// Resilience settings for retries and circuit breaking.
/// </summary>
public record ResilienceConfig
{
    public int RetryCount { get; init; } = 3;
    public int RetryDelaySeconds { get; init; } = 2;
    public int CircuitBreakerThreshold { get; init; } = 5;
    public int CircuitBreakerDurationSeconds { get; init; } = 30;
}

/// <summary>
/// Logging configuration for the actor.
/// </summary>
public record LoggingConfig
{
    public string Level { get; init; } = "Information";
    public string? ActionLog { get; init; }
}
