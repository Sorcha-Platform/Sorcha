// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Computes semantic blueprint versions based on change type.
/// Major increments on structural changes; minor increments on documentation-only changes.
/// </summary>
public static class VersionCalculator
{
    /// <summary>
    /// Computes the next version based on previous version and change type.
    /// </summary>
    /// <param name="previousMajor">Previous major version (null for first publish)</param>
    /// <param name="previousMinor">Previous minor version (null for first publish)</param>
    /// <param name="changeType">"structural" or "documentation"</param>
    /// <returns>Tuple of (Major, Minor)</returns>
    public static (int Major, int Minor) ComputeNextVersion(int? previousMajor, int? previousMinor, string changeType)
    {
        // First publish is always v1.0
        if (previousMajor is null || previousMinor is null)
            return (1, 0);

        return changeType switch
        {
            "structural" => (previousMajor.Value + 1, 0),
            "documentation" => (previousMajor.Value, previousMinor.Value + 1),
            _ => throw new ArgumentException($"Invalid changeType: {changeType}. Must be 'structural' or 'documentation'.", nameof(changeType))
        };
    }

    /// <summary>
    /// Builds version metadata dictionary for inclusion in transaction metadata.
    /// </summary>
    public static Dictionary<string, string> BuildVersionMetadata(
        string changeType, string structuralHash, int? previousMajor, int? previousMinor)
    {
        var (major, minor) = ComputeNextVersion(previousMajor, previousMinor, changeType);

        return new Dictionary<string, string>
        {
            ["changeType"] = changeType,
            ["structuralHash"] = structuralHash,
            ["versionMajor"] = major.ToString(),
            ["versionMinor"] = minor.ToString(),
            ["previousVersionMajor"] = (previousMajor ?? 0).ToString(),
            ["previousVersionMinor"] = (previousMinor ?? 0).ToString()
        };
    }
}
