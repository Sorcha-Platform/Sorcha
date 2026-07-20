// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

namespace Sorcha.UI.Core.Utilities;

/// <summary>
/// The single source of the version string shown anywhere in a Sorcha UI.
///
/// Every component of the platform is stamped with ONE build-time-derived version by the root
/// <c>Directory.Build.props</c> — <c>2.&lt;GITHUB_RUN_NUMBER&gt;.&lt;GITHUB_RUN_ATTEMPT&gt;</c> on CI,
/// <c>2.0.0-dev</c> locally. Surfaces used to each resolve (or hardcode) that independently, so
/// <c>/app</c> could show three different answers on one page load: the footer read the assembly
/// attribute, Help hardcoded <c>2.0.0-preview</c>, Settings hardcoded <c>1.0.0-preview</c> next to a
/// "build number" that was really <c>DateTime.Now</c> and therefore changed on every render. Reading
/// the version through this one helper is what keeps them agreeing.
///
/// Resolved from this assembly rather than the caller's on purpose: this is the *platform* version,
/// not a per-assembly one, and every assembly inherits the same value anyway — so a shared read gives
/// the same answer from the web client, a shared component library, or the PWA.
///
/// A <c>-dev</c> value on a deployed artefact is a BUG, not a local build: it means the image's
/// Dockerfile is missing the <c>ARG</c>/<c>ENV GITHUB_RUN_NUMBER</c> block, so CI's build-args were
/// silently dropped (docker build-args are opt-in) and the tag no longer matches the payload.
/// </summary>
public static class SorchaVersion
{
    /// <summary>
    /// The display version, e.g. <c>2.838.1</c> or <c>2.0.0-dev</c>. Any <c>+&lt;commit&gt;</c> source-revision
    /// suffix is trimmed. Falls back to the numeric assembly version, then <c>"unknown"</c>.
    /// </summary>
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(SorchaVersion).Assembly;

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Split('+')[0];

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
