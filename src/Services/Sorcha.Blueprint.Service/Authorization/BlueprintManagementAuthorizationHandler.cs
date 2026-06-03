// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting; // AuthorizationPolicyExtensions.HasTierAudience
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.Blueprint.Service.Authorization;

/// <summary>
/// Succeeds a <see cref="BlueprintManagementRequirement"/> when the caller may author blueprints,
/// schemas, credential definitions, or status lists (spec 147 / review H2). Two accepted callers:
/// <list type="bullet">
///   <item>a service-tier caller (<c>token_type==service</c> carrying this installation's <c>:service</c> audience); and</item>
///   <item>a platform-tier organization member (a non-empty <c>org_id</c> claim carrying this installation's <c>:platform</c> audience).</item>
/// </list>
/// Consumer-tier tokens are refused <em>even though they carry an <c>org_id</c></em> (Feature 136): the
/// previous <c>org_id OR service</c> gate let a citizen reach authoring. The expected audience is resolved
/// from <see cref="SorchaAudiences"/> at evaluation time so per-installation namespaces are honored.
/// Never calls <see cref="AuthorizationHandlerContext.Fail()"/> — a non-match simply leaves the
/// requirement unmet, so it composes with other requirements on the same policy (e.g. an endpoint that
/// also requires <c>RequirePlatformAudience</c>).
/// </summary>
public sealed class BlueprintManagementAuthorizationHandler
    : AuthorizationHandler<BlueprintManagementRequirement>
{
    private readonly SorchaAudiences _audiences;

    /// <summary>Creates the handler with the installation's audience set (from DI).</summary>
    public BlueprintManagementAuthorizationHandler(SorchaAudiences audiences)
    {
        _audiences = audiences ?? throw new ArgumentNullException(nameof(audiences));
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, BlueprintManagementRequirement requirement)
    {
        var user = context.User;

        var isService =
            user.Claims.Any(c =>
                c.Type == TokenClaimConstants.TokenType &&
                c.Value == TokenClaimConstants.TokenTypeService) &&
            AuthorizationPolicyExtensions.HasTierAudience(user, _audiences, Tier.Service);

        var isPlatformOrgMember =
            user.Claims.Any(c => c.Type == TokenClaimConstants.OrgId && !string.IsNullOrEmpty(c.Value)) &&
            AuthorizationPolicyExtensions.HasTierAudience(user, _audiences, Tier.Platform);

        if (isService || isPlatformOrgMember)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
