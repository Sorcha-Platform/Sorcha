// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Middleware;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Infrastructure;
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

        group.MapGet("/{instanceId}/disclosures", (
            HttpContext httpContext,
            string instanceId,
            IActionDisclosureResolver resolver,
            IWalletServiceClient walletClient,
            IInstanceStore instanceStore,
            IBlueprintStore blueprintStore,
            ILogger<WorkflowDisclosureEndpointsLogCategory> logger) =>
                GetInstanceDisclosuresAsync(
                    httpContext, instanceId, resolver, walletClient, instanceStore, blueprintStore, logger))
        .WithName("GetInstanceDisclosures")
        .WithSummary("Get prior-action data disclosed to the calling participant for a workflow instance")
        .WithDescription("Instance-wide form of the disclosed-data query. For an active instance, anchored "
            + "on every one of its current (possibly parallel) pending actions; for a terminal instance "
            + "(Completed/Rejected/TimedOut/Cancelled), anchored on every action the published blueprint "
            + "defines so disclosures remain readable after completion (#1678). `actionId` is always null "
            + "in the response for this route — see GetActionDisclosures for the per-action form and the "
            + "shared disclosure semantics / caller-wallet resolution.");

        return routes;
    }

    /// <summary>
    /// Handler for the instance-wide <c>GET /{instanceId}/disclosures</c> route (the client's
    /// action-less overload). Anchors the disclosed-data reconstruction on every action the caller
    /// could currently see, rather than a single arbitrary action id (#1678).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An <b>active</b> instance anchors on every entry of <see cref="Instance.CurrentActionIds"/> — a
    /// parallel-branch instance with two pending decisions is never silently narrowed to the first
    /// (the second defect in #1678).
    /// </para>
    /// <para>
    /// A <b>terminal</b> instance (Completed / Rejected / TimedOut / Cancelled) has an EMPTY
    /// <c>CurrentActionIds</c> by definition — there is no pending decision to anchor on. The former
    /// code anchored on the sentinel <c>0</c> in that case, which no blueprint action ever has, so
    /// <see cref="IActionDisclosureResolver.ResolveDisclosedDataAsync"/> took its "action not found"
    /// fail-closed branch and silently returned zero disclosures with HTTP 200 for every completed
    /// instance (#1678). Instead, every action the published blueprint defines is used as an anchor:
    /// each action's own required-prior-action derivation
    /// (<c>IStateReconstructionService.GetRequiredActionIds</c>) is a superset of its ancestors, so the
    /// union across every blueprint action covers every action that could actually have executed on
    /// this instance, regardless of which route it took to get there. An action nobody ever submitted
    /// contributes no data — the union can only ever ADD coverage, never invent it.
    /// </para>
    /// </remarks>
    internal static async Task<IResult> GetInstanceDisclosuresAsync(
        HttpContext httpContext,
        string instanceId,
        IActionDisclosureResolver resolver,
        IWalletServiceClient walletClient,
        IInstanceStore instanceStore,
        IBlueprintStore blueprintStore,
        ILogger logger)
    {
        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
            httpContext, walletClient, logger, httpContext.RequestAborted);

        if (callerWallets.Count == 0)
        {
            return Results.Ok(EmptyInstanceResult(instanceId, string.Empty));
        }

        var instance = await instanceStore.GetAsync(instanceId, httpContext.RequestAborted);
        if (instance is null)
        {
            return Results.Ok(EmptyInstanceResult(instanceId, string.Empty));
        }

        List<int> anchorActionIds;
        if (instance.CurrentActionIds.Count > 0)
        {
            anchorActionIds = instance.CurrentActionIds.Distinct().ToList();
        }
        else
        {
            var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);
            anchorActionIds = blueprint?.Actions?.Select(a => a.Id).Distinct().ToList() ?? [];
        }

        if (anchorActionIds.Count == 0)
        {
            logger.LogWarning(
                "Disclosed-data resolve: instance {InstanceId} (state {State}) has no current action and "
                + "no blueprint-defined action to anchor reconstruction on — returning empty",
                instanceId, instance.State);
            return Results.Ok(EmptyInstanceResult(instanceId, instance.RegisterId));
        }

        var delegationToken = httpContext.GetDelegationToken();

        var mergedEntries = new Dictionary<int, DisclosedActionEntry>();
        var mergedFields = new Dictionary<string, object>(StringComparer.Ordinal);
        var registerId = instance.RegisterId;

        foreach (var anchorActionId in anchorActionIds)
        {
            var data = await resolver.ResolveDisclosedDataAsync(
                instanceId, anchorActionId, callerWallets, delegationToken, httpContext.RequestAborted);

            if (!string.IsNullOrEmpty(data.RegisterId))
            {
                registerId = data.RegisterId;
            }

            foreach (var entry in data.Disclosures)
            {
                // Deterministic per prior-action-id regardless of which anchor surfaced it, so a later
                // anchor overwriting an earlier one's entry for the same prior action is a no-op merge,
                // never a conflict.
                mergedEntries[entry.ActionId] = entry;
            }

            foreach (var (key, value) in data.DisclosedFields)
            {
                mergedFields[key] = value;
            }
        }

        var entries = mergedEntries.Values.OrderBy(e => e.ActionId).ToList();

        return Results.Ok(new DisclosedActionData
        {
            InstanceId = instanceId,
            ActionId = null,
            RegisterId = registerId,
            RecipientResolved = entries.Count > 0,
            Disclosures = entries,
            DisclosedFields = mergedFields,
        });
    }

    /// <summary>
    /// The "nothing could be resolved" instance-wide response shared by every early-exit path
    /// (no caller wallet, unknown instance, no anchor action) — always <c>ActionId = null</c> per the
    /// instance-wide contract, distinguishing it from the per-action route's response.
    /// </summary>
    private static DisclosedActionData EmptyInstanceResult(string instanceId, string registerId) => new()
    {
        InstanceId = instanceId,
        ActionId = null,
        RegisterId = registerId,
        RecipientResolved = false,
    };

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
        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
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
