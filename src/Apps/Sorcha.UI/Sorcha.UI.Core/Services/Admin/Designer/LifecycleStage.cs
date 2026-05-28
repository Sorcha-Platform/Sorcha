// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// The four stages of the Feature 142 designer golden path, shown on the lifecycle rail.
/// Order is significant: <see cref="GoLive"/> stays locked until a full rehearsal passes.
/// </summary>
public enum LifecycleStage
{
    /// <summary>Guided AI on-ramp — turn a plain-English intent into a draft service.</summary>
    Describe,

    /// <summary>Journey-first visualisation of who does what, in order.</summary>
    Understand,

    /// <summary>Quick dry-run and full rehearsal on a sandbox register.</summary>
    Rehearse,

    /// <summary>Governed promote of the rehearsed version to a live register.</summary>
    GoLive,
}

/// <summary>
/// Lineage carried while amending an already-published service (Feature 142 / D10): the source
/// published version and the register it was published to, so re-publish targets the same register
/// with an incremented version.
/// </summary>
/// <param name="RegisterId">The live register the source version was published to.</param>
/// <param name="SourceBlueprintId">The published blueprint identity being amended.</param>
/// <param name="SourceVersion">The published version number being amended.</param>
public sealed record AmendContext(string RegisterId, string SourceBlueprintId, int SourceVersion);
