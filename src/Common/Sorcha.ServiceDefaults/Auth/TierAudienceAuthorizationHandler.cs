// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting; // AuthorizationPolicyExtensions.HasTierAudience

namespace Sorcha.ServiceDefaults.Auth;

/// <summary>
/// Authorization requirement that the caller's token carries this installation's audience for a
/// given <see cref="Tier"/> (spec 136). Paired with <see cref="TierAudienceAuthorizationHandler"/>,
/// which resolves the installation's <see cref="SorchaAudiences"/> from DI at evaluation time — so
/// the expected audience string is never baked in at registration and the same single source of
/// truth drives both issuance and validation.
/// </summary>
public sealed class TierAudienceRequirement : IAuthorizationRequirement
{
    /// <summary>The tier whose audience the token must carry.</summary>
    public Tier Tier { get; }

    /// <summary>Creates a requirement for the given <paramref name="tier"/>.</summary>
    public TierAudienceRequirement(Tier tier) => Tier = tier;
}

/// <summary>
/// Succeeds a <see cref="TierAudienceRequirement"/> when the principal carries an <c>aud</c> claim
/// equal to the installation's audience for the required tier. Resolves <see cref="SorchaAudiences"/>
/// from DI so the namespace (installation name) is the configured one. Never calls
/// <see cref="AuthorizationHandlerContext.Fail()"/> — a non-match simply leaves the requirement
/// unmet, so it composes with other requirements on the same policy.
/// </summary>
public sealed class TierAudienceAuthorizationHandler : AuthorizationHandler<TierAudienceRequirement>
{
    private readonly SorchaAudiences _audiences;

    /// <summary>Creates the handler with the installation's audience set (from DI).</summary>
    public TierAudienceAuthorizationHandler(SorchaAudiences audiences)
    {
        _audiences = audiences ?? throw new ArgumentNullException(nameof(audiences));
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TierAudienceRequirement requirement)
    {
        if (AuthorizationPolicyExtensions.HasTierAudience(context.User, _audiences, requirement.Tier))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
