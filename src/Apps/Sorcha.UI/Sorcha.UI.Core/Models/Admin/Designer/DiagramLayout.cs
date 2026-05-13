// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// The computed auto-layout result for a blueprint's visual representation.
/// </summary>
public record DiagramLayout(
    List<DiagramNode> Nodes,
    List<DiagramEdge> Edges,
    List<ParticipantInfo> ParticipantLegend,
    double Width,
    double Height);
