// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tests.Services;

/// <summary>
/// Coverage for <see cref="McpRoleNormalizer"/> — the single home for mapping a platform role
/// name onto its Sorcha MCP <c>sorcha:*</c> form. Guards against a platform role passing through
/// unmapped (the defect fixed here: <c>Consumer</c> and <c>Auditor</c> previously fell through
/// to the default arm and normalised to themselves).
/// </summary>
public sealed class McpRoleNormalizerTests
{
    [Theory]
    [InlineData("Consumer", "sorcha:participant")]
    [InlineData("Auditor", "sorcha:auditor")]
    [InlineData("Administrator", "sorcha:admin")]
    [InlineData("SystemAdmin", "sorcha:admin")]
    [InlineData("Designer", "sorcha:designer")]
    public void Normalize_MapsEveryPlatformRole(string platformRole, string expected) =>
        McpRoleNormalizer.Normalize(platformRole).Should().Be(expected);

    [Theory]
    [InlineData("sorcha:admin")]
    [InlineData("SORCHA:ADMIN")]
    public void Normalize_AlreadyPrefixed_IsLowercasedAndUnchanged(string platformRole) =>
        McpRoleNormalizer.Normalize(platformRole).Should().Be("sorcha:admin");

    [Fact]
    public void Normalize_UnknownRole_ReturnedAsIs() =>
        McpRoleNormalizer.Normalize("SomeUnknownRole").Should().Be("SomeUnknownRole");

    [Fact]
    public void NormalizeAll_DeduplicatesResult() =>
        McpRoleNormalizer.NormalizeAll(["Consumer", "consumer", "sorcha:participant"])
            .Should().ContainSingle().Which.Should().Be("sorcha:participant");

    /// <summary>
    /// Reflects over the REAL platform role enum (<see cref="Sorcha.Tenant.Service.Models.UserRole"/>)
    /// so this assertion cannot silently rot the way the original bug did: a platform role that
    /// normalises to itself can never satisfy a <c>RequiredRole</c> check, which is exactly what
    /// happened to <c>Consumer</c> and <c>Auditor</c> before this fix.
    /// </summary>
    [Fact]
    public void Normalize_CoversEveryValueOfTheRealPlatformRoleEnum() =>
        Enum.GetNames<Sorcha.Tenant.Service.Models.UserRole>()
            .Should().OnlyContain(r => McpRoleNormalizer.Normalize(r).StartsWith("sorcha:"),
                "a platform role that normalises to itself can never satisfy a RequiredRole check");
}
