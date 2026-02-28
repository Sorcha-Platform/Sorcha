// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Tenant.Models;

using Xunit;

namespace Sorcha.Tenant.Models.Tests;

public class PermissionFlagsTests
{
    [Theory]
    [InlineData(PermissionFlags.BlockchainRead)]
    [InlineData(PermissionFlags.BlockchainWrite)]
    [InlineData(PermissionFlags.BlockchainCreate)]
    [InlineData(PermissionFlags.BlockchainDelete)]
    [InlineData(PermissionFlags.BlueprintRead)]
    [InlineData(PermissionFlags.BlueprintCreate)]
    [InlineData(PermissionFlags.BlueprintPublish)]
    [InlineData(PermissionFlags.BlueprintExecute)]
    [InlineData(PermissionFlags.WalletRead)]
    [InlineData(PermissionFlags.WalletCreate)]
    [InlineData(PermissionFlags.WalletSign)]
    [InlineData(PermissionFlags.OrganizationAdmin)]
    [InlineData(PermissionFlags.AuditLogRead)]
    [InlineData(PermissionFlags.UserManagement)]
    [InlineData(PermissionFlags.IdpConfiguration)]
    [InlineData(PermissionFlags.PermissionManagement)]
    public void HasPermission_FullAdministrator_HasEveryIndividualPermission(PermissionFlags permission)
    {
        PermissionFlags.FullAdministrator.HasPermission(permission).Should().BeTrue();
    }

    [Theory]
    [InlineData(PermissionFlags.BlockchainRead, true)]
    [InlineData(PermissionFlags.BlueprintRead, true)]
    [InlineData(PermissionFlags.BlueprintExecute, true)]
    [InlineData(PermissionFlags.WalletRead, true)]
    [InlineData(PermissionFlags.OrganizationAdmin, false)]
    [InlineData(PermissionFlags.BlockchainWrite, false)]
    [InlineData(PermissionFlags.BlockchainCreate, false)]
    [InlineData(PermissionFlags.BlockchainDelete, false)]
    [InlineData(PermissionFlags.BlueprintCreate, false)]
    [InlineData(PermissionFlags.BlueprintPublish, false)]
    [InlineData(PermissionFlags.WalletCreate, false)]
    [InlineData(PermissionFlags.WalletSign, false)]
    [InlineData(PermissionFlags.AuditLogRead, false)]
    [InlineData(PermissionFlags.UserManagement, false)]
    [InlineData(PermissionFlags.IdpConfiguration, false)]
    [InlineData(PermissionFlags.PermissionManagement, false)]
    public void HasPermission_StandardMember_HasCorrectSubset(PermissionFlags permission, bool expected)
    {
        PermissionFlags.StandardMember.HasPermission(permission).Should().Be(expected);
    }

    [Theory]
    [InlineData(PermissionFlags.BlockchainRead, true)]
    [InlineData(PermissionFlags.BlueprintRead, true)]
    [InlineData(PermissionFlags.WalletRead, true)]
    [InlineData(PermissionFlags.AuditLogRead, true)]
    [InlineData(PermissionFlags.OrganizationAdmin, false)]
    [InlineData(PermissionFlags.BlockchainWrite, false)]
    [InlineData(PermissionFlags.BlueprintExecute, false)]
    [InlineData(PermissionFlags.WalletCreate, false)]
    [InlineData(PermissionFlags.WalletSign, false)]
    [InlineData(PermissionFlags.UserManagement, false)]
    public void HasPermission_Auditor_HasCorrectSubset(PermissionFlags permission, bool expected)
    {
        PermissionFlags.Auditor.HasPermission(permission).Should().Be(expected);
    }

    [Fact]
    public void HasAnyPermission_WhenAnyMatch_ReturnsTrue()
    {
        var permissions = PermissionFlags.StandardMember;

        permissions.HasAnyPermission(PermissionFlags.OrganizationAdmin, PermissionFlags.BlockchainRead)
            .Should().BeTrue();
    }

    [Fact]
    public void HasAnyPermission_WhenNoneMatch_ReturnsFalse()
    {
        var permissions = PermissionFlags.StandardMember;

        permissions.HasAnyPermission(PermissionFlags.OrganizationAdmin, PermissionFlags.UserManagement)
            .Should().BeFalse();
    }

    [Fact]
    public void HasAllPermissions_WhenAllMatch_ReturnsTrue()
    {
        var permissions = PermissionFlags.StandardMember;

        permissions.HasAllPermissions(PermissionFlags.BlockchainRead, PermissionFlags.BlueprintRead)
            .Should().BeTrue();
    }

    [Fact]
    public void HasAllPermissions_WhenAnyMissing_ReturnsFalse()
    {
        var permissions = PermissionFlags.StandardMember;

        permissions.HasAllPermissions(PermissionFlags.BlockchainRead, PermissionFlags.OrganizationAdmin)
            .Should().BeFalse();
    }

    [Fact]
    public void None_HasNoIndividualFlags()
    {
        var none = PermissionFlags.None;

        none.HasPermission(PermissionFlags.BlockchainRead).Should().BeFalse();
        none.HasPermission(PermissionFlags.OrganizationAdmin).Should().BeFalse();
        none.HasPermission(PermissionFlags.PermissionManagement).Should().BeFalse();
        ((long)none).Should().Be(0);
    }

    [Fact]
    public void FullAdministrator_EqualsOrOfAllIndividualFlags()
    {
        var allFlags = PermissionFlags.BlockchainRead | PermissionFlags.BlockchainWrite |
                       PermissionFlags.BlockchainCreate | PermissionFlags.BlockchainDelete |
                       PermissionFlags.BlueprintRead | PermissionFlags.BlueprintCreate |
                       PermissionFlags.BlueprintPublish | PermissionFlags.BlueprintExecute |
                       PermissionFlags.WalletRead | PermissionFlags.WalletCreate |
                       PermissionFlags.WalletSign | PermissionFlags.OrganizationAdmin |
                       PermissionFlags.AuditLogRead | PermissionFlags.UserManagement |
                       PermissionFlags.IdpConfiguration | PermissionFlags.PermissionManagement;

        PermissionFlags.FullAdministrator.Should().Be(allFlags);
    }

    [Fact]
    public void StandardMember_EqualsOrOfComponentFlags()
    {
        var expected = PermissionFlags.BlockchainRead | PermissionFlags.BlueprintRead |
                       PermissionFlags.BlueprintExecute | PermissionFlags.WalletRead;

        PermissionFlags.StandardMember.Should().Be(expected);
    }

    [Fact]
    public void Auditor_EqualsOrOfComponentFlags()
    {
        var expected = PermissionFlags.BlockchainRead | PermissionFlags.BlueprintRead |
                       PermissionFlags.WalletRead | PermissionFlags.AuditLogRead;

        PermissionFlags.Auditor.Should().Be(expected);
    }

    [Theory]
    [InlineData(PermissionFlags.BlockchainRead, 1L << 0)]
    [InlineData(PermissionFlags.BlockchainWrite, 1L << 1)]
    [InlineData(PermissionFlags.BlockchainCreate, 1L << 2)]
    [InlineData(PermissionFlags.BlockchainDelete, 1L << 3)]
    [InlineData(PermissionFlags.BlueprintRead, 1L << 4)]
    [InlineData(PermissionFlags.BlueprintCreate, 1L << 5)]
    [InlineData(PermissionFlags.BlueprintPublish, 1L << 6)]
    [InlineData(PermissionFlags.BlueprintExecute, 1L << 7)]
    [InlineData(PermissionFlags.WalletRead, 1L << 8)]
    [InlineData(PermissionFlags.WalletCreate, 1L << 9)]
    [InlineData(PermissionFlags.WalletSign, 1L << 10)]
    [InlineData(PermissionFlags.OrganizationAdmin, 1L << 11)]
    [InlineData(PermissionFlags.AuditLogRead, 1L << 12)]
    [InlineData(PermissionFlags.UserManagement, 1L << 13)]
    [InlineData(PermissionFlags.IdpConfiguration, 1L << 14)]
    [InlineData(PermissionFlags.PermissionManagement, 1L << 15)]
    public void IndividualFlags_HaveCorrectBitPositions(PermissionFlags flag, long expectedValue)
    {
        ((long)flag).Should().Be(expectedValue);
    }
}
