// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Encryption;

/// <summary>
/// Visual state of the crypto progress popover for an operation.
/// </summary>
public enum PopoverState
{
    /// <summary>Full floating panel with recipient list.</summary>
    Expanded,

    /// <summary>Compact pill with summary text and mini progress bar.</summary>
    Minimised,

    /// <summary>Hidden — toast notification on completion.</summary>
    Dismissed
}
