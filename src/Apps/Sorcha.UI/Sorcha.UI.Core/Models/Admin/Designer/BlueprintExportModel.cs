// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Model for exported blueprint files, containing metadata and the blueprint data.
/// </summary>
public class BlueprintExportModel
{
    /// <summary>Version of the export format for compatibility checking.</summary>
    public string FormatVersion { get; set; } = "1.0";

    /// <summary>When this export was created.</summary>
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Display name of the user who exported the blueprint.</summary>
    public string? ExportedBy { get; set; }

    /// <summary>The exported blueprint data.</summary>
    public Blueprint.Models.Blueprint Blueprint { get; set; } = null!;
}
