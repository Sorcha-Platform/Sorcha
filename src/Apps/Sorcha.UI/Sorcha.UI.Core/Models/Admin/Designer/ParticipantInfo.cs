// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Participant metadata for the diagram colour legend.
/// </summary>
public record ParticipantInfo(string Id, string Name, string Colour)
{
    private static readonly string[] Palette =
    [
        "#1976d2", // Blue
        "#388e3c", // Green
        "#f57c00", // Orange
        "#7b1fa2", // Purple
        "#c62828", // Red
        "#00838f", // Teal
        "#ad1457", // Pink
        "#4527a0", // Deep Purple
        "#2e7d32", // Dark Green
        "#ef6c00", // Dark Orange
        "#1565c0", // Dark Blue
        "#6a1b9a"  // Violet
    ];

    /// <summary>
    /// Assigns a colour from the predefined palette based on participant index.
    /// </summary>
    public static string GetColourForIndex(int index) =>
        Palette[index % Palette.Length];
}
