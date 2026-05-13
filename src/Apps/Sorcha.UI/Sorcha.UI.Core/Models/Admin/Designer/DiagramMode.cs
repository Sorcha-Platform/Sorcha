// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Display mode for the unified BlueprintDiagram component.
/// </summary>
public enum DiagramMode
{
    /// <summary>
    /// Full interactive editor: drag, select, resize nodes.
    /// Uses ActionNodeWidget (unlocked).
    /// </summary>
    Edit,

    /// <summary>
    /// Read-only auto-layout with full detail: swimlanes, labels, badges.
    /// Uses ReadOnlyActionNodeWidget (locked).
    /// </summary>
    Preview,

    /// <summary>
    /// Minimal embeddable view: title-only nodes, no labels, smaller spacing.
    /// Suitable for chat panes, cards, and compact listings.
    /// </summary>
    Compact
}
