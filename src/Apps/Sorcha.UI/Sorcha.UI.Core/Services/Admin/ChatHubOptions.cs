// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Configuration options for the Chat Hub connection.
/// </summary>
public class ChatHubOptions
{
    /// <summary>
    /// Base URL for the Blueprint service where the Chat Hub is hosted.
    /// Empty string means relative URLs (same origin).
    /// </summary>
    public required string BlueprintServiceUrl { get; init; }
}
