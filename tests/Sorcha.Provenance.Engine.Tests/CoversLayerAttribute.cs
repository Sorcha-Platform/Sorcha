// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine.Tests;

/// <summary>
/// Declares that a test class exercises a given <see cref="ProvenanceLayer"/>.
/// </summary>
/// <remarks>
/// Read by <see cref="ProvenanceLayerCoverageTests"/>. Most check-test classes are matched by name
/// (<c>SealCheckTests</c> covers <see cref="ProvenanceLayer.Seal"/>); this attribute is for the ones
/// deliberately named after the behaviour they pin rather than the layer — <c>RosterAsOfTests</c>
/// being the important case, since renaming it to match the convention would bury the single most
/// valuable test in the feature under a generic name.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal sealed class CoversLayerAttribute(ProvenanceLayer layer) : Attribute
{
    /// <summary>The layer this test class exercises.</summary>
    public ProvenanceLayer Layer { get; } = layer;
}
