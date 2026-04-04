// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Service.Services.Implementation;

using Xunit;

namespace Sorcha.Wallet.Core.Tests;

public class DerivationPathBuilderTests
{
    private static readonly Guid TestOrgId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Build_WithValidInputs_ReturnsCorrectFormat()
    {
        var path = DerivationPathBuilder.Build(TestOrgId, 0, TestUserId, KeyUsage.Identity, 0);

        path.Should().StartWith($"m/{DerivationPathBuilder.SorchaPurpose}'/");

        var levels = path.Split('/');
        levels.Should().HaveCount(7); // m, purpose, org, dept, user, usage, index
    }

    [Fact]
    public void Build_SameInputs_ReturnsSamePath()
    {
        var path1 = DerivationPathBuilder.Build(TestOrgId, 1, TestUserId, KeyUsage.VCIssuance, 0);
        var path2 = DerivationPathBuilder.Build(TestOrgId, 1, TestUserId, KeyUsage.VCIssuance, 0);

        path1.Should().Be(path2);
    }

    [Fact]
    public void Build_DifferentUsers_ReturnsDifferentPaths()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var pathA = DerivationPathBuilder.Build(TestOrgId, 0, userA, KeyUsage.Identity, 0);
        var pathB = DerivationPathBuilder.Build(TestOrgId, 0, userB, KeyUsage.Identity, 0);

        pathA.Should().NotBe(pathB);
    }

    [Fact]
    public void Build_DefaultDepartment_UsesZero()
    {
        var path = DerivationPathBuilder.Build(TestOrgId, 0, TestUserId, KeyUsage.Identity, 0);

        var levels = path.Split('/');
        levels[3].Should().Be("0'", "department 0 should appear as 0' (hardened)");
    }

    [Fact]
    public void Build_AllKeyUsages_ProduceUniquePaths()
    {
        var usages = Enum.GetValues<KeyUsage>();
        usages.Should().HaveCount(5, "expected 5 KeyUsage enum values");

        var paths = usages
            .Select(u => DerivationPathBuilder.Build(TestOrgId, 0, TestUserId, u, 0))
            .ToList();

        paths.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GuidToDerivationIndex_Deterministic()
    {
        var guid = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var index1 = DerivationPathBuilder.GuidToDerivationIndex(guid);
        var index2 = DerivationPathBuilder.GuidToDerivationIndex(guid);

        index1.Should().Be(index2);
    }

    [Fact]
    public void GuidToDerivationIndex_FitsIn31Bits()
    {
        // Test with multiple GUIDs to increase confidence
        for (var i = 0; i < 100; i++)
        {
            var index = DerivationPathBuilder.GuidToDerivationIndex(Guid.NewGuid());

            index.Should().BeLessThanOrEqualTo(0x7FFFFFFF,
                "derivation index must fit in 31 bits to avoid hardened range");
        }
    }

    [Fact]
    public void Build_DoesNotCollideWithBip44()
    {
        // Sorcha purpose constant must not collide with standard BIP purposes
        uint[] standardPurposes = [44, 49, 84, 86];

        foreach (var purpose in standardPurposes)
        {
            DerivationPathBuilder.SorchaPurpose.Should().NotBe(purpose,
                $"Sorcha purpose must not collide with BIP purpose {purpose}");
        }
    }
}
