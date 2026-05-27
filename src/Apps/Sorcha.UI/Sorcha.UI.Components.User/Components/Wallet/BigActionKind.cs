// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Components.Wallet;

/// <summary>
/// Visual variant for <c>BigActionButton</c> on the wallet Home action pair.
/// Per the 27-May FOLLOWUP both tiles are now saturated single-hue gradients
/// of equal weight, distinguished only by hue + content alignment.
/// </summary>
public enum BigActionKind
{
    /// <summary>
    /// Blue-base saturated gradient (<c>#667eea</c>), content left-aligned.
    /// The leading action — typically Present.
    /// </summary>
    Primary,

    /// <summary>
    /// Purple-base saturated gradient (<c>#764ba2</c>), content right-aligned —
    /// the mirror peer of <see cref="Primary"/>. Historically named "Ghost" when
    /// it was a surface-fill variant; kept for API stability. The mirror pairing
    /// is intentional: icons in opposite corners, text reading toward the seam.
    /// </summary>
    Ghost
}
