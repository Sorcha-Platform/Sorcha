// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting; // AuthorizationPolicyExtensions.HasTierAudience
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceDefaults.Auth;

namespace Sorcha.Wallet.Service.Authorization;

/// <summary>
/// Succeeds a <see cref="SystemWalletRecoveryRequirement"/> when the caller is authorized to recover
/// (import) a validator's system docket-signing wallet from a BIP39 mnemonic. Two accepted callers
/// (spec 147 / review H1):
/// <list type="bullet">
///   <item>a service-tier caller (<c>token_type==service</c> carrying this installation's <c>:service</c> audience) — forward-looking automation; and</item>
///   <item>a platform-tier administrator (an <c>Administrator</c>/<c>SystemAdmin</c> role carrying this installation's <c>:platform</c> audience) — the genesis-ceremony operator running <c>sorcha system-register import-validator-key</c>.</item>
/// </list>
/// Consumer-tier and unauthenticated callers are refused. The expected audience is resolved from
/// <see cref="SorchaAudiences"/> at evaluation time so per-installation namespaces are honored.
/// Never calls <see cref="AuthorizationHandlerContext.Fail()"/> — a non-match simply leaves the
/// requirement unmet, so it composes with any other requirement on the same policy.
/// </summary>
public sealed class SystemWalletRecoveryAuthorizationHandler
    : AuthorizationHandler<SystemWalletRecoveryRequirement>
{
    private readonly SorchaAudiences _audiences;

    /// <summary>Creates the handler with the installation's audience set (from DI).</summary>
    public SystemWalletRecoveryAuthorizationHandler(SorchaAudiences audiences)
    {
        _audiences = audiences ?? throw new ArgumentNullException(nameof(audiences));
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SystemWalletRecoveryRequirement requirement)
    {
        var user = context.User;

        var isService =
            user.Claims.Any(c =>
                c.Type == TokenClaimConstants.TokenType &&
                c.Value == TokenClaimConstants.TokenTypeService) &&
            AuthorizationPolicyExtensions.HasTierAudience(user, _audiences, Tier.Service);

        var isPlatformAdmin =
            (user.IsInRole("Administrator") || user.IsInRole("SystemAdmin")) &&
            AuthorizationPolicyExtensions.HasTierAudience(user, _audiences, Tier.Platform);

        if (isService || isPlatformAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
