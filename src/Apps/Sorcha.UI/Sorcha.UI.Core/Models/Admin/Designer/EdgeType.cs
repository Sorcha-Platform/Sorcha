// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Visual style classification for diagram edges.
/// </summary>
public enum EdgeType
{
    /// <summary>Solid line — unconditional forward route.</summary>
    Default,

    /// <summary>Dashed line — conditional branch.</summary>
    Conditional,

    /// <summary>Red dashed line — rejection routing.</summary>
    Rejection,

    /// <summary>Curved line with loop icon — cycle back-reference.</summary>
    BackEdge,

    /// <summary>Line to END marker — workflow completion (empty nextActionIds).</summary>
    Terminal
}
