// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Hubs;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Hubs;

/// <summary>
/// Unit tests for <see cref="TenantHubGroups"/>. Phase 7 / US5 of Feature 118 —
/// asserts the builder format is stable so it can be relied on by emit + subscribe code.
/// </summary>
public class TenantHubGroupsTests
{
    [Fact]
    public void User_ProducesNoHyphenGuidFormat()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        TenantHubGroups.User(id).Should().Be("user:11111111222233334444555555555555");
    }

    [Fact]
    public void Org_ProducesNoHyphenGuidFormat()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        TenantHubGroups.Org(id).Should().Be("org:aaaaaaaabbbbccccddddeeeeeeeeeeee");
    }

    [Fact]
    public void SystemAll_IsStableConstant()
    {
        TenantHubGroups.SystemAll.Should().Be("system:all");
    }
}
