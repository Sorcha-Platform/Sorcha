// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Extensions;

/// <summary>
/// Shared JSON serializer options for API deserialization across UI services.
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// Standard options for deserializing API responses (case-insensitive property matching).
    /// </summary>
    public static readonly JsonSerializerOptions Api = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
