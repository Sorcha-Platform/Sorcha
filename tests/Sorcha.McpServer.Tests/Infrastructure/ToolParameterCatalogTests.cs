// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.McpServer.Tools.Participant;

namespace Sorcha.McpServer.Tests.Infrastructure;

/// <summary>
/// #1685(b). <see cref="ToolParameterCatalog"/> is the shared lookup the argument-binding-error
/// filter uses to name every parameter a tool actually binds, so a wrong first guess (cold-start
/// run #7: <c>orgId</c> instead of <c>organizationId</c>) is corrected on that same call instead
/// of costing a second one.
/// </summary>
public class ToolParameterCatalogTests
{
    [Fact]
    public void DescribeParameters_KnownToolWithSeveralParameters_ListsEveryOneByItsRealName()
    {
        var description = ToolParameterCatalog.DescribeParameters("sorcha_user_list");

        description.Should().NotBeNull();
        description!.Should().Contain("organizationId");
        description.Should().Contain("includeInactive");
        description.Should().Contain("emailVerified");
        description.Should().Contain("provisionedVia");
        description.Should().Contain("includePending");

        // The wrong name the agent actually guessed must NOT appear as if it were correct.
        description.Should().NotContain("orgId (");
    }

    [Fact]
    public void DescribeParameters_MarksTheRequiredParameterAsRequired()
    {
        var description = ToolParameterCatalog.DescribeParameters("sorcha_user_list");

        description.Should().Contain("organizationId (string, required)");
    }

    [Fact]
    public void DescribeParameters_MarksAnOptionalParameterWithItsDefault()
    {
        var description = ToolParameterCatalog.DescribeParameters("sorcha_user_list");

        description.Should().Contain("includeInactive (bool, optional, default false)");
    }

    [Fact]
    public void DescribeParameters_ExcludesTheCancellationToken()
    {
        var description = ToolParameterCatalog.DescribeParameters("sorcha_wallet_info");

        description.Should().NotContain("cancellationToken", "the SDK supplies it — an agent cannot pass it");
    }

    [Fact]
    public void DescribeParameters_UnknownToolName_ReturnsNull()
    {
        var description = ToolParameterCatalog.DescribeParameters("sorcha_does_not_exist");

        description.Should().BeNull();
    }

    [Fact]
    public void Build_FindsToolsFromBothAdminAndParticipantNamespaces()
    {
        // Sanity check the reflection walks the WHOLE assembly, not one namespace — a filter
        // scoped to `typeof(UserListTool).Assembly` would silently miss nothing here since both
        // types are in the same assembly, but this pins that assumption explicitly.
        var catalog = ToolParameterCatalog.Build(typeof(UserListTool).Assembly);

        catalog.Should().ContainKey("sorcha_user_list");
        catalog.Should().ContainKey("sorcha_wallet_info");
        catalog["sorcha_wallet_info"].Should().ContainSingle(p => p.Name == "walletAddress");
    }
}
