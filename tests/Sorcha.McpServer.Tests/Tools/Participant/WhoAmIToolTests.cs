// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Participant;
using Sorcha.ServiceClients.Tenant;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.McpServer.Tests.Tools.Participant;

/// <summary>
/// #1705 — an agent could not see which organisation its own token acts for, and spent four calls
/// inferring it from an audit-log entry. These pin that whoami reports the token's own facts, labels
/// the two user ids separately, and never fills a missing platform_user_id in from sub.
/// </summary>
public class WhoAmIToolTests
{
    private const string OrgId = "1b63ebc9-fa8c-41e5-b5c6-6dff5a21c545";

    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<ICallerContext> _caller = new();
    private readonly Mock<ITenantServiceClient> _tenant = new();

    private WhoAmITool Tool() => new(_auth.Object, _caller.Object, _tenant.Object, Mock.Of<ILogger<WhoAmITool>>());

    private static string Token(params Claim[] claims)
    {
        var key = new SymmetricSecurityKey(new byte[32]);
        var jwt = new JwtSecurityToken(
            issuer: "urn:sorcha:test", audience: "test:platform", claims: claims,
            expires: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private void Session(string token, params string[] mcpRoles)
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_whoami")).Returns(true);
        _caller.Setup(c => c.RawToken).Returns(token);
        _caller.Setup(c => c.Tier).Returns(Tier.Platform);
        _caller.Setup(c => c.Roles).Returns(mcpRoles);
    }

    [Fact]
    public async Task WhoAmI_ReportsTheTokensOrganisationRolesAndBothIdsSeparately()
    {
        Session(Token(
                new Claim("org_id", OrgId),
                new Claim("sub", "11111111-1111-1111-1111-111111111111"),
                new Claim("platform_user_id", "22222222-2222-2222-2222-222222222222"),
                new Claim("role", "Administrator"),
                new Claim("role", "Designer"),
                new Claim("email", "a@example.test")),
            "sorcha:admin", "sorcha:designer");
        _tenant.Setup(t => t.GetOrganizationAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"id":"x","name":"Cold-start Run 8 Provider"}""");

        var result = await Tool().WhoAmIAsync();

        result.Status.Should().Be("Success");
        result.OrganizationId.Should().Be(OrgId);
        result.OrganizationName.Should().Be("Cold-start Run 8 Provider");
        result.Roles.Should().BeEquivalentTo(["Administrator", "Designer"]);
        result.McpRoles.Should().BeEquivalentTo(["sorcha:admin", "sorcha:designer"]);
        result.UserIdentityId.Should().Be("11111111-1111-1111-1111-111111111111");
        result.PlatformUserId.Should().Be("22222222-2222-2222-2222-222222222222");
        result.Email.Should().Be("a@example.test");
        result.Tier.Should().Be("Platform");
        result.TokenExpiresAt.Should().Be(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        result.VisibleTools.Should().Contain(["sorcha_whoami", "sorcha_blueprint_create", "sorcha_blueprint_publish"]);
        result.Message.Should().Contain("Cold-start Run 8 Provider").And.Contain("Administrator");
    }

    [Fact]
    public async Task WhoAmI_MissingPlatformUserIdClaim_IsReportedMissing_NotFilledFromSub()
    {
        // CLAUDE.md §26: the two ids are different kinds. ICallerContext.PlatformUserId falls back
        // to sub for wallet lookups; a report that did the same would state a false fact.
        Session(Token(new Claim("org_id", OrgId), new Claim("sub", "11111111-1111-1111-1111-111111111111")));

        var result = await Tool().WhoAmIAsync();

        result.UserIdentityId.Should().Be("11111111-1111-1111-1111-111111111111");
        result.PlatformUserId.Should().BeNull();
    }

    [Fact]
    public async Task WhoAmI_OrganisationNameUnreadable_StillReportsTheIdFromTheToken()
    {
        Session(Token(new Claim("org_id", OrgId)));
        _tenant.Setup(t => t.GetOrganizationAsync(OrgId, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var result = await Tool().WhoAmIAsync();

        result.Status.Should().Be("Success");
        result.OrganizationId.Should().Be(OrgId);
        result.OrganizationName.Should().BeNull();
        result.OrganizationNameNote.Should().Contain("authoritative");
    }

    [Fact]
    public async Task WhoAmI_NoToken_IsUnauthorized()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_whoami")).Returns(true);
        _caller.Setup(c => c.RawToken).Returns((string?)null);

        var result = await Tool().WhoAmIAsync();

        result.Status.Should().Be("Unauthorized");
        _tenant.VerifyNoOtherCalls();
    }
}
