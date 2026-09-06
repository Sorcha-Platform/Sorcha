// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;

using Sorcha.McpServer.Tools.Admin;

namespace Sorcha.McpServer.Tests.Tools;

/// <summary>
/// The Tenant Service returns <c>OrganizationListResponse { organizations, totalCount }</c>.
/// The tool used to deserialize <c>{ items, page, pageSize, totalPages }</c>, so it reported
/// "Retrieved 0 tenant(s)" against a live node holding 27 — while its own description tells an
/// agent to call it "to check whether an organisation is already provisioned before creating a
/// new one". The route is mapped, so the route gate never saw it.
/// </summary>
public class TenantListToolTests
{
    private const string ServerBody = """
        {
          "organizations": [
            { "organizationId": "00000000-0000-0000-0000-000000000001",
              "name": "Sorcha Local", "status": "Active" },
            { "organizationId": "00000000-0000-0000-0000-000000000002",
              "name": "Sorcha Public", "status": "Active" }
          ],
          "totalCount": 27
        }
        """;

    [Fact]
    public void Parse_ServerBody_ReadsOrganizationsNotItems()
    {
        var parsed = TenantListTool.ParseTenantList(ServerBody);

        parsed.Should().NotBeNull();
        parsed!.Organizations.Should().HaveCount(2);
        parsed.Organizations[0].Name.Should().Be("Sorcha Local");
        parsed.TotalCount.Should().Be(27);
    }

    [Fact]
    public void BuildQuery_UsesPageNumber_NotPage()
    {
        // The endpoint binds `pageNumber`. Sending `page` left it at its default, so a request
        // for page 3 silently returned page 1.
        var query = TenantListTool.BuildQueryString(page: 3, pageSize: 10, status: null, search: null);

        query.Should().Contain("pageNumber=3");
        query.Should().NotContain("page=3");
        query.Should().Contain("pageSize=10");
    }
}
