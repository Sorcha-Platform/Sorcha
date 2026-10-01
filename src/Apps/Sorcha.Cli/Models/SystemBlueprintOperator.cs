// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cli.Models;

/// <summary>
/// One catalogued system blueprint's drift classification
/// (mirrors the Register Service <c>SystemBlueprintDriftEntry</c> wire shape).
/// </summary>
public record SystemBlueprintDriftEntryDto
{
    /// <summary>The system blueprint id.</summary>
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>Kebab-case drift state: in-sync, image-behind, image-ahead, missing, unknown. Display only.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The register's current publication id, when known.</summary>
    public string? CurrentPublicationTxId { get; init; }

    /// <summary>The 1-based version of the current publication, when known.</summary>
    public int? CurrentVersion { get; init; }

    /// <summary>The publication id this node's image catalogue would produce.</summary>
    public string? ImagePublicationTxId { get; init; }

    /// <summary>The published version the image's definition matches, when it matches one.</summary>
    public int? ImageMatchesVersion { get; init; }

    /// <summary>When the entry was computed.</summary>
    public DateTimeOffset? CheckedAt { get; init; }
}

/// <summary>Response of <c>GET /api/system-register/drift</c>.</summary>
public record SystemBlueprintDriftReportDto
{
    /// <summary>When the report was computed.</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>One entry per catalogued system blueprint.</summary>
    public List<SystemBlueprintDriftEntryDto> Entries { get; init; } = [];
}

/// <summary>Body of <c>POST /api/system-register/blueprints/{blueprintId}/publish</c>.</summary>
public record SystemBlueprintPublishRequestDto
{
    /// <summary>Decide, but submit nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>Publication id the operator believes is current; the server refuses (409) if it is not.</summary>
    public string? ExpectedCurrent { get; init; }
}

/// <summary>Response of a successful (200/202) operator publish.</summary>
public record SystemBlueprintPublishResultDto
{
    /// <summary>The system blueprint id.</summary>
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>Kebab-case outcome: dry-run, noop, submitted. Display only.</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Kebab-case drift state at decision time. Display only.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>The register's current publication id, when known.</summary>
    public string? CurrentPublicationTxId { get; init; }

    /// <summary>The id a publish of the image's definition produces.</summary>
    public string? CandidatePublicationTxId { get; init; }

    /// <summary>The submitted transaction id; set when the outcome is submitted.</summary>
    public string? TransactionId { get; init; }
}
