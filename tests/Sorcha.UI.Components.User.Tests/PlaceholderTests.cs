// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Xunit;

namespace Sorcha.UI.Components.User.Tests;

/// <summary>
/// Sentinel test — keeps the test project alive before real transport and DI-resolution
/// tests land in B3 (Feature 164). Delete once real tests arrive.
/// </summary>
public sealed class PlaceholderTests
{
    [Fact]
    public void ProjectScaffolding_Compiles() => true.Should().BeTrue();
}
