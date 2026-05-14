// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests;

/// <summary>
/// Sentinel test — keeps the test project alive in CI before real wallet PWA
/// services land in Phase 2 (T054-T074). Delete once real tests arrive.
/// </summary>
public sealed class PlaceholderTests
{
    [Fact]
    public void ProjectScaffolding_Compiles() => true.Should().BeTrue();
}
