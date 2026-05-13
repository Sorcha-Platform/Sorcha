// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// A directed connection between action nodes in the diagram.
/// </summary>
public record DiagramEdge(
    int SourceActionId,
    int TargetActionId,
    string? RouteId,
    EdgeType EdgeType,
    string? Label,
    bool IsBackEdge);
