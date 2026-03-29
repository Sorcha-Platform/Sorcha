// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

namespace Sorcha.Peer.Service;

/// <summary>
/// Embedded build information, populated at compile time via MSBuild properties.
/// The informational version includes the git commit hash when built from a git repository.
/// </summary>
public static class BuildInfo
{
    /// <summary>Assembly informational version (includes +GitCommitHash when available).</summary>
    public static string Version { get; } = typeof(BuildInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";

    /// <summary>Short git commit hash extracted from the informational version, or "dev" if unavailable.</summary>
    public static string CommitHash => Version.Contains('+')
        ? Version.Split('+')[1]
        : "dev";
}
