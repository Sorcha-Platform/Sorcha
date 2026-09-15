// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Sorcha.Wallet.Service.Authorization;

/// <summary>
/// Marker metadata stamped on every endpoint carrying the wallet-ownership gate. Exists so a test
/// can assert the gate is actually wired to each wallet-scoped route: an endpoint filter is
/// otherwise invisible to route metadata, which would make "we forgot the gate on the new group"
/// undetectable — the exact failure mode that produced G1.
/// </summary>
public sealed class WalletOwnershipRequiredMetadata
{
    /// <summary>Singleton instance; the metadata carries no state.</summary>
    public static readonly WalletOwnershipRequiredMetadata Instance = new();
}

/// <summary>Endpoint-builder extensions for the wallet-ownership gate.</summary>
public static class WalletOwnershipEndpointExtensions
{
    /// <summary>
    /// Requires that the caller owns the wallet named in the route (or is a service principal).
    /// Apply to any route group whose template contains a wallet address.
    /// </summary>
    /// <remarks>
    /// Composes with — and does not replace — the group's authorization policy.
    /// <c>CanManageWallets</c> establishes that the caller is a legitimate wallet-API caller at all;
    /// this establishes that they may act on <em>this</em> wallet.
    /// </remarks>
    /// <param name="builder">The endpoint or group builder.</param>
    /// <param name="allowOrganizationAdministrators">
    /// Also admit a current Administrator of the organisation that owns the wallet (#1643). Opt in only
    /// on routes that manage who may act for an organisation's wallet; every other route stays
    /// owner-only.
    /// </param>
    public static TBuilder RequireWalletOwnership<TBuilder>(
        this TBuilder builder, bool allowOrganizationAdministrators = false)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var denial = await WalletOwnershipGate.EvaluateAsync(
                context.HttpContext, allowOrganizationAdministrators, context.HttpContext.RequestAborted);

            return denial ?? await next(context);
        });

        return builder.WithMetadata(WalletOwnershipRequiredMetadata.Instance);
    }
}
