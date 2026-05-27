// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Identifies the active tab in the AI Designer unified shell.
/// </summary>
public enum DesignerTab
{
    /// <summary>AI chat pane (default).</summary>
    Ai,

    /// <summary>Blueprint diagram canvas pane.</summary>
    Diagram,

    /// <summary>Form preview pane (renders one action at a time).</summary>
    Preview
}
