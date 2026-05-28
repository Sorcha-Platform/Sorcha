// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Models.Forms;

/// <summary>
/// Feature 142 / US5 — render mode for the production form renderer
/// (<c>SorchaFormRenderer</c>). Citizen-facing surfaces always pass
/// <see cref="Preview"/>; only designer surfaces may pass <see cref="Edit"/>.
/// </summary>
public enum FormAuthoringMode
{
    /// <summary>Default. The renderer draws fields exactly as the live service would.</summary>
    Preview = 0,

    /// <summary>
    /// Designer-only authoring mode. The renderer wraps each field in an
    /// <c>EditableFieldShell</c> overlay and exposes the layout-tools affordances
    /// alongside it. Citizens MUST NEVER see this mode.
    /// </summary>
    Edit = 1,
}
