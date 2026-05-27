// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Lightweight label/description pair extracted from a JSON Schema property
/// for annotating payload fields in the Transaction Explorer and shared
/// JSON-tree components.
/// </summary>
/// <remarks>
/// Extracted from <c>BlueprintSchemaService.cs</c> as part of Feature 123.
/// Lives in <c>Services/Shared/Blueprints/</c> so user-facing components
/// such as <c>JsonTreeNode</c> and <c>JsonTreeView</c> can declare
/// parameters of this type without inheriting the
/// <c>BlueprintSchemaService</c> surface (which remains admin/designer-flavoured).
/// Namespace preserved.
/// </remarks>
public record SchemaOverlayFieldInfo(string Label, string? Description);
