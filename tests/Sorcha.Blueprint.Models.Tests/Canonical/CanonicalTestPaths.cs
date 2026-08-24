// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

namespace Sorcha.Blueprint.Models.Tests.Canonical;

/// <summary>
/// Locates the committed fixtures used by the canonical-form and golden-vector tests.
/// </summary>
/// <remarks>
/// Resolves through the repo root rather than the build output, matching
/// <c>BlueprintCorpusFreshnessTests</c>. The fixture is therefore read from source, so a change to
/// it is visible in review as a change to the file the golden vector is frozen against — which is
/// the point of freezing it.
/// </remarks>
internal static class CanonicalTestPaths
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to locate the repo root");
        return dir!.FullName;
    }

    /// <summary>
    /// The golden-vector fixture. Deliberately broad, and deliberately authored with its keys out of
    /// alphabetical order so the canonicaliser's sorting is genuinely exercised.
    /// </summary>
    internal static string GoldenBlueprintJson()
    {
        var path = Path.Combine(
            RepoRoot(),
            "tests", "Sorcha.Blueprint.Models.Tests", "Canonical", "Fixtures", "golden-blueprint.json");

        File.Exists(path).Should().BeTrue(
            "the golden vector is meaningless without its fixture — a missing file must fail loudly " +
            "rather than let the test pass over an empty string");

        return File.ReadAllText(path);
    }
}
