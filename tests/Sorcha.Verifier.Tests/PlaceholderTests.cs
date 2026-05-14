// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Xunit;

namespace Sorcha.Verifier.Tests;

/// <summary>
/// Sentinel test — keeps the test project alive in CI before real verifier
/// pipeline tests land in Phase 3 / US1 (T088-T091). Delete once real tests arrive.
/// </summary>
public sealed class PlaceholderTests
{
    [Fact]
    public void ProjectScaffolding_Compiles() => true.Should().BeTrue();
}
