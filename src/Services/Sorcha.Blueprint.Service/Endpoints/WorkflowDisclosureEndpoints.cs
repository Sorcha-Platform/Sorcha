// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Middleware;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Feature 176 — the disclosed prior-action data query surface. Fills the route already targeted by
/// <c>IBlueprintServiceClient.GetDisclosedDataAsync</c> and the MCP <c>DisclosedDataTool</c>, and is the
/// source the autonomous agent reads to decide on the applicant's real submitted data (rather than a
/// blank view). Returns only fields disclosed to the calling participant under the register's DAD
/// disclosure model (FR-006 / FR-010).
/// </summary>
public static class WorkflowDisclosureEndpoints
{
    /// <summary>Maps the workflow disclosed-data endpoints to the application.</summary>
    public static IEndpointRouteBuilder MapWorkflowDisclosureEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/workflows")
            .WithTags("Workflows")
            .RequireAuthorization();

        group.MapGet("/{instanceId}/actions/{actionId:int}/disclosures", (
            HttpContext httpContext,
            string instanceId,
            int actionId,
            IActionDisclosureResolver resolver,
            IWalletServiceClient walletClient,
            ILogger<WorkflowDisclosureEndpointsLogCategory> logger) =>
                GetDisclosuresAsync(httpContext, instanceId, actionId, resolver, walletClient, logger))
        .WithName("GetActionDisclosures")
        .WithSummary("Get prior-action data disclosed to the calling participant for an action")
        .WithDescription("Returns the prior-action payload of a workflow instance that the register has "
            + "disclosed to the calling participant, for the given action being decided. Only fields "
            + "disclosed to the caller's participant are returned — the disclosure model is never widened. "
            + "The caller's wallet(s) are resolved from the `wallet_address` JWT claim when present, and via "
            + "a live Wallet Service lookup keyed by the caller's identity when the claim is absent "
            + "(consumer/service-tier tokens omit the wallet binding under Feature 136). Include the "
            + "`X-Delegation-Token` header to enable decryption on encrypted (non dev-mode) registers. "
            + "`recipientResolved` is false with an empty view when the caller is not a disclosure "
            + "recipient — the autonomous agent treats that as a fail-closed hold signal.");

        group.MapGet("/{instanceId}/disclosures", async (
            HttpContext httpContext,
            string instanceId,
            IActionDisclosureResolver resolver,
            IWalletServiceClient walletClient,
            IInstanceStore instanceStore,
            ILogger<WorkflowDisclosureEndpointsLogCategory> logger) =>
        {
            // Instance-wide variant (the client's action-less overload): anchor the disclosed-data
            // reconstruction on the instance's current action so the caller sees the accumulated
            // prior-action data disclosed to them up to the current point.
            var instance = await instanceStore.GetAsync(instanceId, httpContext.RequestAborted);
            var actionId = instance?.CurrentActionIds.FirstOrDefault() ?? 0;
            return await GetDisclosuresAsync(httpContext, instanceId, actionId, resolver, walletClient, logger);
        })
        .WithName("GetInstanceDisclosures")
        .WithSummary("Get prior-action data disclosed to the calling participant for a workflow instance")
        .WithDescription("Instance-wide form of the disclosed-data query, anchored on the instance's current "
            + "action. See GetActionDisclosures for the disclosure semantics and caller-wallet resolution.");

        return routes;
    }

    /// <summary>
    /// Resolves the caller's wallet(s), then returns the prior-action data disclosed to the caller for
    /// <paramref name="actionId"/>. Always returns 200 with a <see cref="DisclosedActionData"/> — an
    /// unresolved recipient is expressed as <c>recipientResolved=false</c> with an empty view (so the
    /// caller can distinguish "no disclosure" from an auth failure), never a 403.
    /// </summary>
    internal static async Task<IResult> GetDisclosuresAsync(
        HttpContext httpContext,
        string instanceId,
        int actionId,
        IActionDisclosureResolver resolver,
        IWalletServiceClient walletClient,
        ILogger logger)
    {
        var callerWallets = await ActionEndpoints.ResolveUserWalletAddressesAsync(
            httpContext, walletClient, logger, httpContext.RequestAborted);

        if (callerWallets.Count == 0)
        {
            return Results.Ok(new DisclosedActionData
            {
                InstanceId = instanceId,
                ActionId = actionId,
                RegisterId = string.Empty,
                RecipientResolved = false,
            });
        }

        // X-Delegation-Token (when supplied) authorises unwrapping the caller's disclosure-group keys on
        // encrypted registers; dev-mode registers read plaintext and do not require it.
        var delegationToken = httpContext.GetDelegationToken();

        var data = await resolver.ResolveDisclosedDataAsync(
            instanceId, actionId, callerWallets, delegationToken, httpContext.RequestAborted);

        return Results.Ok(data);
    }

    /// <summary>
    /// Marker type for <see cref="ILogger{T}"/> categorisation, mirroring
    /// <c>ActionEndpoints.ActionEndpointsLogCategory</c> so the log category is stable and obvious.
    /// </summary>
    internal sealed class WorkflowDisclosureEndpointsLogCategory { }
}
