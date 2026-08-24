// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Issue #1182 — the three READ endpoints on the <c>/api/instances</c> group, lifted out of
/// <c>Program.cs</c>'s inline lambdas so they can carry a participant gate and be tested without a
/// <c>WebApplicationFactory</c> (the reflection-based pattern
/// <c>InstanceActionEndpointsTests</c> established).
///
/// <para>All three previously returned instance content to ANY authenticated caller. The group's
/// <c>CanExecuteBlueprints</c> policy resolves to a bare <c>RequireAuthenticatedUser()</c>, so the
/// only thing between a stranger and another citizen's in-flight application was knowledge of a
/// GUID — which appears in URLs, logs, inbox <c>detailHref</c> values and client-side state.
/// <c>GET /{instanceId}</c> was the worst of the three: it returned the <c>Instance</c> verbatim,
/// including <c>AccumulatedData</c> (on an identity workflow: name, date of birth, address and
/// portrait image tokens in plaintext) and <c>ParticipantWallets</c> (which de-anonymises every
/// participant).</para>
///
/// <para>The gate itself lives in <see cref="InstanceParticipantGate"/> — see that type for the two
/// traps (consumer-tier tokens carry no <c>wallet_address</c>; open participants are not yet
/// participants) that make the obvious implementation of this check wrong in both directions.</para>
/// </summary>
public static class InstanceReadEndpoints
{
    /// <summary>
    /// Maps the three instance read routes onto the group it is called on (the existing
    /// <c>/api/instances</c> group in <c>Program.cs</c>, already gated by <c>CanExecuteBlueprints</c>).
    /// </summary>
    public static void MapInstanceReadEndpoints(this IEndpointRouteBuilder group)
    {
        group.MapGet("/", ListInstances)
            .WithName("ListInstances")
            .WithSummary("List workflow instances for the authenticated user")
            .WithDescription(
                "Returns paginated workflow instances where a wallet the authenticated caller "
                + "controls is a participant. The caller's wallet(s) are resolved from the "
                + "wallet_address claim when present, else via a Wallet-Service lookup by owner "
                + "(consumer-tier tokens carry no wallet_address per Feature 136). Results span every "
                + "wallet the caller controls, deduplicated by instance and ordered newest-first. "
                + "Supports optional status filtering (Active, Completed, Rejected, TimedOut, Cancelled).")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/{instanceId}", GetInstance)
            .WithName("GetInstance")
            .WithSummary("Get workflow instance")
            .WithDescription(
                "Retrieve a workflow instance by its ID. Readable only by a caller controlling a "
                + "wallet recorded as a participant on the instance — the caller's wallet(s) are "
                + "resolved from the wallet_address claim when present, else via a Wallet-Service "
                + "lookup by owner (consumer-tier tokens carry no wallet_address per Feature 136). "
                + "An instance that has completed no actions and is still awaiting its open "
                + "(Feature 103) starting participant is readable by any authenticated caller, "
                + "because it holds no accumulated data and only the participant wallets the "
                + "published blueprint already carries in the clear. 403 otherwise; 404 if the "
                + "instance does not exist.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{instanceId}/definition", GetInstanceDefinition)
            .WithName("GetInstanceDefinition")
            .WithSummary("Get the blueprint definition this instance is pinned to")
            .WithDescription(
                "Feature 194. Reports which executable definition the instance is running and "
                + "whether that is still the blueprint's latest — the question an operator has to "
                + "answer when an application behaves differently from a newer one on the same "
                + "blueprint. Three states are distinguishable and none is guessed: pinned and "
                + "resolvable (hash + version + isPinnedToLatest); pinned but UNRESOLVABLE on this "
                + "node (hash present, version and isPinnedToLatest null — the stuck-instance state); "
                + "and unpinned, meaning the instance predates the feature (hash null). Same "
                + "participant gate as GET /{instanceId}.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{instanceId}/state", GetInstanceState)
            .WithName("GetInstanceState")
            .WithSummary("Get accumulated state")
            .WithDescription(
                "Get the accumulated state from all prior actions in the workflow. Requires an "
                + "X-Delegation-Token header, and requires the caller to control a wallet recorded "
                + "as a participant on the instance.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{instanceId}/next-actions", GetNextActions)
            .WithName("GetNextActions")
            .WithSummary("Get next available actions")
            .WithDescription(
                "Get the actions currently awaiting execution on the workflow instance. Requires "
                + "the caller to control a wallet recorded as a participant on the instance.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Handler for <c>GET /api/instances/</c> — the caller's own instances. Internal for the same
    /// reason as <see cref="GetInstance"/>.
    /// </summary>
    internal static async Task<IResult> ListInstances(
        HttpContext httpContext,
        IInstanceStore instanceStore,
        IWalletServiceClient walletClient,
        ILogger<InstanceReadEndpointsLogCategory> logger,
        Sorcha.Blueprint.Service.Models.InstanceState? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Issue #1182 (adjacent) — this previously read the wallet_address claim directly and
        // returned an empty page when it was absent. A consumer-tier token never carries that claim
        // (Feature 136), so EVERY citizen saw "no applications" while the server held their data.
        // Same missing resolver as the gate bug on the sibling endpoints, opposite symptom: that one
        // let strangers in, this one locked the owner out.
        var callerWallets = await ParticipantWalletResolver.ResolveUserWalletAddressesAsync(
            httpContext, walletClient, logger, cancellationToken);

        if (callerWallets.Count == 0)
        {
            return Results.Ok(new { items = Array.Empty<object>(), totalCount = 0, pageNumber = page, pageSize });
        }

        // A caller may control several wallets, so the page spans all of them. Each is fetched
        // unpaged and combined here — the previous implementation already made exactly this unpaged
        // call per request to compute totalCount, so this costs no extra round trips. Deduplicated by
        // instance id (a caller holding two participant wallets on one instance must not see it
        // twice) and ordered newest-first with an id tiebreak so paging is deterministic across
        // requests; the store interface promises no ordering of its own.
        var combined = new Dictionary<string, Sorcha.Blueprint.Service.Models.Instance>(StringComparer.Ordinal);
        foreach (var wallet in callerWallets)
        {
            foreach (var instance in await instanceStore.GetByParticipantWalletAsync(
                         wallet, status, 0, int.MaxValue, cancellationToken))
            {
                combined[instance.Id] = instance;
            }
        }

        var ordered = combined.Values
            .OrderByDescending(i => i.CreatedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Results.Ok(new { items, totalCount = ordered.Count, pageNumber = page, pageSize });
    }

    /// <summary>
    /// Handler for <c>GET /api/instances/{instanceId}</c>. Internal (not private) so tests can reach
    /// it by reflection — see <c>tests/Sorcha.Blueprint.Service.Tests/Endpoints/InstanceReadEndpointsTests.cs</c>.
    /// </summary>
    internal static async Task<IResult> GetInstance(
        HttpContext httpContext,
        string instanceId,
        IInstanceStore instanceStore,
        IBlueprintStore blueprintStore,
        IWalletServiceClient walletClient,
        ILogger<InstanceReadEndpointsLogCategory> logger,
        CancellationToken cancellationToken)
    {
        var instance = await instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance == null)
        {
            return Results.NotFound(new { error = "Instance not found" });
        }

        var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);
        if (!await IsPermittedAsync(httpContext, instance, blueprint, walletClient, logger, cancellationToken))
        {
            return Forbidden();
        }

        return Results.Ok(instance);
    }

    /// <summary>
    /// Feature 194 — reports the blueprint definition this instance is pinned to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this feature fixes was entirely invisible from outside: a republish silently moved
    /// every in-flight instance onto new rules. A pin that is correct but unreportable would leave
    /// the next investigation exactly as blind, so this endpoint is part of the feature rather than
    /// a follow-up.
    /// </para>
    /// <para>
    /// <b>Nothing here is guessed.</b> When the pin cannot be resolved on this node, the version
    /// label and <c>isPinnedToLatest</c> come back null rather than falling back to the latest
    /// definition's values — that state IS the diagnosis (a definition that failed to replicate, or
    /// was evicted), and substituting a plausible answer would hide it.
    /// </para>
    /// </remarks>
    internal static async Task<IResult> GetInstanceDefinition(
        HttpContext httpContext,
        string instanceId,
        IInstanceStore instanceStore,
        IBlueprintStore blueprintStore,
        IPublishedBlueprintStore publishedStore,
        IWalletServiceClient walletClient,
        ILogger<InstanceReadEndpointsLogCategory> logger,
        CancellationToken cancellationToken)
    {
        var instance = await instanceStore.GetAsync(instanceId, cancellationToken);
        if (instance == null)
        {
            return Results.NotFound(new { error = "Instance not found" });
        }

        var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);
        if (!await IsPermittedAsync(httpContext, instance, blueprint, walletClient, logger, cancellationToken))
        {
            return Forbidden();
        }

        var pin = instance.BlueprintDefinitionTxId;

        if (string.IsNullOrWhiteSpace(pin))
        {
            // Unpinned: this instance's transactions predate Feature 194. Deliberately
            // distinguishable from "pinned but unresolvable" — they need different responses from an
            // operator, and collapsing them would hide the one failure mode worth seeing.
            return Results.Ok(new
            {
                instanceId,
                blueprintId = instance.BlueprintId,
                blueprintDefinitionTxId = (string?)null,
                blueprintVersion = (int?)null,
                isPinnedToLatest = (bool?)null,
                pinState = "unpinned",
            });
        }

        var pinned = await publishedStore.GetByExecDefHashAsync(instance.BlueprintId, pin);
        if (pinned is null)
        {
            logger.LogWarning(
                "Instance {InstanceId} is pinned to definition {ExecDefHash} of blueprint {BlueprintId}, "
                + "which cannot be resolved on this node — the instance cannot advance here.",
                instanceId, pin, instance.BlueprintId);

            return Results.Ok(new
            {
                instanceId,
                blueprintId = instance.BlueprintId,
                blueprintDefinitionTxId = pin,
                blueprintVersion = (int?)null,
                isPinnedToLatest = (bool?)null,
                pinState = "unresolvable",
            });
        }

        var latest = PublishedBlueprintSelector
            .SelectLatest(await publishedStore.GetVersionsAsync(instance.BlueprintId))?.ExecDefHash;

        return Results.Ok(new
        {
            instanceId,
            blueprintId = instance.BlueprintId,
            blueprintDefinitionTxId = pin,
            // Derived from the pin, never from the stored column — FR-019: the label and the pin
            // cannot disagree if only one of them is a source.
            blueprintVersion = (int?)pinned.Version,
            isPinnedToLatest = latest is null ? (bool?)null : string.Equals(latest, pin, StringComparison.Ordinal),
            pinState = "pinned",
        });
    }

    /// <summary>
    /// The read gate shared by all three handlers: a caller may read an instance if they control a
    /// wallet recorded as a participant on it, OR the instance is still an untouched shell awaiting
    /// its open (Feature 103) starting participant. See <see cref="InstanceParticipantGate"/> for why
    /// both arms are load-bearing.
    /// </summary>
    private static async Task<bool> IsPermittedAsync(
        HttpContext httpContext,
        Sorcha.Blueprint.Service.Models.Instance instance,
        Sorcha.Blueprint.Models.Blueprint? blueprint,
        IWalletServiceClient walletClient,
        ILogger logger,
        CancellationToken cancellationToken)
        => await InstanceParticipantGate.IsParticipantAsync(
               httpContext, instance, walletClient, logger, cancellationToken)
           || InstanceParticipantGate.IsAwaitingOpenParticipant(instance, blueprint);

    /// <summary>
    /// Deliberately identical for "not a participant" and "blueprint/action does not exist", so a
    /// non-participant cannot probe instance internals by reading the difference between error
    /// bodies (#1183).
    /// </summary>
    private static IResult Forbidden() => Results.Problem(
        "You are not a participant on this instance.",
        statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Handler for <c>GET /api/instances/{instanceId}/state</c>. Internal for the same reason as
    /// <see cref="GetInstance"/>.
    /// </summary>
    internal static async Task<IResult> GetInstanceState(
        HttpContext httpContext,
        string instanceId,
        IStateReconstructionService stateService,
        IInstanceStore instanceStore,
        IBlueprintStore blueprintStore,
        IWalletServiceClient walletClient,
        ILogger<InstanceReadEndpointsLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get delegation token from context (set by middleware)
            var delegationToken = httpContext.Items["DelegationToken"] as string;
            if (string.IsNullOrEmpty(delegationToken))
            {
                return Results.BadRequest(new { error = "X-Delegation-Token header is required to view state" });
            }

            var instance = await instanceStore.GetAsync(instanceId, cancellationToken);
            if (instance == null)
            {
                return Results.NotFound(new { error = "Instance not found" });
            }

            // Resolved BEFORE the gate decides (the open-participant arm needs it), and the
            // blueprint-missing 400 is deliberately raised AFTER the gate, so a non-participant
            // cannot distinguish "not allowed" from "blueprint not replicated here" (#1183).
            var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);
            if (!await IsPermittedAsync(httpContext, instance, blueprint, walletClient, logger, cancellationToken))
            {
                return Forbidden();
            }

            if (blueprint == null)
            {
                return Results.BadRequest(new { error = "Blueprint not found" });
            }

            // Use the first current action for state reconstruction
            var currentActionId = instance.CurrentActionIds.FirstOrDefault();
            if (currentActionId == 0)
            {
                return Results.Ok(new
                {
                    instanceId,
                    actionCount = 0,
                    previousTransactionId = (string?)null,
                    data = new Dictionary<string, object?>(),
                    branchStates = new Dictionary<string, object>()
                });
            }

            var state = await stateService.ReconstructAsync(
                blueprint,
                instanceId,
                currentActionId,
                instance.RegisterId,
                delegationToken,
                instance.ParticipantWallets,
                cancellationToken);

            return Results.Ok(new
            {
                instanceId,
                actionCount = state.ActionCount,
                previousTransactionId = state.PreviousTransactionId,
                data = state.GetFlattenedData(),
                branchStates = state.BranchStates
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Request failed");
            return Results.Problem("An error occurred processing the request.", statusCode: 400);
        }
    }

    /// <summary>
    /// Handler for <c>GET /api/instances/{instanceId}/next-actions</c>. Internal for the same reason
    /// as <see cref="GetInstance"/>.
    /// </summary>
    internal static async Task<IResult> GetNextActions(
        HttpContext httpContext,
        string instanceId,
        IInstanceStore instanceStore,
        IBlueprintStore blueprintStore,
        IWalletServiceClient walletClient,
        ILogger<InstanceReadEndpointsLogCategory> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var instance = await instanceStore.GetAsync(instanceId, cancellationToken);
            if (instance == null)
            {
                return Results.NotFound(new { error = "Instance not found" });
            }

            // Gate ordering as in GetInstanceState — blueprint resolved first (the open-participant
            // arm needs it), blueprint-missing 400 raised only after the caller has been admitted.
            var blueprint = await blueprintStore.GetAsync(instance.BlueprintId);
            if (!await IsPermittedAsync(httpContext, instance, blueprint, walletClient, logger, cancellationToken))
            {
                return Forbidden();
            }

            if (blueprint == null)
            {
                return Results.BadRequest(new { error = "Blueprint not found" });
            }

            var nextActions = new List<object>();
            foreach (var actionId in instance.CurrentActionIds)
            {
                var action = blueprint.Actions.FirstOrDefault(a => a.Id == actionId);
                if (action != null)
                {
                    // Get participant info
                    var participant = action.Participants?.FirstOrDefault();
                    nextActions.Add(new
                    {
                        actionId = action.Id,
                        title = action.Title,
                        description = action.Description,
                        participantId = participant?.Principal,
                        branchId = instance.ActiveBranches
                            .FirstOrDefault(b => b.CurrentActionId == actionId)?.Id,
                        blueprintId = instance.BlueprintId,
                        registerId = instance.RegisterId,
                        blueprintName = blueprint.Title
                    });
                }
            }

            return Results.Ok(new
            {
                instanceId,
                state = instance.State.ToString().ToLowerInvariant(),
                isComplete = instance.State == Sorcha.Blueprint.Service.Models.InstanceState.Completed,
                nextActions
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Request failed");
            return Results.Problem("An error occurred processing the request.", statusCode: 400);
        }
    }

    /// <summary>
    /// Marker type for <see cref="ILogger{T}"/> categorisation — <see cref="InstanceReadEndpoints"/>
    /// is static, so it cannot itself be used as a generic type argument. Mirrors
    /// <c>InstanceActionEndpoints.InstanceActionEndpointsLogCategory</c>.
    /// </summary>
    internal sealed class InstanceReadEndpointsLogCategory { }
}
