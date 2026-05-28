// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Blueprints;

/// <summary>
/// View model for blueprint list items.
/// </summary>
public record BlueprintListItemViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Status { get; init; } = "draft";
    public int ActionCount { get; init; }
    public int ParticipantCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
}

/// <summary>
/// View model for blueprint version history entries.
/// </summary>
public record BlueprintVersionViewModel
{
    public int Version { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public int ActionCount { get; init; }
    public string? ChangeDescription { get; init; }

    /// <summary>
    /// Feature 142 (T057 / US6) — the register the version was published to. Needed by the
    /// amend / clone-from-published flow to address the source. The server already returns this on
    /// the <c>GET /api/blueprints/{id}/versions</c> payload (mapped from PublishedBlueprint.RegisterId);
    /// the VM previously dropped it, so the UI had to guess. Nullable for backwards-compatibility
    /// on rows recorded before the field was populated.
    /// </summary>
    public string? RegisterId { get; init; }
}

/// <summary>
/// View model for the publish review dialog.
/// </summary>
public record PublishReviewViewModel
{
    public string BlueprintId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public List<ValidationIssue> ValidationResults { get; init; } = [];
    public bool IsValid { get; init; }
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// A single validation issue from blueprint publishing.
/// </summary>
public record ValidationIssue
{
    public string Severity { get; init; } = "error";
    public string Message { get; init; } = string.Empty;
    public string? Location { get; init; }
}
