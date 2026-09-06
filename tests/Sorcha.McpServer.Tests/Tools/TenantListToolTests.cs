// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;

using Sorcha.McpServer.Tools.Admin;

namespace Sorcha.McpServer.Tests.Tools;

/// <summary>
/// The Tenant Service returns <c>OrganizationListResponse { organizations, totalCount }</c>,
/// where each element is an <c>OrganizationResponse</c> keyed by <c>id</c> — NOT
/// <c>organizationId</c> (see <c>src/Services/Sorcha.Tenant.Service/Models/Dtos/
/// OrganizationDtos.cs</c>). Two bugs, one envelope-level and one element-level: the tool used to
/// deserialize <c>{ items, page, pageSize, totalPages }</c> (envelope), so it reported "Retrieved
/// 0 tenant(s)" against a live node holding 27; even fixed to read <c>organizations</c>, a DTO
/// keyed on <c>organizationId</c> still silently drops every id, because the server never sends
/// that property name. This fixture uses the REAL server field (<c>id</c>) so it fails against
/// either mistake, not just the first one. The tool's own description tells an agent to call it
/// "to discover a tenant ID" and "to check whether an organisation is already provisioned before
/// creating a new one" — both promises are broken by a silently-blank id.
/// </summary>
public class TenantListToolTests
{
    private const string ServerBody = """
        {
          "organizations": [
            { "id": "00000000-0000-0000-0000-000000000001",
              "name": "Sorcha Local", "status": "Active" },
            { "id": "00000000-0000-0000-0000-000000000002",
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
    public void Parse_ServerBody_ReadsIdNotOrganizationId()
    {
        // The server's OrganizationResponse serializes the id as "id". A DTO keyed on
        // "organizationId" (the tool's original, wrong assumption) deserializes this fixture's
        // "id" field into nothing, leaving Id null — this assertion fails against that DTO shape.
        var parsed = TenantListTool.ParseTenantList(ServerBody);

        parsed.Should().NotBeNull();
        parsed!.Organizations[0].Id.Should().Be("00000000-0000-0000-0000-000000000001");
        parsed.Organizations[1].Id.Should().Be("00000000-0000-0000-0000-000000000002");
    }

    [Fact]
    public void BuildQuery_UsesPageNumber_NotPage()
    {
        // The endpoint binds `pageNumber`. Sending `page` left it at its default, so a request
        // for page 3 silently returned page 1.
        var query = TenantListTool.BuildQueryString(page: 3, pageSize: 10);

        query.Should().Contain("pageNumber=3");
        query.Should().NotContain("page=3");
        query.Should().Contain("pageSize=10");
    }

    [Fact]
    public void BuildQuery_NeverIncludesStatusOrSearch()
    {
        // ListOrganizations binds only includeInactive/pageNumber/pageSize — status and search
        // are not server parameters at all, so the tool must not send them as dead query params.
        var query = TenantListTool.BuildQueryString(page: 1, pageSize: 20);

        query.Should().NotContain("status=");
        query.Should().NotContain("search=");
    }
}
