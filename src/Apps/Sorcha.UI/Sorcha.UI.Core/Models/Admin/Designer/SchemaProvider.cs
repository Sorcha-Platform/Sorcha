// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Represents an external schema provider source
/// </summary>
public class SchemaProvider
{
    /// <summary>
    /// Unique identifier for the provider
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name of the provider
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Base URI for the schema provider
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// Whether this provider is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Whether this is a built-in provider (cannot be deleted)
    /// </summary>
    public bool IsBuiltIn { get; set; } = false;

    /// <summary>
    /// Description of the provider
    /// </summary>
    public string Description { get; set; } = string.Empty;
}
